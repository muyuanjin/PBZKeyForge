package.loaded["live-config"] = {new=function() return {poll=function() end} end}
local assertions, scheduled, keybinds, unhooks, cancellations, invalidations, session_checks = 0, 0, 0, 0, 0, 0, 0
local frame, configuration, registered_hooks = nil, nil, {}
local first_refresh_hooks
local function check(value, message)
    assert(value, message)
    assertions = assertions + 1
end
local function config(name)
    return {name = name, targets = {}, bindings = {}, enabled = true,
        hooks = {{path = "/Script/Test.Function:Result", value = true}}}
end
configuration = config("first")
package.preload["router-config"] = function() return configuration end
local backend = {shutdowns = 0, config_generation = 0,
    state = {ready = true, gameplay = true, generation = 1}}
function backend:refresh()
    if not first_refresh_hooks then
        first_refresh_hooks = {}
        for path in pairs(registered_hooks) do first_refresh_hooks[path] = true end
    end
    return true
end
function backend:effective_config() return self.config end
function backend:context() return self.state end
function backend:allowed() return true end
local resolvedObject = {IsValid = function() return true end}
function backend:argument(value, context)
    check(context == self.state, "hook return resolution lost the runtime context")
    if type(value) ~= "table" then return value end
    if value.object then return resolvedObject end
    local result = {}
    for key, child in pairs(value) do result[key] = self:argument(child, context) end
    return result
end
local physical = {W = 1, V = 1}
function backend:snapshot() return {W = physical.W, V = physical.V} end
function backend:time() return 1 end
function backend:shutdown(preserveTuning)
    self.shutdowns = self.shutdowns + 1
    self.preserve_tuning = preserveTuning == true
end
function backend:invalidate_session()
    if not self.session then return end
    invalidations = invalidations + 1
    self.session = nil
    if self.on_retire then self.on_retire() end
end
function backend:validate_session() session_checks = session_checks + 1; return true end
function backend:report() end
backend.session = {}
package.preload.backend = function()
    return {new = function(configuration) backend.config = configuration; return backend end}
end
local stepped, reset, resumed, detached = 0, 0, 0, 0
local resumedInput, steppedInput
package.preload.router = function()
    return {new = function(configuration)
        assert(configuration.targets and configuration.bindings, "invalid configuration")
        return {keys = function() return {} end,
            resume = function(_, snapshot, context)
                resumed = resumed + 1
                resumedInput = snapshot
                check(context.raw_input.W == snapshot.W and context.raw_input.V == snapshot.V,
                    "resume did not receive the physical input snapshot")
            end,
            step = function(self, time, snapshot)
                stepped = stepped + 1
                steppedInput = snapshot
            end,
            reset = function() reset = reset + 1 end,
            detach = function() detached = detached + 1 end}
    end}
end
EngineTickAvailable = true
Key = {F7 = 118, F8 = 119}
function LoopInGameThreadAfterFrames(frames, callback)
    scheduled = scheduled + 1
    frame = callback
    return 23
end
function CancelDelayedAction(handle)
    assert(handle == 23)
    cancellations = cancellations + 1
end
function ExecuteInGameThread(callback) callback() end
function RegisterKeyBind(key, callback) keybinds = keybinds + 1 end
function StaticFindObject() return {IsValid = function() return true end} end
function RegisterHook(path, before, after)
    registered_hooks[path] = after or before
    return 10, 11
end
function UnregisterHook(path, first, second)
    assert(first == 10 and second == 11)
    unhooks = unhooks + 1
    registered_hooks[path] = nil
end

local handle = dofile("prototype/input-router/ue4ss/Scripts/main.lua")
check(handle.backend == backend and type(handle.stop) == "function" and type(handle.reload) == "function",
    "main did not return its lifecycle controls")
check(scheduled == 1 and keybinds == 2, "main installed duplicate schedulers or hotkeys")
frame()
check(stepped == 1, "scheduled callback did not route input")
check(first_refresh_hooks["/Script/Engine.Actor:EndPlay"]
    and first_refresh_hooks["/Script/Engine.Controller:Possess"]
    and first_refresh_hooks["/Script/Engine.Controller:UnPossess"],
    "player lifecycle hooks were installed after the first player refresh")
local hookCallback = registered_hooks["/Script/Test.Function:Result"]
check(hookCallback() == true, "static primitive return override changed")
configuration.hooks[1].value = {object = "/Game/Test.Object"}
check(hookCallback() == resolvedObject, "object descriptor was returned without resolving the UObject")
configuration.hooks[1].value = {nested = {{object = "/Game/Test.Object"}}, enabled = false}
local composite = hookCallback()
check(composite.nested[1] == resolvedObject and composite.enabled == false, "composite hook return did not resolve nested objects")
check(resumed == 1 and resumedInput.W == 1 and steppedInput.W == 1 and steppedInput.V == 1,
    "initial activation suppressed physical input before per-rule resume")
local previous = backend.config
configuration = {}
local accepted = handle.reload()
check(not accepted, "invalid configuration was accepted")
check(backend.config == previous and backend.shutdowns == 0,
    "invalid configuration interrupted the existing route")
frame()
check(stepped == 2, "existing route stopped after invalid reload")
check(resumed == 1, "uninterrupted routing incorrectly resumed rules again")

configuration = config("second")
check(handle.reload(), "valid configuration was rejected")
check(backend.config == configuration and backend.shutdowns == 1,
    "valid reload did not replace configuration after releasing capture")
check(backend.preserve_tuning,"configuration reload discarded launched-projectile settings")
check(unhooks == 4, "reload left function or player lifecycle hooks installed")
check(scheduled == 1 and keybinds == 2, "reload installed another scheduler or hotkey")
frame()
check(stepped == 3, "reloaded route did not run")
check(resumed == 2 and steppedInput.W == 1, "reload discarded a physically held movement key")
backend.state.gameplay = false
frame()
check(stepped == 3, "gameplay suspension routed input")
backend.state.gameplay = true
frame()
check(resumed == 3 and stepped == 4 and steppedInput.W == 1 and steppedInput.V == 1,
    "gameplay resume required held keys to be released instead of applying per-rule recovery")
local pawn = {IsValid=function() return true end,GetAddress=function() return 31 end}
local controller = {IsValid=function() return true end,GetAddress=function() return 32 end}
backend.pawn, backend.controller = pawn, controller
registered_hooks["/Script/Engine.Actor:EndPlay"]({get=function() return pawn end})
registered_hooks["/Script/Engine.Controller:UnPossess"]({get=function() return controller end})
check(invalidations == 1, "player lifecycle hooks did not invalidate the player session")
check(session_checks == 0, "complete lifecycle hooks still performed the per-frame Pawn fallback")
check(detached == 1, "player session invalidation retained the old router configuration")
check(hookCallback() == nil, "configured return hook ran without a player session")
handle.stop()
check(not backend.preserve_tuning,"stopping the mod must fully release tuning")
check(backend.shutdowns == 2 and unhooks == 8 and cancellations == 1,
    "stop did not restore inputs, remove hooks, and cancel the loop")
frame()
check(stepped == 4, "a queued callback routed input after stop")
check(not handle.reload(), "stopped instance accepted reload")

do
    local register = RegisterHook
    local failed = false
    function RegisterHook(path, before, after)
        if not failed and path == "/Script/Engine.Controller:Possess" then
            failed = true
            error("fixture lifecycle hook unavailable")
        end
        return register(path, before, after)
    end
    backend.session = {}
    backend.state = {ready=true,gameplay=true,generation=1}
    local checks = session_checks
    local fallback = dofile("prototype/input-router/ue4ss/Scripts/main.lua")
    frame()
    check(session_checks == checks + 1,
        "incomplete lifecycle hooks did not perform the Pawn fallback")
    fallback.stop()
    RegisterHook = register
end

configuration=config("disabled");configuration.enabled=false;configuration.dynamic_capture=true
local originalRefresh,originalPrepare,originalFind,originalRegister=backend.refresh,backend.prepare_dynamic_capture,StaticFindObject,RegisterHook
backend.refresh=function() error("Disabled enhancement must not inspect the player") end
backend.prepare_dynamic_capture=function() error("Disabled enhancement must not prepare native mappings") end
StaticFindObject=function() error("Disabled enhancement must not resolve game functions") end
RegisterHook=function() error("Disabled enhancement must not install game hooks") end
package.loaded["router-config"]=nil
local disabled=dofile("prototype/input-router/ue4ss/Scripts/main.lua")
frame();check(disabled.reload(),"disabled configuration can reload without game access")
frame();disabled.stop()
backend.refresh,backend.prepare_dynamic_capture,StaticFindObject,RegisterHook=originalRefresh,originalPrepare,originalFind,originalRegister
print("MainLifecycleTests: " .. assertions .. " assertions passed")
