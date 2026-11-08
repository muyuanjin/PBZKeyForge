local LedgeProtection = {}
local Selection = require("ability-selection")
local dashTags = {"Ability.Action.Hun.Dash", "Ability.Action.Hun.ContinuousDash", "Ability.Action.Hun.SpecialDash",
    "Ability.Action.Hun.PerfectDodge", "Ability.Action.Hun.DeflectDash", "Ability.Action.Hun.AirDash"}
local dashTarget = {resource_tags=dashTags,active_only=true}
local dashMatcher, dashSelection = Selection.matcher(dashTarget)
local actionDodge = setmetatable({}, {__mode="k"})
local walkOff
local function valid(object) return object and object:IsValid() end
local function dash_tag(tag)
    for _,name in ipairs(dashTags) do
        if tag == name or tag:sub(1,#name+1) == name.."." then return true end
    end
    return false
end
local function native_gate(backend, movement)
    local gate = backend.ledge_walk_off or walkOff
    if not gate then
        local scripts = debug.getinfo(1,"S").source:sub(2):match("^(.*[/\\])")
        gate = assert(package.loadlib(scripts:gsub("Scripts[/\\]$", "").."lib/pbzability-context.dll", "pbz_ledge_walk_off"))
        walkOff = gate
    end
    local ok,failure = gate(movement and movement:GetAddress() or 0)
    assert(ok == 1, failure or "Native movement edge gate failed")
end

function LedgeProtection.reset(backend)
    local movement = backend.ledge_movement
    if movement then
        native_gate(backend, nil)
        backend.ledge_movement = nil
    end
    backend.ledge_requested = nil
end

function LedgeProtection.update(backend)
    local movement
    if backend.config.enabled ~= false and backend.state.ready and backend.state.gameplay and not backend.state.move_ignored
        and valid(backend.pawn) then
        local candidate = backend.pawn.CharacterMovement
        if valid(candidate) then
            local component = backend:receiver({receiver="ability_system"})
            if valid(component) then
                local function count(tag)
                    return backend:invoke(component,
                        "/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayTagCount", {TagName=FName(tag)})
                end
                if count("CharacterState.InCombat") == 0 then
                    local walk = candidate:GetPropertyValue("bCanWalkOffLedges") == true
                        and candidate:GetPropertyValue("bForceEnableLedgeProtection") == 0
                    if walk or count("CharacterState.Forbid.Jump") == 0 then
                        local active = backend.ledge_requested
                        if not active then
                            Selection.each(backend, dashTarget, function() active = true end, dashMatcher, dashSelection)
                        end
                        if active then movement = candidate end
                    end
                end
            end
        end
    end
    local previous = backend.ledge_movement
    if valid(previous) and valid(movement) and previous:GetAddress() == movement:GetAddress() then return end
    local requested = backend.ledge_requested
    LedgeProtection.reset(backend)
    backend.ledge_requested = requested
    if movement then
        backend.ledge_movement = movement
        native_gate(backend, movement)
    end
end

function LedgeProtection.prepare(backend, requests)
    local requested = false
    for _,request in ipairs(requests) do
        local target = request.target
        if (target.kind == "ability" or target.kind == "move") and dash_tag(target.tag or "") then requested = true end
        if target.kind == "action" then
            local source = target.path or target.object
            local cached = actionDodge[target]
            if not cached or cached.source ~= source then
                local path = target.path or target.object:GetFullName():gsub("^%S+ ", "")
                path = path:gsub("%.[^./]+$", "")
                cached = {source=source,dodge=path == "/PBZDeflectSystem/Input/IA_Dodge" or path == "/PBZDeflectSystem/Input/IA_DodgeHold"}
                actionDodge[target] = cached
            end
            if cached.dodge then requested = true end
        end
    end
    if requested == not not backend.ledge_requested then return end
    backend.ledge_requested = requested
    LedgeProtection.update(backend)
end

return LedgeProtection
