local NativeInput = {}
NativeInput.__index = NativeInput

local function values(array)
    local result = {}
    if not array then return result end
    array:ForEach(function(_, item) result[#result + 1] = item:get() end)
    return result
end

local function address(object)
    return object and object:IsValid() and object:GetAddress() or nil
end

local function same_objects(first, second)
    if #first ~= #second then return false end
    for index, object in ipairs(first) do
        if address(object) ~= address(second[index]) then return false end
    end
    return true
end

local function same_pipeline(first, second)
    return first and second and address(first.object) == address(second.object)
        and same_objects(first.modifier_objects, second.modifier_objects)
        and same_objects(first.trigger_objects, second.trigger_objects)
end

local function path(action)
    return action:GetFullName():gsub("^%S+ ", ""):gsub("%.[^./]+$", "")
end

function NativeInput.new(backend)
    return setmetatable({backend = backend, masked = {}, consumed = {}, maintained = {}, remapped = {}, expanded = nil}, NativeInput)
end

function NativeInput:mappings()
    local controller = self.backend.controller
    local input = controller and controller.PlayerInput
    if not input or not input:IsValid() then error("Local EnhancedPlayerInput unavailable", 0) end
    local mappings = input:GetPropertyValue("EnhancedActionMappings")
    if not mappings then error("EnhancedPlayerInput runtime mappings unavailable", 0) end
    return mappings
end

local function row(mapping)
    return {object = mapping.Action, modifier_objects = values(mapping.Modifiers),
        trigger_objects = values(mapping.Triggers), location = mapping:GetStructAddress()}
end

local movement = {
    ["/SGameLocomotionDriver/Input/IA_KeyboardMoveInput"] = true,
    ["/SGameLocomotionDriver/Input/IA_JoystickMoveInput"] = true,
    ["/SGameLocomotionDriver/Input/IA_Sprint"] = true,
}

local function continuous_triggers(triggers)
    for _, trigger in ipairs(triggers) do
        local class = trigger:GetClass()
        local name = type(class) == "string" and class or class:GetFullName()
        if not name:match("InputTriggerDown$") then return false end
    end
    return true
end

local function continuous_pipeline(source)
    return continuous_triggers(source.trigger_objects) and continuous_triggers(values(source.object.Triggers))
end

local resettable = {InputTriggerDown=true, InputTriggerPressed=true, InputTriggerReleased=true,
    InputTriggerHold=true, InputTriggerHoldAndRelease=true, InputTriggerTap=true, InputTriggerPulse=true,
    InputTrigger_HoldAndReleaseEarly=true}

local function schedulable(source, key)
    if key == "Mouse2D" or key == "MouseX" or key == "MouseY" or key:match("^MouseScroll") or key:match("^PBZ_Touchpad_") then return false, "input source remains native" end
    for _, trigger in ipairs(source.trigger_objects) do
        local class = trigger:GetClass()
        local name = type(class) == "string" and class or class:GetFullName()
        if name:find("Chord", 1, true) or name:find("Touchpad", 1, true) then return false, "dependent trigger remains native" end
        local short = name:match("([^%. /]+)$")
        if not resettable[short] or trigger.ActuationThreshold <= 0
            or short == "InputTriggerHold" and trigger.HoldTimeThreshold <= 0 then
            return false, "trigger cancellation reset is not covered: " .. name
        end
    end
    if not continuous_triggers(values(source.object.Triggers)) then return false, "action-level temporal trigger remains native" end
    return true
end

function NativeInput:original_key(index, mapping, current)
    local key = mapping.Key.KeyName:ToString()
    for _, old in pairs(self.remapped or {}) do
        if old.location == current.location and same_pipeline(old, current) then return old.key end
    end
    if key == "None" then
        for _, old in pairs(self.masked) do
            if old.location == current.location and same_pipeline(old, current) then return old.key end
        end
    end
    return key
end

local function device_of(key)
    if key == "None" then return nil end
    return (key:match("^Gamepad_") or key:match("^PBZ_Touchpad_")) and "gamepad" or "keyboard"
end

function NativeInput:source_key(mapping, index)
    local current = row(mapping)
    local key = self:original_key(index, mapping, current)
    if key ~= "None" then return key end
    -- A saved None retains its action and pipeline; the registered context keeps its device.
    local contexts = self.backend.controller.PlayerInput:GetPropertyValue("AppliedInputContextData")
    local found, ambiguous
    contexts:ForEach(function(entry)
        local context = entry:get()
        local defaults = context.DefaultKeyMappings.Mappings
        defaults:ForEach(function(_, item)
            local template = item:get()
            if same_pipeline(current, row(template)) then
                local original = template.Key.KeyName:ToString()
                if original ~= "None" then
                    if found and device_of(found) ~= device_of(original) then ambiguous = true else found = original end
                end
            end
        end)
    end)
    return not ambiguous and found or key
end

function NativeInput:apply_masks(reserved, snapshot)
    if reserved then
        for key in pairs(reserved) do self.consumed[key] = true end
        for key in pairs(self.consumed) do
            local value = snapshot[key]
            if value ~= true and (type(value) ~= "number" or math.abs(value) < 0.001) then
                self.consumed[key] = nil
            end
        end
    end
    if not next(self.masked) and not next(self.consumed) and not next(self.maintained) and #(self.backend.config.suppressions or {}) == 0
        and #(self.backend.config.native_bindings or {}) == 0 and not next(self.remapped or {}) then return end
    local masked, remapped, positions = {}, {}, {}
    -- UE4SS does not root the ForEach receiver while Lua callbacks allocate.
    local mappings = self:mappings()
    mappings:ForEach(function(index, entry)
        local mapping = entry:get()
        local current = row(mapping)
        local original = self:original_key(index, mapping, current)
        local key = original == "None" and self:source_key(mapping, index) or original
        local unified = self.backend.config.unified_native
        local replacement = original
        if not self.maintained[index] and self.backend.config.enabled ~= false then
            local device = device_of(key)
            for _, override in ipairs(self.backend.config.native_bindings or {}) do
                if override.device == device then
                    for _, action in ipairs(override.actions or {}) do
                        if action:gsub("%.[^./]+$", "") == path(mapping.Action) then
                            local identity = action .. ":" .. device
                            positions[identity] = (positions[identity] or 0) + 1
                            replacement = #override.keys > 0 and override.keys[(positions[identity]-1) % #override.keys + 1] or "None"
                            current.key = original; remapped[index] = current
                        end
                    end
                end
            end
        end
        local mask = key ~= "None" and (self.maintained[index] or not unified and (self.consumed[key] or self.backend:baseline_suppressed(mapping.Action, key)))
        if mask then
            current.key = original
            masked[index] = current
            mapping.Key.KeyName = FName("None")
        elseif mapping.Key.KeyName:ToString() ~= replacement then
            mapping.Key.KeyName = FName(replacement)
        end
    end)
    self.masked = masked
    self.remapped = remapped
end

function NativeInput:restore()
    local controller = self.backend.controller
    local input = controller and controller:IsValid() and controller.PlayerInput
    if (next(self.masked) or next(self.remapped or {})) and input and input:IsValid() then
        local mappings = self:mappings()
        mappings:ForEach(function(index, entry)
            local mapping = entry:get()
            local current = row(mapping)
            local original = self:original_key(index, mapping, current)
            if mapping.Key.KeyName:ToString() ~= original then
                mapping.Key.KeyName = FName(original)
            end
        end)
    end
    self.masked, self.consumed, self.maintained, self.remapped, self.expanded = {}, {}, {}, {}, nil
end

function NativeInput:prepare()
    local backend, expanded, sources, maintained = self.backend, {}, {}, {}
    local candidates = {}; for _, action in ipairs(backend.config.native_actions or {}) do candidates[action] = true end
    local coverage = {}
    self.maintained = {}
    for name, target in pairs(backend.config.targets or {}) do
        if target.kind == "mapping" then expanded[name] = {} end
    end
    local remembered = {}
    local function remember(mapping, suffix, key)
        if remembered[suffix] then return remembered[suffix], sources[remembered[suffix]] end
        local current = row(mapping)
        local name
        for previousName, previous in pairs(self.expanded or {}) do
            if not sources[previousName] and previous.key == key and same_pipeline(current, previous) then name = previousName; break end
        end
        if not name then
            self.next_source_id = (self.next_source_id or 0) + 1
            name = "native_mapping_" .. self.next_source_id
        end
        current.kind, current.key = "action", key
        sources[name] = current
        remembered[suffix] = name
        return name, current
    end
    local mappings = self:mappings()
    mappings:ForEach(function(index, entry)
        local mapping = entry:get()
        local current = row(mapping)
        local key = self:original_key(index, mapping, current)
        local actionPath = path(mapping.Action)
        local candidate = backend.config.unified_native and candidates[actionPath] or not backend.config.unified_native and movement[actionPath] and continuous_pipeline(current)
        local supported, reason = schedulable(current, key)
        if candidate and key ~= "None" and supported and (backend.config.unified_native or not backend:baseline_suppressed(mapping.Action, key)) then
            local name = remember(mapping, index, key)
            maintained[#maintained + 1] = {target=name, key=key, axis=true, action=mapping.Action,
                suppressed=backend:baseline_suppressed(mapping.Action, key)}
            self.maintained[index] = true
            coverage[#coverage+1] = {action=actionPath, key=key, owner=backend:baseline_suppressed(mapping.Action,key) and "suppressed" or "scheduler"}
        elseif candidate and key == "None" and supported then
            local overridden = false
            for _, override in ipairs(backend.config.native_bindings or {}) do
                for _, action in ipairs(override.actions or {}) do
                    if action:gsub("%.[^./]+$", "") == actionPath then overridden = true end
                end
            end
            if overridden then self.maintained[index] = true end
            coverage[#coverage+1] = {action=actionPath, key=key, owner=overridden and "scheduler" or "uncovered", reason="unbound mapping"}
        elseif candidate then
            coverage[#coverage+1] = {action=actionPath, key=key, owner="uncovered", reason=reason or "unbound mapping"}
        else
            coverage[#coverage+1] = {action=actionPath, key=key, owner="game", reason="outside registered gameplay domain"}
        end
    end)
    self.coverage = coverage
    local lines = {}
    for _, entry in ipairs(coverage) do lines[#lines+1] = entry.action .. " [" .. entry.key .. "] " .. entry.owner .. (entry.reason and ": " .. entry.reason or "") end
    local signature = table.concat(lines, "\n")
    if backend.config.unified_native and self.coverage_signature ~= signature then
        self.coverage_signature = signature
        backend.log("Gameplay input ownership:\n" .. signature)
    end
    if next(expanded) then
        local nativePaths = {}
        for name in pairs(expanded) do nativePaths[name] = {} end
        local function expand(mapping, suffix, fallback)
            local current = row(mapping)
            local key = fallback and mapping.Key.KeyName:ToString() or self:original_key(suffix, mapping, current)
            if key == "None" then return end
            local nativePath = path(mapping.Action)
            if backend.config.unified_native and (not candidates[nativePath] or not schedulable(current, key)) then return end
            local pad = key:match("^Gamepad_") or key:match("^PBZ_Touchpad_")
            local device = pad and "gamepad" or "keyboard"
            local identity = nativePath .. ":" .. device
            for name, outputs in pairs(expanded) do
                local target = backend.config.targets[name]
                if (not target.device or target.device == device)
                    and (not fallback or not nativePaths[name][identity]) then
                    for _, targetPath in ipairs(target.actions or {}) do
                        if targetPath:gsub("%.[^./]+$", "") == nativePath then
                            local cloneName = remember(mapping, suffix, key)
                            outputs[#outputs + 1] = cloneName
                            if not fallback then nativePaths[name][identity] = true end
                            break
                        end
                    end
                end
            end
        end
        mappings:ForEach(function(index, entry) expand(entry:get(), index, false) end)
        local contexts = backend.controller.PlayerInput:GetPropertyValue("AppliedInputContextData")
        contexts:ForEach(function(key)
            local context = key:get()
            local defaults = context.DefaultKeyMappings.Mappings
            defaults:ForEach(function(index, entry)
                expand(entry:get(), "template_" .. tostring(context:GetAddress()) .. "_" .. index, true)
            end)
        end)
    end
    if backend.live_validation then
        for name, outputs in pairs(expanded) do
            if #outputs == 0 then error("No runtime input pipeline for " .. tostring(backend.config.targets[name].mapping or name), 0) end
        end
    end
    do
        for _, override in ipairs(backend.config.native_bindings or {}) do
            for _, requested in ipairs(override.actions or {}) do
                local actionPath = requested:gsub("%.[^./]+$", "")
                local count, captured = 0, false
                for name, outputs in pairs(expanded) do
                    local target = backend.config.targets[name]
                    if #outputs > 0 and target.device == override.device then
                        for _, action in ipairs(target.actions or {}) do
                            if action:gsub("%.[^./]+$", "") == actionPath then captured = true end
                        end
                    end
                end
                mappings:ForEach(function(index, entry)
                    local mapping = entry:get()
                    if path(mapping.Action) == actionPath and device_of(self:source_key(mapping, index)) == override.device then
                        if self.maintained[index] then captured = true else count = count + 1 end
                    end
                end)
                if not captured and #override.keys > count then
                    error("Native input " .. tostring(override["function"]) .. " has " .. count .. " active runtime slots for " .. override.device .. "; requested " .. #override.keys, 0)
                end
            end
        end
    end
    local changed = self.expanded == nil
    for name, source in pairs(sources) do
        if not self.expanded or not same_pipeline(source, self.expanded[name]) or source.key ~= self.expanded[name].key then changed = true end
    end
    for name in pairs(self.expanded or {}) do if not sources[name] then changed = true end end
    if changed then
        local config = {}
        for key, value in pairs(backend.config) do config[key] = value end
        config.targets, config.bindings = {}, {}
        local triggers, modifiers = {}, {}
        for name, target in pairs(backend.config.targets) do
            config.targets[name] = target
            if target.kind == "action" and movement[(target.path or ""):gsub("%.[^./]+$", "")] then
                local copy = {}; for key, value in pairs(target) do copy[key] = value end
                local action = target.object or backend:object(target.path, "InputAction")
                local pipeline = target.trigger_objects or backend:object_array(target.triggers, "InputTrigger")
                copy.native_continuous, copy.resume_continuous = true, continuous_pipeline({object=action, trigger_objects=pipeline})
                config.targets[name] = copy
            end
        end
        for name, source in pairs(sources) do
            local previous = self.expanded and self.expanded[name]
            local target = same_pipeline(source, previous) and source.key == previous.key and backend.dynamic_config.targets[name] or
                {kind = "action", object = source.object,
                    modifier_objects = backend:clone_pipeline_objects(source.modifier_objects),
                    trigger_objects = backend:clone_pipeline_objects(source.trigger_objects),
                    resume_continuous = movement[path(source.object)] == true and continuous_pipeline(source),
                    source_relation=name, native_continuous = movement[path(source.object)] == true}
            if target ~= (backend.dynamic_config and backend.dynamic_config.targets[name]) and backend.config.unified_native then
                backend:reset_trigger_objects(target.trigger_objects)
            end
            config.targets[name] = target
            for _, object in ipairs(target.trigger_objects) do triggers[#triggers + 1] = object end
            for _, object in ipairs(target.modifier_objects) do modifiers[#modifiers + 1] = object end
        end
        for _, binding in ipairs(backend.config.bindings) do
            local copy, outputs = {}, {}
            for key, value in pairs(binding) do copy[key] = value end
            for _, output in ipairs(binding.outputs or {}) do
                local name = type(output) == "string" and output or output.target
                if expanded[name] then
                    for _, cloneName in ipairs(expanded[name]) do
                        outputs[#outputs + 1] = {target = cloneName, value = type(output) == "table" and output.value or nil}
                    end
                else outputs[#outputs + 1] = output end
            end
            if #outputs > 0 then copy.outputs = outputs; config.bindings[#config.bindings + 1] = copy end
        end
        for _, entry in ipairs(maintained) do
            local key = entry.key
            config.bindings[#config.bindings + 1] = {id="maintain:" .. entry.target,
                keys=entry.axis and {} or {entry.key}, axis=entry.axis and entry.key or nil,
                trigger=entry.axis and "axis" or "down", threshold=.001, priority=-2147483648,
                outputs={entry.target}, native_continuous=true, relation_key=key,
                when=function() return not self.consumed[key] and not entry.suppressed end}
        end
        if #triggers > 0 or #modifiers > 0 then backend:hold_objects(triggers, modifiers) end
        backend.dynamic_config, backend.dynamic_contexts = config, {}
        backend.config_generation = backend.config_generation + 1
        self.expanded = sources
        backend.log("Gameplay scheduler relations=" .. #config.bindings)
    end
    self:apply_masks()
end

return NativeInput
