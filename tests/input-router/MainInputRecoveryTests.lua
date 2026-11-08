package.loaded["script-context"] = {new=function() error("Unexpected tuning in an untuned fixture") end}
package.loaded["live-config"] = {new=function() return {poll=function() end} end}
local scripts = "prototype/input-router/ue4ss/Scripts/"
package.path = scripts .. "?.lua;" .. package.path
local Backend = require("backend")
local Router = require("router")
local passed = 0
local function check(value, message)
    assert(value, message)
    passed = passed + 1
end

local config = {enabled = true, runtime_hotkeys = false, require_foreground = false,
    targets = {
        move = {kind = "action", path = "move", resume_continuous = true},
        fast = {kind = "action", path = "fast", resume_continuous = true},
        interact = {kind = "event", tag = "interact"},
        animation = {kind = "event", tag = "animation"}
    }, bindings = {
        {id = "move", keys = {"W"}, trigger = "down", outputs = {"move"}},
        {id = "fast", keys = {"W"}, trigger = "down", outputs = {"fast"}},
        {id = "interact", keys = {"W"}, trigger = "press", outputs = {"interact"}},
        {id = "animation", keys = {"V"}, trigger = "press", outputs = {"animation"}}
    }}
local physical, engine, events = {}, {}, {}
local backend = Backend.new(config, function() end)
backend.controller = {IsValid = function() return true end, GetAddress = function() return 1 end}
backend.generation = 1
backend.session = {generation=1, context=function() return backend.state end,
    assert_current=function() return true end, validate_pawn=function() return true end,
    snapshot=function() return backend.state end}
backend.state = {ready = true, gameplay = true, generation = 1}
backend.physical_input = {sample = function(_, key) return physical[key] or 0 end}
function FName(value) return {ToString = function() return value end} end
function backend:refresh() return true end
function backend:key_dimension() return 1 end
function backend:action_identity(target) return target.path end
function backend:pipeline_identity(target) return target.path end
function backend:time() self.now = (self.now or 0) + .01; return self.now end
function backend:invoke(_, method, key)
    local value = engine[key.KeyName:ToString()] or 0
    if method:find(":GetInputAnalogKeyState", 1, true) then return value end
    if method:find(":IsInputKeyDown", 1, true) then return value ~= 0 end
    error("unexpected engine call " .. method)
end
function backend:dispatch(target, phase, value)
    events[#events + 1] = {target = target.path or target.tag, phase = phase, value = value}
    return true
end
function backend:shutdown() end
local function count(target, phase)
    local result = 0
    for _, event in ipairs(events) do
        if event.target == target and (not phase or event.phase == phase) then result = result + 1 end
    end
    return result
end
local frame
package.loaded.backend, package.loaded.router = nil, nil
package.preload.backend = function() return {new = function() return backend end} end
package.preload.router = function() return Router end
package.preload["router-config"] = function() return config end
EngineTickAvailable = true
function LoopInGameThreadAfterFrames(_, callback) frame = callback; return 1 end
function RegisterHook() return 1,2 end
function UnregisterHook() end
function CancelDelayedAction() end
local lifecycle = dofile(scripts .. "main.lua")
frame()
physical.W, engine.W = 1, 1
frame()
check(count("move", "started") == 1 and count("fast", "started") == 1,
    "W must start both movement and continuous fast movement")
check(count("interact") == 1, "a physical W press must still trigger its edge action")
physical.V = 1
frame()
check(count("animation") == 1, "V did not trigger its animation action")

engine.W = 0
events = {}
frame()
check(count("move", "triggered") == 1 and count("fast", "triggered") == 1,
    "UE input flush interrupted physically held movement or fast movement")
check(count("interact") == 0 and count("animation") == 0,
    "UE input flush replayed an edge action")

backend.state.gameplay = false
frame()
events = {}
backend.state.gameplay = true
frame()
check(count("move", "started") == 1 and count("fast", "started") == 1,
    "returning from UI required W to be released and pressed again")
check(count("interact") == 0 and count("animation") == 0,
    "returning from UI replayed held interaction or animation")

backend.generation = 2
backend.session.generation = 2
backend.state.generation = 2
events = {}
frame()
check(count("move", "started") == 1 and count("fast", "started") == 1,
    "player generation change discarded continuous input")
check(count("interact") == 0 and count("animation") == 0,
    "player generation change replayed an edge action")

check(lifecycle.reload(), "valid reload failed")
events = {}
frame()
check(count("move", "started") == 1 and count("fast", "started") == 1,
    "configuration reload required continuous keys to be pressed again")
check(count("interact") == 0 and count("animation") == 0,
    "configuration reload replayed an edge action")

physical.W, physical.V = 0, 0
events = {}
frame()
check(count("move", "completed") == 1 and count("fast", "completed") == 1,
    "physical release failed to end continuous actions")
check(count("interact") == 0 and count("animation") == 0,
    "releasing an input held through recovery emitted an edge action")
physical.W, physical.V = 1, 1
events = {}
frame()
check(count("interact") == 1 and count("animation") == 1,
    "new physical presses did not rearm edge actions after recovery")
lifecycle.stop()
print("MainInputRecoveryTests: " .. passed .. " assertions passed")
