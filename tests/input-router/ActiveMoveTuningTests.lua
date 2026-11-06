package.path = "prototype/input-router/ue4ss/Scripts/?.lua;" .. package.path
local checks,hooks,current = 0,{},nil
local function check(value,message) assert(value,message); checks=checks+1 end
local function parameter(value) return {get=function() return value end} end
local function array(values)
    return {ForEach=function(_,callback) for i,value in ipairs(values) do callback(i,parameter(value)) end end}
end
local function object(address,properties)
    local value=properties or {}
    function value:IsValid() return self.valid ~= false end
    function value:GetAddress() return address end
    function value:GetPropertyValue(name) return self[name] end
    function value:GetClass() return {GetFullName=function() return "Class "..self.class end} end
    function value:IsA() return not self.nonLego end
    return value
end
function FName(name) return {ToString=function() return name end} end
function RegisterHook(path,pre,post) hooks[path]={pre=pre,post=post};return 1,2 end
function UnregisterHook(path) hooks[path]=nil end
package.loaded["script-context"]={new=function() return {
    owner=function(_,states) return current and states[current:GetAddress()] end,reset=function() end
} end}
local Moves=require("complete-moves")
local Costs=require("resource-costs")
local config=assert(loadfile((os.getenv("PBZ_RESOURCE_BINDING_FIXTURES") or "dist/resource-binding-check").."/compiled-active-tuning.lua"))()
local targets,bindingSettings={},{}
for _,target in pairs(config.targets) do targets[target.active_callback_only and "ghost" or "shield"]=target end
check(targets.ghost and targets.shield,"compiled active targets retain their distinct lifetimes")
for _,binding in ipairs(config.bindings) do
    for _,output in ipairs(binding.outputs) do
        local target=config.targets[type(output)=="table" and output.target or output]
        bindingSettings[target.active_callback_only and "ghost" or "shield"]=binding
    end
end
check(bindingSettings.ghost.damage_multiplier==3 and not bindingSettings.ghost.startup_speed
    and bindingSettings.shield.startup_speed==2 and bindingSettings.shield.recovery_speed==4,
    "compiled bindings carry damage for ghost fire and independent speeds for the shield")
local initialize="/Script/SGameCombatSystem.BulletInterface:Initialize"
local start="/Script/SGameCombatSystem.BulletInterface:Start"
local play="/Script/LegoAbility.LegoAbility:PlayTimeline"
local function fixture(callbackOnly)
    local target=targets[callbackOnly and "ghost" or "shield"]
    local instance=object(1,{class=target.active_ability_classes[1],bIsActive=true,nonLego=callbackOnly})
    if target.entry_when and target.entry_when.property_sum_above then
        local condition=target.entry_when.property_sum_above
        instance[condition.threshold_property]=100
        for _,path in ipairs(condition.properties) do instance[path[1]]=instance[path[1]] or {};instance[path[1]][path[2]]=60 end
    end
    local cdo=object(2,{class=instance.class})
    local component=object(3,{ActivatableAbilities={Items=array({{Ability=cdo,ActiveCount=1,
        NonReplicatedInstances=array({instance}),ReplicatedInstances=array({instance})}})}})
    local bullet=object(4,{ability=instance})
    local actor=object(5)
    local timeline=object(6)
    local task=object(7,{Timeline=timeline,CurrentTime=1})
    local backend={scans=0,pawn=object(8)}
    function backend:receiver() return component end
    function backend:object() return object(9) end
    function backend:invoke(value,path,arg)
        if path:find(":GetActor",1,true) then return actor end
        if path:find(":GetSourceAbility",1,true) then return bullet.ability end
        if path:find(":GetDamageSourceActor",1,true) then return value.actor end
        if path:find(":GetDamageSourceAbility",1,true) then return value.ability end
        if path:find(":GetPlayTimelineTask",1,true) then return task end
        if path:find(":GetTimelineByName",1,true) then return timeline end
        if path:find(":SetPlayRate",1,true) then task.rate=arg;return end
        check(path==target.entry_point and value==instance,"only the existing active instance receives the entry callback")
        self.scans=self.scans+1
        current=instance
        check(self.resource_costs.instances[1].damage==(self.expectedDamage or 3),"settings are armed before the active callback")
        check(self.resource_costs.instances[1].bypass==(self.expectedBypass or false),"active callback receives its resource policy")
        if callbackOnly then
            if self.enemy then hooks[start].pre(parameter(bullet)) end
        else
            hooks[play].pre(parameter(instance),parameter(timeline),parameter(1.25))
        end
        if self.fail then error("entry failed") end
    end
    local function hit()
        local source=object(10,{actor=actor,ability=instance})
        local entry=hooks["/Script/SGameCombatSystem.BulletAttackModule:DealDamageToAvatar"]
        local damage=100
        entry.pre(parameter(source))
        hooks["/Script/SGameAttributes.DamageCalculatorBase:CalculateDamage"].post(nil,nil,parameter(component),nil,nil,nil,
            parameter({TagName=FName("Damage.Source.Attack")}),parameter({ForEach=function(_,callback)
                callback(parameter({TagName=FName("Damage.Damage.Target.GeneralDamage.Physical")}),
                    {get=function() return damage end,set=function(_,value) damage=value end})
            end}))
        entry.post()
        return damage
    end
    return backend,target,instance,bullet,task,timeline,hit
end
do
    local settings=bindingSettings.shield
    local backend,target,instance,bullet,task,timeline,hit=fixture(false)
    check(Moves.dispatch(backend,target,"triggered",false,settings),"active shield accepts tuning")
    check(backend.scans==1 and backend.resource_costs.instances[1],"duplicate replicated instance executes once and retains release settings")
    instance['Action Phase']={TagName=FName("CharacterState.Action.Phase.Start")}
    backend.resource_costs:update()
    check(task.rate==2.5,"active shield startup uses the newly started native timeline")
    instance['Action Phase']={TagName=FName("CharacterState.Action.Phase.Cancel")}
    backend.resource_costs:update()
    check(task.rate==5,"active shield recovery retains the binding's speed")
    hooks[initialize].post(parameter(bullet),parameter(nil),parameter(backend.pawn),parameter(instance))
    check(hit()==300,"shield release projectiles inherit its damage setting")
    instance.bIsActive=false;backend.resource_costs:update()
    check(task.rate==1.25 and next(backend.resource_costs.instances)==nil,"shield ending restores the timeline and releases instance settings")
    check(hit()==300,"shield projectile retains its launch setting after the shield ends")
    Moves.reset(backend);backend.resource_costs:reset()
end
do
    local backend,target,instance=fixture(false)
    backend.fail=true
    check(not pcall(Moves.dispatch,backend,target,"triggered",false,bindingSettings.shield),"shield callback errors propagate")
    check(instance.bIsActive and next(backend.resource_costs.instances)==nil,"failed shield release restores settings without canceling the naturally active shield")
    backend.resource_costs:reset()
end
do
    local settings=bindingSettings.ghost
    local backend,target,instance,bullet,task,timeline,hit=fixture(true)
    backend.resource_costs=Costs.new(backend)
    hooks[initialize].post(parameter(bullet),parameter(nil),parameter(backend.pawn),parameter(instance))
    check(hit()==100,"prepared ghost fire starts with ordinary damage")
    check(Moves.dispatch(backend,target,"triggered",false,settings),"ghost scan without a target remains legal")
    check(hit()==100 and next(backend.resource_costs.instances)==nil,"scan without launch leaves the prepared ball and passive untuned")
    backend.enemy=true;backend.expectedBypass=true
    check(Moves.dispatch(backend,target,"triggered",true,settings),"active ghost callback receives both settings and bypass")
    check(hit()==300,"already prepared ghost fire records the binding damage when Start launches it")
    check(next(backend.resource_costs.instances)==nil and instance.bIsActive,"launch tuning releases the persistent passive scope without ending the effect")
    hooks[start].pre(parameter(bullet))
    check(hit()==300,"a launched ball keeps its snapshot outside the callback")
    hooks[initialize].post(parameter(bullet),parameter(nil),parameter(backend.pawn),parameter(instance))
    hooks[start].pre(parameter(bullet))
    check(hit()==100,"later natural preparation and launch retain ordinary damage")
    backend.expectedDamage=5;backend.expectedBypass=false
    Moves.dispatch(backend,target,"triggered",false,{damage_multiplier=5})
    check(hit()==500,"another binding can choose its own ghost damage on the same passive instance")
    backend.expectedDamage=1
    Moves.dispatch(backend,target,"triggered",false)
    check(hit()==100,"ordinary active binding uses default damage after a tuned binding")
    backend.expectedDamage=3;backend.fail=true
    check(not pcall(Moves.dispatch,backend,target,"triggered",false,settings),"active callback errors propagate")
    check(instance.bIsActive and next(backend.resource_costs.instances)==nil,"callback failure releases tuning without canceling the natural passive")
    backend.fail=false
    local prior=backend.resource_costs:arm(instance,true,{damage_multiplier=2})
    backend.expectedDamage=3
    Moves.dispatch(backend,target,"triggered",false,settings)
    check(backend.resource_costs.instances[1]==prior and prior.damage==2 and prior.bypass,"temporary scan restores any preexisting ability scope")
    Moves.reset(backend);check(instance.bIsActive,"routing reset preserves the naturally running effect")
    backend.resource_costs:reset()
end
print("ActiveMoveTuningTests PASS "..checks)
