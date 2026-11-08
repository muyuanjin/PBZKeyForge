local scripts = "prototype/input-router/ue4ss/Scripts/"
package.path = scripts .. "?.lua;" .. package.path
local Live = dofile(scripts .. "live-config.lua")
local count = 0
local function check(value, message) assert(value, message); count=count+1 end
local prefix = "dist/live-edit/protocol-"
local function write(name, value) local f=assert(io.open(prefix .. name,"wb"));f:write(value);f:close() end
local function read(name) local f=assert(io.open(prefix .. name,"rb"));local v=f:read("*a");f:close();return v end
write("live-request.id","stale")
write("live-request.lua", 'return {id="stale",configuration={value=0}}')
local current, applies, live_state, live_error = 1, 0, "ready", nil
local live = Live.new(function(config)
    if config.value < 0 then return false,"invalid candidate" end
    current=config.value; applies=applies+1;live_state="ready";live_error=nil;return true
end,prefix,function() return live_state,live_error end)
live:poll(1)
check(current==1 and applies==0,"startup replayed an old request")
check(read("live-status.txt"):match("^PBZ%-LIVE%-1\n"),"heartbeat protocol missing")
write("live-request.lua", 'return {id="ok",configuration={value=2}}');write("live-request.id","ok")
live:poll(1.3)
check(current==2 and applies==1 and read("live-response.txt"):match("^ok\nok\n"),"success acknowledged before acceptance")
live:poll(3);check(applies==1,"request applied twice")
write("live-request.lua", 'return {id="reject",configuration={value=-1}}');write("live-request.id","reject")
live:poll(4);check(current==2 and read("live-response.txt"):match("^reject\nerror\n"),"rejection changed active config")
write("live-request.lua", 'return {id="late",configuration={value=3}}');write("live-request.id","cancel-late")
live:poll(5);check(current==2 and applies==1,"cancelled request applied when the game resumed")
write("live-request.lua", 'this is invalid Lua');write("live-request.id","syntax")
live:poll(6);check(current==2 and read("live-response.txt"):match("^syntax\nerror\n"),"invalid Lua changed current config")
live_state,live_error="suspended","input pipeline unavailable"
live:poll(7)
check(read("live-status.txt"):find("\nsuspended\ninput pipeline unavailable",1,true),"suspension reported as ready")
write("live-request.lua", 'return {id="recover",configuration={value=4}}');write("live-request.id","recover")
live:poll(8)
check(current==4 and read("live-status.txt"):match("\nready\n$"),"suspended connection could not accept a recovery configuration")

do
    local original_open = io.open
    local opens = 0
    io.open = function(...)
        opens = opens + 1
        return original_open(...)
    end
    for i=1,24 do live:poll(8+i*.01) end
    check(opens == 0, "stable frames performed live file work before the polling interval")
    io.open = original_open

    write("live-request.lua", 'return {id="write-retry",configuration={value=5}}')
    write("live-request.id", "write-retry")
    io.open = function(path, mode)
        if path == prefix .. "live-response.txt" and mode == "wb" then return nil, "fixture sharing violation" end
        return original_open(path, mode)
    end
    local ok = pcall(function() live:poll(9) end)
    check(not ok and current == 5 and applies == 3 and live.pending,
        "a response sharing violation lost the applied request's result")
    io.open = original_open
    live:poll(9.3)
    check(applies == 3 and not live.pending and read("live-response.txt"):match("^write%-retry\nok\n"),
        "retrying a failed acknowledgment repeated the apply or lost confirmation")

    write("live-request.lua", 'return {id="flush-retry",configuration={value=6}}')
    write("live-request.id", "flush-retry")
    local closed = false
    io.open = function(path, mode)
        if path == prefix .. "live-response.txt" and mode == "wb" then
            return {write=function() return nil, "fixture disk full" end,
                close=function() closed=true;return true end}
        end
        return original_open(path, mode)
    end
    ok = pcall(function() live:poll(10) end)
    check(not ok and closed and current == 6 and applies == 4 and live.pending,
        "an unflushed acknowledgment was treated as delivered or left the file open")
    io.open = original_open
    live:poll(10.3)
    check(applies == 4 and read("live-response.txt"):match("^flush%-retry\nok\n"),
        "write failure replayed an accepted edit")

    write("live-request.lua", 'return {id="loading-cancel",configuration={value=7}}')
    write("live-request.id", "loading-cancel")
    local original_loadfile = loadfile
    loadfile = function(path)
        local chunk, err = original_loadfile(path)
        write("live-request.id", "cancel-loading-cancel")
        return chunk, err
    end
    live:poll(11)
    loadfile = original_loadfile
    check(current == 6 and applies == 4 and read("live-response.txt"):find("cancelled",1,true),
        "a cancellation during configuration loading still changed the game")
    live:poll(11.3)
    write("live-request.lua", 'return {id="after-cancel",configuration={value=8}}')
    write("live-request.id", "after-cancel")
    live:poll(11.6)
    check(current == 8 and applies == 5 and read("live-response.txt"):match("^after%-cancel\nok\n"),
        "a cancelled request blocked the next valid edit")
end

local configuration = {enabled=true,targets={old={kind="event",tag="old"}},bindings={{id="old",keys={"J"},trigger="press",outputs={"old"}}},hooks={}}
package.preload["router-config"]=function() return configuration end
local physical, events, frame = {}, {}, nil
local backend={config=configuration,config_generation=0,state={ready=true,gameplay=true,generation=1}}
function backend:refresh() return true end
function backend:effective_config() return self.config end
function backend:context() return self.state end
function backend:snapshot() return physical end
function backend:time() self.now=(self.now or 0)+.1;return self.now end
function backend:shutdown() end
function backend:prepare_dynamic_capture() if self.config.fail then error("pipeline unavailable") end end
function backend:allowed() return true end
function backend:dispatch(target) events[#events+1]=target.tag;return true end
package.loaded.backend=nil;package.preload.backend=function() return {new=function() return backend end} end
local apply, reported_state, reported_error
package.loaded["live-config"]=nil;package.preload["live-config"]=function() return {new=function(callback,_,status)
    apply=callback
    local function publish() reported_state,reported_error=status() end
    return {poll=publish,publish=publish}
end} end
EngineTickAvailable=true
function LoopInGameThreadAfterFrames(_,callback) frame=callback;return 1 end
function CancelDelayedAction() end
Key={F7=1,F8=2};function RegisterKeyBind() end
local lifecycle=dofile(scripts .. "main.lua")
frame();physical.J=1;frame();check(events[1]=="old","baseline did not route")
local bad={enabled=true,dynamic_capture=true,targets=configuration.targets,bindings=configuration.bindings,hooks={},fail=true}
local ok,err=apply(bad)
check(not ok and tostring(err):find("pipeline unavailable",1,true) and backend.config==configuration,"pipeline failure replaced live config")
events={};physical.J=0;frame();physical.J=1;frame();check(events[1]=="old","rejected candidate stopped previous route")
local candidate={enabled=true,dynamic_capture=true,targets={next={kind="event",tag="next"}},bindings={{id="next",keys={"J"},trigger="press",outputs={"next"}}},hooks={}}
check(apply(candidate),"valid live candidate rejected")
events={};frame();check(#events==0,"live apply replayed an already-held press")
physical.J=0;frame();physical.J=1;frame();check(events[1]=="next" and backend.config==candidate,"accepted candidate did not route after release")
local refresh_count=0
local normal_shutdown = backend.shutdown
function backend:shutdown() error("complete-move input release failed") end
function backend:refresh() refresh_count=refresh_count+1;error("input refresh failed") end
frame()
check(reported_state=="suspended" and reported_error:find("input refresh failed",1,true),"runtime failure did not immediately publish its reason")
check(reported_error:find("complete-move input release failed",1,true), "Runtime details dropped a failure restoring the original input")
backend.shutdown = normal_shutdown
frame();check(refresh_count==1 and reported_state=="suspended","suspended runtime kept routing or advertised ready")
check(apply(candidate),"recovery configuration rejected while suspended")
frame();check(reported_state=="suspended" and refresh_count==2,"same error after retry did not suspend routing again")
function backend:refresh() return true end
check(apply(candidate),"recovery after fixing backend rejected")
frame();check(reported_state=="ready" and reported_error==nil,"successful recovery retained suspended state")
lifecycle.stop()
EngineTickAvailable = false
configuration.unified_native = true
local unscheduled = dofile(scripts .. "main.lua")
check(reported_state == "suspended" and reported_error:find("frame scheduler unavailable",1,true),
    "Missing frame scheduling must publish an actionable error without waiting for a tick")
unscheduled.stop()
do
    local workers, polls, publications, in_game = 0, 0, 0, false
    local status_callback
    package.loaded["live-config"] = {new=function(_,_,status,dispatch)
        check(dispatch == nil, "live editing queued work into a second Lua coroutine")
        status_callback = status
        return {poll=function()
            check(in_game, "live protocol ran outside the serialized game frame")
            polls=polls+1
        end,publish=function() publications=publications+1 end}
    end}
    function LoopAsync() workers=workers+1 end
    function ExecuteInGameThread() error("live editing must use the existing game frame") end
    function StaticFindObject() return nil end
    EngineTickAvailable = true
    function backend:refresh() return true end
    local serialized = dofile(scripts .. "main.lua")
    check(workers == 0, "live editing started an unsafe background Lua worker")
    in_game = true
    frame();frame()
    check(polls == 2, "game frame did not service live editing")
    function backend:refresh() error("serialized runtime failure") end
    frame()
    local state, detail = status_callback()
    check(state=="suspended" and detail:find("serialized runtime failure",1,true) and publications==1,
        "suspension did not publish its state on the game thread")
    frame()
    check(polls == 4, "suspension backoff blocked live recovery requests")
    serialized.stop()
    frame()
    check(polls == 4, "stopped runtime kept polling live files")
    in_game = false
    LoopAsync, ExecuteInGameThread = nil, nil
end

print("LiveConfigurationTests: " .. count .. " assertions passed")
