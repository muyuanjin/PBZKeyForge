package.loaded["script-context"] = {new=function() error("Untuned attacks must not create tuning state") end}
package.path = "prototype/input-router/ue4ss/Scripts/?.lua;" .. package.path
local Moves = require("complete-moves")
local Router = require("router")
function FName(value) return value end
local root = os.getenv("PBZ_WEAPON_MOVE_CHECK") or "dist/weapon-move-check"
local count = 0
local function object(value)
    value = value or {}
    function value:IsValid() return true end
    function value:GetAddress() return self end
    return value
end
local function fixture(target, granted)
    local selected = object({GetFullName=function() return "LegoAbilityBlueprintGeneratedClass " .. target.ability_class end})
    local other = object()
    function StaticFindObject(path) return path == target.ability_class and selected or other end
    local empty = {GetArrayNum=function() return 0 end}
    local items = {}
    if granted then
        items[1] = {Ability=object({GetClass=function() return selected end}), ActiveCount=0,
            NonReplicatedInstances=empty, ReplicatedInstances=empty}
    else
        items[1] = {Ability=object({GetClass=function() return other end}), ActiveCount=0,
            NonReplicatedInstances=empty, ReplicatedInstances=empty}
    end
    function items:GetArrayNum() return #self end
    function items:ForEach(callback)
        for index, spec in ipairs(self) do callback(index,{get=function() return spec end}) end
    end
    local asc = object({ActivatableAbilities={Items=items}})
    local receiver, input = object(), object()
    local backend = {calls=0}
    function backend:receiver(spec) return spec.receiver == "ability_system" and asc or receiver end
    function backend:object(path, kind)
        assert(path == target.input_action and kind == "InputAction", "Use the recipe's actual input action")
        return input
    end
    function backend:invoke(owner, method, class, action, priority)
        if method:match(":GetGameplayTagCount$") then
            for _, tag in ipairs(target.ignored_tags or {}) do if class.TagName == tag then return 0 end end
            return self.missing_tag and 0 or 1
        end
        assert(owner == receiver and method:match(":TryActivateExternalActionAbilityByClass$"), "Use normal exact-class activation")
        assert(class == selected and action == input and priority == target.priority, "Do not substitute a shared-tag attack")
        self.calls = self.calls + 1
        return not self.reject
    end
    return backend
end
for _, device in ipairs({"keyboard", "pad"}) do
    local config = assert(loadfile(root .. "/weapon-" .. device .. ".lua"))()
    local expected = 0
    for _, rule in ipairs(config.bindings) do
        for _, output in ipairs(rule.outputs or {}) do
            local target = assert(config.targets[output.target])
            expected = expected + 1
            assert(target.kind == "move" and target.activate_by_class and not target.force)
            local backend = fixture(target,true)
            assert(Moves.dispatch(backend,target,"triggered") and backend.calls == 1, "A granted weapon attack must execute")
            backend = fixture(target,false)
            assert(not Moves.dispatch(backend,target,"triggered") and backend.calls == 0, "A different granted class must not substitute for this attack")
            backend = fixture(target,true); backend.reject = true
            assert(not Moves.dispatch(backend,target,"triggered") and backend.calls == 1, "Native eligibility rejection must remain a rejection")
            if #target.required_tags > 0 then
                backend = fixture(target,true); backend.missing_tag = true
                assert(not Moves.dispatch(backend,target,"triggered") and backend.calls == 0, "The weapon requirement must precede activation")
            end
            assert(Moves.dispatch(fixture(target,true),target,"completed"), "Key release must stop requests")
            count = count + 1
        end
    end
    local routed = 0
    local backend = {}
    function backend:dispatch(target, phase)
        assert(Moves.dispatch(fixture(target,true),target,phase))
        routed = routed + 1
        return true
    end
    local router = Router.new(config,backend)
    local key = device == "pad" and "Gamepad_DPad_Down" or "G"
    router:step(0,{})
    router:step(.1,{[key]=1})
    local first = routed
    assert(first == expected, "The scheduler must request every selected weapon attack")
    router:step(.2,{[key]=1})
    assert(routed == 2*first, "Default weapon bindings must continue requesting while held")
    router:step(.3,{})
    assert(routed == 2*first, "Physical release must stop requests")
    local tuned = assert(loadfile(root .. "/weapon-" .. device .. "-tuned.lua"))()
    local effects = 0
    local effectBackend = {}
    function effectBackend:dispatch(target, phase, value, bindingId, bypassResources, settings)
        assert(bypassResources and settings.damage_multiplier == 2.5
            and settings.startup_speed == 1.5 and settings.recovery_speed == .75,
            "The scheduler must deliver every move's resource, damage and speed settings")
        assert(target.activate_by_class and phase == "triggered")
        effects = effects + 1
        return true
    end
    local tunedRouter = Router.new(tuned,effectBackend)
    tunedRouter:step(0,{})
    tunedRouter:step(.1,{[key]=1})
    assert(effects == expected, "Tuning must apply to the complete catalog on both devices")
end
assert(count > 200, "Exercise the compiled weapon catalog on both devices")
print("CompiledWeaponCatalogTests PASS " .. count)
