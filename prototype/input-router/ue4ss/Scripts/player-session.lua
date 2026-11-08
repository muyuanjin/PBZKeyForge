local PlayerSession = {}
PlayerSession.__index = PlayerSession

local function valid(value)
    if value == nil then return false end
    local ok, result = pcall(function() return value:IsValid() end)
    return ok and result == true
end

local function address(value)
    if not valid(value) then return nil end
    local ok, result = pcall(function() return value:GetAddress() end)
    return ok and tostring(result) or nil
end

function PlayerSession.new(backend, controller, pawn, generation)
    assert(valid(controller), "PlayerSession requires a valid controller")
    assert(valid(pawn), "PlayerSession requires a valid pawn")
    return setmetatable({
        backend = backend,
        controller = controller,
        controller_address = address(controller),
        pawn = pawn,
        pawn_address = address(pawn),
        generation = generation
    }, PlayerSession)
end

function PlayerSession:invalidate()
    self.invalidated = true
    self.controller, self.controller_address = nil, nil
    self.pawn, self.pawn_address = nil, nil
    self.subsystem, self.subsystem_address = nil, nil
    self.ability_input_component, self.ability_input_resolved = nil, nil
    self.ability_system_component = nil
end

function PlayerSession:refresh(controller, pawn)
    if self.invalidated then return false end
    if self.generation ~= self.backend.generation then return false end
    controller = controller or self.controller
    if not valid(controller) or address(controller) ~= self.controller_address then
        return false
    end
    pawn = pawn or self.backend:invoke(controller, "/Script/Engine.Controller:K2_GetPawn")
    if not valid(pawn) or address(pawn) ~= self.pawn_address then return false end
    if not valid(self.subsystem) then
        local ok, subsystem = pcall(self.backend.input_subsystem, self.backend, controller)
        if not ok then subsystem = rawget(self.backend, "subsystem") end
        self.subsystem, self.subsystem_address = subsystem, address(subsystem)
    end
    if not valid(self.ability_input_component) then
        self.ability_input_component, self.ability_input_resolved = nil, nil
    end
    if self.ability_system_component and not valid(self.ability_system_component) then
        self.ability_system_component = nil
    end
    return valid(self.subsystem)
end

function PlayerSession:assert_current()
    return not self.invalidated and self.generation == self.backend.generation
end

function PlayerSession:validate_pawn()
    if not self:assert_current() then return false end
    local controller = self.controller
    if not valid(controller) or address(controller) ~= self.controller_address then return false end
    local pawn = self.backend:invoke(controller, "/Script/Engine.Controller:K2_GetPawn")
    return valid(pawn) and address(pawn) == self.pawn_address
end

function PlayerSession:snapshot()
    return self:assert_current() and self or nil
end

function PlayerSession:context()
    return self:snapshot()
end

function PlayerSession:ability_input(context)
    context = context or self:context()
    if not context then return nil end
    if not self.ability_input_resolved then
        local component = self.backend:invoke(context.pawn, "/Script/Engine.Actor:GetComponentByClass",
            self.backend:object("/Script/SGameInputSystem.AbilityInputComponent"))
        self.ability_input_component, self.ability_input_resolved = component, true
    end
    return valid(self.ability_input_component) and self.ability_input_component or nil
end

function PlayerSession:ability_system(context)
    context = context or self:context()
    if not context then return nil end
    if not valid(self.ability_system_component) then
        self.ability_system_component = self.backend:invoke(
            self.backend:library("GameplayAbilities.AbilitySystemBlueprintLibrary"),
            "/Script/GameplayAbilities.AbilitySystemBlueprintLibrary:GetAbilitySystemComponent", context.pawn)
    end
    return valid(self.ability_system_component) and self.ability_system_component or nil
end

return PlayerSession
