local Router = {}
Router.__index = Router

local kinds = { down=true, press=true, release=true, tap=true, hold=true,
    pulse=true, multi_tap=true, sequence=true, axis=true, toggle=true }
local inactive = {active=false, pulse=false, reserve=false, value=0}

local function vector(value)
    if type(value) == "table" then
        return { x=value.x or value[1] or 0, y=value.y or value[2] or 0,
            z=value.z or value[3] or 0 }
    end
    return { x=value == nil and 1 or value, y=0, z=0 }
end

local function magnitude(v)
    return math.max(math.abs(v.x), math.abs(v.y), math.abs(v.z))
end

local function down(snapshot, key, threshold)
    local v = snapshot[key]
    return v == true or (type(v) == "number" and math.abs(v) >= threshold)
end

local function chord(snapshot, keys, threshold)
    for _, key in ipairs(keys or {}) do
        if not down(snapshot, key, threshold) then return false end
    end
    return true
end

local function held_binding(binding, snapshot, threshold)
    if binding.evaluate and #(binding.keys or {}) == 0 and not binding.axis then
        for _, key in ipairs(binding.watch or {}) do if down(snapshot, key, threshold) then return true end end
        return false
    end
    local axis = not binding.axis or down(snapshot, binding.axis, threshold)
    if binding.axis and binding.axis_direction and binding.axis_direction ~= "both" then
        local value = snapshot[binding.axis] or 0
        axis = type(value) == "number" and (binding.axis_direction == "positive" and value >= threshold or binding.axis_direction == "negative" and value <= -threshold)
    end
    return chord(snapshot, binding.keys, threshold) and axis
end

local function consumes(binding)
    if binding.consume == true then
        local keys = {}
        for _, key in ipairs(binding.keys or {}) do keys[#keys + 1] = key end
        for _, key in ipairs(binding.watch or {}) do keys[#keys + 1] = key end
        if binding.axis then keys[#keys + 1] = binding.axis end
        for _, step in ipairs(binding.steps or {}) do
            if type(step) == "string" then keys[#keys + 1] = step
            else for _, key in ipairs(step) do keys[#keys + 1] = key end end
        end
        return keys
    end
    return type(binding.consume) == "table" and binding.consume or {}
end

local function action_identity(backend, target)
    if backend.action_identity then return backend:action_identity(target) end
    return target.path or target.object
end

local function pipeline_identity(backend, target)
    if backend.pipeline_identity then return backend:pipeline_identity(target) end
    return ""
end

local function settings_identity(settings)
    local restrictions = {}; for _,kind in ipairs(settings.restriction_bypasses or {}) do restrictions[#restrictions+1] = kind end
    table.sort(restrictions)
    return table.concat({tostring(settings.bypass_resources == true),tostring(settings.damage_multiplier or 1),
        tostring(settings.startup_speed or 1),tostring(settings.recovery_speed or 1),
        tostring(settings.super_armor == true),tostring(settings.invincible == true),table.concat(restrictions,",")},"|")
end

function Router.new(config, backend)
    assert(type(config.targets) == "table", "targets must be a table")
    assert(type(config.bindings) == "table", "bindings must be an array")
    local self = setmetatable({ config=config, backend=backend, rules={},
        samples={}, previous={}, lastTime=nil, consumers={} }, Router)
    local ids, keys = {}, {}
    local function add(key)
        assert(type(key) == "string" and key ~= "", "invalid input key")
        keys[key] = true
    end
    for index, binding in ipairs(config.bindings) do
        if binding.enabled ~= false then
        assert(binding.id and not ids[binding.id], "binding id missing or duplicated")
        ids[binding.id] = true
        local kind = binding.trigger or "down"
        assert(kinds[kind] or type(binding.evaluate) == "function", "unknown trigger: " .. kind)
        assert(binding.axis or #(binding.keys or {}) > 0 or kind == "sequence"
            or binding.evaluate, "binding needs an input")
        if kind == "sequence" then assert(#(binding.steps or {}) > 0, "empty sequence") end
        for _, key in ipairs(binding.keys or {}) do add(key) end
        local dimension = 1
        if binding.axis then
            add(binding.axis)
            if backend.key_dimension then dimension = backend:key_dimension(binding.axis) end
            if dimension > 1 then
                for component = 1, dimension do add(binding.axis .. "@" .. component) end
            end
        end
        for _, step in ipairs(binding.steps or {}) do
            if type(step) == "string" then add(step)
            else for _, key in ipairs(step) do add(key) end end
        end
        for _, key in ipairs(binding.watch or {}) do add(key) end
        for _, key in ipairs(consumes(binding)) do add(key) end
        assert(#(binding.outputs or {}) > 0, "binding needs outputs")
        local moveIds, sourceKeys, pulseOutputs = {}, {}, false
        local settingsId = settings_identity(binding)
        for _, key in ipairs(binding.keys or {}) do sourceKeys[#sourceKeys+1] = key end
        if binding.axis then sourceKeys[#sourceKeys+1] = binding.axis end
        table.sort(sourceKeys)
        for _, output in ipairs(binding.outputs) do
            if type(output) == "table" and output.pulse then pulseOutputs = true end
            local name = type(output) == "string" and output or output.target
            assert(config.targets[name], "unknown target: " .. tostring(name))
            local target = config.targets[name]
            if target.kind == "move" or target.kind == "ability" then
                local identity = target.kind == "move" and (target.move_id or target)
                    or table.concat({"ability",target.tag,tostring(target.force),tostring(target.priority or -5),target.input_action or ""},"|")
                moveIds[name] = tostring(identity) .. "|" .. settingsId
            end
        end
        self.rules[#self.rules+1] = { binding=binding, index=index, dimension=dimension, state={},
            consume_keys=consumes(binding), input_signature=table.concat(sourceKeys, ",") .. ":", move_ids=moveIds,
            pulse_outputs=pulseOutputs, repeat_ability=binding.default_trigger == true or kind ~= "press" }
        if #self.rules[#self.rules].consume_keys > 0 then self.consumers[#self.consumers+1] = self.rules[#self.rules] end
        end
    end
    table.sort(self.rules, function(a,b)
        local ap, bp = a.binding.priority or 0, b.binding.priority or 0
        if ap ~= bp then return ap > bp end
        local an, bn = #(a.binding.keys or {}), #(b.binding.keys or {})
        return an ~= bn and an > bn or (an == bn and a.index < b.index)
    end)
    for key in pairs(keys) do self.samples[#self.samples+1] = key end
    table.sort(self.samples)
    return self
end

function Router:keys() return self.samples end

local function evaluate(binding, st, now, snapshot, context, dimension)
    local kind, threshold = binding.trigger or "down", binding.threshold or 0.5
    local held = held_binding(binding, snapshot, threshold)
    if not held and not st.held and not st.active and not st.resume_wait and not binding.evaluate
        and (kind == "down" or kind == "press" or kind == "hold" or kind == "axis") then
        st.resume_outputs = nil
        return inactive
    end
    if st.resume_wait then
        if not held then st.resume_wait, st.held, st.active = nil, false, false end
        return {active=false, pulse=false, reserve=false, value=0}
    end
    if not held then st.resume_outputs = nil end
    if binding.evaluate then return binding.evaluate(now, snapshot, context, st) end
    local pressed, released = held and not st.held, not held and st.held
    st.held = held
    if pressed then st.since = now end
    local active, pulse, value = false, false, 1
    if kind == "down" then active, pulse = held, pressed
    elseif kind == "press" then active, pulse = pressed, pressed
    elseif kind == "release" then active, pulse = released, released
    elseif kind == "tap" then
        pulse = released and now - st.since <= (binding.seconds or 0.25)
        active = pulse
    elseif kind == "hold" then
        active = held and now - st.since >= (binding.seconds or 0.35)
        pulse = active and not st.active
    elseif kind == "pulse" then
        if held and (pressed or now >= (st.nextPulse or now)) then
            pulse = true
            st.nextPulse = now + (binding.interval or 0.15)
        end
        active = pulse
    elseif kind == "multi_tap" then
        if pressed then
            st.count = st.lastTap and now - st.lastTap <= (binding.interval or 0.3)
                and (st.count or 0) + 1 or 1
            st.lastTap = now
            if st.count >= (binding.count or 2) then pulse=true; st.count=0 end
        end
        active = pulse
    elseif kind == "toggle" then
        if pressed then st.toggled = not st.toggled end
        active, pulse = st.toggled or false, pressed and st.toggled or false
    elseif kind == "axis" then
        active, pulse = held, pressed
        if dimension > 1 then
            value = {x=snapshot[binding.axis .. "@1"] or 0, y=snapshot[binding.axis .. "@2"] or 0,
                z=dimension > 2 and snapshot[binding.axis .. "@3"] or 0}
        else value = type(snapshot[binding.axis]) == "number" and snapshot[binding.axis] or 0 end
    elseif kind == "sequence" then
        if st.lastStep and now - st.lastStep > (binding.interval or 0.5) then st.step=0 end
        local progress = st.step or 0
        local expected = binding.steps[progress+1]
        local stepKeys = type(expected) == "string" and {expected} or expected
        local stepDown = chord(snapshot, stepKeys, threshold)
        local stepWasDown = chord(st.previousSnapshot or {}, stepKeys, threshold)
        if stepDown and not stepWasDown then
            st.step, st.lastStep = progress+1, now
            if st.step == #binding.steps then pulse=true; st.step=0 end
        end
        st.previousSnapshot = {}
        for key,v in pairs(snapshot) do st.previousSnapshot[key]=v end
        active = pulse
        held = (st.step or 0) > 0 or pulse
    end
    st.active = active
    return { active=active, move_active=kind == "press" and held, pulse=pulse, value=value,
        reserve=held or released and (kind == "tap" or kind == "release") }
end

function Router:resume(snapshot, context)
    for _, rule in ipairs(self.rules) do
        local b, st = rule.binding, rule.state
        local threshold, kind = b.threshold or .5, b.trigger or "down"
        local held = held_binding(b, snapshot, threshold)
        if kind == "sequence" then
            st.previousSnapshot = {}
            for key, value in pairs(snapshot) do st.previousSnapshot[key] = value end
        elseif held and (kind == "down" or kind == "axis") and not b.evaluate then
            st.held, st.resume_outputs = true, {}
            for _, output in ipairs(b.outputs) do
                local name = type(output) == "string" and output or output.target
                local target = self.config.targets[name]
                if target.kind == "move" or target.kind == "ability"
                    or target.kind == "action" and (target.resume_continuous ~= true
                        or type(output) == "table" and output.pulse) then st.resume_outputs[name] = true end
            end
        elseif held then st.resume_wait = true end
    end
end

function Router:reconfigure(config, snapshot, context)
    local replacement = Router.new(config, self.backend)
    snapshot = snapshot or self.backend:snapshot(replacement:keys())
    replacement:resume(snapshot, context)
    local old = {}; for _, rule in ipairs(self.rules) do old[rule.binding.id] = rule end
    for _, rule in ipairs(replacement.rules) do
        local previous = old[rule.binding.id]
        local same = previous and previous.binding.trigger == rule.binding.trigger and #previous.binding.outputs == #rule.binding.outputs
        if same then
            for index, output in ipairs(rule.binding.outputs) do
                local name = type(output) == "string" and output or output.target
                local prior = previous.binding.outputs[index]
                local priorName = type(prior) == "string" and prior or prior.target
                if self.config.targets[priorName] ~= config.targets[name] then same = false; break end
            end
        end
        if same then rule.state = previous.state end
    end
    replacement.previous, replacement.lastTime = self.previous, self.lastTime
    replacement.previous_pulses = self.previous_pulses
    replacement.consumed = self.consumed
    local retained = {}; for _, target in pairs(config.targets) do retained[target] = true end
    for native, level in pairs(replacement.previous) do
        for pipeline, contribution in pairs(level.pipelines) do
            if not retained[contribution.target] then
                local ok = self.backend:dispatch(contribution.target, "canceled", {x=0,y=0,z=0}, contribution.binding)
                if ok == false then error(self.backend.dispatch_error or "Retired pipeline cancellation failed", 0) end
                level.pipelines[pipeline] = nil
            end
        end
        if not next(level.pipelines) then replacement.previous[native] = nil end
    end
    return replacement
end

function Router:step(now, snapshot, context)
    assert(type(now) == "number", "clock must be numeric")
    if self.lastTime and now < self.lastTime then self:reset() end
    self.lastTime = now
    local levels, reserved, canceled, owners, moves, moveOrder = {}, {}, {}, {}, {}, {}
    local actionInputs, moveInputs, nextPulses = {}, {}, {}
    local unavailable, availableOwners = {}, {}
    if self.backend.target_failed then
        for _, rule in ipairs(self.consumers) do
            local b, available = rule.binding, false
            if not self.backend:target_failed(b,b.id) then
                for _, output in ipairs(b.outputs) do
                    local name = type(output) == "string" and output or output.target
                    if not self.backend:target_failed(self.config.targets[name],b.id) then available=true;break end
                end
            end
            unavailable[b.id] = not available
            local owner = b.input_group or b.id
            availableOwners[owner] = availableOwners[owner] or available
        end
    end
    for key, owner in pairs(self.consumed or {}) do
        if availableOwners[owner] ~= false then
            reserved[key], owners[key] = true, owner
        elseif self.backend.release_reserved_input then
            self.backend:release_reserved_input(key)
        end
    end
    local function occupied(key, binding) return reserved[key] and owners[key] ~= (binding.input_group or binding.id) end
    for rank, rule in ipairs(self.rules) do
        local b, st = rule.binding, rule.state
        local blocked = false
        for _, key in ipairs(b.keys or {}) do if occupied(key, b) then blocked=true end end
        for _, key in ipairs(b.watch or {}) do if occupied(key, b) then blocked=true end end
        if b.axis and occupied(b.axis, b) then blocked=true end
        for _, sequenceStep in ipairs(b.steps or {}) do
            if type(sequenceStep) == "string" then
                if occupied(sequenceStep, b) then blocked=true end
            else
                for _, key in ipairs(sequenceStep) do if occupied(key, b) then blocked=true end end
            end
        end
        local function evaluate_rule()
            local allowed = not b.when or self.backend:allowed(b.when, context)
            if blocked and b.evaluate then
                st = {resume_wait = held_binding(b, snapshot, b.threshold or .5) or nil}
                rule.state = st
                return inactive, allowed
            end
            return evaluate(b, st, now, snapshot, context, rule.dimension), allowed
        end
        local result, allowed
        if self.backend.evaluate_binding then result, allowed = self.backend:evaluate_binding(b,evaluate_rule)
        else result, allowed = evaluate_rule() end
        if not result then
            rule.state, result, allowed = {}, inactive, false
        end
        local outputPulse = result.pulse
        if rule.pulse_outputs then
            -- Shared native actions need a release frame across all pulse sources.
            local releasing = false
            for _, output in ipairs(b.outputs) do
                if type(output) == "table" and output.pulse then
                    local target = self.config.targets[output.target]
                    if target.kind == "action" and self.previous_pulses
                        and self.previous_pulses[action_identity(self.backend, target)] then releasing = true end
                end
            end
            local requested = result.active or st.pending_output_pulse
            outputPulse = not blocked and allowed and requested and not releasing
            local kind = b.trigger or "down"
            st.pending_output_pulse = not blocked and allowed and requested and releasing
                and (kind == "press" or kind == "release" or kind == "tap" or kind == "pulse"
                    or kind == "multi_tap" or kind == "sequence") or nil
        end
        if blocked or not allowed then
            for _, output in ipairs(b.outputs) do
                local name = type(output) == "string" and output or output.target
                local target = self.config.targets[name]
                if target.kind == "action" then
                    local native = action_identity(self.backend, target)
                    canceled[native] = canceled[native] or {}
                    canceled[native][pipeline_identity(self.backend, target)] = true
                    if (not target.resume_continuous or type(output) == "table" and output.pulse)
                        and (b.trigger == "down" or b.trigger == "axis" or not b.trigger)
                        and held_binding(b, snapshot, b.threshold or .5) then
                        st.resume_outputs = st.resume_outputs or {}
                        st.resume_outputs[name] = true
                    end
                end
            end
        end
        if blocked and b.trigger == "sequence" then st.step, st.lastStep = 0, nil end
        if not blocked and allowed then
            if result.reserve and not unavailable[b.id] then
                for _, key in ipairs(rule.consume_keys) do reserved[key], owners[key]=true, b.input_group or b.id end
            end
            if result.active or result.move_active or outputPulse then
                for _, output in ipairs(b.outputs) do
                    local name = type(output) == "string" and output or output.target
                    local target = self.config.targets[name]
                    if not (self.backend.target_failed and self.backend:target_failed(target,b.id)) then
                        if target.native_hold_inputs and (result.active or result.move_active)
                            and not (st.resume_outputs and st.resume_outputs[name]) then
                            moveInputs[#moveInputs+1] = {target=target,binding=b.id}
                        end
                        local outputValue = type(output) == "table" and output.value or nil
                        local pulsed = type(output) == "table" and output.pulse
                        local outputActive = pulsed and outputPulse or not pulsed and result.active
                        local v = type(result.value) == "table" and not outputValue and vector(result.value) or vector(outputValue)
                        local scale = type(result.value) == "number" and result.value
                            or type(result.value) == "table" and outputValue and magnitude(vector(result.value)) or 1
                        v.x, v.y, v.z = v.x*scale, v.y*scale, v.z*scale
                        if outputActive and target.kind == "action" and target.native_continuous and context and context.move_ignored then
                            local native = action_identity(self.backend, target)
                            canceled[native] = canceled[native] or {}
                            canceled[native][pipeline_identity(self.backend, target)] = true
                        elseif outputActive and target.kind == "action" and not (st.resume_outputs and st.resume_outputs[name])
                            and not (target.native_continuous and context and context.move_ignored) then
                            local signature
                            if target.source_relation and (b.trigger == "down" or b.trigger == "axis" or not b.trigger) then
                                signature = rule.input_signature .. tostring(v.x) .. ":" .. tostring(v.y) .. ":" .. tostring(v.z)
                            end
                            actionInputs[#actionInputs+1] = {target=target, binding=b.id, settings=b,
                                value=v, rank=rank, signature=signature, bypass_resources=b.bypass_resources}
                            if pulsed and magnitude(v) > 0 then nextPulses[action_identity(self.backend, target)] = true end
                        elseif (target.kind == "move" or target.kind == "ability"
                            and (result.pulse or rule.repeat_ability and (result.active or result.move_active)))
                            and not (st.resume_outputs and st.resume_outputs[name]) then
                            local identity = rule.move_ids[name]
                            local request = moves[identity]
                            if not request then
                                request = {target=target, value=v, binding=b.id, settings=b,rank=rank,bypass_resources=b.bypass_resources}
                                moves[identity] = request
                                moveOrder[#moveOrder+1] = request
                            else
                                request.alternatives = request.alternatives or {}
                                request.alternatives[#request.alternatives+1] = {target=target,value=v,binding=b.id,settings=b,
                                    rank=rank,bypass_resources=b.bypass_resources}
                            end
                        elseif target.kind ~= "action" and result.pulse then
                            self.backend:dispatch(target, "triggered", v, b.id, b.bypass_resources, b)
                        end
                    end
                end
            end
        end
    end
    if self.backend.prepare_move_inputs then self.backend:prepare_move_inputs(moveInputs) end
    for _, input in ipairs(actionInputs) do
        local target, b, v = input.target, input.settings, input.value
        local native = action_identity(self.backend, target)
        local level = levels[native]
        if not level then
            level={target=target, binding=b.id, pipelines={}}
            levels[native]=level
        end
        local pipeline = pipeline_identity(self.backend, target)
        local contribution = level.pipelines[pipeline]
        if not contribution then
            contribution={target=target, binding=b.id, value={x=0,y=0,z=0}, inputs={}, rank=input.rank,
                settings=b, bypass_resources=b.bypass_resources}
            level.pipelines[pipeline]=contribution
        end
        local signature = input.signature
        if not signature or not contribution.inputs[signature] then
            contribution.value.x = contribution.value.x + v.x
            contribution.value.y = contribution.value.y + v.y
            contribution.value.z = contribution.value.z + v.z
            if signature then contribution.inputs[signature] = true end
        end
    end
    self.consumed = {}
    for key, owner in pairs(owners) do if down(snapshot, key, .001) then self.consumed[key] = owner end end
    if self.backend.reserve_inputs then self.backend:reserve_inputs(reserved, context and context.raw_input or snapshot) end
    local previousLevels = self.previous
    self.previous = {}
    for native, previous in pairs(previousLevels) do self.previous[native]=previous end
    for native, level in pairs(levels) do self.previous[native]=level end
    local actionRequests = {}
    for native, level in pairs(levels) do
        for pipeline, contribution in pairs(level.pipelines) do
            local target, v = contribution.target, contribution.value
            if target.value_type == "bool" then
                v.x, v.y, v.z = magnitude(v) > 0 and 1 or 0, 0, 0
            elseif target.clamp then
                for _, axis in ipairs({"x","y","z"}) do
                    v[axis]=math.max(-target.clamp, math.min(target.clamp, v[axis]))
                end
            end
            if magnitude(v) > 0 then
                actionRequests[#actionRequests+1] = {target=target,value=v,binding=contribution.binding,
                    phase=previousLevels[native] and "triggered" or "started",bypass_resources=contribution.bypass_resources,
                    settings=contribution.settings,rank=contribution.rank}
            else
                level.pipelines[pipeline] = nil
            end
        end
        if not next(level.pipelines) then levels[native]=nil end
    end
    table.sort(actionRequests,function(a,b) return a.rank < b.rank end)
    if self.backend.prepare_input_settings then
        local requests={}
        for _,request in ipairs(moveOrder) do requests[#requests+1]=request end
        for _,request in ipairs(actionRequests) do requests[#requests+1]=request end
        table.sort(requests,function(a,b) return a.rank < b.rank end)
        self.backend:prepare_input_settings(requests)
    end
    for _, request in ipairs(moveOrder) do
        local ok, result = self.backend:dispatch(request.target, "triggered", request.value, request.binding, request.bypass_resources, request.settings)
        local failed = ok == false or result == false or self.backend.target_failed and self.backend:target_failed(request.target,request.binding)
        if failed then
            for _, alternative in ipairs(request.alternatives or {}) do
                ok, result = self.backend:dispatch(alternative.target,"triggered",alternative.value,alternative.binding,
                    alternative.bypass_resources,alternative.settings)
                if ok ~= false and result ~= false and not (self.backend.target_failed and self.backend:target_failed(alternative.target,alternative.binding)) then break end
            end
        end
    end
    for _,request in ipairs(actionRequests) do
        local ok = self.backend:dispatch(request.target,request.phase,request.value,request.binding,request.bypass_resources,request.settings)
        if ok == false then error(self.backend.dispatch_error or "Action dispatch failed", 0) end
    end
    for native, previous in pairs(previousLevels) do
        for pipeline, contribution in pairs(previous.pipelines) do
            if not levels[native] or not levels[native].pipelines[pipeline] then
                local phase = canceled[native] and canceled[native][pipeline] and "canceled" or "completed"
                local ok = self.backend:dispatch(contribution.target, phase, {x=0,y=0,z=0}, contribution.binding)
                if ok == false then error(self.backend.dispatch_error or "Action release failed", 0) end
            end
        end
    end
    self.previous = levels
    self.previous_pulses = nextPulses
end

function Router:reset(cancel)
    local failed = {}
    if self.backend.reset_move_inputs then self.backend:reset_move_inputs() end
    for native, previous in pairs(self.previous) do
        for _, contribution in pairs(previous.pipelines) do
            local ok, result = pcall(self.backend.dispatch, self.backend, contribution.target,
                cancel and "canceled" or "completed", {x=0,y=0,z=0}, contribution.binding)
            if not ok or result == false then failed[native]=previous end
        end
    end
    self.previous, self.lastTime = failed, nil
    self.consumed = nil
    self.previous_pulses = nil
    for _, rule in ipairs(self.rules) do rule.state={} end
    if next(failed) then error(self.backend.dispatch_error or "Action release is incomplete", 0) end
end

return Router
