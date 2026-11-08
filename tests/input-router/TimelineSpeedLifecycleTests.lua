package.path="prototype/input-router/ue4ss/Scripts/?.lua;"..package.path
local checks,hooks,nextAddress=0,{},100
local function check(value,message) assert(value,message);checks=checks+1 end
local function parameter(value) return {get=function() return value end} end
local function array(values)
    return setmetatable({GetArrayNum=function() return #values end,ForEach=function(_,callback) for i,value in ipairs(values) do callback(i,parameter(value)) end end},{__index=values})
end
function FName(value) return {ToString=function() return value end} end
local function object(values)
    nextAddress=nextAddress+1
    local address=nextAddress
    local value=values or {}
    function value:IsValid() return self.valid~=false end
    function value:GetAddress() return address end
    function value:GetPropertyValue(name) return self[name] end
    function value:GetFullName() return "Class "..(self.class or "/Fixture.Object") end
    function value:GetClass() return StaticFindObject(self.class) end
    function value:IsA() return true end
    return value
end
local classes = {}
function StaticFindObject(path)
    if not classes[path] then classes[path]=object({class=path}) end
    return classes[path]
end
function RegisterHook(path,pre,post) hooks[path]={pre=pre,post=post};return 1,2 end
function UnregisterHook(path) hooks[path]=nil end
package.loaded["script-context"]={new=function() return {
    owner=function() end,ability_damage=function() end,enable_damage=function() end,
    update_damage=function() end,reset=function() end,
    interface_hook=function(_,_,path,pre,post) return RegisterHook(path,pre,post) end
} end}
local Costs=require("resource-costs")
local config=assert(loadfile(os.getenv("PBZ_PHASE_MOVE_CONFIG") or "dist/complete-move-catalog-check/phase-moves.lua"))()
local prefix="/Script/SGameInputSystem.AbilityInputComponent:"
local play="/Script/LegoAbility.LegoAbility:PlayTimeline"
local playTask="/Script/LegoAbility.LGATask_PlayTimeline:PlayTimeline"
local playName="/Script/LegoAbility.LegoAbility:PlayTimelineByName"
local paths={
    prefix.."ForceActivateExternalActionAbilityByClass",
    prefix.."TryActivateExternalActionAbilityByClass",
    prefix.."ForceActivateExternalActionAbilityByClassWithEvent",
    prefix.."ForceActivateExternalActionAbilityByTag",
    prefix.."TryActivateExternalActionAbilityByTag",
    prefix.."TryActivateExternalActionAbilityInterruptLowerByTag",
    "/Script/GameplayAbilities.AbilitySystemComponent:TryActivateAbilityByClass",
    "/Script/GameplayAbilities.AbilitySystemComponent:TryActivateAbilitiesByTag"
}
local function fixture(target,phaseWindow)
    local tag=target.tag or "Ability.Fixture.Attack"
    local tags={GameplayTags=array({{TagName=FName(tag)}})}
    local timeline=object({bAutoPlay=true})
    local otherTimeline=object({bAutoPlay=true})
    local function ability()
        local instance=object({class=target.ability_class,AbilityTags=tags,bIsActive=true,
            ["Action Phase"]={TagName=FName("CharacterState.Action.Phase.Act")}})
        instance.task=object({Timeline=timeline,CurrentTime=2,rate=1})
        return instance,{Ability=object({class=target.ability_class,AbilityTags=tags}),ActiveCount=1,
            NonReplicatedInstances=array({instance}),ReplicatedInstances=array({})}
    end
    local instance,spec=ability()
    local other,otherSpec=ability()
    local asc=object({ActivatableAbilities={Items=array({spec,otherSpec})}})
    local input,action=object(),object()
    local backend={pawn=object()}
    function backend:receiver(receiver) return receiver.receiver=="ability_input" and input or asc end
    function backend:object() return object() end
    function backend:invoke(value,path,argument)
        if path:find(":GetPlayTimelineTask",1,true) then return value.task end
        if path:find(":GetTimelineByName",1,true) then
            if not phaseWindow or argument:ToString()==phaseWindow.timeline then return timeline end
            return otherTimeline
        end
        if path:find(":SetPlayRate",1,true) then value.rate=argument;return end
        error(path)
    end
    local cost=Costs.new(backend)
    local function restart(rate,playback)
        if playback==play then hooks[play].pre(parameter(instance),parameter(timeline),parameter(rate))
        elseif playback==playTask then
            hooks[playTask].pre(nil,parameter(instance),nil,parameter(timeline),parameter(rate))
        elseif playback==playName then
            hooks[playName].pre(parameter(instance),parameter(FName("Main")),parameter(rate))
        end
        instance.task=object({Timeline=timeline,CurrentTime=0,rate=rate or 1})
        return instance.task
    end
    local function phase(name,time,expected,message)
        instance["Action Phase"]={TagName=FName("CharacterState.Action.Phase."..name)}
        instance.task.CurrentTime=time
        cost:update()
        check(instance.task.rate==expected,message..": "..tostring(target.move_id or target.ability_class)
            .." / "..tostring(phaseWindow and phaseWindow.timeline).." at "..time
            .." expected="..expected.." actual="..tostring(instance.task.rate))
    end
    local function native(path,result,replace,rate,playback)
        local receiver=path:find("AbilitySystemComponent",1,true) and asc or input
        local argument=path:find("ByClass",1,true) and instance:GetClass()
            or path:find("AbilitiesByTag",1,true) and tags or {TagName=FName(tag)}
        hooks[path].pre(parameter(receiver),parameter(argument),parameter(action))
        if replace then restart(rate,playback) end
        hooks[path].post(nil,parameter(result))
        return result
    end
    local function finish()
        instance.bIsActive=false
        cost:update()
        check(not next(cost.instances),"ending the selected execution releases tuning")
        check(other.task.rate==1,"another active instance retains its own native speed")
        cost:reset()
        check(not next(hooks),"fixture releases all hooks")
    end
    return cost,instance,other,timeline,phase,native,restart,finish,action
end

local windows=0
for _,target in pairs(config.targets) do
    for _,window in ipairs(target.phase_windows or (target.phase_window and {target.phase_window} or {})) do
        windows=windows+1
        local cost,instance,other,timeline,phase,native,restart,finish=fixture(target,window)
        local originalTask=instance.task
        local settings={startup_speed=2,recovery_speed=4}
        cost:activate(target,false,function()
            return native(paths[6],true,true)
        end,settings)
        local state=cost.instances[instance:GetAddress()]
        check(state and (target.phase_windows and state.phase_windows==target.phase_windows
                or target.phase_window and state.phase_window==target.phase_window),
            "every compiled combat-art phase retains its windows after a native active-instance restart")
        check(originalTask.rate==1,"the preceding original combat art is not sped up")
        phase("Act",window.entry_time,window.entry_time<window.attack_start and 2 or 1,
            "phase startup scales before its retained attack after native autoplay without a reflected hook")
        local attackRate=window.attack_start<window.attack_end and 1 or 4
        phase("Act",window.attack_start,attackRate,"phase attack timing retains the native damage window")
        phase("Combo",(window.attack_start+window.attack_end)/2,attackRate,"phase attack intervals retain native timing")
        phase("Combo",window.attack_end,4,"finisher recovery uses the independent multiplier")
        finish()
        check(instance.task.rate==1,"each finisher restores its own native baseline")
    end
end
check(windows==46,"all forty-six compiled attack speed windows were exercised")

local generic={ability_class="/Fixture.GenericAbility_C",tag="Ability.Fixture.Attack"}
for _,path in ipairs(paths) do
    for _,playback in ipairs({false,play,playTask,playName}) do
        local cost,instance,other,timeline,phase,native,restart,finish=fixture(generic)
        local baseline=playback and .75 or 1
        timeline.bAutoPlay=not playback
        local originalTask=instance.task
        local settings={startup_speed=10,recovery_speed=.5}
        cost:activate(generic,false,function() return native(path,true,true,baseline,playback) end,settings)
        local state=cost.instances[instance:GetAddress()]
        check(state and state.startup==10,"each activation API transfers settings to its actual restarted execution")
        check(originalTask.rate==1 and not cost.instances[other:GetAddress()],
            "a matching instance whose native task did not restart remains outside the new request")
        phase("Start",0,10*baseline,"new activation baseline: "..path.." / "..tostring(playback))
        phase("Act",1,baseline,"ordinary attack speed remains unchanged")
        phase("Cancel",2,.5*baseline,"fractional recovery uses the new execution's baseline")
        cost:activate(generic,false,function() return native(path,false,false) end,{startup_speed=2})
        check(cost.instances[instance:GetAddress()]==state,"rejected same-instance requests cannot replace current settings")
        cost:activate(generic,false,function() return native(path,true,false) end,{startup_speed=3})
        check(cost.instances[instance:GetAddress()]==state,"success without new playback cannot claim the current execution")
        finish()
        check(instance.task.rate==baseline,"restart restores a fractional or native playback rate")
    end
end

for _,path in ipairs({paths[1],paths[2],paths[4],paths[5],paths[6]}) do
    local cost,instance,other,timeline,phase,native,restart,finish,action=fixture(generic)
    cost:input({object=action},false,{startup_speed=2,recovery_speed=3},1)
    native(path,true,true)
    check(cost.instances[instance:GetAddress()]~=nil,"InputAction activation captures a successful active-instance restart")
    phase("Start",0,2,"InputAction startup applies after the native restart")
    phase("Act",1,1,"InputAction attack retains native speed")
    finish()
end

do
    local cost,instance,other,timeline,phase,native,restart,finish=fixture(generic)
    cost:activate(generic,false,function() return native(paths[6],true,true) end,{startup_speed=10,recovery_speed=4})
    phase("Start",0,10,"enhanced execution starts with its own multiplier")
    local previous=instance.task
    cost:activate(generic,false,function() return native(paths[6],true,true) end,{})
    check(previous.rate==1,"an immediate ordinary restart restores the preceding enhanced task")
    phase("Start",0,1,"the ordinary restart does not inherit startup speed")
    phase("Cancel",2,1,"the ordinary restart does not inherit recovery speed")
    finish()
end
for _,path in ipairs(paths) do
    local cost,instance,other,timeline,phase,native,restart,finish=fixture(generic)
    cost:activate(generic,false,function() return native(path,true,true) end,{startup_speed=10,recovery_speed=4})
    phase("Start",0,10,"enhanced execution applies its own startup speed before native input")
    local state,previous=cost.instances[instance:GetAddress()],instance.task
    native(path,false,false)
    check(cost.instances[instance:GetAddress()]==state,"rejected ordinary native input preserves the active enhanced execution")
    native(path,true,false)
    check(cost.instances[instance:GetAddress()]==state,"ordinary native input without a new task preserves the active execution")
    native(path,true,true)
    check(not cost.instances[instance:GetAddress()],"an ordinary native restart releases the preceding enhanced policy")
    check(previous.rate==1,"an ordinary native restart restores the preceding enhanced task")
    phase("Start",0,1,"an ordinary native restart cannot inherit startup speed")
    phase("Cancel",2,1,"an ordinary native restart cannot inherit recovery speed")
    finish()
end
print("TimelineSpeedLifecycleTests PASS "..checks..": 46 phase windows, 8 activation APIs, 4 playback routes, InputAction and ordinary reuse")
