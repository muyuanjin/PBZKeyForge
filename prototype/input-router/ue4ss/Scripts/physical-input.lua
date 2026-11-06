local PhysicalInput = {}
PhysicalInput.__index = PhysicalInput

local codes = {LeftMouseButton=1, RightMouseButton=2, MiddleMouseButton=4,
    ThumbMouseButton=5, ThumbMouseButton2=6, BackSpace=8, Tab=9, Enter=13,
    Pause=19, CapsLock=20, Escape=27, SpaceBar=32, PageUp=33, PageDown=34,
    End=35, Home=36, Left=37, Up=38, Right=39, Down=40, PrintScreen=44, Insert=45, Delete=46,
    LeftShift=160, RightShift=161, LeftControl=162, RightControl=163,
    LeftAlt=164, RightAlt=165, LeftCommand=91, RightCommand=92,
    NumLock=144, ScrollLock=145, Multiply=106, Add=107, Subtract=109,
    Decimal=110, Divide=111, Semicolon=186, Equals=187, Comma=188,
    Hyphen=189, Period=190, Slash=191, Tilde=192, LeftBracket=219,
    Backslash=220, RightBracket=221, Quote=222, Apostrophe=222}
local numbers = {Zero=0, One=1, Two=2, Three=3, Four=4, Five=5, Six=6,
    Seven=7, Eight=8, Nine=9}
for name, value in pairs(numbers) do codes[name] = 48 + value; codes["NumPad" .. name] = 96 + value end
for code = 48, 57 do codes[string.char(code)] = code end
for code = 65, 90 do codes[string.char(code)] = code end
for index = 1, 24 do codes["F" .. index] = 111 + index end
for index = 0, 9 do codes["NumPad" .. index] = 96 + index end

function PhysicalInput.new(dll, loader)
    return setmetatable({dll=dll, loader=loader or package.loadlib, probes={}}, PhysicalInput)
end

function PhysicalInput:sample(key)
    local code = codes[key]
    if not code or not self.dll then return nil end
    local probe = self.probes[code]
    if probe == nil then
        probe = self.loader(self.dll, "pbz_input_key_" .. code) or false
        self.probes[code] = probe
    end
    if probe == false then return nil end
    return probe(true) == true and 1 or 0
end

return PhysicalInput
