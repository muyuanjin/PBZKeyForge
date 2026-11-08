package.path = "prototype/input-router/ue4ss/Scripts/?.lua;" .. package.path
local Moves = require("complete-moves")
local config = assert(loadfile(os.getenv("PBZ_PHASE_MOVE_CONFIG") or "dist/complete-move-catalog-check/phase-moves.lua"))()
local targets = {}
for _, target in pairs(config.targets) do targets[target.move_id] = target end

function FName(value) return {ToString=function() return value end} end
local classes, address = {}, 100
function StaticFindObject(path)
    if not classes[path] then
        address = address + 1
        local identity = address
        classes[path] = {IsValid=function() return true end, GetAddress=function() return identity end,
            GetFullName=function() return "BlueprintGeneratedClass " .. path end}
    end
    return classes[path]
end
local function object(identity, values)
    values = values or {}
    function values:IsValid() return true end
    function values:GetAddress() return identity end
    function values:GetClass() return StaticFindObject(self.class) end
    function values:GetPropertyValue(name) return self[name] end
    return values
end
local function array(values)
    return setmetatable({GetArrayNum=function() return #values end, ForEach=function(_, callback)
        for index, value in ipairs(values) do callback(index, {get=function() return value end}) end
    end}, {__index=values})
end
local function fixture(target)
    local instance = object(1, {class=target.ability_class, bIsActive=false, ChargeCount=0,
        ["Current Shachi Reinforced"]=true})
    local spec = {Ability=object(2, {class=target.ability_class}), ActiveCount=0,
        NonReplicatedInstances=array({instance}), ReplicatedInstances=array({})}
    local asc = object(3, {ActivatableAbilities={Items=array({spec})}})
    local receiver, input, hold, subsystem = object(4), object(5), object(6), object(7)
    local inputValue = object(13)
    local timeline = object(8)
    local task = object(9, {Timeline=timeline, CurrentTime=0})
    local backend = {pawn=object(10), subsystem=subsystem, starts=0, stops=0, entries=0, activations=0, config=config,
        instance=instance, task=task, tags={}}
    for _, tag in ipairs(target.required_tags) do backend.tags[tag] = 1 end
    function backend:receiver(value) return value.receiver == "ability_system" and asc or receiver end
    function backend:object(path) return path == target.prepare_input and hold or input end
    function backend:library() return object(11) end
    function backend:fail_target(_, message) error(message) end
    backend.resource_costs={context={start_hold_input=function(_,owner,heldAction)
        assert(owner==subsystem and heldAction==hold)
        backend.starts,backend.holding=backend.starts+1,true
    end},activate=function(_,_,_,callback) return callback() end}
    function backend:invoke(value, path, first, second, third, fourth)
        if path:find(":GetGameplayTagCount", 1, true) then return self.tags[first.TagName:ToString()] or 0
        elseif path == "/Script/EnhancedInput.EnhancedInputLibrary:MakeInputActionValueOfType" then
            error("Opaque FInputActionValue must not return through Lua reflection")
        elseif path:find(":StartContinuousInputInjectionForAction", 1, true) then
            error("Continuous native Hold must use the opaque native bridge")
        elseif path:find(":StopContinuousInputInjectionForAction", 1, true) then
            assert(value == subsystem and first == hold)
            self.stops, self.holding = self.stops + 1, false
        elseif path:find(":GetSGameInputComponent", 1, true) then return object(12)
        elseif path:find(":IsHolding", 1, true) then return self.holding
        elseif path:find(":TryActivateExternalActionAbilityByClass", 1, true) then
            assert(value == receiver and first == StaticFindObject(target.ability_class) and second == input and self.holding)
            self.activations = self.activations + 1
            if self.refused then return false end
            spec.ActiveCount, instance.bIsActive = 1, true
            return true
        elseif path == "/Script/LegoAbility.LegoAbility:GetPlayTimelineTask" then return task
        elseif path == "/Script/LegoAbility.LegoAbility:GetTimelineByName" then
            assert(first:ToString() == "Main")
            return timeline
        elseif path == target.entry_point then
            assert(value == instance and instance.bIsActive and not self.holding)
            self.entries, self.selected_charge = self.entries + 1, instance.ChargeCount
        elseif path:find(":K2_CancelAbility", 1, true) then
            self.canceled = true
            spec.ActiveCount, instance.bIsActive = 0, false
        else error("Unexpected weapon charge call: " .. path) end
    end
    return backend
end

for level = 1, 3 do
    local target = assert(targets["move:Ability.Action.Hun.CombatArt.ChiRen.Release" .. level])
    assert(target.activate_by_class and target.entry_point == target.ability_class .. ":ToRelease")
    local backend = fixture(target)
    assert(Moves.dispatch(backend, target, "started"))
    assert(backend.starts == 1 and backend.activations == 0)
    Moves.update(backend)
    assert(backend.activations == 1 and backend.entries == 0 and backend.instance.ChargeCount == 0)
    backend.task.CurrentTime = target.entry_when.minimum_time
    if level > 1 then
        backend.instance.ChargeCount = level - 2
        Moves.update(backend)
        assert(backend.entries == 0 and backend.stops == 0, "Timeline time must not fabricate a native charge")
    end
    backend.instance.ChargeCount = level - 1
    Moves.update(backend)
    assert(backend.entries == 1 and backend.selected_charge == level - 1 and backend.stops == 1
        and not backend.canceled, "Each release must use the normally activated instance's actual charge")
    Moves.reset(backend)
    assert(backend.stops == 1 and backend.instance.bIsActive, "Completed release must retain the native attack lifecycle")
    local reset = fixture(target)
    assert(Moves.dispatch(reset, target, "started"))
    Moves.update(reset)
    Moves.reset(reset)
    assert(reset.canceled and reset.stops == 1 and reset.entries == 0, "Reset must stop preparation and cancel the pending charge")
    local refused = fixture(target)
    refused.refused = true
    assert(Moves.dispatch(refused, target, "started"))
    Moves.update(refused)
    assert(refused.activations == 1 and refused.stops == 1 and refused.entries == 0,
        "Original resource or equipment refusal must stop the virtual hold")
    if level < 3 then
        local missed = fixture(target)
        assert(Moves.dispatch(missed, target, "started"))
        Moves.update(missed)
        missed.task.CurrentTime, missed.instance.ChargeCount = target.entry_when.before_time, level
        Moves.update(missed)
        assert(missed.canceled and missed.stops == 1 and missed.entries == 0,
            "Missing a lower-charge window must not silently select the stronger release")
    end
end
print("CompiledWeaponChargeMoveTests PASS: three real charge releases, native refusal, missed windows and cleanup")
