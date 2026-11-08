package.loaded["script-context"] = {new=function() error("Unexpected tuning in an untuned fixture") end}
local scripts = "prototype/input-router/ue4ss/Scripts/"
package.path = scripts .. "?.lua;" .. package.path
local Backend = dofile(scripts .. "backend.lua")
local Router = dofile(scripts .. "router.lua")
local directory = os.getenv("PBZ_SCHEME_FIXTURES") or "dist/catalog-input-pipelines"
local passed, abilities = 0, 0
local function check(value, message)
    assert(value, message)
    passed = passed + 1
end
function FName(value) return value end
function RegisterHook() return 1,2 end
function UnregisterHook() end
local function object()
    local result={IsValid=function() return true end}
    function result:GetAddress() return self end
    return result
end
local function rig(config)
    local state = {frame=0, requests=0, activations=0, ready=1, injections=0}
    local output=config.bindings[1].outputs[1]
    local target=config.targets[type(output)=="table" and output.target or output]
    local condition=target.native_release_when or {}
    local backend = Backend.new(config, function(message)
        if message:match("^Waiting for native Hold input:") then state.waits=(state.waits or 0)+1;return end
        assert(message:match("^Move not activated %[") ~= nil, message)
        state.rejections = (state.rejections or 0) + 1
    end)
    backend.state.ready = true
    local component, input, hold, class, cdo, instance, timeline, task, weapons = object(),object(),object(),object(),object(),object(),object(),object(),object()
    local function array(values) return setmetatable({GetArrayNum=function() return #values end},{__index=values}) end
    local classPath=target.native_preparation_class or target.ability_class or "/Fixture/Ability.Ability_C"
    function class:GetFullName() return "Class "..classPath end
    function StaticFindObject() return class end
    function cdo:GetClass() return class end
    function instance:GetClass() return class end
    function cdo:GetPropertyValue(name)
        if name=="AbilityTags" then return {GameplayTags=array({{TagName={ToString=function() return target.tag end}}})} end
    end
    function instance:GetPropertyValue(name)
        if name=="bIsActive" then return state.active end
        if name=="Variables" then return {ForEach=function(_,callback)
            if condition.object_variable then
                callback({get=function() return {ToString=function() return condition.object_variable.key end} end},
                    {get=function() return input end})
            end
        end} end
        return condition.property_minimum and condition.property_minimum[name] or 0
    end
    local spec=setmetatable({Ability=cdo,NonReplicatedInstances=array({instance}),ReplicatedInstances=array({})},
        {__index=function(_,name) if name=="ActiveCount" then return state.active and 1 or 0 end end})
    component.ActivatableAbilities={Items=array({spec})}
    function task:GetPropertyValue(name) return name=="Timeline" and timeline or condition.minimum_time or 0 end
    function input:GetFullName() return "Object "..(condition.object_variable and condition.object_variable.object or "/Fixture/Input.Input") end
    backend.pawn,backend.subsystem=object(),object()
    backend.resource_costs={context={start_hold_input=function(_,owner,heldAction)
        check(owner==backend.subsystem and heldAction==hold,"Compiled Hold must use the native value bridge with its exact action")
        state.native_requested=true;state.starts=(state.starts or 0)+1
    end},activate=function(_,_,_,callback) return callback() end,input=function() end}
    function backend:receiver(spec)
        assert(spec.receiver == "ability_input" or spec.receiver == "ability_system")
        return component
    end
    function backend:object(path)
        if path==target.input_action or path=="/PBZDeflectSystem/Input/IA_Dodge.IA_Dodge" then state.input = path end
        if path:match("_Hold%.") then return hold end
        return input
    end
    function backend:library() return input end
    function backend:key_dimension() return 1 end
    function backend:invoke(receiver, method, tag, action, priority)
        if method == "/Script/Engine.Actor:GetComponentByClass" then
            check(receiver==self.pawn,"Dodge preparation must use the current player's weapon component")
            return weapons
        elseif method == "/Script/SGameCombatSystem.CharacterWeaponsComponent:IsArmed" then
            check(receiver==weapons,"Dodge preparation must query the real weapon component")
            return state.armed==true
        elseif method == "/Script/SGameCombatSystem.CharacterWeaponsComponent:SetArmed" then
            check(receiver==weapons and action==true,"Dodge preparation must draw the weapon before activation")
            state.armed=true
            return
        end
        if method == "/Script/EnhancedInput.EnhancedInputSubsystemInterface:InjectInputVectorForAction" then
            state.injections = state.injections + 1
            return true
        end
        if method:match(":GetSGameInputComponent$") then return component
        elseif method:match(":IsHolding$") then return state.native_held==true
        elseif method:match(":StopContinuousInputInjectionForAction$") then state.native_requested=false;state.stops=(state.stops or 0)+1;return
        elseif method:match(":GetPlayTimelineTask$") then return task
        elseif method:match(":GetTimelineByName$") then return timeline
        elseif method:match(":GetGameplayTagCount$") then return 1
        elseif condition.object_variable and method==condition.object_variable.getter then return input
        elseif condition.object_variable and method==condition.object_variable.status_getter then return condition.object_variable.status end
        assert(receiver == component and method == "/Script/SGameInputSystem.AbilityInputComponent:TryActivateExternalActionAbilityInterruptLowerByTag",
            "unexpected native method during ability repetition: " .. method)
        state.requests = state.requests + 1
        check(state.last_request_frame~=state.frame,"One binding must not request the native ability twice in one scheduler frame")
        state.last_request_frame=state.frame
        if target.native_hold_inputs then check(state.native_held,"Native activation must wait for the compiled Hold input's engine update") end
        state.tag = tag.TagName
        check(action == input and priority == state.priority, "compiled ability input or priority changed")
        if state.frame < state.ready then state.refusals=(state.refusals or 0)+1;return false end
        state.activations, state.ready,state.active = state.activations + 1, state.frame + 8,true
        return true
    end
    local router = Router.new(config, backend)
    local function step(frame, snapshot)
        state.frame = frame
        state.native_held=state.native_requested
        if frame>=state.ready then state.active=false end
        router:step(frame / 60, snapshot, {})
    end
    return router, state, step
end

local zhanMa
for _, device in ipairs({"keyboard", "pad"}) do
    local compiled = dofile(directory .. "/" .. device .. "-abilities.lua")
    local defaultCount, explicitCount, tags = 0, 0, {}
    for _, binding in ipairs(compiled.bindings) do
        local output = binding.outputs[1]
        local target = compiled.targets[type(output) == "table" and output.target or output]
        check(target.kind == "ability", "catalog ability fixture changed output kind")
        check(binding.trigger == "press", "fixture must cover existing default press configurations")
        local config = {enabled=true, require_foreground=false, targets=compiled.targets, bindings={binding}}
        local router, state, step = rig(config)
        state.priority = target.priority
        local held = {[binding.keys[1]]=1}
        if binding.default_trigger then
            defaultCount = defaultCount + 1
            tags[target.tag] = true
            state.ready = 5
            for frame=1,32 do step(frame,held) end
            check((target.native_hold_inputs and state.requests<32 and state.requests>=4 and state.refusals>0
                and state.waits>0 or not target.native_hold_inputs and state.requests==32) and state.activations == 4,
                "default hold did not retry after native rejection and repeat after completion: " .. target.tag
                    .. " requests="..state.requests.." activations="..state.activations)
            if target.tag == "Ability.Action.Hun.Heavy.ZhanMa.DB" then zhanMa = config end
        else
            explicitCount = explicitCount + 1
            for frame=1,32 do step(frame,held) end
            check(state.requests == 1 and state.activations == 1,
                "explicit single press unexpectedly repeated: " .. target.tag)
        end
        check(state.tag == target.tag and state.input == (target.input_action or "/PBZDeflectSystem/Input/IA_Dodge.IA_Dodge"),
            "compiled native tag or input action changed: " .. target.tag)
        local requests = state.requests
        step(33,{})
        step(34,{})
        check(state.requests == requests, "release must stop ability requests: " .. target.tag)
        step(40,held)
        if target.native_hold_inputs then step(41,held) end
        check(state.activations == (binding.default_trigger and 5 or 2), "fresh press failed to rearm: " .. target.tag)
        router:reset()
        router:resume(held,{})
        step(42,held)
        step(80,held)
        check(state.requests == requests + 1, "resuming held ability replayed an attack: " .. target.tag)
        step(81,{})
        step(82,held)
        if target.native_hold_inputs then step(83,held) end
        check(state.requests == requests + 2, "resumed ability did not rearm after release: " .. target.tag)
        router:reset()
        if target.native_hold_inputs then check(state.starts==state.stops and not state.native_requested,
            "Compiled Hold must release its native input when the binding resets") end
    end
    check(defaultCount > 0 and defaultCount == explicitCount and tags["Ability.Action.Hun.Heavy.ZhanMa.DB"],
        "both trigger policies and ZhanMa charge slash must be covered on " .. device)
    abilities = abilities + defaultCount
end

assert(zhanMa, "missing ZhanMa charge slash regression")
local source = zhanMa.bindings[1]
for _, trigger in ipairs({"down", "hold", "toggle", "axis", "pulse", "tap", "release", "multi_tap", "sequence"}) do
    local binding = {}
    for key,value in pairs(source) do binding[key]=value end
    binding.trigger, binding.default_trigger, binding.seconds, binding.interval, binding.count = trigger, nil, .05, .1, 2
    binding.keys = {"K"}
    if trigger == "axis" then binding.axis, binding.keys = "K", {} end
    if trigger == "sequence" then binding.steps, binding.keys = {{"K"},{"J"}}, {} end
    local config = {enabled=true, require_foreground=false, targets=zhanMa.targets, bindings={binding}}
    local router,state,step = rig(config)
    local output = binding.outputs[1]
    state.priority = config.targets[output.target].priority
    for frame=1,32 do step(frame,{K=1}) end
    local expected = {down=32, hold=29, toggle=32, axis=32, pulse=6, tap=0, release=0, multi_tap=0, sequence=0}
    check(state.requests == expected[trigger], "ability trigger repeat policy changed: " .. trigger)
    step(33,{})
    local requests = state.requests
    step(34,{})
    if trigger == "toggle" then
        check(state.requests == requests + 1, "toggle must keep requesting until toggled off")
        step(35,{K=1})
        check(state.requests == requests + 1, "second press must turn off toggle repetition")
    else
        check(state.requests == requests, "release did not stop continuous requests: " .. trigger)
        if trigger == "release" then check(requests == 1, "release must request exactly once") end
        if trigger == "tap" then
            step(35,{K=1}); step(36,{})
            check(state.requests == 1, "short tap must request exactly once")
        elseif trigger == "sequence" then
            step(35,{K=1}); step(36,{J=1}); step(37,{J=1})
            check(state.requests == 1, "sequence completion must request exactly once")
        elseif trigger == "multi_tap" then
            step(35,{K=1}); step(36,{}); step(37,{K=1}); step(38,{K=1})
            check(state.requests == 1, "multi-tap completion must request exactly once")
        end
    end
    if trigger == "down" or trigger == "axis" then
        router:reset(); router:resume({K=1},{})
        requests = state.requests
        step(50,{K=1}); step(51,{K=1})
        check(state.requests == requests, "held continuous ability replayed after resume: " .. trigger)
        step(52,{}); step(53,{K=1})
        check(state.requests == requests + 1, "continuous ability failed to rearm after resume: " .. trigger)
    end
end

do
    local binding = {}
    for key,value in pairs(source) do binding[key]=value end
    local targets = {}
    for key,value in pairs(zhanMa.targets) do targets[key]=value end
    local once = 0
    targets.single = {kind="lua",run=function() once=once+1 end}
    targets.action = {kind="action",path="/Test/IA_Jump"}
    binding.outputs = {source.outputs[1], "single", "action"}
    local config = {enabled=true, require_foreground=false, targets=targets, bindings={binding}}
    local _,state,step = rig(config)
    state.priority = targets[source.outputs[1].target].priority
    for frame=1,32 do step(frame,{[binding.keys[1]]=1}) end
    check(state.activations == 4 and once == 1 and state.injections == 2,
        "default ability repetition must not repeat adjacent single outputs")
end
print("CompiledAbilityRepeatTests: " .. passed .. " assertions passed; " .. abilities .. " catalog abilities across both devices")
