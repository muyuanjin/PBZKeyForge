package.loaded["script-context"] = {new=function() error("Unexpected tuning in an untuned fixture") end}
package.path = "prototype/input-router/ue4ss/Scripts/?.lua;" .. package.path
local Router, Backend, Moves = require("router"), require("backend"), require("complete-moves")
local path = (...) or os.getenv("PBZ_PHASE_MOVE_CONFIG") or "dist/complete-move-catalog-check/phase-moves.lua"
local config = assert(loadfile(path))()
local targets, count = {}, 0
for _, target in pairs(config.targets) do
    assert(target.kind == "move" and target.move_id and not target.force)
    targets[target.move_id], count = target, count + 1
end
assert(count >= 119, "The compiled phase fixture lost complete-move catalog entries")
for _, binding in ipairs(config.bindings) do
    assert(binding.trigger == "down" and binding.keys[1] == "F8", "A phase fixture lost its ordinary held binding")
end
local bronze = assert(targets["move:Ability.Action.Hun.CombatArt.BronzeSword.Final"])
assert(bronze.entry_point == "/Script/LegoAbility.LegoAbility:PlayTimelineByName")
assert(bronze.entry_arguments[1].fname == "Main" and bronze.entry_arguments[4] == 2.43
    and #bronze.entry_arguments == 4)
assert(bronze.entry_when.timeline == "Main" and bronze.entry_when.minimum_time == .01253
    and bronze.entry_when.before_time == 2.43 and not bronze.entry_when.cancel_on_expire)
local bow = assert(targets["move:Ability.Action.Hun.ShachiAbility.Default.UnnamedBow.Shoot.Normal"])
local perfect = assert(targets["move:Ability.Action.Hun.ShachiAbility.Default.UnnamedBow.Shoot.Perfect"])
local variable = bow.entry_when.object_variable
assert(bow.prepare_input == "/PBZInput/InputActions/IA_ShachiAbility_Hold.IA_ShachiAbility_Hold")
assert(variable.property == "Variables" and variable.key == "UnnamedBow"
    and variable.getter == "/Script/SGameCombatSystem.BulletInterface:GetData"
    and variable.status_getter == "/Script/SGameCombatSystem.BulletInterface:GetStatus" and variable.status == 1)
assert(bow.entry_when.cancel_on_expire and perfect.entry_when.cancel_on_expire
    and perfect.entry_when.before_time > perfect.entry_when.minimum_time)
local fist = assert(targets["move:Ability.Action.Hun.ShachiAbility.TieKuangQuanTao.Combo.C.PerfectRelease"])
assert(fist.release_input_before_entry and fist.prepare_input == bow.prepare_input
    and fist.entry_when.completed_after_property == "AttackStartTime"
    and fist.entry_when.required_tags[1] == "CharacterState.AbilityState.TieKuangQuanTao.Charge.PerfectTime")
local ghost = assert(targets["move:Accessory.RandomDrink.GhostFire.Launch"])
assert(#ghost.active_ability_classes == 3 and ghost.active_callback_only and not ghost.prepare_input and not ghost.entry_when
    and ghost.entry_point == ghost.ability_class .. ":Targeting Tick")
for level, path in ipairs(ghost.active_ability_classes) do
    assert(path:match("GA_Accessory_RandomDrink_GhostFire_Lv" .. level .. "_C$"))
end
local quick = 0
for _, target in pairs(targets) do
    if target.ability_class and target.ability_class:find("QuickCharge",1,true) then
        quick = quick + 1
        assert(target.prepare_input == bow.prepare_input and target.entry_when.timeline == "Main"
            and target.tag == "Ability.Action.Hun.ShachiAbility.TigerCannon.QuickCharge"
            and target.ability_class:match("GA_TigerCannon_QuickCharge%.GA_TigerCannon_QuickCharge_C$"),
            "Quick-charge cannon lost its own activation and preparation")
    end
end
assert(quick == 3, "The compiled catalog lost a quick-charge cannon phase")
local training = assert(targets["move:Ability.Action.Hun.ShachiAbility.HuoLiuXing.AAAA.Training"])
assert(training.activation_required_tags[1] == "Ability.Action.Hun.ShachiAbility.HuoLiuXing.AAA"
    and training.activation_required_tags[2] == "Ability.Action.ShaqiReinforced"
    and training.required_tags[2] == "CharacterState.WeaponUpgrade.HuoLiuXing.AAAA",
    "A compiled training follow-up must preserve original predecessor and upgrade qualification separately")
local rush = assert(targets["move:Ability.Action.Hun.ShachiAbility.Default.ZhuoBiDun.Rush"])
assert(rush.activation_any_tags[1] == "Ability.Action.Hun.Dash"
    and rush.activation_any_tags[2] == "CharacterState.Native.Attitude.Sprinting"
    and rush.required_tags[2] == "CharacterState.WeaponUpgrade.ZhuoBiDun.Rush"
    and not rush.prepare_input and not rush.entry_point,
    "Shield rush must retain either native movement source and execute its own finite attack")

local thunderCounter = assert(targets["move:Ability.Action.Hun.ShachiAbility.ThunderSpear.DeflectCounter.Native"])
assert(thunderCounter.tag == "Ability.Action.Hun.ShachiAbility.ThunderSpear.DeflectCounter"
    and not thunderCounter.entry_point and not thunderCounter.prepare_input,
    "The native counter must start normally and keep perfect-dodge events inside its original ability")
local tigerCounter = assert(targets["move:Ability.Action.Hun.ShachiAbility.TigerCannon.Counter"])
assert(tigerCounter.tag == "Ability.Action.Execution.Channel.ShachiWeapon"
    and tigerCounter.execution_channel == "ShachiWeapon"
    and tigerCounter.execution_type == "Execution.Type.Combat.ShachiWeapon.TigerCannon.Counter"
    and not tigerCounter.entry_point and not tigerCounter.prepare_input,
    "Tiger counter must preserve the original execution channel and its exact resolved execution type")
local baiAssault = assert(targets["move:Ability.Action.Hun.BaiYing.Assault"])
assert(baiAssault.tag == "Ability.Action.Execution.Channel.SpecialAttack" and baiAssault.execution_channel == "SpecialAttack"
    and baiAssault.execution_type == "Execution.Type.CombatFar.Assault.BaiYing" and not baiAssault.ability_class,
    "White Shadow assault must initialize through its actual selected execution channel")
local snakeDeflect = assert(targets["move:Ability.Action.Hun.Light.SnakeDual.A_Deflect"])
assert(snakeDeflect.activate_by_class and not snakeDeflect.entry_point
    and snakeDeflect.required_tags[3] == "CharacterState.AbilityState.SnakeDual.Normal",
    "SnakeDual deflect attack must activate its original equipped normal-state class")
local breakAttacks = {}
for _, weapon in ipairs({"Iron", "Jigsaw"}) do
    local target = assert(targets["move:Ability.Action.Hun." .. weapon .. ".BreakAct"])
    assert(target.tag == "Ability.Action.Execution.Channel.SpecialAttack"
        and target.execution_channel == "SpecialAttack"
        and target.execution_type == "Execution.Type.Combat.BreakAct." .. weapon
        and target.required_tags[1] == "Weapon.Type." .. weapon
        and target.input_action == "/PBZInput/InputActions/IA_AttackH_Press.IA_AttackH_Press"
        and target.priority == -5 and not target.ability_class and not target.entry_point,
        "A break attack must preserve its exact native SpecialAttack channel and equipment")
    breakAttacks[#breakAttacks+1] = target
end
assert(breakAttacks[1].required_tags[2] == "CharacterState.WeaponUpgrade.Iron.CanBreakAct")

function FName(value) return {ToString=function() return value end} end
local classes, nextClass = {}, 1000
function StaticFindObject(path)
    if not classes[path] then
        nextClass = nextClass + 1
        local address = nextClass
        classes[path] = {IsValid=function() return true end,GetAddress=function() return address end,
            GetFullName=function() return "BlueprintGeneratedClass " .. path end}
    end
    return classes[path]
end
local function object(address, properties)
    local result = properties or {}
    function result:IsValid() return self.valid ~= false end
    function result:GetAddress() return address end
    function result:GetClass() return StaticFindObject(self.class) end
    function result:GetFullName() return "Object " .. self.path end
    function result:GetPropertyValue(name) return self[name] end
    return result
end
local function array(values)
    return setmetatable({GetArrayNum=function() return #values end,ForEach=function(_,callback)
        for index,value in ipairs(values) do callback(index,{get=function() return value end}) end
    end},{__index=values})
end
local function fixture(target, bindingKey)
    bindingKey = bindingKey or "F8"
    local grantedClass = target.ability_class or "/Fixture/ChannelAbility.ChannelAbility_C"
    local instance = object(1,{class=grantedClass,bIsActive=false,AttackStartTime=10})
    for _, property in ipairs(target.active_boolean_properties or {}) do instance[property] = true end
    local ability = {Ability=object(2,{class=grantedClass}),ActiveCount=0,
        NonReplicatedInstances=array({instance}),ReplicatedInstances=array({})}
    local asc = object(3,{ActivatableAbilities={Items=array({ability})}})
    local input, hold, receiver, subsystem = object(4), object(5), object(6), object(7)
    local timeline = object(8)
    local task = object(9,{Timeline=timeline,CurrentTime=0})
    local namedTimeline = timeline
    if target.completion_timeline then namedTimeline = object(30) end
    local gameInput, library, projectile = object(10), object(11), object(12)
    local execution = object(14)
    local state = {tags={},task=task,namedTimeline=namedTimeline,instance=instance,ability=ability,activations=0,entries=0,starts=0,stops=0}
    for _, tag in ipairs(target.required_tags or {}) do state.tags[tag] = 1 end
    instance.Variables = {ForEach=function(_,callback)
        if state.bullet_data then
            callback({get=function() return FName(target.entry_when.object_variable.key) end},
                {get=function() return projectile end})
        end
    end}
    local backend = setmetatable({config=config,state={ready=true},subsystem=subsystem,pawn=object(13)},Backend)
    function backend:receiver(spec) return spec.receiver == "ability_system" and asc or receiver end
    function backend:object(path)
        return path == target.prepare_input and hold or input
    end
    function backend:library() return library end
    function backend:report(_,message) error(message) end
    backend.resource_costs={context={start_hold_input=function(_,owner,heldAction)
        assert(owner==subsystem and heldAction==hold)
        state.starts=state.starts+1;state.input_requested=true
    end},activate=function(_,_,_,callback) return callback() end}
    backend.resource_costs.instances={}
    function backend.resource_costs:restore(address) self.instances[address]=nil end
    function backend.resource_costs:arm(value)
        local state={instance=value};self.instances[value:GetAddress()]=state;return state
    end
    function backend.resource_costs:protect() end
    function backend.resource_costs.context:ability_damage() end
    function backend:invoke(current,path,...)
        local args = {...}
        if path:find(":GetGameplayTagCount",1,true) then return state.tags[args[1].TagName:ToString()] or 0
        elseif path:find(":TryActivateExternalActionAbilityByClass",1,true) then
            assert(current == receiver and args[1]:GetFullName() == "BlueprintGeneratedClass " .. target.ability_class
                and args[2] == input and args[3] == target.priority)
            if state.reject_activation then return false end
            state.activations = state.activations + 1
            ability.ActiveCount,instance.bIsActive = 1,true
            return true
        elseif path:find(":TryActivateExternalActionAbilityInterruptLowerByTag",1,true) then
            assert(current == receiver and args[1].TagName:ToString() == target.tag and args[2] == input)
            if state.activation_refused then return false end
            state.activations = state.activations + 1
            ability.ActiveCount,instance.bIsActive = 1,true
            if state.remove_activation_tags then
                for _, tag in ipairs(target.activation_required_tags or {}) do state.tags[tag] = 0 end
            end
            return true
        elseif path == "/Script/Engine.Actor:GetComponentByClass" then
            assert(current == backend.pawn and args[1] == input)
            return not state.no_execution and execution or nil
        elseif path == "/Script/SGameExecution.ExecutionComponent:GetResolvedExecutionType" then
            assert(current == execution and args[1]:ToString() == target.execution_channel)
            return {TagName=FName(state.resolved_execution_type or "None")}
        elseif path == "/Script/LegoAbility.LegoAbility:GetPlayTimelineTask" then return task
        elseif path == "/Script/LegoAbility.LegoAbility:GetTimelineByName" then
            assert(current == instance and args[1]:ToString() == (target.completion_timeline or target.entry_when.timeline))
            return namedTimeline
        elseif path:find(":StartContinuousInputInjectionForAction",1,true) then
            error("Continuous native Hold must preserve its value in the native bridge")
        elseif path == "/Script/EnhancedInput.EnhancedInputLibrary:MakeInputActionValueOfType" then
            error("Opaque FInputActionValue must not return through Lua reflection")
        elseif path:find(":StopContinuousInputInjectionForAction",1,true) then
            assert(current == subsystem and args[1] == hold)
            state.stops = state.stops + 1
            state.input_requested = false
        elseif path == "/Script/SGameInputSystem.SGameInputUtils:GetSGameInputComponent" then return gameInput
        elseif path == "/Script/SGameInputSystem.SGameInputComponent:IsHolding" then return state.holding == true
        elseif path == "/Script/SGameCombatSystem.BulletInterface:GetData" then
            assert(current == projectile)
            return state.bullet_data
        elseif path == "/Script/SGameCombatSystem.BulletInterface:GetStatus" then return state.bullet_status
        elseif path == target.entry_point then
            assert(current == instance and instance.bIsActive)
            if path == "/Script/LegoAbility.LegoAbility:PlayTimelineByName" then
                assert(select("#",...) == 4, "PlayTimelineByName requires four reflected parameters")
            end
            state.entries,state.arguments = state.entries + 1,args
        elseif path:find(":K2_CancelAbility",1,true) then
            state.canceled = true
            ability.ActiveCount,instance.bIsActive = 0,false
        else error("Unexpected compiled phase call " .. path) end
    end
    local router = Router.new({targets={selected=target},bindings={
        {id="compiled-phase",keys={bindingKey},trigger="down",outputs={"selected"}}
    }},backend)
    local function step(time,held)
        Moves.update(backend)
        router:step(time,held and {[bindingKey]=1} or {})
    end
    function state:advance_input() self.holding = self.input_requested == true end
    return state,backend,step
end

local preparations = 0
for _, target in pairs(targets) do
    if target.prepare_input then
        preparations = preparations + 1
        for _, key in ipairs({"F8", "Gamepad_DPad_Down"}) do
            local function prepared_fixture()
                local state, backend, step = fixture(target,key)
                for _, tag in ipairs(target.activation_required_tags or {}) do state.tags[tag] = 1 end
                if target.activation_any_tags then state.tags[target.activation_any_tags[1]] = 1 end
                state.task.Timeline = object(70)
                return state, backend, step
            end
            local state, backend, step = prepared_fixture()
            step(0,true); step(.01,true)
            assert(state.starts == 1 and state.activations == 0,
                "Every prepared entrance must wait for effective native input: " .. target.move_id)
            state:advance_input(); step(.02,true)
            assert(state.activations == 1 and state.entries == 0 and state.stops == 0,
                "Activation must retain Hold until the actual phase is ready: " .. target.move_id)
            local condition = target.entry_when or {}
            state.task.Timeline = state.namedTimeline
            state.task.CurrentTime = condition.minimum_time or 0
            for name, minimum in pairs(condition.property_minimum or {}) do state.instance[name] = minimum end
            for _, tag in ipairs(condition.required_tags or {}) do state.tags[tag] = 1 end
            if condition.object_variable then
                Moves.update(backend)
                assert(state.entries == 0 and state.stops == 0,
                    "A ready timeline cannot replace a missing projectile: " .. target.move_id)
                state.bullet_data = object(71,{path=condition.object_variable.object})
                state.bullet_status = 0
                Moves.update(backend)
                assert(state.entries == 0 and state.stops == 0,
                    "An unready projectile cannot be released: " .. target.move_id)
                state.bullet_status = condition.object_variable.status
            end
            Moves.update(backend)
            if target.release_input_before_entry then
                assert(state.entries == 0 and state.stops == 1,
                    "Release-dependent stages must wait for the game's input update: " .. target.move_id)
                state:advance_input(); Moves.update(backend)
            end
            assert(state.entries == (target.entry_point and 1 or 0) and state.stops == 1
                and not next(backend.complete_move_pending or {}) and not next(backend.complete_move_inputs or {}),
                "A prepared entrance must release exactly once and relinquish all Hold ownership: " .. target.move_id)
            step(.1,false); Moves.reset(backend)
            assert(state.stops == 1 and not state.canceled and state.instance.bIsActive,
                "Completed preparation must preserve the native attack: " .. target.move_id)

            for _, outcome in ipairs({"refused","reset-preparing","reset-active","ended"}) do
                state, backend, step = prepared_fixture()
                state.reject_activation, state.activation_refused = outcome == "refused", outcome == "refused"
                step(0,true)
                if outcome ~= "reset-preparing" then state:advance_input(); Moves.update(backend) end
                if outcome == "ended" then
                    state.instance.bIsActive, state.ability.ActiveCount = false,0
                    Moves.update(backend)
                else Moves.reset(backend) end
                assert(state.starts == 1 and state.stops == 1 and not next(backend.complete_move_pending or {})
                    and not next(backend.complete_move_inputs or {}) and state.entries == 0,
                    "Preparation must clean up after " .. outcome .. ": " .. target.move_id)
                assert((state.canceled == true) == (outcome == "reset-active"),
                    "Only an owned active preparation may be canceled: " .. target.move_id)
            end
            if condition.cancel_on_expire and condition.before_time then
                state, backend, step = prepared_fixture()
                step(0,true); state:advance_input(); step(.01,true)
                state.task.Timeline, state.task.CurrentTime = state.namedTimeline, condition.before_time
                Moves.update(backend)
                assert(state.canceled and state.entries == 0 and state.stops == 1,
                    "An expired exact-charge window must cancel without firing another tier: " .. target.move_id)
            end
        end
    end
end
assert(preparations == 37, "The full preparation inventory must include every compiled Hold recipe")

for _, selected in ipairs({tigerCounter,baiAssault,breakAttacks[1],breakAttacks[2]}) do
    local state,backend,step = fixture(selected)
    step(0,true)
    state.resolved_execution_type = "Execution.Type.Combat.ShachiWeapon.TigerCannon.Execution"
    step(.1,true)
    assert(state.activations == 0 and state.entries == 0,
        "A held special attack must reject an unresolved or ordinary execution type")
    for _, other in ipairs({tigerCounter,baiAssault,breakAttacks[1],breakAttacks[2]}) do
        if other ~= selected then
            state.resolved_execution_type = other.execution_type
            step(.15,true)
            assert(state.activations == 0, "A selected attack must reject another attack's resolved execution type")
        end
    end
    state.resolved_execution_type = selected.execution_type
    state.activation_refused = true
    step(.2,true)
    assert(state.activations == 0, "A matching type must still respect original channel qualification")
    state.activation_refused = false
    step(.3,true)
    step(.4,false)
    Moves.reset(backend)
    assert(state.activations == 1 and state.entries == 0 and state.starts == 0
        and state.stops == 0 and not state.canceled and not backend.complete_move_pending,
        "A channel attack must preserve native target selection, execution initialization and lifecycle")
    local missing,_,missingStep = fixture(selected)
    missing.no_execution,missing.resolved_execution_type = true,selected.execution_type
    missingStep(0,true)
    assert(missing.activations == 0, "A missing native execution component cannot perform a channel attack")
    for _, tag in ipairs(selected.required_tags) do
        local unqualified,_,unqualifiedStep = fixture(selected)
        unqualified.resolved_execution_type = selected.execution_type
        unqualified.tags[tag] = 0
        unqualifiedStep(0,true)
        assert(unqualified.activations == 0, "A matching execution type cannot bypass equipment or upgrade qualification")
    end
end

do
    local state,backend,step = fixture(snakeDeflect)
    state.tags[snakeDeflect.required_tags[3]] = 0
    step(0,true)
    assert(state.activations == 0, "SnakeDual attack cannot replace the native released-weapon state")
    state.tags[snakeDeflect.required_tags[3]] = 1
    step(.1,true)
    step(.2,false)
    Moves.reset(backend)
    assert(state.activations == 1 and state.entries == 0 and not state.canceled and not backend.complete_move_pending,
        "SnakeDual deflect attack must use original class activation and retain native combo setup")
end

do
    local state,backend,step = fixture(thunderCounter)
    step(0,true)
    step(.1,false)
    Moves.reset(backend)
    assert(state.activations == 1 and state.entries == 0 and not state.canceled,
        "The compiled native counter must use normal input qualification without injecting its perfect callback")
end

do
    local state,backend,step = fixture(training)
    step(0,true)
    assert(state.activations == 0 and state.entries == 0, "A compiled training move must reject missing native predecessors")
    for _, tag in ipairs(training.activation_required_tags) do state.tags[tag] = 1 end
    state.remove_activation_tags = true
    step(.1,true)
    assert(state.activations == 1 and state.entries == 1 and not state.canceled,
        "A compiled training release must accept normal removal of its predecessor after activation")
end

local finishers = 0
for _, target in pairs(targets) do
    if target.entry_point == "/Script/LegoAbility.LegoAbility:PlayTimelineByName" then
        finishers = finishers + 1
        local state,backend,step = fixture(target)
        step(0,true)
        assert(state.activations == 1 and state.entries == 0)
        state.task.CurrentTime = target.entry_when.minimum_time
        step(.1,true)
        assert(state.entries == 1 and state.arguments[1]:ToString() == target.entry_when.timeline
            and state.arguments[4] == target.entry_when.before_time and #state.arguments == 4,
            "The compiled finisher must initialize normally and invoke the native phase with four parameters")
        step(.2,false)
        Moves.reset(backend)
        assert(state.entries == 1 and not state.canceled)
    end
end
assert(finishers == 17)

do
    local laser = assert(targets["move:Ability.Action.Hun.ShachiAbility.DuYanBow.Laser.Shoot"])
    assert(laser.entry_when.timeline == "Loop" and laser.entry_when.minimum_time == .056,
        "The laser finisher must preserve both original eye generation events before releasing")
    for _, upgraded in ipairs({false,true}) do
        local state,backend,step = fixture(laser)
        state.tags["CharacterState.WeaponUpgrade.DuYanBow.LaserEyes"] = upgraded and 1 or 0
        step(0,true)
        state.holding = true
        step(.1,true)
        assert(state.activations == 1 and state.entries == 0 and state.stops == 0,
            "The laser finisher must wait after normally entering Loop")
        for index,time in ipairs({.05339889973402023,.055938899517059326,.055999}) do
            state.task.CurrentTime = time
            step(.1 + index*.1,true)
            assert(state.entries == 0 and state.stops == 0,
                "Neither eye's original event time may be skipped by the finisher")
        end
        state.task.CurrentTime = .056
        step(.5,true)
        assert(state.entries == 1 and state.stops == 1 and not state.canceled,
            "After native eye preparation the finisher releases once, with or without the optional upgrade")
        step(.6,false)
        Moves.reset(backend)
        assert(state.entries == 1 and state.stops == 1 and state.instance.bIsActive,
            "The laser finisher preserves the native Shoot lifecycle after releasing virtual Hold")
    end
end

do
    local state,backend,step = fixture(bow)
    step(0,true)
    assert(state.starts == 1 and state.activations == 0)
    state.holding = true
    step(.1,true)
    state.task.CurrentTime = bow.entry_when.minimum_time
    step(.2,true)
    assert(state.activations == 1 and state.entries == 0, "A compiled bow release must wait for its original prepared projectile")
    state.bullet_data = object(20,{path=variable.object})
    state.bullet_status = 1
    step(.3,true)
    assert(state.entries == 1 and state.stops == 1 and state.arguments[1] == 0)
    Moves.reset(backend)
    assert(not state.canceled)
end

do
    local state,_,step = fixture(fist)
    step(0,true)
    state.holding = true
    step(.1,true)
    state.tags[fist.entry_when.required_tags[1]] = 1
    step(.2,true)
    assert(state.stops == 1 and state.entries == 0)
    state.holding = false
    state.tags[fist.entry_when.required_tags[1]] = 0
    step(.3,true)
    assert(state.entries == 1 and state.arguments[1] == 0,
        "The compiled fist phase must wait for real input release before calling its original tick entry")
end

do
    local state,backend,step = fixture(perfect)
    step(0,true)
    state.holding = true
    step(.1,true)
    state.task.CurrentTime = perfect.entry_when.before_time + .01
    Moves.update(backend)
    assert(state.canceled and state.stops == 1 and state.entries == 0
        and not next(backend.complete_move_pending),
        "A compiled exact-window release must cancel a missed window instead of firing another projectile stage")
end
for _, path in ipairs(ghost.active_ability_classes) do
    local state,backend,step = fixture(ghost)
    state.instance.class,state.ability.Ability.class = path,path
    step(0,true)
    assert(state.entries == 0 and state.activations == 0,
        "A compiled accessory binding cannot activate a missing random buff")
    state.instance.bIsActive,state.ability.ActiveCount = true,1
    step(.1,true)
    step(.2,true)
    step(.3,false)
    Moves.reset(backend)
    assert(state.entries == 2 and state.activations == 0 and state.starts == 0 and state.stops == 0
        and not state.canceled and state.instance.bIsActive and not backend.complete_move_pending,
        "Compiled accessory dispatch must only reuse native active buff callbacks, retaining the original lifecycle")
end
local exactCounters = 0
for id,target in pairs(targets) do
    if id:match("^move:ContextualCounter%.") then
        exactCounters = exactCounters + 1
        assert(target.activate_by_class and target.ability_class and not target.entry_point)
        local state,_,step = fixture(target)
        step(0,true)
        assert(state.activations == 1 and state.entries == 0,
            "A compiled contextual counter must select its exact normally granted class")
        local missing,_,missingStep = fixture(target)
        missing.ability.Ability.class = "another-class"
        missingStep(0,true)
        assert(missing.activations == 0, "A compiled counter must never create a missing contextual grant")
        local rejected,_,rejectedStep = fixture(target)
        rejected.reject_activation = true
        rejectedStep(0,true)
        assert(rejected.activations == 0, "A compiled counter must preserve original frame qualification refusal")
    end
end
assert(exactCounters == 31)
local naturalCharge = assert(targets["move:Ability.Action.Hun.Heavy.FengLeiJian.BHold.Release"])
assert(naturalCharge.completion_timeline == "End_Success" and not naturalCharge.entry_point
    and naturalCharge.prepare_input == "/PBZInput/InputActions/IA_AttackH_Hold.IA_AttackH_Hold",
    "Resource accumulation must remain in the original held ability, without a forced release callback")
for _, outcome in ipairs({"success","complete","reset","ended","failed"}) do
    local state,backend,step = fixture(naturalCharge)
    step(0,true)
    assert(state.starts == 1 and state.activations == 0)
    state.holding = true
    step(.1,true)
    step(.2,false)
    assert(state.activations == 1 and state.stops == 0 and state.entries == 0,
        "A requested full charge must keep the original Hold active during native resource accumulation")
    if outcome == "success" then
        state.task.Timeline = state.namedTimeline
        Moves.reset(backend)
        assert(state.stops == 1 and not state.canceled and state.instance.bIsActive,
            "Reset after native success must release virtual Hold and preserve the attack")
    elseif outcome == "complete" then
        state.task.Timeline = state.namedTimeline
        Moves.update(backend)
        assert(state.stops == 1 and not state.canceled and state.instance.bIsActive,
            "Natural success must finish preparation without altering the original attack")
    elseif outcome == "reset" then
        Moves.reset(backend)
        assert(state.stops == 1 and state.canceled, "Reset while accumulating must cancel the pending charge")
    else
        if outcome == "failed" then
            state.task.Timeline = object(31) -- Original End_Fail is distinct from the success timeline.
            Moves.update(backend)
            assert(state.stops == 0 and state.entries == 0 and not state.canceled,
                "Native full-energy refusal must retain its own failure motion without forcing success")
        end
        state.instance.bIsActive,state.ability.ActiveCount = false,0
        Moves.update(backend)
        assert(state.stops == 1 and not state.canceled,
            "Native resource exhaustion or ability end must release virtual Hold without replaying a phase")
    end
    assert(not next(backend.complete_move_pending or {}))
end
do
    local target = assert(targets["move:Ability.Action.Hun.Light.SlitheringSword.DA.End"])
    assert(target.release_input_before_entry and target.entry_when.timeline == "Loop")
    local state,backend,step = fixture(target)
    for _, tag in ipairs(target.activation_required_tags) do state.tags[tag] = 1 end
    step(0,true)
    state.holding = true
    step(.1,true)
    Moves.update(backend)
    assert(state.stops == 1 and state.entries == 0)
    state.holding = false
    Moves.update(backend)
    assert(state.entries == 1 and state.arguments[1] == 0 and not state.canceled,
        "The sword-blossom release must wait for original preparation and real input release")
end
for _, device in ipairs({"keyboard","pad"}) do
    for _, trigger in ipairs({"default","press","tap","release"}) do
        local multiPath = path:gsub("phase%-moves%.lua$", "multi-" .. device .. "-" .. trigger .. ".lua")
        local multi = assert(loadfile(multiPath))()
        local states, backends = {}, {}
        local dispatch = {}
        for id, target in pairs(multi.targets) do
            local state, backend = fixture(target)
            states[#states+1], backends[id] = state, backend
        end
        function dispatch:dispatch(target, phase, value, binding)
            for id, candidate in pairs(multi.targets) do
                if candidate == target then return Backend.dispatch(backends[id],target,phase,value,binding) end
            end
            error("Unexpected multi-select target")
        end
        local router = Router.new(multi,dispatch)
        local key = device == "pad" and "Gamepad_DPad_Down" or "G"
        router:step(0,{[key]=1})
        router:step(.05,{[key]=1})
        router:step(.1,{[key]=1})
        router:step(.15,{})
        router:step(.2,{})
        assert(#states == 2)
        for _, state in ipairs(states) do
            assert(state.activations == ((trigger == "default" or trigger == "press") and 3 or 1),
                "Compiled multi-select must repeat default/on-press moves and preserve tap/release: " .. device .. " " .. trigger)
        end
    end
end
for _, device in ipairs({"keyboard","pad"}) do
    local mixed = assert(loadfile("dist/trigger-contract-check/" .. device .. "-mixed.lua"))()
    local backends, states, dispatch = {}, {}, {}
    for id, target in pairs(mixed.targets) do
        states[id], backends[id] = fixture(target)
    end
    function dispatch:dispatch(target, phase, value, binding)
        for id, candidate in pairs(mixed.targets) do
            if candidate == target then
                local ok = Backend.dispatch(backends[id],target,phase,value,binding)
                assert(not backends[id].dispatch_error, backends[id].dispatch_error)
                return ok
            end
        end
    end
    local router = Router.new(mixed,dispatch)
    local key = device == "pad" and "Gamepad_DPad_Down" or "G"
    for _, frame in ipairs({{0,{[key]=1}},{.05,{[key]=1}},{.1,{[key]=1}},{.15,{}},{.2,{}}}) do
        router:step(frame[1],frame[2])
    end
    for id, target in pairs(mixed.targets) do
        assert(states[id].activations == 3,
            "Mixed defaults must keep moves and abilities continuous through the real dispatcher")
    end
end
print("CompiledPhaseMoveTests PASS: " .. preparations .. " prepared entrances on both devices; native readiness, refusal, reset, expiry; 17 finishers and multi-select triggers")
