package.loaded["live-config"] = {new=function() return {poll=function() end} end}
local scripts = "prototype/input-router/ue4ss/Scripts/"
package.path = scripts .. "?.lua;" .. package.path
local NativeInput = dofile(scripts .. "native-input.lua")
local Backend = dofile(scripts .. "backend.lua")
local Router = dofile(scripts .. "router.lua")
local passed, failures, nextAddress = 0, {}, 100
local stress_gc = true

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
    return {ForEach = function(self, callback)
        -- UE4SS removes its receiver from the Lua stack before calling back.
        local receiver = setmetatable({self}, {__mode = "v"})
        self = nil
        for index, value in ipairs(items) do
            if stress_gc then collectgarbage("collect") end
            assert(receiver[1], "native array receiver collected during ForEach")
            local item = transform and transform(value) or value
            callback(index - 1, {get = function() return item end})
        end
    end}
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
        return row[name]
    end})
end

local function fixture(config)
    config = config or {targets = {}, bindings = {}}
    config.suppressions = config.suppressions or {}
    config.targets, config.bindings = config.targets or {}, config.bindings or {}
    local state = {rows = {}, contexts = {}, physical = {}, calls = {}, now = 0, clones = 0}
    local backend = Backend.new(config, function() end)
    backend.physical_input = {sample = function() return nil end}
    backend.controller = object("/Controller.Controller")
    backend.subsystem = object("/Subsystem.Subsystem")
    backend.state = {ready = true, gameplay = true, generation = 1}
    local input = object("/PlayerInput.PlayerInput")
    backend.controller.PlayerInput = input
    function input:GetPropertyValue(name)
        if name == "EnhancedActionMappings" then return array(state.rows, wrapper) end
        assert(name == "AppliedInputContextData", "unexpected input property")
        return {ForEach = function(_, callback)
            for _, context in ipairs(state.contexts) do
                callback({get = function() return context end}, {get = function() return {} end})
            end
        end}
    end
    function StaticConstructObject(class, outer, _, _, _, copyTransients, copyArchetype, template)
        check((class == "test-class" or class:match("^InputTrigger")) and outer == backend.subsystem, "pipeline clone has wrong owner/class")
        check(copyTransients == false and copyArchetype == false and template ~= nil,
            "pipeline clone must use its native object template")
        local properties = {}
        for key, value in pairs(template) do if type(value) ~= "function" then properties[key] = value end end
        state.clones = state.clones + 1
        return object("/Clone.Clone" .. state.clones, properties)
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
            return array(rows, wrapper)
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
    check(custom == automatic, "automatic and custom sources cloned the same native pipeline twice")
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

test("unified main lifecycle resumes W and sprint but never held execution", function()
    local movePath,fastPath,executePath="/SGameLocomotionDriver/Input/IA_KeyboardMoveInput","/SGameLocomotionDriver/Input/IA_Sprint","/Game/IA_Execute"
    local backend,native,state=unified({movePath,fastPath,executePath})
    local config=backend.config
    config.enabled,config.runtime_hotkeys=true,false
    local move,fast,execute=object(movePath .. ".IA_KeyboardMoveInput"),object(fastPath .. ".IA_Sprint"),object(executePath .. ".IA_Execute")
    state.rows={mapping(move,"W"),mapping(fast,"W"),mapping(execute,"E")}
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
    check(#state.calls == 3, "unified main did not start W, sprint and E together")
    state.calls={};backend.state.generation=2;frame()
    local function recovered()
        local found={};for _,call in ipairs(state.calls) do if call.value.x ~= 0 then found[call.target.object]=true end end
        return found[move] and found[fast] and not found[execute]
    end
    check(recovered(), "pawn generation replayed held E or failed to restore movement")
    backend.state.gameplay=false;state.calls={};frame()
    check(key(state.rows[1]) == "W" and key(state.rows[2]) == "W" and key(state.rows[3]) == "E", "UI transition did not restore native key ownership")
    for _,call in ipairs(state.calls) do check(call.phase == "canceled", "UI exit forwarded an ordinary physical release") end
    backend.state.gameplay=true;state.calls={};frame()
    check(recovered(), "return from UI failed to recover W/sprint or replayed E")
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
    check(native.maintained[1] and key(state.rows[2])=="None", "unbound controller mapping became a second native binding")
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

local schemeFixtures = os.getenv("PBZ_SCHEME_FIXTURES")
if schemeFixtures then
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

for _, message in ipairs(failures) do print("FAIL " .. message) end
assert(#failures == 0, #failures .. " NativeInput test case(s) failed")
print("NativeInputTests: " .. passed .. " assertions passed")
