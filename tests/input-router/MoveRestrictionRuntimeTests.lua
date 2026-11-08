package.path="prototype/input-router/ue4ss/Scripts/?.lua;"..package.path
local checks,hooks,current=0,{},nil
local function check(value,message) checks=checks+1; assert(value,message) end
local function param(value) return {get=function() return value end,set=function(_,next) value=next end} end
local function array(values)
    return setmetatable({GetArrayNum=function() return #values end,ForEach=function(_,fn)
        for i,value in ipairs(values) do fn(i,param(value)) end end},{__index=values})
end
function FName(value) return {ToString=function() return value end} end
local function container(names)
    local result={}
    for _,name in ipairs(names) do result[#result+1]={TagName=FName(name)} end
    return {GameplayTags=array(result)}
end
local function object(id,data)
    data=data or {}
    function data:IsValid() return self.valid~=false end
    function data:GetAddress() return id end
    function data:GetPropertyValue(name) return self[name] end
    function data:SetPropertyValue(name,value)
        local names={};for _,tag in ipairs(value.GameplayTags) do names[#names+1]=tag.TagName:ToString() end
        self[name]=container(names)
    end
    function data:GetClass() return self.class end
    function data:GetFullName() return "Object "..(self.path or tostring(id)) end
    function data:IsA() return true end
    return data
end
function RegisterHook(path,pre,post) hooks[path]={pre=pre,post=post};return 1,2 end
function UnregisterHook(path) hooks[path]=nil end
package.loaded["script-context"]={new=function() return {
    interface_hook=function(_,_,path,pre,post)
        return RegisterHook(path,function(context,...) return pre(context:get(),...) end,post)
    end,
    owner=function(_,states) return current and states[current:GetAddress()] end,
    at=function() return nil,nil,nil end,ability_damage=function() end,update_damage=function() end,reset=function() end
} end}
local Backend,Router,Costs=require("backend"),require("router"),require("resource-costs")
local Restrictions=require("move-restrictions")
local kinds={"equipment","training","space","stance","predecessor","cooldown"}
local function fixture(selected,key,predecessor)
    hooks={}
    local class=object(10,{path="/Fixture.Attack_C"})
    function StaticFindObject() return class end
    local required,blocked,groups,actual={}, {}, {}, {}
    for _,kind in ipairs(kinds) do
        local need,deny=kind=="predecessor" and predecessor or "Required."..kind,"Blocked."..kind
        required[#required+1]=need; blocked[#blocked+1]=deny
        groups[kind]={required_tags={need},ignored_tags={deny}}
        actual[need]=1; actual[deny]=0
        if selected==kind or selected=="all" then actual[need]=0;actual[deny]=1 end
    end
    required[#required+1]="Real.ProjectileReady";actual["Real.ProjectileReady"]=1
    local cdo=object(11,{class=class,AbilityTags=container({"Ability.Fixture"}),
        ActivationRequiredTags=container(required),ActivationBlockedTags=container(blocked)})
    local ability=object(12,{class=class,AbilityTags=cdo.AbilityTags,bIsActive=false,
        ActivationRequiredTags=container(required),ActivationBlockedTags=container(blocked)})
    local spec={Ability=cdo,ActiveCount=0,NonReplicatedInstances=array({ability}),ReplicatedInstances=array({})}
    local asc=object(13,{ActivatableAbilities={Items=array({spec})}})
    local input,pawn=object(14),object(15)
    local state={activations=0,actual=actual,cdo=cdo,ability=ability,spec=spec}
    local target={kind="ability",ability_class=class.path,tag="Ability.Fixture",restriction_groups=groups}
    local choices=selected=="all" and kinds or selected and {selected} or {}
    local config={enabled=true,targets={attack=target},bindings={{id="attack",keys={key or "G"},trigger="press",
        restriction_bypasses=choices,outputs={"attack"}}}}
    local backend=setmetatable({config=config,state={ready=true},pawn=pawn},Backend)
    function backend:receiver(request) return request.receiver=="ability_system" and asc or input end
    function backend:object() return class end
    function backend:library() return class end
    function backend:target_failed() return false end
    function backend:target_succeeded() end
    function backend:fail_target(_,failure) error(failure) end
    function backend:report() end
    local function query(name,component)
        local value=param(actual[name] or 0)
        local hook=hooks["/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayTagCount"]
        if hook then hook.post(param(component or asc),value,param({TagName=FName(name)})) end
        return value:get()
    end
    state.query=query
    function backend:invoke(receiver,path,...)
        if path:match(":GetGameplayTagCount$") then return query((...).TagName:ToString()) end
        if path:match(":GetPlayTimelineTask$") then return object(16) end
        if path:match(":TryActivateExternalActionAbilityInterruptLowerByTag$") then
            for _,item in ipairs(required) do
                local exists=false
                for i=1,ability.ActivationRequiredTags.GameplayTags:GetArrayNum() do
                    if ability.ActivationRequiredTags.GameplayTags[i].TagName:ToString()==item then exists=true end
                end
                if exists and actual[item]<=0 then return false end
            end
            for i=1,ability.ActivationBlockedTags.GameplayTags:GetArrayNum() do
                if actual[ability.ActivationBlockedTags.GameplayTags[i].TagName:ToString()]>0 then return false end
            end
            current=ability
            for _,kind in ipairs(kinds) do
                local need=kind=="predecessor" and predecessor or "Required."..kind
                if query(need)==0 or query("Blocked."..kind)>0 then current=nil;return false end
            end
            current=nil;ability.bIsActive=true;spec.ActiveCount=1;state.activations=state.activations+1;return true
        end
        error(path)
    end
    return Router.new(config,backend),backend,state,target,config
end

for _,key in ipairs({"G","Gamepad_FaceButton_Left"}) do
    for _,kind in ipairs(kinds) do
        local router,backend,state,target,config=fixture(kind,key)
        router:step(0,{[key]=1})
        check(state.activations==1,"Selected restriction must be bypassed through Router/Backend/Costs: "..key..kind)
        check(state.cdo.ActivationRequiredTags.GameplayTags:GetArrayNum()==#kinds+1
            and state.cdo.ActivationBlockedTags.GameplayTags:GetArrayNum()==#kinds,"Native activation templates must restore synchronously")
        check(state.ability.ActivationRequiredTags.GameplayTags:GetArrayNum()==#kinds+1
            and state.ability.ActivationBlockedTags.GameplayTags:GetArrayNum()==#kinds,
            "The independent primary instance must restore its own required and blocked tags")
        check(state.actual["Required."..kind]==0 and state.actual["Blocked."..kind]==1,"Bypass must not change the player's actual tag state")
        current=state.ability
        check(state.query("Required."..kind)==1 and state.query("Blocked."..kind)==0,"Owned native continuation must retain the policy")
        check(state.query("Real.ProjectileReady")==1,"Real preparation state must remain native")
        local single=hooks["/Script/GameplayTags.GameplayTagAssetInterface:HasMatchingGameplayTag"]
        local value=param(false)
        single.pre(param(backend:receiver({receiver="ability_system"})),param({TagName=FName("Required."..kind)}))
        single.post(nil,value)
        check(value:get()==true,"Native gameplay-tag interface qualification must receive the owning policy")
        for _,mode in ipairs({"HasAnyMatchingGameplayTags"}) do
            local hook=hooks["/Script/GameplayTags.GameplayTagAssetInterface:"..mode]
            value=param(false)
            hook.pre(param(backend:receiver({receiver="ability_system"})),param(container({"Required."..kind})))
            hook.post(nil,value)
            check(value:get()==true,"Native container qualification must receive the owning policy: "..mode)
            value=param(true)
            hook.pre(param(backend:receiver({receiver="ability_system"})),param(container({"Blocked."..kind})))
            hook.post(nil,value)
            check(value:get()==false,"Native container blocking queries must hide only the selected blocker")
            value=param(true)
            hook.pre(param(object(88)),param(container({"Blocked."..kind})))
            hook.post(nil,value)
            check(value:get()==true,"Another actor's interface qualification must retain its native result")
        end
        check(state.query("Required."..kind,object(88))==0,"Another actor must retain its restrictions")
        current=object(99)
        check(state.query("Required."..kind)==0,"Another ability must not borrow restriction choices")
        current=nil
        backend.config.restriction_tags={["Training.ResourceThresholdLevel"]="training"}
        current=state.ability
        check(state.query("Training.ResourceThresholdLevel")==0,"Training bypass must not grant unrelated passive resource-threshold upgrades")
        current=nil
        state.ability.bIsActive=false;state.spec.ActiveCount=0
        config.bindings[1].restriction_bypasses={};router:reset(true)
        router:step(.1,{});router:step(.2,{[key]=1})
        check(state.activations==1,"Unchecked binding must retain original restriction after reuse")
        backend.resource_costs:reset()
    end
end
do
    local gate="CharacterState.AbilityState.Long.Buff.Lv3"
    for _,key in ipairs({"G","Gamepad_FaceButton_Left"}) do
        local router,backend,state,target=fixture("predecessor",key,gate)
        router:step(0,{[key]=1})
        check(state.activations==1 and state.actual[gate]==0,
            "Long-blade uppercut must bypass its three-stack entrance on the actual instance without granting a player buff")
        current=state.ability
        check(state.query(gate)==1,"The uppercut's own continuation must retain its selected entrance policy")
        current=nil;backend.resource_costs:reset()
    end
end

do
    local router,backend,state=fixture("all")
    state.actual["Real.ProjectileReady"]=0
    router:step(0,{G=1})
    check(state.activations==0,"Selecting every category must not fabricate projectile or target readiness")
    check(state.cdo.ActivationRequiredTags.GameplayTags:GetArrayNum()==#kinds+1,"Failed qualification must restore native tags")
    backend.resource_costs:reset()
end
do
    local _,backend,state,target,config=fixture()
    local cooldown="CharacterState.AbilityState.Dash.Cooldown"
    local forbidden="CharacterState.AbilityState.Dash.Forbid"
    local blocked=container({cooldown,forbidden})
    state.cdo.ActivationBlockedTags=blocked;state.ability.ActivationBlockedTags=blocked
    state.actual[cooldown]=1;state.actual[forbidden]=0
    config.restriction_tags={[cooldown]="cooldown"}
    config.bindings[1].restriction_bypasses={"cooldown"}
    local router=Router.new(config,backend)
    router:step(0,{G=1})
    check(state.activations==1,"A reflected native Dash cooldown must be classified without treating Forbid as cooldown")
    check(state.actual[cooldown]==1 and state.cdo.ActivationBlockedTags.GameplayTags:GetArrayNum()==2,
        "Cooldown bypass must preserve the native effect and restore activation fields")
    current=state.ability
    check(state.query(cooldown)==0 and state.query(forbidden)==0,"Only the tracked native cooldown gate may be relaxed")
    current=nil;state.ability.bIsActive=false;state.spec.ActiveCount=0
    state.actual[forbidden]=1
    router:step(.1,{});router:step(.2,{G=1})
    check(state.activations==1,"An actual Forbid state must still reject a cooldown-bypassed dodge")
    state.actual[forbidden]=0;config.bindings[1].restriction_bypasses={}
    router=Router.new(config,backend);router:step(.3,{G=1})
    check(state.activations==1,"An unchecked dodge must still observe its original cooldown")
    backend.resource_costs:reset()
end
do
    local _,backend,state,target=fixture("all")
    local cost=Costs.new(backend);backend.resource_costs=cost
    local input=backend:receiver({receiver="ability_input"})
    local class=state.ability:GetClass()
    cost:input({object=input,restriction_groups=target.restriction_groups},false,{restriction_bypasses=kinds})
    local hook=hooks["/Script/SGameInputSystem.AbilityInputComponent:TryActivateExternalActionAbilityByClass"]
    hook.pre(param(input),param(class),param(input))
    check(state.cdo.ActivationRequiredTags.GameplayTags:GetArrayNum()==1,
        "A native activation caused by a configured InputAction must receive the same scoped GAS policy")
    check(state.ability.ActivationRequiredTags.GameplayTags:GetArrayNum()==1,
        "InputAction activation must relax the independent primary instance as well as its template")
    state.ability.bIsActive=true;state.spec.ActiveCount=1
    hook.post(nil,param(true))
    check(state.cdo.ActivationRequiredTags.GameplayTags:GetArrayNum()==#kinds+1,"Native input activation must restore all GAS fields on return")
    check(state.ability.ActivationRequiredTags.GameplayTags:GetArrayNum()==#kinds+1,
        "InputAction activation must restore the primary instance's own GAS fields on return")
    current=state.ability
    check(state.query("Required.training")==1,"InputAction native continuation must retain selected restrictions")
    current=nil;cost:reset()
end
do
    local _,backend,state,target=fixture("all")
    local original=state.cdo.ActivationRequiredTags
    local ok=pcall(Restrictions.with,backend,target,{restriction_bypasses=kinds},function() error("activation failed") end)
    check(not ok and state.cdo.ActivationRequiredTags.GameplayTags:GetArrayNum()==original.GameplayTags:GetArrayNum(),
        "An activation exception must restore the shared native fields")
    check(state.ability.ActivationRequiredTags.GameplayTags:GetArrayNum()==original.GameplayTags:GetArrayNum(),
        "An activation exception must restore the independent primary instance")
end

do
    local _,backend,state,target=fixture("all")
    local names={}
    for _,tag in ipairs(state.cdo.ActivationRequiredTags.GameplayTags) do names[#names+1]=tag.TagName:ToString() end
    local replica=object(91,{class=state.ability:GetClass(),ActivationRequiredTags=container(names),
        ActivationBlockedTags=container({"Blocked.training"})})
    local unrelated=object(92,{class=object(93,{path="/Fixture.Other_C"}),
        ActivationRequiredTags=container(names),ActivationBlockedTags=container({"Blocked.training"})})
    state.spec.ReplicatedInstances=array({state.ability,replica})
    local asc=backend:receiver({receiver="ability_system"})
    asc.ActivatableAbilities.Items=array({state.spec,{Ability=unrelated,NonReplicatedInstances=array({}),ReplicatedInstances=array({})}})
    Restrictions.with(backend,target,{restriction_bypasses=kinds},function()
        check(replica.ActivationRequiredTags.GameplayTags:GetArrayNum()==1
            and replica.ActivationBlockedTags.GameplayTags:GetArrayNum()==0,
            "Replicated ability instances must receive the same scoped restriction policy")
        check(unrelated.ActivationRequiredTags.GameplayTags:GetArrayNum()==#kinds+1,
            "An unrelated granted ability must retain every native restriction")
    end)
    check(state.ability.ActivationRequiredTags.GameplayTags:GetArrayNum()==#kinds+1
        and replica.ActivationRequiredTags.GameplayTags:GetArrayNum()==#kinds+1,
        "Duplicate references to the same primary instance must not corrupt restoration")
end
do
    local router,backend,state,target,config=fixture("all")
    target.ability_class=nil
    config.bindings[1].trigger="down"
    router=Router.new(config,backend)
    local asc=backend:receiver({receiver="ability_system"})
    local originalSpec=state.spec
    local grants={originalSpec}
    for index=2,130 do
        local ability=object(1000+index)
        function ability:GetPropertyValue() error("Unrelated grants must remain in the native selector") end
        grants[index]={Ability=ability}
    end
    asc.ActivatableAbilities.Items=array(grants)
    local nativeCalls,templateReads,queries=0,0,0
    function backend.ability_specs(component,class,all,count,...)
        check(component==asc:GetAddress() and class==0 and not all and count==1 and (...)==target.tag,
            "Restriction selection must retain its exact tag request")
        nativeCalls=nativeCalls+1
        return "1 "
    end
    local read=state.cdo.GetPropertyValue
    function state.cdo:GetPropertyValue(name)
        if name=="ActivationRequiredTags" or name=="ActivationBlockedTags" then templateReads=templateReads+1 end
        return read(self,name)
    end
    local invoke=backend.invoke
    function backend:invoke(receiver,path,...)
        if path:match(":GetGameplayTagCount$") then queries=queries+1 end
        local hook=hooks[path]
        if not hook or not path:match(":TryActivateExternalActionAbilityInterruptLowerByTag$") then
            return invoke(self,receiver,path,...)
        end
        local args={...}
        hook.pre(param(receiver),param(args[1]),param(args[2]))
        local result=false
        if not state.ability.bIsActive then result=invoke(self,receiver,path,...) end
        hook.post(param(receiver),param(result))
        return result
    end
    router:step(0,{G=1})
    check(state.activations==1 and templateReads==2,
        "An explicit native activation must prepare required/blocked templates once across both hook layers")
    templateReads,nativeCalls=0,0
    for frame=1,120 do router:step(frame/60,{G=1}) end
    check(templateReads==240 and nativeCalls<=600 and backend.move_restriction_scopes==nil,
        "Repeated refused activations must use one native restriction selection and restore each synchronous scope")
    local any=hooks["/Script/GameplayTags.GameplayTagAssetInterface:HasAnyMatchingGameplayTags"]
    local active=backend.resource_costs.instances[state.ability:GetAddress()]
    current=state.ability
    local function query(names,original)
        queries=0
        local value=param(original)
        any.pre(param(asc),param(container(names)))
        any.post(nil,value)
        return value:get(),queries
    end
    active.restriction_bypasses={}
    local unrelated={"Real.ProjectileReady"}
    for index=2,16 do unrelated[index]="Unrelated.Query."..index end
    local result,count=query(unrelated,true)
    check(result and count==0,"A state without restriction policy must preserve native HasAny without re-querying tags")
    active.restriction_bypasses=kinds
    result,count=query(unrelated,true)
    check(result and count==0,"An unrelated native tag query must retain its result without duplicate reflection")
    result,count=query({"Required.training","Blocked.space"},false)
    check(result and count==0,"A selected required tag must satisfy HasAny without querying native counts")
    result,count=query({"Blocked.space"},true)
    check(not result and count==0,"An entirely bypassed blocker set must need no native queries")
    result,count=query({"Blocked.space","Real.ProjectileReady","Unrelated.Query"},true)
    check(result and count==1,"Remaining native tags must stop after the first match when bypassing blockers")
    result,count=query({"Blocked.space","Unrelated.Query"},false)
    check(not result and count==0,"A false native result cannot acquire a match by removing blockers")
    current=nil
    backend.resource_costs:reset()
end

print("MoveRestrictionRuntimeTests PASS "..checks)
