local Restrictions = require("move-restrictions")
local Selection = require("ability-selection")
local Snake = {}
local root = "/PBZCombatAsset/ActorData/Players/Hun/Abilities/SnakeDual/"
local throwClass = root .. "Attack/GA_Hun_SnakeDual_DB.GA_Hun_SnakeDual_DB_C"
local passiveClass = root .. "Passive/GA_Hun_SnakeDual_Passive.GA_Hun_SnakeDual_Passive_C"
local function valid(value) return value and value:IsValid() end
local function instance(backend,path)
    local found,active
    Selection.each(backend,{ability_class=path},function(spec,cdo)
        for _,values in ipairs({spec.NonReplicatedInstances,spec.ReplicatedInstances}) do
            for index=1,values:GetArrayNum() do
                local value=values[index]
                if valid(value) and value:GetAddress()~=cdo:GetAddress() then
                    assert(not found or found:GetAddress()==value:GetAddress(),"Snake preparation ability instance is ambiguous: "..path)
                    found,active=value,spec.ActiveCount>0
                end
            end
        end
    end)
    return found,active
end
local function projectile(backend,passive)
    if not valid(passive) or not passive:GetPropertyValue("bIsActive") then return false end
    -- UE4SS exposes the object pointer held by this native script interface.
    if not valid(passive:GetPropertyValue("SnakeDual Bullet")) then return false end
    local asc=backend:receiver({receiver="ability_system"})
    return valid(asc) and backend:invoke(asc,"/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayTagCount",
        {TagName=FName("CharacterState.AbilityState.SnakeDual.HasProjectile")})>0
end
local function normal(backend,passive)
    if not valid(passive) or not passive:GetPropertyValue("bIsActive") then return false end
    local asc=backend:receiver({receiver="ability_system"})
    return valid(asc) and backend:invoke(asc,"/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayTagCount",
        {TagName=FName("CharacterState.AbilityState.SnakeDual.Normal")})>0
end
local function identity(target,binding)
    return tostring(binding or target).."|"..tostring(target.move_id or target.ability_class)
end
local function cleanup(backend,state)
    if state.owned and valid(state.instance) and state.instance:GetPropertyValue("bIsActive") then
        backend:invoke(state.instance,"/Script/GameplayAbilities.GameplayAbility:K2_CancelAbility")
    end
    if state.projectile_owned and not state.committed and valid(state.passive) then
        backend:invoke_configured(state.passive,passiveClass..":Force Cancel Projectile")
    end
    if backend.snake_preparation==state then backend.snake_preparation=nil end
end
local function once(settings)
    local trigger=settings and settings.trigger or "down"
    return trigger=="press" and not (settings and settings.default_trigger)
        or trigger=="tap" or trigger=="release" or trigger=="pulse" or trigger=="multi_tap" or trigger=="sequence"
end
function Snake.prepare(backend,target,phase,value,binding,bypass,settings)
    local state=backend.snake_preparation
    local key=identity(target,binding)
    local request=state and state.requests[key]
    if phase=="canceled" or phase=="completed" then
        if request and (phase=="canceled" or not request.once) then
            state.requests[key]=nil
            if not next(state.requests) then cleanup(backend,state) end
        end
        return nil
    end
    if not (target.snake_projectile or target.snake_normal) or not Restrictions.selected(settings,"predecessor")
        or phase~="started" and phase~="triggered" then return nil end
    local mode=target.snake_normal and "normal" or "projectile"
    local passive=state and state.passive or instance(backend,passiveClass)
    if state and state.dispatching then return nil end
    if not state and (mode=="normal" and normal(backend,passive) or mode=="projectile" and projectile(backend,passive)) then return nil end
    request={target=target,phase=phase,value=value,binding=binding,bypass=bypass,settings=settings,once=once(settings)}
    if state then
        if state.mode~=mode then return false,"Another native Snake weapon state is being prepared" end
        state.requests[key]=request;return true
    end
    if not valid(passive) or not passive:GetPropertyValue("bIsActive") then
        return false,"Snake projectile preparation requires the equipped weapon's active passive ability"
    end
    if mode=="normal" and not projectile(backend,passive) then
        return false,"Snake normal weapon preparation requires a native projectile to recall"
    end
    local ability,active=instance(backend,throwClass)
    if not valid(ability) then return false,"Snake throw ability instance is not granted" end
    state={passive=passive,instance=ability,requests={[key]=request},owned=not active,
        projectile_owned=not active and mode=="projectile",mode=mode}
    backend.snake_preparation=state
    if not active then
        local throw={kind="move",move_id="move:Weapon.Weapon_Hun_SnakeDual.GA_Hun_SnakeDual_DB",
            tag="Ability.Action.Hun.Heavy.SnakeDual.DB",ability_class=throwClass,activate_by_class=true,priority=-4,
            input_action="/PBZInput/InputActions/IA_AttackH_Press.IA_AttackH_Press",
            equipment_configs=target.equipment_configs,equipment_slot=target.equipment_slot,armed_state=target.armed_state}
        local ok,accepted,reason=backend:dispatch(throw,"triggered",1,tostring(binding or key)..":snake-throw",bypass,settings)
        if not ok or accepted==false or not ability:GetPropertyValue("bIsActive") then
            cleanup(backend,state)
            return false,reason or "native Snake throw activation refused"
        end
    end
    if backend.report and backend.log then
        backend:report("snake-projectile:"..key,"Waiting for native Snake "..(mode=="normal" and "weapon recall: " or "projectile: ")
            ..tostring(target.move_id or target.ability_class))
    end
    return true
end
local function update(backend,state)
    local active=valid(state.instance) and state.instance:GetPropertyValue("bIsActive")
    local ready=state.mode=="normal" and normal(backend,state.passive) or state.mode=="projectile" and projectile(backend,state.passive)
    if not ready then
        if not active then
            if state.mode=="normal" and valid(state.passive)
                and (projectile(backend,state.passive) or valid(state.passive:GetPropertyValue("Revive Bullet"))) then return end
            for _,request in pairs(state.requests) do
                if backend.move_rejected then backend:move_rejected(request.target,request.binding,state.mode=="normal"
                    and "native Snake recall ended without normal weapon state" or "native Snake throw ended without a projectile") end
            end
            cleanup(backend,state)
        end
        return
    end
    if active then
        local phase=state.instance:GetPropertyValue("Action Phase")
        if not phase or phase.TagName:ToString()~="CharacterState.Action.Phase.Cancel" then return end
    end
    state.dispatching=true
    for key,request in pairs(state.requests) do
        local ok,accepted=backend:dispatch(request.target,request.phase,request.value,request.binding,request.bypass,request.settings)
        if ok and accepted~=false then state.committed=true end
        state.requests[key]=nil
    end
    state.dispatching=nil
    if not state.committed then cleanup(backend,state)
    elseif backend.snake_preparation==state then backend.snake_preparation=nil end
end
function Snake.update(backend)
    local state=backend.snake_preparation
    if not state then return end
    local ok,failure=xpcall(update,debug.traceback,backend,state)
    if not ok then
        for _,request in pairs(state.requests) do backend:fail_target(request.target,failure,request.binding) end
        cleanup(backend,state)
    end
end
function Snake.abort(backend,target,binding)
    local state=backend.snake_preparation
    if not state then return end
    for key,request in pairs(state.requests) do
        if request.target==target and (not binding or request.binding==binding) then state.requests[key]=nil end
    end
    if not next(state.requests) then cleanup(backend,state) end
end
function Snake.reset(backend)
    local state=backend.snake_preparation
    if state then cleanup(backend,state) end
end
return Snake
