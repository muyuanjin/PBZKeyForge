local Moves = {}

local function valid(value)
    return value ~= nil and value:IsValid()
end

local function class_path(value)
    return value:GetClass():GetFullName():gsub("^%S+ ", "")
end

local function owned_instance(component, path)
    local found, active, ambiguous
    component.ActivatableAbilities.Items:ForEach(function(_, parameter)
        local spec = parameter:get()
        if not valid(spec.Ability) or class_path(spec.Ability) ~= path then return end
        if spec.ActiveCount > 0 then active = true end
        for _, instances in ipairs({spec.NonReplicatedInstances, spec.ReplicatedInstances}) do
            instances:ForEach(function(_, instanceParameter)
                local instance = instanceParameter:get()
                if valid(instance) and instance:GetAddress() ~= spec.Ability:GetAddress()
                    and class_path(instance) == path then
                    if found and found:GetAddress() ~= instance:GetAddress() then ambiguous = true end
                    found = instance
                end
            end)
        end
    end)
    return not ambiguous and found or nil, active == true
end

function Moves.dispatch(backend, spec, phase)
    if phase ~= "started" and phase ~= "triggered" then return true end
    assert(not spec.force, "Complete moves require normal ability activation")
    local has_entry = spec.entry_point ~= nil
    if has_entry then
        assert(type(spec.ability_class) == "string" and type(spec.entry_point) == "string",
            "Complete move needs an ability class and entry point")
    end
    local component = backend:receiver({receiver = "ability_system"})
    if not valid(component) then return false end
    for _, tag in ipairs(spec.required_tags or {}) do
        if backend:invoke(component, "/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayTagCount",
            {TagName = FName(tag)}) <= 0 then return false end
    end
    for _, tag in ipairs(spec.ignored_tags or {}) do
        if backend:invoke(component, "/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayTagCount",
            {TagName = FName(tag)}) > 0 then return false end
    end
    local instance
    if has_entry then
        local active
        instance, active = owned_instance(component, spec.ability_class)
        if not instance or active or instance:GetPropertyValue("bIsActive") then return false end
        backend:object(spec.entry_point)
    end
    local input = backend:object(spec.input_action, "InputAction")
    local receiver = backend:receiver({receiver = "ability_input"})
    if not valid(receiver) then return false end
    local activated = backend:invoke(receiver,
        "/Script/SGameInputSystem.AbilityInputComponent:TryActivateExternalActionAbilityInterruptLowerByTag",
        {TagName = FName(spec.tag)}, input, spec.priority or -5)
    if not activated then return false end
    if not has_entry then return true end
    local current, running = owned_instance(component, spec.ability_class)
    if not current or not running or current:GetAddress() ~= instance:GetAddress()
        or not current:GetPropertyValue("bIsActive") then return false end
    for _, property in ipairs(spec.active_boolean_properties or {}) do
        if not current:GetPropertyValue(property) then
            backend:invoke(current, "/Script/GameplayAbilities.GameplayAbility:K2_CancelAbility")
            return false
        end
    end
    local ok, result = pcall(function()
        return backend:invoke(current, spec.entry_point, table.unpack(spec.entry_arguments or {}))
    end)
    if not ok then
        local canceled, cancellationError = pcall(function()
            backend:invoke(current, "/Script/GameplayAbilities.GameplayAbility:K2_CancelAbility")
        end)
        if not canceled then
            error(tostring(result) .. "; ability cancellation failed: " .. tostring(cancellationError), 0)
        end
        error(result, 0)
    end
    return true
end

return Moves
