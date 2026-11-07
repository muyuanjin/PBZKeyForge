package.path = "prototype/input-router/ue4ss/Scripts/?.lua;" .. package.path
local checks,hooks,current = 0,{},nil
local nativeAbilities = {}
local function check(value,message) assert(value,message); checks=checks+1 end
local function parameter(value)
    return {get=function() return value end,set=function(_,replacement) value=replacement end}
end
local function array(values)
    return {ForEach=function(_,callback) for i,value in ipairs(values) do callback(i,parameter(value)) end end}
end
function FName(name) return {ToString=function() return name end} end
local function object(address,properties)
    local value=properties or {}
    function value:IsValid() return self.valid ~= false end
    function value:GetAddress() return address end
    function value:GetPropertyValue(name) return self[name] end
    function value:GetFullName() return "Class "..self.class end
    function value:GetClass() return object(address+10000,{class=self.class}) end
    function value:IsA(type) return self.ability and (type.class=="/Script/GameplayAbilities.GameplayAbility" or type.class=="/Script/LegoAbility.LegoAbility") end
    return value
end
local function tags(...)
    local entries={}
    for _,tag in ipairs({...}) do entries[#entries+1]={TagName=FName(tag)} end
    return {GameplayTags=array(entries)}
end
function RegisterHook(path,pre,post) hooks[path]={pre=pre,post=post};return 1,2 end
function UnregisterHook(path) hooks[path]=nil end
package.loaded["script-context"]={new=function() return {
    enable_damage=function() end,
    interface_hook=function(_,_,path,pre,post)
        return RegisterHook(path,function(context,...) return pre(context:get(),...) end,post)
    end,
    owner=function(_,states,resolve)
        if current then return states[current:GetAddress()] or resolve(current),"Function /Fixture:Apply Shachi Cost And Endure" end
    end,at=function() return nil,nil,"Function /Fixture:Apply Shachi Cost And Endure" end,
    ability_damage=function(_,instance,multiplier)
        local address=type(instance)=="number" and instance or instance:GetAddress()
        nativeAbilities[address]=multiplier~=1 and multiplier or nil
    end,
    reset=function() end,damage_bullet=function() end,update_damage=function() end
} end}
local Costs=require("resource-costs")
local Router=require("router")
local Backend=require("backend")
local initialize="/Script/SGameCombatSystem.BulletInterface:Initialize"
local function launch(backend,bullet,ability)
    hooks[initialize].pre(parameter(bullet),parameter(nil),parameter(backend.pawn),parameter(ability))
    bullet.damage=nativeAbilities[ability:GetAddress()] or 1
    check(not hooks[initialize].post,"Lua must not overwrite the native launch snapshot after initialization")
end
local modify="/Script/SGameAttributes.AttributeUtils:ModifyCharacterAttributeValue"
local play="/Script/LegoAbility.LegoAbility:PlayTimeline"
local forceClass="/Script/SGameInputSystem.AbilityInputComponent:ForceActivateExternalActionAbilityByClass"
local forceTag="/Script/SGameInputSystem.AbilityInputComponent:ForceActivateExternalActionAbilityByTag"
local function fixture()
    current=nil
    nativeAbilities={}
    local source=object(1,{ability=true,class="/Player.Start",bIsActive=false,AbilityTags=tags("Ability.Start")})
    local release=object(2,{ability=true,class="/Player.Release",bIsActive=false,AbilityTags=tags("Ability.Release")})
    local unrelated=object(3,{ability=true,class="/Player.Other",bIsActive=false,AbilityTags=tags("Ability.Other")})
    local specs={}
    local function grant(instance)
        local cdo=object(instance:GetAddress()+100,{class=instance.class,AbilityTags=instance.AbilityTags})
        specs[#specs+1]={Ability=cdo,ActiveCount=0,NonReplicatedInstances=array({instance}),ReplicatedInstances=array({})}
    end
    grant(source);grant(release);grant(unrelated)
    local asc=object(4,{ActivatableAbilities={Items=array(specs)}})
    local input,pawn=object(5),object(6)
    local timeline,task=object(7),object(8)
    task.Timeline=timeline
    local backend=setmetatable({config={resource_inputs={}},state={ready=true},pawn=pawn,subsystem=object(9),missing={},log=function() end},Backend)
    function backend:receiver(target) return target.receiver=="ability_input" and input or asc end
    function backend:object(path) return object(100,{class=path}) end
    function backend:invoke(value,path,rate)
        if path:find(":GetActor",1,true) then return value.actor end
        if path:find(":GetPlayTimelineTask",1,true) then return task end
        if path:find(":SetPlayRate",1,true) then task.rate=rate;return end
        error(path)
    end
    local cost=Costs.new(backend);backend.resource_costs=cost
    return backend,cost,source,release,unrelated,specs,grant,timeline,task
end
local function consume(backend,instance)
    current=instance
    local value=parameter(-2)
    if hooks[modify] then hooks[modify].pre(nil,parameter(backend.pawn),parameter({AttributeName="Shachi"}),parameter(0),value) end
    return value:get()
end

-- A native release can occur on the entry callback or a later minimum-duration
-- event. It can also create an ungranted release instance during activation.
for _,path in ipairs({forceClass,forceTag,
    "/Script/SGameInputSystem.AbilityInputComponent:ForceActivateExternalActionAbilityByClassWithEvent",
    "/Script/SGameInputSystem.AbilityInputComponent:TryActivateExternalActionAbilityByClass",
    "/Script/SGameInputSystem.AbilityInputComponent:TryActivateExternalActionAbilityByTag",
    "/Script/SGameInputSystem.AbilityInputComponent:TryActivateExternalActionAbilityInterruptLowerByTag",
    "/Script/GameplayAbilities.AbilitySystemComponent:TryActivateAbilityByClass",
    "/Script/GameplayAbilities.AbilitySystemComponent:TryActivateAbilitiesByTag"}) do
    for _,createdInside in ipairs({false,true}) do
        local backend,cost,source,release,other,specs,grant,timeline,task=fixture()
        if createdInside then table.remove(specs,2) end
        cost:activate({ability_class=source.class},true,function() source.bIsActive=true;return true end,
            {damage_multiplier=3,startup_speed=2,recovery_speed=4})
        cost:update()
        current=source
        local receiver=backend:receiver({receiver=path:find("AbilitySystemComponent",1,true) and "ability_system" or "ability_input"})
        local argument=path:find("ByClass",1,true) and release:GetClass()
            or path:find("AbilitiesByTag",1,true) and tags("Ability.Release") or {TagName=FName("Ability.Release")}
        hooks[path].pre(parameter(receiver),parameter(argument))
        source.bIsActive=false
        if createdInside then grant(release) end
        check(consume(backend,release)==0,"release inherits resource bypass before its first fee: "..path)
        check(consume(backend,other)==-2,"unrelated abilities do not inherit the request")
        release.bIsActive=true
        hooks[play].pre(parameter(release),parameter(timeline),parameter(1.25))
        local bullet=object(20)
        launch(backend,bullet,release)
        hooks[path].post(nil,parameter(true))
        check(bullet.damage==3,"release projectile inherits damage on the actual attack instance")
        release['Action Phase']={TagName=FName("CharacterState.Action.Phase.Start")};cost:update()
        check(task.rate==2.5,"release startup inherits the requested speed")
        release['Action Phase']={TagName=FName("CharacterState.Action.Phase.Cancel")};cost:update()
        check(task.rate==5,"release recovery inherits the requested speed")
        release.bIsActive=false;cost:update()
        check(task.rate==1.25 and next(cost.instances)==nil,"release end restores speed and clears both ability scopes")
        check(bullet.damage==3,"released projectile keeps its snapshot after handoff ends")
        current=release;release.bIsActive=true
        check(consume(backend,release)==-2,"later natural activation pays normally")
        launch(backend,bullet,release)
        check(bullet.damage==1,"later natural projectile is unmodified")
        cost:reset()
    end
end

do
    local backend,cost,source,release,other=fixture()
    cost:activate({ability_class=source.class},true,function() source.bIsActive=true;return true end,{damage_multiplier=3})
    current=source
    local hook=hooks[forceClass]
    hook.pre(parameter(backend:receiver({receiver="ability_input"})),parameter(release:GetClass()))
    hook.post(nil,parameter(true))
    check(not cost.instances[release:GetAddress()] and #cost.transfers==0,"failed transfer leaves no armed release")
    release.bIsActive=true
    hook.pre(parameter(backend:receiver({receiver="ability_input"})),parameter(release:GetClass()))
    hook.post(nil,parameter(true))
    check(not cost.instances[release:GetAddress()],"already running destination cannot borrow the source settings")
    release.bIsActive=false
    hook.pre(parameter(object(999)),parameter(release:GetClass()))
    check(consume(backend,release)==-2,"another actor's activation cannot inherit player settings")
    hook.post(nil,parameter(true))
    release.bIsActive=false
    local pending=cost:arm(release,false,{damage_multiplier=2})
    current=source
    hook.pre(parameter(backend:receiver({receiver="ability_input"})),parameter(release:GetClass()))
    check(cost.instances[release:GetAddress()].damage==3,"native continuation replaces an inactive input preparation")
    hook.post(nil,parameter(false))
    check(cost.instances[release:GetAddress()]==pending and pending.damage==2,"failed continuation restores the independently prepared input")
    cost:reset()
end

do
    local backend,cost,source,release,other,specs=fixture()
    release.AbilityTags=tags("Ability.Release","Ability.Required")
    other.AbilityTags=tags("Ability.Release")
    specs[2].Ability.AbilityTags=release.AbilityTags;specs[3].Ability.AbilityTags=other.AbilityTags
    cost:arm(source,true,{damage_multiplier=3});source.bIsActive=true;current=source
    local entry=hooks["/Script/GameplayAbilities.AbilitySystemComponent:TryActivateAbilitiesByTag"]
    entry.pre(parameter(backend:receiver({receiver="ability_system"})),parameter(tags("Ability.Release","Ability.Required")))
    check(consume(backend,release)==0 and consume(backend,other)==-2,"multi-tag activation requires every requested ability tag")
    entry.post(nil,parameter(false))
    check(not cost.instances[release:GetAddress()] and not cost.instances[other:GetAddress()],"rejected multi-tag activation leaves no destination scope")
    cost:reset()
end

-- Input requests retain their policy until the native activation supplies its
-- actual InputAction; sharing an ability does not share a binding's tuning.
for _,reversed in ipairs({false,true}) do
    for _,mode in ipairs({"same-action","shared-ability","distinct-pipelines"}) do
        local backend,cost,source,release=fixture()
        local tag="Ability.Action.Hun.PerfectDodge"
        source.AbilityTags=tags(tag)
        local asc=backend:receiver({receiver="ability_system"})
        asc.ActivatableAbilities.Items:ForEach(function(_,p)
            if p:get().Ability.class==source.class then p:get().Ability.AbilityTags=source.AbilityTags end
        end)
        local a,b="/PBZDeflectSystem/Input/IA_Dodge","/PBZDeflectSystem/Input/IA_PerfectDodge"
        backend.config.resource_inputs={[a]={tag},[b]={tag}}
        local targets={dodge={kind="action",path=a..".IA_Dodge",value_type="bool"},
            perfect={kind="action",path=b..".IA_PerfectDodge",value_type="bool"}}
        if mode=="distinct-pipelines" then
            targets.perfect.path=targets.dodge.path
            targets.perfect.pipeline="modified"
        end
        function backend:pipeline_identity(target) return target.pipeline or "" end
        local ordinary={id="ordinary",keys={"LeftShift"},outputs={mode=="same-action" and "dodge" or "perfect"}}
        local tuned={id="tuned",keys={"LeftShift"},bypass_resources=true,damage_multiplier=3,startup_speed=2,recovery_speed=4,outputs={"dodge"}}
        local bindings=reversed and {tuned,ordinary} or {ordinary,tuned}
        local policy=bindings[1]
        local injections=0
        function backend:invoke(_,path,action,vector)
            if path:find(":GetPlayTimelineTask",1,true) then return end
            check(path:find(":InjectInputVectorForAction",1,true),"real backend dispatch injects the action")
            if vector.X==0 then return end
            injections=injections+1
            current=nil
            if injections==1 then check(next(cost.instances)==nil,"input injection never pre-arms shared abilities") end
            hooks[forceTag].pre(parameter(self:receiver({receiver="ability_input"})),parameter({TagName=FName(tag)}),parameter(action))
            check(consume(self,source)==(policy.bypass_resources and 0 or -2),"only the first successful native request supplies resource policy")
            local state=cost.instances[source:GetAddress()]
            check(state.damage==(policy.damage_multiplier or 1) and state.startup==(policy.startup_speed or 1)
                and state.recovery==(policy.recovery_speed or 1),"a later binding cannot change an active ability's policy")
            source.bIsActive=true
            hooks[forceTag].post(nil,parameter(true))
        end
        local objects,nextAddress={},100
        function backend:object(path)
            if not objects[path] then nextAddress=nextAddress+1;objects[path]=object(nextAddress,{class=path}) end
            return objects[path]
        end
        local router=Router.new({targets=targets,bindings=bindings},backend)
        router:step(0,{LeftShift=1})
        check(injections==(mode=="same-action" and 1 or 2),"shared actions merge while different actions and pipelines retain their native injections")
        cost:update();source.bIsActive=false;cost:update()
        router:step(.1,{})
        check(next(cost.instances)==nil,"release clears the previous activation's tuning")
        cost:reset()
    end
end

-- A higher-priority ordinary pipeline owns the resulting native action.
-- Opposing vectors can remove a pipeline entirely; that input grants no tuning.
for _,canceled in ipairs({false,true}) do
    local backend,cost,source=fixture()
    local action="/PBZDeflectSystem/Input/IA_Dodge"
    source.AbilityTags=tags("Ability.Start")
    backend.config.resource_inputs={[action]={"Ability.Start"}}
    local targets={positive={kind="action",path=action..".IA_Dodge",pipeline="opposing"},
        negative={kind="action",path=action..".IA_Dodge",pipeline="opposing"},
        sibling={kind="action",path=action..".IA_Dodge",pipeline="sibling"}}
    local bindings={
        {id="ordinary",keys={"J"},priority=20,outputs={"sibling"}},
        {id="tuned",keys={"G"},priority=10,bypass_resources=true,damage_multiplier=.5,startup_speed=2,recovery_speed=4,
            outputs={{target="positive",value=1}}},
        {id="opposite",keys={"H"},outputs={{target="negative",value=-1}}},
    }
    if not canceled then
        bindings[#bindings+1]={id="lower-priority",keys={"K"},damage_multiplier=3,startup_speed=5,recovery_speed=6,outputs={"sibling"}}
    end
    function backend:pipeline_identity(target) return target.pipeline end
    function backend:object(path) return object(100,{class=path}) end
    local injections=0
    function backend:invoke(_,path,actionObject,vector)
        check(path:find(":InjectInputVectorForAction",1,true),"pipeline input uses the actual backend")
        injections=injections+1
        current=nil
        hooks[forceTag].pre(parameter(self:receiver({receiver="ability_input"})),parameter({TagName=FName("Ability.Start")}),parameter(actionObject))
        local state=cost.instances[source:GetAddress()]
        check(state and state.damage==1,"ordinary priority cannot borrow another pipeline's damage")
        check(state.startup==1 and state.recovery==1,"ordinary priority cannot borrow another pipeline's phase speeds")
        check(consume(self,source)==-2,"ordinary priority cannot borrow another pipeline's resource bypass")
        source.bIsActive=true
        hooks[forceTag].post(nil,parameter(true))
    end
    local router=Router.new({targets=targets,bindings=bindings},backend)
    router:step(0,canceled and {G=1,H=1,J=1} or {G=1,J=1,K=1})
    check(injections==(canceled and 1 or 2),"only nonzero pipelines inject input")
    cost:reset()
end

for _,otherKind in ipairs({"action","ability","move"}) do
    for _,tunedAbility in ipairs({false,true}) do
        for _,reversed in ipairs({false,true}) do
            local backend,cost,source=fixture()
            local action="/PBZInput/InputActions/IA_Test"
            backend.config.resource_inputs={[action]={"Ability.Start"}}
            local targets={ability={kind="ability",tag="Ability.Start",input_action=action},
                other={kind=otherKind,tag="Ability.Start",path=action..".IA_Test",input_action=action,value_type="bool"}}
            local ordinary={id="ordinary",keys={"G"},outputs={tunedAbility and "other" or "ability"}}
            local tuned={id="tuned",keys={"H"},bypass_resources=true,damage_multiplier=3,startup_speed=2,recovery_speed=4,
                outputs={tunedAbility and "ability" or "other"}}
            local bindings=reversed and {tuned,ordinary} or {ordinary,tuned}
            local policy=otherKind=="action" and (tunedAbility and tuned or ordinary) or bindings[1]
            local activations=0
            function backend:invoke(_,path,arg,vector)
                if path:find(":GetPlayTimelineTask",1,true) then return end
                if path:find(":InjectInputVectorForAction",1,true) and vector.X==0 then return end
                check(path:find(":InjectInputVectorForAction",1,true)
                    or path:find(":TryActivateExternalActionAbilityInterruptLowerByTag",1,true),"requests retain their native dispatch")
                current=nil
                local entry=hooks["/Script/SGameInputSystem.AbilityInputComponent:TryActivateExternalActionAbilityInterruptLowerByTag"]
                entry.pre(parameter(self:receiver({receiver="ability_input"})),parameter({TagName=FName("Ability.Start")} ),
                    parameter(path:find(":InjectInputVectorForAction",1,true) and arg or self:object(action)))
                local state=cost.instances[source:GetAddress()]
                check(state and state.damage==(policy.damage_multiplier or 1) and state.startup==(policy.startup_speed or 1)
                    and state.recovery==(policy.recovery_speed or 1),"the successful binding alone supplies the activation's multipliers")
                check(consume(self,source)==(policy.bypass_resources and 0 or -2),"simultaneous entry kinds cannot donate resource policy")
                local accepted=not source.bIsActive
                if accepted then activations=activations+1;source.bIsActive=true end
                entry.post(nil,parameter(accepted))
                return accepted
            end
            local router=Router.new({targets=targets,bindings=bindings},backend)
            router:step(0,{G=1,H=1})
            check(activations==1,"simultaneous bindings activate the shared ability once")
            cost:update();source.bIsActive=false;cost:update()
            router:step(.1,{})
            check(next(cost.instances)==nil,"all entry kinds release their activation's prepared settings")
            cost:reset()
        end
    end
end

do
    local backend,cost,source=fixture()
    local action=object(201,{class="/Input/Tuned.Tuned"})
    local target={kind="action",object=action,value_type="bool"}
    local settings={damage_multiplier=3,startup_speed=2,recovery_speed=4}
    local router=Router.new({targets={tuned=target},bindings={
        {id="tuned",keys={"G"},bypass_resources=true,damage_multiplier=3,startup_speed=2,recovery_speed=4,outputs={"tuned"}}
    }},backend)
    local protected,injections=false,0
    function backend:invoke(receiver,path,argument,vector)
        if path:find(":InjectInputVectorForAction",1,true) then
            if vector.X==0 then return end
            injections=injections+1;current=nil
            hooks[forceTag].pre(parameter(self:receiver({receiver="ability_input"})),
                parameter({TagName=FName("Ability.Start")}),parameter(argument))
            local state=cost.instances[source:GetAddress()]
            check(state and state.damage==(protected and 1 or 3) and state.startup==(protected and 1 or 2)
                and state.recovery==(protected and 1 or 4),"a retained native pipeline keeps the complete InputAction policy at defaults")
            check(consume(self,source)==(protected and -2 or 0),"dispatch cannot overwrite the retained native pipeline's normal resource policy")
            source.bIsActive=true;hooks[forceTag].post(nil,parameter(true));return
        end
        check(path:find(":TryActivateExternalActionAbilityInterruptLowerByTag",1,true),"explicit requests keep their native activation entry")
        check(consume(self,source)==0 and cost.instances[source:GetAddress()].damage==3,
            "a retained native InputAction does not disable an explicit binding's tuning")
        source.bIsActive=true;return true
    end
    local cases={
        {{action="/Input/Tuned",key="MouseScrollUp",owner="uncovered"}},
        {{action="/Input/Tuned",key="PBZ_Touchpad_SwipeUp",owner="game"}},
        {{action="/Input/Tuned",key="G",owner="scheduler"}},
        {{action="/Input/Tuned",key="None",owner="uncovered"}},
        {},
        {{action="/Input/Other",key="MouseScrollUp",owner="uncovered"}},
    }
    for index,coverage in ipairs(cases) do
        source.bIsActive=false;cost:update();current=nil
        backend.native_input={coverage=coverage,apply_masks=function() end}
        protected=index<=2
        router:step(index,{G=1})
        if protected then
            for _,kind in ipairs({"ability","move"}) do
                source.bIsActive=false;current=nil
                check(backend:dispatch({kind=kind,tag="Ability.Start",move_id="start",input_action="/Input/Tuned.Tuned"},
                    "triggered",{x=1,y=0,z=0},"explicit",true,settings),"explicit "..kind.." remains eligible with a retained native action")
            end
        end
    end
    check(injections==#cases,"native row removal and scheduler takeover restore the action binding's tuning")
    cost:reset()
end

do
    local backend,cost,source,release,other=fixture()
    local tunedAction=object(201,{class="/Input/Tuned.Tuned"})
    local ordinaryAction=object(202,{class="/Input/Ordinary.Ordinary"})
    local settings={damage_multiplier=3,startup_speed=2,recovery_speed=4}
    cost:input({object=tunedAction},true,settings,2)
    cost:input({object=ordinaryAction},false,{},1)
    check(next(cost.instances)==nil,"action policies cannot change any instance before native activation")
    local entry=hooks[forceTag]
    local receiver=parameter(backend:receiver({receiver="ability_input"}))
    entry.pre(receiver,parameter({TagName=FName("Ability.Release")}),parameter(ordinaryAction))
    check(consume(backend,release)==-2,"a same-key ordinary action pays its own resources")
    check(cost.instances[release:GetAddress()].damage==1,"an ordinary action sharing the key cannot borrow damage")
    release.bIsActive=true;entry.post(nil,parameter(true));current=nil
    entry.pre(receiver,parameter({TagName=FName("Ability.Start")}),parameter(tunedAction))
    check(consume(backend,source)==0,"the matching InputAction receives the requested resource bypass")
    check(consume(backend,other)==-2,"a different ability cannot borrow that InputAction's bypass")
    source.bIsActive=true;entry.post(nil,parameter(true))
    local projectile=object(203)
    launch(backend,projectile,source)
    check(projectile.damage==3,"the successful binding's projectile captures its damage")
    source.bIsActive,release.bIsActive=false,false;cost:update()
    check(next(cost.instances)==nil,"ended abilities release all per-binding tuning")
    current=nil
    entry.pre(receiver,parameter({TagName=FName("Ability.Start")}),parameter(tunedAction))
    check(consume(backend,source)==-2 and not cost.instances[source:GetAddress()],
        "the original operation uses defaults when no enhanced binding requested this frame")
    source.bIsActive=true;entry.post(nil,parameter(true))
    check(projectile.damage==3,"an existing projectile retains its snapshot across later original operations")
    local naturalProjectile=object(204)
    launch(backend,naturalProjectile,source)
    check(naturalProjectile.damage==1 and projectile.damage==3,
        "original and enhanced projectiles retain independent damage snapshots")
    cost:reset()
end

do
    local backend,cost,source,release,other=fixture()
    local settings={damage_multiplier=3,startup_speed=2,recovery_speed=4}
    check(cost:activate({ability_class=source.class},true,function()
        check(consume(backend,source)==0,"a requested activation can evaluate its own waived fee")
        return false
    end,settings)==false,"native eligibility rejection is retained")
    check(next(cost.instances)==nil and #cost.transfers==0,"a rejected binding leaves no tuning for another request")
    cost:activate({ability_class=source.class},false,function()
        check(consume(backend,source)==-2,"an ordinary binding following a rejection pays normally")
        source.bIsActive=true;return true
    end,{})
    local original=cost.instances[source:GetAddress()]
    cost:activate({ability_class=source.class},true,function() return false end,settings)
    check(cost.instances[source:GetAddress()]==original and original.damage==1 and not original.bypass,
        "a rejected tuned binding cannot change an already active ordinary action")
    check(consume(backend,release)==-2 and not cost.instances[release:GetAddress()],"an unrelated move retains its own resource policy")
    source.bIsActive=false;cost:update()
    local ok=pcall(function()
        cost:activate({ability_class=source.class},true,function() error("activation failure") end,settings)
    end)
    check(not ok and next(cost.instances)==nil and #cost.transfers==0,"an activation exception restores its parameter scope")
    cost:reset()
end

do
    local backend,cost,source,release,other,specs,grant=fixture()
    table.remove(specs,1)
    cost:activate({ability_class=source.class},true,function()
        grant(source)
        check(consume(backend,source)==0,"an instance created inside activation receives only that request's policy")
        source.bIsActive=true;return true
    end,{damage_multiplier=3})
    check(cost.instances[source:GetAddress()].damage==3,"dynamically created instances retain their successful activation's damage")
    cost:reset()
end

do
    local backend,cost,source,release,other,specs,grant,timeline,task=fixture()
    local target={ability_class=source.class}
    local function start(settings,bypass)
        return cost:activate(target,bypass,function() source.bIsActive=true;return true end,settings)
    end
    start({damage_multiplier=3,startup_speed=2,recovery_speed=4},true)
    hooks[play].pre(parameter(source),parameter(timeline),parameter(1.25))
    source['Action Phase']={TagName=FName("CharacterState.Action.Phase.Start")};cost:update()
    check(task.rate==2.5,"the first binding applies its requested phase speed")
    source.bIsActive=false
    cost:activate(target,false,function()
        local state=cost.instances[source:GetAddress()]
        check(state and state.damage==1 and state.startup==1 and state.recovery==1,
            "a same-frame restart replaces the completed binding's entire policy")
        check(task.rate==1.25 and consume(backend,source)==-2,
            "same-frame restart restores the previous timeline rate and normal resource fees")
        source.bIsActive=true;return true
    end,{})
    source.bIsActive=false
    cost:activate(target,true,function() return false end,{damage_multiplier=2})
    check(next(cost.instances)==nil,"rejecting a same-frame restart cannot resurrect a completed activation's tuning")
    start({damage_multiplier=3},true)
    source.bIsActive=false;current=nil
    local entry=hooks[forceClass]
    entry.pre(parameter(backend:receiver({receiver="ability_input"})),parameter(source:GetClass()))
    check(consume(backend,source)==-2 and not cost.instances[source:GetAddress()],
        "an original activation immediately after enhanced action end uses defaults before the next update")
    source.bIsActive=true;entry.post(nil,parameter(true))
    cost:reset()
end

for _,kind in ipairs({"ability","move"}) do
    local backend,cost,source=fixture()
    local requests=0
    function backend:invoke(_,path)
        check(path:find(":TryActivateExternalActionAbilityInterruptLowerByTag",1,true),"same-key bindings retain normal native activation")
        requests=requests+1
        local fee=consume(self,source)
        check(fee==(requests==1 and -2 or 0),"each same-key request evaluates its own resource policy")
        if fee<0 then return false end
        source.bIsActive=true;return true
    end
    local router=Router.new({targets={shared={kind=kind,tag="Ability.Start",move_id="start",input_action="/Input/Test"}},bindings={
        {id="ordinary",keys={"G"},priority=20,outputs={"shared"}},
        {id="tuned",keys={"G"},priority=10,bypass_resources=true,damage_multiplier=3,startup_speed=2,recovery_speed=4,outputs={"shared"}},
    }},backend)
    router:step(0,{G=1})
    check(requests==2,"a rejected ordinary binding allows the independently eligible tuned binding to try")
    local state=cost.instances[source:GetAddress()]
    check(state and state.bypass and state.damage==3 and state.startup==2 and state.recovery==4,
        "the accepted binding retains its complete policy after an earlier rejection")
    cost:reset()
end

do
    local config=assert(loadfile((os.getenv("PBZ_RESOURCE_BINDING_FIXTURES") or "dist/resource-binding-check").."/compiled-tuning-chains.lua"))()
    local moves=require("complete-moves")
    local routes=0
    for name,target in pairs(config.targets) do
        if target.kind=="move" then
            local settings
            for _,binding in ipairs(config.bindings) do
                for _,output in ipairs(binding.outputs) do
                    if (type(output)=="table" and output.target or output)==name then settings=binding end
                end
            end
            check(settings and settings.damage_multiplier==3 and settings.startup_speed==2 and settings.recovery_speed==4,
                "compiled special entry retains all three tuning settings")
            local backend,cost,source,release,other,specs,grant,timeline,task=fixture()
            source.class=target.ability_class;specs[1].Ability.class=source.class
            source.AbilityTags=tags(target.tag);specs[1].Ability.AbilityTags=source.AbilityTags
            local switch=target.move_id:find("move:WeaponSwitch.",1,true)
            local originalInvoke=backend.invoke
            local delayed,projectile
            local function transfer()
                current=source
                local path=switch and forceTag or forceClass
                local argument=switch and {TagName=FName("Ability.Release")} or release:GetClass()
                hooks[path].pre(parameter(backend:receiver({receiver="ability_input"})),parameter(argument))
                check(consume(backend,release)==0,"compiled release retains resource bypass on the attack instance")
                source.bIsActive,specs[1].ActiveCount=false,0
                release.bIsActive,specs[2].ActiveCount=true,1
                hooks[play].pre(parameter(release),parameter(timeline),parameter(1.25))
                projectile=object(21)
                launch(backend,projectile,release)
                hooks[path].post(nil,parameter(true))
            end
            function backend:invoke(value,path,arg,...)
                if path:find(":GetGameplayTagCount",1,true) then
                    for _,ignored in ipairs(target.ignored_tags or {}) do if arg.TagName:ToString()==ignored then return 0 end end
                    return 1
                end
                local initial=target.activate_by_class and "/Script/SGameInputSystem.AbilityInputComponent:TryActivateExternalActionAbilityByClass"
                    or "/Script/SGameInputSystem.AbilityInputComponent:TryActivateExternalActionAbilityInterruptLowerByTag"
                if path==initial then
                    current=nil;hooks[path].pre(parameter(value),parameter(arg))
                    source.bIsActive,specs[1].ActiveCount=true,1
                    if switch then transfer() end
                    hooks[path].post(nil,parameter(true))
                    current=source
                    return true
                end
                if path==target.entry_point then
                    if target.move_id:find("EarlyRelease",1,true) then delayed=transfer else transfer() end
                    return
                end
                return originalInvoke(self,value,path,arg,...)
            end
            current=nil
            check(moves.dispatch(backend,target,"triggered",true,settings),"compiled special entry reaches its normal source activation")
            if delayed then cost:update();delayed() end
            check(projectile and projectile.damage==3,"compiled special entry's actual projectile retains 3x damage")
            release['Action Phase']={TagName=FName("CharacterState.Action.Phase.Start")};cost:update()
            check(task.rate==2.5,"compiled special entry applies startup tuning to its continuation")
            release['Action Phase']={TagName=FName("CharacterState.Action.Phase.Cancel")};cost:update()
            check(task.rate==5,"compiled special entry applies recovery tuning to its continuation")
            moves.reset(backend);cost:reset();routes=routes+1
        end
    end
    check(routes==5,"both ghost-blade release entries and all three weapon-switch wrappers execute")
end
do
    local backend,cost,source = fixture()
    local foreign = object(900,{ability=true,class=source.class,bIsActive=false})
    cost:activate({ability_class=source.class},true,function()
        current=foreign
        local value=parameter(-2)
        hooks[modify].pre(nil,parameter(object(901)),parameter({AttributeName="Shachi"}),parameter(0),value)
        check(value:get()==-2 and not cost.instances[foreign:GetAddress()],"observing another actor's fee cannot arm its same-class ability")
        local query=hooks["/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayAttributeValue"]
        local original=parameter(0)
        check(query.post(parameter(object(902)),original,parameter({AttributeName="Shachi"}))==nil and original:get()==0
            and not cost.instances[foreign:GetAddress()],"observing another ability system cannot inherit a bypass request")
        return false
    end)
    cost:reset()
end
do
    local backend,cost,source,_,other = fixture()
    local target={ability_class=source.class}
    cost:arm(other,false,{damage_multiplier=17})
    check(cost:activate(target,false,function()
        check(nativeAbilities[source:GetAddress()]==7,"native initialization sees this binding's damage before activation executes")
        source.bIsActive=true
        return true
    end,{damage_multiplier=7}),"native damage snapshot activation")
    local previous=cost.instances[source:GetAddress()]
    Costs.with_active(backend,source,false,function()
        check(nativeAbilities[source:GetAddress()]==11,"active callbacks publish their own damage before native work")
        return true
    end,{damage_multiplier=11},true)
    check(cost.instances[source:GetAddress()]==previous and nativeAbilities[source:GetAddress()]==7,
        "callback completion restores the active ability's original native snapshot")
    check(not pcall(function()
        Costs.with_active(backend,source,false,function() error("callback failure") end,{damage_multiplier=13},true)
    end) and nativeAbilities[source:GetAddress()]==7,"callback failure also restores the prior native snapshot")
    source.bIsActive=false
    cost:restore(source:GetAddress())
    check(nativeAbilities[source:GetAddress()]==nil and nativeAbilities[other:GetAddress()]==17,
        "ending one activation clears its native snapshot without affecting another ability")
    cost:activate(target,false,function()
        check(nativeAbilities[source:GetAddress()]==3,"failed requests still arm before the native call")
        return false
    end,{damage_multiplier=3})
    check(nativeAbilities[source:GetAddress()]==nil,"failed activation removes its native snapshot")
    cost:activate(target,false,function()
        check(nativeAbilities[source:GetAddress()]==nil,"ordinary reuse cannot inherit a previous binding's native damage")
        return false
    end)
    local pending=cost:arm(source,false,{damage_multiplier=5})
    cost:activate(target,false,function()
        check(nativeAbilities[source:GetAddress()]==9,"a nested request temporarily publishes its own damage")
        return false
    end,{damage_multiplier=9})
    check(cost.instances[source:GetAddress()]==pending and nativeAbilities[source:GetAddress()]==5,
        "rejected nested transfers restore the pending activation's native snapshot")
    cost:suspend()
    check(nativeAbilities[source:GetAddress()]==5,"temporary input suspension preserves native activation snapshots")
    cost:reset()
    check(next(nativeAbilities)==nil,"hard reset clears every native activation snapshot")
end
do
    local _,cost,source = fixture()
    cost:arm(source,false,{damage_multiplier=6})
    source.valid=false
    function source:GetAddress() error("destroyed abilities must be cleared by the saved address") end
    cost:update()
    check(next(nativeAbilities)==nil,"destroyed abilities clear native snapshots without dereferencing their UObject")
    cost:reset()
end
print("BindingTuningLifecycleTests PASS "..checks)
