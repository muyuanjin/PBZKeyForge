package.path="prototype/input-router/ue4ss/Scripts/?.lua;"..package.path
local checks,hooks,current,damage=0,{},nil,{}
local function check(value,message) checks=checks+1;assert(value,message) end
local function parameter(value)
    return {get=function() return value end,set=function(_,nextValue) value=nextValue end}
end
local function array(values)
    return setmetatable({GetArrayNum=function() return #values end,ForEach=function(_,callback)
        for index,value in ipairs(values) do callback(index,parameter(value)) end
    end},{__index=values})
end
function FName(value) return {ToString=function() return value end} end
local classes,nextAddress={},100
local function object(values)
    nextAddress=nextAddress+1
    local address=nextAddress
    values=values or {}
    function values:IsValid() return self.valid~=false end
    function values:GetAddress() return address end
    function values:GetPropertyValue(name) return self[name] end
    function values:SetPropertyValue(name,value)
        local tags={}
        for _,tag in ipairs(value.GameplayTags) do tags[#tags+1]=tag end
        self[name]={GameplayTags=array(tags)}
    end
    function values:GetClass() return StaticFindObject(self.class) end
    function values:GetFullName() return "Object "..(self.path or self.class or tostring(address)) end
    function values:IsA() return true end
    return values
end
function StaticFindObject(path)
    if not classes[path] then classes[path]=object({path=path}) end
    return classes[path]
end
function RegisterHook(path,pre,post) hooks[path]={pre=pre,post=post};return 1,2 end
function UnregisterHook(path) hooks[path]=nil end
package.loaded["script-context"]={new=function() return {
    enable_damage=function() end,
    interface_hook=function(_,_,path,pre,post)
        return RegisterHook(path,function(context,...) return pre(context:get(),...) end,post)
    end,
    owner=function(_,states) return current and states[current:GetAddress()],"Function /Fixture:NativeCombatArt" end,
    at=function() return "Object /Fixture",current and current:GetAddress(),"Function /Fixture:NativeCombatArt" end,
    ability_damage=function(_,instance,multiplier)
        damage[type(instance)=="number" and instance or instance:GetAddress()]=multiplier
    end,
    damage_bullet=function() end,update_damage=function() end,charge_rate=function() end,reset=function() end
} end}
local Moves=require("complete-moves")
local config=assert(loadfile(os.getenv("PBZ_INDEPENDENT_PHASE_CONFIG")
    or "dist/restrictions-final-check/independent-phases.lua"))()
local play="/Script/LegoAbility.LegoAbility:PlayTimeline"
local byName="/Script/LegoAbility.LegoAbility:PlayTimelineByName"
local modify="/Script/SGameAttributes.AttributeUtils:ModifyCharacterAttributeValue"
local read="/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayAttributeValue"
local wanted={
    ["move:WeaponPhase.GA_Hun_Jigsaw_C.HasSawHitStage2"]=true,
    ["move:WeaponPhase.GA_Hun_Jigsaw_C.HasSawHitFinal"]=true,
    ["move:WeaponPhase.GA_Hun_ZhanMa_C.NormalFinal"]=true
}
local targets={}
for _,target in pairs(config.targets) do
    if wanted[target.move_id] then targets[#targets+1]=target end
end
local function fixture(target,options)
    options=options or {}
    classes,hooks,current,damage={},{},nil,{}
    local empty={GameplayTags=array({})}
    local instance=object({class=target.ability_class,bIsActive=false,
        ["Current Shachi Reinforced"]=true,ActivationRequiredTags=empty,ActivationBlockedTags=empty})
    local cdo=object({class=target.ability_class,ActivationRequiredTags=empty,ActivationBlockedTags=empty})
    local spec={Ability=cdo,ActiveCount=0,NonReplicatedInstances=array({instance}),ReplicatedInstances=array({})}
    local asc=object({ActivatableAbilities={Items=array({spec})}})
    local receiver,pawn,input,owner=object(),object(),object(),object()
    local timelines={Main=object({bAutoPlay=true}),HasSawHit=object({bAutoPlay=true}),
        ExecutionTimeline=object({bAutoPlay=true}),Failed=object({bAutoPlay=true})}
    local backend={pawn=pawn,config=config,activations=0,preparations=0,entries=0,cancellations=0,
        task=object({Timeline=timelines.Main,CurrentTime=0,rate=1}),instance=instance,spec=spec,timelines=timelines,
        tags={},feeTotal=0}
    for _,list in ipairs({target.required_tags or {},target.activation_required_tags or {},target.activation_any_tags or {}}) do
        for _,tag in ipairs(list) do backend.tags[tag]=1 end
    end
    function backend:receiver(value) return value.receiver=="ability_input" and receiver or asc end
    function backend:object(path) return path==target.input_action and input or StaticFindObject(path) end
    function backend:argument(value) return FName(value.fname) end
    function backend:report() end
    function backend:fail_target(_,message) error(message) end
    local function feeAndEligibility()
        local fee=parameter(-1)
        local write=hooks[modify]
        if write then write.pre(nil,parameter(pawn),parameter({AttributeName="UltimateChargeCount"}),parameter(0),fee) end
        local remaining=parameter(2)
        local query=hooks[read]
        if query then query.post(parameter(asc),remaining,
            parameter({AttributeName="UltimateChargeCount",AttributeOwner=owner})) end
        return fee:get(),remaining:get()
    end
    function backend:invoke(value,path,...)
        local a,b,c,d,e=...
        check(not path:find("ForceActivate",1,true),"Independent stages must never request force activation")
        if path:find(":GetGameplayTagCount",1,true) then return self.tags[a.TagName:ToString()] or 0
        elseif path:find(":TryActivateExternalActionAbilityByClass",1,true) then
            check(value==receiver and a==StaticFindObject(target.native_activation.abilityClass)
                and b==input and c==target.native_activation.priority,"Normal initialization must use its compiled class, input and priority")
            check(not instance.bIsActive and spec.ActiveCount==0,"Fallback activation starts from the inactive granted instance")
            self.activations=self.activations+1
            current=instance
            local fee,remaining=feeAndEligibility()
            check(fee==(options.bypass and 0 or -1) and remaining==(options.bypass and 5 or 2),
                "Native initialization retains its fee and eligibility unless this binding selects resource bypass")
            current=nil
            if options.refused then return false end
            self.feeTotal=self.feeTotal+fee
            instance.initialized=true
            spec.ActiveCount,instance.bIsActive=1,true
            self.task=object({Timeline=target.native_preparation_entry and timelines.Main or timelines.ExecutionTimeline,
                CurrentTime=0,rate=1})
            if hooks[play] then hooks[play].pre(parameter(instance),parameter(self.task.Timeline),parameter(1)) end
            return true
        elseif path:find(":GetAttributeSet",1,true) then return object({UltimateChargeCount_Max={CurrentValue=5}})
        elseif path:find(":GetPlayTimelineTask",1,true) then return self.task
        elseif path:find(":GetTimelineByName",1,true) then return timelines[a:ToString()]
        elseif path:find(":SetPlayRate",1,true) then value.rate=a;return
        elseif path==byName then
            check(select("#",...)==4 and value==instance and a:ToString()=="HasSawHit"
                and b==1 and c==0 and d==0,"Branch preparation must pass all four native PlayTimelineByName arguments")
            check(instance.initialized and self.task.Timeline==timelines.Main and self.task.CurrentTime>=.001,
                "HasSawHit preparation must follow real Main initialization")
            self.preparations=self.preparations+1
            if hooks[byName] then hooks[byName].pre(parameter(instance),parameter(a),parameter(b)) end
            self.task=object({Timeline=timelines.HasSawHit,CurrentTime=0,rate=1})
        elseif path==play then
            check(select("#",...)==5 and value==instance and a==self.task.Timeline and b==1 and c==0
                and d==target.entry_arguments[4] and e:ToString()=="None","Final phase must pass the current native timeline and five typed arguments")
            check(instance.initialized and self.task.CurrentTime>=.001,"Final phase must retain native initialization and branch progress")
            check(damage[instance:GetAddress()]==4,"Final phase must retain this binding's damage setting")
            if target.native_preparation_entry then
                check(self.task.Timeline==timelines.HasSawHit,"Saw-hit attacks must use the prepared native branch")
            else check(self.task.Timeline==timelines.ExecutionTimeline,"Normal execution finisher must retain its native ExecutionTimeline") end
            self.entries=self.entries+1
            if hooks[play] then hooks[play].pre(parameter(instance),parameter(a),parameter(b)) end
            self.task.CurrentTime=d
        elseif path:find(":K2_CancelAbility",1,true) then
            self.cancellations=self.cancellations+1
            spec.ActiveCount,instance.bIsActive=0,false
        else error("Unexpected independent phase call: "..path) end
    end
    function backend:naturalBranch()
        instance.initialized,instance.bIsActive,spec.ActiveCount=true,true,1
        self.task.Timeline=timelines[target.entry_when.timeline]
        self.task.CurrentTime=target.entry_when.minimum_time
    end
    function backend:cleanup()
        Moves.reset(self)
        if self.resource_costs then self.resource_costs:reset() end
    end
    return backend
end
local function settings(predecessor)
    return {restriction_bypasses=predecessor and {"predecessor"} or {},
        startup_speed=2,recovery_speed=3,damage_multiplier=4}
end
local function run(target)
    check(target.active_ability_classes and target.native_activation and target.native_activation.activateByClass
        and not target.force,"Compiled independent recipes retain active entry and a separate normal activation")
    check(target.entry_when.minimum_time==.001,"Compiled phase waits for native timeline initialization")
    for _,selection in ipairs({{}, {"equipment"}, {"training"}, {"space"}, {"stance"}}) do
        local backend=fixture(target)
        local policy=settings(false);policy.restriction_bypasses=selection
        check(not Moves.dispatch(backend,target,"started",false,policy),"Unchecked predecessor must retain the original active-only entry")
        check(backend.activations==0 and backend.preparations==0 and backend.entries==0,"Unrelated restriction choices must not initialize an active-only branch")
        backend:cleanup()
    end
    for _,bypass in ipairs({false,true}) do
        local backend=fixture(target,{bypass=bypass})
        check(Moves.dispatch(backend,target,"started",bypass,settings(true),"independent"),"Selected predecessor must normally initialize an independent stage")
        check(backend.activations==1 and backend.instance.initialized and backend.entries==0
            and backend.preparations==0,"Normal initialization precedes every branch and phase entry")
        backend.task.CurrentTime=.0009;Moves.update(backend)
        check(backend.entries==0 and backend.preparations==0,"Native initialization threshold cannot be skipped")
        backend.task.CurrentTime=.001;Moves.update(backend)
        if target.native_preparation_entry then
            check(backend.preparations==1 and backend.entries==0 and backend.task.CurrentTime==0,
                "Starting HasSawHit must wait for its own first native tick")
            backend.task.CurrentTime=.0009;Moves.update(backend)
            check(backend.entries==0 and backend.preparations==1,"Prepared branch must not reenter or jump before initialization")
            backend.task.CurrentTime=.001;Moves.update(backend)
        end
        check(backend.entries==1 and backend.activations==1 and backend.feeTotal==(bypass and 0 or -1),
            "Exactly one normal activation and its own fee enter the requested phase")
        local costs=assert(backend.resource_costs)
        local state=assert(costs.instances[backend.instance:GetAddress()])
        check(state.bypass==bypass and state.damage==4 and state.startup==2 and state.recovery==3,
            "Normal preparation and final phase share binding resource, damage and speed settings")
        local window=target.phase_window
        backend.instance["Action Phase"]={TagName=FName("CharacterState.Action.Phase.Act")}
        costs:update()
        check(backend.task.rate==(window.entry_time<window.attack_start and 2 or 1),"Final phase uses its own retained startup boundary")
        backend.task.CurrentTime=window.attack_end;costs:update()
        check(backend.task.rate==3,"Final phase retains its independent recovery multiplier")
        Moves.update(backend)
        check(backend.entries==1 and backend.preparations==(target.native_preparation_entry and 1 or 0),
            "Completed preparation cannot replay its branch or final attack")
        backend.instance.bIsActive,backend.spec.ActiveCount=false,0;costs:update()
        check(backend.task.rate==1 and next(costs.instances)==nil,"Natural completion restores this binding's tuning")
        backend:cleanup()
    end
    for _,predecessor in ipairs({false,true}) do
        local backend=fixture(target)
        backend:naturalBranch()
        check(Moves.dispatch(backend,target,"started",false,settings(predecessor)),"The original qualified active branch remains available")
        check(backend.activations==0 and backend.preparations==0 and backend.entries==1,
            "An existing native branch must not be reactivated or prepared again")
        backend:cleanup()
    end
    local refused=fixture(target,{refused=true})
    check(not Moves.dispatch(refused,target,"started",false,settings(true)),"Normal native activation refusal must remain effective")
    refused.resource_costs:update()
    check(refused.activations==1 and refused.entries==0 and refused.preparations==0 and not refused.instance.initialized
        and next(refused.resource_costs.instances)==nil,"Rejected initialization must retain no phase or tuning")
    refused:cleanup()
    local reset=fixture(target)
    check(Moves.dispatch(reset,target,"started",false,settings(true)),"Reset case must start normal initialization")
    reset:cleanup()
    check(reset.cancellations==1 and reset.entries==0 and reset.preparations==0,"Reset cancels waiting native initialization")
    local aborted=fixture(target)
    check(Moves.dispatch(aborted,target,"started",false,settings(true),"abort"),"Abort case must start normal initialization")
    Moves.abort(aborted,target,"abort")
    check(aborted.cancellations==1 and not aborted.instance.bIsActive,
        "Aborting the original binding must cancel its prepared clone and native initialization")
    aborted:cleanup()
    local unqualified=fixture(target)
    check(Moves.dispatch(unqualified,target,"started",false,settings(true)),"Qualification-loss case must start normal initialization")
    unqualified.instance["Current Shachi Reinforced"]=false
    unqualified.task.CurrentTime=.001;Moves.update(unqualified)
    check(unqualified.cancellations==1 and unqualified.entries==0 and unqualified.preparations==0,
        "Lost native resource qualification must cancel before branch preparation or attack")
    unqualified:cleanup()
end
local count,covered=0,{}
for _,target in ipairs(targets) do
    run(target);count=count+1;covered[target.move_id]=true
end
for identity in pairs(wanted) do
    check(covered[identity],"Missing freshly compiled independent phase: "..identity)
end
check(count>=3,"The independent preparation sweep must cover all three native activation recipes")
local external=0
for _,target in pairs(config.targets) do
    if target.active_ability_classes and not target.native_activation and target.ability_class then
        external=external+1
        local backend=fixture(target)
        check(not Moves.dispatch(backend,target,"started",false,settings(true)),"Real external-context phases must remain active-only")
        check(backend.activations==0 and backend.preparations==0 and backend.entries==0,"Predecessor selection must not invent an external target")
        backend:cleanup()
    end
end
check(external>0,"The compiled fixture must include real external-context phases to retain their active-only qualification")
print("IndependentPhasePreparationTests PASS "..checks..": "..count
    .." compiled native preparations, branch waits, tuning and "..external.." external-context phases")
