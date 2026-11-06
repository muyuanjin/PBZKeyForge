local Config = require("router-config")
local Backend = require("backend")
local Router = require("router")

local function log(message)
    print("[PBZInputRouter] " .. tostring(message) .. "\n")
end

local runtime = {running = true, pending = false, generation = nil, last_error = nil,
    suspended = false, hooks = {}, active = false}
local backend = Backend.new(Config, log)
local router = Router.new(Config, backend)
backend.on_retire = function() router:reset(true) end
backend.on_release = function() router:reset(true) end

local function restore()
    local outputs, outputError = pcall(function() router:reset(true) end)
    local contexts, contextError = pcall(function() backend:shutdown() end)
    if not outputs then error(outputError, 0) end
    if not contexts then error(contextError, 0) end
end

local function uninstall_hooks()
    for path, ids in pairs(runtime.hooks) do UnregisterHook(path, ids[1], ids[2]) end
    runtime.hooks = {}
    for path, ids in pairs(runtime.ui_hooks or {}) do UnregisterHook(path, ids[1], ids[2]) end
    runtime.ui_hooks = nil
end

local function stop()
    runtime.running = false
    local restored, restoreError = pcall(restore)
    uninstall_hooks()
    if runtime.handle and type(CancelDelayedAction) == "function" then CancelDelayedAction(runtime.handle) end
    if not restored then error(restoreError, 0) end
    log("Stopped; original input contexts restored")
end

local function apply_configuration(candidate)
    if not runtime.running then return false, "Router is stopped" end
    local ok, candidateRouter = pcall(Router.new, candidate, backend)
    if not ok then
        log("Configuration rejected: " .. tostring(candidateRouter))
        return false, candidateRouter
    end
    local restored, restoreError = pcall(restore)
    if not restored then
        runtime.suspended = true
        runtime.last_error = tostring(restoreError)
        log("Reload suspended until original input restoration completes: " .. tostring(restoreError))
        return false, restoreError
    end
    local previous = Config
    backend.config = candidate
    backend.live_validation = true
    local prepared, prepareError = pcall(function()
        if candidate.dynamic_capture and type(backend.prepare_dynamic_capture) == "function" then
            backend:prepare_dynamic_capture()
            Router.new(backend:effective_config(), backend)
        end
    end)
    backend.live_validation = nil
    local cleaned, cleanupError = true, nil
    if candidate.dynamic_capture and type(backend.prepare_dynamic_capture) == "function" then cleaned, cleanupError = pcall(restore) end
    if not prepared or not cleaned then
        backend.config = previous
        runtime.active = false
        if not cleaned then runtime.suspended = true; runtime.last_error = tostring(cleanupError) end
        return false, not cleaned and cleanupError or prepareError
    end
    uninstall_hooks()
    Config, router = candidate, candidateRouter
    backend.config = Config
    runtime.active, runtime.suspended = false, false
    runtime.generation, runtime.config_generation, runtime.last_error = nil, nil, nil
    log("Configuration reloaded; bindings=" .. #Config.bindings)
    return true
end

local function reload()
    local previous = package.loaded["router-config"]
    package.loaded["router-config"] = nil
    local ok, candidate = pcall(require, "router-config")
    if not ok then package.loaded["router-config"] = previous; return false, candidate end
    local applied, err = apply_configuration(candidate)
    if not applied then package.loaded["router-config"] = previous end
    return applied, err
end

local live
if type(io) == "table" and type(io.open) == "function" then
    local ok, module = pcall(require, "live-config")
    if ok then live = module.new(apply_configuration, nil, function()
        return runtime.suspended and "suspended" or Config.enabled == false and "disabled" or "ready", runtime.last_error
    end) end
end

local function install_hooks()
    if Config.unified_native and not runtime.ui_hooks then
        runtime.ui_hooks = {}
        for name, blocked in pairs({SetInputMode_UIOnlyEx=true, SetInputMode_GameAndUIEx=true, SetInputMode_GameOnly=false}) do
            local path = "/Script/UMG.WidgetBlueprintLibrary:" .. name
            local object = StaticFindObject(path)
            if object and object:IsValid() then
                local value = blocked
                local first, second = RegisterHook(path, function(_, controller)
                    local player = controller:get()
                    if player and player:IsValid() and backend.controller and player:GetAddress() == backend.controller:GetAddress() then backend.ui_only = value end
                end)
                runtime.ui_hooks[path] = {first, second}
            end
        end
    end
    for _, rule in ipairs(Config.hooks or {}) do
        if not runtime.hooks[rule.path] then
            local object = StaticFindObject(rule.path)
            if object and object:IsValid() then
                local function callback(...)
                    if not runtime.running or Config.enabled == false or runtime.suspended or not backend:allowed(rule.when, backend:context()) then return end
                    if rule.evaluate then
                        local ok, result = pcall(rule.evaluate, ...)
                        if not ok then backend:report("hook:" .. rule.path, "Hook failed: " .. tostring(result)); return end
                        return result
                    end
                    return backend:argument(rule.value, backend:context())
                end
                local first, second
                if rule.path:sub(1, 8) == "/Script/" then
                    first, second = RegisterHook(rule.path, function() end, callback)
                else first, second = RegisterHook(rule.path, callback) end
                runtime.hooks[rule.path] = {first, second}
                log("Return hook installed: " .. rule.path)
            end
        end
    end
end

local function tick()
    if live and runtime.running then
        local ok, err = pcall(function() live:poll(os.clock()) end)
        if not ok then log("Live configuration: " .. tostring(err)) end
    end
    if not runtime.running or runtime.suspended then return end
    local ok, err = xpcall(function()
        if not backend:refresh() then runtime.active = false; return end
        if backend.config_generation ~= runtime.config_generation then
            local nextConfig = backend:effective_config()
            if runtime.config_generation == nil then router = Router.new(nextConfig, backend)
            else
                local nextRouter = Router.new(nextConfig, backend)
                router = router:reconfigure(nextConfig, backend:snapshot(nextRouter:keys()), backend:context())
            end
            runtime.config_generation = backend.config_generation
        end
        local context = backend:context()
        install_hooks()
        if context.generation ~= runtime.generation then
            router:reset(true)
            runtime.generation = context.generation
            runtime.active = false
        end
        if Config.enabled == false or not context.gameplay then
            router:reset(true)
            runtime.active = false
            return
        end
        local snapshot = backend:snapshot(router:keys())
        context.raw_input = {}
        for key, value in pairs(snapshot) do context.raw_input[key] = value end
        if not runtime.active then
            router:resume(snapshot, context)
            runtime.active = true
        end
        backend.dispatch_error = nil
        router:step(backend:time(), snapshot, context)
        if backend.dispatch_error then error(backend.dispatch_error, 0) end
    end, debug.traceback)
    if not ok then
        local restored, restoreError = pcall(restore)
        runtime.suspended = true
        runtime.active = false
        runtime.last_error = tostring(err)
        if live then pcall(function() live:publish() end) end
        log("Routing suspended: " .. runtime.last_error .. (restored and "; original input restored" or "; restoration incomplete: " .. tostring(restoreError)) ..
            (Config.runtime_hotkeys ~= false and "; F8 retries" or "; restart the game to retry"))
    elseif ok then runtime.last_error = nil end
end

if type(LoopInGameThreadAfterFrames) == "function" and EngineTickAvailable ~= false then
    runtime.handle = LoopInGameThreadAfterFrames(1, tick)
    log("Game-thread frame scheduler ready")
elseif type(ExecuteInGameThreadAfterFrames) == "function" and EngineTickAvailable ~= false then
    local frame
    frame = function()
        tick()
        if runtime.running then runtime.handle = ExecuteInGameThreadAfterFrames(1, frame) end
    end
    runtime.handle = ExecuteInGameThreadAfterFrames(1, frame)
    log("Game-thread frame scheduler ready")
else
    if Config.unified_native then
        runtime.suspended = true
        log("Frame scheduler unavailable; gameplay input remains native")
    else
    LoopAsync(8, function()
        if not runtime.running then return true end
        if runtime.pending then return false end
        runtime.pending = true
        ExecuteInGameThread(function()
            runtime.pending = false
            tick()
        end)
        return false
    end)
    log("Game-thread queued scheduler ready (8 ms polling)")
    end
end

if Config.runtime_hotkeys ~= false then
RegisterKeyBind(Key.F8, function()
    if not runtime.running or Config.runtime_hotkeys == false then return end
    ExecuteInGameThread(function()
        if not runtime.running then return end
        local restored, restoreError = pcall(restore)
        if not restored then
            runtime.suspended = true
            runtime.last_error = tostring(restoreError)
            log("Input restoration incomplete: " .. runtime.last_error .. "; F8 retries")
            return
        end
        if runtime.suspended then Config.enabled = true; runtime.suspended = false
        else Config.enabled = not (Config.enabled ~= false) end
        runtime.active = false
        log(Config.enabled and "Routing enabled" or "Routing disabled; original contexts restored")
    end)
end)

RegisterKeyBind(Key.F7, function()
    if runtime.running and Config.runtime_hotkeys ~= false then ExecuteInGameThread(reload) end
end)
end

log("Loaded; bindings=" .. #(Config.bindings or {}) .. (Config.runtime_hotkeys ~= false and "; F8 toggles routing, F7 reloads configuration" or "; runtime hotkeys disabled"))

return {backend = backend, stop = stop, reload = reload}
