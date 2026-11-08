package.loaded["script-context"] = {new=function() error("Unexpected tuning in an untuned fixture") end}
local scripts = "prototype/input-router/ue4ss/Scripts/"
package.path = scripts .. "?.lua;" .. package.path
local Backend, Router, Moves = require("backend"), require("router"), require("complete-moves")
local passed = 0
local function check(value, message) assert(value, message); passed = passed + 1 end
local function object(values)
    values = values or {}
    function values:IsValid() return not self.invalid end
    function values:IsA(class) return self.class == class or self.base == class end
    function values:GetAddress() return self end
    function values:GetPropertyValue(name) return self[name] end
    return values
end

-- Exercise the native call boundary, including inherited methods and interfaces.
do
    local logs, calls = {}, 0
    local backend = Backend.new({require_foreground=false}, function(message) logs[#logs+1] = message end)
    backend.state.ready = true
    local class, other, functionClass = object(), object(), object()
    local fn = object({class=functionClass})
    function fn:GetOuter() return class end
    local receiver = object({base=class})
    function receiver:CallFunction() calls = calls + 1; return 42 end
    local library = object()
    function library:CallFunction(_, target, owner) return target.interface == owner end
    local interfaceCheck = object({class=functionClass})
    function backend:object(path)
        if path == "/Script/CoreUObject.Function" then return functionClass end
        if path:find(":DoesImplementInterface", 1, true) then return interfaceCheck end
        if path:find("Default__", 1, true) then return library end
        return fn
    end
    backend.pawn = receiver
    local target = {kind="function",path="/Game/Weapon_C:Attack"}
    local ok, result = backend:dispatch(target,"triggered",nil,"valid")
    check(ok and result == 42 and calls == 1, "inherited configured function was rejected")
    receiver.base, receiver.interface = nil, class
    ok, result = backend:dispatch(target,"triggered",nil,"interface")
    check(ok and result == 42 and calls == 2, "implemented interface was rejected")
    receiver.interface, receiver.class = nil, other
    ok = backend:dispatch(target,"triggered",nil,"wrong-weapon")
    check(not ok and calls == 2 and backend:target_failed(target,"wrong-weapon"), "wrong receiver reached native code")
    local reported = #logs
    for _=1,120 do backend:dispatch(target,"triggered",nil,"wrong-weapon") end
    check(calls == 2 and #logs == reported, "failed target retried native code or repeated diagnostics")
    backend.config = {require_foreground=false}
    receiver.base = class
    check(backend:dispatch(target,"triggered",nil,"reloaded") and calls == 3, "new configuration did not rearm target")
    backend:fail_target(target,"test failure")
    backend.generation = backend.generation + 1
    check(not backend:target_failed(target), "new player retained the previous player's failures")
end

-- A bad target must not stop the actual frame scheduler or its healthy sibling.
do
    local failures, healthy, shutdowns, predicates, registrations, missingLookups = 0, 0, 0, 0, 0, 0
    local config = {enabled=true, runtime_hotkeys=false, require_foreground=false, targets={
        bad={kind="lua",run=function() failures=failures+1; error("bad weapon blueprint") end},
        good={kind="lua",run=function() healthy=healthy+1; return true end}
    }, bindings={
        {id="bad",keys={"G"},trigger="pulse",interval=.01,outputs={"bad"}},
        {id="good",keys={"G"},trigger="pulse",interval=.01,outputs={"good"}}
    }, hooks={
        {path="/Script/Test:Missing"},
        {path="/Script/Test:Invalid"},
        {path="/Script/Test:Predicate",when=function() predicates=predicates+1; error("bad hook condition") end},
        {path="/Script/Test:Healthy",value=7}
    }}
    local backend = Backend.new(config,function() end)
    backend.state = {ready=true,gameplay=true,generation=1}
    function backend:refresh() return true end
    function backend:snapshot() return self.pressed and {G=1} or {} end
    function backend:time() self.now=(self.now or 0)+.02; return self.now end
    function backend:shutdown() shutdowns=shutdowns+1 end
    local frame, callbacks = nil, {}
    local clock = os.clock
    os.clock=function() return backend.now or 0 end
    function StaticFindObject(path)
        if path == "/Script/Test:Missing" then missingLookups=missingLookups+1; return nil end
        return object()
    end
    function RegisterHook(path, _, callback)
        if path == "/Script/Test:Invalid" then registrations=registrations+1; error("not a UFunction") end
        callbacks[path] = callback
        return 1, 2
    end
    function UnregisterHook() end
    package.loaded.backend = {new=function() return backend end}
    package.loaded["router-config"] = config
    package.loaded["live-config"] = {new=function() return {poll=function() end} end}
    EngineTickAvailable = true
    function LoopInGameThreadAfterFrames(_,callback) frame=callback; return 1 end
    function CancelDelayedAction() end
    local runtime = dofile(scripts .. "main.lua")
    frame(); backend.pressed=true
    for _=1,120 do
        frame()
        check(callbacks["/Script/Test:Predicate"]() == nil, "bad hook replaced the native result")
    end
    os.clock=clock
    check(failures == 2 and healthy == 120 and shutdowns == 0,
        "one bad target suspended the scheduler or kept throwing every frame")
    check(predicates == 1 and registrations == 1 and missingLookups <= 6,
        "bad hooks retried every frame or missing Blueprint hooks repeated lookup without delay")
    check(callbacks["/Script/Test:Healthy"]() == 7, "a failed hook disabled its healthy sibling")
    runtime.stop()
end

-- Abort releases only the failed move's input and preserves unrelated pending work.
do
    local backend = Backend.new({require_foreground=false},function() end)
    local failed, good = {kind="move",move_id="bad"}, {kind="move",move_id="good"}
    local instance = object({bIsActive=true})
    local stops, cancellations = 0, 0
    local input = {identity="input",owners=1,subsystem=object(),action=object()}
    local pending = {identity="bad",spec=failed,instance=instance,input=input}
    backend.complete_move_pending = {bad=pending,good={spec=good}}
    backend.complete_move_active = {bad=pending}
    backend.complete_move_inputs = {input=input}
    function backend:invoke(_,path)
        if path:find(":K2_CancelAbility",1,true) then cancellations=cancellations+1; instance.bIsActive=false
        elseif path:find(":StopContinuousInputInjectionForAction",1,true) then stops=stops+1
        else error("unexpected call " .. path) end
    end
    backend:fail_target(failed,"bad deferred blueprint")
    check(stops == 1 and cancellations == 1 and not backend.complete_move_pending.bad,
        "failed move left injected input or an active ability")
    check(backend.complete_move_pending.good and not next(backend.complete_move_inputs),
        "failed move removed unrelated work or retained its input")
    local broken = {identity="broken",spec=failed,instance=instance,input=input}
    input.owners=1; backend.complete_move_pending.bad=broken
    function backend:invoke() error("input restoration failed") end
    check(not pcall(backend.fail_target,backend,failed,"bad callback") and broken.input == input,
        "cleanup failure must escape to global restoration and retain pending cleanup")
end

-- Stable held dispatches use the compiled identities and consume list.
do
    local backend = {dispatch=function() return true end}
    local router = Router.new({targets={move={kind="move",move_id="held",tag="held"}},bindings={
        {id="held",keys={"G"},consume=true,outputs={"move"},damage_multiplier=2}
    }},backend)
    local concat, concatenations = table.concat, 0
    table.concat=function(...) concatenations=concatenations+1; return concat(...) end
    local started = os.clock()
    for frame=1,2000 do router:step(frame/60,{G=1},{}) end
    local elapsed = os.clock()-started
    table.concat=concat
    check(concatenations == 0, "stable held moves rebuilt static request identities")
    print(string.format("Held-move model: %.4f ms/frame (2000 frames)",elapsed*1000/2000))
end
print("RuntimeIsolationTests: " .. passed .. " assertions passed")
