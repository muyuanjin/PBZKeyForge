package.path = "prototype/input-router/ue4ss/Scripts/?.lua;" .. package.path
local Router = require("router")
local path = os.getenv("PBZ_COMPLETE_MOVE_CONFIG") or "dist/complete-move-catalog-check/complete-move.lua"
local config = assert(loadfile(path))()
local dispatches = {}
local backend = {}
function backend:dispatch(target, phase, value, binding)
    assert(target.kind == "move" and target.move_id == "move:Ability.Action.Hun.Heavy.BronzeSword.BHold.Release2")
    assert(target.tag == "Ability.Action.Hun.Heavy.BronzeSword.BHold")
    assert(target.entry_point:match("_C:EndSuccess$") and target.priority == 3)
    assert(target.required_tags[1] == "CharacterState.Space.Ground" and target.required_tags[2] == "Weapon.Type.BronzeSword")
    dispatches[#dispatches+1] = {phase=phase, binding=binding}
    return true
end
local router = Router.new(config,backend)
router:step(0, {})
router:step(.1, {G=1})
router:step(.2, {G=1})
router:step(1.2, {G=1})
assert(#dispatches == 3 and dispatches[1].phase == "triggered", "Holding a default binding must continue requesting the complete move")
router:step(1.3, {})
assert(#dispatches == 3, "Physical release must stop complete-move requests")
router:step(1.4, {G=1})
assert(#dispatches == 4, "A fresh press must request another move")
router:step(1.5, {})
router:step(1.6, {Gamepad_DPad_Down=1})
assert(#dispatches == 5, "The gamepad binding must use the same complete-move output")
router:step(1.7, {Gamepad_DPad_Down=1})
assert(#dispatches == 6, "The gamepad default must continue complete-move requests while held")
local firePath = path:gsub("complete%-move%.lua$", "fire-move.lua")
local fireConfig = assert(loadfile(firePath))()
local fireCalls = 0
local fireBackend = {}
function fireBackend:dispatch(target, phase)
    assert(target.kind == "move" and target.move_id == "move:Ability.Action.Hun.ShachiAbility.HuoLiuXing.AAAA.Final")
    assert(target.entry_point:match("_C:ToRelease$") and target.priority == 3)
    assert(target.required_tags[1] == "CharacterState.Space.Ground"
        and target.required_tags[2] == "CharacterState.WeaponUpgrade.HuoLiuXing.AAAA"
        and target.required_tags[3] == "Weapon.Type.ShaqiWeapon.HuoLiuXing")
    assert(target.active_boolean_properties[1] == "Current Shachi Reinforced")
    assert(phase == "triggered")
    fireCalls = fireCalls + 1
    return true
end
local fireRouter = Router.new(fireConfig, fireBackend)
fireRouter:step(0, {})
fireRouter:step(.1, {J=1})
fireRouter:step(1, {J=1})
fireRouter:step(1.1, {})
assert(fireCalls == 2, "The Phantom Edge default must continue resource-qualified requests while held")
local chargePath = path:gsub("complete%-move%.lua$", "charge-move.lua")
local chargeConfig = assert(loadfile(chargePath))()
local chargeCalls = 0
local chargeBackend = {}
function chargeBackend:dispatch(target, phase)
    assert(target.kind == "move" and target.move_id == "move:Ability.Action.Hun.Heavy.GuiTouDao.BHold.Release")
    assert(target.entry_point:match("_C:EventReceived_7594CBDE4CE41BEBFC9ABFBA4E2D1DF1$") and target.priority == 2)
    assert(target.required_tags[2] == "CharacterState.WeaponUpgrade.GuiTouBlade.CanCharge" and target.required_tags[3] == "Weapon.Type.GuiTouBlade")
    assert(target.ignored_tags[1] == "CharacterState.AbilityState.GuiTouBlade.InBHold")
    assert(#target.entry_arguments == 1 and type(target.entry_arguments[1]) == "table" and next(target.entry_arguments[1]) == nil)
    assert(phase == "triggered")
    chargeCalls = chargeCalls + 1
    return true
end
local chargeRouter = Router.new(chargeConfig, chargeBackend)
chargeRouter:step(0, {})
chargeRouter:step(.1, {K=1})
chargeRouter:step(1, {K=1})
chargeRouter:step(1.1, {})
assert(chargeCalls == 2, "The selected charged release must continue normal activation requests while held")
for _,fixture in ipairs({{name="json-press-move.lua", expected=1}, {name="explicit-press-move.lua", expected=1}}) do
    local fixturePath = path:gsub("complete%-move%.lua$", fixture.name)
    local fixtureConfig = assert(loadfile(fixturePath))()
    local requests = 0
    local fixtureBackend = {}
    function fixtureBackend:dispatch(target, phase)
        assert(target.move_id == "move:Ability.Action.Hun.Heavy.BronzeSword.BHold.Release2" and phase == "triggered")
        requests = requests + 1
        return true
    end
    local fixtureRouter = Router.new(fixtureConfig, fixtureBackend)
    fixtureRouter:step(0, {G=1})
    fixtureRouter:step(.1, {G=1})
    fixtureRouter:step(.2, {G=1})
    fixtureRouter:step(.3, {})
    assert(requests == fixture.expected, "Saved complete-move trigger semantics changed: " .. fixture.name)
end
local phasePath = path:gsub("complete%-move%.lua$", "phase-moves.lua")
assert(loadfile("tests/input-router/CompiledPhaseMoveTests.lua"))(phasePath)
print("CompiledCompleteMoveTests PASS")
