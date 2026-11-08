local StanceInput = {}
local stancePath = "/PBZDeflectSystem/Input/IA_Stance"
local passivePath = "/PBZDeflectSystem/Abilities/BP_PlayerDeflectPassive.BP_PlayerDeflectPassive_C"
local framePath = "/Script/Engine.KismetSystemLibrary:GetFrameCount"
local vectorPath = "/Script/Engine.PlayerController:GetInputVectorKeyState"
local holdTag = "CharacterState.AbilityState.Stance.HoldingStanceKey"
local tag = {TagName=nil}

local function valid(object) return object and object:IsValid() end
local function load(backend,name)
    local source = debug.getinfo(1,"S").source:sub(2):match("^(.*[/\\])")
    local path = source:gsub("Scripts[/\\]$", "") .. "lib/pbzability-context.dll"
    return assert(package.loadlib(path,name))
end

local function passive(backend,owner)
    for _,ability in ipairs(FindAllOf("BP_PlayerDeflectPassive_C") or {}) do
        if valid(ability) then
            local component = backend:invoke(ability,
                "/Script/GameplayAbilities.GameplayAbility:GetAbilitySystemComponentFromActorInfo")
            if valid(component) and component:GetAddress() == owner:GetAddress() then return ability end
        end
    end
    error("Local deflect passive unavailable",0)
end

local function initialise(backend)
    local owner = backend:receiver({receiver="ability_system"})
    assert(valid(owner),"Local ability system unavailable")
    local ability = passive(backend,owner)
    local abilityLibrary = backend:library("SGameUtils.AbilityUtils")
    local frameLibrary = backend:library("Engine.KismetSystemLibrary")
    tag.TagName = FName(holdTag)
    return {
        owner=owner, passive=ability,
        prepare=load(backend,"pbz_stance_prepare"), reset=load(backend,"pbz_stance_restore"),
        watch_trigger=load(backend,"pbz_input_watch_trigger"),
        trigger_state=load(backend,"pbz_input_trigger_state"),
        count=backend:invoke(owner,
            "/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayTagCount",tag),
        break_forbid=passivePath .. ":Break Forbid Stance",
        ability_library=abilityLibrary:GetAddress(),
        add=backend:object("/Script/SGameUtils.AbilityUtils:AddLooseGameplayTagForSystem"):GetAddress(),
        remove=backend:object("/Script/SGameUtils.AbilityUtils:RemoveLooseGameplayTagForSystem"):GetAddress(),
        down=backend:object(passivePath .. ":On Stance Key Down"):GetAddress(),
        release=backend:object(passivePath .. ":On Stance Key Release"):GetAddress(),
        trigger_update=backend:object("/Script/EnhancedInput.InputTrigger:UpdateState"):GetAddress(),
        trigger_type=backend:object("/Script/EnhancedInput.InputTrigger:GetTriggerType"):GetAddress(),
        frame_library=frameLibrary, frame_library_address=frameLibrary:GetAddress(),
        frame_function=backend:object(framePath):GetAddress(),
        trigger_addresses={}, key_names={}, key_values={}
    }
end

local function stance_sources(backend,state)
    local coverage = backend.native_input and backend.native_input.coverage or {}
    if state.coverage ~= coverage or state.coverage_count ~= #coverage then
        state.coverage,state.coverage_count = coverage,#coverage
        state.sources,state.trigger_addresses = {}, {}
        for _,entry in ipairs(coverage) do
            if entry.action == stancePath then state.sources[#state.sources+1]=entry end
        end
    end
    return state.sources
end

local function native_held(backend,state)
    local controller = backend.controller
    local frame,playerInput
    for _,entry in ipairs(stance_sources(backend,state)) do
        local key = entry.active_key
        if (entry.owner == "game" or entry.owner == "uncovered") and key and key ~= "None" then
            if #entry.triggers == 0 then
                local valueKey = state.key_values[key]
                if not valueKey then
                    local name = state.key_names[key]
                    if not name then name=FName(key);state.key_names[key]=name end
                    valueKey={KeyName=name};state.key_values[key]=valueKey
                end
                local value = backend:invoke(controller,vectorPath,valueKey)
                if value.X ~= 0 or value.Y ~= 0 or value.Z ~= 0 then return true end
            else
                frame = frame or backend:invoke(state.frame_library,framePath)
                playerInput = playerInput or controller.PlayerInput
                local playerAddress = playerInput:GetAddress()
                local explicit,triggered,implicit,blocked,evaluated = false,false,true,false,true
                for _,trigger in ipairs(entry.triggers) do
                    local address = state.trigger_addresses[trigger]
                    if not address then address=trigger:GetAddress();state.trigger_addresses[trigger]=address end
                    local value,kind,blocking = state.trigger_state(address,playerAddress,frame)
                    if value == nil then
                        assert(state.watch_trigger(address,state.trigger_update,state.trigger_type,
                            state.frame_library_address,state.frame_function,true) == 1)
                        value,kind,blocking = state.trigger_state(address,playerAddress,frame)
                    end
                    if value == nil then evaluated=false; break end
                    if kind == 0 then explicit,triggered=true,triggered or value == 2
                    elseif kind == 1 then implicit=implicit and value == 2 end
                    blocked = blocked or blocking == 1
                end
                if evaluated and not blocked and implicit and (not explicit or triggered) then return true end
            end
        end
    end
    return false
end

function StanceInput.prepare(backend,inputs)
    if not (backend.dynamic_config and backend.dynamic_config.separate_stance) then
        if backend.stance_input then StanceInput.restore(backend) end
        return
    end
    local state = backend.stance_input
    if state and (not valid(state.owner) or not valid(state.passive)) then
        StanceInput.restore(backend)
        return
    end
    if not state then
        state = initialise(backend)
        state.held = state.count > 0
        backend.stance_input = state
    end
    local held = false
    for _,input in ipairs(inputs) do
        local value = input.value
        if input.target.stance_source and (value.x ~= 0 or value.y ~= 0 or value.z ~= 0) then held=true; break end
    end
    held = held or native_held(backend,state)
    if held and not state.held then
        held = not backend:invoke(state.passive,state.break_forbid)
    end
    if not state.initialized or held ~= state.held then
        local status,failure = state.prepare(state.owner:GetAddress(),state.passive:GetAddress(),
            state.ability_library,state.add,state.remove,state.down,state.release,held,state.count)
        assert(status == 1,failure)
        state.initialized,state.held = true,held
    end
end

function StanceInput.restore(backend)
    local state = backend.stance_input
    if not state then return end
    local owner = valid(state.owner) and valid(state.passive) and state.owner:GetAddress() or 0
    local held = owner ~= 0 and state.passive["Stance Key Down"] == true
    if held and not state.held then
        held = not backend:invoke(state.passive,state.break_forbid)
    end
    local status,failure = state.reset(owner,held)
    assert(status == 1,failure)
    backend.stance_input = nil
end

return StanceInput
