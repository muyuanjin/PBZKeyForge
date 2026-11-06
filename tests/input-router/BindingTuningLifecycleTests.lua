package.path = "prototype/input-router/ue4ss/Scripts/?.lua;" .. package.path
local checks,hooks,current = 0,{},nil
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
    owner=function(_,states,resolve)
        if current then return states[current:GetAddress()] or resolve(current),"Function /Fixture:Apply Shachi Cost And Endure" end
    end,reset=function() end
} end}
local Costs=require("resource-costs")
local Router=require("router")
local Backend=require("backend")
local initialize="/Script/SGameCombatSystem.BulletInterface:Initialize"
local modify="/Script/SGameAttributes.AttributeUtils:ModifyCharacterAttributeValue"
local play="/Script/LegoAbility.LegoAbility:PlayTimeline"
local forceClass="/Script/SGameInputSystem.AbilityInputComponent:ForceActivateExternalActionAbilityByClass"
local forceTag="/Script/SGameInputSystem.AbilityInputComponent:ForceActivateExternalActionAbilityByTag"
local function fixture()
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
    local backend=setmetatable({config={resource_inputs={}},state={ready=true},pawn=pawn,subsystem=object(9)},Backend)
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
    hooks[modify].pre(nil,parameter(backend.pawn),parameter({AttributeName="Shachi"}),parameter(0),value)
    return value:get()
end

-- A native release can occur on the entry callback or a later minimum-duration
-- event. It can also create an ungranted release instance during activation.
for _,path in ipairs({forceClass,forceTag,
    "/Script/SGameInputSystem.AbilityInputComponent:TryActivateExternalActionAbilityByClass",
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
        hooks[initialize].post(parameter(bullet),parameter(nil),parameter(backend.pawn),parameter(release))
        hooks[path].post(nil,parameter(true))
        check(cost.bullets[20].damage==3,"release projectile inherits damage on the actual attack instance")
        release['Action Phase']={TagName=FName("CharacterState.Action.Phase.Start")};cost:update()
        check(task.rate==2.5,"release startup inherits the requested speed")
        release['Action Phase']={TagName=FName("CharacterState.Action.Phase.Cancel")};cost:update()
        check(task.rate==5,"release recovery inherits the requested speed")
        release.bIsActive=false;cost:update()
        check(task.rate==1.25 and next(cost.instances)==nil,"release end restores speed and clears both ability scopes")
        check(cost.bullets[20].damage==3,"released projectile keeps its snapshot after handoff ends")
        current=release;release.bIsActive=true
        check(consume(backend,release)==-2,"later natural activation pays normally")
        hooks[initialize].post(parameter(bullet),parameter(nil),parameter(backend.pawn),parameter(release))
        check(cost.bullets[20].damage==1,"later natural projectile is unmodified")
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

-- Dodge and fixed PerfectDodge can share an ability even though their native
-- action identities differ. Stage both before the first input can activate it.
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
        local injections=0
        function backend:invoke(_,path,action,vector)
            if path:find(":GetPlayTimelineTask",1,true) then return end
            check(path:find(":InjectInputVectorForAction",1,true),"real backend dispatch injects the action")
            if vector.X==0 then return end
            injections=injections+1
            check(consume(self,source)==0,"first and later same-frame injections see merged resource settings")
            local state=cost.instances[source:GetAddress()]
            check(state.damage==3 and state.startup==2 and state.recovery==4,"all tuning reaches the shared ability regardless of binding order")
            source.bIsActive=true
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

-- Resolve settings across pipelines before any of them activates the ability.
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
    function backend:invoke(_,path,_,vector)
        check(path:find(":InjectInputVectorForAction",1,true),"pipeline input uses the actual backend")
        injections=injections+1
        local state=cost.instances[source:GetAddress()]
        check(state and state.damage==(canceled and 1 or .5),"tuning respects rule priority and excludes canceled vector sums")
        check(state.startup==(canceled and 1 or 2) and state.recovery==(canceled and 1 or 4),
            "one winning binding supplies all phase settings")
        check(consume(self,source)==(canceled and -2 or 0),"canceled input cannot donate its resource bypass")
        source.bIsActive=true
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
            local activations=0
            function backend:invoke(_,path,arg,vector)
                if path:find(":GetPlayTimelineTask",1,true) then return end
                if path:find(":InjectInputVectorForAction",1,true) and vector.X==0 then return end
                check(path:find(":InjectInputVectorForAction",1,true)
                    or path:find(":TryActivateExternalActionAbilityInterruptLowerByTag",1,true),"requests retain their native dispatch")
                local state=cost.instances[source:GetAddress()]
                check(state and state.damage==3 and state.startup==2 and state.recovery==4,
                    "ordinary ability requests cannot mask simultaneous action, ability or complete-move tuning")
                check(consume(self,source)==0,"all entry kinds share the prepared resource policy before activation")
                if source.bIsActive then return false end
                activations=activations+1;source.bIsActive=true;return true
            end
            local router=Router.new({targets=targets,bindings=reversed and {tuned,ordinary} or {ordinary,tuned}},backend)
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
                hooks[initialize].post(parameter(projectile),parameter(nil),parameter(backend.pawn),parameter(release))
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
            check(projectile and cost.bullets[21].damage==3,"compiled special entry's actual projectile retains 3x damage")
            release['Action Phase']={TagName=FName("CharacterState.Action.Phase.Start")};cost:update()
            check(task.rate==2.5,"compiled special entry applies startup tuning to its continuation")
            release['Action Phase']={TagName=FName("CharacterState.Action.Phase.Cancel")};cost:update()
            check(task.rate==5,"compiled special entry applies recovery tuning to its continuation")
            moves.reset(backend);cost:reset();routes=routes+1
        end
    end
    check(routes==5,"both ghost-blade release entries and all three weapon-switch wrappers execute")
end
print("BindingTuningLifecycleTests PASS "..checks)
