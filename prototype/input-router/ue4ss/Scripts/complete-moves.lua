local Moves = {}
local ResourceCosts = require("resource-costs")

local function valid(value)
    return value ~= nil and value:IsValid()
end

local function class_path(value)
    return value:GetClass():GetFullName():gsub("^%S+ ", "")
end

local function owned_instance(component, path, tag)
    local found, active, ambiguous
    local matched_class
    component.ActivatableAbilities.Items:ForEach(function(_, parameter)
        local spec = parameter:get()
        if not valid(spec.Ability) then return end
        local candidate = class_path(spec.Ability)
        if path then
            if candidate ~= path then return end
        else
            local tags = spec.Ability:GetPropertyValue("AbilityTags")
            local matches = false
            if tags then tags.GameplayTags:ForEach(function(_, item)
                local name = item:get().TagName:ToString()
                if name == tag or tag and name:sub(1,#tag+1) == tag .. "." then matches = true end
            end) end
            if not matches then return end
        end
        if matched_class and matched_class ~= candidate then ambiguous = true end
        matched_class = candidate
        if spec.ActiveCount > 0 then active = true end
        for _, instances in ipairs({spec.NonReplicatedInstances, spec.ReplicatedInstances}) do
            instances:ForEach(function(_, instanceParameter)
                local instance = instanceParameter:get()
                if valid(instance) and instance:GetAddress() ~= spec.Ability:GetAddress()
                    and class_path(instance) == candidate then
                    if found and found:GetAddress() ~= instance:GetAddress() then ambiguous = true end
                    found = instance
                end
            end)
        end
    end)
    return not ambiguous and found or nil, active == true
end

local function eligible(backend, component, spec, check_ignored)
    for _, tags in ipairs({spec.required_tags or {}, check_ignored and spec.activation_required_tags or {}}) do
        for _, tag in ipairs(tags) do
            if backend:invoke(component, "/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayTagCount",
                {TagName = FName(tag)}) <= 0 then return false end
        end
    end
    for _, tag in ipairs(check_ignored and spec.ignored_tags or {}) do
        if backend:invoke(component, "/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayTagCount",
            {TagName = FName(tag)}) > 0 then return false end
    end
    if check_ignored and spec.activation_any_tags and #spec.activation_any_tags > 0 then
        local matched = false
        for _, tag in ipairs(spec.activation_any_tags) do
            if backend:invoke(component, "/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayTagCount",
                {TagName = FName(tag)}) > 0 then matched = true; break end
        end
        if not matched then return false end
    end
    return true
end

local function resource_qualified(instance, spec)
    for _, property in ipairs(spec.active_boolean_properties or {}) do
        if not instance:GetPropertyValue(property) then return false end
    end
    return true
end

local function cancel(backend, instance)
    backend:invoke(instance, "/Script/GameplayAbilities.GameplayAbility:K2_CancelAbility")
end

local function remember_activation(backend, pending)
    if not pending.instance then return end
    backend.complete_move_active = backend.complete_move_active or {}
    backend.complete_move_active[pending.identity] = pending
end

local function restartable(backend, component, instance, identity)
    local owned = backend.complete_move_active and backend.complete_move_active[identity]
    if not owned or owned.component:GetAddress() ~= component:GetAddress()
        or owned.instance:GetAddress() ~= instance:GetAddress()
        or backend.complete_move_pending and backend.complete_move_pending[identity] then return false end
    local phase = instance:GetPropertyValue("Action Phase")
    if not phase or phase.TagName:ToString() ~= "CharacterState.Action.Phase.Cancel" then return false end
    backend:invoke(instance, "/MagCombatAbilities/Abilities/BP_ActionAbilityBase.BP_ActionAbilityBase_C:Move Cancel Ability")
    return not instance:GetPropertyValue("bIsActive")
end

local function enter(backend, component, instance, spec)
    if not eligible(backend, component, spec) or not resource_qualified(instance, spec) then
        cancel(backend, instance)
        return false
    end
    local ok, result = pcall(function()
        local arguments = {}
        for index, argument in ipairs(spec.entry_arguments or {}) do
            arguments[index] = type(argument) == "table" and argument.fname
                and backend:argument(argument, {phase="triggered"}) or argument
        end
        return backend:invoke(instance, spec.entry_point, table.unpack(arguments))
    end)
    if not ok then
        local canceled, cancellationError = pcall(cancel, backend, instance)
        if not canceled then
            error(tostring(result) .. "; ability cancellation failed: " .. tostring(cancellationError), 0)
        end
        error(result, 0)
    end
    return true
end

local function timeline_time(backend, instance, name)
    local task = backend:invoke(instance, "/Script/LegoAbility.LegoAbility:GetPlayTimelineTask")
    if not valid(task) then return nil,"waiting" end
    local timeline = task:GetPropertyValue("Timeline")
    if not valid(timeline) then return nil,"waiting" end
    local expected = backend:invoke(instance, "/Script/LegoAbility.LegoAbility:GetTimelineByName", FName(name))
    if not valid(expected) then return nil,"waiting" end
    if timeline:GetAddress() ~= expected:GetAddress() then return nil,"other" end
    return task:GetPropertyValue("CurrentTime"),"matching"
end

local function entry_state(backend, component, instance, condition)
    if condition.timeline then
        local time, state = timeline_time(backend,instance,condition.timeline)
        if state ~= "matching" then return "waiting" end
        if condition.before_time and time >= condition.before_time then return "expired" end
        if time < (condition.minimum_time or 0) then return "waiting" end
    end
    for property, minimum in pairs(condition.property_minimum or {}) do
        if instance:GetPropertyValue(property) < minimum then return "waiting" end
    end
    if condition.object_variable then
        local variable, found = condition.object_variable
        instance:GetPropertyValue(variable.property):ForEach(function(key, value)
            if key:get():ToString() == variable.key then found = value:get() end
        end)
        if not valid(found) then return "waiting" end
        local data = backend:invoke(found, variable.getter)
        if not valid(data) or data:GetFullName():gsub("^%S+ ", "") ~= variable.object then return "waiting" end
        if variable.status_getter and backend:invoke(found,variable.status_getter) ~= variable.status then return "waiting" end
    end
    if not eligible(backend, component, condition) then return "waiting" end
    return "ready"
end

local function pending_instance(backend, pending)
    local component = backend:receiver({receiver="ability_system"})
    if not valid(component) or component:GetAddress() ~= pending.component:GetAddress() then return end
    local instance, active = owned_instance(component, pending.spec.ability_class)
    local running = not pending.preparing
    if instance and instance:GetAddress() == pending.instance:GetAddress()
        and active == running and instance:GetPropertyValue("bIsActive") == running then
        return instance, component
    end
end

local function hold_input(backend, pending)
    local action = backend:object(pending.spec.prepare_input, "InputAction")
    if not valid(backend.subsystem) or not valid(action) then return false end
    backend.complete_move_inputs = backend.complete_move_inputs or {}
    local identity = tostring(backend.subsystem:GetAddress()) .. ":" .. tostring(action:GetAddress())
    local input = backend.complete_move_inputs[identity]
    if not input then
        backend:invoke(backend.subsystem,
            "/Script/EnhancedInput.EnhancedInputSubsystemInterface:StartContinuousInputInjectionForAction",
            action, {X=1,Y=0,Z=0}, {}, {})
        input = {identity=identity, action=action, subsystem=backend.subsystem, owners=0}
        backend.complete_move_inputs[identity] = input
    end
    input.owners = input.owners + 1
    pending.input = input
    pending.prepared_action = action
    return true
end

local function release_input(backend, pending)
    local input = pending.input
    if not input then return end
    if input.owners == 1 then
        backend:invoke(input.subsystem,
            "/Script/EnhancedInput.EnhancedInputSubsystemInterface:StopContinuousInputInjectionForAction", input.action)
        backend.complete_move_inputs[input.identity] = nil
    end
    input.owners = input.owners - 1
    pending.input = nil
end

local function finish(backend, pending)
    release_input(backend, pending)
    if backend.complete_move_pending then backend.complete_move_pending[pending.identity] = nil end
end

local function queue(backend, pending)
    backend.complete_move_pending = backend.complete_move_pending or {}
    backend.complete_move_pending[pending.identity] = pending
end

local function is_holding(backend, action)
    local component = backend:invoke(backend:library("SGameInputSystem.SGameInputUtils"),
        "/Script/SGameInputSystem.SGameInputUtils:GetSGameInputComponent", backend.pawn)
    if valid(component) then
        return backend:invoke(component,"/Script/SGameInputSystem.SGameInputComponent:IsHolding",action)
    end
end

local function released_entry_state(backend, instance, pending)
    local condition = pending.spec.entry_when
    local time, state = timeline_time(backend,instance,condition.timeline)
    if state == "other" or state == "matching" and condition.completed_after_property
        and time >= instance:GetPropertyValue(condition.completed_after_property) then
        return "completed"
    end
    if state == "matching" and is_holding(backend,pending.prepared_action) == false then return "ready" end
    return "waiting"
end

local function apply_entry(backend, component, instance, pending)
    if pending.spec.release_input_before_entry then
        release_input(backend,pending)
        pending.releasing = true
        queue(backend,pending)
        return true
    end
    finish(backend,pending)
    return enter(backend,component,instance,pending.spec)
end

local function activate_by_class(backend, component, receiver, input, target)
    local ability_class
    component.ActivatableAbilities.Items:ForEach(function(_, parameter)
        local spec = parameter:get()
        if valid(spec.Ability) and class_path(spec.Ability) == target.ability_class then
            ability_class = spec.Ability:GetClass()
        end
    end)
    if not valid(ability_class) then return false end
    return backend:invoke(receiver,
        "/Script/SGameInputSystem.AbilityInputComponent:TryActivateExternalActionAbilityByClass",
        ability_class, input, target.priority or -5)
end

local function activate(backend, component, instance, pending)
    local spec = pending.spec
    local input = backend:object(spec.input_action, "InputAction")
    local receiver = backend:receiver({receiver = "ability_input"})
    if not valid(receiver) then finish(backend,pending); return false end
    local activated
    activated = ResourceCosts.with(backend,spec,pending.bypass_resources,function()
        if spec.activate_by_class then return activate_by_class(backend, component, receiver, input, spec) end
        return backend:invoke(receiver,
            "/Script/SGameInputSystem.AbilityInputComponent:TryActivateExternalActionAbilityInterruptLowerByTag",
            {TagName = FName(spec.tag)}, input, spec.priority or -5)
    end,pending.settings)
    if not activated then finish(backend,pending); return false end
    pending.preparing = nil
    if not spec.entry_point and not spec.completion_timeline then
        if instance then
            local current, running = owned_instance(component,spec.ability_class,spec.tag)
            if current and running and current:GetAddress() == instance:GetAddress() and current:GetPropertyValue("bIsActive") then
                remember_activation(backend,pending)
            end
        end
        finish(backend,pending)
        return true
    end
    local current, running = owned_instance(component, spec.ability_class)
    if not current or not running or current:GetAddress() ~= instance:GetAddress()
        or not current:GetPropertyValue("bIsActive") then finish(backend,pending); return false end
    if not resource_qualified(current, spec) then finish(backend,pending); cancel(backend,current); return false end
    remember_activation(backend,pending)
    if spec.completion_timeline then
        local _, state = timeline_time(backend,current,spec.completion_timeline)
        if state == "matching" then finish(backend,pending) else queue(backend,pending) end
        return true
    end
    if spec.entry_when then
        local state = entry_state(backend, component, current, spec.entry_when)
        if state == "expired" then
            finish(backend,pending)
            if spec.entry_when.cancel_on_expire then cancel(backend,current) end
            return true
        end
        if state == "waiting" then
            queue(backend,pending)
            return true
        end
    end
    return apply_entry(backend,component,current,pending)
end

local function dispatch_active(backend, component, target, bypass, settings)
    local classes, dispatched, seen = {}, false, {}
    for _, path in ipairs(target.active_ability_classes) do classes[path] = true end
    backend:object(target.entry_point)
    local function qualified(instance)
        if not resource_qualified(instance,target) then return false end
        local condition = target.entry_when
        if not condition then return true end
        if entry_state(backend,component,instance,condition) ~= "ready" then return false end
        if condition.property_sum_above then
            local sum = 0
            for _, path in ipairs(condition.property_sum_above.properties) do
                local value = instance:GetPropertyValue(path[1])
                for index = 2,#path do value = value[path[index]] end
                sum = sum + value
            end
            if sum <= instance:GetPropertyValue(condition.property_sum_above.threshold_property) then return false end
        end
        return true
    end
    component.ActivatableAbilities.Items:ForEach(function(_, parameter)
        local spec = parameter:get()
        if not valid(spec.Ability) or not classes[class_path(spec.Ability)] or spec.ActiveCount <= 0 then return end
        for _, instances in ipairs({spec.NonReplicatedInstances, spec.ReplicatedInstances}) do
            instances:ForEach(function(_, instanceParameter)
                local instance = instanceParameter:get()
                if valid(instance) and instance:GetAddress() ~= spec.Ability:GetAddress()
                    and classes[class_path(instance)] and instance:GetPropertyValue("bIsActive")
                    and not seen[instance:GetAddress()] and qualified(instance) then
                    seen[instance:GetAddress()] = true
                    ResourceCosts.with_active(backend,instance,bypass,function()
                        return backend:invoke(instance, target.entry_point)
                    end,settings,target.active_callback_only)
                    dispatched = true
                end
            end)
        end
    end)
    return dispatched
end

function Moves.update(backend)
    for identity, active in pairs(backend.complete_move_active or {}) do
        if not valid(active.instance) or not active.instance:GetPropertyValue("bIsActive") then
            backend.complete_move_active[identity] = nil
        end
    end
    for _, pending in pairs(backend.complete_move_pending or {}) do
        local instance, component = pending_instance(backend, pending)
        if not instance then finish(backend,pending)
        elseif pending.preparing then
            if not eligible(backend,component,pending.spec,true) then finish(backend,pending)
            else
                if is_holding(backend,pending.prepared_action) then
                    activate(backend,component,instance,pending)
                end
            end
        elseif not eligible(backend,component,pending.spec) or not resource_qualified(instance,pending.spec) then
            finish(backend,pending)
            cancel(backend,instance)
        elseif pending.spec.completion_timeline then
            local _, state = timeline_time(backend,instance,pending.spec.completion_timeline)
            if state == "matching" then finish(backend,pending) end
        elseif pending.releasing then
            local state = released_entry_state(backend,instance,pending)
            if state ~= "waiting" then
                finish(backend,pending)
                if state == "ready" then enter(backend,component,instance,pending.spec) end
            end
        else
            local state = entry_state(backend,component,instance,pending.spec.entry_when)
            if state ~= "waiting" then
                if state == "ready" then apply_entry(backend,component,instance,pending)
                else
                    finish(backend,pending)
                    if pending.spec.entry_when.cancel_on_expire then cancel(backend,instance) end
                end
            end
        end
    end
end

function Moves.reset(backend)
    backend.complete_move_active = nil
    local failures = {}
    for _, pending in pairs(backend.complete_move_pending or {}) do
        local ok, failure = pcall(function()
            finish(backend,pending)
            if not pending.preparing then
                local instance = pending_instance(backend,pending)
                if instance then
                    local completed = pending.releasing and released_entry_state(backend,instance,pending) == "completed"
                    if pending.spec.completion_timeline then
                        local _, state = timeline_time(backend,instance,pending.spec.completion_timeline)
                        completed = state == "matching"
                    end
                    if not completed then cancel(backend,instance) end
                end
            end
        end)
        if not ok then failures[#failures+1] = tostring(failure) end
    end
    if backend.complete_move_pending and not next(backend.complete_move_pending) then backend.complete_move_pending = nil end
    if #failures > 0 then error(table.concat(failures,"; "),0) end
end

function Moves.dispatch(backend, spec, phase, bypass,settings)
    if phase ~= "started" and phase ~= "triggered" then return true end
    assert(not spec.force, "Complete moves require normal ability activation")
    if spec.entry_point or spec.completion_timeline then
        assert(type(spec.ability_class) == "string", "Prepared complete move needs an ability class")
    end
    if spec.entry_point then
        assert(type(spec.ability_class) == "string" and type(spec.entry_point) == "string",
            "Complete move needs an ability class and entry point")
    end
    local component = backend:receiver({receiver = "ability_system"})
    if not valid(component) or not eligible(backend, component, spec, true) then return false end
    if spec.execution_type then
        local execution = backend:invoke(backend.pawn, "/Script/Engine.Actor:GetComponentByClass",
            backend:object("/Script/SGameExecution.ExecutionComponent"))
        if not valid(execution) then return false end
        local resolved = backend:invoke(execution, "/Script/SGameExecution.ExecutionComponent:GetResolvedExecutionType",
            FName(spec.execution_channel))
        if resolved.TagName:ToString() ~= spec.execution_type then return false end
    end
    if spec.active_ability_classes then return dispatch_active(backend,component,spec,bypass,settings) end
    local instance
    if spec.ability_class or spec.tag then
        local active
        instance, active = owned_instance(component, spec.ability_class,spec.tag)
        if not instance and (spec.entry_point or spec.completion_timeline) then return false end
        if instance then
            local identity = class_path(instance) .. ":" .. tostring(instance:GetAddress())
            if (active or instance:GetPropertyValue("bIsActive")) and not restartable(backend,component,instance,identity) then return false end
        end
        if spec.entry_point then backend:object(spec.entry_point) end
    end
    local identity = instance and class_path(instance) .. ":" .. tostring(instance:GetAddress()) or spec.tag
    if backend.complete_move_pending and backend.complete_move_pending[identity] then return true end
    local pending = {identity=identity, spec=spec, instance=instance, component=component, bypass_resources=bypass,settings=settings}
    if spec.prepare_input then
        assert(instance, "Prepared complete moves require a granted ability instance")
        pending.preparing = true
        if not hold_input(backend,pending) then return false end
        queue(backend,pending)
        return true
    end
    return activate(backend,component,instance,pending)
end

return Moves
