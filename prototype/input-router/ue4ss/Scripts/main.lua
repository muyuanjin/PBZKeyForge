local Config = require("router-config")
local Backend = require("backend")
local Router = require("router")

local source = debug.getinfo(1, "S").source:gsub("^@", "")
local scheme_path = (source:match("^(.*[/\\])") or "") .. "../scheme-state.txt"
if Config.schemes and io then
    local file = io.open(scheme_path, "rb")
    if file then
        local id = file:read("*a"):match("^%s*(.-)%s*$"); file:close()
        if Config.schemes[id] then
            local schemes, order = Config.schemes, Config.scheme_order
            Config = schemes[id]; Config.schemes, Config.scheme_order = schemes, order
        end
    end
end

if Config.configure_scheme and not Config.scheme_initialized then
    Config.configure_scheme(Config); Config.scheme_initialized = true
end

local function log(message)
    print("[PBZInputRouter] " .. tostring(message) .. "\n")
end

local runtime = {running = true, pending = false, generation = nil, last_error = nil,
    suspended = false, hooks = {}, failed_hooks = {}, hook_retry = {}, session_hooks = {}, active = false}
local uninstall_session_hooks
local backend = Backend.new(Config, log)
local router = Router.new(Config, backend)

local function suspend(failure)
    local now = os.clock()
    runtime.retry_attempts = (runtime.retry_attempts or 0) + 1
    runtime.failed_at = now
    runtime.recovered_at = nil
    runtime.retry_at = now + math.min(30, 2 ^ math.min(runtime.retry_attempts - 1, 5))
    runtime.suspended, runtime.active, runtime.last_error = true, false, tostring(failure)
end

local function publish_scheme()
    if not Config.scheme_id or not io then return end
    local file, err = io.open(scheme_path, "wb")
    if not file then log("Could not save active scheme: " .. tostring(err)); return end
    file:write(Config.scheme_id); file:close()
end
backend.on_retire = function()
    router:detach()
    runtime.active, runtime.generation = false, nil
    runtime.switch_latch, runtime.pending_scheme = nil, nil
end
backend.on_release = function() router:reset(true) end

local function restore(preserveTuning)
    local outputs, outputError = pcall(function() router:reset(true) end)
    local contexts, contextError = pcall(function() backend:shutdown(preserveTuning) end)
    if not outputs and not contexts then error(tostring(outputError) .. "\n" .. tostring(contextError), 0) end
    if not outputs then error(outputError, 0) end
    if not contexts then error(contextError, 0) end
end

local function uninstall_hooks()
    local failures = {}
    local function remove(hooks)
        for path,ids in pairs(hooks) do
            local ok,failure = pcall(UnregisterHook,path,ids[1],ids[2])
            if ok then hooks[path] = nil
            else failures[#failures+1] = tostring(failure) end
        end
    end
    remove(runtime.hooks)
    uninstall_session_hooks()
    if #failures > 0 then error(table.concat(failures,"\n"),0) end
    runtime.failed_hooks, runtime.hook_retry = {}, {}
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
    if candidate.configure_scheme and not candidate.scheme_initialized then
        local initialized, err = pcall(candidate.configure_scheme, candidate)
        if not initialized then return false, err end
        candidate.scheme_initialized = true
    end
    local ok, candidateRouter = pcall(Router.new, candidate, backend)
    if not ok then
        log("Configuration rejected: " .. tostring(candidateRouter))
        return false, candidateRouter
    end
    local restored, restoreError = pcall(restore, candidate.enabled ~= false)
    if not restored then
        suspend(restoreError)
        log("Reload suspended until original input restoration completes: " .. tostring(restoreError))
        return false, restoreError
    end
    local previous = Config
    backend.config = candidate
    backend.live_validation = true
    local prepared, prepareError = pcall(function()
        if candidate.enabled ~= false and candidate.dynamic_capture and type(backend.prepare_dynamic_capture) == "function" then
            backend:prepare_dynamic_capture()
            Router.new(backend:effective_config(), backend)
        end
    end)
    backend.live_validation = nil
    local cleaned, cleanupError = true, nil
    if candidate.enabled ~= false and candidate.dynamic_capture and type(backend.prepare_dynamic_capture) == "function" then cleaned, cleanupError = pcall(restore, true) end
    if not prepared or not cleaned then
        backend.config = previous
        runtime.active = false
        if not cleaned then suspend(cleanupError) end
        return false, not cleaned and cleanupError or prepareError
    end
    uninstall_hooks()
    Config, router = candidate, candidateRouter
    backend.config = Config
    runtime.active, runtime.suspended = false, false
    runtime.generation, runtime.config_generation, runtime.last_error = nil, nil, nil
    runtime.retry_at, runtime.failed_at, runtime.retry_attempts, runtime.reported_error, runtime.recovered_at = nil, nil, nil, nil, nil
    log("Configuration reloaded; bindings=" .. #Config.bindings)
    publish_scheme()
    return true
end

local function switch_scheme(id)
    if not Config.schemes then return false, "No scheme library loaded" end
    local order = Config.scheme_order or {}
    if id == "next" or id == "previous" then
        local index = 1
        for i, value in ipairs(order) do if value == Config.scheme_id then index = i; break end end
        id = order[(index - 1 + (id == "next" and 1 or -1)) % #order + 1]
    end
    local candidate = Config.schemes[id]
    if not candidate then return false, "Unknown scheme: " .. tostring(id) end
    if candidate == Config then return true end
    local previous = Config
    candidate.schemes, candidate.scheme_order = Config.schemes, order
    local applied, err = apply_configuration(candidate)
    if not applied then
        local restored, restore_error = apply_configuration(previous)
        if not restored then suspend(restore_error) end
        log("Scheme rejected: " .. tostring(err))
        return false, err
    end
    log("Active scheme: " .. tostring(Config.scheme_name))
    if backend.scheme_notice then backend:scheme_notice(Config.scheme_name) end
    return true
end

backend.request_scheme = function(id, binding_id)
    if runtime.pending_scheme or runtime.switch_latch then return true end
    local keys = {}
    for _, binding in ipairs(Config.bindings) do
        if binding.id == binding_id then
            for _, key in ipairs(binding.keys or {}) do keys[key] = true end
            for _, key in ipairs(binding.watch or {}) do keys[key] = true end
            if binding.axis then keys[binding.axis] = true end
            for _, step in ipairs(binding.steps or {}) do
                if type(step) == "string" then keys[step] = true else for _, key in ipairs(step) do keys[key] = true end end
            end
        end
    end
    runtime.switch_latch, runtime.pending_scheme = keys, id
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

local function publish_live()
    if live then pcall(function() live:publish() end) end
end

local session_hook_paths = {
    {path = "/Script/Engine.Actor:EndPlay", owner = "pawn"},
    {path = "/Script/Engine.Controller:UnPossess", owner = "controller"},
    {path = "/Script/Engine.Controller:Possess", owner = "controller"}
}

local function hook_object(value)
    if value == nil then return nil end
    local ok, object = pcall(function() return value:get() end)
    return ok and object or value
end

local function same_object(first, second)
    if not first or not second then return false end
    local ok, first_address = pcall(function() return tostring(first:GetAddress()) end)
    local other_ok, second_address = pcall(function() return tostring(second:GetAddress()) end)
    if ok and other_ok and first_address and second_address then return first_address == second_address end
    local first_valid_ok, first_valid = pcall(function() return first:IsValid() end)
    local second_valid_ok, second_valid = pcall(function() return second:IsValid() end)
    return first_valid_ok and first_valid and second_valid_ok and second_valid and first == second
end

local function install_session_hooks()
    if Config.enabled == false or runtime.session_hooks.installed then return end
    runtime.session_hooks = {installed = true, complete = true}
    for _, rule in ipairs(session_hook_paths) do
        local function callback(value)
            local current = rule.owner == "pawn" and backend.pawn or backend.controller
            if not same_object(hook_object(value), current) then return end
            backend:invalidate_session()
            runtime.active, runtime.generation = false, nil
        end
        local ok, first, second = pcall(RegisterHook, rule.path, callback, nil)
        if ok then
            runtime.session_hooks[rule.path] = {first, second}
        else
            runtime.session_hooks.complete = false
            backend:report("session-hook:" .. rule.path, "Player lifecycle hook unavailable: " .. tostring(first))
        end
    end
end

uninstall_session_hooks = function()
    for path, ids in pairs(runtime.session_hooks) do
        if type(path) == "string" and type(ids) == "table" then
            pcall(UnregisterHook, path, ids[1], ids[2])
        end
    end
    runtime.session_hooks = {}
end

local function install_hooks()
    if Config.enabled == false then return end
    for _, rule in ipairs(Config.hooks or {}) do
        if not runtime.hooks[rule.path] and not runtime.failed_hooks[rule.path]
            and os.clock() >= (runtime.hook_retry[rule.path] or 0) then
            runtime.hook_retry[rule.path] = os.clock() + .5
            local object = StaticFindObject(rule.path)
            if object and object:IsValid() then
                local ruleFailed = false
                local function evaluate(...)
                    if not backend:allowed(rule.when, backend:context()) then return end
                    if rule.evaluate then return rule.evaluate(...) end
                    return backend:argument(rule.value, backend:context())
                end
                local function callback(...)
                    if not runtime.running or Config.enabled == false or runtime.suspended or ruleFailed
                        or not backend.session then return end
                    local ok, result = xpcall(evaluate, debug.traceback, ...)
                    if not ok then
                        ruleFailed = true
                        backend:report("hook:" .. rule.path, "Hook disabled: " .. tostring(result))
                        return
                    end
                    return result
                end
                local ok, first, second
                if rule.path:sub(1, 8) == "/Script/" then
                    ok, first, second = pcall(RegisterHook, rule.path, function() end, callback)
                else ok, first, second = pcall(RegisterHook, rule.path, callback) end
                if ok then
                    runtime.hooks[rule.path] = {first, second}
                    log("Return hook installed: " .. rule.path)
                else
                    runtime.failed_hooks[rule.path] = true
                    backend:report("hook:" .. rule.path, "Hook disabled: " .. tostring(first))
                end
            end
        end
    end
end

local function tick()
    if live and runtime.running then
        local ok, err = pcall(function() live:poll(os.clock()) end)
        if not ok then log("Live configuration: " .. tostring(err)) end
    end
    if not runtime.running then return end
    if runtime.suspended and (not runtime.retry_at or os.clock() < runtime.retry_at) then return end
    local recovering = runtime.suspended
    local ok, err = xpcall(function()
        if recovering then
            restore()
            uninstall_hooks()
            runtime.generation, runtime.config_generation = nil, nil
            runtime.suspended = false
        end
        if Config.enabled == false then
            if runtime.active or runtime.generation ~= nil or backend.resource_costs
                or next(runtime.hooks) or runtime.session_hooks.installed then
                restore()
                uninstall_hooks()
                runtime.active, runtime.generation, runtime.config_generation = false,nil,nil
            end
            return
        end
        install_session_hooks()
        if not backend:refresh() then runtime.active = false; return end
        if backend.config_generation ~= runtime.config_generation then
            local nextConfig = backend:effective_config()
            if runtime.config_generation == nil then router = Router.new(nextConfig, backend)
            else
                router = router:reconfigure(nextConfig, nil, backend:context())
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
        local watched = router:keys()
        if runtime.switch_latch then
            local samples, included = {}, {}
            for _, key in ipairs(watched) do samples[#samples + 1] = key; included[key] = true end
            for key in pairs(runtime.switch_latch) do
                if not included[key] then samples[#samples + 1] = key; included[key] = true end
            end
            watched = samples
        end
        local snapshot = backend:snapshot(watched)
        if runtime.switch_latch then
            local held = false
            for key in pairs(runtime.switch_latch) do
                local value = snapshot[key]
                if value == true or type(value) == "number" and math.abs(value) > .001 then held = true end
            end
            if not held then runtime.switch_latch = nil end
        end
        context.raw_input = {}
        for key, value in pairs(snapshot) do context.raw_input[key] = value end
        if not runtime.active then
            router:resume(snapshot, context)
            runtime.active = true
        end
        if not runtime.session_hooks.complete and backend.session
            and backend.validate_session and not backend:validate_session() then
            runtime.active, runtime.generation = false, nil
            return
        end
        backend.dispatch_error = nil
        router:step(backend:time(), snapshot, context)
        if runtime.pending_scheme then
            local id = runtime.pending_scheme; runtime.pending_scheme = nil
            switch_scheme(id)
        end
    end, debug.traceback)
    if not ok then
        local restored, restoreError = pcall(restore)
        local unhooked, unhookError = pcall(uninstall_hooks)
        suspend(err)
        if not restored then runtime.last_error = runtime.last_error .. "\nInput restoration incomplete: " .. tostring(restoreError) end
        if not unhooked then runtime.last_error = runtime.last_error .. "\nHook removal incomplete: " .. tostring(unhookError) end
        publish_live()
        local diagnostic = tostring(err):match("^[^\n]+")
        if runtime.reported_error ~= diagnostic then
            runtime.reported_error = diagnostic
            log("Routing suspended; automatic retry with backoff: " .. runtime.last_error)
        end
    else
        runtime.last_error = nil
        if recovering then runtime.retry_at = nil; runtime.recovered_at = os.clock(); publish_live() end
        if runtime.recovered_at and os.clock() - runtime.recovered_at >= 30 then
            runtime.failed_at, runtime.retry_attempts, runtime.reported_error = nil, nil, nil
            runtime.recovered_at = nil
        end
    end
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
        runtime.last_error = "Game-thread frame scheduler unavailable; gameplay input remains native"
        publish_live()
        log(runtime.last_error)
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
            suspend(restoreError)
            log("Input restoration incomplete; automatic retry with backoff: " .. runtime.last_error)
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
publish_scheme()

return {backend = backend, stop = stop, reload = reload, switch_scheme = switch_scheme}
