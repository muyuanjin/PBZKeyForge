package.loaded["script-context"] = {new=function() error("Unexpected tuning in an untuned fixture") end}
local scripts = "prototype/input-router/ue4ss/Scripts/"
package.path = scripts .. "?.lua;" .. package.path
local Backend = dofile(scripts .. "backend.lua")
local passed = 0

local function check(value, message)
    assert(value, message)
    passed = passed + 1
end

local function object(address, path, properties)
    local value = properties or {}
    value.IsValid = function() return true end
    value.GetClass = function() return "test-class" end
    value.GetAddress = function() return address end
    value.GetFullName = function() return "InputMappingContext " .. path end
    return value
end

function FName(value) return {ToString = function() return value end} end

local function array(values)
    return setmetatable({GetArrayNum=function() return #values end, ForEach = function(self, callback)
        for index, value in ipairs(values) do callback(index, {get = function() return value end}) end
    end}, {__index=values})
end

local function fixture()
    local contexts, objects, calls = {}, {}, {add = 0, remove = 0, release = 0, clones = 0, samples = {}}
    local nextAddress = 1000
    local backend = Backend.new({targets = {}, bindings = {}, require_foreground = false}, function() end)
    backend.physical_input = {sample = function() return nil end}
    backend.subsystem = object(1, "subsystem")
    backend.controller = object(2, "controller")
    backend.state = {ready = true}
    local input = object(3, "input")
    backend.controller.PlayerInput = input
    local contextMap = {ForEach = function(self, callback)
        for path, data in pairs(contexts) do
            local context, current = objects[path], data
            callback({get = function() return context end}, {get = function() return current end})
        end
    end}
    function input:GetPropertyValue(name)
        if name == "AppliedInputContextData" then return contextMap end
        local mappings = {}
        for path in pairs(contexts) do
            if not objects[path].unbound then
                for _, mapping in ipairs(objects[path].mapping_values) do mappings[#mappings + 1] = mapping end
            end
        end
        return array(mappings)
    end
    function backend:object(path)
        assert(objects[path], "missing object " .. path)
        return objects[path]
    end
    function backend:invoke(receiver, path, context, parameter)
        if path == "/Script/Engine.Controller:IsMoveInputIgnored" then
            assert(receiver == self.controller, "Move-input suppression must be queried on the local controller")
            calls.move_queries = (calls.move_queries or 0) + 1
            return calls.move_ignored == true
        elseif path == "/Script/Engine.Controller:K2_GetPawn" then
            return self.pawn
        elseif path == "/Script/Engine.GameplayStatics:IsGamePaused" then
            return calls.paused == true
        elseif path:find(":GetInputAnalogKeyState", 1, true) then
            local key = context.KeyName:ToString()
            calls.samples[key] = (calls.samples[key] or 0) + 1
            return (calls.physical or {})[key] or 0
        elseif path:find(":IsInputKeyDown", 1, true) then
            return (calls.digital or {})[context.KeyName:ToString()] == true
        end
        local contextPath = context:GetFullName():gsub("^%S+ ", "")
        if path:find(":HasMappingContext", 1, true) then
            local current = contexts[contextPath]
            parameter.OutFoundPriority = current and current.Priority or -1
            return current ~= nil
        elseif path:find(":RemoveMappingContext", 1, true) then
            calls.remove = calls.remove + 1
            local current = assert(contexts[contextPath])
            current.RegistrationCount = current.RegistrationCount - 1
            if current.RegistrationCount == 0 then contexts[contextPath] = nil end
        elseif path:find(":AddMappingContext", 1, true) then
            calls.add = calls.add + 1
            if calls.fail_add == calls.add then error("AddMappingContext failed") end
            local current = contexts[contextPath]
            if current then current.RegistrationCount = current.RegistrationCount + 1
            else contexts[contextPath] = {Priority = parameter, RegistrationCount = 1} end
        else error("Unexpected function " .. path) end
    end
    function backend:hold_objects() self.retained = true end
    function backend:hold_contexts() self.contexts_retained = true end
    function backend:release_holder() self.retained = false; calls.release = calls.release + 1 end
    function backend:key_dimension() return 1 end
    function StaticConstructObject(class, outer, _, _, _, copyTransients, copyArchetype, template)
        assert(class == "test-class" and outer == backend.subsystem, "Independent pipeline clone needs the native class and subsystem")
        assert(copyTransients == false and copyArchetype == false and template, "Independent pipeline clone needs its source template without copying transient state")
        local properties = {}
        for key, value in pairs(template) do if type(value) ~= "function" then properties[key] = value end end
        nextAddress = nextAddress + 1
        calls.clones = calls.clones + 1
        return object(nextAddress, "/Clone/" .. calls.clones, properties)
    end
    local function add(path, priority, registrations, key)
        nextAddress = nextAddress + 3
        local context = object(nextAddress, path)
        local action = object(nextAddress + 1, path .. ".Action")
        local mappingAddress = nextAddress + 2
        local mapping = {Action = action, Key = {KeyName = FName(key or "F24")},
            Triggers = array({}), Modifiers = array({}),
            GetStructAddress = function() return mappingAddress end}
        context.mapping_values = {mapping}
        context.DefaultKeyMappings = {Mappings = array(context.mapping_values)}
        objects[path] = context
        objects[path .. ".Action"] = action
        contexts[path] = {Priority = priority, RegistrationCount = registrations or 1}
        return context, action
    end
    return backend, contexts, calls, add
end

do
    local backend, contexts, calls, add = fixture()
    add("/Test/A.A", 42)
    backend.config.capture_contexts = {{path = "/Test/A.A"}, {path = "/Test/B.B"}}
    add("/Test/B.B", -3)
    contexts["/Test/B.B"] = nil
    backend:take_capture()
    backend:take_capture()
    check(calls.remove == 1, "Capture repeated removal of an inactive context")
    backend:shutdown()
    check(calls.add == 1 and contexts["/Test/A.A"].Priority == 42, "Original priority was not restored")
    check(contexts["/Test/B.B"] == nil, "Shutdown enabled a context that was originally inactive")
end

do
    local backend, contexts, calls, add = fixture()
    add("/Test/A.A", 17, 3)
    backend.config.capture_contexts = {{path = "/Test/A.A", registration_count = 3}}
    backend:take_capture()
    check(contexts["/Test/A.A"] == nil and calls.remove == 3, "Registration references were not captured")
    calls.fail_add = 2
    local restored = pcall(function() backend:shutdown() end)
    check(not restored and backend.captured["/Test/A.A"].removals == 2,
        "Partial restore did not retain the remaining work")
    check(backend.captured["/Test/A.A"] ~= nil and calls.release == 0,
        "Partial restore dropped context retention")
    calls.fail_add = nil
    backend:shutdown()
    check(contexts["/Test/A.A"].RegistrationCount == 3, "Restore retry duplicated completed registrations")
    check(contexts["/Test/A.A"].Priority == 17, "Restore retry lost priority")
end

do
    local backend,contexts,calls,add=fixture()
    add("/Test/A.A",23)
    backend.config.capture_contexts={{path="/Test/A.A"}}
    backend:take_capture()
    local complete=require("complete-moves")
    local original=complete.reset
    local fail=true
    complete.reset=function() if fail then error("move cancellation rejected") end end
    local tuningReset,abilitiesRestored,inputsRestored,releases=0,0,0,0
    backend.resource_costs={reset=function()
        tuningReset=tuningReset+1
        if fail then error("timeline restoration rejected") end
    end}
    backend.native_abilities={restore=function()
        abilitiesRestored=abilitiesRestored+1
        if fail then error("ability restoration rejected") end
    end}
    backend.native_input={restore=function() inputsRestored=inputsRestored+1 end}
    backend.on_release=function() releases=releases+1 end
    local ok,message=pcall(backend.shutdown,backend)
    complete.reset=original
    check(not ok and message:find("move cancellation rejected",1,true)
        and message:find("timeline restoration rejected",1,true) and message:find("ability restoration rejected",1,true),
        "Shutdown must report all independent release failures")
    check(tuningReset==1 and inputsRestored==1 and abilitiesRestored==1 and releases==1,
        "A failed cancellation or tuning reset must not skip the remaining cleanup")
    check(contexts["/Test/A.A"].Priority==23 and calls.add==1 and not next(backend.captured),
        "Shutdown must restore captured native contexts despite other cleanup failures")
    check(calls.release==0,"Incomplete cleanup retains the dynamic pipeline holder")
    fail=false
    backend:shutdown()
    check(calls.add==1 and calls.release==1 and not backend.resource_costs,
        "Retry finishes cleanup without duplicating restored native contexts")
end

do
    for _,closed in ipairs({"costs","context","neither"}) do
        local backend=fixture()
        local resets,suspends=0,0
        local costs={closed=closed=="costs",context={closed=closed=="context"},
            reset=function() resets=resets+1 end,suspend=function() suspends=suspends+1 end}
        backend.resource_costs=costs
        backend:shutdown(true)
        if closed=="neither" then
            check(suspends==1 and resets==0 and backend.resource_costs==costs,
                "Configuration reload retains a healthy tuning context and launched snapshots")
        else
            check(resets==1 and suspends==0 and not backend.resource_costs,
                "Configuration reload discards a closed tuning or native context so requests can recreate it")
        end
    end
    local backend=fixture()
    local costs={closed=true,reset=function() error("pending tuning cleanup") end}
    backend.resource_costs=costs
    check(not pcall(backend.shutdown,backend,true) and backend.resource_costs==costs,
        "Reload cannot discard a closed context when its cleanup still fails")
end

do
    local backend, contexts, calls, add = fixture()
    local context, action = add("/Test/A.A", 7, 2, "E")
    backend:prepare_dynamic_capture()
    local generation = backend.config_generation
    check(#backend:effective_config().bindings == 0 and next(backend:effective_config().targets) == nil,
        "An empty configuration must not replay native actions as routed bindings")
    check(context.mapping_values[1].Key.KeyName:ToString() == "E" and context.mapping_values[1].Action == action,
        "An empty configuration changed the native E mapping")
    check(not backend.retained and #backend.dynamic_contexts == 0,
        "An empty overlay must not retain synthetic action pipelines or capture native contexts")
    backend:take_capture()
    backend:prepare_dynamic_capture()
    check(backend.config_generation == generation and calls.add == 0 and calls.remove == 0,
        "Unchanged overlay rebuilt mappings or removed native contexts")
    add("/Test/B.B", 11)
    backend:prepare_dynamic_capture()
    check(backend.config_generation == generation and #backend:effective_config().bindings == 0,
        "A newly applied native context must not become a replay baseline")
    check(contexts["/Test/A.A"].RegistrationCount == 2 and contexts["/Test/B.B"].RegistrationCount == 1,
        "Preparing an overlay changed native context registrations")
    backend:take_capture()
    backend:shutdown()
    check(contexts["/Test/A.A"].Priority == 7 and contexts["/Test/B.B"].Priority == 11 and calls.add == 0 and calls.remove == 0,
        "Overlay shutdown unnecessarily replaced native contexts")
end

do
    local backend = Backend.new({require_foreground = false}, function() end)
    backend.state = {ready = true}
    backend.pawn = object(73, "pawn")
    local component = object(74, "component")
    function backend:object(path) return path end
    function backend:invoke(receiver, path, class)
        check(receiver == self.pawn and path == "/Script/Engine.Actor:GetComponentByClass",
            "Ability receiver must resolve the concrete component")
        check(class == "/Script/SGameInputSystem.AbilityInputComponent", "Incorrect component class")
        return component
    end
    check(backend:receiver({receiver="ability_input"}) == component, "Ability component was not resolved")
    check(backend:context().pawn == backend.pawn, "Custom predicates cannot inspect the local pawn")
    check(backend:receiver({receiver=function(b) return b.pawn end}) == backend.pawn,
        "Custom receiver was not evaluated")
    local ok, result = backend:dispatch({kind="lua", run=function(b, event)
        return event.binding == "callback" and event.value.x == 2 and event.state.pawn == b.pawn
    end}, "triggered", {x=2}, "callback")
    check(ok and result, "Lua target did not receive the binding context")
    check(backend:argument(function(b, event) return event.phase end, {phase="triggered"}) == "triggered",
        "Dynamic argument was not evaluated")
    local resolved = backend:argument({component={object="/Test/Component"}, values={{object="/Test/Item"}, false, 3}}, {})
    check(resolved.component == "/Test/Component" and resolved.values[1] == "/Test/Item" and resolved.values[2] == false and resolved.values[3] == 3,
        "Nested object return descriptors or primitive fields were not preserved")
end

do
    local backend,contexts,calls,add=fixture()
    local context,action=add("/Test/A.A",9)
    backend.config.suppressions={{action="/Test/A.A.Action",key="F24"}}
    backend:prepare_dynamic_capture()
    check(#backend:effective_config().bindings==0 and context.mapping_values[1].Key.KeyName:ToString()=="None",
        "Selected key/action must be masked in the native mapping rather than replayed")
    check(contexts["/Test/A.A"]~=nil and calls.remove==0,"Masking a native relation removed its context")
    backend:shutdown()
    check(context.mapping_values[1].Key.KeyName:ToString()=="F24","Shutdown did not restore the masked native key")
    backend.config.suppressions={{action="/Test/A.A.Action",key="Other"}}
    backend:prepare_dynamic_capture()
    check(#backend:effective_config().bindings==0 and context.mapping_values[1].Key.KeyName:ToString()=="F24",
        "A suppression for another key changed the native mapping")
    backend:shutdown()
    backend.config.suppressions={{action="/NotLoaded/IA_Future"}}
    backend:prepare_dynamic_capture()
    check(#backend:effective_config().bindings==0 and context.mapping_values[1].Key.KeyName:ToString()=="F24",
        "A suppression for an unloaded action changed the native mapping")
    backend:shutdown()
    backend.config.suppressions={{action="/Test/A.A.Action"}}
    backend:prepare_dynamic_capture()
    check(#backend:effective_config().bindings==0 and context.mapping_values[1].Key.KeyName:ToString()=="None",
        "An action suppression must mask its native keys")
    backend:shutdown()
    check(context.mapping_values[1].Key.KeyName:ToString()=="F24","Action-wide mask was not restored")
end

do
    local backend,contexts,calls,add=fixture()
    local selected=add("/Test/A.A",9)
    local other=add("/Test/B.B",10)
    backend.config.suppressions={{action="/Test/A.A.Action",key="F24"}}
    backend:prepare_dynamic_capture()
    local effective=backend:effective_config()
    check(#effective.bindings==0,"A suppression must not create a native replay binding")
    check(selected.mapping_values[1].Key.KeyName:ToString()=="None" and other.mapping_values[1].Key.KeyName:ToString()=="F24",
        "Replacing one action must preserve another action on the same physical key")
    backend:shutdown()
    check(selected.mapping_values[1].Key.KeyName:ToString()=="F24" and calls.add==0 and calls.remove==0,
        "Native relation restore must not replace contexts")
end

do
    local backend,contexts,calls,add=fixture()
    add("/Test/A.A",12)
    backend.config.capture_contexts={{path="/Test/A.A"}}
    backend:take_capture()
    local subsystem=backend.subsystem
    backend.subsystem=nil
    check(not pcall(function() backend:shutdown() end),"Unavailable subsystem must not report restoration success")
    check(next(backend.captured)~=nil,"Unavailable subsystem must retain restoration work")
    backend.subsystem=subsystem
    backend:shutdown()
    check(contexts["/Test/A.A"]~=nil,"Restoration must remain retryable")
end

do
    local backend,contexts,calls,add=fixture()
    local context,action=add("/Test/A.A",9)
    local modifier=object(701,"modifier",{State=13})
    local trigger=object(702,"trigger",{State=27})
    context.mapping_values[1].Modifiers=array({modifier})
    context.mapping_values[1].Triggers=array({trigger})
    context.mapping_values[1].SettingBehavior=1
    context.mapping_values[1].PlayerMappableKeySettings=object(703,"settings",{Name=FName("IA_Guard")})
    backend.config.targets.guard={kind="mapping",mapping="IA_Guard",actions={"/Test/A.A.Action"},device="keyboard"}
    backend.config.bindings={{id="new-key",keys={"J"},trigger="down",outputs={"guard"}}}
    backend.config.suppressions={{action="/Test/A.A.Action",key="F24"}}
    backend:prepare_dynamic_capture()
    local effective=backend:effective_config()
    check(#effective.bindings==1,"Replacing a source key must retain the cloned mapping")
    local cloned=effective.targets[effective.bindings[1].outputs[1].target]
    check(cloned.kind=="action" and cloned.object==action,"Mapping output did not expand into its captured native action")
    check(cloned.modifier_objects[1]~=modifier and cloned.trigger_objects[1]~=trigger and calls.clones==2,
        "Additional mapping must use independent native modifier and trigger instances")
    check(cloned.modifier_objects[1].State==13 and cloned.trigger_objects[1].State==27,
        "Mapping expansion lost native pipeline configuration")
    cloned.trigger_objects[1].State=99
    check(trigger.State==27,"An additional binding changed its native trigger instance")
    local generation=backend.config_generation
    backend:prepare_dynamic_capture()
    check(backend.config_generation==generation and calls.clones==2,
        "An unchanged mapping unnecessarily cloned its pipeline again")
    check(context.mapping_values[1].Key.KeyName:ToString()=="None" and calls.remove==0,
        "Mapping expansion must retain its source context while masking the selected relation")
    backend:shutdown()
    context.unbound=true
    backend:prepare_dynamic_capture()
    effective=backend:effective_config()
    check(#effective.bindings==1,"A fully unbound saved action must still accept a key enhancement binding")
    cloned=effective.targets[effective.bindings[1].outputs[1].target]
    check(cloned.trigger_objects[1]~=trigger and cloned.modifier_objects[1]~=modifier and
        cloned.trigger_objects[1].State==27 and cloned.modifier_objects[1].State==13,
        "An unbound action must clone the active context's original pipeline")
    backend:shutdown()
    check(contexts["/Test/A.A"].Priority==9 and calls.remove==0 and calls.add==0,
        "Mapping overlay shutdown changed the original context")
end

do
    local backend,contexts,calls,add=fixture()
    local context=add("/Test/A.A",9,1,"E")
    backend:prepare_dynamic_capture()
    calls.physical={E=1,J=1}
    backend:reserve_inputs({E=true},{E=1})
    check(context.mapping_values[1].Key.KeyName:ToString()=="None","Consumed physical key was not masked")
    local snapshot=backend:snapshot({"J"})
    check(snapshot.J==1 and snapshot.E==1 and calls.samples.E==1,
        "Consumed keys absent from router keys must still be physically sampled")
    backend:reserve_inputs({},snapshot)
    check(context.mapping_values[1].Key.KeyName:ToString()=="None","Consumption ended before the physical key was released")
    calls.physical.E=0
    snapshot=backend:snapshot({"J"})
    check(snapshot.E==0 and calls.samples.E==2,"Consumed physical key release was not sampled")
    backend:reserve_inputs({},snapshot)
    check(context.mapping_values[1].Key.KeyName:ToString()=="E" and next(backend.native_input.consumed)==nil,
        "Released consumed key did not restore its native mapping")
    calls.samples={}
    backend:snapshot({"J"})
    check(calls.samples.E==nil,"Released unrequested keys must stop being sampled")
    backend:shutdown()
    check(contexts["/Test/A.A"]~=nil and calls.remove==0 and calls.add==0,"Consumption replaced a native context")
end

do
    local backend, _, calls, add = fixture()
    local context = add("/Test/E.E", 9, 1, "E")
    local trigger = {TriggerTag = {TagName = FName("Ability.Action.Hun.SpecialDash")},
        GetStructAddress = function() return 902 end}
    local data = {Actions = array({trigger}), Combos = array({}), InterruptLower = array({})}
    local native = object(903, "/Test/Binding", {Bindings = array({
        {NormalData = data, TagConditionData = array({})}})})
    function FindAllOf(name)
        assert(name == "InputActionBinding")
        return {native}
    end
    backend.config.ability_suppressions = {"Ability.Action.Hun.SpecialDash"}
    backend:prepare_dynamic_capture()
    check(trigger.TriggerTag.TagName:ToString() == "None", "Native ability trigger was not suppressed")
    backend.config.ability_suppressions = {}
    backend:prepare_dynamic_capture()
    check(trigger.TriggerTag.TagName:ToString() == "Ability.Action.Hun.SpecialDash",
        "Clearing native ability suppressions in place did not restore the native trigger")
    check(context.mapping_values[1].Key.KeyName:ToString() == "E" and calls.remove == 0 and calls.add == 0,
        "Disabling a native ability changed an unrelated execution key or context")
    backend:shutdown()
end

do
    local backend, _, calls = fixture()
    calls.physical = {W = 0, V = 1, Gamepad_LeftX = .6, MouseX = -2}
    calls.digital = {V = true}
    local held = {W = 1, V = 0}
    backend.physical_input = {sample = function(_, key) return held[key] end}
    local snapshot = backend:snapshot({"W", "V", "Gamepad_LeftX", "MouseX"})
    check(snapshot.W == 1 and calls.samples.W == nil,
        "Engine FlushPressedKeys must not clear a physically held keyboard key")
    check(snapshot.V == 0 and calls.samples.V == nil,
        "A physically released key must not fall back to a stale engine down state")
    check(snapshot.Gamepad_LeftX == .6 and snapshot.MouseX == -2,
        "Unsupported physical probes must preserve native analog input")
    held.W = 0
    snapshot = backend:snapshot({"W"})
    check(snapshot.W == 0, "Independent physical input must observe release without latching movement")
end

do
    local backend, _, calls = fixture()
    backend.pawn = object(1200, "/Test/Pawn")
    local library = object(1201, "/Test/GameplayStatics")
    function backend:library() return library end
    backend.physical_input = {sample = function(_, key) return key == "W" and 1 or 0 end}
    calls.move_ignored = true
    check(backend:refresh() and backend:context().move_ignored == true and calls.move_queries == 1,
        "Backend refresh did not expose the controller's current movement suppression")
    check(backend:context().gameplay == true,
        "Movement suppression must not suspend unrelated gameplay inputs")
    check(backend:snapshot({"W"}).W == 1,
        "Movement suppression must preserve physically held movement keys")
    calls.move_ignored = false
    check(backend:refresh() and backend:context().move_ignored == false and calls.move_queries == 2,
        "Movement suppression remained latched after the controller allowed movement again")
    check(backend:context().gameplay == true and backend:snapshot({"W"}).W == 1,
        "Clearing movement suppression must keep gameplay and the held physical input available")
end

do
    local backend = Backend.new({require_foreground = false}, function() end)
    local path = "/Game/Input/IA_Test"
    local action = object(1300, path .. ".IA_Test")
    local package = object(1301, path)
    function StaticFindObject(query)
        if query == path then return package end
        if query == path .. ".IA_Test" then return action end
    end
    check(backend:object(path, "InputAction") == action,
        "A package path resolved to the package instead of its InputAction")
    check(backend:object(path .. ".IA_Test", "InputAction") == action,
        "An explicit object path changed during resource resolution")
    backend.objects = {}
    function StaticFindObject() return nil end
    function FindObject(class, short)
        assert(class == "InputAction" and short == "IA_Test")
        return action
    end
    check(backend:object(path) == action, "Resource lookup lost its class/basename fallback")
end

do
    local backend = Backend.new({require_foreground=false},function() end)
    local path="/Game/Maps/Test/Map.Test:PersistentLevel.WorldActor"
    local first,second,calls=object(1700,path),object(1701,path),0
    function StaticFindObject(query)
        if query==path then calls=calls+1;return calls==1 and first or second end
    end
    check(backend:object(path)==first and backend:object(path)==second and calls==2 and backend.objects[path]==nil,
        "world-owned receiver path was cached across generations")
end

do
    local backend,_,_,calls=fixture()
    local queries=0
    function backend:key_dimension(key) return key=="Stick" and 2 or 1 end
    function backend:invoke(_,path)
        assert(path:find(":GetInputVectorKeyState",1,true))
        queries=queries+1;return {X=-.6,Y=.8,Z=0}
    end
    local sample=backend:snapshot({"Stick","Stick@1","Stick@2"})
    check(queries==1 and sample.Stick==.8 and sample["Stick@1"]==-.6 and sample["Stick@2"]==.8,
        "one vector input was sampled repeatedly or lost signed components")
    backend:snapshot({"Stick@2","Stick@1","Stick"})
    check(queries==2, "vector sample cache escaped its physical frame")
end

do
    local backend = fixture()
    local function failing_move() error("native move invocation failed") end
    local ok = backend:dispatch({kind="lua",run=failing_move}, "triggered", nil, "move-binding")
    check(not ok and backend.dispatch_error:find("move-binding",1,true)
        and backend.dispatch_error:find("native move invocation failed",1,true)
        and backend.dispatch_error:find("stack traceback:",1,true)
        and backend.dispatch_error:find("BackendTests.lua",1,true),
        "Synchronous dispatch failures must retain the original call stack for copying: " .. tostring(backend.dispatch_error))
end

do
    local backend = fixture()
    backend.pawn = object(1400, "/Test/PawnOne")
    function backend:library() return object(1402, "/Test/GameplayStatics") end
    function backend:input_subsystem() return self.subsystem end
    check(backend:refresh(), "player session failed to initialize from a valid pawn")
    local first, generation = backend.session, backend.generation
    check(first and first.pawn == backend.pawn and backend:context().generation == generation,
        "fresh player session did not own the current pawn generation")
    local invoke, pawn_queries = backend.invoke, 0
    function backend:invoke(receiver,path,...)
        if path == "/Script/Engine.Controller:K2_GetPawn" then pawn_queries = pawn_queries + 1 end
        return invoke(self,receiver,path,...)
    end
    check(backend:refresh() and pawn_queries == 1,
        "stable refresh queried the Pawn more than once")
    local find_all, find_all_calls = FindAllOf, 0
    FindAllOf = function(...) find_all_calls = find_all_calls + 1; return find_all(...) end
    check(backend:dispatch({kind="lua",run=function() return true end},"triggered",nil,"hot"),"generation guard rejected a live dispatch")
    FindAllOf = find_all
    check(pawn_queries == 1 and find_all_calls == 0,
        "stable dispatch queried the Pawn or searched UObjects instead of comparing the session generation")
    backend.pawn = object(1401, "/Test/PawnTwo")
    check(backend:refresh(), "replacement pawn did not initialize a new session")
    check(first.invalidated and backend.session ~= first and backend.generation == generation + 1,
        "pawn replacement retained the previous session or generation")
    check(backend.session.pawn == backend.pawn and backend:context().pawn == backend.pawn,
        "new session did not resolve the replacement pawn")
end

do
    local backend = fixture()
    backend.pawn = object(1500, "/Test/PawnOne")
    function backend:library() return object(1501, "/Test/GameplayStatics") end
    function backend:input_subsystem() return self.subsystem end
    check(backend:refresh(), "session guard fixture failed to initialize")
    local first_generation = backend.generation
    backend.complete_move_pending = {stale = true}
    local discarded = 0
    backend.resource_costs = {discard = function() discarded = discarded + 1 end}
    local restored, native_input_discarded = 0, 0
    local retired = 0
    local cleanup = {}
    backend.ledge_movement = object(1503, "/Test/Movement")
    backend.ledge_walk_off = function(address)
        cleanup[#cleanup+1] = "ledge:" .. tostring(address)
        return 1
    end
    backend.move_preparation = {state = {long_buffs = 23,
        context = {discard_long_buffs = function(_, token)
            cleanup[#cleanup+1] = "buffs:" .. tostring(token)
        end}}}
    backend.stance_input = {reset = function(owner, held)
        cleanup[#cleanup+1] = "stance:" .. tostring(owner) .. ":" .. tostring(held)
        return 1
    end}
    backend.complete_move_inputs = {held = {generation = backend.generation,
        subsystem = object(1504, "/Test/InputSubsystem"), action = object(1505, "/Test/HoldAction")}}
    local world_calls = 0
    local stop_inputs = backend.invoke
    function backend:invoke(receiver, path, action)
        if path == "/Script/EnhancedInput.EnhancedInputSubsystemInterface:StopContinuousInputInjectionForAction" then
            world_calls = world_calls + 1
            return
        end
        return stop_inputs(self, receiver, path, action)
    end
    backend.on_retire = function() retired = retired + 1; cleanup[#cleanup+1] = "retire" end
    backend.native_input = {
        restore = function() error("Session invalidation must not restore through the old controller") end,
        discard = function() native_input_discarded = native_input_discarded + 1 end}
    backend.native_abilities = {restore = function() restored = restored + 1 end}
    backend.pawn = object(1502, "/Test/PawnTwo")
    local ran = false
    check(not backend:validate_session(), "frame guard accepted a replacement pawn")
    local ok = backend:dispatch({kind="lua",run=function() ran=true end}, "triggered", nil, "transition")
    check(not ok and not ran, "dispatch used state from the previous player generation")
    check(backend.generation == first_generation + 1 and backend.session == nil,
        "frame guard did not immediately rotate and discard the old session")
    check(backend:refresh() and backend.generation == first_generation + 2,
        "replacement pawn did not establish the next player session")
    check(restored == 1 and native_input_discarded == 1 and discarded == 1 and retired == 1 and world_calls == 0
        and not backend.complete_move_pending and not backend.complete_move_inputs
        and not backend.complete_move_active and not backend.move_preparation
        and not backend.stance_input and not backend.ledge_movement and not backend.resource_costs,
        "session rotation retained delayed move, tuning or native input state")
    check(table.concat(cleanup, ",") == "retire,ledge:0,buffs:23,stance:0:false",
        "session rotation did not discard native state before dropping old world objects: "
            .. table.concat(cleanup, ","))
    check(not backend.session.invalidated and backend.session.pawn == backend.pawn,
        "new generation did not replace the stale session")
end

do
    local backend = fixture()
    backend.pawn = object(1550, "/Test/PawnOne")
    function backend:library() return object(1551, "/Test/GameplayStatics") end
    function backend:input_subsystem() return self.subsystem end
    check(backend:refresh(), "dispatch transition fixture failed to initialize")
    local retired = 0
    backend.on_retire = function() retired = retired + 1 end
    local ok, result = backend:dispatch({kind="lua",run=function(active)
        active:invalidate_session()
        local invoked, failure = pcall(active.invoke,active,object(1552,"/Test/Stale"),"/Test.Stale:Call")
        check(not invoked and tostring(failure):find("Player session changed during native dispatch",1,true),
            "dispatch continued into a native call after its owning session was invalidated")
        return true, "must not reach cleanup"
    end}, "triggered", nil, "during-dispatch")
    check(ok and result == false and retired == 1 and backend.session == nil,
        "synchronous player replacement leaked through the active dispatch")
end

do
    local backend = fixture()
    local logs, accepted = {}, false
    backend.log = function(message) logs[#logs+1] = message end
    local receiver = object(700, "receiver")
    function backend:receiver() return receiver end
    function backend:object() return receiver end
    function backend:invoke(_, method)
        assert(method:match(":TryActivateExternalActionAbilityInterruptLowerByTag$"))
        return accepted
    end
    local target = {kind="ability",tag="Ability.Action.Test"}
    for attempt=1,60 do
        local ok,result,reason = backend:dispatch(target,"triggered",nil,"first")
        check(ok and result==false and reason:find(target.tag,1,true),
            "Native qualification refusal must retain a diagnostic without becoming a dispatch exception")
    end
    backend:dispatch(target,"triggered",nil,"second")
    check(#logs==2 and logs[1]:find("first",1,true) and logs[2]:find("second",1,true),
        "Repeated qualification refusal must log once per binding and reason")
    check(not backend:target_failed(target,"first") and backend.dispatch_error==nil,
        "A normal native refusal must not put a held input into error backoff")
    accepted=true
    local ok,result = backend:dispatch(target,"triggered",nil,"first")
    check(ok and result and #logs==2,"A held request must remain able to activate when its native qualification changes")
end

do
    local backend = Backend.new({require_foreground=false},function() end)
    backend.state = {ready=true,gameplay=true}
    local component, action = object(1600, "/Test/AbilityInput"), object(1601, "/Test/IA_Dodge")
    backend.pawn = object(1602, "/Test/Pawn")
    function backend:receiver()
        return component
    end
    local injection, direct, seen = nil, false, {}
    function backend:object(path)
        if path == "/PBZDeflectSystem/Input/IA_Dodge.IA_Dodge" then return action end
        return object(1603, path)
    end
    function backend:invoke(receiver, path, value, inputAction)
        if path == "/Script/Engine.Actor:GetComponentByClass" then return action end
        if path:find(":IsArmed",1,true) then return true end
        if path:find(":InjectInputVectorForAction",1,true) then
            injection = {receiver=receiver,action=value,path=path}
            return true
        end
        if path:find("AbilityInputComponent:",1,true) then
            seen.receiver, seen.tag, seen.input = receiver, value.TagName:ToString(), inputAction
            direct = receiver == component and seen.tag == "Ability.Action.Hun.SpecialDash"
                and inputAction == action
        end
        return true
    end
    local ok = backend:dispatch({kind="ability",tag="Ability.Action.Hun.SpecialDash"},"triggered",nil,"native-dash")
    check(ok and direct and not injection,
        "SpecialDash was replaced with the generic IA_Dodge action instead of activating its exact ability tag: ok="
            .. tostring(ok) .. " direct=" .. tostring(direct) .. " receiver=" .. tostring(seen.receiver)
            .. " tag=" .. tostring(seen.tag) .. " input=" .. tostring(seen.input == action) .. " injection=" .. tostring(injection ~= nil))
end

print("BackendTests: " .. passed .. " assertions passed")
