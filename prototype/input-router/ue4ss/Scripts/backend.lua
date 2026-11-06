local PhysicalInput = require("physical-input")
local CompleteMoves = require("complete-moves")
local ResourceCosts = require("resource-costs")
local ObjectPool = require("object-pool")
local Backend = {}
Backend.__index = Backend

local function valid(value)
    if value == nil then return false end
    local ok, result = pcall(function() return value:IsValid() end)
    return ok and result == true
end

local function identity(value)
    return valid(value) and tostring(value:GetAddress()) or nil
end

function Backend.new(config, log)
    local source = debug.getinfo(1, "S").source
    local scripts = source:sub(1, 1) == "@" and source:sub(2):match("^(.*[/\\])")
    local focus, focusError, dllPath
    if scripts then
        local modRoot = scripts:gsub("Scripts[/\\]$", "")
        dllPath = config.focus_dll or (modRoot .. "lib/pbzinput-focus.dll")
        focus, focusError = package.loadlib(dllPath, "pbz_input_is_foreground")
    end
    local instance = setmetatable({config = config, log = log or print, objects = {}, functions = {},
        missing = {}, controller = nil, pawn = nil, subsystem = nil, captured = {},
        names = {}, key_dimensions = {}, focus_probe = focus,
        physical_input = PhysicalInput.new(dllPath), generation = 0, config_generation = 0,
        state = {ready = false, gameplay = false}}, Backend)
    if not focus and config.require_foreground ~= false then
        instance.log("Foreground probe unavailable; gameplay routing stays inactive: " .. tostring(focusError))
    end
    return instance
end

function Backend:report(key, message)
    if self.missing[key] == message then return end
    self.missing[key] = message
    self.log(message)
end

function Backend:object(path, className)
    local object = self.objects[path]
    if not valid(object) then
        local lookup = path
        local short = path:match("%.([^.:]+)$") or path:match("/([^/]+)$")
        if path:sub(1, 8) ~= "/Script/" and short and not path:find(".", 1, true) then
            lookup = path .. "." .. short
        end
        object = StaticFindObject(lookup)
        if not valid(object) and path:sub(1, 8) ~= "/Script/" then
            local function matches(candidate)
                if not valid(candidate) then return false end
                local found = candidate:GetFullName():gsub("^%S+ ", "")
                return found == lookup
            end
            if not className then
                if short:match("^IMC_") then className = "InputMappingContext"
                elseif short:match("^IA_") then className = "InputAction" end
            end
            if className then
                local candidate = FindObject(className, short)
                if matches(candidate) then object = candidate
                else
                    for _, item in ipairs(FindAllOf(className) or {}) do
                        if matches(item) then object = item; break end
                    end
                end
            end
        end
        if valid(object) then self.objects[path] = object end
    end
    if not valid(object) then error("Object is not loaded: " .. path, 0) end
    return object
end

function Backend:invoke(object, path, ...)
    if not valid(object) then error("Receiver unavailable for " .. path, 0) end
    local fn = self.functions[path]
    if not valid(fn) then
        fn = self:object(path)
        local functionClass = self:object("/Script/CoreUObject.Function")
        if not fn:IsA(functionClass) then error("Not a UFunction: " .. path, 0) end
        self.functions[path] = fn
    end
    return object:CallFunction(fn, ...)
end

function Backend:library(name)
    return self:object("/Script/" .. name:gsub("%.", ".Default__", 1))
end

function Backend:refresh()
    local controller = self.controller
    if not valid(controller) then
        controller = nil
        for _, candidate in ipairs(FindAllOf("PlayerController") or {}) do
            if valid(candidate) and self:invoke(candidate, "/Script/Engine.Controller:IsLocalPlayerController") then
                controller = candidate
                break
            end
        end
    end
    if not valid(controller) then
        self:retire()
        self.state = {ready = false, gameplay = false, generation = self.generation}
        return false
    end
    local pawn = self:invoke(controller, "/Script/Engine.Controller:K2_GetPawn")
    local changed = identity(controller) ~= identity(self.controller) or identity(pawn) ~= identity(self.pawn)
    if changed then
        self:retire()
        self.controller, self.pawn, self.subsystem = controller, pawn, nil
        self.ui_only = nil
        self.generation = self.generation + 1
        self.log("Local player changed; generation=" .. self.generation)
    end
    if not valid(self.subsystem) then
        local library = self:library("Engine.SubsystemBlueprintLibrary")
        self.subsystem = self:invoke(library,
            "/Script/Engine.SubsystemBlueprintLibrary:GetLocalPlayerSubSystemFromPlayerController",
            controller, self:object("/Script/EnhancedInput.EnhancedInputLocalPlayerSubsystem"))
    end
    local paused = self:invoke(self:library("Engine.GameplayStatics"),
        "/Script/Engine.GameplayStatics:IsGamePaused", controller)
    local moveIgnored = self:invoke(controller, "/Script/Engine.Controller:IsMoveInputIgnored")
    local cursor = controller.bShowMouseCursor == true
    local foreground = self.config.require_foreground == false or
        (type(self.focus_probe) == "function" and self.focus_probe(true) == true)
    local inputLayer = not self.config.unified_native or self:gameplay_input_layer()
    self.state = {ready = valid(pawn) and valid(self.subsystem), has_pawn = valid(pawn),
        paused = paused == true, cursor = cursor, foreground = foreground,
        move_ignored = moveIgnored == true, generation = self.generation,
        input_layer = inputLayer,
        gameplay = valid(pawn) and paused ~= true and not cursor and foreground and inputLayer}
    if self.state.gameplay and self.config.enabled ~= false then
        if self.config.dynamic_capture then self:prepare_dynamic_capture() end
        self:take_capture()
    else
        self:release_capture()
        if self.config.enabled ~= false and #(self.config.native_bindings or {}) > 0 then
            if not self.native_input then self.native_input = require("native-input").new(self) end
            self.native_input:apply_masks()
        end
    end
    if self.state.ready and self.state.gameplay and self.config.enabled ~= false then
        if self.resource_costs then self.resource_costs:update() end
        CompleteMoves.update(self)
    end
    return self.state.ready
end

function Backend:gameplay_input_layer()
    local class = self.input_router_class
    if not valid(class) then
        class = StaticFindObject("/Script/ProjectPBZ.PBZUIActionRouterBase")
        self.input_router_class = class
    end
    if not valid(class) then self:report("input-layer", "Gameplay input layer unavailable; native input retained"); return false end
    local router = self.input_router
    if not valid(router) then
        router = self:invoke(self:library("Engine.SubsystemBlueprintLibrary"),
            "/Script/Engine.SubsystemBlueprintLibrary:GetLocalPlayerSubSystemFromPlayerController", self.controller, class)
        self.input_router = router
    end
    if not valid(router) then return false end
    local mode = self:invoke(router, "/Script/ProjectPBZ.PBZUIActionRouterBase:GetActiveInputMode")
    if mode == 0 or self.ui_only then return false end
    for _, name in ipairs({"EditableText", "EditableTextBox", "MultiLineEditableText", "MultiLineEditableTextBox"}) do
        for _, widget in ipairs(ObjectPool.get(name, "/Script/UMG." .. name)) do
            if valid(widget) and (self:invoke(widget, "/Script/UMG.Widget:HasKeyboardFocus")
                or self:invoke(widget, "/Script/UMG.Widget:HasFocusedDescendants")) then return false end
        end
    end
    return mode == 1 or mode == 2
end

function Backend:take_capture()
    if not valid(self.subsystem) then return end
    for _, entry in ipairs(self.dynamic_contexts or self.config.capture_contexts or {}) do
        local path = type(entry) == "string" and entry or entry.path
        local ok, result = pcall(function()
            local context = self:object(path)
            for removal = 1, math.max(1, type(entry) == "table" and entry.registration_count or 1) do
                local priority = {}
                if not self:invoke(self.subsystem,
                    "/Script/EnhancedInput.EnhancedInputSubsystemInterface:HasMappingContext", context, priority) then return end
                if type(priority.OutFoundPriority) ~= "number" then
                    error("HasMappingContext did not return OutFoundPriority", 0)
                end
                self:invoke(self.subsystem, "/Script/EnhancedInput.EnhancedInputSubsystemInterface:RemoveMappingContext",
                    context, {bIgnoreAllPressedKeysUntilRelease = false, bForceImmediately = true,
                        bNotifyUserSettings = false})
                local previous = self.captured[path]
                self.captured[path] = {object = context, priority = priority.OutFoundPriority,
                    removals = (previous and previous.removals or 0) + 1}
            end
        end)
        if not ok then error("Capture failed for " .. path .. ": " .. tostring(result), 0) end
    end
end

function Backend:retire()
    if self.on_retire then self.on_retire() end
    self:release_capture()
    self.controller, self.pawn, self.subsystem = nil, nil, nil
    self.input_router = nil
end

function Backend:release_capture()
    CompleteMoves.reset(self)
    if self.resource_costs then self.resource_costs:reset(); self.resource_costs = nil end
    if self.native_abilities then self.native_abilities:restore() end
    if self.native_input then self.native_input:restore() end
    local released, releaseError = true, nil
    if (next(self.captured) or self.dynamic_config) and self.on_release then released, releaseError = pcall(self.on_release) end
    local failed = {}
    if valid(self.subsystem) then
        for path, entry in pairs(self.captured) do
            if valid(entry.object) then
                local ok, err = pcall(function()
                    while entry.removals > 0 do
                        self:invoke(self.subsystem, "/Script/EnhancedInput.EnhancedInputSubsystemInterface:AddMappingContext",
                            entry.object, entry.priority, {bIgnoreAllPressedKeysUntilRelease = false,
                                bForceImmediately = true, bNotifyUserSettings = false})
                        entry.removals = entry.removals - 1
                    end
                end)
                if not ok then
                    failed[path] = entry
                    self:report("restore:" .. path, "Context restore failed: " .. tostring(err))
                end
            else failed[path] = entry end
        end
    elseif next(self.captured) then
        for path, entry in pairs(self.captured) do failed[path] = entry end
    end
    self.captured = failed
    if next(failed) then error("Original input context restore is incomplete", 0) end
    if not released then error("Action release is incomplete: " .. tostring(releaseError), 0) end
    self:release_holder()
    if self.dynamic_config then
        self.dynamic_config, self.dynamic_contexts = nil, nil
        self.config_generation = self.config_generation + 1
    end
end

local function array_values(array)
    local result = {}
    array:ForEach(function(index, item) result[#result + 1] = item:get() end)
    return result
end

function Backend:hold_objects(triggers, modifiers)
    if valid(self.holder) then
        self.holder:SetPropertyValue("Triggers", triggers)
        self.holder:SetPropertyValue("Modifiers", modifiers)
        return
    end
    local actionClass = self:object("/Script/EnhancedInput.InputAction")
    local action = StaticConstructObject(actionClass, self.subsystem)
    local holder = StaticConstructObject(actionClass, self.subsystem)
    local settings = StaticConstructObject(self:object("/Script/EnhancedInput.PlayerMappableKeySettings"), action)
    if not valid(action) or not valid(holder) or not valid(settings) then
        error("Cannot create dynamic input GC holder", 0)
    end
    holder:SetPropertyValue("Triggers", triggers)
    holder:SetPropertyValue("Modifiers", modifiers)
    settings:SetPropertyValue("Metadata", holder)
    action:SetPropertyValue("PlayerMappableKeySettings", settings)
    self:invoke(self.subsystem,
        "/Script/EnhancedInput.EnhancedInputSubsystemInterface:StartContinuousInputInjectionForAction",
        action, {}, {}, {})
    self.holder, self.holder_action = holder, action
end

function Backend:release_holder()
    if valid(self.holder_action) and valid(self.subsystem) then
        self:invoke(self.subsystem,
            "/Script/EnhancedInput.EnhancedInputSubsystemInterface:StopContinuousInputInjectionForAction", self.holder_action)
    end
    for _, action in ipairs(self.context_holders or {}) do
        if valid(action) and valid(self.subsystem) then
            self:invoke(self.subsystem,
                "/Script/EnhancedInput.EnhancedInputSubsystemInterface:StopContinuousInputInjectionForAction", action)
        end
    end
    self.context_holders = nil
    self.holder, self.holder_action = nil, nil
end

function Backend:hold_contexts(contexts)
    self.context_holders = self.context_holders or {}
    for _, entry in ipairs(contexts) do
        local action = StaticConstructObject(self:object("/Script/EnhancedInput.InputAction"), self.subsystem)
        local settings = StaticConstructObject(self:object("/Script/EnhancedInput.PlayerMappableKeySettings"), action)
        if not valid(action) or not valid(settings) then error("Cannot retain mapping context", 0) end
        settings:SetPropertyValue("Metadata", self:object(entry.path))
        action:SetPropertyValue("PlayerMappableKeySettings", settings)
        self:invoke(self.subsystem,
            "/Script/EnhancedInput.EnhancedInputSubsystemInterface:StartContinuousInputInjectionForAction", action, {}, {}, {})
        self.context_holders[#self.context_holders + 1] = action
    end
end

function Backend:effective_config()
    return self.dynamic_config or self.config
end

function Backend:action_identity(spec)
    if spec.native_action_id then return spec.native_action_id end
    return identity(spec.object or self:object(spec.path, "InputAction"))
end

function Backend:pipeline_identity(spec)
    if spec.source_relation then return spec.source_relation end
    local parts = {}
    for _, item in ipairs(spec.modifier_objects or {}) do parts[#parts+1] = "m:" .. tostring(identity(item)) end
    for _, item in ipairs(spec.trigger_objects or {}) do parts[#parts+1] = "t:" .. tostring(identity(item)) end
    for _, item in ipairs(spec.modifiers or {}) do parts[#parts+1] = "m:" .. item end
    for _, item in ipairs(spec.triggers or {}) do parts[#parts+1] = "t:" .. item end
    return table.concat(parts, "|")
end

function Backend:baseline_suppressed(action, key, mappingName)
    local actual = action:GetFullName():gsub("^%S+ ", "")
    local function path(value)
        local short = value:match("/([^/]+)$")
        return short and not short:find(".", 1, true) and value .. "." .. short or value
    end
    local device = (key:match("^Gamepad_") or key:match("^PBZ_Touchpad_")) and "gamepad" or "keyboard"
    for _, override in ipairs(self.config.native_bindings or {}) do
        local owned = mappingName and (not override["function"] or mappingName == override["function"])
        for _, fixed in ipairs(override.fixed_keys or {}) do if not mappingName and fixed == key then owned = true end end
        if override.device == device and owned then
            for _, value in ipairs(override.actions or {}) do if path(value) == path(actual) then return true end end
        end
    end
    for _, suppression in ipairs(self.config.suppressions or {}) do
        if not suppression.key or suppression.key == key then
            if path(suppression.action) == path(actual) then return true end
        end
    end
    return false
end

local function action_path(action)
    local path = action:GetFullName():gsub("^%S+ ", "")
    return path:gsub("%.[^./]+$", "")
end

function Backend:prepare_dynamic_capture()
    if self.native_abilities or #(self.config.ability_suppressions or {}) > 0 then
        if not self.native_abilities then self.native_abilities = require("native-abilities").new(self) end
        self.native_abilities:prepare()
    end
    if not self.native_input then self.native_input = require("native-input").new(self) end
    self.native_input:prepare()
end

function Backend:reserve_inputs(reserved, snapshot)
    if self.native_input then self.native_input:apply_masks(reserved, snapshot) end
end

function Backend:clone_pipeline_objects(objects)
    local result = {}
    for _, object in ipairs(objects) do
        local clone = StaticConstructObject(object:GetClass(), self.subsystem, 0, 0, 0, false, false, object)
        if not valid(clone) then error("Cannot create an independent input pipeline", 0) end
        result[#result + 1] = clone
    end
    return result
end

function Backend:reset_trigger_objects(triggers)
    if #triggers == 0 then return end
    local zero = self:object("/Script/EnhancedInput.Default__InputTriggerDown").LastValue
    for _, trigger in ipairs(triggers) do
        -- Evaluate the release locally; its result must never reach the action.
        self:invoke(trigger, "/Script/EnhancedInput.InputTrigger:UpdateState", self.controller.PlayerInput, zero, 0)
        trigger:SetPropertyValue("LastValue", zero)
    end
end

function Backend:key_dimension(key)
    if self.key_dimensions[key] then return self.key_dimensions[key] end
    local library, keyStruct = self:library("Engine.KismetInputLibrary"), {KeyName = FName(key)}
    local dimension = 1
    if self:invoke(library, "/Script/Engine.KismetInputLibrary:Key_IsAxis3D", keyStruct) then dimension = 3
    elseif self:invoke(library, "/Script/Engine.KismetInputLibrary:Key_IsAxis2D", keyStruct) then dimension = 2 end
    self.key_dimensions[key] = dimension
    return dimension
end

function Backend:context()
    self.state.pawn, self.state.controller = self.pawn, self.controller
    self.state.subsystem, self.state.backend = self.subsystem, self
    return self.state
end

function Backend:allowed(condition, context)
    if not condition then return true end
    if type(condition) == "function" then return condition(context) == true end
    if type(condition) == "string" then return context[condition] == true end
    for key, expected in pairs(condition) do
        if context[key] ~= expected then return false end
    end
    return true
end

function Backend:snapshot(keys)
    local result = {}
    if not valid(self.controller) then return result end
    local samples, seen, vectors = {}, {}, {}
    local function vector_value(key)
        if not vectors[key] then
            local name = self.names[key]
            if not name then name = FName(key); self.names[key] = name end
            vectors[key] = self:invoke(self.controller, "/Script/Engine.PlayerController:GetInputVectorKeyState", {KeyName=name})
        end
        return vectors[key]
    end
    for _, key in ipairs(keys) do samples[#samples + 1] = key; seen[key] = true end
    for key in pairs(self.native_input and self.native_input.consumed or {}) do
        if not seen[key] then samples[#samples + 1] = key end
    end
    for _, key in ipairs(samples) do
        local vectorKey, component = key:match("^(.-)@([123])$")
        if vectorKey then
            local vector = vector_value(vectorKey)
            result[key] = vector[({"X", "Y", "Z"})[tonumber(component)]]
        elseif self:key_dimension(key) > 1 then
            local vector = vector_value(key)
            result[key] = math.max(math.abs(vector.X), math.abs(vector.Y), math.abs(vector.Z))
        else
            local value = self.physical_input:sample(key)
            if value == nil then
                local fname = self.names[key]
                if not fname then fname = FName(key); self.names[key] = fname end
                local keyStruct = {KeyName = fname}
                value = self:invoke(self.controller, "/Script/Engine.PlayerController:GetInputAnalogKeyState", keyStruct)
                if value == 0 then
                    value = self:invoke(self.controller, "/Script/Engine.PlayerController:IsInputKeyDown", keyStruct) and 1 or 0
                end
            end
            result[key] = value
        end
    end
    return result
end

function Backend:time()
    return self:invoke(self:library("Engine.GameplayStatics"),
        "/Script/Engine.GameplayStatics:GetRealTimeSeconds", self.controller)
end

function Backend:receiver(spec)
    local name = spec.receiver or "pawn"
    if type(name) == "function" then return name(self, self:context()) end
    if name == "pawn" then return self.pawn end
    if name == "controller" then return self.controller end
    if name == "subsystem" then return self.subsystem end
    if name == "ability_input" then
        return self:invoke(self.pawn, "/Script/Engine.Actor:GetComponentByClass",
            self:object("/Script/SGameInputSystem.AbilityInputComponent"))
    end
    if name == "ability_system" then
        return self:invoke(self:library("GameplayAbilities.AbilitySystemBlueprintLibrary"),
            "/Script/GameplayAbilities.AbilitySystemBlueprintLibrary:GetAbilitySystemComponent", self.pawn)
    end
    return self:object(name, spec.receiver_class)
end

function Backend:argument(value, context)
    if type(value) == "function" then return value(self, context) end
    if type(value) ~= "table" then return value end
    if value.object then return self:object(value.object, value.class) end
    if value.receiver then return self:receiver(value) end
    if value.fname then return FName(value.fname) end
    if value.tag then return {TagName = FName(value.tag)} end
    if value.value then return context.value end
    if value.phase then return context.phase end
    local result = {}
    for key, item in pairs(value) do result[key] = self:argument(item, context) end
    return result
end

function Backend:object_array(paths, className)
    local result = {}
    for i, path in ipairs(paths or {}) do result[i] = self:object(path, className) end
    return result
end

function Backend:activate_with_resources(spec, bypass, callback, settings)
    return ResourceCosts.with(self,spec,bypass,callback,settings)
end

function Backend:prepare_input_settings(requests)
    if not self.resource_costs then
        for _,request in ipairs(requests) do
            local s = request.settings or {}
            if request.bypass_resources or (s.damage_multiplier or 1) ~= 1
                or (s.startup_speed or 1) ~= 1 or (s.recovery_speed or 1) ~= 1 then
                self.resource_costs = ResourceCosts.new(self)
                break
            end
        end
    end
    if self.resource_costs then
        for _,request in ipairs(requests) do
            if request.target.kind == "action" then
                self.resource_costs:input(request.target,request.bypass_resources,request.settings,request.rank)
            else self.resource_costs:prepare(request.target,request.bypass_resources,request.settings,request.rank) end
        end
    end
end

function Backend:dispatch(spec, phase, value, bindingId, bypass, settings)
    if spec.kind == "action" and phase == "canceled" and (not valid(self.controller) or not valid(self.subsystem)) then return true end
    if not self.state.ready then return false end
    local ok, result = xpcall(function()
        if spec.kind == "action" then
            if (self.resource_costs or bypass or settings and (settings.damage_multiplier or settings.startup_speed or settings.recovery_speed)) and phase ~= "completed" and phase ~= "canceled" then
                if not self.resource_costs then self.resource_costs = ResourceCosts.new(self) end
                self.resource_costs:input(spec,bypass,settings)
            end
            local canceled = phase == "canceled"
            if canceled and spec.source_relation then self:reset_trigger_objects(spec.trigger_objects or {}) end
            local vector = (phase == "completed" or canceled) and {X = 0, Y = 0, Z = 0}
                or {X = value and (value.x or value.X) or 1,
                    Y = value and (value.y or value.Y) or 0,
                    Z = value and (value.z or value.Z) or 0}
            return self:invoke(self.subsystem,
                "/Script/EnhancedInput.EnhancedInputSubsystemInterface:InjectInputVectorForAction",
                spec.object or self:object(spec.path, "InputAction"), vector,
                canceled and {} or spec.modifier_objects or self:object_array(spec.modifiers, "InputModifier"),
                canceled and {} or spec.trigger_objects or self:object_array(spec.triggers, "InputTrigger"))
        end
        if phase == "completed" then return true end
        if spec.kind == "scheme" then
            if phase == "triggered" and self.request_scheme then return self.request_scheme(spec.scheme, bindingId) end
            return true
        end
        if spec.kind == "mapping" then return true end
        if spec.kind == "move" then return CompleteMoves.dispatch(self, spec, phase, bypass,settings) end
        if spec.kind == "lua" then
            return spec.run(self, {phase = phase, value = value, binding = bindingId,
                state = self:context()})
        end
        if spec.kind == "ability" then
            local component = self:receiver({receiver = "ability_input"})
            local method = spec.force and "ForceActivateExternalActionAbilityByTag"
                or "TryActivateExternalActionAbilityInterruptLowerByTag"
            return self:activate_with_resources(spec,bypass,function()
                return self:invoke(component, "/Script/SGameInputSystem.AbilityInputComponent:" .. method,
                    {TagName = FName(spec.tag)},
                    self:object(spec.input_action or "/PBZDeflectSystem/Input/IA_Dodge.IA_Dodge"), spec.priority or -5)
            end,settings)
        end
        if spec.kind == "event" then
            local tag = {TagName = FName(spec.tag)}
            local payload = self:argument(spec.payload or {}, {value = value, phase = phase})
            payload.EventTag = tag
            payload.Instigator = payload.Instigator or self.pawn
            payload.Target = payload.Target or self.pawn
            payload.EventMagnitude = payload.EventMagnitude or (value and value.x) or 1
            return self:invoke(self:library("GameplayAbilities.AbilitySystemBlueprintLibrary"),
                "/Script/GameplayAbilities.AbilitySystemBlueprintLibrary:SendGameplayEventToActor",
                self.pawn, tag, payload)
        end
        if spec.kind == "function" then
            local arguments = {}
            for i, item in ipairs(spec.args or {}) do
                arguments[i] = self:argument(item, {value = value, phase = phase})
            end
            return self:invoke(self:receiver(spec), spec.path, table.unpack(arguments, 1, #arguments))
        end
        if spec.kind == "property" then
            local receiver = self:receiver(spec)
            if not valid(receiver) then error("Property receiver unavailable", 0) end
            receiver:SetPropertyValue(spec.property, self:argument(spec.value, {value = value, phase = phase}))
            return true
        end
        error("Unsupported target kind: " .. tostring(spec.kind), 0)
    end, debug.traceback)
    if not ok then
        self.dispatch_error = "Dispatch failed [" .. tostring(bindingId) .. "]: " .. tostring(result)
        self:report("dispatch:" .. tostring(bindingId) .. ":" .. tostring(spec.path or spec.tag),
            self.dispatch_error)
    end
    return ok, result
end

function Backend:scheme_notice(name)
    local ok, err = pcall(function()
        self:invoke(self:library("Engine.KismetSystemLibrary"), "/Script/Engine.KismetSystemLibrary:PrintString",
            self.controller, tostring(name), true, false, {R=.76,G=.60,B=1,A=1}, 2, "PBZKeyForgeScheme")
    end)
    if not ok then self:report("scheme-notice", tostring(err)) end
end

function Backend:shutdown()
    self:release_capture()
end

return Backend
