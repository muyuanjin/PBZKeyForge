local Router = dofile("prototype/input-router/ue4ss/Scripts/router.lua")
local passed = 0
local function check(value, message)
    assert(value, message)
    passed = passed + 1
end
local function rig(binding, targets)
    targets = targets or {once = {kind = "event", tag = "once"}}
    local backend = {events = {}}
    function backend:dispatch(target, phase, value)
        self.events[#self.events + 1] = {name = target.path or target.tag, phase = phase, value = value}
        return true
    end
    local router = Router.new({targets = targets, bindings = {binding}}, backend)
    local function step(time, snapshot)
        backend.events = {}
        router:step(time, snapshot, {})
        return backend.events
    end
    return router, step
end
local function has(events, name, phase)
    for _, event in ipairs(events) do
        if event.name == name and (not phase or event.phase == phase) then return true end
    end
    return false
end

do
    local router, step = rig({id = "mixed", keys = {"W"}, trigger = "down",
        outputs = {"move", "pressed", "once"}}, {
        move = {kind = "action", path = "move", resume_continuous = true},
        pressed = {kind = "action", path = "pressed", resume_continuous = false},
        once = {kind = "event", tag = "once"}})
    router:resume({W = 1}, {})
    local events = step(0, {W = 1})
    check(#events == 1 and has(events, "move", "started"),
        "mixed output recovery must restore movement without injecting the Pressed pipeline")
    events = step(.1, {W = 1})
    check(#events == 1 and has(events, "move", "triggered"),
        "held mixed outputs replayed a one-shot action after recovery")
    events = step(.2, {})
    check(#events == 1 and has(events, "move", "completed"),
        "recovered mixed outputs did not release only the resumed movement")
    events = step(.3, {W = 1})
    check(#events == 3 and has(events, "move") and has(events, "pressed") and has(events, "once"),
        "a fresh press did not rearm every mixed output")
end

do
    local router, step = rig({id = "chord", keys = {"LB", "A"}, trigger = "press", outputs = {"once"}})
    router:resume({LB = 1}, {})
    check(#step(0, {LB = 1}) == 0, "an incomplete resumed chord fired")
    check(#step(.1, {LB = 1, A = 1}) == 1,
        "a held modifier prevented a newly pressed chord member from firing")
end

do
    local router, step = rig({id = "axis", keys = {"LB"}, axis = "Stick", trigger = "axis",
        threshold = .1, outputs = {"move", "pressed"}}, {
        move = {kind = "action", path = "move", resume_continuous = true},
        pressed = {kind = "action", path = "pressed"}})
    router:resume({LB = 1, Stick = -.6}, {})
    local events = step(0, {LB = 1, Stick = -.6})
    check(#events == 1 and has(events, "move") and events[1].value.x == -.6,
        "axis recovery lost signed movement or replayed an edge pipeline")
    events = step(.1, {Stick = -.6})
    check(#events == 1 and has(events, "move", "completed"),
        "releasing an axis chord modifier did not end recovered movement")
    events = step(.2, {LB = 1, Stick = -.6})
    check(#events == 2 and has(events, "pressed"),
        "a newly completed axis chord failed to rearm its edge pipeline")
end

for _, kind in ipairs({"press", "release", "tap", "hold", "pulse", "toggle", "multi_tap"}) do
    local router, step = rig({id = kind, keys = {"K"}, trigger = kind,
        seconds = .2, interval = .3, count = 2, outputs = {"once"}})
    router:resume({K = 1}, {})
    check(#step(0, {K = 1}) == 0, kind .. " replayed the key held during recovery")
    check(#step(1, {K = 1}) == 0, kind .. " replayed after a timing threshold during recovery")
    check(#step(1.1, {}) == 0, kind .. " emitted an action while releasing a recovered key")
    local pressed = step(2, {K = 1})
    if kind == "press" or kind == "pulse" or kind == "toggle" then
        check(#pressed == 1, kind .. " failed to rearm on a fresh press")
    elseif kind == "release" or kind == "tap" then
        check(#pressed == 0 and #step(2.1, {}) == 1, kind .. " failed to rearm for a new release")
    elseif kind == "hold" then
        check(#pressed == 0 and #step(2.3, {K = 1}) == 1, "hold failed to start a fresh timer")
    elseif kind == "multi_tap" then
        step(2.05, {})
        check(#pressed == 0 and #step(2.1, {K = 1}) == 1,
            "multi_tap counted a recovered press or failed to count fresh presses")
    end
end

do
    local router, step = rig({id = "sequence", trigger = "sequence", steps = {"A", "B", "A"},
        interval = .5, outputs = {"once"}})
    router:resume({A = 1}, {})
    check(#step(0, {A = 1}) == 0, "sequence replayed a key held during recovery")
    step(.1, {})
    step(.2, {B = 1})
    check(#step(.3, {A = 1}) == 0, "sequence counted the resumed A as its first step")
    step(.4, {B = 1})
    check(#step(.5, {A = 1}) == 1, "sequence failed to rearm after fresh A-B-A presses")
end

do
    local router, step = rig({id = "custom", watch = {"K"}, evaluate = function(_, snapshot, _, state)
        local held = snapshot.K == 1
        local pulse = held and not state.customHeld
        state.customHeld = held
        return {active = pulse, pulse = pulse, value = 1}
    end, outputs = {"once"}})
    router:resume({K = 1}, {})
    check(#step(0, {K = 1}) == 0, "custom trigger replayed an input held during recovery")
    step(.1, {})
    check(#step(.2, {K = 1}) == 1, "watch-only custom trigger never rearmed after physical release")
end

do
    local router, step = rig({id = "custom_release", watch = {"K", "Q"},
        evaluate = function(_, snapshot, _, state)
            local held = snapshot.K == 1 or snapshot.Q == 1
            local pulse = state.customHeld == true and not held
            state.customHeld = held
            return {active = pulse, pulse = pulse, value = 1}
        end, outputs = {"once"}})
    router:resume({K = 1, Q = 1}, {})
    check(#step(0, {K = 1, Q = 1}) == 0 and #step(1, {K = 1, Q = 1}) == 0,
        "a custom release trigger replayed while recovered keys remained held")
    check(#step(1.1, {Q = 1}) == 0 and #step(1.2, {}) == 0,
        "a partial or complete release of recovered watched keys emitted a custom action")
    check(#step(1.3, {K = 1}) == 0 and #step(1.4, {}) == 1,
        "a fresh watch-only press/release failed to rearm its custom release trigger")
end

do
    local router, step = rig({id = "context_only", evaluate = function()
        return {active = true, pulse = true, value = 1}
    end, outputs = {"once"}})
    router:resume({}, {})
    check(#step(0, {}) == 1, "an evaluator without watched keys was permanently suspended by recovery")
end

print("InputResumeTests: " .. passed .. " assertions passed")
