local Moves = require("complete-moves")
local Equipment = require("move-equipment")
local Selection = require("ability-selection")
local Inputs = {}

local function key(target, binding)
    return tostring(binding or target) .. "|" .. tostring(target.move_id or target.ability_class or target.tag)
end

local function release(backend, state)
    for _, input in ipairs(state.inputs) do Moves.release_input(backend,input) end
    backend.native_move_inputs[state.key] = nil
end

local function ready(backend, state)
    for _, input in ipairs(state.inputs) do
        if not Moves.input_held(backend,input.prepared_action) then return false end
    end
    return true
end

local function execute(backend, state)
    local request = state.pending
    state.pending = nil
    state.dispatched_frame = backend.native_move_input_frame or 0
    local ok, result, reason = backend:dispatch(request.target,request.phase,request.value,request.binding,
        request.bypass,request.settings,true)
    state.result = {ok,result,reason}
    if ok and result ~= false then state.activated = true end
    if not ok or result == false and not state.activated then
        backend.native_move_input_results=backend.native_move_input_results or {}
        backend.native_move_input_results[state.key]=state.result
        release(backend,state)
    elseif state.once then state.releasing = true end
    return ok,result,reason
end

local function finish_ready(backend,state)
    if not state.once and state.release_ready_seen then release(backend,state); return true end
    local status = Moves.native_release_state(backend,state.target,not state.once)
    if status ~= "waiting" then release(backend,state); return true end
end

local function resolve(backend,target)
    if not target.native_hold_variants then return target end
    local selected={}
    for name,value in pairs(target) do selected[name]=value end
    selected.native_hold_variants=nil
    local component=backend:receiver({receiver="ability_system"})
    if not component or not component:IsValid() then return selected end
    local granted={}
    Selection.each(backend,target,function(_,ability)
        granted[ability:GetClass():GetFullName():gsub("^%S+ ","")]=true
    end)
    for _,variant in ipairs(target.native_hold_variants) do
        local matches=granted[variant.abilityClass] == true
        for _,tag in ipairs(variant.requiredTags) do
            if backend:invoke(component,"/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayTagCount",
                {TagName=FName(tag)})<=0 then matches=false;break end
        end
        if matches then
            selected.native_hold_inputs=variant.nativeHoldInputs
            selected.native_release_when=variant.automaticReleaseWhen
            selected.native_preparation_class=variant.abilityClass
            return selected
        end
    end
    return selected
end

function Inputs.prepare(backend, requests)
    backend.native_move_input_frame = (backend.native_move_input_frame or 0)+1
    backend.native_move_input_results={}
    local held = {}
    for _, request in ipairs(requests) do held[key(request.target,request.binding)] = true end
    local states = {}
    for _, state in pairs(backend.native_move_inputs or {}) do states[#states+1] = state end
    for _, state in ipairs(states) do
        if not held[state.key] and not state.once then
            if not state.activated then release(backend,state) else state.releasing = true; state.pending = nil end
        end
        if backend.native_move_inputs[state.key] then
            if state.activated and not state.once and not state.release_ready_seen then
                state.release_ready_seen = Moves.native_release_state(backend,state.target,true)=="ready"
            end
            if state.releasing then finish_ready(backend,state)
            elseif state.pending and ready(backend,state) then
                execute(backend,state)
                if state.releasing then finish_ready(backend,state) end
            end
        end
    end
end

function Inputs.dispatch(backend,target,phase,value,binding,bypass,settings)
    backend.native_move_inputs = backend.native_move_inputs or {}
    local identity = key(target,binding)
    local state = backend.native_move_inputs[identity]
    if phase == "completed" or phase == "canceled" then
        if state then
            if state.activated then state.releasing = true; state.pending = nil; finish_ready(backend,state)
            else release(backend,state) end
        end
        return true
    end
    if phase ~= "started" and phase ~= "triggered" then return true end
    local completed=backend.native_move_input_results and backend.native_move_input_results[identity]
    if completed then return table.unpack(completed) end
    if not state then
        target=resolve(backend,target)
        if not target.native_hold_inputs then
            return backend:dispatch(target,phase,value,binding,bypass,settings,true)
        end
        -- Equipment grants and registers the native input handlers used by IsHolding.
        local prepared,reason = Equipment.prepare(backend,target,settings)
        if not prepared then return true,false,reason end
        local trigger = settings and settings.trigger or "down"
        state = {key=identity,target=target,inputs={},once=trigger == "press" and not (settings and settings.default_trigger)
            or trigger == "tap" or trigger == "release" or trigger == "pulse"
            or trigger == "multi_tap" or trigger == "sequence"}
        backend.native_move_inputs[identity] = state
        local ok,failure = pcall(function()
            for _,path in ipairs(target.native_hold_inputs) do
                local input = {spec={prepare_input=path}}
                assert(Moves.hold_input(backend,input),"Native move input preparation failed: "..path)
                state.inputs[#state.inputs+1] = input
            end
        end)
        if not ok then release(backend,state); error(failure,0) end
    end
    target=state.target
    if state.releasing then return true end
    if state.dispatched_frame == (backend.native_move_input_frame or 0) then return table.unpack(state.result) end
    state.pending = {target=target,phase=phase,value=value,binding=binding,bypass=bypass,settings=settings}
    if ready(backend,state) then
        local ok,result,reason = execute(backend,state)
        if state.releasing then finish_ready(backend,state) end
        return ok,result,reason
    end
    if not state.reported_wait and backend.report and backend.log then
        state.reported_wait = true
        backend:report("native-hold:" .. identity,"Waiting for native Hold input: "
            .. table.concat(target.native_hold_inputs,", ") .. "; target: "
            .. tostring(target.move_id or target.ability_class or target.tag))
    end
    return true
end

function Inputs.reset(backend)
    backend.native_move_input_results=nil
    local states,failures = {},{}
    for _,state in pairs(backend.native_move_inputs or {}) do states[#states+1] = state end
    for _,state in ipairs(states) do
        local ok,failure = pcall(release,backend,state)
        if not ok then failures[#failures+1] = tostring(failure) end
    end
    if #failures > 0 then error(table.concat(failures,"; "),0) end
end

return Inputs
