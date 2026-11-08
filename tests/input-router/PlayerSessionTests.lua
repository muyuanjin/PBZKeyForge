package.path = "prototype/input-router/ue4ss/Scripts/?.lua;" .. package.path
local PlayerSession = require("player-session")

local passed = 0
local function check(value, message)
    assert(value, message)
    passed = passed + 1
end

local function object(address, properties)
    local value = properties or {}
    value.address = address
    function value:IsValid() return not self.invalid end
    function value:GetAddress() return self.address end
    return value
end

local controller = object(10)
local subsystem = object(11)
local pawn = object(20)
local component = object(21)
local ability_system = object(22)
local backend = {generation = 1}
local pawn_queries, component_queries = 0, 0

function backend:invoke(receiver, path, argument)
    if path == "/Script/Engine.Controller:K2_GetPawn" then pawn_queries = pawn_queries + 1; return pawn end
    if path == "/Script/Engine.Actor:GetComponentByClass" then
        component_queries = component_queries + 1
        check(receiver == pawn and argument == "/Script/SGameInputSystem.AbilityInputComponent",
            "ability component was not resolved from the session pawn")
        return component
    end
    if path == "/Script/GameplayAbilities.AbilitySystemBlueprintLibrary:GetAbilitySystemComponent" then
        check(argument == pawn, "ability system was not resolved from the session pawn")
        return ability_system
    end
    error("unexpected invoke " .. path)
end

function backend:input_subsystem(value)
    check(value == controller, "input subsystem was resolved from a different controller")
    return subsystem
end

function backend:library()
    return object(30)
end

function backend:object()
    return "/Script/SGameInputSystem.AbilityInputComponent"
end

local session = PlayerSession.new(backend, controller, pawn, backend.generation)
check(session:refresh(), "new player session did not resolve its input subsystem")
local context = session:context()
check(context and context.generation == 1 and context.pawn == pawn and context.subsystem == subsystem,
    "player session context lost its generation or current objects")
check(session:ability_input(context) == component, "ability input was not resolved at dispatch")
check(session:ability_system(context) == ability_system, "ability system was not resolved at dispatch")
pawn_queries, component_queries = 0, 0
for _=1,1000 do
    check(session:ability_input(context) == component, "cached ability input changed during the generation")
    check(session:ability_system(context) == ability_system, "cached ability system changed during the generation")
end
check(pawn_queries == 0 and component_queries == 0,
    "hot dispatch repeated Pawn or ability component lookups inside one generation")
session.ability_input_component.invalid = true
component = object(23)
check(session:refresh() and session:ability_input(context) == component,
    "same-generation component replacement retained the stale ability input")

local replacement = object(40)
pawn = replacement
check(not session:validate_pawn(), "pawn replacement passed the frame identity assertion")
check(session:assert_current(), "generation-only dispatch guard performed an unnecessary pawn lookup")
session:invalidate()
check(session.invalidated and session.controller == nil and session.pawn == nil and session.subsystem == nil,
    "session invalidation did not drop all player-owned references")

local next_session = PlayerSession.new(backend, controller, pawn, backend.generation)
check(next_session:refresh(), "replacement session did not resolve")
local next_context = next_session:context()
check(next_context.pawn == replacement and next_context.generation == 1,
    "replacement session retained the previous pawn")
backend.generation = 2
check(not next_session:assert_current(), "session from the previous generation remained active")

print("PlayerSessionTests: " .. passed .. " assertions passed")
