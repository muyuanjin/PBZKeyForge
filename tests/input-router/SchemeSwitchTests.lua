local scripts = "prototype/input-router/ue4ss/Scripts/"
package.path = scripts .. "?.lua;" .. package.path
local checks, frame, now = 0, nil, 0
local function check(value, message) assert(value, message); checks = checks + 1 end
local directory = "dist/enhancement-schemes/runtime/"
os.remove(directory .. "scheme-state.txt")

local function config(id)
    return {scheme_id=id,scheme_name=id,enabled=true,runtime_hotkeys=false,hooks={},targets={
        hit={kind="event",tag=id},attack={kind="action",path="/Attack"},
        move={kind="action",path="/Move",resume_continuous=true},
        switch={kind="scheme",scheme="next"}},bindings={
        {id="hit",keys={"G"},trigger="press",outputs={"hit"}},
        {id="attack",keys={"J"},trigger="down",outputs={"attack"}},
        {id="move",keys={"W"},trigger="down",outputs={"move"}},
        {id="switch",keys={"F6"},trigger="pulse",interval=.01,outputs={"switch"}}}}
end
local first, second, bad = config("first"), config("second"), config("bad")
bad.dynamic_capture, bad.fail = true, true
local library, order = {first=first,second=second,bad=bad}, {"first","second","bad"}
first.schemes, first.scheme_order = library, order
package.preload["router-config"] = function() return first end
package.loaded["live-config"] = {new=function() return {poll=function() end,publish=function() end} end}
local physical, events, released, notices = {}, {}, {}, {}
local backend = {generation=1,session={},state={ready=true,gameplay=true,generation=1}}
function backend:refresh() return true end
function backend:effective_config() return self.config end
function backend:context() return self.state end
function backend:allowed() return true end
function backend:report() end
function backend:snapshot(keys)
    self.last_samples = keys
    local values = {}; for _, key in ipairs(keys) do values[key] = physical[key] or 0 end; return values
end
function backend:time() return now end
function backend:shutdown() end
function backend:prepare_dynamic_capture() if self.config.fail then error("candidate pipeline unavailable") end end
function backend:scheme_notice(name) notices[#notices+1] = name end
function backend:dispatch(target, phase, value, id)
    if target.kind == "scheme" then return self.request_scheme(target.scheme, id) end
    if target.kind == "event" then events[#events+1] = target.tag end
    if target.kind == "action" then
        if phase == "canceled" or phase == "completed" then released[#released+1] = target.path
        else events[#events+1] = target.path end
    end
    return true
end
package.preload.backend = function() return {new=function(configuration) backend.config=configuration;return backend end} end
EngineTickAvailable = true
function LoopInGameThreadAfterFrames(_, callback) frame=callback;return 1 end
function CancelDelayedAction() end
function RegisterHook() return 1,2 end
function UnregisterHook() end
function StaticFindObject() return {IsValid=function() return true end} end
local file=assert(io.open(scripts .. "main.lua","rb")); local source=file:read("*a");file:close()
local handle=assert(load(source,"@" .. directory .. "Scripts/main.lua"))()
local function step() now=now+.1;frame() end
step()
physical.G, physical.J, physical.W = 1, 1, 1; step()
check(events[1]=="first", "first scheme did not execute its G action")
physical.F6=1;step()
check(backend.config.scheme_id=="second", "hotkey did not switch scheme")
check(notices[#notices]=="second", "active scheme notice missing")
check(table.concat(released,","):find("/Attack",1,true) and table.concat(released,","):find("/Move",1,true), "old held outputs were not released")
local count=#events;step()
check(backend.config.scheme_id=="second", "held switch key advanced twice")
check(#events==count+1 and events[#events]=="/Move", "held discrete action replayed or continuous movement did not resume")
for _=1,120 do step() end
check(#backend.last_samples==4, "held switch key grew the router sample list")
local samples={}
for _, key in ipairs(backend.last_samples) do samples[key]=(samples[key] or 0)+1 end
check(samples.F6==1, "switch key was sampled more than once per frame")
physical.G,physical.J,physical.F6=0,0,0;step()
check(#backend.last_samples==4, "released switch key left duplicate samples in the router")
physical.G=1;step()
check(events[#events-1]=="second" or events[#events]=="second", "second scheme did not execute its own G action")
physical.F6=1;step()
check(backend.config.scheme_id=="second", "unavailable target replaced the active scheme")
step()
check(backend.config.scheme_id=="second", "rejected switch retried while held")
physical.F6,physical.G=0,0;step();physical.G=1;step()
check(events[#events-1]=="second" or events[#events]=="second", "old routing was not restored after rejection")
check(handle.switch_scheme("previous") and backend.config.scheme_id=="first", "previous scheme did not switch")
local state=assert(io.open(directory .. "scheme-state.txt","rb"));local saved=state:read("*a");state:close()
check(saved=="first", "active scheme was not persisted")
local missing, reason=handle.switch_scheme("missing")
check(not missing and reason:find("Unknown scheme",1,true) and backend.config.scheme_id=="first", "unknown target changed routing")
handle.stop()
local state_out=assert(io.open(directory .. "scheme-state.txt","wb"));state_out:write("second");state_out:close()
local restarted=assert(load(source,"@" .. directory .. "Scripts/main.lua"))()
check(backend.config.scheme_id=="second", "restart did not restore the last runtime scheme")
restarted.stop()

local compiled=dofile("dist/enhancement-schemes/checks/schemes.lua")
check(PBZSchemeScriptRuns==nil, "dormant schemes executed global setup")
compiled.configure_scheme(compiled)
check(PBZSchemeScriptRuns==1 and compiled.marker, "selected scheme setup was not isolated")
check(#compiled.scheme_order==4 and compiled.schemes[compiled.scheme_order[4]], "compiled library omitted schemes")
check(compiled.scheme_id==compiled.scheme_order[1], "compiler activated the editor selection without applying")
check(#compiled.schemes[compiled.scheme_order[1]].native_bindings>=40, "compiler lost native baselines")
print("SchemeSwitchTests: " .. checks .. " assertions passed")
