local Switch = {}
local qualifier = "/PBZCombatAsset/Prototypes/BPL_PBZCombatUtils.BPL_PBZCombatUtils_C:Can Active Switch Attack"
local prefix = "Ability.Action.Hun.Weapon.Switch."

local function valid(value)
    return value ~= nil and value:IsValid()
end

local function matches(ability, target)
    if target.ability_class then
        local class = StaticFindObject(target.ability_class)
        return valid(class) and ability:GetClass():GetAddress() == class:GetAddress()
    end
    local tags = ability:GetPropertyValue("AbilityTags").GameplayTags
    for index = 1, tags:GetArrayNum() do
        local tag = tags[index].TagName:ToString()
        if tag == target.tag or tag:sub(1,#target.tag+1) == target.tag .. "." then return true end
    end
    return false
end

local function qualify(backend, avatar, ability)
    local scope = backend.weapon_switch_scope
    if not scope then return end
    avatar, ability = avatar:get(), ability:get()
    if not valid(avatar) or not valid(ability) or not valid(backend.pawn)
        or avatar:GetAddress() ~= backend.pawn:GetAddress() or not matches(ability,scope.target) then return end
    local class = ability:GetClass():GetAddress()
    local specs = scope.component.ActivatableAbilities.Items
    for index = 1, specs:GetArrayNum() do
        local granted = specs[index].Ability
        if valid(granted) and granted:GetClass():GetAddress() == class then return true end
    end
end

function Switch.with(backend, target, callback)
    if type(target.tag) ~= "string" or target.tag:sub(1,#prefix) ~= prefix then return callback() end
    local component = backend:receiver({receiver="ability_system"})
    if not valid(component) then return false end
    if not backend.weapon_switch_hook then
        local first, second = RegisterHook(qualifier,function(_,avatar,ability)
            local scope = backend.weapon_switch_scope
            if not scope then return end
            local ok, result = pcall(qualify,backend,avatar,ability)
            if not ok then scope.failure = result; return end
            return result
        end)
        backend.weapon_switch_hook = {first,second}
    end
    local previous = backend.weapon_switch_scope
    local scope = {target=target,component=component}
    backend.weapon_switch_scope = scope
    local ok, result = xpcall(callback,debug.traceback)
    backend.weapon_switch_scope = previous
    if not ok then error(result,0) end
    if scope.failure then error(scope.failure,0) end
    return result
end

function Switch.reset(backend)
    backend.weapon_switch_scope = nil
    local hook = backend.weapon_switch_hook
    if hook then
        UnregisterHook(qualifier,hook[1],hook[2])
        backend.weapon_switch_hook = nil
    end
end

return Switch
