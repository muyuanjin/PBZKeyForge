package.loaded["script-context"] = {new=function() error("Unexpected tuning in an untuned fixture") end}
package.loaded["live-config"] = {new=function() return {poll=function() end} end}
local scripts = "prototype/input-router/ue4ss/Scripts/"
package.path = scripts .. "?.lua;" .. package.path
local NativeInput = dofile(scripts .. "native-input.lua")
local Backend = dofile(scripts .. "backend.lua")
local Router = dofile(scripts .. "router.lua")
local passed, failures, nextAddress = 0, {}, 100
local stress_gc = true
local map_callback = false

local function check(value, message)
    assert(value, message)
    passed = passed + 1
end

local function test(name, run)
    local ok, message = pcall(run)
    stress_gc = true
    if not ok then failures[#failures + 1] = name .. ": " .. tostring(message) end
end

local function object(path, properties)
    nextAddress = nextAddress + 1
    local result = properties or {}
    result.ActuationThreshold = result.ActuationThreshold or .5
    local pointer = nextAddress
    function result:IsValid() return true end
    function result:GetAddress() return pointer end
    function result:GetFullName() return "Object " .. path end
    function result:GetClass()
        local trigger = path:match("^/Trigger%.([%a]+)$")
        return trigger and "InputTrigger" .. trigger or "test-class"
    end
    function result:SetPropertyValue(name, value) self[name] = value end
    return result
end

function FName(value) return {ToString = function() return value end} end

local function array(items, transform)
    return setmetatable({GetArrayNum = function() return #items end, ForEach = function(self, callback)
        -- UE4SS removes its receiver from the Lua stack before calling back.
        local receiver = setmetatable({self}, {__mode = "v"})
        self = nil
        for index, value in ipairs(items) do
            if stress_gc then collectgarbage("collect") end
            assert(receiver[1], "native array receiver collected during ForEach")
            local item = transform and transform(value) or value
            callback(index - 1, {get = function() return item end})
        end
    end}, {__index = function(_, index)
        assert(not map_callback, "array traversal inside a native map callback")
        assert(type(index) == "number" and index >= 1 and index <= #items, "native array read out of range")
        if stress_gc then collectgarbage("collect") end
        local value = items[index]
        return transform and transform(value) or value
    end})
end

local function mapping(action, key, triggers, modifiers)
    nextAddress = nextAddress + 1
    local result = {Action = action, Key = {KeyName = FName(key)}, alive = true,
        Triggers = array(triggers or {}), Modifiers = array(modifiers or {}), pointer = nextAddress}
    return result
end

local function wrapper(row)
    return setmetatable({}, {__index = function(_, name)
        assert(row.alive, "stale mapping wrapper accessed after native array rebuild")
        if name == "GetStructAddress" then return function() return row.pointer end end
        if name == "Modifiers" or name == "Triggers" then row.pipeline_reads = (row.pipeline_reads or 0) + 1 end
        return row[name]
    end})
end

local function fixture(config)
    config = config or {targets = {}, bindings = {}}
    config.suppressions = config.suppressions or {}
    config.targets, config.bindings = config.targets or {}, config.bindings or {}
    local state = {rows = {}, contexts = {}, physical = {}, calls = {}, now = 0, clones = 0}
    local backend = Backend.new(config, function() end)
    local function metadata(value)
        if value.SettingBehavior ~= nil then return value end
        local actionPath = value.Action:GetFullName():gsub("^%S+ ", ""):gsub("%.[^./]+$", "")
        local name = actionPath:match("/([^/]+)$")
        for _, override in ipairs(config.native_bindings or {}) do
            for _, action in ipairs(override.actions or {}) do
                if action:gsub("%.[^./]+$", "") == actionPath then name = override["function"] or name end
            end
        end
        for _, target in pairs(config.targets) do
            for _, action in ipairs(target.actions or {}) do
                if not target.catalog_pipeline and action:gsub("%.[^./]+$", "") == actionPath then name = target.mapping or name end
            end
        end
        value.SettingBehavior = 1
        value.PlayerMappableKeySettings = object("/Settings.Settings", {Name=FName(name)})
        return value
    end
    backend.physical_input = {sample = function() return nil end}
    backend.controller = object("/Controller.Controller")
    backend.subsystem = object("/Subsystem.Subsystem")
    backend.state = {ready = true, gameplay = true, generation = 1}
    local input = object("/PlayerInput.PlayerInput")
    backend.controller.PlayerInput = input
    function input:GetPropertyValue(name)
        if name == "EnhancedActionMappings" then return array(state.rows, function(value)
            state.mapping_reads = (state.mapping_reads or 0) + 1
            return wrapper(metadata(value))
        end) end
        assert(name == "AppliedInputContextData", "unexpected input property")
        return {ForEach = function(_, callback)
            for _, context in ipairs(state.contexts) do
                map_callback = true
                callback({get = function() return context end}, {get = function() return {} end})
                map_callback = false
            end
        end}
    end
    function StaticConstructObject(class, outer, _, _, _, copyTransients, copyArchetype, template)
        check((class == "test-class" or class:match("^InputTrigger") or class:match("^InputModifier")) and outer == backend.subsystem, "pipeline clone has wrong owner/class")
        check(copyTransients == false and copyArchetype == false and template ~= nil,
            "pipeline clone must use its native object template")
        local properties = {}
        for key, value in pairs(template) do if type(value) ~= "function" then properties[key] = value end end
        state.clones = state.clones + 1
        local clone=object("/Clone.Clone" .. state.clones, properties)
        function clone:GetClass() return class end
        return clone
    end
    function backend:hold_objects(triggers, modifiers)
        state.heldTriggers, state.heldModifiers = triggers, modifiers
    end
    function backend:refresh() self.native_input:prepare(); return true end
    function backend:time() state.now = state.now + .1; return state.now end
    function backend:key_dimension() return 1 end
    function backend:object(path)
        assert(path == "/Script/EnhancedInput.Default__InputTriggerDown", "unexpected object lookup " .. path)
        return {LastValue = 0}
    end
    function backend:invoke(receiver, method, key, value)
        if method == "/Script/EnhancedInput.InputTrigger:UpdateState" then receiver.LastValue = value; return 0 end
        assert(receiver == self.controller, "unexpected input receiver")
        local value = state.physical[key.KeyName:ToString()] or 0
        if method:find(":GetInputAnalogKeyState", 1, true) then return value end
        if method:find(":IsInputKeyDown", 1, true) then return value ~= 0 end
        error("unexpected native call " .. method)
    end
    function backend:dispatch(target, phase, value)
        state.calls[#state.calls + 1] = {target = target, phase = phase, value = value}
        return true
    end
    function backend:shutdown() self.native_input:restore() end
    function state:add_context(rows)
        local context = object("/Context.Context" .. (#self.contexts + 1))
        context.DefaultKeyMappings = setmetatable({}, {__index = function(_, name)
            assert(name == "Mappings")
            return array(rows, function(value)
                state.template_reads = (state.template_reads or 0) + 1
                return wrapper(metadata(value))
            end)
        end})
        self.contexts[#self.contexts + 1] = context
        return context
    end
    function state:rebuild(rows)
        for _, row in ipairs(self.rows) do row.alive = false end
        self.rows = rows
    end
    backend.native_input = NativeInput.new(backend)
    return backend, backend.native_input, state
end

local function key(row) return row.Key.KeyName:ToString() end
local function target_for(config, output)
    return config.targets[type(output) == "string" and output or output.target]
end

test("repeated capture recovery releases retired healthy rules", function()
    stress_gc=false
    local config={unified_native=true,require_foreground=false,targets={},bindings={},native_actions={}}
    local actions,retired={},setmetatable({},{__mode="v"})
    for index=1,64 do
        local path="/Game/Recovery_"..index
        actions[index]=object(path..".Recovery_"..index)
        config.native_actions[index]=path
    end
    local backend,native,state=fixture(config)
    local router
    for cycle=1,200 do
        backend:release_capture(true)
        local rows={}
        for index,action in ipairs(actions) do rows[index]=mapping(action,"K"..index) end
        state:rebuild(rows)
        native:prepare()
        router=Router.new(backend.dynamic_config,backend)
        router:step(cycle,{}, {})
        if cycle==1 then
            for index,rule in ipairs(router.rules) do retired[index]=rule.binding end
        end
    end
    collectgarbage("collect")
    check(#router.rules==64,"capture recovery lost current scheduler rules")
    check(next(retired)==nil,"failure tracking retained healthy rules from an earlier capture")
    check(not backend.failed_targets or next(backend.failed_targets)==nil,
        "healthy rules allocated failure records")
    backend:release_capture(true)
end)

test("unbound mappings use indexed reads outside native map callbacks", function()
    local action=object("/Game/UI.UI")
    local _,native,state=fixture({unified_native=true,native_bindings={
        {["function"]="UI",device="keyboard",keys={"J"},actions={"/Game/UI"}}
    }})
    state.rows={mapping(action,"None")}
    state:add_context({mapping(action,"Enter")})
    native:apply_masks()
    check(key(state.rows[1])=="J", "unbound mapping lost its registered keyboard identity")
    native:apply_masks()
    native:restore()
    check(key(state.rows[1])=="None", "repeated remapping lost an explicit unbinding")
end)

test("partial remapping preserves restoration metadata", function()
    local action=object("/Game/UI.UI")
    local _,native,state=fixture({unified_native=true,native_bindings={
        {["function"]="UI",device="keyboard",keys={"J"},actions={"/Game/UI"}}
    }})
    state.rows={mapping(action,"Enter"),mapping(action,"Tab")}
    state.rows[2].alive=false
    check(not pcall(native.apply_masks,native) and key(state.rows[1])=="J", "partial mapping failure was not exercised")
    state.rows[2].alive=true
    native:restore()
    check(key(state.rows[1])=="Enter" and key(state.rows[2])=="Tab", "partial failure lost the original native key")
end)

test("native contexts and unrelated keys remain intact", function()
    local backend, native, state = fixture({targets = {extra = {kind = "lua"}},
        bindings = {{id = "extra", keys = {"F9"}, outputs = {"extra"}}}})
    local action = object("/Game/IA_Jump.IA_Jump")
    state.rows = {mapping(action, "SpaceBar"), mapping(action, "Gamepad_FaceButton_Bottom")}
    local context = state:add_context(state.rows)
    native:prepare()
    check(#state.contexts == 1 and state.contexts[1] == context, "native context was removed or replaced")
    check(#backend.dynamic_contexts == 0, "native contexts must not be scheduled for capture")
    check(#backend.dynamic_config.bindings == 1 and backend.dynamic_config.bindings[1].id == "extra",
        "untouched native mappings were replayed as extra bindings")
    check(key(state.rows[1]) == "SpaceBar" and key(state.rows[2]) == "Gamepad_FaceButton_Bottom",
        "preparation changed an untouched native key")
    check(#state.calls == 0, "preparation injected a native action")
end)

test("suppression is scoped to one key and action", function()
    local jump, execute = object("/Game/IA_Jump.IA_Jump"), object("/Game/IA_Execute.IA_Execute")
    local backend, native, state = fixture({suppressions = {{action = "/Game/IA_Jump", key = "E"}}})
    state.rows = {mapping(jump, "E"), mapping(execute, "E"), mapping(jump, "SpaceBar")}
    native:prepare()
    check(key(state.rows[1]) == "None", "selected native key/action was not suppressed")
    check(key(state.rows[2]) == "E", "suppression changed another action sharing the physical key")
    check(key(state.rows[3]) == "SpaceBar", "suppression changed another key for the selected action")
    backend.config.suppressions = {}
    native:apply_masks({}, {})
    check(key(state.rows[1]) == "E", "removing suppression failed to restore the key")
    native:restore()
    check(key(state.rows[2]) == "E", "restore changed an untouched row")
end)

test("consume stays latched until physical release", function()
    local backend, native, state = fixture()
    local execute = object("/Game/IA_Execute.IA_Execute")
    state.rows = {mapping(execute, "E"), mapping(execute, "Gamepad_FaceButton_Top")}
    native:apply_masks({E = true}, {E = 1})
    check(key(state.rows[1]) == "None", "consumed key still reaches native execution")
    check(key(state.rows[2]) == "Gamepad_FaceButton_Top", "consume changed another device's key")
    native:apply_masks({}, {E = 1})
    check(key(state.rows[1]) == "None", "ending the chord released its still-held physical key")
    native:apply_masks({}, {E = .1})
    check(key(state.rows[1]) == "None", "physical analog release was confused with rule threshold")
    native:apply_masks({}, {E = 0})
    check(key(state.rows[1]) == "E" and not next(native.consumed), "physical release failed to restore native execution")
end)

test("array rebuild uses current wrappers and source key", function()
    local action = object("/Game/IA_Execute.IA_Execute")
    local backend, native, state = fixture({suppressions = {{action = "/Game/IA_Execute", key = "E"}}})
    state.rows = {mapping(action, "E")}
    native:prepare()
    local stale = state.rows[1]
    state:rebuild({mapping(action, "E"), mapping(action, "F")})
    native:prepare()
    check(key(state.rows[1]) == "None" and key(state.rows[2]) == "F", "rebuilt native mappings lost scoped suppression")
    native:restore()
    check(key(state.rows[1]) == "E", "restore did not use the current native mapping")
    check(key(stale) == "None", "restore wrote into an obsolete mapping allocation")
    state:rebuild({mapping(action, "None")})
    native:restore()
    check(key(state.rows[1]) == "None", "an intentionally unbound replacement inherited an obsolete key")
end)

test("mapping outputs clone native pipelines without sharing their state", function()
    local action = object("/Game/IA_Guard.IA_Guard")
    local pressed = object("/Trigger.Pressed", {LastValue = 0, ActuationThreshold = .37})
    local modifier = object("/Modifier.Scale", {Scalar = 2})
    local backend, native, state = fixture({targets = {guard = {kind = "mapping", actions = {"/Game/IA_Guard"}, device = "keyboard"}},
        bindings = {{id = "guard", keys = {"F10"}, outputs = {{target = "guard", value = 1}}}}})
    state.rows = {mapping(action, "E", {pressed}, {modifier}), mapping(action, "Gamepad_LeftShoulder")}
    native:prepare()
    local effective = backend.dynamic_config
    check(#effective.bindings == 1 and #effective.bindings[1].outputs == 1, "mapping expanded the wrong device")
    local clone = target_for(effective, effective.bindings[1].outputs[1])
    check(clone.object == action and clone.kind == "action", "mapping lost its native InputAction")
    check(clone.trigger_objects[1] ~= pressed and clone.modifier_objects[1] ~= modifier,
        "custom injection shares mutable native trigger/modifier state")
    check(clone.trigger_objects[1].ActuationThreshold == .37 and clone.modifier_objects[1].Scalar == 2,
        "pipeline clone lost native configuration")
    clone.trigger_objects[1].LastValue = 1
    check(pressed.LastValue == 0 and key(state.rows[1]) == "E", "custom input changed the native E pipeline")
    check(state.heldTriggers[1] == clone.trigger_objects[1], "cloned trigger lacks a native GC owner")
    local generation, cloneCount = backend.config_generation, state.clones
    native:prepare()
    check(backend.config_generation == generation and state.clones == cloneCount, "unchanged context recreated stateful custom triggers")
    state.rows[#state.rows + 1] = mapping(action, "J")
    native:prepare()
    local preserved = target_for(backend.dynamic_config, backend.dynamic_config.bindings[1].outputs[1])
    check(preserved == clone and preserved.trigger_objects[1].LastValue == 1,
        "another mapping reset an unchanged custom pipeline's held state")
end)

test("mapping expansion preserves binding-specific restriction preparation on both devices", function()
    local path = "/PBZInput/InputActions/IA_AttackL_PressLong"
    local action = object(path .. ".IA_AttackL_PressLong")
    local config = {unified_native=true, native_actions={path}, targets={}, bindings={}}
    for index, device in ipairs({"keyboard", "keyboard", "gamepad"}) do
        local name = "attack_" .. index
        config.targets[name] = {kind="mapping", actions={path}, device=device,
            equipment_configs={"/Game/Weapon_" .. index}, equipment_slot="Weapon.Slot.Main",
            restriction_groups={equipment={required_tags={"Weapon.Type.Test" .. index}}},
            armed_state={slot="Weapon.Slot.Main", armed=true},
            native_hold_inputs={path .. ".IA_AttackL_PressLong"},
            native_release_when={timeline="Main", minimum_time=.1},
            native_preparation_class="/Game/Ability_" .. index}
        config.bindings[index] = {id=name, keys={device == "gamepad" and "Gamepad_DPad_Down" or "F" .. index},
            trigger="press", bypass_restrictions=true, restriction_bypasses={"equipment", "stance"}, outputs={name}}
    end
    local backend, native, state = fixture(config)
    state.rows = {mapping(action,"E"), mapping(action,"Gamepad_FaceButton_Left")}
    native:prepare()
    local effective, clones = backend.dynamic_config, {}
    for index=1,3 do
        local clone = target_for(effective,effective.bindings[index].outputs[1])
        local original = config.targets["attack_" .. index]
        check(clone.kind == "action" and clone.object == action, "prepared mapping lost its native action")
        for _, field in ipairs({"equipment_configs", "equipment_slot", "restriction_groups", "armed_state",
            "native_hold_inputs", "native_release_when", "native_preparation_class"}) do
            check(clone[field] == original[field], "expanded input lost preparation field " .. field)
        end
        clones[index] = clone
    end
    check(clones[1] ~= clones[2] and clones[1].source_relation == clones[2].source_relation,
        "bindings sharing one native pipeline must retain separate preparation policies")
    for index=4,#effective.bindings do
        local original = target_for(effective,effective.bindings[index].outputs[1])
        check(not original.equipment_configs and not original.restriction_groups and not original.native_hold_inputs,
            "enhanced preparation leaked into ordinary native input")
    end
    local router = Router.new(effective,backend)
    for index, clone in ipairs(clones) do
        router:step(index*2,{[config.bindings[index].keys[1]]=1},{})
        local dispatched = false
        for _, call in ipairs(state.calls) do if call.target == clone then dispatched = true end end
        check(dispatched, "scheduler did not dispatch binding-specific preparation " .. index)
        router:step(index*2+1,{},{})
    end
    state.rows[#state.rows+1] = mapping(action,"J")
    native:prepare()
    for index, clone in ipairs(clones) do
        check(target_for(backend.dynamic_config,backend.dynamic_config.bindings[index].outputs[1]) == clone,
            "mapping rebuild discarded a retained prepared input")
    end
end)

test("fully unbound action retains every template pipeline", function()
    local action = object("/Game/IA_Move.IA_Move")
    local positive = object("/Modifier.Positive", {Scalar = 1})
    local negative = object("/Modifier.Negative", {Scalar = -1})
    local backend, native, state = fixture({targets = {move = {kind = "mapping", actions = {"/Game/IA_Move"}, device = "keyboard"}},
        bindings = {{id = "move", keys = {"F11"}, outputs = {"move"}}}})
    state:add_context({mapping(action, "W", {}, {positive}), mapping(action, "S", {}, {negative}),
        mapping(action, "Gamepad_LeftY")})
    native:prepare()
    local outputs = backend.dynamic_config.bindings[1].outputs
    check(#outputs == 2, "fallback discarded a distinct native mapping pipeline")
    local first, second = target_for(backend.dynamic_config, outputs[1]), target_for(backend.dynamic_config, outputs[2])
    check(first.modifier_objects[1].Scalar == 1 and second.modifier_objects[1].Scalar == -1,
        "fallback did not preserve the default per-key modifiers")
end)

test("runtime mappings override default templates", function()
    local action = object("/Game/IA_Guard.IA_Guard")
    local active = object("/Trigger.Active", {ActuationThreshold = .2})
    local defaults = object("/Trigger.Default", {ActuationThreshold = .8})
    local backend, native, state = fixture({targets = {guard = {kind = "mapping", actions = {"/Game/IA_Guard"}, device = "keyboard"}},
        bindings = {{id = "guard", keys = {"F10"}, outputs = {"guard"}}}})
    state.rows = {mapping(action, "E", {active})}
    state:add_context({mapping(action, "Q", {defaults})})
    native:prepare()
    local outputs = backend.dynamic_config.bindings[1].outputs
    check(#outputs == 1, "default template duplicated an active native action")
    check(target_for(backend.dynamic_config, outputs[1]).trigger_objects[1].ActuationThreshold == .2,
        "mapping used the default template instead of the current native pipeline")
end)

test("main preserves consume through mapping generation changes", function()
    local execute, jump = object("/Game/IA_Execute.IA_Execute"), object("/Game/IA_Jump.IA_Jump")
    local config = {targets = {jump = {kind = "mapping", actions = {"/Game/IA_Jump"}, device = "keyboard"}},
        bindings = {{id = "chord", keys = {"E", "LeftShift"}, trigger = "press", consume = {"E"}, outputs = {"jump"}}},
        enabled = true, runtime_hotkeys = false}
    local backend, native, state = fixture(config)
    state.rows = {mapping(execute, "E"), mapping(jump, "SpaceBar")}
    local frame
    package.loaded["backend"], package.loaded["router"], package.loaded["router-config"] = nil, nil, nil
    package.preload["backend"] = function() return {new = function() return backend end} end
    package.preload["router"] = function() return Router end
    package.preload["router-config"] = function() return config end
    EngineTickAvailable = true
    function LoopInGameThreadAfterFrames(_, callback) frame = callback; return 1 end
    function CancelDelayedAction() end
    local lifecycle = dofile(scripts .. "main.lua")
    frame()
    state.physical.E, state.physical.LeftShift = 1, 1
    frame()
    check(key(state.rows[1]) == "None", "main did not consume native execution for the chord")
    state.physical.LeftShift = 0
    state.rows[#state.rows + 1] = mapping(jump, "J")
    frame()
    check(key(state.rows[1]) == "None", "mapping rebuild exposed still-held execution to the native pipeline")
    frame()
    check(key(state.rows[1]) == "None", "wait-for-release filtering prematurely cleared native consume")
    state.physical.E = 0
    frame()
    check(key(state.rows[1]) == "E", "main failed to restore execution after physical release")
    state.physical.E, state.physical.LeftShift = 1, 1
    frame()
    check(key(state.rows[1]) == "None", "second chord did not consume execution")
    state.rows = {state.rows[1]}
    frame()
    check(#backend.dynamic_config.bindings == 0, "unavailable mapping target retained an unroutable binding")
    check(key(state.rows[1]) == "None", "removing the consuming rule's target exposed a still-held key")
    check(backend:snapshot({}).E == 1, "consumed physical keys vanished with the router's sampling list")
    state.physical.E = 0
    frame()
    check(key(state.rows[1]) == "E" and not next(native.consumed),
        "an orphaned consumed key was not restored after physical release")
    lifecycle.stop()
end)

test("native held movement survives flush without replaying unrelated keys", function()
    local backend, native, state = fixture()
    local move = object("/SGameLocomotionDriver/Input/IA_KeyboardMoveInput.IA_KeyboardMoveInput")
    local fast = object("/SGameLocomotionDriver/Input/IA_Sprint.IA_Sprint")
    local execute = object("/Game/IA_Execute.IA_Execute")
    state.rows = {mapping(move, "W"), mapping(fast, "W"), mapping(execute, "E")}
    local physical = {W=1}
    backend.physical_input = {sample=function(_, key) return physical[key] or 0 end}
    native:prepare()
    check(#backend.dynamic_config.bindings == 2, "native movement must be maintained without any custom rule")
    check(key(state.rows[1]) == "None" and key(state.rows[2]) == "None" and key(state.rows[3]) == "E",
        "only continuous movement relations may be masked; execution must remain native")
    local router = Router.new(backend.dynamic_config, backend)
    local snapshot = backend:snapshot(router:keys()); router:resume(snapshot, backend:context())
    router:step(.1, snapshot, backend:context())
    check(#state.calls == 2 and state.calls[1].phase == "started" and state.calls[2].phase == "started",
        "held W did not recover both movement and fast movement")
    state.physical.W = 0
    router:step(.2, backend:snapshot(router:keys()), backend:context())
    check(#state.calls == 4 and state.calls[3].phase == "triggered" and state.calls[4].phase == "triggered",
        "cleared UE cache interrupted a still-held physical key")
    physical.W = 0
    router:step(.3, backend:snapshot(router:keys()), backend:context())
    check(#state.calls == 6 and state.calls[5].phase == "completed" and state.calls[6].phase == "completed",
        "physical release must end both movement outputs")
    native:restore()
    check(key(state.rows[1]) == "W" and key(state.rows[2]) == "W" and key(state.rows[3]) == "E", "restoration lost native key ownership")
end)

test("held scene controls and catalog axes recover independently of movement blocking", function()
    local base="/Game/PBZContent/Input/InputActions/"
    for _,spec in ipairs({
        {base .. "IA_ShipForward","W"}, {base .. "IA_ShipBackword","S"},
        {base .. "IA_ShipRight","A"}, {base .. "IA_ShipRight","D"},
        {base .. "IA_ShipForward","Gamepad_RightTriggerAxis"},
        {base .. "IA_ShipBackword","Gamepad_LeftTriggerAxis"},
        {base .. "IA_ShipRight","Gamepad_LeftX"},
        {"/PBZDeflectSystem/Input/IA_Stance","RightMouseButton",false,true},
        {"/PBZDeflectSystem/Input/IA_Stance","Gamepad_LeftShoulder",false,true},
        {"/SGameCombatSystem/Weapon/Input/IA_ChangingWeapon","Gamepad_LeftTrigger",false,true},
        {"/SGameCombatSystem/Weapon/Input/IA_ChangingWeapon","Gamepad_LeftTriggerAxis",false,true},
        {"/SGameLocomotionDriver/Input/IA_JoyStickLookInput","Gamepad_Right2D",true},
        {"/SGameCombatSystem/Targeting/IA_JoystickChangeLockTargetAxis","Gamepad_Right2D",true}
    }) do
        local path,inputKey,catalog,withDown=table.unpack(spec)
        local config={unified_native=true,native_actions=catalog and {} or {path}}
        local boundKey=catalog and "Gamepad_LeftX" or inputKey
        if catalog then
            config.targets={axis={kind="mapping",catalog_pipeline=true,device="gamepad",actions={path}}}
            config.bindings={{id="axis",axis=boundKey,trigger="axis",outputs={"axis"}}}
        end
        local backend,native,state=fixture(config)
        local action=object(path .. "." .. path:match("([^/]+)$"))
        local function rows() return {mapping(action,inputKey,withDown and {object("/Trigger.Down")} or nil)} end
        state.rows=rows();native:prepare()
        local gate=true
        for _,binding in ipairs(backend.dynamic_config.bindings) do
            local previous=binding.when
            binding.when=function(...) return gate and (not previous or previous(...)) end
        end
        local router=Router.new(backend.dynamic_config,backend)
        local held={[boundKey]=.8}
        local function active()
            local count=0
            for _,call in ipairs(state.calls) do
                if call.target.object==action and call.value.x~=0 then count=count+1 end
            end
            return count
        end
        router:step(0,{},backend:context());router:step(.1,held,backend:context())
        check(active()==1,"initial scene control failed: " .. path)
        router:reset(true);state.calls={}
        router:resume(held,backend:context());router:step(.2,held,backend:context())
        check(active()==1,"held scene control did not resume: " .. path)
        gate=false;router:step(.3,held,backend:context());state.calls={}
        gate=true;router:step(.4,held,backend:context())
        check(active()==1,"condition recovery required another press: " .. path)
        backend.state.move_ignored=true;state.calls={};router:step(.5,held,backend:context())
        check(active()==1,"character movement blocking suppressed an independent scene control: " .. path)
        backend.state.move_ignored=false
        state:rebuild({});native:prepare()
        router=router:reconfigure(backend.dynamic_config,held,backend:context())
        router:step(.6,held,backend:context())
        state:rebuild(rows());native:prepare()
        router=router:reconfigure(backend.dynamic_config,held,backend:context());state.calls={}
        router:step(.7,held,backend:context())
        check(active()==1,"returning scene context required another press: " .. path)
        state.calls={};router:step(.8,{},backend:context())
        check(#state.calls==1 and state.calls[1].phase=="completed","recovered control failed to release: " .. path)
        native:restore()
        check(key(state.rows[1])==inputKey,"recovery changed the original binding: " .. path)
    end
end)

test("explicit continuous axis outputs recover without changing their trigger pipeline", function()
    for _,path in ipairs({
        "/SGameLocomotionDriver/Input/IA_MouseLookInput",
        "/SGameLocomotionDriver/Input/IA_JoyStickLookInput",
        "/SGameCombatSystem/Targeting/IA_MouseChangeLockTargetAxis",
        "/SGameCombatSystem/Targeting/IA_JoystickChangeLockTargetAxis",
        "/Game/PBZContent/Input/InputActions/IA_MoveInput",
        "/Game/PBZContent/Input/InputActions/IA_LookInput",
        "/Game/PBZContent/Input/InputActions/IA_ClimblMove",
        "/Game/PBZContent/Input/InputActions/IA_ClimbMoveGamePad",
        "/Game/PBZContent/Input/InputActions/IA_ShipLook"
    }) do
        for _,temporal in ipairs({false,"mapping","action"}) do
            local action=object(path .. "." .. path:match("([^/]+)$"))
            local trigger=object(temporal and "/Trigger.Pressed" or "/Trigger.Down")
            local pipeline=temporal=="action" and {} or {trigger}
            if temporal=="action" then action.Triggers=array({trigger}) end
            local backend,native,state=fixture({unified_native=true,
                targets={axis={kind="action",path=path,object=action,trigger_objects=pipeline}},
                bindings={{id="axis",axis="K",trigger="axis",outputs={"axis"}}}})
            native:prepare()
            local target=backend.dynamic_config.targets.axis
            check(target.resume_continuous==not temporal and not target.native_continuous,
                "axis recovery confused temporal triggers or movement blocking: " .. path)
            check(target.trigger_objects==pipeline,"axis recovery replaced the configured trigger")
            local router=Router.new(backend.dynamic_config,backend)
            router:resume({K=-.8},backend:context());router:step(0,{K=-.8},backend:context())
            check(#state.calls==(temporal and 0 or 1),"explicit axis recovery violated its trigger policy: " .. path)
            if not temporal then check(state.calls[1].value.x==-.8,"axis recovery lost its sign") end
            native:restore()
        end
    end
end)

test("automatic movement and a same-key custom mapping do not double input", function()
    local path = "/SGameLocomotionDriver/Input/IA_KeyboardMoveInput"
    local backend, native, state = fixture({targets={move={kind="mapping", actions={path}}},
        bindings={{id="extra", keys={"W"}, trigger="down", outputs={"move"}}}})
    local move = object(path .. ".IA_KeyboardMoveInput")
    state.rows = {mapping(move, "W")}
    state.physical.W = 1
    native:prepare()
    local config = backend.dynamic_config
    local custom = target_for(config, config.bindings[1].outputs[1])
    local automatic = target_for(config, config.bindings[2].outputs[1])
    check(custom.source_relation == automatic.source_relation and custom.trigger_objects == automatic.trigger_objects
        and custom.modifier_objects == automatic.modifier_objects, "automatic and custom sources cloned the same native pipeline twice")
    local router = Router.new(config, backend)
    router:step(.1, backend:snapshot(router:keys()), backend:context())
    check(#state.calls == 1 and state.calls[1].value.x == 1, "same physical W was counted twice")
end)

test("temporal movement switches and removed contexts stay native", function()
    local backend, native, state = fixture()
    local walk = object("/SGameLocomotionDriver/Input/IA_Walk.IA_Walk")
    local toggle = object("/SGameLocomotionDriver/Input/IA_SprintSwitch.IA_SprintSwitch")
    local move = object("/SGameLocomotionDriver/Input/IA_KeyboardMoveInput.IA_KeyboardMoveInput")
    state.rows = {mapping(walk, "LeftAlt"), mapping(toggle, "Gamepad_LeftThumbstick"), mapping(move, "W")}
    state:add_context({mapping(move, "W")})
    native:prepare()
    check(key(state.rows[1]) == "LeftAlt" and key(state.rows[2]) == "Gamepad_LeftThumbstick",
        "pressed/toggle movement controls must retain their native edge behavior")
    check(#backend.dynamic_config.bindings == 1, "continuous movement group contains unrelated controls")
    state:rebuild({mapping(walk, "LeftAlt"), mapping(toggle, "Gamepad_LeftThumbstick")})
    native:prepare()
    check(#backend.dynamic_config.bindings == 0, "removed runtime movement mapping was resurrected from a template")
end)

test("consuming a movement key wins until its physical release", function()
    local backend, native, state = fixture({targets={event={kind="lua"}},
        bindings={{id="combo", keys={"W","V"}, trigger="press", consume={"W"}, outputs={"event"}}}})
    state.rows = {mapping(object("/SGameLocomotionDriver/Input/IA_KeyboardMoveInput.IA_KeyboardMoveInput"), "W")}
    native:prepare()
    local router = Router.new(backend.dynamic_config, backend)
    state.physical.W, state.physical.V = 1, 1
    router:step(.1, backend:snapshot(router:keys()), backend:context())
    check(#state.calls == 1 and state.calls[1].target.kind == "lua", "automatic movement ignored the consuming chord")
    state.physical.V = 0
    router:step(.2, backend:snapshot(router:keys()), backend:context())
    check(#state.calls == 1, "a consumed held movement key resumed before release")
    state.physical.W = 0; router:step(.3, backend:snapshot(router:keys()), backend:context())
    state.physical.W = 1; router:step(.4, backend:snapshot(router:keys()), backend:context())
    check(#state.calls == 2 and state.calls[2].target.kind == "action", "new movement press remained blocked after release")
end)

test("movement recovery classifies both action and mapping triggers", function()
    local backend, native, state = fixture()
    local down = object("/Trigger.Down", {ActuationThreshold=.4})
    function down:GetClass() return "InputTriggerDown" end
    local pressed = object("/Trigger.Pressed")
    local move = object("/SGameLocomotionDriver/Input/IA_KeyboardMoveInput.IA_KeyboardMoveInput", {Triggers=array({down})})
    local sprint = object("/SGameLocomotionDriver/Input/IA_Sprint.IA_Sprint")
    state.rows = {mapping(move, "W"), mapping(sprint, "LeftShift", {down})}
    native:prepare()
    check(#backend.dynamic_config.bindings == 2, "Down triggers on either layer were rejected as edge-only input")
    local target = target_for(backend.dynamic_config, backend.dynamic_config.bindings[2].outputs[1])
    check(target.resume_continuous and target.trigger_objects[1].ActuationThreshold == .4,
        "continuous recovery lost the original trigger threshold")
    move.Triggers = array({pressed})
    state:rebuild({mapping(move, "W"), mapping(sprint, "LeftShift", {pressed})})
    native:prepare()
    check(#backend.dynamic_config.bindings == 0 and key(state.rows[1]) == "W" and key(state.rows[2]) == "LeftShift",
        "a Pressed trigger on either layer was turned into automatic continuous input")
end)

test("native movement suppression takes priority over maintenance", function()
    local path = "/SGameLocomotionDriver/Input/IA_KeyboardMoveInput"
    local backend, native, state = fixture({suppressions={{action=path, key="W"}}})
    local action = object(path .. ".IA_KeyboardMoveInput")
    state.rows = {mapping(action, "W"), mapping(action, "S")}
    native:prepare()
    check(#backend.dynamic_config.bindings == 1 and backend.dynamic_config.bindings[1].axis == "S",
        "maintenance revived an explicitly suppressed native movement relation")
end)

test("animation movement blocks preserve held input for automatic recovery", function()
    local backend, native, state = fixture({targets={event={kind="lua"}},
        bindings={{id="interaction", keys={"E"}, trigger="press", outputs={"event"}}}})
    local move = object("/SGameLocomotionDriver/Input/IA_KeyboardMoveInput.IA_KeyboardMoveInput")
    local sprint = object("/SGameLocomotionDriver/Input/IA_Sprint.IA_Sprint")
    state.rows = {mapping(move, "W"), mapping(sprint, "W")}
    native:prepare()
    local router = Router.new(backend.dynamic_config, backend)
    state.physical.W = 1
    router:step(.1, backend:snapshot(router:keys()), backend:context())
    backend.state.move_ignored = true
    router:step(.2, backend:snapshot(router:keys()), backend:context())
    check(#state.calls == 4 and state.calls[3].phase == "canceled" and state.calls[4].phase == "canceled",
        "animation movement blocking did not end held movement outputs")
    state.physical.E = 1
    router:step(.3, backend:snapshot(router:keys()), backend:context())
    check(#state.calls == 5 and state.calls[5].target.kind == "lua", "movement blocking swallowed an unrelated interaction")
    backend.state.move_ignored = false
    router:step(.4, backend:snapshot(router:keys()), backend:context())
    check(#state.calls == 7 and state.calls[6].phase == "started" and state.calls[7].phase == "started",
        "movement and fast movement did not recover when the animation unblocked without a physical re-press")
end)

local function unified(paths, targets, bindings)
    return fixture({unified_native=true, native_actions=paths, targets=targets or {}, bindings=bindings or {}})
end

test("failed consumers release native masks and held movement without releasing healthy owners", function()
    for _, unifiedNative in ipairs({false,true}) do
        local movePath = "/SGameLocomotionDriver/Input/IA_KeyboardMoveInput"
        local usePath = "/Game/IA_Use"
        local move, use = object(movePath .. ".IA_KeyboardMoveInput"), object(usePath .. ".IA_Use")
        local bad = {kind="action",object=object("/Game/IA_Broken.IA_Broken")}
        local config = {unified_native=unifiedNative,native_actions={movePath},targets={
            bad=bad,good={kind="lua",run=function() return true end}
        },bindings={
            {id="bad",keys={"W"},consume={"W","E"},outputs={"bad"}},
            {id="good",keys={"R"},consume=true,outputs={"good"}}
        }}
        local backend,native,state = fixture(config)
        state.rows = {mapping(move,"W"),mapping(use,"E"),mapping(use,"R")}
        backend.dispatch = Backend.dispatch
        local attempts,cancellations,movement = 0,0,0
        function backend:invoke(receiver,method,action,value)
            check(receiver==self.subsystem and method:find(":InjectInputVectorForAction",1,true),
                "unexpected failure recovery call")
            if action==bad.object then
                if value.X~=0 then attempts=attempts+1;error("broken action injection",0) end
                cancellations=cancellations+1
            elseif action==move and value.X>0 then movement=movement+1 end
            return true
        end
        native:prepare()
        local router = Router.new(backend.dynamic_config,backend)
        router:step(.1,{W=1,E=1,R=1},backend:context())
        check(attempts==1 and cancellations==1 and backend:target_failed(bad,"bad"),
            "action failure did not cancel and enter isolation")
        check(native.consumed.W and native.consumed.E and native.consumed.R and movement==0,
            "failure scenario did not latch native consumption")
        native:prepare()
        router:step(.2,{W=1,E=1,R=1},backend:context())
        check(not native.consumed.W and not native.consumed.E and movement==1,
            "failed consumer kept native movement locked until physical release")
        check(key(state.rows[1])=="None" and key(state.rows[2])=="E",
            "recovery duplicated scheduler movement or retained the unrelated native mask")
        check(native.consumed.R and (unifiedNative or key(state.rows[3])=="None"),
            "failed consumer released a healthy owner's native consumption")
        router:step(.3,{W=1,E=1,R=1},backend:context())
        check(attempts==1 and movement==2,"held movement recovery retried the broken action or stopped moving")
    end
end)

test("partial failures retain native consumption for healthy outputs and shared owners", function()
    for _, sharedGroup in ipairs({false,true}) do
        local bad, good = {kind="lua"}, {kind="lua"}
        local binding = {id="bad",keys={"W"},consume=true,trigger="pulse",interval=.001,outputs={"bad"}}
        local config = {targets={bad=bad,good=good},bindings={binding}}
        if sharedGroup then
            binding.input_group = "shared"
            config.bindings[2] = {id="good",input_group="shared",keys={"W"},consume=true,
                trigger="pulse",interval=.001,outputs={"good"}}
        else binding.outputs[2] = "good" end
        local backend,native,state = fixture(config)
        state.rows = {mapping(object("/Game/IA_Use.IA_Use"),"W")}
        native:prepare()
        local router = Router.new(backend.dynamic_config,backend)
        router:step(.1,{W=1},backend:context())
        backend:fail_target(bad,"unavailable output","bad")
        state.calls = {}
        router:step(.2,{W=1},backend:context())
        check(native.consumed.W and key(state.rows[1])=="None",
            "partial failure released native consumption still owned by a healthy output")
        check(router.consumed.W==(sharedGroup and "shared" or "bad"),
            "partial failure discarded a healthy routing reservation")
        local healthy = false
        for _,call in ipairs(state.calls) do if call.target==good then healthy=true end end
        check(healthy,"partial failure prevented the healthy output from running")
    end
end)

test("unified custom mappings preserve unbound device pipelines without restoring native input", function()
    for _, device in ipairs({"gamepad", "keyboard"}) do
        local actionPath = "/Game/IA_Jump"
        local action = object(actionPath .. ".IA_Jump")
        local pressed = object("/Trigger.Pressed", {ActuationThreshold=.37})
        local released = object("/Trigger.Released", {ActuationThreshold=.61})
        local targets = {jump={kind="mapping", actions={actionPath}, device=device}}
        local backend, native, state = unified({actionPath}, targets,
            {{id="combo", keys={"Gamepad_LeftShoulder", "Gamepad_FaceButton_Bottom"}, outputs={"jump"}}})
        local otherKey = device == "gamepad" and "SpaceBar" or "Gamepad_FaceButton_Bottom"
        local sourceKey = device == "gamepad" and "Gamepad_FaceButton_Bottom" or "SpaceBar"
        state.rows = {mapping(action, otherKey), mapping(action, "None")}
        state:add_context({mapping(action, otherKey), mapping(action, sourceKey, {pressed}), mapping(action, sourceKey, {released})})
        native:prepare()
        local config = backend.dynamic_config
        check(config.bindings[1].id == "combo" and #config.bindings[1].outputs == 2, "unbound device lost its distinct enhanced output pipelines")
        local first, second = target_for(config, config.bindings[1].outputs[1]), target_for(config, config.bindings[1].outputs[2])
        check(first.trigger_objects[1].ActuationThreshold == .37 and second.trigger_objects[1].ActuationThreshold == .61,
            "unbound device used the other device's pipeline")
        check(#config.bindings == 2 and native.coverage[2].owner == "uncovered", "template fallback resurrected an unbound native source")
        native:restore()
        check(key(state.rows[1]) == otherKey and key(state.rows[2]) == "None", "enhanced pipeline fallback restored an intentionally unbound key")
        state:rebuild({mapping(action, "None"), mapping(action, "None")})
        native:prepare()
        check(#backend.dynamic_config.bindings == 1 and #backend.dynamic_config.bindings[1].outputs == 2,
            "fully unbound action lost its device-specific enhanced pipelines or regained native sources")
        state:rebuild({mapping(action, sourceKey, {pressed})})
        native:prepare()
        check(#backend.dynamic_config.bindings[1].outputs == 1, "default templates duplicated a rebound runtime pipeline")
    end
end)

test("unified execution and interaction each retain one E source", function()
    local executePath, interactPath = "/Game/IA_Execute", "/Game/IA_Interact"
    local execute, interact = object(executePath .. ".IA_Execute"), object(interactPath .. ".IA_Interact")
    local pressed = object("/Trigger.Pressed", {LastValue=1, ActuationThreshold=.5})
    local backend, native, state = unified({executePath, interactPath})
    state.rows = {mapping(execute,"E",{pressed}), mapping(interact,"E",{pressed})}
    native:prepare()
    check(key(state.rows[1]) == "None" and key(state.rows[2]) == "None", "ordinary E retained a second native execution source")
    check(#native.coverage == 2 and native.coverage[1].owner == "scheduler" and native.coverage[2].owner == "scheduler", "runtime ownership omitted E relations")
    local count = {[execute]=0,[interact]=0}
    function backend:dispatch(target, phase, value)
        local trigger = target.trigger_objects[1]
        if phase ~= "canceled" and value.x > .5 and trigger.LastValue <= .5 then count[target.object] = count[target.object] + 1 end
        trigger.LastValue = phase == "canceled" and 0 or value.x
        return true
    end
    local router = Router.new(backend.dynamic_config,backend)
    router:step(0,{},backend:context())
    router:step(.1,{E=1},backend:context()); router:step(.2,{E=1},backend:context())
    check(count[execute] == 1 and count[interact] == 1, "E must execute both different functions once, without held-key repeat")
    router:reset(true); router:resume({E=1},backend:context())
    router:step(.3,{E=1},backend:context()); router:step(.4,{},backend:context())
    check(count[execute] == 1 and count[interact] == 1, "resuming after UI replayed execution or interaction")
    router:step(.5,{E=1},backend:context())
    check(count[execute] == 2 and count[interact] == 2, "a new E press failed to rearm")
    native:restore()
    check(key(state.rows[1]) == "E" and key(state.rows[2]) == "E", "disable failed to restore both original E relationships")
end)

test("unified preserves signed one-dimensional analog values and direction modifiers", function()
    local actionPath = "/Game/IA_ShipRight"
    local backend,native,state = unified({actionPath})
    local negate = object("/Modifier.Negate",{Scalar=-1})
    state.rows = {mapping(object(actionPath .. ".IA_ShipRight"),"Gamepad_LeftX",{}, {negate})}
    native:prepare()
    local router = Router.new(backend.dynamic_config,backend)
    router:step(.1,{Gamepad_LeftX=-.7},backend:context())
    check(#state.calls == 1 and state.calls[1].value.x == -.7, "one-dimensional analog input lost its sign or magnitude")
    check(state.calls[1].target.modifier_objects[1].Scalar == -1, "direction modifier was discarded")
    router:step(.2,{Gamepad_LeftX=.05},backend:context())
    check(state.calls[2].value.x == .05, "small analog input was promoted to full actuation before native trigger/deadzone")
end)

test("unified consume cancels gameplay relations but preserves native UI rows", function()
    local guardPath,jumpPath,menuPath = "/Game/IA_Guard","/Game/IA_Jump","/Game/IA_Menu"
    local guard,jump,menu = object(guardPath .. ".IA_Guard"),object(jumpPath .. ".IA_Jump"),object(menuPath .. ".IA_Menu")
    local backend,native,state = unified({guardPath,jumpPath},{event={kind="lua"}},
        {{id="chord",keys={"LB","A"},trigger="press",priority=100,consume={"A"},outputs={"event"}}})
    state.rows = {mapping(guard,"LB"),mapping(jump,"A"),mapping(menu,"A")}
    native:prepare()
    local router = Router.new(backend.dynamic_config,backend)
    router:step(.1,{LB=1,A=1},backend:context())
    local seen={}; for _,call in ipairs(state.calls) do seen[call.target.object or call.target.kind]=true end
    check(seen[guard] and seen.lua and not seen[jump], "partial chord consume swallowed guard or allowed jump")
    check(key(state.rows[3]) == "A", "consume masked UI or a domain outside gameplay ownership")
    state.calls={}; router:step(.2,{A=1},backend:context())
    check(#state.calls == 1 and state.calls[1].phase == "completed" and state.calls[1].target.object == guard, "consumed A resumed before physical release")
    router:step(.3,{},backend:context());state.calls={};router:step(.4,{A=1},backend:context())
    check(#state.calls == 1 and state.calls[1].target.object == jump, "consumed relation did not rearm after release")
    native:restore()
    check(key(state.rows[1]) == "LB" and key(state.rows[2]) == "A" and key(state.rows[3]) == "A", "restore changed physical/UI relationships")
end)

test("unified priority alone permits single, chord, short and long actions to coexist", function()
    local backend,native,state = unified({"/Game/IA_Use"},{short={kind="lua"},long={kind="lua"},chord={kind="lua"}}, {
        {id="short",keys={"E"},trigger="tap",seconds=.25,priority=10,outputs={"short"}},
        {id="long",keys={"E"},trigger="hold",seconds=.3,priority=20,outputs={"long"}},
        {id="chord",keys={"E","Shift"},trigger="press",priority=99,outputs={"chord"}}})
    state.rows={mapping(object("/Game/IA_Use.IA_Use"),"E")};native:prepare()
    local router=Router.new(backend.dynamic_config,backend)
    router:step(0,{},backend:context());router:step(.1,{E=1,Shift=1},backend:context())
    check(#state.calls == 2, "priority without consume swallowed the ordinary E or chord function")
    router:step(.2,{},backend:context())
    local short=false;for _,call in ipairs(state.calls) do if call.target == backend.dynamic_config.targets.short then short=true end end
    check(short, "short press did not coexist with ordinary input")
    state.calls={};router:step(.4,{E=1},backend:context());router:step(.8,{E=1},backend:context())
    local long=false;for _,call in ipairs(state.calls) do if call.target == backend.dynamic_config.targets.long then long=true end end
    check(long, "long press did not coexist with ordinary input")
end)

test("unified unsupported rows retain native ownership and cannot expand as custom mappings", function()
    local actionPath="/Game/IA_Sense"
    local chord=object("/Trigger.Chord");function chord:GetClass() return "InputTriggerChordAction" end
    local backend,native,state=unified({actionPath},{sense={kind="mapping",actions={actionPath}}},{{id="extra",keys={"E"},outputs={"sense"}}})
    local action=object(actionPath .. ".IA_Sense")
    state.rows={mapping(action,"MouseScrollUp"),mapping(action,"PBZ_Touchpad_SwipeUp"),mapping(action,"F",{chord})}
    state:add_context({mapping(action,"MouseScrollUp"),mapping(action,"PBZ_Touchpad_SwipeUp"),mapping(action,"F",{chord})})
    native:prepare()
    check(#backend.dynamic_config.bindings == 0, "unsupported native pipeline was reintroduced through mapping expansion")
    check(key(state.rows[1]) == "MouseScrollUp" and key(state.rows[2]) == "PBZ_Touchpad_SwipeUp" and key(state.rows[3]) == "F", "unsupported inputs lost native ownership")
    for _,entry in ipairs(native.coverage) do check(entry.owner == "uncovered" and entry.reason ~= nil, "unsupported input lacks explicit coverage reason") end
    local zero=object("/Trigger.Pressed",{ActuationThreshold=0})
    local holdZero=object("/Trigger.Hold",{HoldTimeThreshold=0})
    local repeated=object("/Trigger.RepeatedTap")
    state:rebuild({mapping(action,"A",{zero}),mapping(action,"B",{holdZero}),mapping(action,"C",{repeated})})
    native:prepare()
    check(#backend.dynamic_config.bindings == 0 and key(state.rows[1]) == "A" and key(state.rows[2]) == "B" and key(state.rows[3]) == "C", "unresettable temporal pipelines claimed scheduler ownership")
    action.Triggers=array({object("/Trigger.Pressed")})
    state:rebuild({mapping(action,"D")});native:prepare()
    check(native.coverage[1].owner == "uncovered" and key(state.rows[1]) == "D", "unsupported action-level temporal trigger was masked")
end)

test("unified context reorder preserves relation, hold state and original key", function()
    local guardPath,otherPath="/Game/IA_Guard","/Game/IA_Use"
    local hold=object("/Trigger.Hold",{HeldDuration=.6,HoldTimeThreshold=.5})
    local guard=object(guardPath .. ".IA_Guard")
    local backend,native,state=unified({guardPath,otherPath})
    local guardRow=mapping(guard,"RightMouseButton",{hold})
    state.rows={guardRow};native:prepare()
    local router=Router.new(backend.dynamic_config,backend)
    router:step(0,{RightMouseButton=1},backend:context())
    local rule=router.rules[1];local originalTarget=backend.dynamic_config.targets[rule.binding.outputs[1]]
    originalTarget.trigger_objects[1].HeldDuration=.6
    state.rows={mapping(object(otherPath .. ".IA_Use"),"E"),guardRow}
    native:prepare()
    router=router:reconfigure(backend.dynamic_config,{RightMouseButton=1},backend:context())
    local preserved;for _,nextRule in ipairs(router.rules) do if nextRule.binding.id == rule.binding.id then preserved=nextRule end end
    check(preserved and preserved.state == rule.state, "unrelated context reordered and reset the held rule")
    check(backend.dynamic_config.targets[preserved.binding.outputs[1]] == originalTarget and originalTarget.trigger_objects[1].HeldDuration == .6, "context reorder recreated the trigger state")
    check(key(state.rows[2]) == "None", "mask ownership was lost when a row moved")
    state.calls={};router:step(.1,{RightMouseButton=1},backend:context())
    check(#state.calls == 1 and state.calls[1].phase == "triggered", "context reorder interrupted held input")
    native:restore()
    check(key(state.rows[2]) == "RightMouseButton" and key(state.rows[1]) == "E", "reordered mapping failed to restore exact original keys")
end)

test("unified consume rearms cloned triggers without firing release actions", function()
    local pressedPath,releasedPath,holdPath="/Game/IA_Execute","/Game/IA_Release","/Game/IA_Hold"
    local backend,native,state=unified({pressedPath,releasedPath,holdPath},{chord={kind="lua",run=function() return true end}},
        {{id="chord",keys={"E","K"},trigger="press",consume={"E"},outputs={"chord"}}})
    local function trigger(kind)
        return object("/Trigger." .. kind,{Kind=kind,LastValue=0,HeldDuration=0,HoldTimeThreshold=.5})
    end
    local pressed,released,hold=object(pressedPath .. ".IA_Execute"),object(releasedPath .. ".IA_Release"),object(holdPath .. ".IA_Hold")
    local originals={trigger("Pressed"),trigger("Released"),trigger("Hold")}
    state.rows={mapping(pressed,"E",{originals[1]}),mapping(released,"E",{originals[2]}),mapping(hold,"E",{originals[3]})}
    local emitted={[pressed]=0,[released]=0,[hold]=0}
    local invoke=backend.invoke
    function backend:invoke(receiver,method,...)
        local args={...}
        if method == "/Script/EnhancedInput.InputTrigger:UpdateState" then
            check(receiver ~= originals[1] and receiver ~= originals[2] and receiver ~= originals[3], "reset must apply only to cloned pipelines")
            receiver.HeldDuration=0
            return invoke(self,receiver,method,...)
        end
        if method:find(":InjectInputVectorForAction",1,true) then
            local action,value,triggers=args[1],args[2],args[4]
            for _,t in ipairs(triggers) do
                if t.Kind == "Pressed" and value.X > .5 and t.LastValue <= .5
                    or t.Kind == "Released" and value.X == 0 and t.LastValue > .5 then emitted[action]=emitted[action]+1 end
                t.HeldDuration=value.X == 0 and 0 or t.HeldDuration+.1
                t.LastValue=value.X
            end
            return true
        end
        return invoke(self,receiver,method,...)
    end
    backend.dispatch=Backend.dispatch
    native:prepare()
    local router=Router.new(backend.dynamic_config,backend)
    router:step(0,{},backend:context());router:step(.1,{E=1},backend:context())
    check(emitted[pressed] == 1 and emitted[released] == 0, "ordinary E edge failed before consume")
    router:step(.2,{E=1,K=1},backend:context())
    check(emitted[released] == 0, "cancel was forwarded into a native Released action")
    for _,target in pairs(backend.dynamic_config.targets) do
        if target.source_relation then check(target.trigger_objects[1].LastValue == 0 and target.trigger_objects[1].HeldDuration == 0, "cancel retained cloned press/hold state") end
    end
    router:step(.3,{E=1},backend:context());router:step(.4,{},backend:context())
    check(emitted[released] == 0, "release after a canceled chord replayed the interrupted action")
    router:step(.5,{E=1},backend:context())
    check(emitted[pressed] == 2, "E execution stayed swallowed after consume ended")
    router:step(.6,{},backend:context())
    check(emitted[released] == 1, "a real release no longer reaches the native Released trigger")
end)

test("unified main lifecycle resumes movement, sailing and stance but never held execution", function()
    local movePath,fastPath,executePath="/SGameLocomotionDriver/Input/IA_KeyboardMoveInput","/SGameLocomotionDriver/Input/IA_Sprint","/Game/IA_Execute"
    local shipPath,stancePath="/Game/PBZContent/Input/InputActions/IA_ShipForward","/PBZDeflectSystem/Input/IA_Stance"
    local backend,native,state=unified({movePath,fastPath,executePath,shipPath,stancePath})
    local config=backend.config
    config.enabled,config.runtime_hotkeys=true,false
    local move,fast,execute=object(movePath .. ".IA_KeyboardMoveInput"),object(fastPath .. ".IA_Sprint"),object(executePath .. ".IA_Execute")
    local ship,stance=object(shipPath .. ".IA_ShipForward"),object(stancePath .. ".IA_Stance")
    state.rows={mapping(move,"W"),mapping(fast,"W"),mapping(execute,"E"),mapping(ship,"W"),mapping(stance,"E",{object("/Trigger.Down")})}
    backend.physical_input={sample=function(_,key) return state.physical[key] or 0 end}
    function backend:refresh()
        if self.state.gameplay and config.enabled then native:prepare()
        elseif self.dynamic_config then native:restore();self.dynamic_config=nil;self.config_generation=self.config_generation+1 end
        return true
    end
    local frame
    package.loaded.backend,package.loaded.router,package.loaded["router-config"]=nil,nil,nil
    package.preload.backend=function() return {new=function() return backend end} end
    package.preload.router=function() return Router end
    package.preload["router-config"]=function() return config end
    EngineTickAvailable=true
    function StaticFindObject() return nil end
    function LoopInGameThreadAfterFrames(_,callback) frame=callback;return 1 end
    function CancelDelayedAction() end
    local lifecycle=dofile(scripts .. "main.lua")
    frame();state.physical.W,state.physical.E=1,1;frame()
    check(#state.calls == 5, "unified main did not start movement, sailing, stance and execution together")
    state.calls={};backend.state.generation=2;frame()
    local function recovered()
        local found={};for _,call in ipairs(state.calls) do if call.value.x ~= 0 then found[call.target.object]=true end end
        return found[move] and found[fast] and found[ship] and found[stance] and not found[execute]
    end
    check(recovered(), "pawn generation replayed held E or failed to restore movement")
    backend.state.gameplay=false;state.calls={};frame()
    check(key(state.rows[1]) == "W" and key(state.rows[2]) == "W" and key(state.rows[3]) == "E", "UI transition did not restore native key ownership")
    for _,call in ipairs(state.calls) do check(call.phase == "canceled", "UI exit forwarded an ordinary physical release") end
    backend.state.gameplay=true;state.calls={};frame()
    check(recovered(), "return from UI failed to recover W/sprint or replayed E")
    check(lifecycle.reload(), "reloading continuous controls failed")
    state.calls={};frame()
    check(recovered(), "reload interrupted sailing/stance or replayed execution")
    state.physical.E=0;frame();state.calls={};state.physical.E=1;frame()
    local executed=false;for _,call in ipairs(state.calls) do if call.target.object == execute and call.value.x == 1 then executed=true end end
    check(executed, "E could not execute after UI recovery and a fresh press")
    config.enabled=false;frame()
    check(key(state.rows[1]) == "W" and key(state.rows[2]) == "W" and key(state.rows[3]) == "E", "disable did not restore original W/sprint/E mappings")
    lifecycle.stop()
end)

test("unified vanished local player discards obsolete masks and output state", function()
    local actionPath="/Game/IA_Execute"
    local backend,native,state=unified({actionPath})
    state.rows={mapping(object(actionPath .. ".IA_Execute"),"E")};native:prepare()
    local router=Router.new(backend.dynamic_config,backend)
    router:step(0,{E=1},backend:context())
    backend.dispatch=Backend.dispatch
    backend.controller.IsValid=function() return false end
    backend.subsystem.IsValid=function() return false end
    function backend.controller:GetPropertyValue() error("destroyed controller must not be accessed") end
    router:reset(true);native:restore()
    check(not next(router.previous) and not next(native.masked) and native.expanded == nil, "destroyed player left pending state that would suspend the next scene")
    local replacement=object("/Controller.NewController")
    local input=object("/Input.NewInput")
    replacement.PlayerInput=input
    state.rows={mapping(object(actionPath .. ".IA_Execute"),"F")}
    function input:GetPropertyValue(name) if name == "EnhancedActionMappings" then return array(state.rows,wrapper) end return array({}) end
    backend.controller,backend.subsystem=replacement,object("/Subsystem.NewSubsystem")
    native:prepare();native:restore()
    check(key(state.rows[1]) == "F", "a new scene inherited the old controller's E key")
end)

test("unified mode requires the frame scheduler before taking native ownership", function()
    local backend,native,state=unified({"/Game/IA_Execute"})
    local config=backend.config;config.enabled,config.runtime_hotkeys=true,false
    state.rows={mapping(object("/Game/IA_Execute.IA_Execute"),"E")}
    package.loaded.backend,package.loaded.router,package.loaded["router-config"]=nil,nil,nil
    package.preload.backend=function() return {new=function() return backend end} end
    package.preload.router=function() return Router end
    package.preload["router-config"]=function() return config end
    LoopInGameThreadAfterFrames,ExecuteInGameThreadAfterFrames,EngineTickAvailable=nil,nil,false
    function LoopAsync() error("unified mode must not use an independent polling loop") end
    local lifecycle=dofile(scripts .. "main.lua")
    check(key(state.rows[1]) == "E" and native.expanded == nil, "unavailable frame scheduler masked native E")
    lifecycle.stop()
end)

test("live retained overrides restore original rows", function()
    local action = object("/Game/UI.Confirm")
    local backend, native, state = fixture({unified_native=true, native_bindings={
        {["function"]="UI",device="keyboard",keys={"J"},actions={"/Game/UI"}}
    }})
    state.rows = {mapping(action,"Enter"), mapping(action,"Gamepad_FaceButton_Bottom")}
    native:apply_masks()
    check(key(state.rows[1])=="J" and key(state.rows[2])=="Gamepad_FaceButton_Bottom", "retained UI override changed wrong device")
    native:apply_masks()
    native:restore()
    check(key(state.rows[1])=="Enter", "repeated override lost original native key")
end)

test("unbound controller override keeps default device identity", function()
    local action = object("/Game/Jump.Jump")
    local backend,native,state=fixture({unified_native=true,native_actions={"/Game/Jump"},
        targets={jump={kind="mapping",mapping="IA_Jump",device="gamepad",actions={"/Game/Jump"}}},
        bindings={{id="jump",keys={"Gamepad_LeftShoulder"},outputs={"jump"}}},
        native_bindings={{["function"]="IA_Jump",device="gamepad",keys={"Gamepad_LeftShoulder"},actions={"/Game/Jump"}}}})
    state.rows={mapping(action,"SpaceBar"),mapping(action,"None")}
    state:add_context({mapping(action,"SpaceBar"),mapping(action,"Gamepad_FaceButton_Bottom")})
    backend.live_validation=true
    native:prepare()
    check(native.maintained[2] and key(state.rows[2])=="None", "unbound controller mapping became a second native binding")
    check(#backend.dynamic_config.bindings>0, "unbound controller mapping lost its enhanced output")
    local found=false
    for _,binding in ipairs(backend.dynamic_config.bindings) do
        if binding.id=="jump" then found=#binding.outputs>0 end
    end
    check(found,"unbound controller combination was dropped")
    native:restore()
    check(key(state.rows[2])=="None", "restore rebound originally unbound controller source")
end)

test("startup and live retained overflow reject instead of losing keys", function()
    for _, live in ipairs({false,true}) do
    local action=object("/Game/UI.UI")
    local backend,native,state=fixture({unified_native=true,native_bindings={
        {["function"]="UI",device="keyboard",keys={"J","K"},actions={"/Game/UI"}}
    }})
    state.rows={mapping(action,"Enter")}
    backend.live_validation=live
    local ok=pcall(function() native:prepare() end)
    check(not ok and key(state.rows[1])=="Enter", "runtime accepted and truncated retained native input")
    native:restore()
    check(key(state.rows[1])=="Enter", "rejected override did not restore native input")
    backend.config.native_bindings[1].keys={"J"}
    native:prepare();native:apply_masks()
    check(key(state.rows[1])=="J", "valid retained override was rejected after overflow")
    native:restore()
    check(key(state.rows[1])=="Enter", "valid override lost original input after retry")
    end
end)

test("startup overflow suspends routing and restores previously owned input", function()
    local actionPath="/Game/UI"
    local backend,native,state=fixture({enabled=true,dynamic_capture=true,runtime_hotkeys=false,unified_native=true,
        native_bindings={{["function"]="UI",device="keyboard",keys={"J"},actions={actionPath}}}})
    state.rows={mapping(object(actionPath .. ".UI"),"Enter")}
    native:prepare();native:apply_masks()
    check(key(state.rows[1])=="J", "valid native mapping was not owned before capacity failure")
    backend.config.native_bindings[1].keys={"J","K","L"}
    local refreshes=0
    function backend:refresh() refreshes=refreshes+1;native:prepare();return true end
    package.loaded.backend,package.loaded.router,package.loaded["router-config"]=nil,nil,nil
    package.preload.backend=function() return {new=function() return backend end} end
    package.preload.router=function() return Router end
    package.preload["router-config"]=function() return backend.config end
    local frame
    EngineTickAvailable=true
    function LoopInGameThreadAfterFrames(_,callback) frame=callback;return 1 end
    function CancelDelayedAction() end
    local lifecycle=dofile(scripts .. "main.lua")
    frame()
    check(key(state.rows[1])=="Enter" and native.expanded==nil, "startup capacity rejection left native input overridden")
    frame()
    check(refreshes==1, "startup accepted an overflowing saved configuration")
    lifecycle.stop()
end)

test("empty overrides allow absent devices while active actions require a populated device pipeline", function()
    for _, live in ipairs({false,true}) do
        local backend,native,state=fixture({unified_native=true,native_bindings={
            {["function"]="IA_MoveSlow",device="gamepad",keys={},actions={"/Game/MoveSlow"}},
            {["function"]="IA_Jump",device="keyboard",keys={},actions={"/Game/Jump"}}
        }})
        state.rows={mapping(object("/Game/Jump.Jump"),"SpaceBar"),mapping(object("/Game/MoveSlow.MoveSlow"),"LeftAlt")}
        backend.live_validation=live
        native:prepare();native:apply_masks()
        check(key(state.rows[1])=="None", "empty override did not unbind an existing device mapping")
        native:restore()
        check(key(state.rows[1])=="SpaceBar", "empty override lost the original native key")
        backend.config.native_bindings[1].keys={"Gamepad_FaceButton_Bottom"}
        local ok,err=pcall(function() native:prepare() end)
        check(not ok and tostring(err):find("active runtime slots",1,true), "populated override accepted a missing device pipeline")
    end
end)

test("scene mappings wait for their context and resume without suspending other input", function()
    for _, live in ipairs({false,true}) do
        for _, device in ipairs({"keyboard","gamepad"}) do
            local interactPath="/Game/PBZContent/Input/InputActions/IA_Interaction"
            local movePath="/SGameLocomotionDriver/Input/IA_KeyboardMoveInput"
            local nativeKey=device=="keyboard" and "E" or "Gamepad_FaceButton_Right"
            local customKey=device=="keyboard" and "K" or "Gamepad_FaceButton_Top"
            local action=object(interactPath .. ".IA_Interaction")
            local move=object(movePath .. ".IA_KeyboardMoveInput")
            local pressed=object("/Trigger.Pressed")
            local backend,native,state=fixture({unified_native=true,native_actions={interactPath,movePath},
                targets={interact={kind="mapping",mapping="IA_Interact",device=device,actions={interactPath}}, utility={kind="lua"}},
                bindings={{id="interact",keys={customKey},trigger="down",outputs={"interact"}},
                    {id="utility",keys={"F9"},trigger="press",outputs={"utility"}}},
                native_bindings={{["function"]="IA_Interact",device=device,keys={customKey},actions={interactPath}}}})
            backend.live_validation=live
            state.rows={mapping(move,"W")}
            local moveContext=state:add_context({mapping(move,"W")})
            native:prepare()
            local function binding(id)
                for _, item in ipairs(backend.dynamic_config.bindings) do if item.id==id then return item end end
            end
            check(not binding("interact") and binding("utility"), "inactive scene mapping suspended or consumed unrelated bindings")
            local router=Router.new(backend.dynamic_config,backend)
            local held={W=1,[customKey]=1}
            router:step(0,{},backend:context());router:step(.1,held,backend:context())
            local sceneContext=state:add_context({mapping(action,nativeKey,{pressed})})
            state:rebuild({mapping(move,"W"),mapping(action,nativeKey,{pressed})})
            native:prepare()
            check(binding("interact") and key(state.rows[2])=="None", "returning context failed to schedule the override or left duplicate native input")
            state.calls={}
            router=router:reconfigure(backend.dynamic_config,held,backend:context())
            router:step(.2,held,backend:context())
            for _,call in ipairs(state.calls) do
                check(call.target.object~=action, "a held key fired interaction when its context first appeared")
            end
            router:step(.3,{W=1},backend:context());state.calls={}
            router:step(.4,held,backend:context())
            local fired=false
            for _,call in ipairs(state.calls) do if call.target.object==action and call.value.x==1 then fired=true end end
            check(fired, "scene interaction did not accept a fresh press")
            state.contexts={moveContext};state:rebuild({mapping(move,"W")})
            native:prepare();state.calls={}
            router=router:reconfigure(backend.dynamic_config,held,backend:context())
            check(not binding("interact") and binding("utility"), "removed context left a stale interaction binding")
            local canceled=false
            for _,call in ipairs(state.calls) do
                if call.target.object==action then canceled=call.phase=="canceled"
                else check(call.phase~="canceled", "removing scene interaction canceled unrelated movement") end
            end
            check(canceled, "context removal failed to cancel the active interaction pipeline")
            state.calls={};router:step(.5,held,backend:context())
            check(#state.calls==1 and state.calls[1].target.object==move, "movement stopped after scene interaction disappeared")
            state.contexts={moveContext,sceneContext}
            state:rebuild({mapping(move,"W"),mapping(action,nativeKey,{pressed})})
            native:prepare();state.calls={}
            router=router:reconfigure(backend.dynamic_config,held,backend:context())
            router:step(.6,held,backend:context())
            for _,call in ipairs(state.calls) do check(call.target.object~=action, "restoring a scene context replayed held interaction") end
            router:step(.7,{W=1},backend:context());state.calls={}
            router:step(.8,held,backend:context())
            fired=false
            for _,call in ipairs(state.calls) do if call.target.object==action and call.value.x==1 then fired=true end end
            check(fired, "restored scene context did not rearm interaction")
            native:restore()
            check(key(state.rows[2])==nativeKey, "restoring native ownership lost the returning scene key")
        end
    end
end)

test("active context templates still reject an unavailable requested device pipeline", function()
    local action=object("/Game/Interaction.Interaction")
    local backend,native,state=fixture({unified_native=true,native_actions={"/Game/Interaction"},
        targets={interact={kind="mapping",mapping="IA_Interact",device="gamepad",actions={"/Game/Interaction"}}},
        bindings={{id="interact",keys={"Gamepad_FaceButton_Right"},outputs={"interact"}}}})
    backend.live_validation=true
    state:add_context({mapping(action,"E")})
    local ok,err=pcall(function() native:prepare() end)
    check(not ok and tostring(err):find("No runtime input pipeline",1,true),
        "active context with no requested device pipeline was mistaken for an inactive scene")
end)

test("unbound mapping rebuilds read each context template once", function()
    stress_gc=false
    local config={unified_native=true,native_actions={},native_bindings={},targets={},bindings={}}
    local backend,native,state=fixture(config)
    local templates={}
    for index=1,48 do
        local path="/Game/Unbound_"..index
        local action=object(path..".Unbound_"..index)
        local trigger=object("/Trigger.Pressed")
        config.native_actions[index]=path
        config.native_bindings[index]={["function"]="unbound_"..index,device="keyboard",keys={},actions={path}}
        state.rows[index]=mapping(action,"None",{trigger})
        templates[index]=mapping(action,"E",{trigger})
    end
    state:add_context(templates)
    native:prepare()
    check(state.template_reads<=#templates*2,"unbound rebuild repeatedly re-read the native context templates: "..state.template_reads)
    for _,entry in ipairs(native.coverage) do check(entry.owner=="scheduler","unbound native override lost scheduler ownership") end
    native:restore()
    for _,entry in ipairs(state.rows) do check(key(entry)=="None","unbound rebuild restored a default key over a saved unbinding") end
end)

test("mapping rebuilds read pipeline metadata once and keep restoration independent", function()
    stress_gc=false
    local config={unified_native=true,native_actions={},targets={},bindings={}}
    local backend,native,state=fixture(config)
    local templates={}
    for index=1,24 do
        local path="/Game/Metadata_"..index
        local action=object(path..".Metadata_"..index)
        local trigger=object("/Trigger.Pressed")
        config.native_actions[index]=path
        config.targets["extra_"..index]={kind="mapping",actions={path}}
        config.bindings[index]={id="extra_"..index,keys={"J"},outputs={"extra_"..index}}
        state.rows[#state.rows+1]=mapping(action,"E",{trigger})
        state.rows[#state.rows+1]=mapping(action,"None",{trigger})
        templates[#templates+1]=mapping(action,"E",{trigger})
        templates[#templates+1]=mapping(action,"Gamepad_FaceButton_Left",{trigger})
    end
    state:add_context(templates)
    native:prepare()
    for _,rows in ipairs({state.rows,templates}) do
        for _,entry in ipairs(rows) do
            check(entry.pipeline_reads==2,"rebuild repeatedly read one mapping's native pipeline: "..tostring(entry.pipeline_reads))
        end
    end
    for index=1,24 do
        local outputs=backend.dynamic_config.bindings[index].outputs
        check(#outputs==2,"metadata reuse lost a runtime or unbound device pipeline")
        for _,output in ipairs(outputs) do
            local target = target_for(backend.dynamic_config,output)
            check(native.expanded[target.source_relation].key~="None","observing masked keys changed a scheduler source key")
        end
    end
    native:restore()
    for index,entry in ipairs(state.rows) do
        check(key(entry)==(index%2==1 and "E" or "None"),"metadata reuse changed restoration or saved unbinding")
    end
end)

test("continuous rebuilds index action identities and share trigger reflection within the frame", function()
    stress_gc=false
    local actionPath="/SGameLocomotionDriver/Input/IA_KeyboardMoveInput"
    local config={unified_native=true,native_actions={actionPath},targets={},bindings={}}
    local backend,native,state=fixture(config)
    local trigger=object("/Trigger.Down")
    local reads,classes=0,0
    function trigger:GetClass() classes=classes+1;return "InputTriggerDown" end
    for index=1,64 do
        local action=object(actionPath..".IA_KeyboardMoveInput")
        local getAddress=action.GetAddress
        function action:GetAddress() reads=reads+1;return getAddress(self) end
        state.rows[index]=mapping(action,"W",{trigger})
    end
    native:prepare()
    check(reads<=#state.rows*40,"continuous sources scanned unrelated action identities: "..reads)
    check(classes<=#state.rows+1,"shared trigger class was repeatedly reflected during one rebuild: "..classes)
    check(#backend.dynamic_config.bindings==#state.rows,"distinct action objects lost their independent injection")
    trigger.ActuationThreshold=0
    native:prepare()
    check(#backend.dynamic_config.bindings==0,"frame metadata hid a changed trigger threshold")
    for _,entry in ipairs(state.rows) do check(key(entry)=="W","unsupported sources must return to native ownership") end
    trigger.ActuationThreshold=.5
    native:prepare()
    check(#backend.dynamic_config.bindings==#state.rows,"a later rebuild failed to reacquire supported sources")
    native:restore()
end)

test("mapping rebuilds match previous pipelines without scanning unrelated actions", function()
    stress_gc=false
    local config={unified_native=true,native_actions={},targets={},bindings={}}
    local backend,native,state=fixture(config)
    local actions,triggers,reads={},{},0
    for index=1,80 do
        local path="/Game/Rebuild_"..index
        local action=object(path..".Rebuild_"..index)
        local getAddress=action.GetAddress
        function action:GetAddress() reads=reads+1;return getAddress(self) end
        actions[index],triggers[index]=action,object("/Trigger.Pressed")
        config.native_actions[index]=path
        state.rows[index]=mapping(action,"E",{triggers[index]})
    end
    native:prepare()
    local clones=state.clones
    local rows={}
    for index,action in ipairs(actions) do rows[index]=mapping(action,"E",{triggers[index]}) end
    state:rebuild(rows);reads=0
    native:prepare()
    check(reads<=#rows*32,"mapping rebuild repeatedly inspected unrelated action addresses: "..reads)
    check(state.clones==clones,"moving unchanged native rows recreated their independent pipeline clones")
    native:restore()
    for _,entry in ipairs(state.rows) do check(key(entry)=="E","pipeline grouping lost the original key") end
end)

test("rebuilds inspect only related action targets and reuse action metadata", function()
    stress_gc=false
    local config={unified_native=true,native_actions={},targets={},bindings={}}
    local backend,native,state=fixture(config)
    local templates,deviceReads,pathReads,triggerReads={},0,0,0
    for index=1,64 do
        local actionPath="/Game/Indexed_"..index
        local action=object(actionPath..".Indexed_"..index)
        local fullName=action.GetFullName
        function action:GetFullName() pathReads=pathReads+1;return fullName(self) end
        local triggers=array({})
        setmetatable(action,{__index=function(_,key)
            if key=="Triggers" then triggerReads=triggerReads+1;return triggers end
        end})
        local name="indexed_"..index
        config.targets[name]=setmetatable({kind="mapping",catalog_pipeline=true,actions={actionPath}},{__index=function(_,key)
            if key=="device" then deviceReads=deviceReads+1 end
        end})
        config.bindings[index]={id=name,keys={"J"},outputs={name}}
        local runtime=mapping(action,"E")
        local template=mapping(action,"Gamepad_FaceButton_Left")
        runtime.SettingBehavior,template.SettingBehavior=2,2
        state.rows[index],templates[index]=runtime,template
    end
    state:add_context(templates)
    native:prepare()
    check(deviceReads<=#state.rows*12,"rebuild inspected unrelated targets: "..deviceReads)
    check(pathReads==#state.rows,"rebuild repeatedly converted a shared action path: "..pathReads)
    check(triggerReads==#state.rows,"rebuild repeatedly read shared action trigger arrays: "..triggerReads)
    for index=1,#state.rows do
        check(#backend.dynamic_config.bindings[index].outputs==2,"action indexing lost a device's template pipeline")
    end
    native:restore()
end)

test("unbound device discovery skips unrelated template actions", function()
    stress_gc=false
    local config={unified_native=true,native_actions={},native_bindings={},targets={},bindings={}}
    local backend,native,state=fixture(config)
    local templates,reads={},0
    for index=1,64 do
        local actionPath="/Game/IndexedUnbound_"..index
        local action=object(actionPath..".IndexedUnbound_"..index)
        local getAddress=action.GetAddress
        function action:GetAddress() reads=reads+1;return getAddress(self) end
        local trigger=object("/Trigger.Pressed")
        config.native_actions[index]=actionPath
        config.native_bindings[index]={["function"]="unbound_"..index,device="keyboard",keys={},actions={actionPath}}
        state.rows[index]=mapping(action,"None",{trigger})
        templates[index]=mapping(action,"E",{trigger})
    end
    state:add_context(templates)
    native:prepare()
    check(reads<=#state.rows*32,"unbound mappings compared unrelated template actions: "..reads)
    for _,entry in ipairs(native.coverage) do check(entry.owner=="scheduler","unbound source lost its device ownership") end
    native:restore()
end)

test("stable menu overrides reuse native observation and rebuild after key or config changes", function()
    stress_gc=false
    local backend,native,state=fixture({unified_native=true,native_bindings={
        {["function"]="Menu",device="keyboard",keys={"J"},actions={"/Game/Menu"}}
    }})
    local action=object("/Game/Menu.Menu")
    state.rows={mapping(action,"None")}
    state:add_context({mapping(action,"Enter")})
    local dirty,observes=true,0
    native.observe_native=function() observes=observes+1;dirty=false end
    native.unchanged_native=function() return not dirty end
    native:prepare_overrides()
    check(key(state.rows[1])=="J" and observes==1,"menu overrides were not applied and observed")
    state.mapping_reads,state.template_reads=0,0
    for _=1,120 do native:prepare_overrides() end
    check(state.mapping_reads==0 and state.template_reads==0 and observes==1,"stable menu overrides traversed native rows or defaults")
    backend.config.native_bindings[1].keys[1]="K"
    native:prepare_overrides()
    check(key(state.rows[1])=="K" and observes==2,"in-place override edit reused stale menu mappings")
    state:rebuild({mapping(action,"F")});dirty=true
    native:prepare_overrides()
    check(key(state.rows[1])=="K" and observes==3,"menu mapping rebuild was not remapped")
    native:restore()
    check(key(state.rows[1])=="F","menu restoration lost the rebuilt native key")
    native:prepare_overrides()
    check(key(state.rows[1])=="K" and observes==4,"restored menu reused stale native observation")
    native:restore()
end)

test("unchanged unified frames inspect rows once and consumption never rescans native mappings", function()
    local actionPath="/Game/Jump"
    local action=object(actionPath .. ".Jump")
    local backend,native,state=fixture({unified_native=true,native_actions={actionPath},
        targets={jump={kind="mapping",actions={actionPath},device="keyboard",native_override=true}},
        bindings={{id="jump",keys={"J"},outputs={"jump"}}},
        native_bindings={{["function"]="jump",device="keyboard",keys={"J"},actions={actionPath}}}})
    state.rows={mapping(action,"SpaceBar"),mapping(action,"Gamepad_FaceButton_Bottom")}
    native:prepare()
    local generation,clones=backend.config_generation,state.clones
    for _=1,3 do
        state.mapping_reads=0;native:prepare()
        check(state.mapping_reads==#state.rows, "unchanged frame repeatedly traversed native mappings")
        native:apply_masks({J=true},{J=1})
        check(state.mapping_reads==#state.rows and native.consumed.J, "consumption rescanned mappings or failed to latch")
        native:apply_masks({},{J=0})
        check(not native.consumed.J, "cached ownership lost physical release")
    end
    check(backend.config_generation==generation and state.clones==clones, "unchanged frame rebuilt scheduler state")
    native:restore();native:prepare()
    check(key(state.rows[1])=="None", "restoring ownership left a stale prepared snapshot")
    state.rows[1].Key.KeyName=FName("F")
    native:prepare();native:restore()
    check(key(state.rows[1])=="F", "an in-place native key edit reused an obsolete original key")
end)

test("unchanged rows reuse observed addresses and read shared mapping names once per frame", function()
    local actionPath = "/Game/Jump"
    local action = object(actionPath .. ".Jump")
    local name, nameReads = "jump", 0
    local settings = object("/Settings.Shared", {Name={ToString=function() nameReads=nameReads+1;return name end}})
    action.PlayerMappableKeySettings = settings
    local trigger = object("/Trigger.Pressed")
    local backend, native, state = fixture({unified_native=true,native_actions={actionPath}})
    state.rows = {mapping(action,"E",{trigger}),mapping(action,"J",{trigger})}
    local templates = {mapping(action,"E",{trigger}),mapping(action,"J",{trigger})}
    for _, rows in ipairs({state.rows,templates}) do
        for _, row in ipairs(rows) do row.SettingBehavior = 0 end
    end
    state:add_context(templates)
    native:prepare()
    local actionReads, triggerReads = 0, 0
    local actionAddress, triggerAddress = action.GetAddress, trigger.GetAddress
    function action:GetAddress() actionReads=actionReads+1;return actionAddress(self) end
    function trigger:GetAddress() triggerReads=triggerReads+1;return triggerAddress(self) end
    nameReads = 0
    native:prepare()
    check(actionReads==4 and triggerReads==4,
        "stable rows must read each current address once without dereferencing observed objects again")
    check(nameReads==1, "shared mapping settings were converted to text repeatedly in one frame")
    name = "renamed"
    check(not native:unchanged(), "shared name caching hid an in-place mapping identity change")
    name = "jump"
    native:restore()
    check(key(state.rows[1])=="E" and key(state.rows[2])=="J", "address metadata lost native restoration")
end)

test("native observation skips stable Lua traversal and rebuilds before accepting new ownership", function()
    local loader = package.loadlib
    local observes, probes, changed, rejected = 0,0,false,false
    package.loadlib = function(_, name)
        if name=="pbz_input_mappings_observe" then return function(input)
            check(type(input)=="number", "native observation received a wrapper instead of the current input address")
            observes=observes+1;changed=false
            if rejected then return "unsupported input layout" end
        end end
        if name=="pbz_input_mappings_unchanged" then return function(input, value)
            check(type(input)=="number" and value==true, "native comparison lost its Lua result argument")
            probes=probes+1;return not changed
        end end
        return nil
    end
    local action=object("/Game/Jump.Jump")
    local backend,native,state=fixture({unified_native=true,native_actions={"/Game/Jump"}})
    package.loadlib=loader
    state.rows={mapping(action,"E")}
    native:prepare()
    check(observes==1, "native observation ran before masked mappings were ready or was omitted")
    state.mapping_reads=0
    for _=1,120 do native:prepare() end
    check(probes==120 and state.mapping_reads==0, "stable native observation still traversed rows through Lua")
    state:rebuild({mapping(action,"F")});changed=true
    native:prepare()
    check(observes==2 and key(state.rows[1])=="None" and backend.dynamic_config.bindings[1].relation_key=="F",
        "native invalidation did not rebuild and mask the new mapping")
    native:restore();native:prepare()
    check(observes==3, "restoration reused a native observation for retired ownership")
    rejected=true;changed=true
    native:prepare()
    check(not native.observe_native and not native.unchanged_native, "unsupported native layout did not retain Lua observation")
    state.mapping_reads=0;native:prepare()
    check(state.mapping_reads==1, "Lua observation did not resume after native layout rejection")
    native:restore()
    check(key(state.rows[1])=="F", "native observation failure changed native restoration")
end)

test("rebuilding resolves each original key once and discards frame-local results", function()
    local backend,native,state=fixture()
    local action=object("/Game/Jump.Jump")
    local mappingRow=mapping(action,"SpaceBar")
    local reads=0
    local original=mappingRow.Key.KeyName.ToString
    mappingRow.Key.KeyName.ToString=function(self) reads=reads+1;return original(self) end
    local current={location=mappingRow.pointer,object=action,modifier_objects={},trigger_objects={}}
    local frame={}
    for _=1,20 do check(native:original_key(1,mappingRow,current,frame)=="SpaceBar","same rebuild lost its original key") end
    check(reads==1,"same rebuild repeatedly read the original native key")
    mappingRow.Key.KeyName=FName("F")
    check(native:original_key(1,mappingRow,current,{})=="F","a later rebuild reused an earlier key")
    check(native:original_key(1,mappingRow,current)=="F","restoration must read the current native row")
end)

test("stable native preparation reuses configuration storage and detects target changes", function()
    stress_gc = false
    local config={unified_native=true,native_actions={"/Game/Jump"},targets={}}
    for index=1,48 do
        config.targets["mapping_"..index]={kind="mapping",actions={"/Game/Jump"},native_override=true}
    end
    local backend,native,state=fixture(config)
    state.rows={mapping(object("/Game/Jump.Jump"),"E")}
    native:prepare()
    native.unchanged_native=function() return true end
    for _=1,10 do native:prepare() end
    collectgarbage("collect");collectgarbage("stop")
    local before=collectgarbage("count")
    for _=1,1000 do native:prepare() end
    local allocated=collectgarbage("count")-before
    collectgarbage("restart")
    check(allocated<16,"stable native preparation allocated transient configuration tables: "..allocated.." KiB")
    local generation=backend.config_generation
    config.targets.mapping_48=nil
    native:prepare()
    check(backend.config_generation>generation and not backend.dynamic_config.targets.mapping_48,
        "reused settings retained a deleted mapping target")
    generation=backend.config_generation
    config.targets.mapping_1.kind="event";config.targets.mapping_1.tag="Changed"
    native:prepare()
    check(backend.config_generation>generation and backend.dynamic_config.targets.mapping_1==config.targets.mapping_1,
        "reused settings missed a target changing from a mapping to an event")
    generation=backend.config_generation
    config.targets.mapping_48={kind="mapping",actions={"/Game/Jump"},native_override=true}
    native:prepare()
    check(backend.config_generation>generation and backend.dynamic_config.targets.mapping_48,
        "reused settings missed an added mapping target")
end)

test("suppressed native sources remain masked without permanently inactive scheduler rules", function()
    local action=object("/Game/Jump.Jump")
    local config={unified_native=true,native_actions={"/Game/Jump"},suppressions={{action="/Game/Jump",key="E"}}}
    local backend,native,state=fixture(config)
    state.rows={mapping(action,"E"),mapping(action,"SpaceBar")}
    native:prepare()
    check(#backend.dynamic_config.bindings==1 and backend.dynamic_config.bindings[1].relation_key=="SpaceBar",
        "a permanently suppressed input must not remain in the frame scheduler")
    check(key(state.rows[1])=="None" and native.maintained[1],"suppression must keep native duplicate prevention")
    config.suppressions={};native:prepare()
    check(#backend.dynamic_config.bindings==2,"removing suppression must restore the scheduler relation")
    config.suppressions={{action="/Game/Jump",key="E"}};native:prepare()
    check(#backend.dynamic_config.bindings==1,"changing suppression must retire the obsolete scheduler relation")
    native:restore()
    check(key(state.rows[1])=="E" and key(state.rows[2])=="SpaceBar","shutdown must retain original keys for suppressed inputs")
end)

test("equivalent continuous movement sources share one injection without merging distinct pipelines", function()
    local movePath="/SGameLocomotionDriver/Input/IA_KeyboardMoveInput"
    local action=object(movePath .. ".IA_KeyboardMoveInput")
    local function modifier(class,fields)
        local result=object("/Modifier." .. class,fields)
        function result:GetClass() return class end
        return result
    end
    local first=modifier("InputModifierSwizzleAxis",{Order=1})
    local equivalent=modifier("InputModifierSwizzleAxis",{Order=1})
    local different=modifier("InputModifierSwizzleAxis",{Order=2})
    local backend,native,state=fixture({unified_native=true,native_actions={movePath}})
    state.rows={mapping(action,"W",{},{first}),mapping(action,"W",{},{equivalent}),mapping(action,"S",{},{different})}
    native:prepare()
    check(#backend.dynamic_config.bindings==2, "equivalent movement sources were not merged or distinct directions were lost")
    check(key(state.rows[1])=="None" and key(state.rows[2])=="None", "duplicate movement retained native execution")
    local router=Router.new(backend.dynamic_config,backend)
    router:step(0,{},backend:context());router:step(.1,{W=1},backend:context())
    check(#state.calls==1 and state.calls[1].value.x==1, "one physical W injected movement more than once")
    state.calls={};router:step(.2,{W=1,S=1},backend:context())
    check(#state.calls==2, "distinct movement modifier pipelines were merged")
    native:restore()
    check(key(state.rows[1])=="W" and key(state.rows[2])=="W" and key(state.rows[3])=="S", "movement deduplication lost original native rows")
    local hold=object("/Trigger.Hold",{HoldTimeThreshold=.5})
    state:rebuild({mapping(action,"W",{hold}),mapping(action,"W",{hold})})
    native:prepare()
    check(#backend.dynamic_config.bindings==2, "temporal movement pipelines lost their independent trigger state")
    local unknownA=modifier("InputModifierSmooth",{})
    local unknownB=modifier("InputModifierSmooth",{})
    state:rebuild({mapping(action,"W",{},{unknownA}),mapping(action,"W",{},{unknownB})})
    native:prepare()
    check(#backend.dynamic_config.bindings==2, "stateful or unknown movement modifiers were merged")
end)

test("cached ownership detects in-place trigger eligibility changes", function()
    local actionPath="/Game/Use"
    local action=object(actionPath .. ".Use")
    local pressed=object("/Trigger.Pressed",{ActuationThreshold=.5})
    local backend,native,state=fixture({unified_native=true,native_actions={actionPath}})
    state.rows={mapping(action,"E",{pressed})}
    native:prepare();native:prepare()
    pressed.ActuationThreshold=0
    native:prepare()
    check(key(state.rows[1])=="E" and #backend.dynamic_config.bindings==0,
        "cached ownership ignored a trigger becoming ineligible for scheduling")
    pressed.ActuationThreshold=.5
    native:prepare()
    check(key(state.rows[1])=="None" and #backend.dynamic_config.bindings==1,
        "restoring a trigger threshold did not restore scheduling")
    action.Triggers=array({object("/Trigger.Hold",{HoldTimeThreshold=.5})})
    native:prepare()
    check(key(state.rows[1])=="E" and #backend.dynamic_config.bindings==0,
        "cached ownership ignored newly installed action-level temporal triggers")
end)

test("native overrides retain dependent pipelines during startup and live apply", function()
    for _, live in ipairs({false,true}) do
        for _, triggerName in ipairs({"Cooldown","ChordAction"}) do
            local actionPath=triggerName=="Cooldown" and "/SGameLocomotionDriver/Input/IA_Crouch"
                or "/SGameCombatSystem/Weapon/Input/IA_ChangeWeapon"
            local action=object(actionPath .. "." .. actionPath:match("/([^/]+)$"))
            local target={kind="mapping",mapping="native",device="keyboard",actions={actionPath},native_override=true}
            local backend,native,state=fixture({unified_native=true,native_actions={actionPath},
                targets={native=target},bindings={{id="native",keys={"J"},outputs={"native"}}},
                native_bindings={{["function"]="native",device="keyboard",keys={"J"},actions={actionPath}}}})
            backend.live_validation=live
            state.rows={mapping(action,"C",{object("/Trigger." .. triggerName)})}
            native:prepare()
            check(key(state.rows[1])=="J" and #backend.dynamic_config.bindings==0,
                "valid native dependent pipeline was rejected or duplicated by the scheduler")
            native:prepare();native:restore()
            check(key(state.rows[1])=="C", "native dependent pipeline lost its original key")
            target.native_override=nil
            backend.live_validation=true
            local ok,err=pcall(function() native:prepare() end)
            check(not ok and tostring(err):find("No runtime input pipeline",1,true),
                "custom enhanced output accepted an unsupported dependent pipeline")
        end
    end
end)

test("shadow weapon switching keeps runtime chord blockers authoritative over templates", function()
    for _, live in ipairs({false,true}) do
        for _, inputKey in ipairs({"Two","J"}) do
            local actionPath="/SGameCombatSystem/Weapon/Input/IA_ChangeShachiWeapon"
            local action=object(actionPath .. ".IA_ChangeShachiWeapon")
            local target={kind="mapping",mapping="IA_SwitchShadowArm",device="keyboard",actions={actionPath},native_override=true}
            local backend,native,state=fixture({unified_native=true,native_actions={actionPath},
                targets={keyboard=target,pad={kind="mapping",device="gamepad",actions={actionPath},native_override=true}},
                bindings={{id="keyboard",keys={inputKey},trigger="down",outputs={"keyboard"}},
                    {id="pad",keys={"Gamepad_DPad_Right"},trigger="down",outputs={"pad"}}},
                native_bindings={{["function"]="IA_SwitchShadowArm",device="keyboard",keys={inputKey},actions={actionPath}},
                    {["function"]="IA_SwitchShadowArm",device="gamepad",keys={"Gamepad_DPad_Right"},actions={actionPath}}}})
            backend.live_validation=live
            local pressed=object("/Trigger.Pressed")
            state.rows={mapping(action,"Two",{pressed,object("/Trigger.ChordBlocker")}),
                mapping(action,"Gamepad_DPad_Right",{pressed})}
            state:add_context({mapping(action,"Two",{pressed}),mapping(action,"Gamepad_DPad_Right",{pressed})})
            native:prepare()
            check(key(state.rows[1])==inputKey and key(state.rows[2])=="None",
                "runtime keyboard blocker or scheduled controller ownership changed")
            local router=Router.new(backend.dynamic_config,backend)
            router:step(0,{},backend:context())
            router:step(.1,{[inputKey]=1},backend:context())
            router:step(.2,{[inputKey]=1},backend:context())
            router:step(.3,{},backend:context())
            check(#state.calls==0, "keyboard shadow switch was injected alongside its retained native pipeline")
            router:step(.4,{Gamepad_DPad_Right=1},backend:context())
            check(#state.calls==1 and #state.calls[1].target.trigger_objects==1,
                "retained keyboard pipeline prevented controller scheduling")
            router:reset(true)
            native:prepare()
            native:restore()
            check(key(state.rows[1])=="Two" and key(state.rows[2])=="Gamepad_DPad_Right",
                "restoring shadow switching lost the original keys")
            target.native_override=nil
            backend.live_validation=true
            local ok,err=pcall(function() native:prepare() end)
            check(not ok and tostring(err):find("No runtime input pipeline",1,true),
                "custom output bypassed a runtime chord blocker through the default template")
        end
    end
end)

test("scheduled outputs do not hide retained capacity failures", function()
    for _, live in ipairs({false,true}) do
        for _, sameAction in ipairs({false,true}) do
            local firstPath="/Game/First"
            local secondPath=sameAction and firstPath or "/Game/Second"
            local first=object(firstPath .. ".First")
            local second=sameAction and first or object(secondPath .. ".Second")
            local actions=sameAction and {firstPath} or {firstPath,secondPath}
            local backend,native,state=fixture({unified_native=true,native_actions=actions,
                targets={mixed={kind="mapping",mapping="mixed",device="keyboard",actions=actions,native_override=true}},
                bindings={{id="mixed",keys={"J"},outputs={"mixed"}}},
                native_bindings={{["function"]="mixed",device="keyboard",keys={"J","K"},actions=actions}}})
            backend.live_validation=live
            state.rows={mapping(first,"E"),mapping(second,"E",{object("/Trigger.ChordAction")})}
            local ok,err=pcall(function() native:prepare() end)
            check(not ok and tostring(err):find("active runtime slots",1,true),
                "a scheduled pipeline hid a retained pipeline's insufficient key slots")
            check(key(state.rows[1])=="E" and key(state.rows[2])=="E", "capacity rejection partially changed native input")
            backend.config.native_bindings[1].keys={"J"}
            native:prepare()
            check(key(state.rows[1])=="None" and key(state.rows[2])=="J", "valid mixed ownership could not recover after rejection")
            native:restore()
            check(key(state.rows[1])=="E" and key(state.rows[2])=="E", "mixed ownership failed to restore original input")
        end
    end
end)

test("frame lifecycle keeps routing through context changes, live reload and pawn replacement", function()
    local interactPath="/Game/PBZContent/Input/InputActions/IA_Interaction"
    local movePath="/SGameLocomotionDriver/Input/IA_KeyboardMoveInput"
    local crouchPath="/SGameLocomotionDriver/Input/IA_Crouch"
    local interact=object(interactPath .. ".IA_Interaction")
    local move=object(movePath .. ".IA_KeyboardMoveInput")
    local crouch=object(crouchPath .. ".IA_Crouch")
    local pressed,cooldown=object("/Trigger.Pressed"),object("/Trigger.Cooldown")
    local function config(key)
        return {enabled=true,require_foreground=false,unified_native=true,dynamic_capture=true,runtime_hotkeys=false,
            native_actions={interactPath,movePath,crouchPath},
            targets={interact={kind="mapping",mapping="IA_Interact",device="keyboard",actions={interactPath}},
                crouch={kind="mapping",mapping="IA_Crouch",device="keyboard",actions={crouchPath},native_override=true}},
            bindings={{id="interact",keys={key},trigger="down",outputs={"interact"}},
                {id="crouch",keys={"J"},trigger="down",outputs={"crouch"}}},
            native_bindings={{["function"]="IA_Crouch",device="keyboard",keys={"J"},actions={crouchPath}}}}
    end
    local current=config("K")
    local backend,native,state=fixture(current)
    backend.refresh,backend.shutdown=Backend.refresh,Backend.shutdown
    state.pawn,backend.pawn=object("/Pawn.First"),nil
    local subsystem=backend.subsystem
    state.input_layer=true
    function backend:gameplay_input_layer() return state.input_layer end
    function backend:library() return subsystem end
    function backend:scheme_notice() end
    local nativeInvoke,nativeObject=backend.invoke,backend.object
    function backend:object(path)
        if path=="/Script/EnhancedInput.EnhancedInputLocalPlayerSubsystem" then return subsystem end
        return nativeObject(self,path)
    end
    function backend:invoke(receiver,path,...)
        if path=="/Script/Engine.Controller:K2_GetPawn" then return state.pawn end
        if path=="/Script/Engine.Controller:IsMoveInputIgnored" or path=="/Script/Engine.GameplayStatics:IsGamePaused" then return false end
        if path=="/Script/Engine.SubsystemBlueprintLibrary:GetLocalPlayerSubSystemFromPlayerController" then return subsystem end
        return nativeInvoke(self,receiver,path,...)
    end
    local function scene(active)
        local rows={mapping(move,"W"),mapping(crouch,"C",{cooldown})}
        if active then rows[#rows+1]=mapping(interact,"E",{pressed}) end
        state:rebuild(rows)
    end
    scene(false)
    package.loaded.backend,package.loaded.router,package.loaded["router-config"]=nil,nil,nil
    package.preload.backend=function() return {new=function() return backend end} end
    package.preload.router=function() return Router end
    package.preload["router-config"]=function() return current end
    local frame
    EngineTickAvailable=true
    function LoopInGameThreadAfterFrames(_,callback) frame=callback;return 1 end
    function CancelDelayedAction() end
    function StaticFindObject() return nil end
    local handle=dofile(scripts .. "main.lua")
    frame()
    state.physical.W,state.physical.K=1,1
    local function fires(action)
        state.calls={};frame()
        for _,call in ipairs(state.calls) do if call.target.object==action and call.value.x==1 then return true end end
        return false
    end
    check(fires(move), "initial absent scene suspended the real backend frame loop")
    scene(true)
    check(not fires(interact), "context activation replayed held interaction through the frame loop")
    state.physical.K=0;frame();state.physical.K=1
    check(fires(interact), "returning interaction could not execute through the frame loop")
    scene(false)
    check(fires(move), "context removal suspended the frame loop")
    current=config("L")
    check(handle.reload() and key(state.rows[2])=="C", "live reload rejected dormant interaction or retained cooldown, or failed to restore native input")
    check(fires(move) and key(state.rows[2])=="J", "live reload failed to resume movement and native ownership")
    local first,second=backend.config,config("M")
    first.schemes={first=first,second=second};first.scheme_order={"first","second"}
    check(handle.switch_scheme("second") and backend.config==second, "scheme switch rejected a dormant scene action")
    check(fires(move), "scheme switch stopped unrelated movement")
    state.input_layer=false
    check(not fires(move) and key(state.rows[1])=="W", "menu transition did not restore native movement")
    state.input_layer=true
    check(fires(move), "return from menu failed to resume movement")
    state.pawn=object("/Pawn.Second");scene(true)
    check(fires(move), "pawn replacement failed to rebuild native input")
    state.physical.M=1
    check(fires(interact), "replacement pawn did not receive a fresh scene interaction")
    state:rebuild({});frame();scene(true)
    check(fires(move), "an empty mapping context permanently suspended routing")
    handle.stop()
    check(key(state.rows[1])=="W" and key(state.rows[2])=="C" and key(state.rows[3])=="E",
        "lifecycle shutdown failed to restore the current native rows")
end)

test("independent deflect pulses preserve trigger thresholds and mixed continuous outputs", function()
    local deflectPath,stancePath="/PBZDeflectSystem/Input/IA_Deflect","/PBZDeflectSystem/Input/IA_Stance"
    for _,trigger in ipairs({"down","hold","toggle"}) do
        local extra=object("/Game/IA_Extra.IA_Extra")
        local config={unified_native=true,native_actions={deflectPath,stancePath},targets={
            deflect={kind="mapping",catalog_pipeline=true,actions={deflectPath}},
            extra={kind="action",object=extra}},
            bindings={{id="mixed",keys={"G"},trigger=trigger,seconds=.3,outputs={"deflect","extra"}}}}
        local backend,native,state=fixture(config)
        local deflect,stance=object(deflectPath .. ".IA_Deflect"),object(stancePath .. ".IA_Stance")
        state.rows={mapping(deflect,"Two",{object("/Trigger.Pressed",{LastValue=0})}),
            mapping(stance,"Two",{object("/Trigger.Down",{LastValue=0})})}
        native:prepare()
        local router=Router.new(backend.dynamic_config,backend)
        local function step(now,keys)
            state.calls={};router:step(now,keys,{gameplay=true})
            local active={}
            for _,call in ipairs(state.calls) do
                if call.phase~="completed" and call.phase~="canceled" then active[call.target.object]=true end
            end
            return active
        end
        step(0,{})
        local early=step(.1,{G=1})
        check(early[deflect]==(trigger~="hold" and true or nil),"independent deflect ignored its hold threshold")
        local first=trigger=="hold" and step(.41,{G=1}) or early
        check(first[deflect] and first[stance] and first[extra],"mixed binding lost its deflect pulse or other output")
        local later=step(.5,{G=1})
        check(not later[deflect] and not later[stance] and later[extra],
            "deflect pulse changed another output's continuous trigger")
        local repeated=step(.6,{G=1})
        check(repeated[deflect] and repeated[stance] and repeated[extra],
            "sustained deflect failed to repeat after its native release frame")
        router:reset(true);native:restore()
    end
end)
local schemeFixtures = os.getenv("PBZ_SCHEME_FIXTURES")
test("independent deflect pulses and held stance retain native defense windows and release both states", function()
    local deflectPath,stancePath="/PBZDeflectSystem/Input/IA_Deflect","/PBZDeflectSystem/Input/IA_Stance"
    for _,device in ipairs({"keyboard","pad"}) do
        local pad=device=="pad"
        local rb,lb=pad and "Gamepad_RightShoulder" or "F9",pad and "Gamepad_LeftShoulder" or "F10"
        local nativeKey=pad and "Gamepad_DPad_Right" or "Two"
        local config=schemeFixtures and dofile(schemeFixtures .. "/" .. device .. "-guard.lua") or {
            unified_native=true,native_actions={deflectPath,stancePath},targets={
                deflect={kind="mapping",catalog_pipeline=true,device=pad and "gamepad" or "keyboard",actions={deflectPath}},
                stance={kind="mapping",catalog_pipeline=true,device=pad and "gamepad" or "keyboard",actions={stancePath}}},
            bindings={{id="deflect",keys={rb},trigger="down",outputs={"deflect"}},
                {id="stance",keys={lb},trigger="down",outputs={"stance"}}}}
        local overlapKey=pad and "Gamepad_FaceButton_Right" or "F11"
        config.bindings[#config.bindings+1]={id="deflect-overlap",keys={overlapKey},trigger="down",outputs=config.bindings[1].outputs}
        local backend,native,state=fixture(config)
        local deflect,stance=object(deflectPath .. ".IA_Deflect"),object(stancePath .. ".IA_Stance")
        state.rows={mapping(deflect,nativeKey,{object("/Trigger.Pressed",{LastValue=0})}),
            mapping(stance,nativeKey,{object("/Trigger.Down",{LastValue=0})})}
        native:prepare()
        local inject,invoke={},backend.invoke
        backend.dispatch=Backend.dispatch
        function backend:invoke(receiver,method,action,value,modifiers,triggers)
            if method:find(":InjectInputVectorForAction",1,true) then
                inject[#inject+1]={action=action,value=value.X,triggers=triggers}
                return true
            end
            return invoke(self,receiver,method,action,value,modifiers,triggers)
        end
        local router=Router.new(backend.dynamic_config,backend)
        local holding,abilityHolding,stanceActive,perfect,now=false,false,false,0,0
        local normalWindow,perfectWindow,abilityWindow=0,0,0
        local function step(keys)
            inject={};now=now+1/120
            router:step(now,keys,backend:context())
            local deflectTriggered,stanceTriggered=false,false
            for _,entry in ipairs(inject) do
                for _,trigger in ipairs(entry.triggers) do
                    local active=entry.value>.5
                    if entry.action==deflect and active and trigger.LastValue<=.5 then deflectTriggered=true end
                    if entry.action==stance and active then stanceTriggered=true end
                    trigger.LastValue=entry.value
                end
            end
            -- CL-302256 Passive processes Stance before Deflect. Only Stance.Completed sends KeyUp.
            if stanceTriggered then holding=true
            elseif stanceActive then holding,abilityHolding=false,false end
            if deflectTriggered then
                perfect=perfect+1;holding=true;abilityHolding=true
                normalWindow,perfectWindow=now+.2,now+.1
                abilityWindow=normalWindow
            end
            -- Passive Tick activates the stance ability while its shared key state is held.
            if holding and not abilityHolding then abilityHolding=true end
            stanceActive=stanceTriggered
            return tostring(deflectTriggered) .. ":" .. tostring(stanceTriggered) .. ":" .. tostring(holding) .. ":" .. tostring(abilityHolding)
        end
        step({});step({[rb]=1});step({})
        check(perfect==1 and not holding and not abilityHolding,"tapping deflect left the Passive or ability in held stance")
        check(normalWindow>now and perfectWindow>now and abilityWindow==normalWindow,
            "native release shortened the passive or active ability defense window")
        step({[rb]=1})
        local deadline=normalWindow
        for frame=1,120 do
            step({[rb]=1})
            local pressed=frame%2==0
            check(holding==pressed and abilityHolding==pressed,"held deflect must alternate a native press and complete release")
            if pressed then
                check(normalWindow>deadline and abilityWindow==normalWindow,"repeated deflect failed to refresh the native window")
                deadline=normalWindow
            else
                check(normalWindow==deadline and abilityWindow==deadline,"release frame cleared the native defense window")
            end
        end
        check(perfect==62,"held deflect did not repeat once per complete press/release cycle")
        step({})
        for frame=1,120 do step({}) end
        check(perfect==62 and now>normalWindow and not holding and not abilityHolding,
            "releasing held deflect must stop additional requests and let its native window expire")
        local repeats=perfect
        step({[lb]=1})
        for frame=1,10 do step({[lb]=1});check(holding and abilityHolding,"held stance became a tap") end
        check(perfect==repeats,"stance-only input refreshed the perfect-deflect window")
        step({[rb]=1,[lb]=1});step({[lb]=1})
        check(holding and abilityHolding and perfect==repeats+1,"deflect release canceled a separately held stance")
        step({})
        check(not holding and not abilityHolding,"stance release did not clear both layers")
        step({[rb]=1,[lb]=1});step({[rb]=1})
        check(not holding and not abilityHolding and perfect==repeats+2,"a held deflect kept stance alive after its separate stance key was released")
        step({});step({[rb]=1});step({})
        check(perfect==repeats+3 and not holding and not abilityHolding,"deflect failed to rearm after overlapping inputs")
        step({[nativeKey]=1});step({[nativeKey]=1,[rb]=1});step({[nativeKey]=1})
        check(holding and abilityHolding,"enhanced deflect release canceled a held original guard input")
        step({})
        check(not holding and not abilityHolding,"original and enhanced guard sources did not release together")
        step({[rb]=1});router:reset(true);step({})
        check(not holding and not abilityHolding,"router reset left the shared guard state held")
        step({[nativeKey]=1})
        local originalWindow=normalWindow
        for frame=1,120 do step({[nativeKey]=1});check(holding and abilityHolding,"ordinary combined guard lost native sustained stance") end
        check(normalWindow==originalWindow,"ordinary combined guard repeatedly refreshed its perfect window")
        step({})
        check(not holding and not abilityHolding,"ordinary combined guard release left either key state held")
        local beforeRecovery=perfect
        router:resume({[nativeKey]=1},backend:context())
        step({[nativeKey]=1})
        check(holding and abilityHolding and perfect==beforeRecovery,
            "combined guard recovery must restore stance without refreshing perfect deflect")
        step({})
        router:resume({[lb]=1},backend:context())
        step({[lb]=1})
        check(holding and abilityHolding and perfect==beforeRecovery,
            "independent stance failed to recover its two native key states")
        step({})
        router:resume({[rb]=1},backend:context())
        for frame=1,3 do step({[rb]=1}) end
        check(not holding and not abilityHolding and perfect==beforeRecovery,
            "recovery replayed independent deflect or its paired stance pulse")
        step({})
        local nativeTap={}
        for frame=1,30 do nativeTap[frame]=step(frame%2==1 and {[nativeKey]=1} or {}) end
        for frame=1,30 do
            check(step({[rb]=1})==nativeTap[frame],
                "independent held deflect differs from repeated original short taps at frame " .. frame)
        end
        step({})
        local overlapStart=perfect
        step({[rb]=1})
        for frame=1,120 do
            step({[rb]=1,[overlapKey]=1})
            local pressed=frame%2==0
            check(holding==pressed and abilityHolding==pressed,
                "staggered deflect sources must share a complete native release frame")
        end
        check(perfect==overlapStart+61,"overlapping deflect sources stopped refreshing the native perfect window")
        step({})
        native:restore()
        check(key(state.rows[1])==nativeKey and key(state.rows[2])==nativeKey,"guard cleanup changed native bindings")
    end
end)
if schemeFixtures then
    local baseline = os.getenv("PBZ_INPUT_BENCHMARK_BASELINE")
    if baseline then
        test("native input performance comparison", function()
            stress_gc=false
            local current=NativeInput
            for _,implementation in ipairs({{name="before",module=dofile(baseline)},{name="after",module=current}}) do
                NativeInput=implementation.module
                local config=dofile(schemeFixtures .. "/factory-schemes.lua")
                local backend,native,state=fixture(config)
                local seen={}
                for _,override in ipairs(config.native_bindings) do
                    for _,path in ipairs(override.actions) do
                        local package=path:gsub("%.[^./]+$", "")
                        local identity=package .. ":" .. override.device
                        if not seen[identity] then
                            seen[identity]=true
                            local action=object(package .. "." .. package:match("/([^/]+)$"))
                            for _,key in ipairs(override.keys) do state.rows[#state.rows+1]=mapping(action,key) end
                        end
                    end
                end
                native:prepare()
                local frames=200
                state.mapping_reads=0
                collectgarbage("collect")
                local started=os.clock()
                for _=1,frames do native:prepare();native:apply_masks({},{}) end
                local elapsed=os.clock()-started
                print(string.format("NativeInputBenchmark %s: frames=%d rows=%d visits/frame=%.0f Lua-ms/frame=%.4f",
                    implementation.name,frames,#state.rows,state.mapping_reads/frames,elapsed*1000/frames))
                local router=Router.new(backend:effective_config(),backend)
                local snapshot={W=1}
                started=os.clock()
                for index=1,frames do
                    native:prepare()
                    router:step(index/60,snapshot,backend:context())
                    state.calls={}
                end
                print(string.format("GameplayLuaBenchmark %s: frames=%d rules=%d Lua-ms/frame=%.4f",
                    implementation.name,frames,#router.rules,(os.clock()-started)*1000/frames))
                native:restore()
            end
            NativeInput=current
        end)
    end

    test("every compiled native action tolerates context retirement with mixed pipeline ownership", function()
        -- Wrapper lifetime is stressed by the focused tests; this sweep varies action availability.
        stress_gc=false
        for _, live in ipairs({false,true}) do
            local compiled=dofile(schemeFixtures .. "/factory-schemes.lua")
            for _, id in ipairs(compiled.scheme_order) do
                local config=compiled.schemes[id]
                local backend,native,state=fixture(config)
                local originals,seen,actions={},{},{}
                for name,target in pairs(config.targets) do
                    if name:match("^native_override_") then check(target.native_override==true, "compiler lost native override ownership") end
                end
                for _,override in ipairs(config.native_bindings) do
                    for _,action in ipairs(override.actions) do
                        local package=action:gsub("%.[^./]+$", "")
                        if not seen[package] then actions[#actions+1]=package;seen[package]=true end
                        local identity=package .. ":" .. override.device
                        if not seen[identity] then
                            seen[identity]=true
                            local triggers={}
                            if package:match("/IA_Crouch$") then triggers={object("/Trigger.Cooldown")}
                            elseif override.device=="keyboard" and package:match("/IA_ChangeWeapon$") then triggers={object("/Trigger.ChordAction")} end
                            local actionObject=object(package .. "." .. package:match("/([^/]+)$"))
                            for _,key in ipairs(override.keys) do originals[#originals+1]={action=actionObject,key=key,triggers=triggers,path=package} end
                        end
                    end
                end
                backend.live_validation=live
                local function rebuild(absent)
                    local rows={}
                    for _,source in ipairs(originals) do
                        if source.path~=absent then rows[#rows+1]=mapping(source.action,source.key,source.triggers) end
                    end
                    state:rebuild(rows)
                end
                rebuild();native:prepare();native:restore()
                for _,absent in ipairs(actions) do
                    rebuild(absent);native:prepare()
                    for _,target in pairs(backend.dynamic_config.targets) do
                        if target.source_relation then check(target.object:GetFullName():gsub("^%S+ ", ""):gsub("%.[^./]+$", "")~=absent,
                            "retired compiled action kept a scheduled pipeline: " .. absent) end
                    end
                    check(#backend.dynamic_config.bindings>0, "retiring one action suspended the compiled scheme")
                    native:restore();rebuild();native:prepare();native:restore()
                end
                state:rebuild({});native:prepare()
                check(native.expanded~=nil, "empty runtime contexts rejected the compiled scheme")
                rebuild();native:prepare()
                check(#backend.dynamic_config.bindings>0, "compiled scheme could not recover after empty runtime contexts")
                native:restore()
            end
        end
    end)

    test("compiled factory schemes prepare with absent device mappings", function()
        for _, live in ipairs({false,true}) do
            local compiled=dofile(schemeFixtures .. "/factory-schemes.lua")
            for _, id in ipairs(compiled.scheme_order) do
                local config=compiled.schemes[id]
                local backend,native,state=fixture(config)
                local seen,empty={},false
                for _, override in ipairs(config.native_bindings) do
                    if override["function"]=="IA_MoveSlow" and override.device=="gamepad" and #override.keys==0 then empty=true end
                    for _, action in ipairs(override.actions) do
                        local package=action:gsub("%.[^./]+$", "")
                        local identity=package .. ":" .. override.device
                        if #override.keys>0 and not seen[identity] then
                            seen[identity]=true
                            local actionObject=object(package .. "." .. package:match("/([^/]+)$"))
                            for _, key in ipairs(override.keys) do state.rows[#state.rows+1]=mapping(actionObject,key) end
                        end
                    end
                end
                check(empty, "compiled snapshot omitted absent IA_MoveSlow controller mapping")
                backend.live_validation=live
                native:prepare();native:apply_masks()
                check(native.expanded~=nil, "compiled scheme did not prepare native input")
                native:restore()
                local original,sceneRows={},{}
                for _,row in ipairs(state.rows) do
                    original[#original+1]={action=row.Action,key=key(row)}
                    if not row.Action:GetFullName():find("/IA_Interaction",1,true) then
                        sceneRows[#sceneRows+1]=mapping(row.Action,key(row))
                    end
                end
                check(#sceneRows<#original, "compiled scheme fixture did not contain interaction mappings")
                state:rebuild(sceneRows)
                native:prepare()
                check(native.expanded~=nil and #backend.dynamic_config.bindings>0,
                    "compiled scheme suspended unrelated input while the interaction context was absent")
                local restored={}
                for _,row in ipairs(original) do restored[#restored+1]=mapping(row.action,row.key) end
                state:rebuild(restored)
                native:prepare()
                local interaction=false
                for _,target in pairs(backend.dynamic_config.targets) do
                    if target.source_relation and target.object:GetFullName():find("/IA_Interaction",1,true) then interaction=true end
                end
                check(interaction, "compiled scheme failed to restore returning interaction mappings")
                native:restore()
            end
        end
    end)
end

local generated = os.getenv("PBZ_SUPPRESSION_FIXTURES")
if generated then
    local function compiled(name, key)
        local config = dofile(generated .. "/" .. name .. ".lua")
        local backend, native, state = fixture(config)
        local actions = {}
        for _, override in ipairs(config.native_bindings) do
            for _, action in ipairs(override.actions) do actions[action] = override.device end
        end
        for _, target in pairs(config.targets) do
            if target.kind == "mapping" then
                for _, action in ipairs(target.actions) do actions[action] = target.device end
            end
        end
        for action, device in pairs(actions) do
            local package = action:gsub("%.[^./]+$", "")
            local full = package .. "." .. package:match("/([^/]+)$")
            state.rows[#state.rows+1] = mapping(object(full), device == "gamepad" and "Gamepad_FaceButton_Bottom" or "SpaceBar")
        end
        native:prepare()
        local router = Router.new(backend.dynamic_config, backend)
        router:step(0, {}, {gameplay=true})
        state.calls = {}
        router:step(.1, {[key]=1}, {gameplay=true})
        return router, state, config
    end
    for _, device in ipairs({"keyboard", "pad"}) do
        local first = device == "pad" and "Gamepad_FaceButton_Left" or "J"
        local second = device == "pad" and "Gamepad_FaceButton_Top" or "K"
        test("compiled " .. device .. " override suppression", function()
            local router, state = compiled(device .. "-hold", first)
            check(#state.calls == 0, "hold override fired on the first frame")
            router:step(.69, {[first]=1}, {gameplay=true})
            check(#state.calls == 0, "hold override fired before its threshold")
            router:step(.71, {[first]=1}, {gameplay=true})
            check(#state.calls > 0, "hold override did not fire after its threshold")
            local _, other = compiled(device .. "-hold", second)
            check(#other.calls > 0, "key-scoped hold blocked another native override key")
            local _, blocked = compiled(device .. "-disabled-block", first)
            check(#blocked.calls == 0, "disabled blocking rule revived the plain override")
            local _, allowed = compiled(device .. "-disabled-pass", first)
            check(#allowed.calls > 0, "disabled nonblocking rule still suppressed the override")
            for _, key in ipairs({first, second}) do
                local _, all = compiled(device .. "-global", key)
                check(#all.calls == 0, "action-wide suppression allowed a native override")
            end
            local _, partial, config = compiled(device .. "-partial", first)
            local suppressed = config.suppressions[1].action
            local kept, removed = false, false
            for _, call in ipairs(partial.calls) do
                local action = call.target.object:GetFullName():gsub("^%S+ ", ""):gsub("%.[^./]+$", "")
                if action == suppressed then removed = true end
                for _, override in ipairs(config.native_bindings) do
                    if override["function"] == "IA_Guard" then
                        for _, path in ipairs(override.actions) do
                            if path:gsub("%.[^./]+$", "") == action and action ~= suppressed then kept = true end
                        end
                    end
                end
            end
            check(not removed and kept, "partial suppression lost an unblocked output or replayed a blocked action")
        end)
    end
    for _, name in ipairs({"runtime-move", "runtime-swap", "offline-move", "offline-swap"}) do
        test("compiled suppression after " .. name, function()
            local router, state = compiled(name, "J")
            check(#state.calls == 0, "moved override fired immediately beside its hold rule")
            router:step(.71, {J=1}, {gameplay=true})
            check(#state.calls > 0, "moved hold failed to execute")
        end)
    end
end

test("editable identities preserve fixed menu, touchpad and scene sources", function()
    local menuPath="/Game/PBZContent/Input/InputActions/IA_PushGameSystemMenu"
    local sensePath="/Game/PBZContent/Input/InputActions/IA_Sense_Default"
    local sprintPath="/SGameLocomotionDriver/Input/IA_Sprint"
    local menu=object(menuPath .. ".IA_PushGameSystemMenu")
    local sense=object(sensePath .. ".IA_Sense_Default")
    local sprint=object(sprintPath .. ".IA_Sprint")
    local function named(action,key,name,behavior,triggers)
        local result=mapping(action,key,triggers)
        result.SettingBehavior=behavior
        if behavior==1 then result.PlayerMappableKeySettings=object("/Settings.Mapping",{Name=FName(name)}) end
        return result
    end
    for _,live in ipairs({false,true}) do
        local config={unified_native=true,native_actions={sensePath,sprintPath},native_bindings={
            {["function"]="IA_OpenCharacterMenu",device="keyboard",keys={"J"},actions={menuPath}},
            {["function"]="IA_Sense_Default",device="gamepad",keys={"Gamepad_DPad_Up"},actions={sensePath}},
            {["function"]="IA_MoveFast",device="keyboard",keys={"K"},actions={sprintPath}}}}
        local backend,native,state=fixture(config);backend.live_validation=live
        sense.PlayerMappableKeySettings=object("/Settings.Action",{Name=FName("IA_Sense_Default")})
        state.rows={named(menu,"Escape",nil,0),named(menu,"I","IA_OpenCharacterMenu",1),
            named(sense,"PBZ_Touchpad_SwipeUp",nil,2,{object("/Trigger.Touchpad")}),
            named(sense,"Gamepad_Special_Left",nil,0,{object("/Trigger.TouchpadHold")}),
            named(sprint,"LeftShift",nil,2),named(sprint,"LeftShift","IA_MoveFast",1)}
        native:prepare();native:prepare()
        check(key(state.rows[1])=="Escape" and key(state.rows[2])=="J", "menu fixed input was remapped or ordinary edit was lost")
        check(key(state.rows[3])=="PBZ_Touchpad_SwipeUp" and key(state.rows[4])=="Gamepad_DPad_Up", "IgnoreSettings or inherited action settings lost ownership")
        local fixed,editable=0,0
        for _,binding in ipairs(backend.dynamic_config.bindings) do
            if binding.relation_key=="LeftShift" then
                if binding.when() then fixed=fixed+1 else editable=editable+1 end
            end
        end
        check(fixed==1 and editable==0,"fixed sprint must stay active without a permanently suppressed editable rule")
        check(native.maintained[5] and native.maintained[6] and key(state.rows[5])=="None" and key(state.rows[6])=="None",
            "removing a suppressed scheduler rule must retain native duplicate prevention")
        native:restore();native:apply_masks()
        check(key(state.rows[1])=="Escape" and key(state.rows[3])=="PBZ_Touchpad_SwipeUp", "UI reapplication overwrote fixed inputs")
        check(key(state.rows[2])=="J" and key(state.rows[6])=="K" and key(state.rows[5])=="LeftShift", "UI ownership ignored mapping identity")
        config.native_bindings[1].keys={"J","K"}
        local ok,err=pcall(function() native:prepare() end)
        check(not ok and tostring(err):find("1 active runtime slots",1,true),"fixed Escape incorrectly increased editable slot capacity")
        native:restore();config.enabled=false;native:apply_masks()
        check(key(state.rows[1])=="Escape" and key(state.rows[2])=="I" and key(state.rows[3])=="PBZ_Touchpad_SwipeUp", "disable failed to restore original sources")
    end
end)

test("catalog outputs share runtime pipelines and never bypass dependent triggers", function()
    local actionPath="/SGameCombatSystem/Weapon/Input/IA_ChangeShachiWeapon"
    local action=object(actionPath .. ".IA_ChangeShachiWeapon")
    for _,device in ipairs({"keyboard","gamepad"}) do
        local keyName=device=="keyboard" and "Two" or "Gamepad_DPad_Right"
        local other=device=="keyboard" and "Gamepad_DPad_Right" or "Two"
        for _,blocked in ipairs({false,true}) do
            local target={kind="mapping",catalog_pipeline=true,device=device,actions={actionPath}}
            local backend,native,state=fixture({unified_native=true,native_actions={actionPath},
                targets={catalog=target},bindings={{id="catalog",keys={keyName},trigger="down",outputs={"catalog"}}}})
            state.rows={mapping(action,keyName,{object(blocked and "/Trigger.ChordBlocker" or "/Trigger.Pressed")}),
                mapping(action,other,{object("/Trigger.Released")})}
            state:add_context({mapping(action,keyName,{object("/Trigger.Pressed")})})
            native:prepare()
            local router=Router.new(backend.dynamic_config,backend)
            router:step(0,{},backend:context());router:step(.1,{[keyName]=1},backend:context())
            if blocked then
                check(key(state.rows[1])==keyName and #state.calls==0,"catalog bypassed a native dependency through a default template")
            else
                check(key(state.rows[1])=="None" and #state.calls==1,"catalog and original same-key mapping emitted duplicate injections")
                local source=state.calls[1].target
                check(#source.trigger_objects==1 and source.trigger_objects[1]:GetClass()=="InputTriggerPressed", "catalog selected the other device's pipeline")
            end
            native:restore()
        end
    end
end)

if schemeFixtures then
    test("every gameplay action retains runtime dependencies over default templates", function()
        stress_gc=false
        local actions=dofile(schemeFixtures .. "/native-actions.lua")
        for _,actionPath in ipairs(actions) do
            for _,device in ipairs({"keyboard","gamepad"}) do
                for _,live in ipairs({false,true}) do
                    for _,kind in ipairs({"ChordAction","ChordBlocker","Cooldown","RepeatedTap"}) do
                        local keyName=device=="keyboard" and "Two" or "Gamepad_DPad_Right"
                        local replacement=device=="keyboard" and "J" or "Gamepad_DPad_Left"
                        local action=object(actionPath .. "." .. actionPath:match("/([^/]+)$"))
                        local backend,native,state=fixture({unified_native=true,native_actions={actionPath},
                            targets={target={kind="mapping",mapping="audit",device=device,actions={actionPath},native_override=true}},
                            bindings={{id="override",keys={replacement},trigger="down",outputs={"target"}}},
                            native_bindings={{["function"]="audit",device=device,keys={replacement},actions={actionPath}}}})
                        backend.live_validation=live
                        state.rows={mapping(action,keyName,{object("/Trigger." .. kind)})}
                        state:add_context({mapping(action,keyName,{object("/Trigger.Pressed")})})
                        native:prepare()
                        check(key(state.rows[1])==replacement,"dependent native pipeline lost its edited key")
                        local router=Router.new(backend.dynamic_config,backend)
                        router:step(0,{},backend:context());router:step(.1,{[replacement]=1},backend:context())
                        router:step(.2,{[replacement]=1},backend:context());router:step(.3,{},backend:context())
                        check(#state.calls==0,"default template bypassed runtime dependency: " .. actionPath .. " " .. kind)
                        native:restore()
                        check(key(state.rows[1])==keyName,"restore lost an original dependent source")
                    end
                end
            end
        end
    end)

    test("all serialized input contexts preserve fixed sources across compiled schemes", function()
        stress_gc=false
        local contexts=dofile("tests/input-router/fixtures/native-mappings-302256.lua")
        local library=dofile(schemeFixtures .. "/factory-schemes.lua")
        local mappings,actions=0,{}
        for _,context in ipairs(contexts) do
            for _,data in ipairs(context.rows) do mappings=mappings+1;actions[data[1]]=true end
        end
        check(#contexts==20 and mappings==173,"full context fixture lost a source")
        local actionCount=0;for _ in pairs(actions) do actionCount=actionCount+1 end
        check(actionCount==79,"full context fixture lost an InputAction")
        for _,live in ipairs({false,true}) do
            for _,id in ipairs(library.scheme_order) do
                local config=library.schemes[id]
                local backend,native,state=fixture(config);backend.live_validation=live
                local objects,originals={},{}
                for _,context in ipairs(contexts) do
                    local templates={}
                    for _,data in ipairs(context.rows) do
                        local action=objects[data[1]]
                        if not action then action=object(data[1] .. "." .. data[1]:match("/([^/]+)$"));objects[data[1]]=action end
                        if data[3]==0 and data[4] then action.PlayerMappableKeySettings=object("/Settings.Inherited",{Name=FName(data[4])}) end
                        local triggers={}
                        for _,triggerName in ipairs(data[5]) do
                            local class=triggerName:gsub("_%d+$", "")
                            local trigger=object("/Trigger.Fixture",{HoldTimeThreshold=.5})
                            function trigger:GetClass() return class end
                            triggers[#triggers+1]=trigger
                        end
                        local current=mapping(action,data[2],triggers);current.SettingBehavior=data[3]
                        if data[3]==1 and data[4] then current.PlayerMappableKeySettings=object("/Settings.Mapping",{Name=FName(data[4])}) end
                        state.rows[#state.rows+1]=current;templates[#templates+1]=current
                        originals[#originals+1]={key=data[2],fixed=not data[4],action=action,path=data[1]}
                    end
                    state:add_context(templates)
                end
                native:prepare();native:prepare()
                for index,original in ipairs(originals) do
                    if original.fixed and not original.path:match("/IA_ShachiAbility_LongPress$") then
                        if key(state.rows[index])=="None" then
                            local kept=false
                            for _,binding in ipairs(backend.dynamic_config.bindings) do
                                if binding.relation_key==original.key and binding.when() then
                                    local spec=target_for(backend.dynamic_config,binding.outputs[1])
                                    if spec.object==original.action then kept=true end
                                end
                            end
                            check(kept,"fixed source was masked without an active scheduler relation: " .. original.path .. " " .. original.key)
                        else check(key(state.rows[index])==original.key,"ordinary override changed fixed source: " .. original.path .. " " .. original.key) end
                    end
                end
                native:restore()
                for index,original in ipairs(originals) do check(key(state.rows[index])==original.key,"restore changed a serialized source") end
            end
        end
    end)

    test("compiled catalog defaults preserve pressed and released events through the real backend", function()
        for _,device in ipairs({"keyboard","pad"}) do
            for _,name in ipairs({"IA_ChangeShachiWeapon","IA_Execution","IA_AttackL_Press","IA_ShachiAbility"}) do
                local config=dofile(schemeFixtures .. "/" .. device .. "-" .. name .. ".lua")
                local spec=config.targets.output_0
                check(spec.kind=="mapping" and spec.catalog_pipeline,"compiler left a catalog default as raw action injection")
                local action=object(spec.path .. "." .. name)
                local nativeKey=device=="keyboard" and "Two" or "Gamepad_DPad_Right"
                local boundKey=device=="keyboard" and "F9" or "Gamepad_DPad_Down"
                local released=device=="pad" and name=="IA_ChangeShachiWeapon"
                local backend,native,state=fixture(config)
                state.rows={mapping(action,nativeKey,{object(released and "/Trigger.Released" or "/Trigger.Pressed",{LastValue=0})})}
                native:prepare()
                local invoke=backend.invoke
                local emitted=0
                function backend:invoke(receiver,method,...)
                    local args={...}
                    if method:find(":InjectInputVectorForAction",1,true) then
                        local value,triggers=args[2],args[4]
                        check(#triggers==1,"mapping trigger was lost before injection")
                        for _,trigger in ipairs(triggers) do
                            local was,active=trigger.LastValue>.5,value.X>.5
                            if released and was and not active or not released and active and not was then emitted=emitted+1 end
                            trigger.LastValue=value.X
                        end
                        return true
                    end
                    return invoke(self,receiver,method,...)
                end
                backend.dispatch=Backend.dispatch
                local router=Router.new(backend.dynamic_config,backend)
                router:step(0,{},backend:context())
                for frame=1,3 do router:step(frame*.1,{[boundKey]=1},backend:context()) end
                check(emitted==(released and 0 or 1),"holding a catalog default repeated its edge event or fired before release")
                router:step(.4,{},backend:context())
                check(emitted==1,"catalog binding did not retain the device's release semantics")
                for frame=5,7 do router:step(frame*.1,{[boundKey]=1},backend:context()) end
                router:step(.8,{},backend:context())
                check(emitted==2,"catalog trigger failed to rearm on the next physical input")
                native:restore()
            end
        end
    end)
end

test("shared jump/dodge retains native press and hold pipelines", function()
    for _, boundKey in ipairs({"SpaceBar", "Gamepad_FaceButton_Bottom"}) do
        for _, enhanced in ipairs({false, true}) do
            local jumpPath, dodgePath = "/PBZInput/InputActions/IA_Jump", "/PBZDeflectSystem/Input/IA_Dodge"
            local holdPath = "/PBZDeflectSystem/Input/IA_DodgeHold"
            local config = {unified_native=true, native_actions={jumpPath,dodgePath,holdPath}, targets={}, bindings={}}
            if enhanced then
                config.targets.jump={kind="mapping", actions={jumpPath}, mapping="IA_Jump"}
                config.targets.dodge={kind="mapping", actions={dodgePath,holdPath}, mapping="IA_Dodge"}
                config.bindings={{id="shared",keys={boundKey},outputs={"jump","dodge"}}}
                config.suppressions={{action=jumpPath},{action=dodgePath},{action=holdPath}}
            end
            local backend, native, state = fixture(config)
            local jump = object(jumpPath .. ".IA_Jump")
            local dodge = object(dodgePath .. ".IA_Dodge")
            local dodgeHold = object(holdPath .. ".IA_DodgeHold")
            state.rows={mapping(jump,boundKey,{object("/Trigger.Pressed")}),
                mapping(dodge,boundKey,{object("/Trigger.Pressed")}),
                mapping(dodgeHold,boundKey,{object("/Trigger.Down")})}
            state:add_context(state.rows)
            native:prepare()
            check(key(state.rows[1]) == "None" and key(state.rows[2]) == "None" and key(state.rows[3]) == "None",
                "all shared native rows must be owned by the scheduler")
            local emitted = {}
            local invoke = backend.invoke
            function backend:invoke(receiver, method, ...)
                local args={...}
                if method == "/Script/EnhancedInput.EnhancedInputSubsystemInterface:InjectInputVectorForAction" then
                    local action, value, triggers = args[1], args[2], args[4]
                    for _, trigger in ipairs(triggers) do
                        local was, active = (trigger.LastValue or 0) > .5, value.X > .5
                        if active and not was then emitted[#emitted+1] = action end
                        trigger.LastValue = value.X
                    end
                    return true
                end
                return invoke(self,receiver,method,...)
            end
            backend.dispatch=Backend.dispatch
            local router=Router.new(backend.dynamic_config,backend)
            router:step(0,{},backend:context())
            for frame=1,3 do router:step(frame*.1,{[boundKey]=1},backend:context()) end
            check(#emitted == 3, "shared press must retain jump, dodge and dodge-hold inputs")
            local seen = {}
            for _,action in ipairs(emitted) do seen[action] = true end
            check(seen[jump] and seen[dodge] and seen[dodgeHold], "shared mappings lost a native input action")
            router:step(.4,{[boundKey]=1},backend:context())
            check(#emitted == 3, "held input must retain native trigger semantics")
            router:step(.5,{},backend:context())
            router:step(.6,{[boundKey]=1},backend:context())
            check(#emitted == 6, "release must rearm every native jump/dodge pipeline")
            router:reset(true)
            native:restore()
            check(key(state.rows[1]) == boundKey and key(state.rows[2]) == boundKey and key(state.rows[3]) == boundKey,
                "shared-input routing damaged native mapping restoration")
        end
    end
end)

for _, message in ipairs(failures) do print("FAIL " .. message) end
assert(#failures == 0, #failures .. " NativeInput test case(s) failed")
print("NativeInputTests: " .. passed .. " assertions passed")
