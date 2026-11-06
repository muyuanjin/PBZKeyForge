local scripts = "prototype/input-router/ue4ss/Scripts/"
package.path = scripts .. "?.lua;" .. package.path
local checks, hooks = 0, {}
local function check(value,message) assert(value,message); checks=checks+1 end
local function parameter(value)
    return {get=function() return value end, set=function(_,nextValue) value=nextValue end}
end
local function object(address, properties)
    local o = properties or {}
    function o:IsValid() return self.valid ~= false end
    function o:GetAddress() return address end
    function o:GetPropertyValue(name) return self[name] end
    function o:IsA() return not self.nonLego end
    function o:GetClass() return {GetFullName=function() return "Class /Player.Attack" end} end
    return o
end
local function array(values)
    return {ForEach=function(_,callback) for i,value in ipairs(values) do callback(i,parameter(value)) end end}
end
local current, currentFunction, stopped
package.loaded["script-context"] = {new=function() return {
    owner=function(_,lookup) return current and lookup[current:GetAddress()], currentFunction end,
    reset=function() stopped=true end
} end}
function RegisterHook(path,pre,post) hooks[path]={pre=pre,post=post}; return 1,2 end
function UnregisterHook(path,a,b) check(a==1 and b==2,"hook ids"); hooks[path]=nil end
local Costs = require("resource-costs")
local attack, other, cdo = object(1,{bIsActive=false}),object(2),object(3)
local component = object(4,{ActivatableAbilities={Items=array({{Ability=cdo,ActiveCount=0,NonReplicatedInstances=array({attack}),ReplicatedInstances=array({})}})}})
local pawn = object(5)
local backend={pawn=pawn,receiver=function() return component end}
function backend:object() return object(99) end
local target={ability_class="/Player.Attack"}
local cost = Costs.new(backend)
local modify="/Script/SGameAttributes.AttributeUtils:ModifyCharacterAttributeValue"
local read="/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayAttributeValue"
local libraryRead="/Script/GameplayAbilities.AbilitySystemBlueprintLibrary:GetFloatAttributeFromAbilitySystemComponent"
local function consume(resource,amount,actor,operation)
    local value=parameter(amount)
    hooks[modify].pre(nil,parameter(actor or pawn),parameter({AttributeName={ToString=function() return resource end}}),parameter(operation or 0),value)
    return value:get()
end
current=attack; currentFunction="Function /Base:ExecuteUbergraph_BP_CombatArt_Base"
local getterCost=2
check(cost:activate(target,true,function()
    check(consume("UltimateChargeCount",-getterCost)==0,"ultimate fee is suppressed before deduction")
    check(getterCost==2,"original nominal fee remains available for reinforcement")
    check(hooks[read].post(parameter(component),parameter(0),parameter({AttributeName="UltimateChargeCount"}))>=getterCost,"empty charge bank can qualify")
    check(hooks[libraryRead].post(
        parameter(nil),parameter(0),parameter(component),parameter({AttributeName="Shachi"}))>0,
        "library post-hook skips the original return value before reading component and attribute")
    check(hooks[read].post(parameter(other),parameter(0),parameter({AttributeName="Shachi"}))==nil,
        "resource queries on another ability system retain the native result")
    check(hooks[libraryRead].post(nil,parameter(0),parameter(other),parameter({AttributeName="Shachi"}))==nil,
        "library queries on another ability system retain the native result")
    check(hooks[read].post(parameter(component),parameter(20),parameter({AttributeName="Health"}))==nil,
        "non-resource attribute queries retain the native result")
    check(hooks[libraryRead].post(nil,parameter(20),parameter(component),parameter({AttributeName="Health"}))==nil,
        "library non-resource queries retain the native result")
    check(consume("Shachi",-350)==0,"continuous shachi cost")
    check(consume("SpiritShadow",-2)==0,"explicit bullet cost")
    check(consume("SpiritShadow",2)==2,"gains are preserved")
    check(consume("Health",-20)==-20,"damage is preserved")
    check(consume("Shachi",-10,other)==-10,"other actors are preserved")
    check(consume("Shachi",-10,pawn,1)==-10,"non-additive writes are preserved")
    current=other;check(consume("Shachi",-10)==-10,"another ability cannot borrow the scope")
    current=attack;currentFunction="Function /Base:CalculateDamage"
    check(hooks[read].post(parameter(component),parameter(17),parameter({AttributeName="Shachi"}))==nil,"ordinary resource reads retain actual values")
    check(hooks[libraryRead].post(nil,parameter(17),parameter(component),parameter({AttributeName="Shachi"}))==nil,
        "ordinary library resource reads retain actual values")
    attack.bIsActive=true;return true
end),"normal activation")
cost:update();check(consume("SpiritShadow",-1)==0,"scope persists through later ticks")
attack.bIsActive=false;cost:update();check(consume("SpiritShadow",-1)==-1,"ending removes the scope")
check(cost:activate(target,false,function() check(consume("Shachi",-1)==-1,"unchecked binding pays normally");return false end)==false,"normal failure result")
check(cost:activate(target,true,function() return false end)==false,"failed activation")
check(next(cost.instances)==nil,"failed activation leaves no scope")
check(not pcall(function() cost:activate(target,true,function() error("activation error") end) end),"activation exceptions propagate")
check(next(cost.instances)==nil,"exception leaves no scope")
do
    local Router = require("router")
    local config = assert(loadfile((os.getenv("PBZ_RESOURCE_BINDING_FIXTURES") or "dist/resource-binding-check") .. "/merged-resource-move.lua"))()
    local requests, activations = 0, 0
    function backend:dispatch(spec,phase,value,id,bypass)
        requests=requests+1
        check(not spec.ability_class and spec.tag=="Ability.Action.Hun.Heavy.ComboAtk_N3H3","compiled move retains tag activation")
        cost:activate(spec,bypass,function()
            local available=hooks[read].post(parameter(component),parameter(0),parameter({AttributeName="Shachi"})) or 0
            if available<1 then return false end
            check(consume("Shachi",-1)==0,"merged bypass suppresses the actual resource deduction")
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
        check(requests==before+2 and activations==(reversed and 4 or 1),"simultaneous ordinary and bypass keys activate exactly once")
        router:step(.2,{G=1,H=1})
        router:step(.3,{G=1})
        check(requests==before+4 and activations==(reversed and 6 or 3),"held and standalone bypass remain effective")
        router:step(.4,{H=1})
        check(requests==before+5 and activations==(reversed and 6 or 3),"releasing bypass restores resource checks")
        router:step(.5,{})
        check(requests==before+5 and next(cost.instances)==nil,"release leaves no request or bypass scope")
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
    local initialize=hooks["/Script/SGameCombatSystem.BulletInterface:Initialize"].post
    initialize(parameter(bullet),parameter(nil),parameter(pawn),parameter(attack))
    local takeDamage=hooks["/Script/SGameAttributes.AttributeComponent:TakeDamage"]
    local function calculate(attacker)
        local rows={{"Damage.Damage.Target.GeneralDamage.Physical",100},{"Damage.Damage.Target.GeneralDamage",20},
            {"Damage.Damage.Target.AngerDamage.Normal",30},{"Damage.Damage.Target.IgniteAccumulation",40}}
        local result={ForEach=function(_,callback)
            for _,row in ipairs(rows) do
                local parameterValue={get=function() return row[2] end,set=function(_,value) row[2]=value end}
                callback(parameter({TagName={ToString=function() return row[1] end}}),parameterValue)
            end
        end}
        local sourceTag={TagName={ToString=function() return "Damage.Source.Attack" end}}
        hooks["/Script/SGameAttributes.DamageCalculatorBase:CalculateDamage"].post(nil,nil,parameter(attacker or component),nil,nil,nil,parameter(sourceTag),parameter(result))
        return rows
    end
    local function damage(attacker,entry)
        entry=entry or takeDamage
        if entry==takeDamage then entry.pre(nil,parameter(source)) else entry.pre(parameter(source)) end
        local result=calculate(attacker)
        entry.post()
        return result
    end
    local rows=damage()
    check(rows[1][2]==300 and rows[2][2]==60,"damage multiplier reaches both general health damage keys")
    check(rows[3][2]==30 and rows[4][2]==40,"posture damage and status accumulation are preserved")
    check(damage(other)[1][2]==100,"another attacker's damage is preserved")
    check(calculate()[1][2]==100,"a tag without an enclosing damage source cannot borrow a prior hit")
    for _,name in ipairs({"DealDamageToAvatar","DealDamageCustom"}) do
        check(damage(nil,hooks["/Script/SGameCombatSystem.BulletAttackModule:"..name])[1][2]==300,
            "native bullet entry supplies the source when TakeDamage bypasses its reflected thunk: "..name)
    end
    takeDamage.pre(nil,parameter(source))
    takeDamage.pre(nil,parameter(object(26)))
    check(calculate()[1][2]==100,"nested unrelated damage masks the enclosing tuned source")
    takeDamage.post()
    check(calculate()[1][2]==300,"nested damage restores the enclosing source")
    takeDamage.post()
    local projectileActor=source.actor
    source.actor=nil
    check(damage()[1][2]==300,"direct ability damage uses the actual source interface's ability")
    source.actor=projectileActor
    attack['Action Phase']={TagName={ToString=function() return 'CharacterState.Action.Phase.Start' end}}
    cost:update();check(rates[#rates]==2.5,"startup scales the native timeline rate")
    attack['Action Phase']={TagName={ToString=function() return 'CharacterState.Action.Phase.Act' end}}
    cost:update();check(rates[#rates]==1.25,"active attack returns to the original rate")
    attack['Action Phase']={TagName={ToString=function() return 'CharacterState.Action.Phase.Cancel' end}}
    cost:update();check(rates[#rates]==5,"recovery has a separate speed multiplier")
    attack.bIsActive=false;cost:update()
    check(rates[#rates]==1.25,"ending restores the original task rate")
    check(damage()[1][2]==300,"a launched projectile retains its multiplier after the ability ends")
    check(cost:activate(target,false,function() attack.bIsActive=true;return true end),"ordinary reactivation")
    check(damage()[1][2]==300,"ordinary reuse of the ability cannot change an older projectile")
    initialize(parameter(bullet),parameter(nil),parameter(pawn),parameter(attack))
    check(damage()[1][2]==100,"reusing a projectile object clears the prior multiplier")
    attack.bIsActive=false;cost:update()
    cost:activate(target,false,function() attack.bIsActive=true;return true end,{damage_multiplier=5})
    check(damage()[1][2]==100,"an ordinary projectile retains 1x while its source ability is reused with tuning")
    local child,childActor=object(27),object(28)
    child.actor=childActor
    initialize(parameter(child),parameter(bullet),parameter(pawn),parameter(attack))
    source.actor=childActor
    check(damage()[1][2]==100,"child projectiles inherit their parent's shot rather than a newer ability activation")
    attack.bIsActive=false;cost:update()
    actor.valid=false;bullet.valid=false;child.valid=false;childActor.valid=false;cost:update()
    check(next(cost.bullets)==nil,"destroyed projectile snapshots are removed")
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
