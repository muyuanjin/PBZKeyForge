local Restrictions = require("move-restrictions")
local Selection = require("ability-selection")
local activeTarget = {activation_tags={},active_only=true}
local activeMatcher, activeSelection = Selection.matcher(activeTarget)
local Equipment = {}
local prefix = "/Script/SGameCombatSystem.CharacterWeaponsComponent:"
local function valid(object) return object and object:IsValid() end
local function path(object) return object:GetFullName():gsub("^%S+ ","") end
local function config_path(object) return path(object):gsub("%.[^./]+$","") end

local function restore(backend,state)
    if valid(state.component) and valid(state.original) then
        state.context:set_weapon_slot(state.component,state.slot,state.index,state.original)
    end
    if state.armed_original ~= nil and valid(state.component) then
        backend:invoke(state.component,prefix.."SetArmed",{TagName=FName(state.slot)},state.armed_original)
    end
    backend.move_equipment[state.slot] = nil
end

local function running(backend,state)
    for _,request in pairs(backend.snake_preparation and backend.snake_preparation.requests or {}) do
        if request.target.equipment_slot == state.slot then return true end
    end
    for _,pending in pairs(backend.complete_move_pending or {}) do
        if pending.spec.equipment_slot == state.slot then return true end
    end
    for _,held in pairs(backend.native_move_inputs or {}) do
        if held.target.equipment_slot == state.slot then return true end
    end
    local component = backend:receiver({receiver="ability_system"})
    if not valid(component) then return false end
    local active = false
    Selection.each(backend,activeTarget,function(_,ability)
        if state.classes[ability:GetClass():GetAddress()] then active = true end
    end,activeMatcher,activeSelection)
    if active then return true end
    for _,tuning in pairs(backend.resource_costs and backend.resource_costs.instances or {}) do
        if tuning.restriction_target and tuning.restriction_target.equipment_slot == state.slot
            and valid(tuning.instance) and tuning.instance:GetPropertyValue("bIsActive") then return true end
    end
    return false
end

function Equipment.update(backend)
    local states = {}
    for _,state in pairs(backend.move_equipment or {}) do states[#states+1] = state end
    for _,state in ipairs(states) do if not running(backend,state) then restore(backend,state) end end
end

function Equipment.prepare(backend,target,settings)
    if target.kind == "ability" and (target.tag == "Ability.Action.Hun.SpecialDash"
        or target.tag == "Ability.Action.Hun.DeflectDash") then
        local component = backend:invoke(backend.pawn,"/Script/Engine.Actor:GetComponentByClass",
            backend:object("/Script/SGameCombatSystem.CharacterWeaponsComponent"))
        assert(valid(component),"Dodge weapon component is unavailable")
        local slot = {TagName=FName("Weapon.Slot.Main")}
        if not backend:invoke(component,prefix.."IsArmed",slot) then
            backend:invoke(component,prefix.."SetArmed",slot,true)
        end
    end
    local equip = Restrictions.selected(settings,"equipment") and target.equipment_configs and #target.equipment_configs>0
    local armed = Restrictions.selected(settings,"stance") and target.armed_state or nil
    if not equip and not armed then return true end
    local slot = equip and assert(target.equipment_slot,"Move equipment slot is absent") or armed.slot
    if armed then assert(armed.slot==slot,"Weapon and armed-state slots must match") end
    backend.move_equipment = backend.move_equipment or {}
    local previous = backend.move_equipment[slot]
    if previous then
        local same = not equip
        for _,config in ipairs(equip and target.equipment_configs or {}) do
            if config_path(previous.config)==config:gsub("%.[^./]+$","") then same = true end
        end
        if same and (not armed or previous.armed_target==armed.armed) then return true end
        if running(backend,previous) then return false, "Another prepared weapon is still in use: " .. slot end
        restore(backend,previous)
    end
    local component = backend:invoke(backend.pawn,"/Script/Engine.Actor:GetComponentByClass",
        backend:object("/Script/SGameCombatSystem.CharacterWeaponsComponent"))
    assert(valid(component),"Move equipment component is unavailable")
    local tag = {TagName=FName(slot)}
    local index = backend:invoke(component,prefix.."GetCurrentWeaponIndex",tag,false)
    if index<0 then return false, "No current weapon in slot: " .. slot end
    local output = {}
    if not backend:invoke(component,prefix.."GetSlotWeapon",tag,index,output) then
        return false, "Current weapon slot data is unavailable: " .. slot
    end
    local original = output.WeaponConfig
    assert(valid(original),"The current weapon config is unavailable")
    local wanted = original
    local same = not equip
    for _,config in ipairs(equip and target.equipment_configs or {}) do
        if config_path(original)==config:gsub("%.[^./]+$","") then same = true end
    end
    if not same then wanted = backend:object(target.equipment_configs[1]) end
    assert(valid(wanted),"The requested weapon config is unavailable")
    local originalArmed = armed and backend:invoke(component,prefix.."IsArmed",tag)
    local changeArmed = armed and originalArmed~=armed.armed
    if same and not changeArmed then return true end
    local asc = backend:receiver({receiver="ability_system"})
    local before = {}
    local specs = asc.ActivatableAbilities.Items
    for n=1,specs:GetArrayNum() do
        if valid(specs[n].Ability) then
            before[specs[n].Ability:GetClass():GetAddress()] = true
            if specs[n].ActiveCount>0 then
                local tags = specs[n].Ability:GetPropertyValue("AbilityTags").GameplayTags
                for t=1,tags:GetArrayNum() do
                    local name = tags[t].TagName:ToString()
                    if name=="Ability.Action" or name:sub(1,15)=="Ability.Action." then
                        return false, "Weapon preparation is waiting for an active ability: " .. path(specs[n].Ability:GetClass())
                    end
                end
            end
        end
    end
    if not backend.resource_costs then backend.resource_costs = require("resource-costs").new(backend) end
    local state = {slot=slot,index=index,component=component,original=not same and original or nil,config=wanted,
        armed_target=armed and armed.armed,
        context=backend.resource_costs.context,classes={}}
    if changeArmed then state.armed_original = originalArmed end
    backend.move_equipment[slot] = state
    -- Record the restore operation before the native call, including partial failures.
    if not same then state.context:set_weapon_slot(component,slot,index,wanted) end
    if changeArmed then backend:invoke(component,prefix.."SetArmed",tag,armed.armed) end
    specs = asc.ActivatableAbilities.Items
    for n=1,specs:GetArrayNum() do
        if valid(specs[n].Ability) then
            local class = specs[n].Ability:GetClass()
            if not before[class:GetAddress()] or target.ability_class and path(class)==target.ability_class
                or changeArmed then
                local tags = specs[n].Ability:GetPropertyValue("AbilityTags").GameplayTags
                for t=1,tags:GetArrayNum() do
                    if tags[t].TagName:ToString():sub(1,15)=="Ability.Action." then state.classes[class:GetAddress()] = true end
                end
            end
        end
    end
    return true
end

function Equipment.reset(backend)
    local states,failures = {},{}
    for _,state in pairs(backend.move_equipment or {}) do states[#states+1] = state end
    for _,state in ipairs(states) do
        local ok,failure = pcall(restore,backend,state)
        if not ok then failures[#failures+1] = tostring(failure) end
    end
    if #failures>0 then error(table.concat(failures,"; "),0) end
end
return Equipment
