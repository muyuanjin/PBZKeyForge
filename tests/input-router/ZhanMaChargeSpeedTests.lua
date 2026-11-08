package.path = "prototype/input-router/ue4ss/Scripts/?.lua;" .. package.path
local checks,hooks = 0,{}
local function check(value,message) assert(value,message);checks=checks+1 end
local function parameter(value) return {get=function() return value end} end
local function array(values)
    return setmetatable({GetArrayNum=function() return #values end,ForEach=function(_,callback) for i,value in ipairs(values) do callback(i,parameter(value)) end end},{__index=values})
end
function FName(name) return {ToString=function() return name end} end
local classes = {}
function StaticFindObject(path)
    if not classes[path] then
        classes[path] = {IsValid=function() return true end,GetAddress=function() return path end,
            GetFullName=function() return "Class "..path end}
    end
    return classes[path]
end
local function object(address,properties)
    local value=properties or {}
    function value:IsValid() return self.valid ~= false end
    function value:GetAddress() return address end
    function value:GetPropertyValue(name) return self[name] end
    function value:GetClass() return StaticFindObject(self.class) end
    function value:IsA() return true end
    return value
end
function RegisterHook(path,pre,post) hooks[path]={pre=pre,post=post};return 1,2 end
function UnregisterHook(path) hooks[path]=nil end
package.loaded["script-context"]={new=function() return {
    owner=function() end,ability_damage=function() end,update_damage=function() end,reset=function() end
} end}
local Costs=require("resource-costs")
local class="/PBZCombatAsset/ActorData/Players/Hun/Abilities/ZhanMaBlade/GA_Hun_ZhanMa_DB.GA_Hun_ZhanMa_DB_C"
local tag="Ability.Action.Hun.Heavy.ZhanMa.DB"
local instance=object(1,{class=class,bIsActive=false})
local other=object(2,{class="/Fixture.Other_C",bIsActive=false})
local function spec(value)
    return {Ability=object(value:GetAddress()+100,{class=value.class,
        AbilityTags={GameplayTags=array({{TagName=FName(tag)}})}}),ActiveCount=0,
        NonReplicatedInstances=array({value}),ReplicatedInstances=array({})}
end
local nativeSpec=spec(instance)
local component=object(3,{ActivatableAbilities={Items=array({nativeSpec,spec(other)})}})
local timeline,failedTimeline=object(4,{bAutoPlay=true}),object(5,{bAutoPlay=false})
local task=object(6,{Timeline=timeline,CurrentTime=0})
local failedTask=object(7,{Timeline=failedTimeline,CurrentTime=.8})
local otherTask=object(8,{Timeline=timeline,CurrentTime=.8})
instance.task,other.task=task,otherTask
local backend={pawn=object(9)}
function backend:receiver() return component end
function backend:object() return object(10) end
function backend:invoke(value,path,argument)
    if path:find(":GetPlayTimelineTask",1,true) then return value.task end
    if path:find(":GetTimelineByName",1,true) then
        check(argument:ToString()=="Main","charge window resolves the authored timeline")
        return timeline
    end
    if path:find(":SetPlayRate",1,true) then value.rate=argument;return end
    error(path)
end
local cost=Costs.new(backend)
local play="/Script/LegoAbility.LegoAbility:PlayTimeline"
local function activate(settings,target,nativePlayback)
    check(cost:activate(target or {tag=tag},false,function()
        if not nativePlayback then hooks[play].pre(parameter(instance),parameter(timeline),parameter(1.25)) end
        instance.bIsActive=true
        return true
    end,settings),"native activation accepts the binding settings")
end
local function update(time,phase,expected,message)
    task.CurrentTime=time
    instance["Action Phase"]={TagName=FName("CharacterState.Action.Phase."..phase)}
    cost:update()
    check(task.rate==expected,message)
end
activate({startup_speed=10,recovery_speed=4},nil,true)
update(0,"Start",10,"native autoplay starts at its engine rate without a reflected playback call")
update(.8,"Act",10,"native autoplay charge uses the binding multiplier without a playback hook")
update(1.3485949039459229,"Act",1,"native autoplay restores attack speed at the first bullet action")
update(3.1,"Cancel",4,"native autoplay recovery uses its separate multiplier")
instance.bIsActive=false;cost:update()
check(task.rate==1 and next(cost.instances)==nil,"native autoplay cancellation restores the original engine rate")
activate({startup_speed=2,recovery_speed=4})
update(0,"Start",2.5,"initial startup uses the requested speed")
update(.2,"Act",2.5,"charge after the native Start phase uses startup speed")
update(.8,"Act",2.5,"charge animation remains accelerated")
local firstBullet=1.3485949039459229
update(firstBullet-.0001,"Act",2.5,"charge accelerates until the first cooked bullet action")
update(firstBullet,"Act",1.25,"first bullet preparation restores the native attack speed")
update(1.4,"Act",1.25,"first damage window retains its native rate")
update(1.8,"Act",1.25,"the interval between both attacks retains its native rate")
update(1.9548537731170654,"Act",1.25,"second damage window retains its native rate")
update(2.5,"Combo",1.25,"combo phase is independent of charge tuning")
update(3.1,"Cancel",5,"recovery retains its separate multiplier")

hooks[play].pre(parameter(instance),parameter(failedTimeline),parameter(.75))
instance.task=failedTask
cost:update()
check(task.rate==1.25 and failedTask.rate==3,"switching timelines restores the old task and retains native recovery")
instance["Action Phase"]={TagName=FName("CharacterState.Action.Phase.Act")}
cost:update()
check(failedTask.rate==.75,"another timeline at charge time is not accelerated")
instance.task=task
update(.8,"Act",2.5,"returning to Main restores charge tuning")
check(failedTask.rate==.75,"leaving the other timeline restores its native rate")
instance.bIsActive=false
cost:update()
check(task.rate==1.25 and next(cost.instances)==nil,"cancellation restores the original rate and clears tuning")

activate({startup_speed=.5,recovery_speed=4},{ability_class=class})
update(.8,"Act",.625,"exact-class entry can slow charge independently of recovery")
update(firstBullet,"Act",1.25,"slower charge still restores native attack timing")
instance.bIsActive=false;cost:update()
activate({recovery_speed=4},{ability_class=class})
update(.8,"Act",1.25,"recovery-only settings retain the original charge speed")
update(3.1,"Cancel",5,"recovery-only settings still scale recovery")
instance.bIsActive=false;cost:update()

instance.task=failedTask
failedTask.rate=.75
activate({startup_speed=10,recovery_speed=4},{ability_class=class},true)
instance["Action Phase"]={TagName=FName("CharacterState.Action.Phase.Start")}
cost:update()
check(failedTask.rate==.75 and not cost.instances[instance:GetAddress()].task,
    "a timeline without autoplay or a captured rate is not assumed to start at one")
instance.bIsActive=false;cost:update()
instance.task=task
activate({},{ability_class=class})
update(.8,"Act",1.25,"later ordinary activation cannot inherit charge acceleration")
instance.bIsActive=false;cost:update()
check(cost:activate({ability_class=other.class},false,function()
    hooks[play].pre(parameter(other),parameter(timeline),parameter(1.25))
    other.bIsActive=true
    return true
end,{startup_speed=2,recovery_speed=4}),"another ability can use its own settings")
other["Action Phase"]={TagName=FName("CharacterState.Action.Phase.Act")}
cost:update()
check(otherTask.rate==1.25,"other abilities on the same actor retain native attack speed")
other.bIsActive=false;cost:update()

local entry="/Script/SGameInputSystem.AbilityInputComponent:TryActivateExternalActionAbilityInterruptLowerByTag"
local function request_while_active(succeeded,replaceTask,reflectedRate)
    local target={ability_class=class}
    check(cost:activate(target,false,function()
        hooks[entry].pre(parameter(component),parameter({TagName=FName(tag)}),parameter(nil))
        if replaceTask then
            if reflectedRate then hooks[play].pre(parameter(instance),parameter(timeline),parameter(reflectedRate)) end
            task=object(task:GetAddress()+100,{Timeline=timeline,CurrentTime=0})
            instance.task=task
        end
        hooks[entry].post(nil,parameter(succeeded))
        return succeeded
    end,{startup_speed=10,recovery_speed=4})==succeeded,"active-instance request retains the native result")
end

instance.bIsActive=true
nativeSpec.ActiveCount=1
task=object(600,{Timeline=timeline,CurrentTime=2.25,rate=1})
instance.task=task
request_while_active(false,false)
check(not cost.instances[instance:GetAddress()] and task.rate==1,"rejected G cannot change the original active charge")
request_while_active(true,false)
check(not cost.instances[instance:GetAddress()] and task.rate==1,"success without new playback cannot claim the original charge")
request_while_active(true,true)
local restarted=cost.instances[instance:GetAddress()]
check(restarted and restarted.startup==10,"successful native restart on an active instance receives G's startup setting")
update(.2,"Act",10,"G accelerates a new charge immediately following an original charge")
check(cost.instances[instance:GetAddress()]==restarted,"nested native activation retains one scope for the restarted charge")
update(firstBullet,"Act",1,"restarted charge still uses native attack timing")
instance.bIsActive=false;cost:update()

activate({},{ability_class=class},true)
task.CurrentTime=2.25
request_while_active(true,true,1.25)
update(.2,"Act",12.5,"reflected initial rate belongs to the restarted charge, not the previous activation")
instance.bIsActive=false;cost:update()
check(task.rate==1.25 and next(cost.instances)==nil,"restarted charge restores its own native rate on completion")
nativeSpec.ActiveCount=0
cost:reset()
check(next(hooks)==nil,"reset removes tuning hooks")
print("ZhanMaChargeSpeedTests PASS "..checks)
