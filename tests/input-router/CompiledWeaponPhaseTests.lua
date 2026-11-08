package.path = "prototype/input-router/ue4ss/Scripts/?.lua;" .. package.path
local checks, hooks, current, damage = 0, {}, nil, {}
local function check(value, message) assert(value, message); checks=checks+1 end
local function parameter(value)
    return {get=function() return value end,set=function(_,nextValue) value=nextValue end}
end
local function array(values)
    return setmetatable({GetArrayNum=function() return #values end,ForEach=function(_,callback)
        for index,value in ipairs(values) do callback(index,parameter(value)) end
    end},{__index=values})
end
function FName(value) return {ToString=function() return value end} end
local classes = {}
local function object(address,values)
    values=values or {}
    function values:IsValid() return self.valid~=false end
    function values:GetAddress() return address end
    function values:GetPropertyValue(name) return self[name] end
    function values:GetClass() return StaticFindObject(self.class) end
    function values:GetFullName() return "Object "..(self.path or self.class or tostring(address)) end
    function values:IsA() return true end
    return values
end
function StaticFindObject(path)
    if not classes[path] then classes[path]=object(1000+#classes,{path=path}); table.insert(classes,classes[path]) end
    return classes[path]
end
function RegisterHook(path,pre,post) hooks[path]={pre=pre,post=post}; return 1,2 end
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
local config=assert(loadfile(os.getenv("PBZ_PHASE_MOVE_CONFIG") or "dist/complete-move-catalog-check/phase-moves.lua"))()
local play="/Script/LegoAbility.LegoAbility:PlayTimeline"
local modify="/Script/SGameAttributes.AttributeUtils:ModifyCharacterAttributeValue"
local read="/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayAttributeValue"
local function fixture(target,timelineName)
    classes,hooks,current,damage={},{},nil,{}
    local active=target.active_ability_classes~=nil
    local instance=object(1,{class=target.ability_class,bIsActive=active,["Current Shachi Reinforced"]=true})
    local spec={Ability=object(2,{class=target.ability_class}),ActiveCount=active and 1 or 0,
        NonReplicatedInstances=array({instance}),ReplicatedInstances=array({})}
    local asc=object(3,{ActivatableAbilities={Items=array({spec})}})
    local timelines={}
    for _,name in ipairs({"Main","Perfect","NearTimeline","HasSawHit","ExecutionTimeline","GrabSuccess","Failed"}) do
        timelines[name]=object(10+#timelines,{bAutoPlay=true}); table.insert(timelines,timelines[name])
    end
    local task=object(30,{Timeline=timelines[timelineName],CurrentTime=0,rate=1})
    local pawn,receiver,input,owner=object(31),object(32),object(33),object(34)
    local backend={pawn=pawn,entries=0,activations=0,config=config}
    local data=object(35,{path=target.entry_when.object_variable and target.entry_when.object_variable.object})
    instance.Variables={ForEach=function(_,callback) callback(parameter(FName("JiuTan")),parameter(object(36))) end}
    function backend:receiver(value) return value.receiver=="ability_input" and receiver or asc end
    function backend:object() return object(37) end
    function backend:argument(value) return FName(value.fname) end
    function backend:report() end
    function backend:fail_target(_,message) error(message) end
    local function fee()
        local value=parameter(-15)
        hooks[modify].pre(nil,parameter(pawn),parameter({AttributeName="Shachi"}),parameter(0),value)
        return value:get()
    end
    local function resource()
        local value=parameter(0)
        hooks[read].post(parameter(asc),value,parameter({AttributeName="Shachi",AttributeOwner=owner}))
        return value:get()
    end
    function backend:invoke(value,path,a,b,c,d,e)
        if path:find(":GetGameplayTagCount",1,true) then return 1
        elseif path:find(":TryActivateExternalActionAbilityByClass",1,true) then
            check(value==receiver and a==StaticFindObject(target.ability_class),"Exact native class owns phase activation")
            self.activations=self.activations+1
            current=instance
            check(fee()==0 and resource()==135,"The phase retains full resource eligibility, proportional effects and fee bypass")
            if self.refused then current=nil; return false end
            spec.ActiveCount,instance.bIsActive=1,true
            hooks[play].pre(parameter(instance),parameter(task.Timeline),parameter(1))
            current=nil
            return true
        elseif path:find(":GetAttributeSet",1,true) then return object(38,{Shachi_Max={CurrentValue=135}})
        elseif path:find(":GetPlayTimelineTask",1,true) then return task
        elseif path:find(":GetTimelineByName",1,true) then return timelines[a:ToString()]
        elseif path:find(":SetPlayRate",1,true) then value.rate=a; return
        elseif path:find(":GetData",1,true) then return self.missingPreparation and object(39,{valid=false}) or data
        elseif path==play then
            check(value==instance and a==task.Timeline and a==timelines[timelineName],
                "Phase playback retains the native branch's exact timeline object: "..target.move_id.." / "..timelineName
                .." receiver="..tostring(value==instance).." current="..tostring(a==task.Timeline).." named="..tostring(a==timelines[timelineName]))
            check(b==1 and c==0 and d==target.entry_arguments[4] and e:ToString()=="None","Native PlayTimeline receives all five typed arguments")
            check(damage[instance:GetAddress()]==4,"The phase entry sees this binding's damage multiplier")
            current=instance
            check(fee()==0 and resource()==135,"Phase callbacks retain the same full resource effects")
            hooks[play].pre(parameter(instance),parameter(a),parameter(b))
            current=nil
            task.CurrentTime=d
            self.entries=self.entries+1
        elseif path:find(":K2_CancelAbility",1,true) then
            spec.ActiveCount,instance.bIsActive=0,false; self.canceled=true
        else error(path) end
    end
    return backend,instance,spec,task,timelines
end

local settings={startup_speed=2,recovery_speed=3,damage_multiplier=4}
local count=0
for _,target in pairs(config.targets) do
    if target.move_id:find("move:WeaponPhase.",1,true)==1 then
        count=count+1
        for _,window in ipairs(target.phase_windows or {target.phase_window}) do
            local backend,instance,spec,task,timelines=fixture(target,window.timeline)
            local active=target.active_ability_classes~=nil
            if active then
                task.CurrentTime=target.entry_when.minimum_time
                task.Timeline=timelines.Failed
                check(not Moves.dispatch(backend,target,"started",true,settings),"Active phases cannot fabricate a hit, grab or perfect-counter branch")
                task.Timeline=timelines[window.timeline]
            end
            check(Moves.dispatch(backend,target,"started",true,settings),"Every qualified compiled phase dispatches")
            if not active then
                check(backend.entries==0 and backend.activations==1,"Normal activation waits for actual native setup time")
                task.CurrentTime=target.entry_when.minimum_time
                if target.entry_when.object_variable then
                    backend.missingPreparation=true
                    Moves.update(backend)
                    check(backend.entries==0,"The native prepared jar must exist before its release phase")
                    backend.missingPreparation=false
                end
                Moves.update(backend)
            end
            check(backend.entries==1 and backend.activations==(active and 0 or 1),"Existing-branch stages reuse their active instance without a new fee or activation")
            local costs=assert(backend.resource_costs)
            local state=assert(costs.instances[instance:GetAddress()])
            check(state.bypass and state.damage==4 and state.startup==2 and state.recovery==3,"All four binding settings reach the actual phase instance")
            instance["Action Phase"]={TagName=FName("CharacterState.Action.Phase.Act")}
            costs:update()
            check(task.rate==(window.entry_time<window.attack_start and 2 or 1),"Phase startup uses the actual retained attack boundary")
            task.CurrentTime=window.attack_end
            costs:update()
            check(task.rate==3,"Phase recovery uses this native branch's own final attack boundary")
            instance.bIsActive,spec.ActiveCount=false,0
            costs:update()
            check(task.rate==1 and next(costs.instances)==nil,"Ending the native phase restores speed and tuning state")
            Moves.reset(backend); costs:reset()
        end
        if not target.active_ability_classes then
            local backend,_,_,task=fixture(target,(target.phase_windows or {target.phase_window})[1].timeline)
            backend.refused=true
            check(not Moves.dispatch(backend,target,"started",true,settings),"Native target or equipment refusal rejects phase entry")
            backend.resource_costs:update()
            check(backend.entries==0 and next(backend.resource_costs.instances)==nil and task.rate==1,"Refused phases do not retain tuning or emit an attack")
            backend.resource_costs:reset()
        end
    end
end
check(count==19,"The sweep covers all newly separated combat-art phases")
print("CompiledWeaponPhaseTests PASS "..checks..": "..count.." native phases, all branches and binding effects")
