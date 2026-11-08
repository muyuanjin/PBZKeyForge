local scripts = "prototype/input-router/ue4ss/Scripts/"
package.path = scripts .. "?.lua;" .. package.path
local Backend = require("backend")
local LedgeProtection = require("ledge-protection")
local passed = 0
local function check(value, message)
    assert(value, message)
    passed = passed + 1
end
function FName(value) return {ToString=function() return value end} end

local function object(address, properties)
    local value = properties or {}
    function value:IsValid() return not self.destroyed end
    function value:GetAddress() return address end
    function value:GetPropertyValue(name) return self[name] end
    return value
end

local function fixture()
    local state = {combat=false,jump_forbidden=false,gate=0,calls={}}
    state.movement = object(3,{bForceEnableLedgeProtection=0,bForceDisableLedgeProtection=0,bCanWalkOffLedges=true})
    state.pawn = object(2,{CharacterMovement=state.movement})
    state.spec = {ActiveCount=0,Ability=object(10)}
    state.component = object(4,{ActivatableAbilities={Items={state.spec}}})
    local backend = Backend.new({enabled=true,require_foreground=false},function() end)
    backend.controller = object(1,{bShowMouseCursor=false})
    backend.pawn,backend.subsystem = state.pawn,object(5)
    state.subsystem = backend.subsystem
    backend.ability_specs = function() return "1" end
    backend.ledge_walk_off = function(address)
        if state.fail_gate then return 0,"fixture gate failure" end
        state.gate = address
        state.calls[#state.calls+1] = address
        return 1
    end
    function backend:object(path)
        check(path == "/Script/EnhancedInput.EnhancedInputLocalPlayerSubsystem", "unexpected reflected class")
        return object(9)
    end
    function backend:receiver(spec)
        check(spec.receiver == "ability_system", "eligibility belongs to the current player")
        return state.component
    end
    function backend:invoke(receiver, method, ...)
        local args = {...}
        if method == "/Script/Engine.SubsystemBlueprintLibrary:GetLocalPlayerSubSystemFromPlayerController" then return state.subsystem end
        if method == "/Script/Engine.Controller:K2_GetPawn" then return state.pawn end
        if method == "/Script/Engine.GameplayStatics:IsGamePaused" then return state.paused == true end
        if method == "/Script/Engine.Controller:IsMoveInputIgnored" then return state.ignored == true end
        if method == "/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayTagCount" then
            check(receiver == state.component, "tags must come from the current player")
            local tag = args[1].TagName:ToString()
            if tag == "CharacterState.InCombat" then return state.combat and 1 or 0 end
            if tag == "CharacterState.Forbid.Jump" then return state.jump_forbidden and 1 or 0 end
        end
        error("unexpected engine call: " .. method)
    end
    function backend:library() return {} end
    return backend,state
end

local function dodge(backend,target)
    LedgeProtection.prepare(backend,{{target=target or {kind="action",path="/PBZDeflectSystem/Input/IA_Dodge"}}})
end

do
    local backend,state = fixture()
    check(backend:refresh(), "local gameplay must remain ready")
    check(state.gate == 0, "walking alone must retain native protection")
    dodge(backend)
    check(state.gate == 3 and #state.calls == 1, "dodge must prepare its movement exception before input dispatch")
    state.spec.ActiveCount = 1
    LedgeProtection.prepare(backend,{})
    for frame=1,120 do backend:refresh() end
    check(state.gate == 3 and #state.calls == 1, "active rolls must retain the exception after release without repeated setup")
    state.movement.bForceDisableLedgeProtection = 0
    backend:refresh()
    check(state.gate == 3, "native counter resets must not remove the active physics exception")
    state.spec.ActiveCount = 0
    backend:refresh()
    check(state.gate == 0, "roll completion must restore walking protection")
    dodge(backend)
    state.combat = true
    backend:refresh()
    check(state.gate == 0, "entering combat must restore protection immediately")
    local calls = #state.calls
    backend:refresh()
    check(#state.calls == calls, "combat frames must not repeat cleanup")
end

for _,walk in ipairs({false,true}) do
    for _,forced in ipairs({0,2}) do
        for _,jump in ipairs({false,true}) do
            local backend,state = fixture()
            state.movement.bCanWalkOffLedges = walk
            state.movement.bForceEnableLedgeProtection = forced
            state.movement.bForceDisableLedgeProtection = 3
            state.jump_forbidden = not jump
            backend:refresh()
            dodge(backend)
            local allowed = (walk and forced == 0) or jump
            check(state.gate == (allowed and 3 or 0), "noncombat dodge must use walking OR jumping eligibility")
            check(state.movement.bCanWalkOffLedges == walk and state.movement.bForceEnableLedgeProtection == forced
                and state.movement.bForceDisableLedgeProtection == 3, "Lua must preserve native walking flags and all native counters")
            backend:shutdown()
            check(state.gate == 0 and state.movement.bForceDisableLedgeProtection == 3, "shutdown must retain other native owners")
        end
    end
end

for _,target in ipairs({
    {kind="action",path="/PBZDeflectSystem/Input/IA_DodgeHold.IA_DodgeHold"},
    {kind="action",object=object(11,{GetFullName=function() return "InputAction /PBZDeflectSystem/Input/IA_Dodge.IA_Dodge" end})},
    {kind="ability",tag="Ability.Action.Hun.SpecialDash"},
    {kind="move",tag="Ability.Action.Hun.ContinuousDash.Dash_02"}
}) do
    local backend,state = fixture()
    backend:refresh()
    dodge(backend,target)
    check(state.gate == 3, "native, object-backed, held and direct dodge requests must share the edge policy")
    backend:shutdown()
end

for _,condition in ipairs({"paused","cursor","ignored","disabled","component_missing"}) do
    local backend,state = fixture()
    backend:refresh()
    dodge(backend)
    if condition == "cursor" then backend.controller.bShowMouseCursor = true
    elseif condition == "disabled" then backend.config.enabled = false
    elseif condition == "component_missing" then state.component.destroyed = true
    else state[condition] = true end
    backend:refresh()
    check(state.gate == 0, condition .. " must restore native protection")
    backend:shutdown()
end

do
    local backend,state = fixture()
    backend:refresh()
    dodge(backend)
    state.movement = object(6,{bForceEnableLedgeProtection=0,bCanWalkOffLedges=true})
    state.pawn.CharacterMovement = state.movement
    backend:refresh()
    check(state.gate == 6, "movement replacement must transfer the active exception")
    state.pawn = object(7,{CharacterMovement=object(8,{bForceEnableLedgeProtection=0,bCanWalkOffLedges=true})})
    backend:refresh()
    check(state.gate == 0 and backend.ledge_movement == nil, "respawn must clear old dodge intent")
end

do
    local backend,state = fixture()
    backend:refresh()
    dodge(backend)
    state.movement.destroyed = true
    state.pawn.CharacterMovement = nil
    backend:refresh()
    check(backend.ledge_movement == nil and state.gate == 0, "destroyed movement must release the native hook")
end

do
    local backend,state = fixture()
    backend:refresh()
    dodge(backend)
    state.fail_gate = true
    check(not pcall(backend.shutdown,backend) and backend.ledge_movement == state.movement,
        "failed native restoration must remain tracked")
    state.fail_gate = false
    backend:shutdown()
    check(backend.ledge_movement == nil and state.gate == 0, "restoration retry must finish cleanup")
end

do
    local backend,state = fixture()
    backend:refresh()
    state.fail_gate = true
    check(not pcall(dodge,backend), "failed native setup must be reported")
    state.fail_gate = false
    backend:shutdown()
    check(backend.ledge_movement == nil and state.gate == 0, "partially acquired setup must remain cleanable")
end

do
    local backend,state = fixture()
    local scans,names = 0,0
    backend.active_ability_specs = function()
        scans=scans+1
        return state.spec.ActiveCount>0 and "1" or ""
    end
    backend.ability_specs = function() error("ledge queries must use the active selector") end
    local target = {kind="action",object=object(12,{GetFullName=function()
        names=names+1;return "InputAction /Input/IA_Move.IA_Move"
    end})}
    for frame=1,120 do
        backend:refresh()
        LedgeProtection.prepare(backend,{{target=target}})
    end
    check(scans==120 and names==1 and #state.calls==0,
        "idle movement must scan once per frame, resolve a stable action once and never patch the native gate")
    -- Native/buffered activation can begin after the request was released.
    state.spec.ActiveCount=1
    backend:refresh()
    check(state.gate==3,"a late native dodge must be detected without a pending input request")
    state.spec.ActiveCount=0
    backend:refresh()
    check(state.gate==0,"late native dodge completion must restore protection")
    target.path="/PBZDeflectSystem/Input/IA_Dodge"
    LedgeProtection.prepare(backend,{{target=target}})
    check(state.gate==3,"changing the action source must invalidate its classification")
    target.path="/Input/IA_Move"
    LedgeProtection.prepare(backend,{{target=target}})
    check(state.gate==0,"a replaced walking action must retain native protection")
end

print("MovementInputTests: " .. passed .. " assertions passed")
