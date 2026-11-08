package.path="prototype/input-router/ue4ss/Scripts/?.lua;"..package.path
local checks,hooks,classes,rates=0,{},{},{}
local nextAddress=100
local function check(value,message) assert(value,message);checks=checks+1 end
local function parameter(value) return {get=function() return value end,set=function(_,v) value=v end} end
local function array(values) return setmetatable({GetArrayNum=function() return #values end},{__index=values}) end
function FName(value) return {ToString=function() return value end} end
local function object(values)
    nextAddress=nextAddress+1
    local address=nextAddress
    local value=values or {}
    function value:GetAddress() return address end
    function value:IsValid() return self.valid~=false end
    function value:GetPropertyValue(name) return self[name] end
    function value:GetClass() return StaticFindObject(self.class) end
    function value:GetFullName() return "Class "..self.class end
    function value:IsA() return true end
    return value
end
function StaticFindObject(path)
    if not classes[path] then classes[path]=object({class=path}) end
    return classes[path]
end
function RegisterHook(path,pre,post) hooks[path]={pre=pre,post=post};return 1,2 end
function UnregisterHook(path) hooks[path]=nil end
package.loaded["script-context"]={new=function() return {
    owner=function() end,ability_damage=function() end,update_damage=function() end,reset=function() rates={} end,
    charge_rate=function(_,instance,rate,tick)
        local address=type(instance)=="number" and instance or instance:GetAddress()
        if rate~=1 then check(tick and tick:IsValid(),"charge clock has a reflected tick entry") end
        rates[address]=rate~=1 and rate or nil
    end
} end}
local Costs=require("resource-costs")
local hold="/Script/SGameInputSystem.AbilityTask_WaitSInputHold:CreateTask"
local play="/Script/LegoAbility.LegoAbility:PlayTimeline"
local activate="/Script/SGameInputSystem.AbilityInputComponent:TryActivateExternalActionAbilityByClass"
local bronze="/PBZCombatAsset/ActorData/Players/Hun/Abilities/BronzeSword/GA_Hun_BronzeSword_B_Hold.GA_Hun_BronzeSword_B_Hold_C"
local storm="/PBZCombatAsset/ActorData/Players/Hun/Abilities/BaiShanFengLeiJian/Hold/GA_Hun_FengLeiJian_B_Hold.GA_Hun_FengLeiJian_B_Hold_C"
local function fixture(path)
    local main,release=object({bAutoPlay=true}),object({bAutoPlay=false})
    local instance=object({class=path,bIsActive=false,["Action Phase"]={TagName=FName("CharacterState.Action.Phase.Act")}})
    local other=object({class=path,bIsActive=true})
    local input,asc=object(),object({ActivatableAbilities={Items=array({{
        Ability=object({class=path}),ActiveCount=0,NonReplicatedInstances=array({instance}),ReplicatedInstances=array({})
    }})}})
    local backend={pawn=object()}
    function backend:receiver(target) return target.receiver=="ability_input" and input or asc end
    function backend:object(name) return StaticFindObject(name) end
    function backend:invoke(value,name,argument)
        if name:find(":GetPlayTimelineTask",1,true) then return instance.task end
        if name:find(":GetTimelineByName",1,true) then return main end
        if name:find(":SetPlayRate",1,true) then value.rate=argument;return end
        error(name)
    end
    local cost=Costs.new(backend);backend.resource_costs=cost
    local function duration(ability)
        local value=parameter(.5)
        hooks[hold].pre(nil,parameter(ability or instance),nil,value)
        return value:get()
    end
    local function restart(settings,expected,succeeded)
        local native=hooks[activate]
        return cost:activate({ability_class=path},false,function()
            native.pre(parameter(input),parameter(instance:GetClass()))
            check(duration()==expected,"hold duration belongs to the requested activation")
            check(duration(other)==.5,"another actor's same-class charge keeps its native duration")
            if succeeded~=false then
                instance.bIsActive=true
                instance.task=object({Timeline=main,CurrentTime=.8,rate=1})
            end
            native.post(nil,parameter(succeeded~=false))
            return succeeded~=false
        end,settings)
    end
    return cost,instance,main,release,duration,restart,input
end
for _,path in ipairs({bronze,storm}) do
    for _,speed in ipairs({.5,2,10}) do
        local cost,instance,main,release,duration,restart=fixture(path)
        restart({startup_speed=speed,recovery_speed=4},.5/speed)
        cost:update()
        check(instance.task.rate==speed,"the full charge timeline follows startup speed after the native Start phase")
        check((rates[instance:GetAddress()] or 1)==(path==storm and speed or 1),"only storm charge scales the independent native Tick")
        local state=cost.instances[instance:GetAddress()]
        restart({startup_speed=3},.5/3,false)
        check(cost.instances[instance:GetAddress()]==state,"a failed restart preserves the previous clock policy")
        check((rates[instance:GetAddress()] or 1)==(path==storm and speed or 1),"a failed restart preserves the native Tick rate")
        local previous=instance.task
        hooks[play].pre(parameter(instance),parameter(release),parameter(.75))
        instance.task=object({Timeline=release,CurrentTime=.1,rate=.75})
        cost:update()
        check(previous.rate==1 and instance.task.rate==.75,"release attack restores the native attack rate")
        instance["Action Phase"]={TagName=FName("CharacterState.Action.Phase.Cancel")}
        cost:update();check(instance.task.rate==3,"release recovery keeps its own baseline and multiplier")
        restart({},.5)
        cost:update()
        check(instance.task.rate==1 and not rates[instance:GetAddress()],"ordinary binding restart restores independent clocks and animation")
        restart({startup_speed=speed},.5/speed)
        instance.bIsActive=false;cost:update()
        check(not next(cost.instances) and not next(rates),"ending the ability clears its independent clock")
        cost:reset();check(not next(hooks),"reset removes timing and animation hooks")
    end
end
do
    local cost,instance,main,release,duration,restart,input=fixture(storm)
    restart({startup_speed=10},.05)
    local previous=cost.instances[instance:GetAddress()]
    check(not Costs.with_active({resource_costs=cost},instance,false,function() return false end,{startup_speed=2}),
        "failed active callbacks retain the native result")
    check(cost.instances[instance:GetAddress()]==previous and rates[instance:GetAddress()]==10,
        "failed active callbacks restore the preceding native clock policy")
    local native=hooks[activate]
    native.pre(parameter(input),parameter(instance:GetClass()))
    check(duration()==.5,"ordinary native restart cannot inherit the enhanced hold threshold")
    instance.task=object({Timeline=main,CurrentTime=0,rate=1})
    native.post(nil,parameter(true))
    check(not cost.instances[instance:GetAddress()] and not rates[instance:GetAddress()],"ordinary native restart clears the enhanced Tick rate")
    cost:reset()
end
print("ChargeClockTests PASS "..checks)
