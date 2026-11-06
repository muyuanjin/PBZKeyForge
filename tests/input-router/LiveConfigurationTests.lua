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
live:poll(1.06)
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
print("LiveConfigurationTests: " .. count .. " assertions passed")
