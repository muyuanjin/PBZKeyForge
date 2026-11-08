local scripts = "prototype/input-router/ue4ss/Scripts/"
package.path = scripts .. "?.lua;" .. package.path
local checks, hooks = 0, {}
local function check(value,message) assert(value,message); checks=checks+1 end
local function parameter(value)
    return {get=function() return value end, set=function(_,nextValue) value=nextValue end}
end
local attackClass = {IsValid=function() return true end,GetAddress=function() return 1000 end,
    GetFullName=function() return "Class /Player.Attack" end}
function StaticFindObject(path) return path == "/Player.Attack" and attackClass or nil end
local function object(address, properties)
    local o = properties or {}
    function o:IsValid() return self.valid ~= false end
    function o:GetAddress() return address end
    function o:GetFullName() return "Ability /Player.Attack_"..address end
    function o:GetPropertyValue(name) return self[name] end
    function o:IsA() return not self.nonLego end
    function o:GetClass() return attackClass end
    return o
end
local function array(values)
    return setmetatable({GetArrayNum=function() return #values end,ForEach=function(_,callback) for i,value in ipairs(values) do callback(i,parameter(value)) end end},{__index=values})
end
local current, currentFunction, stopped
function FName(value) return {ToString=function() return value end} end
local nativeAbilities = {}
local emptyClass=object(0,{valid=false})
local function class_parameter(value)
    return {get=function() return value or emptyClass end,value=function() return value end,
        set=function(_,nextValue)
            if nextValue~=nil then
                if nextValue==emptyClass then value=nil else value=nextValue end
            end
        end}
end
package.loaded["script-context"] = {new=function() return {
    empty_class=function() return emptyClass end,
    enable_damage=function() end,
    interface_hook=function(_,_,path,pre,post)
        return RegisterHook(path,function(context,...) return pre(context:get(),...) end,post)
    end,
    owner=function(_,lookup) return current and lookup[current:GetAddress()], currentFunction end,
    at=function() return current and current:GetFullName(),current and current:GetAddress(),currentFunction end,
    damage_bullet=function() error("ordinary initialization must keep its native snapshot") end,
    ability_damage=function(_,instance,multiplier)
        local address=type(instance)=="number" and instance or instance:GetAddress()
        nativeAbilities[address]=multiplier
    end,
    update_damage=function() end,
    reset=function() stopped=true end
} end}
function RegisterHook(path,pre,post) hooks[path]={pre=pre,post=post}; return 1,2 end
function UnregisterHook(path,a,b) check(a==1 and b==2,"hook ids"); hooks[path]=nil end
local Costs = require("resource-costs")
local attack, other, cdo = object(1,{bIsActive=false}),object(2),object(3)
local component = object(4,{ActivatableAbilities={Items=array({{Ability=cdo,ActiveCount=0,NonReplicatedInstances=array({attack}),ReplicatedInstances=array({})}})}})
local pawn = object(5)
local backend={pawn=pawn,receiver=function() return component end}
local attributeOwner=object(6)
local maxima={Shachi_Max={CurrentValue=100},SpiritShadow_Max={CurrentValue=8},UltimateChargeCount_Max={CurrentValue=3}}
local attributes=object(7,maxima)
function backend:invoke(value,path,owner)
    check(value==component and path=="/Script/GameplayAbilities.AbilitySystemComponent:GetAttributeSet"
        and owner==attributeOwner,"resource limits must come from the player's actual attribute set")
    return attributes
end
local reports,reportCalls = {},0
function backend:report(key,message) reports[key]=message;reportCalls=reportCalls+1 end
function backend:object() return object(99) end
local target={ability_class="/Player.Attack"}
local cost = Costs.new(backend)
local modify="/Script/SGameAttributes.AttributeUtils:ModifyCharacterAttributeValue"
local read="/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayAttributeValue"
local libraryRead="/Script/GameplayAbilities.AbilitySystemBlueprintLibrary:GetFloatAttributeFromAbilitySystemComponent"
local applyEffect="/Script/GameplayAbilities.GameplayAbility:BP_ApplyGameplayEffectToOwner"
local function consume(resource,amount,actor,operation)
    local value=parameter(amount)
    hooks[modify].pre(nil,parameter(actor or pawn),parameter({AttributeName={ToString=function() return resource end}}),parameter(operation or 0),value)
    return value:get()
end
local function query(resource,initial,queried,library)
    local result=parameter(initial)
    local attribute=parameter({AttributeName=resource,AttributeOwner=attributeOwner})
    local returned
    if library then returned=hooks[libraryRead].post(nil,result,parameter(queried or component),attribute)
    else returned=hooks[read].post(parameter(queried or component),result,attribute) end
    check(returned==nil,"resource post hooks write the native result slot directly")
    return result:get()
end
local costEffects={
    "/PBZCombatAsset/ActorData/Players/Hun/Weapon/SlitheringSword/UpgradeData/GE_SlitheringSword_EvalutionCombo_CostShachi.GE_SlitheringSword_EvalutionCombo_CostShachi_C",
    "/PBZCombatAsset/ActorData/Players/Hun/Weapon/ChaosBlade/GE_Chaos_SwitchShaQiCost.GE_Chaos_SwitchShaQiCost_C"
}
for index,path in ipairs(costEffects) do
    local effect=object(100+index)
    function effect:GetFullName() return "BlueprintGeneratedClass "..path end
    local function apply(instance,class)
        local parameterClass=class_parameter(class)
        hooks[applyEffect].pre(parameter(instance),parameterClass,parameter(0),parameter(1))
        return parameterClass.value()
    end
    cost:activate(target,true,function()
        check(apply(attack,effect)==nil,"pure resource GameplayEffects are skipped for the owning bypass binding")
        check(apply(other,effect)==effect,"another ability retains its resource GameplayEffect")
        check(not cost.instances[other:GetAddress()],"an unrelated same-class ability cannot acquire the bypass scope")
        local benefit=object(110)
        function benefit:GetFullName() return "BlueprintGeneratedClass /Effects/RestoreShachi.RestoreShachi_C" end
        check(apply(attack,benefit)==benefit,"resource gains and unrelated GameplayEffects remain active")
        check(apply(attack,nil)==nil,"an already absent effect remains absent")
        return false
    end)
    cost:activate(target,false,function()
        check(apply(attack,effect)==effect,"unchecked bindings keep their resource GameplayEffect")
        return false
    end)
    check(apply(attack,effect)==effect,"the same ability's ordinary later operation retains the resource effect")
end
for _,class in ipairs({
    "/PBZCombatAsset/ActorData/Players/Hun/Abilities/HeiMoShang/GA_Hun_HeiMoShang_Reborn.GA_Hun_HeiMoShang_Reborn_C",
    "/PBZCombatAsset/ActorData/Players/Hun/Abilities/ShachiAbility/XuKongHuaShan/GA_XuKongHuaShan_Reborn.GA_XuKongHuaShan_Reborn_C"
}) do
    for _,bypass in ipairs({true,false}) do
        current=attack
        cost:activate(target,bypass,function()
            local original=attackClass.GetFullName
            attackClass.GetFullName=function() return "BlueprintGeneratedClass "..class end
            local operation,value=parameter(3),parameter(0.0010000000474974513)
            hooks[modify].pre(nil,parameter(pawn),parameter({AttributeName="Shachi"}),operation,value)
            check(operation:get()==(bypass and 0 or 3) and value:get()==(bypass and 0 or 0.0010000000474974513),
                "recovery's resource reset becomes a neutral additive operation only for the owning bypass binding")
            local otherOperation,otherValue=parameter(3),parameter(0.0010000000474974513)
            hooks[modify].pre(nil,parameter(other),parameter({AttributeName="Shachi"}),otherOperation,otherValue)
            check(otherOperation:get()==3 and otherValue:get()==0.0010000000474974513,
                "recovery bypass must preserve resource resets on other actors")
            attackClass.GetFullName=original
            return false
        end)
    end
end
current=nil
local feeCases={
    {fn="Function /PBZCombatAsset/ActorData/Players/Hun/Abilities/ShachiAbility/Lute/GA_Hun_Lute_SKill.GA_Hun_Lute_SKill_C:ExecuteUbergraph_GA_Hun_Lute_SKill",
        resource="SpiritShadow",enough=function(value) return value>0 end,label="Lute entry and timer continue with zero ammunition"},
    {fn="Function /PBZCombatAsset/ActorData/Players/Hun/Abilities/ShachiAbility/BrutalAxe/GA_BrutalAxe_Normal.GA_BrutalAxe_Normal_C:ExecuteUbergraph_GA_BrutalAxe_Normal",
        resource="SpiritShadow",enough=function(value) return value>0.01 end,label="Brutal Axe quick-charge qualification accepts zero ammunition"},
    {fn="Function /PBZCombatAsset/Prototypes/Actions/GAA_ShachiAbilityShachi.GAA_ShachiAbilityShachi_C:Received_ActionTick",
        resource="Shachi",enough=function(value) return value>=3 end,label="Lion Head and water Dragon Head retain their continuous attack with zero shachi"},
    {fn="Function /PBZCombatAsset/Prototypes/Abilities/Base/BP_HeavyAttack_Air_Base.BP_HeavyAttack_Air_Base_C:Play Heavy Attack Timeline",
        resource="Shachi",enough=function(value) return value>=30 and math.min(value,30)==30 end,label="air heavy attacks qualify and retain the nominal consumed amount"},
    {fn="Function /MagCombatAbilities/Abilities/BP_GlobalActionAbility_Base.BP_GlobalActionAbility_Base_C:K2_CanActivateAbility",
        resource="Shachi",enough=function(value) return value>=100*0.9 end,label="global attacks pass their resource threshold with zero shachi"},
    {fn="Function /PBZCombatAsset/ActorData/Players/Hun/Abilities/SlitheringSword/Attack/GA_Hun_SlitheringSword_EvalutionCombo_New.GA_Hun_SlitheringSword_EvalutionCombo_New_C:K2_CanActivateAbility",
        resource="Shachi",enough=function(value) return value/100>0.1 end,label="Slithering Sword evolution passes its resource ratio with zero shachi"},
    {fn="Function /PBZCombatAsset/ActorData/Players/Hun/Abilities/ZhanMaBlade/GA_Hun_ZhanMa_C.GA_Hun_ZhanMa_C_C:ExecuteUbergraph_GA_Hun_ZhanMa_C",
        resource="UltimateChargeCount",enough=function(value) return value>=1 end,label="Zhan Ma combat art avoids its failed timeline with zero ultimate charge"}
}
for _,path in ipairs({"/Script/SGameAttributes.AttributeUtils:GetCharacterAttributeValue",
    "/Script/GameplayAbilities.AbilitySystemBlueprintLibrary:GetFloatAttribute"}) do
    current=attack;currentFunction="Function /Attack:CalculateResourceEffect"
    cost:activate(target,true,function()
        for _,resource in ipairs({"Shachi","SpiritShadow","UltimateChargeCount"}) do
            local attribute=parameter({AttributeName=resource,AttributeOwner=attributeOwner})
            local value=parameter(0)
            hooks[path].post(nil,value,parameter(pawn),attribute)
            check(value:get()==maxima[resource.."_Max"].CurrentValue,
                "actor-based queries must use the same actual capacity: "..resource)
            value:set(17)
            hooks[path].post(nil,value,parameter(other),attribute)
            check(value:get()==17,"queries for another actor must keep its real resource")
        end
        return false
    end)
end
for _,case in ipairs(feeCases) do
    current=attack;currentFunction=case.fn
    cost:activate(target,true,function()
        check(case.enough(query(case.resource,0)),case.label)
        check(case.enough(query(case.resource,0,nil,true)),"library reads use the same fee qualification")
        check(consume(case.resource,-3)==0,"the qualified attack retains its resource without changing the fee")
        check(query("Shachi_Max",100)==100,"resource maxima retain the actual value")
        current=other
        check(query(case.resource,0)==0,"unrelated abilities cannot inherit an enabled fee site")
        current=attack
        return false
    end)
    cost:activate(target,false,function()
        check(query(case.resource,0)==0,"unchecked bindings retain zero-resource qualification")
        return false
    end)
end
current=attack
currentFunction="Function /PBZCombatAsset/ActorData/Players/Hun/Abilities/SlitheringSword/Attack/GA_Hun_SlitheringSword_EvalutionCombo_New.GA_Hun_SlitheringSword_EvalutionCombo_New_C:ExecuteUbergraph_GA_Hun_SlitheringSword_EvalutionCombo_New"
cost:activate(target,true,function()
    check(query("Shachi",17)==100,"resource-based damage must use full current capacity when bypassed")
    currentFunction="Function /Other.Ability:K2_CanActivateAbility"
    check(query("Shachi",0)==100,"all owned eligibility functions must use the same full resource view")
    maxima.Shachi_Max.CurrentValue=175
    check(query("Shachi",17)==175 and query("Shachi",0,nil,true)==175,
        "full resource effects must follow the player's changed limit without arbitrary inflated values")
    maxima.Shachi_Max.CurrentValue=0
    check(query("Shachi",17)==0,"a zero resource limit must remain zero")
    maxima.Shachi_Max.CurrentValue=100
    return false
end)
current=attack; currentFunction="Function /Base:ExecuteUbergraph_BP_CombatArt_Base"
cost:activate(target,true,function()
    currentFunction="Function /Attack:RepeatedResourceEffect"
    check(query("Shachi",17)==100,"repeated effects begin with the current full-capacity view")
    local count=reportCalls
    for index=1,100 do
        check(query("Shachi",index)==100,"repeated effects retain full capacity")
    end
    check(reportCalls==count,"repeated reads must not rebuild and emit the same resource diagnostic")
    currentFunction="Function /Base:ExecuteUbergraph_BP_CombatArt_Base"
    return false
end)
local getterCost=2
check(cost:activate(target,true,function()
    check(consume("UltimateChargeCount",-getterCost)==0,"ultimate fee is suppressed before deduction")
    local writeReport = reports["resource-cost:write:applied:"..currentFunction]
    check(writeReport:find("attribute=UltimateChargeCount op=0 value=-2",1,true)
        and writeReport:find("ownerAddress=1 ownerName=[Ability /Player.Attack_1]",1,true),
        "fee diagnostics include the actual resource, operation, unmodified deduction and owning ability identity")
    check(getterCost==2,"original nominal fee remains available for reinforcement")
    check(query("UltimateChargeCount",0)>=getterCost,"empty charge bank can qualify through its native result slot")
    check(reports["resource-cost:read:applied:"..currentFunction]:find("attribute=UltimateChargeCount original=0",1,true),
        "qualification diagnostics retain the native resource result before overriding it")
    check(query("Shachi",0,nil,true)>0,
        "library post-hook skips the original return value before reading component and attribute")
    check(query("Shachi",0,other)==0,
        "resource queries on another ability system retain the native result")
    check(query("Shachi",0,other,true)==0,
        "library queries on another ability system retain the native result")
    check(query("Health",20)==20,
        "non-resource attribute queries retain the native result")
    check(query("Health",20,nil,true)==20,
        "library non-resource queries retain the native result")
    check(consume("Shachi",-350)==0,"continuous shachi cost")
    check(consume("SpiritShadow",-2)==0,"explicit bullet cost")
    check(consume("SpiritShadow",2)==2,"gains are preserved")
    check(consume("Health",-20)==-20,"damage is preserved")
    check(consume("Shachi",-10,other)==-10,"other actors are preserved")
    local actorMismatch = reports["resource-cost:write:actor-mismatch:"..currentFunction]
    check(actorMismatch and actorMismatch:find("actor=2 pawn=5",1,true),"bypass diagnostics retain the rejected actor and player identity")
    check(consume("Shachi",-10,pawn,1)==-10,"non-additive writes are preserved")
    current=other;check(consume("Shachi",-10)==-10,"another ability cannot borrow the scope")
    check(query("UltimateChargeCount",0)==0 and query("Shachi",0,nil,true)==0,
        "another ability on the same player retains its actual zero-resource results")
    check(reports["resource-cost:write:owner-missing:"..currentFunction]~=nil,"an unowned fee during a bypass activation reports the missing attribution")
    check(reports["resource-cost:write:owner-missing:"..currentFunction]:find("contextAddress=2 contextName=[Ability /Player.Attack_2]",1,true),
        "missing ownership retains the actual Blueprint object without identifying it as an owned activation")
    current=attack;currentFunction="Function /Base:CalculateDamage"
    check(query("Shachi",17)==100,"later resource-dependent effects use this binding's full resource view")
    check(query("Shachi",17,nil,true)==100,
        "library reads retain the same full resource effect")
    attack.bIsActive=true;return true
end),"normal activation")
do
    local previous = reports
    reports = {}
    current=other
    check(consume("Shachi",-10)==-10 and next(reports)==nil,"ordinary fees outside a bypass activation produce no diagnostic noise")
    reports = previous
    current=attack
end
cost:update();check(consume("SpiritShadow",-1)==0,"scope persists through later ticks")
attack.bIsActive=false;cost:update();check(consume("SpiritShadow",-1)==-1,"ending removes the scope")
check(cost:activate(target,false,function() check(consume("Shachi",-1)==-1,"unchecked binding pays normally");return false end)==false,"normal failure result")
check(cost:activate(target,true,function() return false end)==false,"failed activation")
check(next(cost.instances)==nil,"failed activation leaves no scope")
check(not pcall(function() cost:activate(target,true,function() error("activation error") end) end),"activation exceptions propagate")
check(next(cost.instances)==nil,"exception leaves no scope")
do
    local receiver=backend.receiver
    backend.receiver=function() error("Idle resource hooks must not resolve game objects") end
    for index=1,100 do
        check(query("Shachi",17)==17 and consume("Shachi",-1)==-1,
            "idle resource hooks must leave game reads and writes unchanged without resolving the player")
    end
    backend.receiver=receiver
end
do
    local Router = require("router")
    local config = assert(loadfile((os.getenv("PBZ_RESOURCE_BINDING_FIXTURES") or "dist/resource-binding-check") .. "/merged-resource-move.lua"))()
    local requests, activations = 0, 0
    function backend:dispatch(spec,phase,value,id,bypass)
        requests=requests+1
        check(not spec.ability_class and spec.tag=="Ability.Action.Hun.Heavy.ComboAtk_N3H3","compiled move retains tag activation")
        cost:activate(spec,bypass,function()
            if not bypass then check(consume("Shachi",-1)==-1,"ordinary requests cannot borrow another binding's bypass") end
            local available=query("Shachi",0)
            if available<1 then return false end
            check(consume("Shachi",-1)==0,"this binding bypasses its own resource deduction")
            activations=activations+1;return true
        end)
    end
    cdo.AbilityTags={GameplayTags=array({{TagName={ToString=function() return "Ability.Action.Hun.Heavy.ComboAtk_N3H3" end}}})}
    current=attack;currentFunction="Function /Base:Apply Shachi Cost And Endure"
    for _,reversed in ipairs({false,true}) do
        if reversed then config.bindings[1],config.bindings[2]=config.bindings[2],config.bindings[1] end
        local router=Router.new(config,backend)
        local before=requests
        router:step(0,{H=1})
        check(requests==before+1 and activations==(reversed and 3 or 0),"ordinary request fails with an empty resource bank")
        router:step(.1,{G=1,H=1})
        check(requests==before+3 and activations==(reversed and 4 or 1),"different binding policies make separate requests and only the qualified one activates")
        router:step(.2,{G=1,H=1})
        router:step(.3,{G=1})
        check(requests==before+6 and activations==(reversed and 6 or 3),"held and standalone bypass remain effective")
        router:step(.4,{H=1})
        check(requests==before+7 and activations==(reversed and 6 or 3),"releasing bypass restores resource checks")
        router:step(.5,{})
        check(requests==before+7 and next(cost.instances)==nil,"release leaves no request or bypass scope")
    end
end

do
    local bullet, actor = object(21),object(22)
    local source = object(20,{actor=actor,ability=attack})
    local timeline = object(24)
    local task = object(23,{PlayRate=1.25,Timeline=timeline})
    local rates={}
    function backend:invoke(value,path,rate)
        if path:find(":GetDamageSourceActor",1,true) then return value.actor end
        if path:find(":GetActor",1,true) then return value.actor or actor end
        if path:find(":GetDamageSourceAbility",1,true) then return value.ability end
        if path:find(":GetPlayTimelineTask",1,true) then return task end
        if path:find(":SetPlayRate",1,true) then rates[#rates+1]=rate;task.PlayRate=rate;return end
        error(path)
    end
    attack.bIsActive=false
    check(cost:activate(target,false,function() attack.bIsActive=true;return true end,
        {damage_multiplier=3,startup_speed=2,recovery_speed=4}),"tuning activates without bypass")
    hooks["/Script/LegoAbility.LGATask_PlayTimeline:PlayTimeline"].pre(nil,parameter(attack),nil,parameter(timeline),parameter(1.25))
    current=attack
    check(consume("Shachi",-1)==-1,"damage tuning does not grant free costs")
    local initialization=hooks["/Script/SGameCombatSystem.BulletInterface:Initialize"]
    initialization.pre(parameter(bullet),parameter(nil),parameter(pawn),parameter(attack))
    check(nativeAbilities[attack:GetAddress()]==3,"native initialization receives this activation's damage setting")
    check(not initialization.post,"native launch snapshots have no Lua post-initialization overwrite")
    check(not hooks["/Script/SGameAttributes.DamageCalculatorBase:CalculateDamage"],
        "damage uses the native calculation path instead of the reflected thunk")
    attack['Action Phase']={TagName={ToString=function() return 'CharacterState.Action.Phase.Start' end}}
    cost:update();check(rates[#rates]==2.5,"startup scales the native timeline rate")
    attack['Action Phase']={TagName={ToString=function() return 'CharacterState.Action.Phase.Act' end}}
    cost:update();check(rates[#rates]==1.25,"active attack returns to the original rate")
    attack['Action Phase']={TagName={ToString=function() return 'CharacterState.Action.Phase.Cancel' end}}
    cost:update();check(rates[#rates]==5,"recovery has a separate speed multiplier")
    attack.bIsActive=false;cost:update()
    check(rates[#rates]==1.25,"ending restores the original task rate")
    check(nativeAbilities[attack:GetAddress()]==1,"ending clears the native ability setting")
    cost.inputs={G={damage=3}}
    cost:suspend()
    check(not next(cost.inputs),"input suspension clears future requests")
    check(cost:activate(target,false,function() attack.bIsActive=true;return true end),"ordinary reactivation")
    initialization.pre(parameter(bullet),parameter(nil),parameter(pawn),parameter(attack))
    check(nativeAbilities[attack:GetAddress()]==1,"ordinary initialization retains its own default multiplier")
    attack.bIsActive=false;cost:update()
end
do
    attack.nonLego = true
    function backend:invoke() error("Non-timeline abilities must never call timeline methods") end
    check(cost:activate(target,false,function() attack.bIsActive=true;return true end,{startup_speed=2}),"non-timeline ability activates normally")
    cost:update()
    check(cost.instances[attack:GetAddress()].speed_supported == false,"abilities without Lego timelines skip timeline tuning")
    attack.bIsActive=false;cost:update();attack.nonLego=nil
end
cost:reset();check(stopped and next(hooks)==nil,"reset releases hooks and native capture")
print("ResourceCostTests PASS "..checks)
