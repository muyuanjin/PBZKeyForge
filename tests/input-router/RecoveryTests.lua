package.loaded["live-config"] = {new=function() return {poll=function() end} end}
local frame,keys,logs=nil,{},{}
local originalPrint=print
print=function(value) logs[#logs+1]=value end
local config={enabled=true,targets={},bindings={},hooks={}}
local backend={state={ready=true,gameplay=true,generation=1},config_generation=0,shutdowns=0}
function backend:refresh() return true end
function backend:effective_config() return self.config end
function backend:context() return self.state end
function backend:snapshot() return {} end
function backend:time() return 1 end
function backend:shutdown()
    self.shutdowns=self.shutdowns+1
    if self.failRestore then error("context restore unavailable") end
end
local route={steps=0}
function route:keys() return {} end
function route:resume() end
function route:step()
    self.steps=self.steps+1
    if self.failStep then error("dispatch unavailable") end
end
function route:reset() if self.failReset then error("action release unavailable") end end
package.preload["router-config"]=function() return config end
package.preload.backend=function() return {new=function(configuration) backend.config=configuration;return backend end} end
package.preload.router=function() return {new=function() return route end} end
EngineTickAvailable=true
Key={F7=7,F8=8}
function LoopInGameThreadAfterFrames(_,callback) frame=callback;return 1 end
function ExecuteInGameThread(callback) callback() end
function RegisterKeyBind(key,callback) keys[key]=callback end
function CancelDelayedAction() end
local handle=dofile("prototype/input-router/ue4ss/Scripts/main.lua")
frame()
route.failStep=true
route.failReset=true
backend.failRestore=true
frame()
assert(logs[#logs]:find("restoration incomplete",1,true))
assert(backend.shutdowns==1,"Action release failure must still attempt context restoration")
local steps=route.steps
frame()
assert(route.steps==steps,"Suspended runtime must stop output")
keys[Key.F8]()
assert(logs[#logs]:find("restoration incomplete",1,true))
frame()
assert(route.steps==steps,"Failed recovery must remain suspended")
backend.failRestore=false
route.failReset=false
route.failStep=false
keys[Key.F8]()
frame()
assert(route.steps==steps+1,"Successful recovery must resume")
keys[Key.F8]()
assert(config.enabled==false,"Temporary disable failed")
frame()
assert(route.steps==steps+1,"Disabled runtime must stop output")
handle.stop()
print=originalPrint
print("RecoveryTests: failed restoration, retained suspension and F8 retry passed")
