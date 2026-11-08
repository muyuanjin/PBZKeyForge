local Restrictions = {}
local Selection = require("ability-selection")

local function valid(value) return value ~= nil and value:IsValid() end
local function selected(settings,kind)
    for _,value in ipairs(settings and settings.restriction_bypasses or {}) do if value == kind then return true end end
    return false
end
Restrictions.selected = selected

function Restrictions.category(backend,target,tag)
    for kind,group in pairs(target.restriction_groups or {}) do
        for _,mode in ipairs({"required_tags","ignored_tags"}) do
            for _,name in ipairs(group[mode] or {}) do if name == tag then return kind end end
        end
    end
    return backend.config and backend.config.restriction_tags and backend.config.restriction_tags[tag]
end

function Restrictions.relaxed(backend,target,tag,settings)
    local kind = Restrictions.category(backend,target,tag)
    return kind and selected(settings,kind) or false
end

local function tags(value)
    local result={}
    if value then
        local entries=value.GameplayTags
        for index=1,entries:GetArrayNum() do result[#result+1]=entries[index].TagName:ToString() end
    end
    return result
end
local function container(names)
    local result={GameplayTags={}}
    for _,name in ipairs(names) do result.GameplayTags[#result.GameplayTags+1]={TagName=FName(name)} end
    return result
end

function Restrictions.begin(backend,target,settings)
    if #(settings and settings.restriction_bypasses or {}) == 0 then return {} end
    local component=backend:receiver({receiver="ability_system"})
    assert(valid(component),"Move restriction preparation requires the player's ability system")
    local matches,selection=Selection.matcher(target)
    for _,scope in ipairs(backend.move_restriction_scopes or {}) do
        if scope.settings.restriction_bypasses==settings.restriction_bypasses
            and scope.target.restriction_groups==target.restriction_groups
            and Selection.same(scope.selection,selection) then return {} end
    end
    local changed,seen={},{}
    local function relax(ability)
        if not valid(ability) or seen[ability:GetAddress()] then return end
        seen[ability:GetAddress()]=true
        for _,name in ipairs({"ActivationRequiredTags","ActivationBlockedTags"}) do
            local original=tags(ability:GetPropertyValue(name))
            local retained={}
            for _,tag in ipairs(original) do
                if not Restrictions.relaxed(backend,target,tag,settings) then retained[#retained+1]=tag end
            end
            if #original ~= #retained then
                changed[#changed+1]={object=ability,name=name,value=container(original)}
                ability:SetPropertyValue(name,container(retained))
            end
        end
    end
    local ok,result=pcall(function()
        Selection.each(backend,target,function(spec,ability)
            relax(ability)
            -- GAS uses the primary instance for CanActivateAbility when one exists.
            for _,instances in ipairs({spec.NonReplicatedInstances,spec.ReplicatedInstances}) do
                for slot=1,instances:GetArrayNum() do relax(instances[slot]) end
            end
        end,matches,selection)
    end)
    if not ok then
        Restrictions.finish(changed)
        error(result,0)
    end
    return changed
end

function Restrictions.finish(changed)
    local failures={}
    for index=#(changed or {}),1,-1 do
        local item=changed[index]
        local restored,failure=pcall(item.object.SetPropertyValue,item.object,item.name,item.value)
        if not restored then failures[#failures+1]=tostring(failure) end
    end
    if #failures > 0 then error(table.concat(failures,"; "),0) end
end

function Restrictions.with(backend,target,settings,callback)
    if #(settings and settings.restriction_bypasses or {})==0 then return callback() end
    local changed=Restrictions.begin(backend,target,settings)
    local scopes=backend.move_restriction_scopes or {}
    backend.move_restriction_scopes=scopes
    local _,selection=Selection.matcher(target)
    scopes[#scopes+1]={target=target,settings=settings,selection=selection}
    local ok,result=pcall(callback)
    scopes[#scopes]=nil
    if #scopes==0 then backend.move_restriction_scopes=nil end
    local restored,failure=pcall(Restrictions.finish,changed)
    if not ok then error(tostring(result)..(restored and "" or "; "..tostring(failure)),0) end
    if not restored then error(failure,0) end
    return result
end

function Restrictions.protect(backend,state)
    if not state.restriction_target or #(state.restriction_bypasses or {}) == 0 then return end
    local target={}
    for key,value in pairs(state.restriction_target) do target[key]=value end
    target.restriction_groups={}
    for kind,group in pairs(state.restriction_target and state.restriction_target.restriction_groups or {}) do
        target.restriction_groups[kind]={required_tags={},ignored_tags={}}
        for _,mode in ipairs({"required_tags","ignored_tags"}) do
            for _,tag in ipairs(group[mode] or {}) do target.restriction_groups[kind][mode][#target.restriction_groups[kind][mode]+1]=tag end
        end
    end
    for property,mode in pairs({ActivationRequiredTags="required_tags",ActivationBlockedTags="ignored_tags"}) do
        for _,tag in ipairs(tags(state.instance:GetPropertyValue(property))) do
            local kind=Restrictions.category(backend,target,tag)
            if kind and selected(state,kind) then
                target.restriction_groups[kind]=target.restriction_groups[kind] or {required_tags={},ignored_tags={}}
                local list=target.restriction_groups[kind][mode]
                list[#list+1]=tag
            end
        end
    end
    state.restriction_target=target
end

function Restrictions.active(state)
    return state and (state.resource_tags or state.restriction_target and #(state.restriction_bypasses or {})>0)
end

function Restrictions.override(state,name)
    if state and state.resource_tags and state.resource_tags[name] then
        local value=state.resource_tags[name]
        if type(value)=="function" then return value() end
        return value
    end
    if not Restrictions.active(state) then return end
    for kind,group in pairs(state.restriction_target and state.restriction_target.restriction_groups or {}) do
        if selected(state,kind) then
            for _,tag in ipairs(group.ignored_tags or {}) do if tag==name then return 0 end end
            for _,tag in ipairs(group.required_tags or {}) do if tag==name then return 1 end end
        end
    end
end

function Restrictions.query(backend,state,name,original)
    local value=Restrictions.override(state,name)
    if value==0 then return 0 end
    return value==1 and math.max(1,original) or original
end

return Restrictions
