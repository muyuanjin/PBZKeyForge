local scripts = "prototype/input-router/ue4ss/Scripts/"
package.path = scripts .. "?.lua;" .. package.path
local Backend = dofile(scripts .. "backend.lua")
local passed = 0
local function check(value, message)
    assert(value, message)
    passed = passed + 1
end
local function object(address, fields)
    local result = fields or {}
    function result:IsValid() return self.invalid ~= true end
    function result:GetAddress() return address end
    return result
end
local textClasses = {"EditableText", "EditableTextBox", "MultiLineEditableText", "MultiLineEditableTextBox"}
local function fixture(unified)
    local state = {mode = 1, widgets = {}, logs = {}, masks = false,
        prepares = 0, restores = 0, classQueries = 0, modeQueries = 0, subsystemQueries = 0}
    local backend = Backend.new({enabled = true, targets = {}, bindings = {},
        dynamic_capture = true, unified_native = unified ~= false, require_foreground = false},
        function(message) state.logs[#state.logs + 1] = message end)
    backend.controller, backend.pawn, backend.subsystem = object(1), object(2), object(3)
    state.class, state.router = object(4), object(5)
    local gameplayLibrary, subsystemLibrary = object(6), object(7)
    function StaticFindObject(path)
        assert(path == "/Script/ProjectPBZ.PBZUIActionRouterBase", "unexpected class query " .. path)
        state.classQueries = state.classQueries + 1
        return state.class
    end
    function FindAllOf(name)
        for _, className in ipairs(textClasses) do
            if name == className then return state.widgets[name] end
        end
        error("unexpected object query " .. name)
    end
    function backend:library(name)
        if name == "Engine.GameplayStatics" then return gameplayLibrary end
        assert(name == "Engine.SubsystemBlueprintLibrary", "unexpected library " .. name)
        return subsystemLibrary
    end
    function backend:invoke(receiver, path, ...)
        local args = {...}
        if path == "/Script/Engine.Controller:K2_GetPawn" then
            assert(receiver == self.controller and #args == 0)
            return self.pawn
        elseif path == "/Script/Engine.Controller:IsMoveInputIgnored" then
            assert(receiver == self.controller and #args == 0)
            return state.moveIgnored == true
        elseif path == "/Script/Engine.GameplayStatics:IsGamePaused" then
            assert(receiver == gameplayLibrary and args[1] == self.controller and #args == 1)
            return state.paused == true
        elseif path == "/Script/Engine.SubsystemBlueprintLibrary:GetLocalPlayerSubSystemFromPlayerController" then
            assert(receiver == subsystemLibrary and args[1] == self.controller and args[2] == state.class and #args == 2,
                "CommonUI router must be obtained from the current local player's PBZ subsystem class")
            state.subsystemQueries = state.subsystemQueries + 1
            return state.router
        elseif path == "/Script/ProjectPBZ.PBZUIActionRouterBase:GetActiveInputMode" then
            assert(receiver == state.router and #args == 0)
            state.modeQueries = state.modeQueries + 1
            return state.mode
        elseif path == "/Script/UMG.Widget:HasKeyboardFocus" then
            assert(#args == 0)
            return receiver.keyboard == true
        elseif path == "/Script/UMG.Widget:HasFocusedDescendants" then
            assert(#args == 0)
            return receiver.descendants == true
        end
        error("unexpected method " .. path)
    end
    backend.physical_input = {sample = function(_, key) return key == "W" and 1 or 0 end}
    function backend:key_dimension() return 1 end
    backend.native_input = {restore = function()
        state.restores = state.restores + 1
        state.masks = false
    end}
    function backend:prepare_dynamic_capture()
        state.prepares = state.prepares + 1
        state.masks = true
    end
    return backend, state
end

do
    local backend, state = fixture()
    check(backend:refresh() and backend:context().input_layer and backend:context().gameplay,
        "PBZ CommonUI Game=1 did not enable gameplay routing")
    check(state.masks and state.prepares == 1 and state.modeQueries == 1 and state.subsystemQueries == 1,
        "Game mode did not capture through the current local player's action router")
    state.mode = 0
    check(backend:refresh() and not backend:context().input_layer and not backend:context().gameplay,
        "PBZ CommonUI Menu=0 was treated as gameplay")
    check(not state.masks and state.restores == 1 and state.prepares == 1,
        "entering Menu mode left gameplay key enhancement active instead of restoring native input")
    state.mode = 2
    check(backend:refresh() and backend:context().input_layer and backend:context().gameplay and state.masks,
        "PBZ CommonUI All=2 did not resume gameplay routing")
    state.mode = 3
    check(backend:refresh() and not backend:context().gameplay and not state.masks,
        "an unknown input mode retained gameplay key enhancement")
end

for _, className in ipairs(textClasses) do
    for _, focus in ipairs({"keyboard", "descendants"}) do
        for _, mode in ipairs({1, 2}) do
            local backend, state = fixture()
            state.mode = mode
            backend:refresh()
            local widget = object(100, {[focus] = true})
            state.widgets[className] = {widget}
            check(backend:refresh() and not backend:context().input_layer and not backend:context().gameplay,
                className .. " " .. focus .. " focus did not suspend gameplay routing")
            check(not state.masks and state.restores == 1,
                className .. " " .. focus .. " focus did not restore native input")
            check(backend:snapshot({"W"}).W == 1,
                "text focus discarded physically held movement input")
            widget[focus] = false
            check(backend:refresh() and backend:context().gameplay and state.masks and state.prepares == 2,
                className .. " " .. focus .. " blur did not resume gameplay routing")
        end
    end
end

do
    local backend, state = fixture()
    state.widgets.EditableText = {object(100, {keyboard = true, invalid = true}), object(101)}
    check(backend:refresh() and backend:context().gameplay,
        "an invalid or unfocused editable widget blocked gameplay routing")
    for _, mode in ipairs({1, 2}) do
        state.mode, backend.ui_only = mode, true
        check(backend:refresh() and not backend:context().input_layer and not state.masks,
            "UI-only state failed to restore native input in an active gameplay input mode")
        backend.ui_only = false
        check(backend:refresh() and backend:context().gameplay and state.masks,
            "leaving UI-only state did not resume gameplay routing")
    end
end

do
    local backend, state = fixture()
    backend:refresh()
    state.class.invalid = true
    state.class = nil
    check(backend:refresh() and not backend:context().input_layer and not backend:context().gameplay,
        "a missing PBZ CommonUI class did not fall back to native input")
    check(not state.masks and state.restores == 1 and state.subsystemQueries == 1,
        "missing class retained key enhancement or invoked a subsystem with no valid class")
    backend:refresh()
    check(#state.logs == 1 and state.logs[1]:find("native input retained", 1, true),
        "missing-class fallback did not report once that native input was retained")
    state.class = object(4)
    check(backend:refresh() and backend:context().gameplay and state.masks,
        "a subsequently loaded PBZ CommonUI class did not allow routing to resume")
end

do
    local backend, state = fixture()
    backend:refresh()
    state.router.invalid = true
    state.router = nil
    check(backend:refresh() and not backend:context().gameplay and not state.masks,
        "an unavailable local-player UI router did not restore native input")
    check(state.modeQueries == 1, "GetActiveInputMode was invoked on an unavailable UI router")
    state.router = object(5)
    check(backend:refresh() and backend:context().gameplay and state.masks,
        "a restored local-player UI router did not resume routing")
end

do
    local backend, state = fixture(false)
    state.class, state.mode = nil, 0
    backend.ui_only = true
    state.widgets.EditableText = {object(100, {keyboard = true})}
    check(backend:refresh() and backend:context().input_layer and backend:context().gameplay,
        "legacy routing unexpectedly required the unified CommonUI input-layer policy")
    check(state.classQueries == 0 and state.modeQueries == 0 and state.subsystemQueries == 0,
        "disabled unified routing still queried the PBZ CommonUI subsystem")
end

do
    local backend,state=fixture()
    local listeners,searches={},0
    function NotifyOnNewObject(path,callback) listeners[path]=callback end
    local search=FindAllOf
    function FindAllOf(name) searches=searches+1;return search(name) end
    backend:refresh()
    for _=1,120 do backend:refresh() end
    check(searches==4 and state.classQueries==1 and state.subsystemQueries==1,
        "stable frames repeated global widget, class or subsystem discovery")
    local widget=object(201,{keyboard=true})
    listeners["/Script/UMG.EditableText"](widget)
    check(backend:refresh() and not backend:context().gameplay and not state.masks,
        "newly created focused text widget was not honored on the next frame")
    widget.keyboard=false
    check(backend:refresh() and backend:context().gameplay and state.masks,
        "cached widget focus did not recover immediately after blur")
    widget.invalid=true
    check(backend:refresh() and backend:context().gameplay and searches==4,
        "destroying a cached widget blocked gameplay or forced rediscovery")
end

print("UnifiedBackendTests: " .. passed .. " assertions passed")
