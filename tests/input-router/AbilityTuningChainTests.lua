package.path="prototype/input-router/ue4ss/Scripts/?.lua;"..package.path
local hooks,checks,current,currentFunction={},0,nil,nil
local function check(value,message) assert(value,message);checks=checks+1 end
local function parameter(value) return {get=function() return value end} end
local function array(values)
    return {ForEach=function(_,callback) for i,value in ipairs(values) do callback(i,parameter(value)) end end}
end
local function name(value) return {ToString=function() return value end} end
local function object(address,properties)
    local value=properties or {}
    function value:IsValid() return self.valid~=false end
    function value:GetAddress() return address end
    function value:GetPropertyValue(key) return self[key] end
    function value:GetFullName() return "Class "..self.path end
    function value:GetClass() return self.class end
    function value:IsA() return true end
    return value
end
function RegisterHook(path,pre,post) hooks[path]={pre=pre,post=post};return 1,2 end
function UnregisterHook(path) hooks[path]=nil end
package.loaded['script-context']={new=function() return {
    owner=function(_,states) return current and states[current:GetAddress()],currentFunction end,reset=function() end
} end}
local Costs=require('resource-costs')
local prefix="/Script/SGameInputSystem.AbilityInputComponent:"
local methods={"TryActivateExternalActionAbilityByClass","ForceActivateExternalActionAbilityByClass",
    "ForceActivateExternalActionAbilityByClassWithEvent","TryActivateExternalActionAbilityByTag",
    "ForceActivateExternalActionAbilityByTag","TryActivateExternalActionAbilityInterruptLowerByTag",
    "/Script/GameplayAbilities.AbilitySystemComponent:TryActivateAbilityByClass"}
local function fixture()
    local classes={object(11,{path="/Player.Start_C"}),object(12,{path="/Player.Release_C"}),object(13,{path="/Player.Finish_C"})}
    local abilities,specs={},{}
    for i,class in ipairs(classes) do
        local tags={GameplayTags=array({{TagName=name("Ability.Test."..i)}})}
        abilities[i]=object(i,{class=class,bIsActive=false})
        specs[i]={Ability=object(i+20,{class=class,AbilityTags=tags}),ActiveCount=0,
            NonReplicatedInstances=array({abilities[i]}),ReplicatedInstances=array({abilities[i]})}
    end
    local asc=object(30,{ActivatableAbilities={Items=array(specs)}})
    local input,pawn=object(31),object(32)
    local timeline,task=object(33),object(34)
    task.Timeline=timeline
    local backend={pawn=pawn}
    function backend:receiver(target) return target.receiver=="ability_input" and input or asc end
    function backend:object() return object(99) end
    function backend:invoke(value,path,rate)
        if path:find(":GetPlayTimelineTask",1,true) then return task end
        if path:find(":SetPlayRate",1,true) then task.rate=rate;return end
        error(path)
    end
    local cost=Costs.new(backend)
    local function fee()
        local value=-2
        hooks["/Script/SGameAttributes.AttributeUtils:ModifyCharacterAttributeValue"].pre(nil,parameter(pawn),
            parameter({AttributeName="Shachi"}),parameter(0),{get=function() return value end,set=function(_,v) value=v end})
        return value
    end
    return cost,backend,classes,abilities,asc,input,task,timeline,fee
end
for _,method in ipairs(methods) do
    local cost,backend,classes,abilities,asc,input,task,timeline,fee=fixture()
    local path=method:sub(1,1)=="/" and method or prefix..method
    local entry=hooks[path]
    local selector=method:find("ByTag",1,true) and {TagName=name("Ability.Test.2")} or classes[2]
    local receiver=method:sub(1,1)=="/" and asc or input
    check(cost:activate({ability_class=classes[1].path},true,function()
        abilities[1].bIsActive=true;current=abilities[1]
        entry.pre(parameter(receiver),parameter(selector))
        current=abilities[2]
        check(fee()==0,"continuation resource fee is bypassed before native activation: "..method)
        abilities[1].bIsActive=false
        abilities[2].bIsActive=true
        hooks['/Script/LegoAbility.LegoAbility:PlayTimeline'].pre(parameter(abilities[2]),parameter(timeline),parameter(1.25))
        entry.post(parameter(receiver),parameter(true),parameter(selector))
        return true
    end,{damage_multiplier=3,startup_speed=2,recovery_speed=4}),"parent activation follows its native continuation")
    check(not cost.instances[1] and cost.instances[2].damage==3 and cost.instances[2].bypass,
        "continuation retains its own settings after its predecessor ends: "..method)
    abilities[2]['Action Phase']={TagName=name('CharacterState.Action.Phase.Start')}
    cost:update();check(task.rate==2.5,"continuation startup speed uses its own timeline")
    abilities[2]['Action Phase']={TagName=name('CharacterState.Action.Phase.Cancel')}
    cost:update();check(task.rate==5,"continuation recovery speed follows the binding")
    local force=hooks[prefix..'ForceActivateExternalActionAbilityByClass']
    current=abilities[2];force.pre(parameter(input),parameter(classes[3]))
    current=abilities[3];check(fee()==0,"settings propagate through more than one native continuation")
    abilities[3].bIsActive=true;force.post(parameter(input),parameter(true))
    check(cost.instances[3].damage==3,"the next continuation retains damage independently")
    abilities[2].bIsActive=false;abilities[3].bIsActive=false;cost:update()
    check(next(cost.instances)==nil and task.rate==1.25,"ending the chain clears settings and restores speed")
    check(fee()==-2,"ended continuations cannot retain resource bypass")
    cost:reset()
end
for _,condition in ipairs({'failed','inactive','unowned','unrelated','callbackOnly','nested'}) do
    local cost,backend,classes,abilities,asc,input,task,timeline,fee=fixture()
    abilities[1].bIsActive=true
    local state=cost:arm(abilities[1],true,{damage_multiplier=3})
    state.callback_only=condition=='callbackOnly'
    current=condition=='unrelated' and abilities[3] or abilities[1]
    local entry=hooks[prefix..'ForceActivateExternalActionAbilityByClass']
    local receiver=condition=='unowned' and object(90) or input
    entry.pre(parameter(receiver),parameter(classes[2]))
    if condition=='nested' then
        entry.pre(parameter(receiver),parameter(classes[2]))
        abilities[2].bIsActive=true
        entry.post(parameter(receiver),parameter(true))
    elseif condition~='inactive' then abilities[2].bIsActive=true end
    entry.post(parameter(receiver),parameter(condition~='failed'))
    check((cost.instances[2]~=nil)==(condition=='nested'),"only successful owned continuation inherits: "..condition)
    check(cost.instances[1]==state,"continuation selection never replaces the caller's scope")
    cost:reset()
end
print('AbilityTuningChainTests PASS '..checks)
