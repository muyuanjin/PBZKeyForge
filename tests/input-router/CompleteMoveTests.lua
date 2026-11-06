local scripts = "prototype/input-router/ue4ss/Scripts/"
package.path = scripts .. "?.lua;" .. package.path
local Moves = require("complete-moves")
local Backend = require("backend")
local checks = 0
local function check(value, message)
    assert(value, message)
    checks = checks + 1
end
function FName(value) return {ToString = function() return value end} end
local function array(items)
    return {ForEach = function(_, callback)
        for index, item in ipairs(items) do callback(index, {get = function() return item end}) end
    end}
end
local class = "/PBZCombatAsset/ActorData/Players/Hun/Abilities/BronzeSword/GA_Hun_BronzeSword_B_Hold.GA_Hun_BronzeSword_B_Hold_C"
local move = {kind="move", tag="Ability.Action.Hun.Heavy.BronzeSword.BHold", ability_class=class,
    entry_point=class .. ":EndSuccess", input_action="/PBZInput/InputActions/IA_AttackH_PressLong.IA_AttackH_PressLong"}
local function object(address, properties)
    local result = properties or {}
    function result:IsValid() return self.valid ~= false end
    function result:GetAddress() return address end
    function result:GetClass() return {GetFullName = function() return "BlueprintGeneratedClass " .. (self.class or class) end} end
    function result:GetPropertyValue(name) return self[name] end
    return result
end
local function fixture()
    local cdo, instance = object(1), object(2, {bIsActive=false})
    local spec = {Ability=cdo, ActiveCount=0, NonReplicatedInstances=array({instance}), ReplicatedInstances=array({})}
    local asc = object(3, {ActivatableAbilities={Items=array({spec})}})
    local input, receiver = object(4), object(5)
    local backend = setmetatable({state={ready=true}, calls={}}, Backend)
    function backend:receiver(target) return target.receiver == "ability_system" and asc or receiver end
    function backend:object(path)
        self.calls[#self.calls+1] = {lookup=path}
        if self.missing_entry and path == move.entry_point then error("entry unavailable") end
        return input
    end
    function backend:report(_, message) self.last_report = message end
    function backend:invoke(target, path, tag, action)
        self.calls[#self.calls+1] = {receiver=target, path=path}
        if path:find(":GetGameplayTagCount",1,true) then
            local name = tag.TagName:ToString()
            check(target == asc and (self.expected_tags and self.expected_tags[name]
                or name == (self.expected_state or "CharacterState.Space.Ground")),
                "Native input eligibility must be queried on the player's ASC")
            if self.tag_counts then return self.tag_counts[name] or 0 end
            return self.airborne and 0 or 1
        elseif path:find("TryActivateExternalActionAbilityInterruptLowerByTag",1,true) then
            check(target == receiver and tag.TagName:ToString() == (self.expected_tag or move.tag) and action == input,
                "Activation must use the normal game entry and the real heavy input")
            if self.reject then return false end
            instance.bIsActive, spec.ActiveCount = true, 1
            if self.end_during_activation then instance.bIsActive, spec.ActiveCount = false, 0 end
            if self.replace_during_activation then spec.NonReplicatedInstances = array({object(6,{bIsActive=true})}) end
            return true
        elseif path == move.entry_point then
            check(target == instance and target ~= cdo and instance.bIsActive,
                "Only the player's activated instance can enter the release stage")
            if self.fail_entry then error("release failed") end
            if self.expected_payload then
                check(tag == self.expected_payload, "The phase callback must receive its declared payload without gameplay-input conversion")
            end
            self.released = true
        elseif path:find(":K2_CancelAbility",1,true) then
            check(target == instance, "Failure cleanup must cancel the ability it activated")
            instance.bIsActive, spec.ActiveCount = false, 0
            self.canceled = true
        else error("Unexpected function " .. path) end
    end
    return backend, spec, instance, cdo
end
do
    local backend = fixture()
    local ok, result = backend:dispatch(move,"triggered",{x=1},"G")
    check(ok and result and backend.released, "One pulse must activate and immediately enter the complete release stage")
    check(#backend.calls == 4 and backend.calls[3].path:find("TryActivate",1,true)
        and backend.calls[4].path == move.entry_point, "The release must follow normal activation without held input")
end
do
    local backend = fixture(); backend.reject = true
    check(Moves.dispatch(backend,move,"triggered") == false and not backend.released,
        "Locked, unequipped, unaffordable or state-blocked moves must honor native activation refusal")
end
do
    local backend, spec, instance = fixture(); spec.ActiveCount=1; instance.bIsActive=true
    check(Moves.dispatch(backend,move,"triggered") == false and #backend.calls == 0,
        "A pulse must not hijack an already running ability")
end
do
    local backend, spec = fixture(); spec.NonReplicatedInstances=array({})
    check(Moves.dispatch(backend,move,"triggered") == false and #backend.calls == 0,
        "Missing player instances must be rejected before activation")
    spec.NonReplicatedInstances=array({spec.Ability})
    check(Moves.dispatch(backend,move,"triggered") == false, "The CDO must never be used as a live instance")
end
do
    local backend, spec = fixture(); spec.Ability.class="/Other/Weapon_C"
    check(Moves.dispatch(backend,move,"triggered") == false and #backend.calls == 0,
        "Another weapon's granted ability must not qualify")
end
do
    local backend = fixture(); backend.missing_entry=true
    local ok = backend:dispatch(move,"triggered",{x=1},"G")
    check(not ok and #backend.calls == 1 and not backend.released, "Resolve the release entry before any activation side effect")
end
for _, condition in ipairs({"end_during_activation","replace_during_activation"}) do
    local backend = fixture(); backend[condition]=true
    check(Moves.dispatch(backend,move,"triggered") == false and not backend.released,
        "An ended or replaced instance must not receive the release call")
end
do
    local backend, _, instance = fixture(); backend.fail_entry=true
    local ok = backend:dispatch(move,"triggered",{x=1},"G")
    check(not ok and backend.canceled and not instance.bIsActive,
        "A release dispatch failure must not leave an ability charging")
end
do
    local backend = fixture()
    for _, phase in ipairs({"completed","canceled"}) do
        check(Moves.dispatch(backend,move,phase) and #backend.calls == 0,
            "Input release or routing cancellation must not start another move")
    end
    local forced = {}; for key,value in pairs(move) do forced[key]=value end; forced.force=true
    check(not pcall(Moves.dispatch,backend,forced,"triggered") and #backend.calls == 0,
        "Complete moves must not force activation past game qualification")
end
do
    local grounded = {}; for key,value in pairs(move) do grounded[key]=value end
    grounded.required_tags={"CharacterState.Space.Ground"}
    local backend=fixture(); backend.airborne=true
    check(Moves.dispatch(backend,grounded,"triggered") == false and #backend.calls == 1,
        "A ground-only move must not bypass its input-side eligibility in midair")
    backend.airborne=false
    check(Moves.dispatch(backend,grounded,"triggered") and backend.released,
        "A qualified grounded move must still activate")
end
do
    local equipped = {}; for key,value in pairs(move) do equipped[key]=value end
    equipped.required_tags={"CharacterState.Space.Ground", "Weapon.Type.BronzeSword"}
    local backend=fixture()
    backend.expected_tags={["CharacterState.Space.Ground"]=true,["Weapon.Type.BronzeSword"]=true}
    backend.tag_counts={["CharacterState.Space.Ground"]=1}
    check(not Moves.dispatch(backend,equipped,"triggered") and #backend.calls == 2,
        "A granted instance must not bypass the required equipped weapon tag")
    backend.tag_counts["Weapon.Type.BronzeSword"]=1
    check(Moves.dispatch(backend,equipped,"triggered") and backend.released,
        "An equipped qualified weapon must still enter the complete attack")
end
do
    local selected = {}; for key,value in pairs(move) do selected[key]=value end
    local payload = {}
    selected.entry_arguments={payload}
    local backend = fixture(); backend.expected_payload=payload
    check(Moves.dispatch(backend,selected,"triggered") and backend.released,
        "An instance-owned phase callback must receive its GameplayEventData payload")
    selected.entry_arguments=nil
    selected.ignored_tags={"CharacterState.AbilityState.GuiTouBlade.InBHold"}
    backend = fixture(); backend.expected_state=selected.ignored_tags[1]
    check(not Moves.dispatch(backend,selected,"triggered") and #backend.calls == 1,
        "An excluded native action state must prevent activation")
    backend.airborne=true
    check(Moves.dispatch(backend,selected,"triggered") and backend.released,
        "A phase move must remain available when the excluded native state is absent")
end
do
    local charged = {}; for key,value in pairs(move) do charged[key]=value end
    charged.active_boolean_properties={"Current Shachi Reinforced"}
    local backend, _, instance = fixture()
    instance["Current Shachi Reinforced"] = false
    check(not Moves.dispatch(backend,charged,"triggered") and backend.canceled and not backend.released,
        "An activation that selected the resource failure branch must cancel without entering the complete attack")
    backend, _, instance = fixture()
    instance["Current Shachi Reinforced"] = true
    check(Moves.dispatch(backend,charged,"triggered") and backend.released and not backend.canceled,
        "The native resource-qualified branch must be allowed to enter the release phase")
end
do
    local combo = {kind="move", tag="Ability.Action.Hun.Light.Air.AAA",
        input_action="/PBZInput/InputActions/IA_AttackL_Press.IA_AttackL_Press",
        required_tags={"CharacterState.Space.Air"}, priority=2}
    local backend, spec = fixture()
    backend.expected_tag, backend.expected_state = combo.tag, "CharacterState.Space.Air"
    spec.NonReplicatedInstances = array({})
    check(Moves.dispatch(backend,combo,"triggered") and not backend.released,
        "A self-contained combo must use normal activation without requiring an existing instance or release method")
    check(#backend.calls == 3 and backend.calls[3].path:find("TryActivate",1,true),
        "A missing combo entry must not synthesize earlier combo inputs")
    backend = fixture(); backend.expected_state = "CharacterState.Space.Air"; backend.airborne=true
    check(not Moves.dispatch(backend,combo,"triggered") and #backend.calls == 1,
        "An air combo must reject a missing air state before normal activation")
    backend = fixture(); backend.expected_tag, backend.expected_state = combo.tag, "CharacterState.Space.Air"; backend.reject=true
    check(not Moves.dispatch(backend,combo,"triggered"), "Native qualification refusal must also apply to self-contained combo moves")
end
print("CompleteMoveTests PASS " .. checks)
