package.path = "prototype/input-router/ue4ss/Scripts/?.lua;" .. package.path
package.loaded["script-context"] = {new=function() error("Unexpected tuning") end}
local Backend, Router, Moves = require("backend"), require("router"), require("complete-moves")
local passed, now = 0, 0
local function check(value,message) assert(value,message); passed=passed+1 end
local originalClock = os.clock
os.clock = function() return now end

-- Failure tracking retains live cooldowns and releases retired targets.
do
    local backend=Backend.new({require_foreground=false},function() end)
    local active={kind="lua"}
    backend:fail_target(active,"active failure","binding")
    local retired=setmetatable({},{__mode="v"})
    local function retire()
        local target={kind="lua"}
        retired[1]=target
        backend:fail_target(target,"retired failure","binding")
        local unbound={kind="lua"}
        retired[2]=unbound
        backend:fail_target(unbound,"retired unbound failure")
    end
    retire()
    collectgarbage("collect")
    check(next(retired)==nil,"failure tracking retained retired targets")
    check(backend:target_failed(active,"binding"),"collecting retired targets erased a live cooldown")
    now=1
    backend:target_succeeded(active,"binding")
    now=31
    backend:target_succeeded(active,"binding")
    check(next(backend.failed_targets)==nil,"recovery retained an empty failure record")
    now=0
end

-- Same target, same key and other keys remain independently usable during backoff.
do
    local attempts, healthy, logs, healed = 0, 0, {}, false
    local shared = {kind="lua",run=function(_,context)
        if context.binding == "bad" then
            attempts=attempts+1
            if not healed then error("binding unavailable",0) end
        else healthy=healthy+1 end
        return true
    end}
    local config={enabled=true,require_foreground=false,targets={shared=shared},bindings={
        {id="bad",keys={"G"},trigger="pulse",interval=.001,outputs={"shared"}},
        {id="same-key",keys={"G"},trigger="pulse",interval=.001,outputs={"shared"}},
        {id="other-key",keys={"H"},trigger="pulse",interval=.001,outputs={"shared"}}
    }}
    local backend=Backend.new(config,function(message) logs[#logs+1]=message end)
    backend.state={ready=true,gameplay=true}
    local router=Router.new(config,backend)
    router:step(now,{},{})
    for frame=1,6000 do now=frame/100; router:step(now,{G=1,H=1},{}) end
    check(healthy==12000,"a failed binding blocked the same target on the same or another key")
    check(attempts>=5 and attempts<=7 and #logs==1,"persistent failures retried or logged every frame")
    check(backend:target_failed(shared,"bad") and not backend:target_failed(shared,"same-key"),
        "failure ownership was shared between bindings")
    healed=true
    for frame=6001,9300 do now=frame/100; router:step(now,{G=1,H=1},{}) end
    check(attempts>100 and healthy==18600,"a repaired held binding failed to recover automatically")
    check(not backend:target_failed(shared,"bad"),"recovered binding retained its cooldown")
end

-- A failed consumer must release its key reservation during cooldown.
do
    local healthy, failures=0,0
    local config={require_foreground=false,targets={bad={kind="lua",run=function()
        failures=failures+1;error("consumer unavailable")
    end},good={kind="lua",run=function() healthy=healthy+1 end}},bindings={
        {id="bad-consumer",keys={"G"},consume=true,trigger="pulse",interval=.001,outputs={"bad"}},
        {id="healthy",keys={"G"},trigger="pulse",interval=.001,outputs={"good"}}
    }}
    local backend=Backend.new(config,function() end)
    backend.state={ready=true,gameplay=true}
    local router=Router.new(config,backend)
    for frame=1,20 do now=95+frame/100;router:step(now,{G=1},{}) end
    check(failures==1 and healthy>=19,"failed consumer kept its same-key sibling locked until physical release")
end

-- Deduplicated moves may use a healthy binding if the preferred binding fails.
do
    local spec={kind="move",move_id="shared"}
    local backend=Backend.new({require_foreground=false},function() end)
    local calls={}
    function backend:dispatch(target,_,_,binding)
        calls[#calls+1]=binding
        if binding=="bad" then self:fail_target(target,"bad activation",binding); return false end
        return true
    end
    local router=Router.new({targets={move=spec},bindings={
        {id="bad",keys={"G"},outputs={"move"}}, {id="good",keys={"G"},outputs={"move"}}
    }},backend)
    router:step(now,{G=1},{})
    check(#calls==2 and calls[2]=="good","move deduplication discarded a healthy alternative")
end

-- Deferred cleanup is scoped to the binding that owns the activation.
do
    local calls={}
    local spec={kind="move",move_id="move:native-refusal"}
    local backend={}
    function backend:dispatch(_,_,_,binding)
        calls[#calls+1]=binding
        return true,binding=="last"
    end
    local router=Router.new({targets={move=spec},bindings={
        {id="first",keys={"G"},outputs={"move"}},
        {id="second",keys={"G"},outputs={"move"}},
        {id="last",keys={"G"},outputs={"move"}}
    }},backend)
    router:step(now,{G=1},{})
    check(#calls==3 and calls[3]=="last",
        "A normal native refusal must try every remaining equivalent binding until one actually activates")
end

-- Deferred cleanup is scoped to the binding that owns the activation.
do
    local backend=Backend.new({require_foreground=false},function() end)
    local spec={kind="move"}
    backend.complete_move_pending={bad={identity="bad",spec=spec,binding_id="bad",preparing=true},
        good={identity="good",spec=spec,binding_id="good",preparing=true}}
    backend:fail_target(spec,"deferred failure","bad")
    check(not backend.complete_move_pending.bad and backend.complete_move_pending.good,
        "deferred failure aborted another binding of the same move")
end

-- A broken predicate is isolated, throttled, and retried after it is repaired.
do
    local failures, healthy, repaired=0,0,false
    local backend=Backend.new({require_foreground=false},function() end)
    backend.state={ready=true,gameplay=true}
    local config={targets={good={kind="lua",run=function() healthy=healthy+1 end}},bindings={
        {id="predicate",keys={"G"},trigger="pulse",interval=.001,when=function()
            failures=failures+1; if not repaired then error("predicate unavailable") end; return true
        end,outputs={"good"}},
        {id="healthy",keys={"G"},trigger="pulse",interval=.001,outputs={"good"}}
    }}
    backend.config=config
    local router=Router.new(config,backend)
    for frame=1,50 do now=100+frame/100;router:step(now,{G=1},{}) end
    check(failures==1 and healthy==50,"predicate failure interrupted its same-key sibling or retried every frame")
    repaired=true;now=102;router:step(now,{G=1},{})
    check(failures==2 and healthy==52,"repaired predicate did not resume automatically")
end

-- Failed input injection releases only that pipeline; failed releases still require shared recovery.
do
    local backend=Backend.new({require_foreground=false},function() end)
    backend.state={ready=true}
    local valid={IsValid=function() return true end}
    backend.controller,backend.subsystem=valid,valid
    local calls={}
    function backend:invoke(_,_,_,value)
        calls[#calls+1]=value.X
        if value.X~=0 or self.release_fails then error("injection unavailable") end
    end
    local spec={kind="action",object=valid}
    check(backend:dispatch(spec,"started",nil,"bad") and #calls==2 and calls[2]==0,
        "failed injection did not release before isolation")
    backend:dispatch(spec,"triggered",nil,"bad")
    check(#calls==2,"isolated action ignored its cooldown")
    backend.release_fails=true
    check(not backend:dispatch(spec,"canceled",nil,"bad"),"failed cleanup was hidden by isolation")
end

-- Tuning preparation failures belong to the requesting binding.
do
    local backend=Backend.new({require_foreground=false},function() end)
    backend.state={ready=true}
    local action={IsValid=function() return true end,GetFullName=function() return "InputAction /Game/Shared.Shared" end}
    backend.controller,backend.subsystem=action,action
    local injected=0
    function backend:invoke() injected=injected+1;return true end
    backend.resource_costs={input=function(_,_,_,settings)
        if settings and settings.broken then error("invalid binding settings") end
    end}
    local target={kind="action",object=action}
    backend:prepare_input_settings({{target=target,binding="bad",settings={broken=true}},
        {target=target,binding="good",settings={}}})
    backend:dispatch(target,"started",nil,"bad",false,{broken=true})
    backend:dispatch(target,"started",nil,"good",false,{})
    check(injected==1 and backend:target_failed(target,"bad") and not backend:target_failed(target,"good"),
        "binding settings failure disabled the shared action's healthy binding")
end

-- Shared recovery completes cleanup, backs off, and never replays held edge actions.
do
    local refreshes,cleanups,steps,logs=0,0,0,{}
    local broken,cleanupBroken=true,true
    local events={}
    local config={enabled=true,runtime_hotkeys=false,require_foreground=false,targets={
        move={kind="action",path="W",resume_continuous=true},edge={kind="event",tag="edge"}
    },bindings={{id="move",keys={"W"},outputs={"move"}},
        {id="edge",keys={"G"},trigger="press",outputs={"edge"}}},hooks={}}
    local backend=Backend.new(config,function() end)
    backend.state={ready=true,gameplay=true,generation=1}
    function backend:refresh() refreshes=refreshes+1; if broken then error("shared input unavailable",0) end;return true end
    function backend:shutdown() cleanups=cleanups+1;if cleanupBroken then error("cleanup unavailable",0) end end
    function backend:snapshot() return {W=1,G=1} end
    function backend:action_identity(target) return target.path end
    function backend:pipeline_identity() return "" end
    function backend:time() return now end
    function backend:dispatch(target,phase) events[#events+1]={target=target.path or target.tag,phase=phase};return true end
    package.loaded.backend={new=function() return backend end}
    package.loaded["router-config"]=config
    local status,detail
    package.loaded["live-config"]={new=function(_,_,getStatus)
        local function publish() status,detail=getStatus() end
        return {poll=publish,publish=publish}
    end}
    local frame
    EngineTickAvailable=true
    function LoopInGameThreadAfterFrames(_,callback) frame=callback;return 1 end
    function CancelDelayedAction() end
    local originalPrint=print
    print=function(message) logs[#logs+1]=message end
    now=200
    local runtime=dofile("prototype/input-router/ue4ss/Scripts/main.lua")
    frame()
    check(status=="suspended" and detail:find("cleanup unavailable",1,true),"shared cleanup failure was not retained")
    for _=1,200 do frame() end
    check(refreshes==1 and cleanups==1,"suspended runtime retried cleanup every frame")
    now=201;frame()
    check(refreshes==1 and status=="suspended","runtime resumed before cleanup succeeded")
    cleanupBroken=false
    now=203;frame()
    check(refreshes==2 and status=="suspended","persistent shared failure was not retried")
    broken=false
    now=206.9;frame();check(refreshes==2,"shared backoff ended too early")
    now=207;frame()
    check(refreshes==3 and status=="ready" and detail==nil,"shared layer did not recover without reapplying configuration: " .. tostring(refreshes) .. " " .. tostring(status) .. " " .. tostring(detail))
    local movement,edges=0,0
    for _,event in ipairs(events) do
        if event.target=="W" and event.phase=="started" then movement=movement+1 end
        if event.target=="edge" then edges=edges+1 end
    end
    check(movement==1 and edges==0,"recovery lost held movement or replayed a held edge action")
    local diagnostics=0
    for _,message in ipairs(logs) do if message:find("Routing suspended",1,true) then diagnostics=diagnostics+1 end end
    check(diagnostics<=3,"shared retry repeated diagnostics on every frame")
    runtime.stop()
    print=originalPrint
end
os.clock=originalClock
print("RuntimeRecoveryTests: " .. passed .. " assertions passed")
