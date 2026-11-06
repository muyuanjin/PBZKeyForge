package.path = "prototype/input-router/ue4ss/Scripts/?.lua;" .. package.path
local NativeAbilities = dofile("prototype/input-router/ue4ss/Scripts/native-abilities.lua")
local passed, failures, nextAddress = 0, {}, 100
local special = "Ability.Action.Hun.SpecialDash"
local deflect = "Ability.Action.Hun.DeflectDash"
local dash = "Ability.Action.Hun.Dash"

local function check(value, message)
    assert(value, message)
    passed = passed + 1
end

local function test(name, run)
    local ok, message = pcall(run)
    if not ok then failures[#failures + 1] = name .. ": " .. tostring(message) end
end

function FName(value) return {ToString = function() return value end} end

local function trigger(tag, pointer)
    nextAddress = nextAddress + 1
    return {tag = tag, pointer = pointer or nextAddress, alive = true, writes = 0,
        Description = "native row", RequiredTags = {"State.Grounded"}, IgnoredTags = {"State.Dead"},
        DebugAbility = "unchanged-debug-reference"}
end

local function wrapper(row)
    local tag = setmetatable({}, {
        __index = function(_, key)
            assert(row.alive, "stale trigger tag accessed")
            assert(key == "TagName")
            return FName(row.tag)
        end,
        __newindex = function(_, key, value)
            assert(row.alive, "stale trigger tag written")
            assert(key == "TagName")
            row.tag, row.writes = value:ToString(), row.writes + 1
        end})
    return setmetatable({}, {__index = function(_, key)
        assert(row.alive, "stale trigger wrapper accessed")
        if key == "GetStructAddress" then return function() return row.pointer end end
        if key == "TriggerTag" then return tag end
        return row[key]
    end})
end

local function array(rows, transform)
    return {ForEach = function(_, callback)
        for index, row in ipairs(rows) do
            callback(index - 1, {get = function() return transform and transform(row) or row end})
        end
    end}
end

local function data(actions, combos, interrupts)
    return {Actions = array(actions or {}, wrapper), Combos = array(combos or {}, wrapper),
        InterruptLower = array(interrupts or {}, wrapper)}
end

local function binding(normal, conditions)
    return {ActionPriority = -5, Description = "native group", NormalData = normal,
        TagConditionData = {ForEach = function(_, callback)
            for name, value in pairs(conditions or {}) do
                callback({get = function() return name end}, {get = function() return value end})
            end
        end}}
end

local function asset(groups, pointer)
    nextAddress = nextAddress + 1
    local result = {Bindings = array(groups), valid = true, pointer = pointer or nextAddress}
    function result:IsValid() return self.valid end
    function result:GetAddress() assert(self.valid, "invalid asset dereferenced"); return self.pointer end
    return result
end

local function fixture(tags)
    local state = {assets = {}, scans = 0}
    local backend = {config = {ability_suppressions = tags or {}}}
    function FindAllOf(name)
        assert(name == "InputActionBinding", "unexpected native class")
        state.scans = state.scans + 1
        return state.assets
    end
    return NativeAbilities.new(backend), backend, state
end

test("empty configuration performs no native enumeration", function()
    local native, _, state = fixture()
    native:prepare()
    native:restore()
    check(state.scans == 0, "clean empty configuration scanned native objects")
    check(next(native.masked) == nil, "empty configuration retained changes")
end)

test("normal and conditional trigger arrays retain all surrounding native data", function()
    local native, backend, state = fixture({special})
    local rows, untouched = {}, trigger(dash)
    for index = 1, 6 do rows[index] = trigger(special) end
    local group = binding(data({rows[1], untouched}, {rows[2]}, {rows[3]}),
        {Grounded = data({rows[4]}, {rows[5]}, {rows[6]})})
    local otherGroup = binding(data({trigger(dash)}))
    state.assets = {asset({group, otherGroup})}
    native:prepare()
    for _, row in ipairs(rows) do
        check(row.tag == "None" and row.writes == 1, "matching trigger array was not masked")
        check(row.Description == "native row" and row.RequiredTags[1] == "State.Grounded"
            and row.IgnoredTags[1] == "State.Dead" and row.DebugAbility == "unchanged-debug-reference",
            "non-trigger fields changed")
    end
    check(group.ActionPriority == -5 and group.Description == "native group", "binding group changed")
    check(untouched.tag == dash and untouched.writes == 0, "ordinary dash changed")
    native:prepare()
    check(rows[1].writes == 1, "already masked tag was rewritten")
    backend.config.ability_suppressions = {}
    native:prepare()
    for _, row in ipairs(rows) do check(row.tag == special and row.writes == 2, "native tag not restored") end
    local scans = state.scans
    native:prepare()
    native:restore()
    check(state.scans == scans and next(native.masked) == nil, "clean restoration retained work")
end)

test("configuration changes restore only tags no longer suppressed", function()
    local native, backend, state = fixture({special})
    local first, second, none = trigger(special), trigger(deflect), trigger("None")
    state.assets = {asset({binding(data({first, second, none}))})}
    native:prepare()
    backend.config.ability_suppressions = {deflect}
    native:prepare()
    check(first.tag == special and second.tag == "None", "new suppression did not replace old suppression")
    check(none.tag == "None" and none.writes == 0, "original None acquired an invented tag")
    native:restore()
    check(second.tag == deflect and first.writes == 2, "release did not restore retained tags")
    check(none.writes == 0, "release changed an originally None tag")
end)

test("late assets and later array allocations are scanned every active frame", function()
    local native, _, state = fixture({special})
    native:prepare()
    local first = trigger(special)
    local loaded = asset({binding(data({first}))})
    state.assets = {loaded}
    native:prepare()
    check(first.tag == "None", "late loaded asset was not suppressed")
    local rebuilt, originallyNone = trigger(special), trigger("None")
    first.alive = false
    loaded.Bindings = array({binding(data({originallyNone, rebuilt}))})
    native:prepare()
    check(rebuilt.tag == "None" and originallyNone.writes == 0, "rebuilt rows were not distinguished")
    native:restore()
    check(rebuilt.tag == special and originallyNone.tag == "None", "current allocation restored incorrectly")
    check(first.writes == 1, "retired allocation was written")
end)

test("restoration enumerates current positions without retaining wrappers", function()
    local native, _, state = fixture({special})
    local old, replacement = trigger(special), trigger("None")
    local loaded = asset({binding(data({old}))})
    state.assets = {loaded}
    native:prepare()
    old.alive = false
    loaded.Bindings = array({binding(data({replacement}))})
    native:restore()
    check(replacement.tag == "None" and replacement.writes == 0, "new None row inherited retired original")
    check(next(native.masked) == nil, "retired allocation remained tracked")
end)

test("asset address separates identical struct addresses", function()
    local native, _, state = fixture({special, deflect})
    local first, second = trigger(special, 500), trigger(deflect, 500)
    state.assets = {asset({binding(data({first}))}, 600), asset({binding(data({second}))}, 700)}
    native:prepare()
    native:restore()
    check(first.tag == special and second.tag == deflect, "one asset restored another asset's tag")
end)

test("current positions may move while original struct remains valid", function()
    local native, _, state = fixture({special})
    local row = trigger(special)
    local loaded = asset({binding(data({row}))})
    state.assets = {loaded}
    native:prepare()
    loaded.Bindings = array({binding(data({})), binding(data({}, {}, {row}))})
    native:restore()
    check(row.tag == special, "restoration depended on an obsolete array index")
end)

test("external tag edits remain authoritative", function()
    local native, backend, state = fixture({special})
    local row = trigger(special)
    state.assets = {asset({binding(data({row}))})}
    native:prepare()
    row.tag = dash
    native:restore()
    check(row.tag == dash and row.writes == 1, "restoration overwrote an external tag edit")
    row.tag = deflect
    backend.config.ability_suppressions = {deflect}
    native:prepare()
    native:restore()
    check(row.tag == deflect, "new external value did not become the restored original")
end)

test("invalid and unloaded assets are never dereferenced for restoration", function()
    local native, _, state = fixture({special})
    local row = trigger(special)
    local loaded = asset({binding(data({row}))})
    state.assets = {loaded}
    native:prepare()
    row.alive, loaded.valid = false, false
    native:restore()
    check(row.writes == 1 and next(native.masked) == nil, "invalid asset was retained or written")
    state.assets = nil
    native:prepare()
    native:restore()
    check(next(native.masked) == nil, "empty enumeration retained obsolete tags")
end)

test("temporary native containers survive collection during callbacks", function()
    local function temporary(rows, transform)
        return {ForEach = function(self, callback)
            local receiver = setmetatable({self}, {__mode = "v"})
            self = nil
            for index, row in ipairs(rows) do
                collectgarbage("collect")
                check(receiver[1] ~= nil, "native container collected during ForEach")
                callback(index, {get = function() return transform and transform(row) or row end})
            end
        end}
    end
    local native, _, state = fixture({special})
    local row = trigger(special)
    local entries = setmetatable({}, {__index = function()
        return temporary({row}, wrapper)
    end})
    local group = setmetatable({NormalData = entries}, {__index = function(_, name)
        assert(name == "TagConditionData")
        return temporary({entries})
    end})
    local loaded = asset({})
    loaded.Bindings = nil
    setmetatable(loaded, {__index = function(_, name)
        assert(name == "Bindings")
        return temporary({group})
    end})
    state.assets = {loaded}
    native:prepare()
    check(row.tag == "None", "collection interrupted ability suppression")
    native:restore()
    check(row.tag == special, "collection interrupted ability restoration")
end)

test("asset creation notifications replace repeated global discovery", function()
    local native,_,state=fixture({special})
    local first=trigger(special)
    local loaded=asset({binding(data({first}))})
    function loaded:GetClass() return {GetFullName=function() return "Class /Script/Game.InputActionBinding" end} end
    state.assets={loaded}
    local created
    function NotifyOnNewObject(path,callback)
        check(path=="/Script/Game.InputActionBinding", "asset observer used a guessed class")
        created=callback
    end
    native:prepare();native:prepare()
    check(state.scans==1 and first.tag=="None", "unchanged assets repeated global discovery")
    local second=trigger(special)
    created(asset({binding(data({second}))}))
    native:prepare()
    check(second.tag=="None" and state.scans==1, "new asset was not suppressed on its first frame")
    first.alive=false
    local rebuilt=trigger(special)
    loaded.Bindings=array({binding(data({rebuilt}))})
    native:prepare();native:restore()
    check(rebuilt.tag==special and second.tag==special and first.writes==1,
        "asset caching lost current allocation restoration")
end)

if #failures > 0 then error(table.concat(failures, "\n"), 0) end
print("NativeAbilityTests: PASS " .. passed)
