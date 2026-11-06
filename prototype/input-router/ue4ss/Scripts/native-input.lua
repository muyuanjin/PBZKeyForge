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
        and first.mapping_name == second.mapping_name
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

local function mapping_name(mapping)
    local behavior = mapping.SettingBehavior
    local settings
    if behavior == 0 then settings = mapping.Action.PlayerMappableKeySettings
    elseif behavior == 1 then settings = mapping.PlayerMappableKeySettings end
    if settings and settings:IsValid() then
        local name = settings.Name:ToString()
        if name ~= "None" then return name end
    end
    return false
end

local function owns_mapping(spec, current, key)
    if spec.catalog_pipeline then return true end
    if current.mapping_name then
        return not (spec.mapping or spec["function"]) or current.mapping_name == (spec.mapping or spec["function"])
    end
    for _, fixed in ipairs(spec.fixed_keys or {}) do if fixed == key then return true end end
    return false
end

local function row(mapping)
    return {object = mapping.Action, modifier_objects = values(mapping.Modifiers),
        trigger_objects = values(mapping.Triggers), location = mapping:GetStructAddress(), mapping_name = mapping_name(mapping)}
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

local function class_name(object)
    local class = object:GetClass()
    return (type(class) == "string" and class or class:GetFullName()):match("([^%. /]+)$")
end

local function same_movement_pipeline(first, second)
    if not first or not second or address(first.object) ~= address(second.object)
        or first.mapping_name ~= second.mapping_name
        or not continuous_pipeline(first) or not continuous_pipeline(second)
        or #first.modifier_objects ~= #second.modifier_objects or #first.trigger_objects ~= #second.trigger_objects then return false end
    for index, trigger in ipairs(first.trigger_objects) do
        local other = second.trigger_objects[index]
        if class_name(trigger) ~= class_name(other) or trigger.ActuationThreshold ~= other.ActuationThreshold then return false end
    end
    for index, modifier in ipairs(first.modifier_objects) do
        local other = second.modifier_objects[index]
        if address(modifier) ~= address(other) then
            local class = class_name(modifier)
            if class ~= class_name(other) then return false end
            if class == "InputModifierNegate" then
                if modifier.bX ~= other.bX or modifier.bY ~= other.bY or modifier.bZ ~= other.bZ then return false end
            elseif class == "InputModifierSwizzleAxis" then
                if modifier.Order ~= other.Order then return false end
            elseif class == "InputModifierScalar" then
                local a,b = modifier.Scalar,other.Scalar
                if a.X ~= b.X or a.Y ~= b.Y or a.Z ~= b.Z then return false end
            elseif class == "InputModifierDeadZone" then
                if modifier.LowerThreshold ~= other.LowerThreshold or modifier.UpperThreshold ~= other.UpperThreshold
                    or modifier.Type ~= other.Type then return false end
            else return false end
        end
    end
    return true
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
    local old = (self.originals or {})[current.location]
    if old and (old.native_remapped or key == "None") and same_pipeline(old, current) then return old.key end
    return key
end

local function device_of(key)
    if key == "None" then return nil end
    return (key:match("^Gamepad_") or key:match("^PBZ_Touchpad_")) and "gamepad" or "keyboard"
end

local function copy_settings(value)
    if type(value) ~= "table" then return value end
    local result = {}
    for key, item in pairs(value) do result[key] = copy_settings(item) end
    return result
end

local function same_settings(first, second)
    if type(first) ~= "table" or type(second) ~= "table" then return first == second end
    for key, value in pairs(first) do if not same_settings(value, second[key]) then return false end end
    for key in pairs(second) do if first[key] == nil then return false end end
    return true
end

local function array_matches(array, previous, limits, holds)
    local count, limit, matches = 0, 0, true
    if array then
        array:ForEach(function(_, entry)
            count = count + 1
            if not matches then return end
            local object = entry:get()
            if address(object) ~= address(previous[count]) then matches = false; return end
            if limits then
                limit = limit + 1
                if object.ActuationThreshold ~= limits[limit] then matches = false; return end
                if holds[count] then
                    limit = limit + 1
                    if object.HoldTimeThreshold ~= limits[limit] then matches = false end
                end
            end
        end)
    end
    return matches and count == #previous
end

function NativeInput:unchanged()
    local previous = self.observed
    if not previous then return false end
    local unchanged, count, actions = true, 0, {}
    local function matches(mapping, before, context)
        if not before or before.context ~= context or before.location ~= mapping:GetStructAddress()
            or before.mapping_name ~= mapping_name(mapping)
            or before.key ~= mapping.Key.KeyName:ToString() or address(mapping.Action) ~= address(before.object)
            or not array_matches(mapping.Modifiers, before.modifier_objects)
            or not array_matches(mapping.Triggers, before.trigger_objects, before.trigger_limits, before.trigger_holds) then return false end
        local id = address(before.object)
        if not actions[id] then
            if not array_matches(mapping.Action.Triggers, before.action_triggers) then return false end
            actions[id] = true
        end
        return true
    end
    local mappings = self:mappings()
    mappings:ForEach(function(_, entry)
        count = count + 1
        if unchanged and not matches(entry:get(), previous.rows[count]) then unchanged = false end
    end)
    if not unchanged or count ~= #previous.rows then return false end
    local context_count, template_count = 0, 0
    local contexts = self.backend.controller.PlayerInput:GetPropertyValue("AppliedInputContextData")
    contexts:ForEach(function(key)
        context_count = context_count + 1
        if not unchanged then return end
        local context = key:get()
        local id = context:GetAddress()
        if previous.contexts[context_count] ~= id then unchanged = false; return end
        local defaults = context.DefaultKeyMappings.Mappings
        defaults:ForEach(function(_, entry)
            template_count = template_count + 1
            if unchanged and not matches(entry:get(), previous.templates[template_count], id) then unchanged = false end
        end)
    end)
    return unchanged and context_count == #previous.contexts and template_count == #previous.templates
end

function NativeInput:inspect()
    local frame = {rows={}, templates={}, contexts={}}
    local action_triggers = {}
    local previous, unchanged = self.observed, self.observed ~= nil
    local function inspect(mapping, index, destination, old, context)
        local current = row(mapping)
        current.key, current.context = mapping.Key.KeyName:ToString(), context
        local action = address(current.object)
        if not action_triggers[action] then action_triggers[action] = values(current.object.Triggers) end
        current.action_triggers = action_triggers[action]
        if #current.trigger_objects > 0 then
            current.trigger_limits, current.trigger_holds = {}, {}
            for index, trigger in ipairs(current.trigger_objects) do
                current.trigger_limits[#current.trigger_limits+1] = trigger.ActuationThreshold
                if class_name(trigger) == "InputTriggerHold" then
                    current.trigger_holds[index] = true
                    current.trigger_limits[#current.trigger_limits+1] = trigger.HoldTimeThreshold
                end
            end
        end
        local slot = #destination + 1
        local before = old and old[slot]
        if not before or before.location ~= current.location or before.key ~= current.key
            or before.context ~= context or not same_pipeline(current, before)
            or not same_objects(current.action_triggers, before.action_triggers)
            or not same_settings(current.trigger_limits, before.trigger_limits) then unchanged = false end
        destination[slot] = {mapping=mapping, index=index, current=current}
    end
    local mappings = self:mappings()
    mappings:ForEach(function(index, entry) inspect(entry:get(), index, frame.rows, previous and previous.rows) end)
    local contexts = self.backend.controller.PlayerInput:GetPropertyValue("AppliedInputContextData")
    contexts:ForEach(function(key)
        local context = key:get()
        local id = context:GetAddress()
        frame.contexts[#frame.contexts+1] = id
        local defaults = context.DefaultKeyMappings.Mappings
        defaults:ForEach(function(index, entry)
            inspect(entry:get(), "template_" .. tostring(id) .. "_" .. index, frame.templates, previous and previous.templates, id)
        end)
    end)
    if previous and (#previous.rows ~= #frame.rows or #previous.templates ~= #frame.templates
        or not same_settings(previous.contexts, frame.contexts)) then unchanged = false end
    return frame, unchanged
end

function NativeInput:observe(frame, settings)
    -- Keep copied metadata, never native array/struct wrappers, across frames.
    local observed = {rows={}, templates={}, contexts=frame.contexts}
    for _, entry in ipairs(frame.rows) do
        entry.current.key = entry.mapping.Key.KeyName:ToString()
        observed.rows[#observed.rows+1] = entry.current
    end
    for _, entry in ipairs(frame.templates) do observed.templates[#observed.templates+1] = entry.current end
    self.observed, self.prepared_config, self.prepared_config_source = observed, copy_settings(settings), self.backend.config
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
        -- Unified ownership already masks scheduler rows independently of consumption.
        if self.backend.config.unified_native then return end
    end
    if not next(self.masked) and not next(self.consumed) and not next(self.maintained) and #(self.backend.config.suppressions or {}) == 0
        and #(self.backend.config.native_bindings or {}) == 0 and not next(self.remapped or {}) then return end
    local masked, remapped, positions, originals = {}, {}, {}, {}
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
                if override.device == device and owns_mapping(override, current, key) then
                    for _, action in ipairs(override.actions or {}) do
                        if action:gsub("%.[^./]+$", "") == path(mapping.Action) then
                            local identity = action .. ":" .. device
                            positions[identity] = (positions[identity] or 0) + 1
                            replacement = #override.keys > 0 and override.keys[(positions[identity]-1) % #override.keys + 1] or "None"
                            current.key, current.native_remapped = original, true; remapped[index] = current
                            originals[current.location] = current
                        end
                    end
                end
            end
        end
        local mask = key ~= "None" and (self.maintained[index] or not unified and (self.consumed[key] or self.backend:baseline_suppressed(mapping.Action, key, current.mapping_name)))
        if mask then
            current.key = original
            masked[index] = current
            originals[current.location] = current
            if mapping.Key.KeyName:ToString() ~= "None" then mapping.Key.KeyName = FName("None") end
        elseif mapping.Key.KeyName:ToString() ~= replacement then
            mapping.Key.KeyName = FName(replacement)
        end
    end)
    self.masked = masked
    self.remapped = remapped
    self.originals = originals
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
    self.originals, self.observed, self.prepared_config, self.prepared_config_source = nil, nil, nil, nil
end

function NativeInput:prepare()
    local backend = self.backend
    local settings = {enabled=backend.config.enabled, unified=backend.config.unified_native,
        live=backend.live_validation == true, actions=backend.config.native_actions,
        overrides=backend.config.native_bindings, suppressions=backend.config.suppressions, targets={}}
    for name, target in pairs(backend.config.targets or {}) do
        if target.kind == "mapping" then settings.targets[name] = target end
    end
    local prepared = self.prepared_config_source == backend.config and same_settings(settings, self.prepared_config)
    if backend.config.unified_native and prepared and self.expanded and self:unchanged() then return end
    local expanded, sources, maintained = {}, {}, {}
    local frame, unchanged = self:inspect()
    if unchanged and prepared and self.expanded then
        if not backend.config.unified_native then self:apply_masks(); self:observe(frame, settings) end
        return
    end
    local candidates = {}; for _, action in ipairs(backend.config.native_actions or {}) do candidates[action] = true end
    local function requested_pipeline(actionPath, key)
        for _, target in pairs(backend.config.targets or {}) do
            if target.catalog_pipeline and (not target.device or target.device == device_of(key)) then
                for _, action in ipairs(target.actions) do
                    if action:gsub("%.[^./]+$", "") == actionPath then return true end
                end
            end
        end
        return false
    end
    local coverage, active_actions = {}, {}
    self.maintained = {}
    for name, target in pairs(backend.config.targets or {}) do
        if target.kind == "mapping" then expanded[name] = {} end
    end
    local remembered, maintained_sources = {}, {}
    local function remember(mapping, suffix, key)
        if remembered[suffix] then return remembered[suffix], sources[remembered[suffix]] end
        local current = row(mapping)
        local continuous = movement[path(current.object)] and continuous_pipeline(current)
        if continuous then
            for name, source in pairs(sources) do
                if source.key == key and same_movement_pipeline(current, source) then
                    remembered[suffix] = name
                    return name, source
                end
            end
        end
        local name
        for previousName, previous in pairs(self.expanded or {}) do
            if not sources[previousName] and previous.key == key and (same_pipeline(current, previous)
                or continuous and same_movement_pipeline(current, previous)) then name = previousName; break end
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
    for _, entry in ipairs(frame.rows) do
        local index, mapping, current = entry.index, entry.mapping, entry.current
        local key = self:original_key(index, mapping, current)
        local actionPath = path(mapping.Action)
        active_actions[actionPath] = true
        local candidate = backend.config.unified_native and (candidates[actionPath] or requested_pipeline(actionPath, key)) or not backend.config.unified_native and movement[actionPath] and continuous_pipeline(current)
        local supported, reason = schedulable(current, key)
        local suppressed = backend:baseline_suppressed(mapping.Action, key, current.mapping_name)
        if candidate and key ~= "None" and supported and (backend.config.unified_native or not suppressed) then
            local name = remember(mapping, index, key)
            if not maintained_sources[name] then
                maintained[#maintained + 1] = {target=name, key=key, axis=true, action=mapping.Action,
                    suppressed=suppressed}
                maintained_sources[name] = true
            end
            self.maintained[index] = true
            coverage[#coverage+1] = {action=actionPath, key=key, owner=suppressed and "suppressed" or "scheduler"}
        elseif candidate and key == "None" and supported then
            local overridden = false
            for _, override in ipairs(backend.config.native_bindings or {}) do
                for _, action in ipairs(override.actions or {}) do
                    if action:gsub("%.[^./]+$", "") == actionPath and owns_mapping(override, current, self:source_key(mapping,index)) then overridden = true end
                end
            end
            if overridden then self.maintained[index] = true end
            coverage[#coverage+1] = {action=actionPath, key=key, owner=overridden and "scheduler" or "uncovered", reason="unbound mapping"}
        elseif candidate then
            coverage[#coverage+1] = {action=actionPath, key=key, owner="uncovered", reason=reason or "unbound mapping"}
        else
            coverage[#coverage+1] = {action=actionPath, key=key, owner="game", reason="outside registered gameplay domain"}
        end
    end
    self.coverage = coverage
    local lines = {}
    for _, entry in ipairs(coverage) do lines[#lines+1] = entry.action .. " [" .. entry.key .. "] " .. entry.owner .. (entry.reason and ": " .. entry.reason or "") end
    local signature = table.concat(lines, "\n")
    if backend.config.unified_native and self.coverage_signature ~= signature then
        self.coverage_signature = signature
        backend.log("Gameplay input ownership:\n" .. signature)
    end
    local expand
    if next(expanded) then
        local nativePaths, outputSeen = {}, {}
        for name in pairs(expanded) do nativePaths[name], outputSeen[name] = {}, {} end
        expand = function(mapping, suffix, fallback)
            local current = row(mapping)
            local key = fallback and mapping.Key.KeyName:ToString() or self:original_key(suffix, mapping, current)
            if key == "None" then return end
            local nativePath = path(mapping.Action)
            local supported = not backend.config.unified_native or (candidates[nativePath] or requested_pipeline(nativePath,key)) and schedulable(current, key)
            local pad = key:match("^Gamepad_") or key:match("^PBZ_Touchpad_")
            local device = pad and "gamepad" or "keyboard"
            local identity = nativePath .. ":" .. device
            for name, outputs in pairs(expanded) do
                local target = backend.config.targets[name]
                if owns_mapping(target, current, key) and (not target.device or target.device == device)
                    and (not fallback or not nativePaths[name][identity]) then
                    for _, targetPath in ipairs(target.actions or {}) do
                        if targetPath:gsub("%.[^./]+$", "") == nativePath then
                            -- Runtime dependencies take precedence over the context's default pipeline.
                            if not fallback then nativePaths[name][identity] = true end
                            if supported then
                                local cloneName = remember(mapping, suffix, key)
                                if not outputSeen[name][cloneName] then
                                    outputs[#outputs + 1] = cloneName
                                    outputSeen[name][cloneName] = true
                                end
                            end
                            break
                        end
                    end
                end
            end
        end
        for _, entry in ipairs(frame.rows) do expand(entry.mapping, entry.index, false) end
    end
    for _, entry in ipairs(frame.templates) do
        active_actions[path(entry.mapping.Action)] = true
        if expand then expand(entry.mapping, entry.index, true) end
    end
    local function active_target(target)
        -- Scene contexts may remove an action entirely; its bindings wait for that context to return.
        for _, action in ipairs(target.actions or {}) do
            if active_actions[action:gsub("%.[^./]+$", "")] then return true end
        end
        return false
    end
    if backend.live_validation then
        for name, outputs in pairs(expanded) do
            local target = backend.config.targets[name]
            if #outputs == 0 and not target.native_override and active_target(target) then
                error("No runtime input pipeline for " .. tostring(target.mapping or name), 0)
            end
        end
    end
    do
        local slots, captured_actions, active_mappings = {}, {}, {}
        for name, outputs in pairs(expanded) do
            local device = backend.config.targets[name].device
            if device then
                for _, output in ipairs(outputs) do captured_actions[path(sources[output].object) .. ":" .. device] = true end
            end
        end
        for _, entry in ipairs(frame.rows) do
            local actionPath = path(entry.mapping.Action)
            local key = self:source_key(entry.mapping, entry.index)
            local device = device_of(key)
            for _, override in ipairs(backend.config.native_bindings or {}) do
                local owned = owns_mapping(override, entry.current, key)
                if owned then
                    active_mappings[actionPath .. ":" .. tostring(override["function"])] = true
                end
                if device == override.device and owned then
                    local identity = actionPath .. ":" .. device .. ":" .. tostring(override["function"])
                    if self.maintained[entry.index] then captured_actions[identity] = true
                    else slots[identity] = (slots[identity] or 0) + 1 end
                end
            end
        end
        for _, override in ipairs(backend.config.native_bindings or {}) do
            for _, requested in ipairs(override.actions or {}) do
                local actionPath = requested:gsub("%.[^./]+$", "")
                local identity = actionPath .. ":" .. override.device .. ":" .. tostring(override["function"])
                local count, captured = slots[identity] or 0, captured_actions[identity] or captured_actions[actionPath .. ":" .. override.device]
                if active_mappings[actionPath .. ":" .. tostring(override["function"])] and (count > 0 or not captured) and #override.keys > count then
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
                    native_action_id = tostring(address(source.object)),
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
    self:observe(frame, settings)
end

return NativeInput
