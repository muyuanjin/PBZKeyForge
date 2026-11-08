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
    enable_damage=function() end,
    interface_hook=function(_,_,path,pre,post)
        return RegisterHook(path,function(context,...) return pre(context:get(),...) end,post)
    end,
    owner=function() end,damage_bullet=function() end,ability_damage=function() end,update_damage=function() end,reset=function() end
} end}
local config=assert(loadfile(os.getenv("PBZ_PHASE_MOVE_CONFIG") or "dist/complete-move-catalog-check/phase-moves.lua"))()
local target,count
count=0
for _,candidate in pairs(config.targets) do
    for _,window in ipairs(candidate.phase_windows or (candidate.phase_window and {candidate.phase_window} or {})) do
        count=count+1
        check((candidate.entry_arguments[1].current_timeline or window.timeline==candidate.entry_arguments[1].fname)
            and window.entry_time==candidate.entry_arguments[4],
            "compiled phase window belongs to its selected entry timeline")
        check(window.entry_time<=window.attack_start and window.attack_start<=window.attack_end,
            "compiled phase window retains an ordered startup, attack and recovery")
    end
    if candidate.move_id=="move:Ability.Action.Hun.CombatArt.BronzeSword.Final" then target=candidate end
end
check(count>=36,"all named combat-art phases retain their windows through compilation")
local window=assert(target and target.phase_window)
check(window.entry_time==2.43 and math.abs(window.attack_start-2.7894864082336426)<1e-12
    and math.abs(window.attack_end-3.904533863067627)<1e-12,
    "Bronze finisher uses its cooked projectile boundaries")

local Costs=require("resource-costs")
local instance=object(1,{class=target.ability_class,bIsActive=false})
local release=object(2,{class="/Fixture.Release",bIsActive=false})
local function spec(value)
    return {Ability=object(value:GetAddress()+100,{class=value.class}),ActiveCount=0,
        NonReplicatedInstances=array({value}),ReplicatedInstances=array({})}
end
local component=object(3,{ActivatableAbilities={Items=array({spec(instance),spec(release)})}})
local timeline,otherTimeline=object(4),object(5)
local task=object(6,{Timeline=timeline,CurrentTime=0})
local otherTask=object(7,{Timeline=otherTimeline,CurrentTime=0})
local releaseTask=object(8,{Timeline=timeline,CurrentTime=2.5})
instance.task,release.task=task,releaseTask
local backend={pawn=object(9)}
local branchTimelines
function backend:receiver() return component end
function backend:object() return object(10) end
function backend:invoke(value,path,argument)
    if path:find(":GetPlayTimelineTask",1,true) then return value.task end
    if path:find(":GetTimelineByName",1,true) then
        if branchTimelines then return branchTimelines[argument:ToString()] end
        check(argument:ToString()==window.timeline,"window resolves the authored timeline name")
        return timeline
    end
    if path:find(":SetPlayRate",1,true) then value.rate=argument;return end
    error(path)
end
local cost=Costs.new(backend)
local play="/Script/LegoAbility.LegoAbility:PlayTimeline"
check(cost:activate(target,false,function()
    hooks[play].pre(parameter(instance),parameter(timeline),parameter(1.25))
    instance.bIsActive=true
    return true
end,{startup_speed=2,recovery_speed=4}),"finisher arms its settings before native playback")
local function update(time,phase,expected,message)
    task.CurrentTime=time
    instance["Action Phase"]={TagName=FName("CharacterState.Action.Phase."..phase)}
    cost:update()
    check(task.rate==expected,message)
end
update(window.entry_time-.01,"Start",2.5,"pre-entry playback retains the original global phase policy")
update(window.entry_time,"Act",2.5,"finisher startup scales by two inside the original global Act phase")
update(window.attack_start-.001,"Act",2.5,"startup remains scaled until the first projectile preparation")
update(window.attack_start,"Act",1.25,"first projectile preparation restores the native baseline")
update(3.58,"Combo",1.25,"gaps between retained attacks remain at the native rate")
update(window.attack_end-.001,"Combo",1.25,"last damage window remains at the native rate")
update(window.attack_end,"Combo",5,"finisher recovery scales by four while the original phase is still Combo")

hooks[play].pre(parameter(instance),parameter(otherTimeline),parameter(.75))
instance.task=otherTask
instance["Action Phase"]={TagName=FName("CharacterState.Action.Phase.Act")}
otherTask.CurrentTime=5
cost:update()
check(task.rate==1.25 and otherTask.rate==.75,"switching timelines restores the old task and preserves ordinary Act")
instance["Action Phase"]={TagName=FName("CharacterState.Action.Phase.Cancel")}
cost:update()
check(otherTask.rate==3,"another timeline uses its own native baseline and global recovery phase")
instance.task=task
update(2.5,"Act",2.5,"returning to the selected timeline resumes its own startup window")
check(otherTask.rate==.75,"leaving another timeline restores its native baseline")

cost:begin_transfer(cost.instances[instance:GetAddress()],{ability_class=release.class})
release.bIsActive=true
hooks[play].pre(parameter(release),parameter(timeline),parameter(.5))
cost:end_transfer(true)
release["Action Phase"]={TagName=FName("CharacterState.Action.Phase.Act")}
cost:update()
check(not cost.instances[release:GetAddress()].phase_window and releaseTask.rate==.5,
    "a different ability inherits tuning without inheriting another move's phase window")
release["Action Phase"]={TagName=FName("CharacterState.Action.Phase.Start")}
cost:update()
check(releaseTask.rate==1,"the continuation retains the requested startup multiplier through its own phase")
instance.bIsActive,release.bIsActive=false,false
cost:update()
check(task.rate==1.25 and releaseTask.rate==.5 and next(cost.instances)==nil,
    "ending both abilities restores their independent native rates and clears the windows")
cost:reset()
local branchTarget
for _,candidate in pairs(config.targets) do
    if candidate.move_id=="move:WeaponPhase.GA_Hun_SlitheringSword_C.Stage2" then branchTarget=candidate end
end
check(branchTarget and #branchTarget.phase_windows==2,"the native counter branch retains both distinct phase envelopes")
instance.class=branchTarget.ability_class
component.ActivatableAbilities.Items[1].Ability.class=branchTarget.ability_class
instance.bIsActive=false
instance.task=task
branchTimelines={Main=timeline,Perfect=otherTimeline}
task.Timeline,otherTask.Timeline=timeline,otherTimeline
cost=Costs.new(backend)
check(cost:activate(branchTarget,true,function()
    hooks[play].pre(parameter(instance),parameter(timeline),parameter(1.25))
    instance.bIsActive=true
    return true
end,{startup_speed=2,recovery_speed=4,damage_multiplier=3}),"branch phase arms its own bypass and multipliers")
check(cost.instances[instance:GetAddress()].bypass and cost.instances[instance:GetAddress()].damage==3,
    "resource bypass and damage stay attached to this normally activated phase instance")
for index,phaseWindow in ipairs(branchTarget.phase_windows) do
    instance.task=index==1 and task or otherTask
    local selectedTimeline=index==1 and timeline or otherTimeline
    hooks[play].pre(parameter(instance),parameter(selectedTimeline),parameter(1.25))
    instance.task.CurrentTime=phaseWindow.entry_time
    instance["Action Phase"]={TagName=FName("CharacterState.Action.Phase.Act")}
    cost:update()
    check(instance.task.rate==2.5,"each original branch uses its own startup")
    instance.task.CurrentTime=phaseWindow.attack_end
    cost:update()
    check(instance.task.rate==5,"both native branches apply recovery at their own final attack boundary")
end
instance.bIsActive=false
cost:update()
check(task.rate==1.25 and otherTask.rate==1.25,"native branch completion restores both original rates")
cost:reset()
print("FinisherSpeedTests PASS "..checks)
