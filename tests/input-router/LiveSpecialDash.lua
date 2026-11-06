-- Run on the game thread with a backend configured for dynamic capture.
return function(backend, log)
    assert(backend:refresh(), "Enter a controllable character scene first")
    local asc = backend:receiver({receiver="ability_system"})
    local attributes = backend:invoke(asc, "/Script/GameplayAbilities.AbilitySystemComponent:GetAttributeSet",
        backend:object("/Script/CharacterBasics.BasicAttributeSet"))
    local library = backend:library("SGameUtils.AbilityUtils")
    local talent = "CharacterState.Talent.EnableGhostStep"
    local function tag_count(tag)
        return backend:invoke(asc, "/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayTagCount",
            {TagName=FName(tag)})
    end
    local addedTalent = tag_count(talent) == 0
    if addedTalent then
        backend:invoke(library, "/Script/SGameUtils.AbilityUtils:AddLooseGameplayTagForSystem",
            asc, {TagName=FName(talent)}, false)
        assert(tag_count(talent) > 0, "Cannot provide transient test eligibility")
    end
    log("ELIGIBILITY transientGhostStep=" .. tostring(addedTalent))
    local targets = {
        stance={kind="action",path="/PBZDeflectSystem/Input/IA_Stance.IA_Stance"},
        deflect={kind="action",path="/PBZDeflectSystem/Input/IA_Deflect.IA_Deflect"},
        dodge={kind="action",path="/PBZDeflectSystem/Input/IA_Dodge.IA_Dodge"},
        jump={kind="action",path="/PBZInput/InputActions/IA_Jump.IA_Jump"}
    }
    local scenarios = {
        {name="normal-jump",action="jump"},
        {name="normal-dodge",action="dodge"},
        {name="old-guard-dodge",guard=true,action="dodge"},
        {name="new-guard-jump",guard=true,action="jump"}
    }
    local function cleanup()
        for _, target in pairs(targets) do backend:dispatch(target,"completed",{x=0},"cleanup") end
        backend:shutdown()
        if addedTalent then
            backend:invoke(library, "/Script/SGameUtils.AbilityUtils:RemoveLooseGameplayTagForSystem",
                asc, {TagName=FName(talent)}, false)
        end
        log("CLEANUP ghostStep=" .. tag_count(talent) .. " native contexts restored")
    end
    local index, frame, result = 1, 0, nil
    local function tick()
        local ok, err = pcall(function()
            local scenario = scenarios[index]
            frame = frame + 1
            local position = backend.pawn:K2_GetActorLocation()
            local energy = attributes:GetPropertyValue("Shachi").CurrentValue
            if frame == 1 then
                result = {name=scenario.name,start=position,energy=energy,minEnergy=energy,
                    height=0,distance=0,special=0,jump=0,dodge=0}
            end
            if frame == 34 and scenario.guard then
                log("CONDITION " .. scenario.name .. " holding=" ..
                    tag_count("CharacterState.AbilityState.Stance.HoldingStanceKey") ..
                    " ground=" .. tag_count("CharacterState.Space.Ground") .. " talent=" .. tag_count(talent))
            end
            for name, target in pairs(targets) do
                local active = name == "stance" and scenario.guard and frame <= 75
                    or name == "deflect" and scenario.guard and frame == 1
                    or name == scenario.action and frame == 35
                backend:dispatch(target,active and "triggered" or "completed",{x=active and 1 or 0},scenario.name)
            end
            result.minEnergy = math.min(result.minEnergy,energy)
            result.height = math.max(result.height,position.Z-result.start.Z)
            result.distance = math.max(result.distance,
                math.sqrt((position.X-result.start.X)^2+(position.Y-result.start.Y)^2))
            for field, tag in pairs({special="Ability.Action.Hun.SpecialDash",
                jump="Ability.Action.Hun.Jump",dodge="Ability.Action.Hun.Dash"}) do
                result[field] = math.max(result[field],tag_count(tag))
            end
            asc.ActivatableAbilities.Items:ForEach(function(_, parameter)
                local spec = parameter:get()
                if spec.ActiveCount > 0 and spec.Ability:GetFullName():find("SpecialDash",1,true) then result.special=1 end
            end)
            if frame == 180 then
                log(string.format("SCENARIO %s cost=%.3f height=%.3f distance=%.3f special=%d jump=%d dodge=%d",
                    result.name,result.energy-result.minEnergy,result.height,result.distance,
                    result.special,result.jump,result.dodge))
                scenario.result = result
                index, frame = index+1, 0
            end
        end)
        if not ok then cleanup(); error(err,0) end
        if scenarios[index] then ExecuteInGameThreadAfterFrames(1,tick)
        else
            cleanup()
            assert(scenarios[1].result.height > 10,"Normal jump failed")
            assert(scenarios[2].result.dodge > 0,"Normal dodge failed")
            assert(scenarios[3].result.special == 0,"Old chord still activated SpecialDash")
            assert(scenarios[4].result.special > 0,"New chord did not activate SpecialDash")
            log("PASS normal jump/dodge, old chord blocked, new chord activated")
        end
    end
    ExecuteInGameThreadAfterFrames(1,tick)
end
