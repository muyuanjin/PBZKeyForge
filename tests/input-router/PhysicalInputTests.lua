local PhysicalInput = dofile("prototype/input-router/ue4ss/Scripts/physical-input.lua")
local states, loads, passed = {}, {}, 0
local function check(value, message) assert(value, message); passed = passed + 1 end
local input = PhysicalInput.new("local.dll", function(dll, symbol)
    check(dll == "local.dll", "physical probe loaded an unrelated library")
    local code = tonumber(symbol:match("pbz_input_key_(%d+)$")); assert(code)
    loads[code] = (loads[code] or 0) + 1
    return function(argument) check(argument == true, "probe lost its Lua return argument"); return states[code] and true or nil end
end)
states[87] = true
check(input:sample("W") == 1, "W press was not sampled")
states[87] = false
check(input:sample("W") == 0, "release must return zero rather than permit stale engine input")
check(loads[87] == 1, "per-frame sampling reloaded an existing export")
states[160] = true; check(input:sample("LeftShift") == 1, "left modifier mapping is wrong")
states[161] = false; check(input:sample("RightShift") == 0, "right modifier reused left modifier")
states[5] = true; check(input:sample("ThumbMouseButton") == 1, "mouse side key was not sampled")
states[49] = true; check(input:sample("One") == 1 and input:sample("1") == 1, "digit aliases differ")
states[97] = true; check(input:sample("NumPadOne") == 1 and input:sample("NumPad1") == 1, "supported numeric keypad names differ")
states[222] = true; check(input:sample("Quote") == 1, "the editor's quote key is unsupported")
check(input:sample("Gamepad_Left2D") == nil and input:sample("MouseX") == nil, "unsupported axes must retain engine sampling")
local missing = PhysicalInput.new("old.dll", function() return nil end)
check(missing:sample("W") == nil and missing:sample("W") == nil, "unavailable exports masquerade as released keys")
check(PhysicalInput.new(nil):sample("W") == nil, "a missing library disabled engine input")
local binary = os.getenv("PBZ_PHYSICAL_TEST_DLL")
if binary then
    local actual = PhysicalInput.new(binary)
    check(type(actual:sample("W")) == "number", "packaged native W export cannot be called by Lua")
    check(type(actual:sample("LeftMouseButton")) == "number", "packaged mouse export cannot be called by Lua")
end
print("PhysicalInputTests: " .. passed .. " assertions passed")
