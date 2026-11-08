package.path = "prototype/input-router/ue4ss/Scripts/?.lua;"..package.path
local checks = 0
local function check(value,message) checks=checks+1;assert(value,message) end
local function array(values)
    return setmetatable({GetArrayNum=function() return #values end,ForEach=function(_,callback)
        for index,value in ipairs(values) do callback(index,{get=function() return value end}) end
    end},{__index=values})
end
function FName(value) return {ToString=function() return value end} end
local classes,nextAddress = {},100
local function object(values)
    nextAddress=nextAddress+1
    local address=nextAddress
    values=values or {}
    function values:IsValid() return true end
    function values:GetAddress() return address end
    function values:GetPropertyValue(name) return self[name] end
    function values:GetFullName() return "Object "..self.path end
    function values:GetClass() return StaticFindObject(self.class) end
    return values
end
function StaticFindObject(path)
    if not classes[path] then classes[path]=object({path=path}) end
    return classes[path]
end
-- Native resource scaling and equipment mutation have separate integration
-- checks. Keep their call boundaries while exercising the real dispatcher,
-- phase executor and native effect ownership with compiled binding settings.
package.loaded["resource-costs"]={
    tuned=function() return false end,
    with=function(backend,target,bypass,callback,settings)
        check(settings and settings.damage_multiplier and settings.startup_speed==1.25
            and settings.recovery_speed==1.75 and bypass==settings.bypass_resources,
            "Compiled settings must reach normal activation independently of native preparation")
        return callback()
    end,
    with_active=function(_,_,_,callback) return callback() end
}
package.loaded["move-equipment"]={prepare=function() return true end}
package.loaded["snake-preparation"]={prepare=function() end,abort=function() end}
local Backend,Preparation,Moves=require("backend"),require("move-preparation"),require("complete-moves")
local config=assert(loadfile(os.getenv("PBZ_NATIVE_STATE_CONFIG") or (...) or "dist/native-state-preparation-check/native-state-effects.lua"))()
local function fixture(target,binding,rejected,wrongWeapon,effectIndex)
    local effect=target.native_state_effects[effectIndex or 1]
    local tags=wrongWeapon and {UnrelatedWeapon=true} or {[effect.equipmentTag]=true}
    local preparationTags={}
    for _,candidate in ipairs(target.native_state_effects) do
        preparationTags[candidate.equipmentTag]=true
        preparationTags[candidate.qualificationTag]=true
    end
    local lowerDefinitions={}
    for _,path in ipairs(effect.replacedEffectClasses or {}) do
        local cdo=object({path=path..":OriginalDefault"})
        local lowerClass=StaticFindObject(path)
        function lowerClass:GetCDO() return cdo end
        lowerDefinitions[#lowerDefinitions+1]=cdo
    end
    local scopeObjects={}
    for _,scope in ipairs(effect.requirementScopes or {}) do
        local component=object({path=scope.effectClass..":NativeRequirements"})
        function component:IsA(class)
            return class==StaticFindObject("/Script/GameplayAbilities.TargetTagRequirementsGameplayEffectComponent")
        end
        local unrelated=object()
        function unrelated:IsA() return false end
        local cdo=object({GEComponents=array({unrelated,component})})
        local class=StaticFindObject(scope.effectClass)
        function class:GetCDO() return cdo end
        scopeObjects[#scopeObjects+1]=component
    end
    if effect.replacedEffectClasses then tags["CharacterState.AbilityState.Long.Buff.Lv2"]=true end
    local class=target.ability_class or "/Fixture/SharedAbility.SharedAbility_C"
    local abilityTags={GameplayTags=array({{TagName=FName(target.tag)}})}
    local instance=object({class=class,bIsActive=false,["Current Shachi Reinforced"]=true,AbilityTags=abilityTags})
    local cdo=object({class=class,AbilityTags=abilityTags})
    local spec={Ability=cdo,ActiveCount=0,NonReplicatedInstances=array({instance}),ReplicatedInstances=array({})}
    local asc=object({ActivatableAbilities={Items=array({spec})}})
    local input,receiver=object(),object()
    local timeline=object({path=target.entry_when and target.entry_when.timeline or "Main"})
    local task=object({Timeline=timeline,CurrentTime=0})
    local state={applied=0,removed=0,activated=0,entries=0,captured=0,restored=0}
    local backend
    local context={}
    function context:apply_effect_self(component,class,level,scopes)
        check(component==asc and class==StaticFindObject(effect.effectClass) and level==1,
            "Compiled native effect must reach the original class and player ASC")
        if effect.requirementScopes then
            check(scopes and #scopes==#effect.requirementScopes,"Long preparation must resolve its original native application component")
            for index,scope in ipairs(effect.requirementScopes) do
                check(scopes[index].object==scopeObjects[index] and scopes[index].application==scope.application
                    and scopes[index].removal==scope.removal,"Requirement scope must retain the compiled original component and selected fields")
            end
            check(scopes[1].application and not scopes[1].removal,
                "Long must preserve native Lv3 weapon removal")
        else check(scopes==nil,"Ordinary prepared effects must retain all their native tag requirements") end
        if effect.replacedEffectClasses then
            check(state.captured==1 and not tags["CharacterState.AbilityState.Long.Buff.Lv2"],
                "Long must remove the saved lower tier before applying Lv3 to prevent excess damage")
        end
        state.applied=state.applied+1;tags[effect.qualificationTag]=true
        return state.applied
    end
    function context:remove_effect(component,handle)
        check(component==asc and handle==1,"Cleanup must remove only the tool's original native handle")
        state.removed=state.removed+1;tags[effect.qualificationTag]=nil
        return true
    end
    function context:capture_long_buffs(component,definitions)
        check(component==asc and #definitions==#lowerDefinitions,"Long capture must select both actual lower-tier default objects")
        for index,definition in ipairs(definitions) do check(definition==lowerDefinitions[index],"Long capture must preserve exact native class ownership") end
        check(tags["CharacterState.AbilityState.Long.Buff.Lv2"],"Capturing original effects must not modify them")
        state.captured=state.captured+1
        state.token={level=4,remaining=7.5,context={source="original"}}
        return state.token
    end
    function context:remove_long_buffs(component,token)
        check(component==asc and token==state.token,"Only the captured original effects may be removed")
        local retained=false
        for _,saved in pairs(backend.move_preparation or {}) do
            for _,value in pairs(saved) do if value==token then retained=true end end
        end
        check(retained,"The native restore token must be retained before original effects are removed")
        tags["CharacterState.AbilityState.Long.Buff.Lv2"]=nil
    end
    function context:restore_long_buffs(component,token)
        check(component==asc and token==state.token and not tags[effect.qualificationTag],
            "Long must remove the added Lv3 before restoring its original tier")
        check(token.level==4 and token.remaining==7.5 and token.context.source=="original",
            "The restore token must preserve native level, context and remaining time")
        tags["CharacterState.AbilityState.Long.Buff.Lv2"]=true
        state.restored=state.restored+1
    end
    function context:discard_long_buffs(token) check(token==state.token,"Only an owned native restore token may be discarded") end
    backend=setmetatable({config=config,state={ready=true},missing={},log=function() end,
        pawn=object(),resource_costs={context=context,instances={}}},Backend)
    function backend:object(path) return StaticFindObject(path) end
    function backend:receiver(request) return request.receiver=="ability_system" and asc or receiver end
    function backend:argument(value) return FName(value.fname) end
    function backend:invoke(value,path,a,b,c,d)
        if path:match(":GetGameplayTagCount$") then
            check(value==asc,"Preparation must query the actual player's ASC")
            local tag=a.TagName:ToString()
            return tags[tag] and 1 or preparationTags[tag] and 0 or 1
        elseif path:match(":TryActivateExternalActionAbilityByClass$")
            or path:match(":TryActivateExternalActionAbilityInterruptLowerByTag$") then
            check(value==receiver and (path:match("ByClass$") and a==StaticFindObject(target.ability_class)
                or path:match("ByTag$") and a.TagName:ToString()==target.tag)
                and b==StaticFindObject(target.input_action),"Native activation must retain its compiled exact class and input")
            check(wrongWeapon and not tags[effect.qualificationTag] and state.applied==0
                or not wrongWeapon and tags[effect.qualificationTag] and state.applied==1,
                "Native state preparation must match this equipped weapon before activation")
            state.activated=state.activated+1
            if rejected then return false end
            spec.ActiveCount,instance.bIsActive=1,true
            return true
        elseif path:match(":GetPlayTimelineTask$") then return task
        elseif path:match(":GetTimelineByName$") then return timeline
        elseif path==target.entry_point then
            check(value==instance and tags[effect.qualificationTag],"Native phase must retain the prepared full effect")
            state.entries=state.entries+1;task.CurrentTime=d
            return
        end
        error("Unexpected native invocation: "..path)
    end
    return backend,state,spec,instance,task,tags
end
local devices,targets={},{}
for _,binding in ipairs(config.bindings) do
    local output=binding.outputs[1]
    local target=config.targets[type(output)=="table" and output.target or output]
    if target.native_state_effects then
        local device=binding.keys[1]:find("Gamepad_",1,true) and "gamepad" or "keyboard"
        local identity=target.move_id or "ability:"..target.tag
        devices[device]=true;targets[identity]=true
        local backend,state,spec,instance,task,tags=fixture(target,binding)
        local ok,result,reason=backend:dispatch(target,"triggered",nil,binding.id,binding.bypass_resources,binding)
        check(ok and result~=false,"Compiled native preparation failed on "..device..": "..identity.." / "..tostring(result).." / "..tostring(reason))
        check(state.applied==1 and state.activated==1 and state.removed==0,
            "Accepted native activation must retain the full effect on "..device)
        if target.entry_point then
            task.CurrentTime=target.entry_when.minimum_time
            Moves.update(backend)
            check(state.entries==1,"Prepared YuuBlade C must enter its original Exit timeline")
        end
        Preparation.update(backend)
        check(state.removed==0,"The compiled active attack must retain the native effect")
        spec.ActiveCount,instance.bIsActive=0,false
        Preparation.update(backend)
        check(state.removed==1,"Attack completion must remove its added native effect")
        if target.native_state_effects[1].replacedEffectClasses then
            check(tags["CharacterState.AbilityState.Long.Buff.Lv2"] and state.restored==1,
                "Native effect cleanup must restore the player's original Lv2 state")
        end
        backend,state=fixture(target,binding,true)
        ok,result=backend:dispatch(target,"triggered",nil,binding.id,binding.bypass_resources,binding)
        check(ok and result==false and state.applied==1 and state.removed==1,
            "The real dispatcher must roll back a refused native activation on "..device)
        if target.kind=="ability" then
            backend,state=fixture(target,binding,false,true)
            ok,result=backend:dispatch(target,"triggered",nil,binding.id,binding.bypass_resources,binding)
            check(ok and result~=false and state.activated==1 and state.applied==0 and state.removed==0,
                "A shared ability must retain native routing without another weapon's buff")
        end
        for index=2,#target.native_state_effects do
            if target.native_state_effects[index].equipmentTag~=target.native_state_effects[1].equipmentTag then
                backend,state,spec,instance=fixture(target,binding,false,false,index)
                ok,result=backend:dispatch(target,"triggered",nil,binding.id,binding.bypass_resources,binding)
                check(ok and result~=false and state.applied==1 and state.activated==1,
                    "A shared tag must select only the original effect for its currently equipped alternative")
                spec.ActiveCount,instance.bIsActive=0,false
                Preparation.update(backend)
                check(state.removed==1,"Alternative weapon effects must retain separate native handle ownership")
            end
        end
    end
end
local count=0;for _ in pairs(targets) do count=count+1 end
local expected,identities=0,{}
for _,target in pairs(config.targets) do
    local identity=target.move_id or "ability:"..target.tag
    if target.native_state_effects and not identities[identity] then
        identities[identity]=true;expected=expected+1
    end
end
check(devices.keyboard and devices.gamepad and count==expected and count>=9,
    "Compiled save/share fixture must cover both devices and every native state attack")
print("CompiledNativeStatePreparationTests PASS "..checks)
