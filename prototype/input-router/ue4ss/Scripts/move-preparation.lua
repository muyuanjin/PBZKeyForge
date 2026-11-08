local Restrictions = require("move-restrictions")
local Selection = require("ability-selection")
local Preparation = {}
local prefix = "/Script/GameplayAbilities.AbilitySystemComponent:"
local function valid(object) return object and object:IsValid() end
local function has(backend,component,tag)
    return backend:invoke(component,prefix.."GetGameplayTagCount",{TagName=FName(tag)}) > 0
end

local function requirement_scopes(backend,effect)
    if not effect.requirementScopes then return nil end
    local scopes = {}
    local componentClass = backend:object("/Script/GameplayAbilities.TargetTagRequirementsGameplayEffectComponent")
    for _,scope in ipairs(effect.requirementScopes) do
        local cdo = backend:object(scope.effectClass):GetCDO()
        assert(valid(cdo),"Native effect default object is unavailable: "..scope.effectClass)
        local components,found = cdo:GetPropertyValue("GEComponents"),false
        for index=1,components:GetArrayNum() do
            local component = components[index]
            if valid(component) and component:IsA(componentClass) then
                scopes[#scopes+1] = {object=component,application=scope.application}
                found = true
            end
        end
        assert(found,"Native effect tag requirements are unavailable: "..scope.effectClass)
    end
    return scopes
end

local function running(backend,target)
    for _,request in pairs(backend.snake_preparation and backend.snake_preparation.requests or {}) do
        if request.target == target then return true end
    end
    for _,pending in pairs(backend.complete_move_pending or {}) do
        if pending.spec == target then return true end
    end
    for _,held in pairs(backend.native_move_inputs or {}) do
        if held.target == target then return true end
    end
    local active = false
    Selection.each(backend,target,function(spec) if spec.ActiveCount > 0 then active = true end end)
    if active then return true end
    for _,tuning in pairs(backend.resource_costs and backend.resource_costs.instances or {}) do
        if tuning.restriction_target == target and valid(tuning.instance)
            and tuning.instance:GetPropertyValue("bIsActive") then return true end
    end
    return false
end

local function remove(backend,state)
    if valid(state.component) then
        if state.handle ~= nil then
            state.context:remove_effect(state.component,state.handle)
            state.handle = nil
        end
        if state.long_buffs then
            state.context:restore_long_buffs(state.component,state.long_buffs)
            state.long_buffs = nil
        end
    elseif state.long_buffs then
        state.context:discard_long_buffs(state.long_buffs)
        state.long_buffs = nil
    end
    backend.move_preparation[state.key] = nil
end

local function begin(backend,target,settings,bindingId,scope)
    for _,effect in ipairs(target.native_state_effects or {}) do
        local selected = true
        for _,kind in ipairs(effect.requiredBypassKinds) do
            if not Restrictions.selected(settings,kind) then selected = false; break end
        end
        if selected then
            local component = backend:receiver({receiver="ability_system"})
            assert(valid(component),"Native move preparation requires the player's ability system")
            if not effect.equipmentTag or has(backend,component,effect.equipmentTag) then
                backend.move_preparation = backend.move_preparation or {}
                local key = tostring(component:GetAddress())..":"..effect.effectClass
                local state = backend.move_preparation[key]
                if state and state.handle == nil then remove(backend,state); state = nil end
                if state or not has(backend,component,effect.qualificationTag) then
                    if not state then
                        if not backend.resource_costs then backend.resource_costs = require("resource-costs").new(backend) end
                        local context = backend.resource_costs.context
                        state = {key=key,component=component,context=context,targets={}}
                        backend.move_preparation[key] = state
                        scope[#scope+1] = {state=state,target=target,owner=bindingId or target,added=true}
                        if effect.replacedEffectClasses then
                            local definitions = {}
                            for _,path in ipairs(effect.replacedEffectClasses) do
                                -- An active effect's class is loaded with its definition.
                                local class = StaticFindObject(path)
                                if valid(class) then definitions[#definitions+1] = class:GetCDO() end
                            end
                            state.long_buffs = context:capture_long_buffs(component,definitions)
                            context:remove_long_buffs(component,state.long_buffs)
                        end
                        state.handle = context:apply_effect_self(component,backend:object(effect.effectClass),1,
                            requirement_scopes(backend,effect))
                        -- Keep native ownership until both removal and resource restoration succeed.
                        if not has(backend,component,effect.qualificationTag) then
                            Preparation.finish(backend,scope,false)
                            return nil,"Native state effect did not establish "..effect.qualificationTag
                        end
                    end
                    local owners = state.targets[target]
                    if not owners then owners = {}; state.targets[target] = owners end
                    local owner = bindingId or target
                    if not owners[owner] then
                        owners[owner] = true
                        local last = scope[#scope]
                        if not last or last.state ~= state or last.target ~= target then
                            scope[#scope+1] = {state=state,target=target,owner=owner,added=true}
                        end
                    end
                end
            end
        end
    end
    return scope
end

function Preparation.begin(backend,target,settings,bindingId)
    local scope = {}
    local ok,result,reason = pcall(begin,backend,target,settings,bindingId,scope)
    if not ok then
        local cleaned,failure = pcall(Preparation.finish,backend,scope,false)
        error(tostring(result)..(cleaned and "" or "\n"..tostring(failure)),0)
    end
    return result,reason
end

function Preparation.finish(backend,scope,accepted)
    if accepted then return end
    for index=#(scope or {}),1,-1 do
        local item = scope[index]
        local state = item.state
        if item.added then
            local owners = state.targets[item.target]
            if owners then
                owners[item.owner] = nil
                if not next(owners) then state.targets[item.target] = nil end
            end
            if not next(state.targets) then remove(backend,state) end
        end
    end
end

function Preparation.update(backend)
    for _,state in pairs(backend.move_preparation or {}) do
        for target in pairs(state.targets) do
            if not running(backend,target) then state.targets[target] = nil end
        end
        if not next(state.targets) then remove(backend,state) end
    end
end

function Preparation.reset(backend)
    local failures = {}
    for _,state in pairs(backend.move_preparation or {}) do
        local ok,failure = pcall(remove,backend,state)
        if not ok then failures[#failures+1] = tostring(failure) end
    end
    if #failures > 0 then error(table.concat(failures,"; "),0) end
end

return Preparation
