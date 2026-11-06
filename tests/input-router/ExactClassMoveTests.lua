package.path = "prototype/input-router/ue4ss/Scripts/?.lua;" .. package.path
local Moves = require("complete-moves")
local checks = 0
local function check(value, message)
    assert(value, message)
    checks = checks + 1
end
local function object(values)
    local result = values or {}
    function result:IsValid() return true end
    return result
end
local first = "/PBZCombatAsset/ActorData/Players/Hun/Abilities/Common/GA_Hun_PerfectDodge_FireBallCounter"
local second = "/PBZCombatAsset/ActorData/Players/Hun/Abilities/Common/GA_Hun_PerfectDodge_ThunderCounter"
local function fixture(granted)
    local specs, classes = {}, {}
    for _, path in ipairs(granted) do
        classes[path] = object({GetFullName=function() return "LegoAbilityBlueprintGeneratedClass " .. path end})
        local cdo = object({GetClass=function() return classes[path] end})
        specs[#specs+1] = {Ability=cdo, ActiveCount=0}
    end
    local asc = object({ActivatableAbilities={Items={ForEach=function(_, callback)
        for index, spec in ipairs(specs) do callback(index, {get=function() return spec end}) end
    end}}})
    local receiver, input = object(), object()
    local backend = {calls=0}
    function backend:receiver(target) return target.receiver == "ability_system" and asc or receiver end
    function backend:object(path, kind)
        check(path == "input/perfect-dodge" and kind == "InputAction", "Only the original input action is resolved")
        return input
    end
    function backend:invoke(target, method, ability_class, action, priority)
        check(target == receiver and method == "/Script/SGameInputSystem.AbilityInputComponent:TryActivateExternalActionAbilityByClass",
            "Exact class activation must retain the native input-component entry")
        check(ability_class == classes[second] and action == input and priority == 3,
            "Abilities sharing a tag must select only the requested granted class, original action and priority")
        self.calls = self.calls + 1
        return not self.reject
    end
    return backend
end
local move = {tag="Ability.Action.Hun.PerfectDodge", ability_class=second .. ".GA_Hun_PerfectDodge_ThunderCounter_C",
    input_action="input/perfect-dodge", activate_by_class=true, priority=3}
-- Fixtures use the actual generated class object path, including _C.
first = first .. ".GA_Hun_PerfectDodge_FireBallCounter_C"
second = second .. ".GA_Hun_PerfectDodge_ThunderCounter_C"
do
    local backend = fixture({first, second})
    check(Moves.dispatch(backend,move,"started") and backend.calls == 1,
        "The selected counter must activate without a tag-based fallback")
end
do
    local backend = fixture({first, second})
    backend.reject = true
    check(not Moves.dispatch(backend,move,"started") and backend.calls == 1,
        "Native counter-frame and state rejection must remain a rejection")
end
do
    local backend = fixture({first})
    check(not Moves.dispatch(backend,move,"started") and backend.calls == 0,
        "A counter not temporarily granted by the game must never be created or replaced by another same-tag counter")
end
do
    local backend = fixture({})
    check(not Moves.dispatch(backend,move,"triggered") and backend.calls == 0,
        "Expired grants must leave no executable counter")
end
do
    local backend = fixture({first,second})
    check(Moves.dispatch(backend,move,"completed") and backend.calls == 0,
        "Releasing the physical key must not activate another counter")
end
print("PASS: " .. checks .. " exact-class counter checks")
