package.path = "prototype/input-router/ue4ss/Scripts/?.lua;"..package.path
local Backend,Router = require("backend"),require("router")
local Inputs = require("move-inputs")
local checks=0
local function check(value,message) checks=checks+1; assert(value,message) end
function FName(value) return value end
local function object(value)
    value=value or {}; function value:IsValid() return true end; function value:GetAddress() return self end
    return value
end
local function fixture(trigger,nativeTarget,nativeBinding,grantCount)
    local action,component,subsystem=object(),object(),object()
    local nativeValue=object()
    local state={requested=false,held=false,active=false,starts=0,stops=0,activations=0,shots=0}
    local condition=nativeTarget and nativeTarget.native_release_when or {}
    local class = object()
    function class:GetFullName() return "Class "..(nativeTarget and (nativeTarget.ability_class or nativeTarget.native_preparation_class) or "/Fixture.Ability_C") end
    function StaticFindObject() return class end
    local timeline = object()
    local task = object()
    function task:GetPropertyValue(name) return name=="Timeline" and timeline or state.time or 0 end
    local ability,instance = object(),object()
    function ability:GetPropertyValue(name)
        if name=="AbilityTags" then
            local values={{TagName={ToString=function() return nativeTarget and nativeTarget.tag or "Ability.Fixture" end}}}
            function values:GetArrayNum() return #self end
            return {GameplayTags=values}
        end
    end
    function ability:GetClass() state.class_reads=(state.class_reads or 0)+1;return class end
    function instance:GetClass() return class end
    function instance:GetPropertyValue(name)
        if name=="bIsActive" then return state.active end
        if name=="Action Phase" then return nil end
        if name=="Variables" then return {ForEach=function(_,callback)
            if state.prepared and condition.object_variable then
                callback({get=function() return {ToString=function() return condition.object_variable.key end} end},
                    {get=function() return object() end})
            end
        end} end
        return condition.property_minimum and condition.property_minimum[name] or 0
    end
    local instances = {instance}; function instances:GetArrayNum() return #self end
    local empty = {}; function empty:GetArrayNum() return 0 end
    local spec = {Ability=ability,NonReplicatedInstances=instances,ReplicatedInstances=empty}
    local grants={spec}
    for index=2,grantCount or 1 do
        local unrelated,otherClass=object(),object()
        function unrelated:GetClass() state.class_reads=(state.class_reads or 0)+1;return otherClass end
        function unrelated:GetPropertyValue() error("Unrelated grant must stay inside native selection") end
        grants[index]={Ability=unrelated,ActiveCount=0,NonReplicatedInstances=empty,ReplicatedInstances=empty}
    end
    local specs = setmetatable({GetArrayNum=function() return #grants end},{__index=grants})
    function specs:ForEach(callback)
        for index,item in ipairs(grants) do callback(index,{get=function() return item end}) end
    end
    component.ActivatableAbilities = {Items=specs}
    setmetatable(spec,{__index=function(_,name) if name=="ActiveCount" then return state.active and 1 or 0 end end})
    local backend=setmetatable({config={enabled=true},state={ready=true},subsystem=subsystem,pawn=object()},Backend)
    function backend:object(path)
        if nativeTarget and nativeTarget.equipment_configs and path==nativeTarget.equipment_configs[1] then
            local config=object();function config:GetFullName() return "Object "..path end;return config
        end
        return action
    end
    function backend:receiver() return component end
    function backend:library() return object() end
    function backend:target_failed() return false end
    function backend:target_succeeded() end
    function backend:fail_target(_,failure) error(failure) end
    local context={start_hold_input=function(_,owner,heldAction)
        check(owner==subsystem and heldAction==action,"Native Hold bridge must receive the exact subsystem and action")
        state.requested=true;state.starts=state.starts+1
    end}
    backend.resource_costs={context=context,activate=function(_,_,_,callback) return callback() end}
    function backend:invoke(_,path,...)
        if path=="/Script/EnhancedInput.EnhancedInputLibrary:MakeInputActionValueOfType" then
            error("Opaque FInputActionValue must not be returned through Lua reflection")
        elseif path:match(":StartContinuousInputInjectionForAction$") then
            error("Continuous native Hold must use the opaque native bridge")
        elseif path:match(":StopContinuousInputInjectionForAction$") then
            state.requested=false; state.stops=state.stops+1
        elseif path:match(":GetSGameInputComponent$") then return component
        elseif path:match(":IsHolding$") then return state.held
        elseif path:match(":GetComponentByClass$") then return component
        elseif path:match(":GetCurrentWeaponIndex$") then return 0
        elseif path:match(":GetSlotWeapon$") then
            local args={...};args[3].WeaponConfig=backend:object(nativeTarget.equipment_configs[1]);return true
        elseif path:match(":GetGameplayTagCount$") then
            local tag=(...).TagName
            for _,required in ipairs(condition.required_tags or {}) do if tag==required then return state.prepared and 1 or 0 end end
            return 1
        elseif condition.object_variable and path==condition.object_variable.getter then
            local data=object();function data:GetFullName() return "Object "..condition.object_variable.object end;return data
        elseif condition.object_variable and path==condition.object_variable.status_getter then return condition.object_variable.status
        elseif path:match(":GetPlayTimelineTask$") then return task
        elseif path:match(":GetTimelineByName$") then return state.other and object() or timeline
        elseif path:match(":K2_CancelAbility$") then state.active=false; state.canceled=true
        elseif path:match(":TryActivateExternalActionAbilityInterruptLowerByTag$") or path:match(":TryActivateExternalActionAbilityByClass$") then
            if not state.held or state.active or state.refuse then return false end
            state.activations=state.activations+1; state.active=true; return true
        else error(path) end
    end
    function state:advance()
        if self.held and not self.requested and self.active then self.shots=self.shots+1; self.active=false end
        self.held=self.requested
    end
    local target={kind="ability",tag="Ability.Action.Hun.ShachiAbility.DuYanBow.Laser",
        native_hold_inputs={"/PBZInput/InputActions/IA_ShachiAbility_Hold.IA_ShachiAbility_Hold"}}
    local config={targets={laser=target},bindings={{id="laser",keys={"G"},trigger=trigger or "press",default_trigger=true,outputs={"laser"}}}}
    if nativeTarget then
        target=nativeTarget;config.targets.laser=target
        local binding={};for name,value in pairs(nativeBinding) do binding[name]=value end
        binding.outputs={"laser"};binding.trigger=trigger;config.bindings={binding}
        backend.resource_costs={context=context,activate=function(_,request,bypass,callback,settings)
            state.settings,state.bypass=settings,bypass
            return callback()
        end}
    end
    if grantCount then
        function backend.ability_specs(owner,wanted,all,count,...)
            check(owner==component:GetAddress() and not all and (wanted==class:GetAddress() and count==0 or wanted==0 and count==1 and (...)==target.tag),
                "Native preparation must select its exact current class")
            state.native_calls=(state.native_calls or 0)+1
            return "1 "
        end
    end
    backend.config=config
    return Router.new(config,backend),backend,state,target,config
end

do
    local path=os.getenv("PBZ_NATIVE_HOLD_CONFIG") or "dist/restrictions-final-check/native-holds.lua"
    local compiled=assert(loadfile(path))()
    local count=0
    for _,binding in ipairs(compiled.bindings) do
        local output=binding.outputs[1]
        local target=compiled.targets[type(output)=="table" and output.target or output]
        check(target.native_hold_inputs and target.native_release_when,"Every compiled native Hold needs its audited release readiness")
        local router,backend,state=fixture("tap",target,binding)
        state.other=true
        local key=binding.keys[1]
        router:step(0,{[key]=1});router:step(.001,{})
        check(state.activations==0,"Compiled keyboard/controller activation must wait for native Hold")
        state:advance();router:step(.01,{})
        check(state.activations==1 and state.stops==0,"A compiled one-shot must retain its real prepared ability: "..tostring(target.move_id or target.tag)
            .." activations="..state.activations.." stops="..state.stops)
        state.time=target.native_release_when.minimum_time or 0;state.prepared=true;state.other=false
        router:step(.02,{});state:advance()
        check(state.shots==1 and state.starts==state.stops,"The compiled entrance must release one actual prepared attack")
        check(state.bypass==true and state.settings.damage_multiplier==binding.damage_multiplier
            and state.settings.startup_speed==binding.startup_speed and state.settings.recovery_speed==binding.recovery_speed
            and #state.settings.restriction_bypasses==6,"The native preparation handoff must retain this binding's independent tuning")
        router:reset(true);count=count+1

        local identity=tostring(target.move_id or target.tag).." / "..key
        for _,outcome in ipairs({"refused","reset-preparing","reset-active","ended"}) do
            router,backend,state=fixture("tap",target,binding)
            state.other=true;state.refuse=outcome=="refused"
            router:step(0,{[key]=1});router:step(.001,{})
            if outcome~="reset-preparing" then state:advance();router:step(.01,{}) end
            if outcome=="ended" then
                state.active=false;router:step(.02,{})
            else router:reset(true) end
            check(state.starts==1 and state.stops==1 and not next(backend.native_move_inputs or {})
                and not next(backend.complete_move_inputs or {}),
                "Native Hold must clean up after "..outcome..": "..identity)
            check(state.activations==((outcome=="reset-active" or outcome=="ended") and 1 or 0)
                and not state.canceled,"Cleanup must preserve native ability ownership: "..identity)
            state:advance();router:step(.03,{});router:reset(true)
            check(state.starts==1 and state.stops==1,"Cleanup must not replay a one-shot: "..identity)
        end

        if target.native_release_when.before_time then
            router,backend,state=fixture("tap",target,binding)
            state.other=true
            router:step(0,{[key]=1});router:step(.001,{})
            state:advance();router:step(.01,{})
            state.other=false;state.time=target.native_release_when.before_time
            router:step(.02,{})
            check(state.stops==1 and (state.canceled==true)==(target.native_release_when.cancel_on_expire==true),
                "A missed one-shot release window must apply its native cancellation policy: "..identity)
            router:reset(true)
        end

        router,backend,state=fixture("tap",target,binding)
        state.other=true
        local settings={trigger="tap"}
        Inputs.dispatch(backend,target,"triggered",1,"first",false,settings)
        Inputs.dispatch(backend,target,"triggered",1,"second",false,settings)
        check(state.starts==1 and state.stops==0,"Independent bindings must share one native injection: "..identity)
        state:advance();Inputs.prepare(backend,{})
        check(state.activations==1 and state.stops==0 and next(backend.native_move_inputs),
            "A refused binding must retain the active binding's Hold: "..identity)
        state.time=target.native_release_when.minimum_time or 0;state.prepared=true;state.other=false
        Inputs.prepare(backend,{});state:advance()
        check(state.shots==1 and state.starts==1 and state.stops==1
            and not next(backend.native_move_inputs) and not next(backend.complete_move_inputs),
            "The final shared owner must release exactly once: "..identity)
        Inputs.reset(backend)

        router,backend,state=fixture("down",target,binding,130)
        state.other=true
        router:step(0,{[key]=1});state:advance();router:step(.01,{[key]=1})
        state.class_reads,state.native_calls=0,0
        for frame=1,120 do router:step(.01+frame/60,{[key]=1}) end
        check(state.class_reads<=240 and state.native_calls<=240,
            "Held preparation must read only native-selected grants, not the full 130-item list: "..identity)
        check(state.starts==1 and state.stops==0 and state.activations==1,
            "Native filtering must retain the real pending charge: "..identity)
        router:reset(true)
    end
    check(count==28,"The current native preparation inventory covers fourteen entrances on both devices")
end

for _,trigger in ipairs({"tap","release","press","down"}) do
    local router,backend,state,target=fixture(trigger)
    target.native_preparation_class="/Fixture.Laser_C"
    target.native_release_when={timeline="Loop",minimum_time=.056}
    router:step(0,{G=1})
    if trigger=="tap" or trigger=="release" then router:step(.001,{}) end
    state:advance(); router:step(.01,(trigger=="tap" or trigger=="release") and {} or {G=1})
    router:step(.02,{})
    check(state.requested and state.shots==0,"Early release must retain native Hold until projectile events: "..trigger)
    state.time=.055; router:step(.03,{})
    check(state.requested,"Both eye events must finish before native release: "..trigger)
    state.time=.056; router:step(.04,{}); state:advance()
    check(state.shots==1 and state.stops==1,"Prepared input must release exactly once: "..trigger)
end
do
    local router,backend,state,target=fixture("press")
    target.native_preparation_class="/Fixture.Normal_C"
    target.native_release_when={timeline="Main",minimum_time=1,before_time=2,cancel_on_expire=true}
    router:step(0,{G=1}); state:advance(); router:step(.01,{G=1})
    state.time=3; router:step(.02,{}) ; state:advance()
    check(state.shots==1 and not state.canceled,"A real longer physical hold must retain its higher charge tier")
end
do
    local router,backend,state,target=fixture("press")
    target.native_preparation_class="/Fixture.Car_C"
    target.native_release_when={timeline="Spawn",minimum_time=1.843}
    router:step(0,{G=1});state:advance();router:step(.01,{G=1})
    state.time=1.843;router:step(.02,{G=1})
    state.other=true;router:step(.03,{G=1});router:step(.04,{}) ;state:advance()
    check(state.shots==1,"Native CanRelease readiness must survive a later transition from Spawn to Drive")
end
do
    local router,backend,state,target,config=fixture("press")
    config.bindings[1].default_trigger=false
    router=Router.new(config,backend)
    target.native_preparation_class="/Fixture.Normal_C"
    target.native_release_when={timeline="Main",minimum_time=1}
    router:step(0,{G=1});state:advance();router:step(.01,{G=1})
    check(state.requested,"An explicit press must retain preparation until its finite release is ready")
    state.time=1;router:step(.02,{G=1});state:advance();router:step(.03,{G=1})
    check(state.shots==1 and state.starts==1,"An explicit press releases once while the physical key remains held")
end
do
    local router,backend,state,target=fixture("tap")
    target.native_preparation_class="/Fixture.Laser_C"
    target.native_release_when={timeline="Loop",minimum_time=.056}
    router:step(0,{G=1}); router:step(.001,{}); state:advance(); router:step(.01,{})
    state.other=true; state.active=false; router:step(.02,{})
    check(state.stops==1,"A single-shot Main branch that ends without Loop must release its input ownership")
end

for _,trigger in ipairs({"press","down","hold","toggle","tap","release"}) do
    local router,backend,state,target,config=fixture(trigger)
    config.bindings[1].seconds=.01
    router:step(0,{})
    router:step(.1,{G=1})
    if trigger=="tap" or trigger=="release" then router:step(.105,{})
    elseif trigger=="hold" then router:step(.12,{G=1}) end
    check(state.starts==1 and state.activations==0,"Activation must wait for actual native Hold: "..trigger)
    state:advance()
    router:step(.13,(trigger=="tap" or trigger=="release") and {} or {G=1})
    check(state.activations==1,"Prepared native Hold must activate the ability: "..trigger)
    if trigger~="tap" and trigger~="release" then
        router:step(.14,{G=1}); router:step(.15,{G=1})
        check(state.stops==0,"A refused repeat must not release a charge already running: "..trigger)
        if trigger=="toggle" then router:step(.16,{}); router:step(.17,{G=1}) else router:step(.16,{}) end
    end
    state:advance()
    check(state.shots==1 and state.starts==state.stops,"Release must finish the original native attack exactly once: "..trigger)
    router:reset(true)
end
do
    local router,backend,state=fixture("press")
    router:step(0,{G=1}); router:step(.01,{})
    state:advance(); router:step(.02,{})
    check(state.activations==0 and state.starts==state.stops,"Release before native input is ready must cancel a held request")
end
do
    local router,backend,state=fixture("tap")
    router:step(0,{G=1}); router:step(.01,{})
    state:advance(); router:reset(true); router:step(.02,{})
    check(state.activations==0 and state.starts==state.stops,"Reset must discard a queued one-shot without firing it")
end
do
    local router,backend,state=fixture("press")
    state.refuse=true
    router:step(0,{G=1}); state:advance(); router:step(.01,{G=1}); router:step(.02,{})
    check(state.activations==0 and state.starts==state.stops,"Failed qualification must release all virtual Hold ownership")
end
print("NativeMoveInputTests PASS "..checks)
