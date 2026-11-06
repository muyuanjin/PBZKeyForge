local config = dofile("dist/movement-bindings.lua")
local Router = dofile("prototype/input-router/ue4ss/Scripts/router.lua")
local backend = {events = {}}
function backend:dispatch(target, phase, value)
    self.events[#self.events + 1] = {path = target.path, phase = phase, value = value}
    return true
end
local router = Router.new(config, backend)
local function step(time, keys)
    backend.events = {}
    router:step(time, keys, {gameplay = true})
    return backend.events
end
local function movement(events, x, y)
    for _, event in ipairs(events) do
        if event.path == "/SGameLocomotionDriver/Input/IA_KeyboardMoveInput"
            and event.phase ~= "completed" and event.value.x == x and event.value.y == y then return true end
    end
    return false
end
assert(movement(step(0, {I = 1}), 1, 0), "Forward binding lost the native X axis")
assert(movement(step(.1, {K = 1}), -1, 0), "Backward binding lost its negative X axis")
assert(movement(step(.2, {H = 1}), 0, -1), "Left binding lost its negative Y axis")
assert(movement(step(.3, {L = 1}), 0, 1), "Right binding lost its Y axis")
local events = step(.4, {})
assert(#events == 1 and events[1].phase == "completed", "Releasing movement did not end injection")
events = step(.5, {F9 = 1})
assert(#events == 1 and events[1].path == "/SGameCombatSystem/Weapon/Input/IA_ChangeArmed", "Standalone sheathing did not dispatch")
events = step(.6, {F9 = 1})
for _, event in ipairs(events) do assert(event.phase == "completed", "Holding a standalone sheathing key retriggered it") end
print("MovementBindingsTests PASS: native directional vectors, release and single-press sheathing")
