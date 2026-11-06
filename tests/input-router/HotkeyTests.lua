package.loaded["live-config"] = {new=function() return {poll=function() end} end}
local registrations=0
local config
package.preload["router-config"]=function() return config end
package.preload.backend=function() return {new=function() return {shutdown=function() end} end} end
package.preload.router=function() return {new=function() return {reset=function() end} end} end
EngineTickAvailable=true
Key={F7=7,F8=8}
function LoopInGameThreadAfterFrames() return 1 end
function CancelDelayedAction() end
function RegisterKeyBind() registrations=registrations+1 end
local function run(hotkeys,expected)
    registrations=0
    config={targets={},bindings={},runtime_hotkeys=hotkeys}
    package.loaded["router-config"]=nil
    local handle=dofile("prototype/input-router/ue4ss/Scripts/main.lua")
    assert(registrations==expected,"Unexpected reserved F7/F8 keys")
    handle.stop()
end
run(false,0)
run(true,2)
run(nil,2)
print("HotkeyTests: disabled, enabled and legacy defaults passed")
