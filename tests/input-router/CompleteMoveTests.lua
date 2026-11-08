package.loaded["script-context"] = {new=function() error("Unexpected tuning in an untuned fixture") end}
local scripts = "prototype/input-router/ue4ss/Scripts/"
package.path = scripts .. "?.lua;" .. package.path
local Moves = require("complete-moves")
local Backend = require("backend")
local Router = require("router")
local checks = 0
local function check(value, message)
    assert(value, message)
    checks = checks + 1
end
function FName(value) return {ToString = function() return value end} end
local function array(items)
    return setmetatable({GetArrayNum=function() return #items end,ForEach = function(_, callback)
        for index, item in ipairs(items) do callback(index, {get = function() return item end}) end
    end},{__index=items})
end
local function map(items)
    return {ForEach = function(_, callback)
        for key,value in pairs(items) do
            callback({get=function() return FName(key) end}, {get=function() return value end})
        end
    end}
end
local class = "/PBZCombatAsset/ActorData/Players/Hun/Abilities/BronzeSword/GA_Hun_BronzeSword_B_Hold.GA_Hun_BronzeSword_B_Hold_C"
local move = {kind="move", tag="Ability.Action.Hun.Heavy.BronzeSword.BHold", ability_class=class,
    entry_point=class .. ":EndSuccess", input_action="/PBZInput/InputActions/IA_AttackH_PressLong.IA_AttackH_PressLong"}
local classes, nextClass = {}, 100000
function StaticFindObject(path)
    if not classes[path] then
        nextClass = nextClass+1
        local address=nextClass
        classes[path]={IsValid=function() return true end,GetAddress=function() return address end,
            GetFullName=function() return "BlueprintGeneratedClass "..path end}
    end
    return classes[path]
end
local function object(address, properties)
    local result = properties or {}
    function result:IsValid() return self.valid ~= false end
    function result:GetAddress() return address end
    function result:GetClass() return StaticFindObject(self.class or class) end
    function result:GetFullName() return "Object " .. self.path end
    function result:GetPropertyValue(name) return self[name] end
    return result
end
local function fixture()
    local cdo, instance = object(1), object(2, {bIsActive=false})
    local spec = {Ability=cdo, ActiveCount=0, NonReplicatedInstances=array({instance}), ReplicatedInstances=array({})}
    local asc = object(3, {ActivatableAbilities={Items=array({spec})}})
    local input, receiver = object(4), object(5)
    local backend = setmetatable({config={}, state={ready=true}, calls={}}, Backend)
    backend.main_timeline = object(7)
    backend.timeline_task = object(8, {Timeline=backend.main_timeline, CurrentTime=0})
    backend.subsystem, backend.pawn = object(9), object(10)
    backend.input_component, backend.input_library = object(11), object(12)
    backend.input_value_library, backend.input_value = object(15), object(16)
    backend.execution_component = object(14)
    function backend:library(name)
        if name == "EnhancedInput.EnhancedInputLibrary" then return self.input_value_library end
        check(name == "SGameInputSystem.SGameInputUtils", "Prepared input must use the native game input utility")
        return self.input_library
    end
    function backend:receiver(target) return target.receiver == "ability_system" and asc or receiver end
    function backend:object(path)
        self.calls[#self.calls+1] = {lookup=path}
        if self.missing_entry and path == move.entry_point then error("entry unavailable") end
        return input
    end
    function backend:report(_, message) self.last_report = message end
    backend.resource_costs={context={start_hold_input=function(_,owner,heldAction)
        check(owner==backend.subsystem and heldAction==input,"Preparation must pass its exact subsystem/action to the native Hold bridge")
        backend.input_starts=(backend.input_starts or 0)+1
    end},activate=function(_,_,_,callback) return callback() end}
    backend.resource_costs.instances={}
    function backend.resource_costs:restore(address) self.instances[address]=nil end
    function backend.resource_costs:arm(value)
        local state={instance=value};self.instances[value:GetAddress()]=state;return state
    end
    function backend.resource_costs:protect() end
    function backend.resource_costs.context:ability_damage() end
    function backend:invoke(target, path, tag, action, third, fourth)
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
            self.activations = (self.activations or 0) + 1
            instance.bIsActive, spec.ActiveCount = true, 1
            if self.drop_tag_on_activation then self.tag_counts[self.drop_tag_on_activation] = nil end
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
            if self.expected_fname then
                check(tag:ToString() == self.expected_fname, "A declared FName argument must use the backend's argument conversion")
            end
            self.released = true
        elseif path == "/Script/LegoAbility.LegoAbility:GetPlayTimelineTask" then
            check(target == instance, "Timeline readiness must inspect the activated player instance")
            return self.timeline_task
        elseif path == "/Script/LegoAbility.LegoAbility:GetTimelineByName" then
            check(target == instance and tag:ToString() == "Main", "The named timeline must resolve on the same ability instance")
            return self.main_timeline
        elseif path == "/Script/EnhancedInput.EnhancedInputLibrary:MakeInputActionValueOfType" then
            error("Opaque FInputActionValue must not return through Lua reflection")
        elseif path:find(":StartContinuousInputInjectionForAction",1,true) then
            error("Continuous native Hold must use the opaque native bridge")
        elseif path:find(":StopContinuousInputInjectionForAction",1,true) then
            check(target == self.subsystem and tag == input, "Preparation must stop the same action it injected")
            self.input_stops = (self.input_stops or 0) + 1
        elseif path == "/Script/SGameInputSystem.SGameInputUtils:GetSGameInputComponent" then
            check(target == self.input_library and tag == self.pawn, "Preparation must inspect the current player's game input component")
            return self.input_component
        elseif path == "/Script/SGameInputSystem.SGameInputComponent:IsHolding" then
            check(target == self.input_component and tag == input, "Preparation must wait for the native holding state of its original action")
            return self.holding == true
        elseif path == "/Script/SGameCombatSystem.BulletInterface:GetData" then
            check(target == self.projectile, "The stage must inspect the actual prepared projectile from this ability's Variables map")
            return self.projectile_data
        elseif path == "/Script/SGameCombatSystem.BulletInterface:GetStatus" then
            check(target == self.projectile, "Projectile readiness must come from the prepared bullet's native status")
            return self.projectile_status
        elseif path == "/Script/Engine.Actor:GetComponentByClass" then
            check(target == self.pawn and tag == input,
                "Execution qualification must inspect the current player's original execution component")
            return self.execution_component
        elseif path == "/Script/SGameExecution.ExecutionComponent:GetResolvedExecutionType" then
            check(target == self.execution_component and tag:ToString() == "ShachiWeapon",
                "Execution qualification must use the original channel name")
            return {TagName=FName(self.resolved_execution_type or "None")}
        elseif path:find(":K2_CancelAbility",1,true) then
            check(target == instance, "Failure cleanup must cancel the ability it activated")
            instance.bIsActive, spec.ActiveCount = false, 0
            self.canceled = true
        elseif path == "/MagCombatAbilities/Abilities/BP_ActionAbilityBase.BP_ActionAbilityBase_C:Move Cancel Ability" then
            check(target == instance and instance["Action Phase"].TagName:ToString() == "CharacterState.Action.Phase.Cancel",
                "Held restart must use the original recovery callback on its own instance")
            if not self.refuse_move_cancel then instance.bIsActive, spec.ActiveCount = false, 0 end
            self.move_cancels = (self.move_cancels or 0)+1
        else error("Unexpected function " .. path) end
    end
    return backend, spec, instance, cdo, asc
end

do
    local backend, spec, instance = fixture()
    check(Moves.dispatch(backend,move,"triggered"), "The first complete move must start normally")
    for _,phase in ipairs({"Start","Act","Combo"}) do
        instance["Action Phase"] = {TagName=FName("CharacterState.Action.Phase." .. phase)}
        check(not Moves.dispatch(backend,move,"triggered") and backend.activations == 1 and not backend.move_cancels,
            "A held binding cannot cut an attack before the native recovery cancellation window")
    end
    instance["Action Phase"] = {TagName=FName("CharacterState.Action.Phase.Cancel")}
    backend.refuse_move_cancel = true
    check(not Moves.dispatch(backend,move,"triggered") and backend.activations == 1,
        "A refused native end must not attempt overlapping activation")
    backend.refuse_move_cancel = false
    check(Moves.dispatch(backend,move,"triggered") and backend.activations == 2 and spec.ActiveCount == 1,
        "A still-active ability in recovery must end and reactivate without a guard binding")
    Moves.dispatch(backend,move,"completed")
    check(backend.activations == 2 and instance.bIsActive, "Key release must preserve the current attack")
    Moves.reset(backend)
    check(not Moves.dispatch(backend,move,"triggered") and backend.activations == 2,
        "Configuration reset must relinquish recovery ownership")
end
do
    local backend = fixture()
    local ok, result = backend:dispatch(move,"triggered",{x=1},"G")
    check(ok and result and backend.released, "One pulse must activate and immediately enter the complete release stage")
    check(#backend.calls == 4 and backend.calls[3].path:find("TryActivate",1,true)
        and backend.calls[4].path == move.entry_point, "The release must follow normal activation without held input")
end
do
    local backend, spec, _, _, asc = fixture()
    local granted={spec}
    for index=1,512 do
        local path="/Unrelated/Ability_"..index
        local unrelated=object(1000+index,{class=path})
        StaticFindObject(path).GetFullName=function() error("Unrelated ability class names must not be converted") end
        granted[#granted+1]={Ability=unrelated,ActiveCount=0,NonReplicatedInstances=array({}),ReplicatedInstances=array({})}
    end
    asc.ActivatableAbilities.Items=array(granted)
    check(Moves.dispatch(backend,move,"triggered") and backend.released,
        "Class-address selection failed with a large unrelated granted ability list")
end
do
    local backend = fixture()
    local lookup=StaticFindObject
    function StaticFindObject(path) if path~=move.ability_class then return lookup(path) end end
    local accepted=Moves.dispatch(backend,move,"triggered")
    StaticFindObject=lookup
    check(not accepted and not backend.activations,"An unloaded ability class must remain dormant")
    check(Moves.dispatch(backend,move,"triggered") and backend.released,
        "A subsequently loaded ability class must be retried without reloading the configuration")
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
for _,trigger in ipairs({"down","press"}) do
    local backend, spec, instance = fixture()
    local requests, releases = 0, 0
    local dispatch = backend.dispatch
    function backend:dispatch(...)
        requests = requests + 1
        self.released = false
        local ok, result = dispatch(self, ...)
        if self.released then releases = releases + 1 end
        return ok, result
    end
    local target = {}; for key,value in pairs(move) do target[key]=value end
    target.move_id = "move:BronzeSword.Release2"
    local router = Router.new({targets={release=target}, bindings={
        {id="held-release", keys={"G"}, trigger=trigger, outputs={"release"}}
    }}, backend)
    router:step(0, {G=1})
    router:step(.1, {G=1})
    check(requests == 2 and releases == 1 and instance.bIsActive and not backend.canceled,
        "Held move requests must continue without reentering or canceling the active release")
    spec.ActiveCount, instance.bIsActive = 0, false
    backend.reject = true
    router:step(.2, {G=1})
    check(requests == 3 and releases == 1,
        "A held move must preserve native refusal after the previous attack ends")
    backend.reject = false
    router:step(.3, {G=1})
    check(requests == 4 and releases == 2 and instance.bIsActive,
        "The same held key must start another complete release when normal activation allows it")
    router:step(.4, {})
    router:reset(true)
    check(requests == 4 and releases == 2 and instance.bIsActive and not backend.canceled,
        "Releasing or resetting the binding must stop retries and preserve the attack already in progress")
    spec.ActiveCount, instance.bIsActive = 0, false
    router:step(.5, {})
    check(requests == 4 and releases == 2,
        "An ended attack must not restart after the key was released")
end

local function deferred_move()
    local target = {}; for key,value in pairs(move) do target[key]=value end
    target.entry_when = {timeline="Main", minimum_time=.2, before_time=.8}
    return target
end

do
    local backend, _, instance = fixture()
    local target = deferred_move()
    check(Moves.dispatch(backend,target,"triggered") and not backend.released and instance.bIsActive,
        "An early complete-move request must preserve normal initialization and wait for its timeline boundary")
    local pending = backend.complete_move_pending
    check(pending and next(pending), "A waiting move must retain its activated instance")
    check(not Moves.dispatch(backend,target,"triggered") and backend.complete_move_pending == pending,
        "Repeated held requests must not replace or reactivate an already pending instance")
    backend.timeline_task.CurrentTime = .1
    Moves.update(backend)
    check(not backend.released and next(pending), "An earlier timeline frame must not enter the attack phase")
    backend.timeline_task.CurrentTime = .2
    Moves.update(backend)
    check(backend.released and not next(pending), "The native timeline reaching the threshold must enter the phase once")
    local calls = #backend.calls
    Moves.update(backend)
    Moves.reset(backend)
    check(#backend.calls == calls and instance.bIsActive and not backend.canceled,
        "A completed pending entry must not repeat or cancel the attack on reset")
end

do
    local backend = fixture()
    local target = deferred_move()
    backend.timeline_task = nil
    check(Moves.dispatch(backend,target,"triggered") and not backend.released,
        "Normal activation may initialize its timeline task on a later frame")
    backend.timeline_task = object(9, {Timeline=object(10), CurrentTime=.5})
    Moves.update(backend)
    check(not backend.released, "A different timeline's playback time must not satisfy the pending entry")
    backend.timeline_task.Timeline = backend.main_timeline
    Moves.update(backend)
    check(backend.released, "The current task must enter only after its timeline matches the expected native timeline")
end

do
    local backend, _, instance = fixture()
    check(Moves.dispatch(backend,deferred_move(),"triggered"), "The move must start before testing a missed entry window")
    backend.timeline_task.CurrentTime = .8
    Moves.update(backend)
    check(not backend.released and not next(backend.complete_move_pending) and instance.bIsActive and not backend.canceled,
        "A passed entry window must not rewind or cancel the original ability")
end

for _,change in ipairs({"ended","replaced","removed","component"}) do
    local backend, spec, instance, _, asc = fixture()
    Moves.dispatch(backend,deferred_move(),"triggered")
    backend.timeline_task.CurrentTime = .2
    if change == "ended" then spec.ActiveCount, instance.bIsActive = 0, false
    elseif change == "replaced" then spec.NonReplicatedInstances = array({object(10,{bIsActive=true})})
    elseif change == "removed" then spec.NonReplicatedInstances = array({})
    else asc.valid = false end
    Moves.update(backend)
    check(not backend.released and not next(backend.complete_move_pending) and not backend.canceled,
        "A pending phase must discard an " .. change .. " activation without touching another instance")
end

for _,qualification in ipairs({"required","ignored","resource"}) do
    local backend, _, instance = fixture()
    local target = deferred_move()
    if qualification == "required" then
        target.required_tags = {"CharacterState.Space.Ground"}
    elseif qualification == "ignored" then
        target.ignored_tags = {"CharacterState.Space.Ground"}
        backend.airborne = true
    else
        target.active_boolean_properties = {"Current Shachi Reinforced"}
        instance["Current Shachi Reinforced"] = true
    end
    check(Moves.dispatch(backend,target,"triggered"), "The move must initially meet its " .. qualification .. " qualification")
    if qualification == "resource" then instance["Current Shachi Reinforced"] = false
    else backend.airborne = not backend.airborne end
    backend.timeline_task.CurrentTime = .2
    Moves.update(backend)
    if qualification == "ignored" then
        check(backend.released and not backend.canceled,
            "An activation-side ignored tag must not block a later phase of the same normally activated instance")
    else
        check(not backend.released and not next(backend.complete_move_pending) and backend.canceled and not instance.bIsActive,
            "A pending entry must recheck its " .. qualification .. " qualification and clean up its own waiting activation")
    end
end

do
    local backend, _, instance = fixture()
    Moves.dispatch(backend,deferred_move(),"triggered")
    Moves.reset(backend)
    check(backend.complete_move_pending == nil and backend.canceled and not instance.bIsActive,
        "Disabling a pending move must cancel only the waiting activation")
    local calls = #backend.calls
    Moves.reset(backend)
    check(#backend.calls == calls, "Resetting cleared pending state must not cancel twice")
end

do
    local backend = fixture()
    local target = deferred_move()
    target.entry_arguments = {{fname="Main"}, .2}
    backend.expected_fname = "Main"
    Moves.dispatch(backend,target,"triggered")
    backend.timeline_task.CurrentTime = .2
    Moves.update(backend)
    check(backend.released, "A deferred native phase callback must receive converted FName arguments")
end

do
    local backend = fixture()
    local target = deferred_move()
    Moves.dispatch(backend,target,"triggered")
    backend.fail_entry = true
    backend.timeline_task.CurrentTime = .2
    local ok = pcall(Moves.update,backend)
    check(ok and backend.canceled and not next(backend.complete_move_pending) and backend:target_failed(target),
        "A failed deferred callback must cancel its own activation and disable only its target")
end

local function prepared_move()
    local target = deferred_move()
    target.prepare_input = "/PBZInput/InputActions/IA_ShachiAbility_Hold.IA_ShachiAbility_Hold"
    return target
end

do
    local backend, _, instance = fixture()
    local target = prepared_move()
    target.entry_when.property_minimum = {["Current Charge"]=2}
    target.entry_when.required_tags = {"CharacterState.Phase.Ready"}
    backend.expected_tags = {["CharacterState.Phase.Ready"]=true}
    backend.tag_counts = {}
    instance["Current Charge"] = 0
    check(Moves.dispatch(backend,target,"triggered") and backend.input_starts == 1 and not instance.bIsActive,
        "A prepared move must first inject the hold action without activating the ability")
    Moves.dispatch(backend,target,"triggered")
    Moves.update(backend)
    check(backend.input_starts == 1 and not instance.bIsActive and not backend.input_stops,
        "Repeated requests must share preparation and wait for the native holding state")
    backend.holding = true
    Moves.update(backend)
    check(instance.bIsActive and not backend.released and not backend.input_stops,
        "Native holding confirmation must allow normal activation while preserving the input for charging")
    backend.timeline_task.CurrentTime = .2
    Moves.update(backend)
    check(not backend.released, "An attack must wait for the original ability's charge property")
    instance["Current Charge"] = 2
    Moves.update(backend)
    check(not backend.released, "A charge threshold must not substitute for a required native phase tag")
    backend.tag_counts["CharacterState.Phase.Ready"] = 1
    Moves.update(backend)
    check(backend.released and backend.input_stops == 1 and not next(backend.complete_move_pending),
        "A qualified prepared entry must release its virtual hold and enter the original callback once")
    Moves.reset(backend)
    check(backend.input_stops == 1 and not backend.canceled,
        "Reset must preserve an attack that has already entered its final stage")
end

do
    local backend, _, instance = fixture()
    local target = prepared_move()
    backend.reject = true
    Moves.dispatch(backend,target,"triggered")
    backend.holding = true
    Moves.update(backend)
    check(not instance.bIsActive and backend.input_stops == 1 and not next(backend.complete_move_pending),
        "Native activation refusal must release preparation instead of holding a virtual action indefinitely")
    backend.reject = false
    backend.timeline_task.CurrentTime = .2
    Moves.dispatch(backend,target,"triggered")
    Moves.update(backend)
    check(backend.input_starts == 2 and backend.input_stops == 2 and backend.released,
        "A later held request must be able to prepare again and succeed after native refusal")
end

for _,active in ipairs({false,true}) do
    local backend, _, instance = fixture()
    Moves.dispatch(backend,prepared_move(),"triggered")
    if active then backend.holding = true; Moves.update(backend) end
    Moves.reset(backend)
    check(backend.input_stops == 1 and not instance.bIsActive and backend.canceled == (active or nil)
        and backend.complete_move_pending == nil,
        "Reset must release preparation and cancel only an already activated waiting ability")
end

do
    local backend, first, _, _, asc = fixture()
    local otherClass = "/Other/GA_SharedHold.GA_SharedHold_C"
    local otherCdo, otherInstance = object(20,{class=otherClass}), object(21,{class=otherClass,bIsActive=false})
    local second = {Ability=otherCdo, ActiveCount=0, NonReplicatedInstances=array({otherInstance}), ReplicatedInstances=array({})}
    asc.ActivatableAbilities.Items = array({first,second})
    local firstMove, secondMove = prepared_move(), prepared_move()
    secondMove.ability_class, secondMove.entry_point = otherClass, otherClass .. ":Enter"
    Moves.dispatch(backend,firstMove,"triggered")
    Moves.dispatch(backend,secondMove,"triggered")
    check(backend.input_starts == 1, "Recipes sharing a native hold action must share one continuous input injection")
    asc.ActivatableAbilities.Items = array({second})
    Moves.update(backend)
    check(not backend.input_stops and next(backend.complete_move_pending),
        "Removing one preparation owner must preserve the other owner's virtual hold")
    Moves.reset(backend)
    check(backend.input_stops == 1 and not next(backend.complete_move_inputs),
        "The final preparation owner must stop the shared virtual hold exactly once")
end

do
    local backend, _, instance = fixture()
    local target = deferred_move()
    target.entry_when.object_variable = {property="Variables", key="UnnamedBow",
        getter="/Script/SGameCombatSystem.BulletInterface:GetData", object="/Game/Bullet/Max.Max",
        status_getter="/Script/SGameCombatSystem.BulletInterface:GetStatus", status=1}
    instance.Variables = map({})
    Moves.dispatch(backend,target,"triggered")
    backend.timeline_task.CurrentTime = .2
    Moves.update(backend)
    check(not backend.released, "A release must wait for its original projectile to be prepared in Variables")
    backend.projectile = object(13)
    instance.Variables = map({OtherBow=backend.projectile})
    Moves.update(backend)
    check(not backend.released, "Another Variables entry must not qualify as the requested prepared projectile")
    instance.Variables = map({UnnamedBow=backend.projectile})
    backend.projectile_data = object(14,{path="/Game/Bullet/Normal.Normal"})
    Moves.update(backend)
    check(not backend.released, "An earlier charge stage's projectile data must not qualify for the selected attack")
    backend.projectile_data = object(15,{path="/Game/Bullet/Max.Max"})
    backend.projectile_status = 0
    Moves.update(backend)
    check(not backend.released, "A matching projectile that has not reached the native ready status must not release")
    backend.projectile_status = 1
    Moves.update(backend)
    check(backend.released and not next(backend.complete_move_pending),
        "The requested native projectile data must qualify the release without creating or replacing a projectile")
end

do
    local backend, _, instance = fixture()
    local target = prepared_move()
    target.entry_when.cancel_on_expire = true
    Moves.dispatch(backend,target,"triggered")
    backend.holding = true
    Moves.update(backend)
    backend.timeline_task.CurrentTime = .8
    Moves.update(backend)
    check(backend.input_stops == 1 and backend.canceled and not instance.bIsActive
        and not backend.released and not next(backend.complete_move_pending),
        "Missing an exact projectile window must stop preparation and cancel instead of releasing another charge stage")
end

for _,qualification in ipairs({"required","resource"}) do
    local backend, _, instance = fixture()
    local target = prepared_move()
    if qualification == "required" then target.required_tags = {"CharacterState.Space.Ground"}
    else
        target.active_boolean_properties = {"Current Shachi Reinforced"}
        instance["Current Shachi Reinforced"] = true
    end
    Moves.dispatch(backend,target,"triggered")
    backend.holding = true
    Moves.update(backend)
    if qualification == "required" then backend.airborne = true
    else instance["Current Shachi Reinforced"] = false end
    Moves.update(backend)
    check(backend.canceled and backend.input_stops == 1 and not backend.released,
        "Losing " .. qualification .. " qualification before the phase is ready must release its waiting input immediately")
end

local function release_before_move()
    local target = prepared_move()
    target.release_input_before_entry = true
    target.entry_when.completed_after_property = "AttackStartTime"
    target.entry_when.required_tags = {"CharacterState.PerfectTime"}
    return target
end

do
    local backend, _, instance = fixture()
    local target = release_before_move()
    instance.AttackStartTime = 1
    backend.expected_tags = {["CharacterState.PerfectTime"]=true}
    backend.tag_counts = {["CharacterState.PerfectTime"]=1}
    Moves.dispatch(backend,target,"triggered")
    backend.holding = true
    Moves.update(backend)
    backend.timeline_task.CurrentTime = .2
    Moves.update(backend)
    check(backend.input_stops == 1 and not backend.released and next(backend.complete_move_pending),
        "A callback that reads holding state must wait after stopping its virtual input")
    backend.tag_counts["CharacterState.PerfectTime"] = 0
    Moves.update(backend)
    check(not backend.released and not backend.canceled,
        "A still-held physical input must postpone the callback after the phase window has qualified")
    backend.holding = false
    Moves.update(backend)
    check(backend.released and backend.input_stops == 1 and not next(backend.complete_move_pending),
        "A qualified native release must enter once holding becomes false without requiring the consumed phase tag again")
end

for _,progress in ipairs({"time","timeline","reset"}) do
    local backend, _, instance = fixture()
    local target = release_before_move()
    instance.AttackStartTime = 1
    backend.expected_tags = {["CharacterState.PerfectTime"]=true}
    backend.tag_counts = {["CharacterState.PerfectTime"]=1}
    Moves.dispatch(backend,target,"triggered")
    backend.holding = true
    Moves.update(backend)
    backend.timeline_task.CurrentTime = .2
    Moves.update(backend)
    backend.holding = false
    if progress == "timeline" then backend.timeline_task.Timeline = object(16)
    else backend.timeline_task.CurrentTime = 1 end
    if progress == "reset" then Moves.reset(backend) else Moves.update(backend) end
    check(not backend.released and not backend.canceled and instance.bIsActive and backend.input_stops == 1,
        "Native attack progress detected by " .. progress .. " must finish pending release without restarting or canceling the attack")
end

for _,natural in ipairs({false,true}) do
    local backend, _, instance = fixture()
    local target = release_before_move()
    target.entry_when.completed_after_property = nil
    backend.expected_tags = {["CharacterState.PerfectTime"]=true}
    backend.tag_counts = {["CharacterState.PerfectTime"]=1}
    Moves.dispatch(backend,target,"triggered")
    backend.holding = true
    Moves.update(backend)
    backend.timeline_task.CurrentTime = .2
    Moves.update(backend)
    backend.holding = false
    if natural then backend.timeline_task.Timeline = object(16) end
    Moves.update(backend)
    check((backend.released == true) == not natural and not backend.canceled and not next(backend.complete_move_pending),
        "Native release with a separate attack timeline must dispatch once or retain its already-entered attack")
end

do
    local target = {}; for key,value in pairs(move) do target[key]=value end
    local prior = "Ability.Action.Hun.ShachiAbility.HuoLiuXing.AAA"
    target.activation_required_tags = {prior}
    local backend,_,instance = fixture()
    backend.expected_tags,backend.tag_counts = {[prior]=true},{}
    check(not Moves.dispatch(backend,target,"triggered") and not instance.bIsActive,
        "A training continuation must require its predecessor before normal activation")
    backend.tag_counts[prior],backend.drop_tag_on_activation = 1,prior
    check(Moves.dispatch(backend,target,"triggered") and backend.released and not backend.canceled
        and not backend.tag_counts[prior],
        "Normal continuation activation may end its predecessor without invalidating the new release callback")
end

do
    local classes = {"/Accessories/GhostFire_Lv1_C", "/Accessories/GhostFire_Lv2_C", "/Accessories/GhostFire_Lv3_C"}
    local target = {kind="move", ability_class=classes[1], active_ability_classes=classes, active_callback_only=true,
        entry_point=classes[1] .. ":Targeting Tick"}
    local function active_fixture()
        local backend, _, _, _, asc = fixture()
        local abilities, instances = {}, {}
        for index, path in ipairs(classes) do
            local cdo = object(20+index,{class=path})
            local instance = object(30+index,{class=path,bIsActive=true,balls=1})
            instances[index] = instance
            abilities[index] = {Ability=cdo,ActiveCount=1,
                NonReplicatedInstances=array({instance,cdo}),ReplicatedInstances=array({instance})}
        end
        asc.ActivatableAbilities.Items = array(abilities)
        backend.fired,backend.scans = 0,0
        function backend:invoke(instance,path)
            check(path == target.entry_point and instance.bIsActive and instance.balls ~= nil,
                "Accessory dispatch may only invoke the targeting callback on naturally active owned instances")
            self.scans = self.scans+1
            if self.fail_entry then error("targeting failed") end
            if self.enemy and instance.balls > 0 then
                self.fired = self.fired+instance.balls
                instance.balls = 0
            end
        end
        return backend,abilities,instances
    end
    local backend,abilities,instances = active_fixture()
    check(Moves.dispatch(backend,target,"triggered") and backend.scans == 3 and backend.fired == 0,
        "All naturally active accessory variants may scan without creating a projectile when no target qualifies")
    backend.enemy = true
    check(Moves.dispatch(backend,target,"triggered") and backend.scans == 6 and backend.fired == 3,
        "Accessory dispatch must reuse all three owned active variants and their existing prepared projectiles")
    Moves.dispatch(backend,target,"triggered")
    check(backend.scans == 9 and backend.fired == 3 and not backend.complete_move_pending,
        "Held accessory dispatch must not refill consumed balls or own the passive lifecycle")
    Moves.reset(backend)
    check(instances[1].bIsActive and instances[2].bIsActive and instances[3].bIsActive,
        "Routing cleanup must preserve naturally active accessory buffs and their timers")
    backend,abilities,instances = active_fixture()
    abilities[1].ActiveCount = 0
    instances[2].bIsActive = false
    abilities[3].Ability.class = "/Accessories/Other_C"
    check(not Moves.dispatch(backend,target,"triggered") and backend.scans == 0,
        "Inactive grants, ended instances and unrelated passive classes cannot dispatch accessory attacks")
    backend,abilities,instances = active_fixture()
    local extra = object(44,{class=classes[1],bIsActive=true,balls=1})
    abilities[1].NonReplicatedInstances = array({instances[1],extra})
    backend.enemy = true
    Moves.dispatch(backend,target,"triggered")
    check(backend.scans == 4 and backend.fired == 4,
        "Per-execution accessory abilities must dispatch each active owned instance exactly once")
    backend,abilities,instances = active_fixture()
    backend.fail_entry = true
    local ok = backend:dispatch(target,"triggered",{x=1},"G")
    check(not ok and instances[1].bIsActive and not backend.canceled and not backend.complete_move_pending,
        "An accessory callback failure must preserve the naturally granted passive rather than cancel it")
    backend,abilities,instances = active_fixture()
    for _,phase in ipairs({"completed","canceled"}) do Moves.dispatch(backend,target,phase) end
    check(backend.scans == 0, "Accessory release cannot trigger another targeting callback")
end

for _, qualification in ipairs({"none","dash","sprint","both"}) do
    local backend = fixture()
    local target = {}; for key,value in pairs(move) do target[key] = value end
    local dash, sprint = "Ability.Action.Hun.Dash", "CharacterState.Native.Attitude.Sprinting"
    target.activation_any_tags = {dash,sprint}
    backend.expected_tags,backend.tag_counts = {[dash]=true,[sprint]=true},{}
    if qualification == "dash" or qualification == "both" then backend.tag_counts[dash] = 1 end
    if qualification == "sprint" or qualification == "both" then backend.tag_counts[sprint] = 1 end
    backend.drop_tag_on_activation = qualification == "sprint" and sprint or dash
    local result = Moves.dispatch(backend,target,"triggered")
    check(result == (qualification ~= "none") and (backend.released == true) == (qualification ~= "none"),
        "Alternative native activation states must accept either source and reject an idle request")
    check(not backend.canceled, "A consumed movement state must not cancel an already-qualified attack")
end

do
    local target = {kind="move",tag="Ability.Action.Execution.Channel.ShachiWeapon",
        input_action=move.input_action,execution_channel="ShachiWeapon",
        execution_type="Execution.Type.Combat.ShachiWeapon.TigerCannon.Counter"}
    for _,resolved in ipairs({"None","Execution.Type.Combat.ShachiWeapon.TigerCannon.Execution",target.execution_type}) do
        local backend = fixture()
        backend.expected_tag,backend.resolved_execution_type = target.tag,resolved
        local result = Moves.dispatch(backend,target,"triggered")
        check(result == (resolved == target.execution_type) and (backend.activations or 0) == (result and 1 or 0),
            "A conditional execution binding must reject missing or other executions and request only its original channel")
        check(not backend.released and not backend.canceled and not backend.complete_move_pending,
            "Execution requests must preserve native target initialization and never invoke the attack's timeline directly")
    end
    local backend = fixture()
    backend.execution_component.valid = false
    check(not Moves.dispatch(backend,target,"triggered") and not backend.activations,
        "A missing native execution component must prevent a conditional counter request")
    backend = fixture()
    backend.expected_tag,backend.resolved_execution_type,backend.reject = target.tag,target.execution_type,true
    check(not Moves.dispatch(backend,target,"triggered") and not backend.activations,
        "Native channel refusal for target, resource or action state must remain effective after a matching resolution")
end

for _,selection in ipairs({"class","tag"}) do
    local backend, spec, instance, cdo = fixture()
    local target = {kind="move",tag=move.tag,input_action=move.input_action}
    if selection == "class" then target.ability_class = class
    else cdo.AbilityTags = {GameplayTags=array({{TagName=FName(move.tag)}})} end
    check(Moves.dispatch(backend,target,"triggered") and backend.activations == 1,
        "A self-contained " .. selection .. " move must retain normal activation")
    instance["Action Phase"] = {TagName=FName("CharacterState.Action.Phase.Act")}
    check(not Moves.dispatch(backend,target,"triggered") and not backend.move_cancels,
        "Self-contained held moves must preserve their attack phase")
    instance["Action Phase"] = {TagName=FName("CharacterState.Action.Phase.Cancel")}
    check(Moves.dispatch(backend,target,"triggered") and backend.activations == 2 and backend.move_cancels == 1,
        "Self-contained " .. selection .. " moves must repeat from their owned recovery window")
end
print("CompleteMoveTests PASS " .. checks)
