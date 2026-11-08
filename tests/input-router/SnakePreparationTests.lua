package.path="prototype/input-router/ue4ss/Scripts/?.lua;"..package.path
local Snake=require("snake-preparation")
local checks=0
local function check(value,message) checks=checks+1;assert(value,message) end
function FName(value) return {ToString=function() return value end} end
local function object(address,properties)
    local value=properties or {}
    function value:IsValid() return self.valid~=false end
    function value:GetAddress() return address end
    function value:GetPropertyValue(name) return self[name] end
    return value
end
local function array(values)
    return setmetatable({GetArrayNum=function() return #values end},{__index=values})
end
local function fixture()
    local root="/PBZCombatAsset/ActorData/Players/Hun/Abilities/SnakeDual/"
    local dbPath=root.."Attack/GA_Hun_SnakeDual_DB.GA_Hun_SnakeDual_DB_C"
    local passivePath=root.."Passive/GA_Hun_SnakeDual_Passive.GA_Hun_SnakeDual_Passive_C"
    local classes={[dbPath]=object(1),[passivePath]=object(2)}
    function StaticFindObject(path) return classes[path] end
    local db=object(3,{bIsActive=false,["Action Phase"]={TagName=FName("CharacterState.Action.Phase.Start")}})
    local passive=object(4,{bIsActive=true})
    local function grant(class,value)
        local cdo=object(value:GetAddress()+10)
        function cdo:GetClass() return classes[class] end
        function value:GetClass() return classes[class] end
        local spec={Ability=cdo,NonReplicatedInstances=array({value}),ReplicatedInstances=array({})}
        return setmetatable(spec,{__index=function(_,name) if name=="ActiveCount" then return value.bIsActive and 1 or 0 end end})
    end
    local asc=object(5,{ActivatableAbilities={Items=array({grant(dbPath,db),grant(passivePath,passive)})}})
    local state={starts=0,targets={},canceled=0,cleaned=0,rejected={},errors={},tags=0,normal=1}
    local backend={config={}}
    function backend:receiver() return asc end
    function backend:invoke(value,path,tag)
        if path:match(":GetGameplayTagCount$") then
            return tag.TagName:ToString()=="CharacterState.AbilityState.SnakeDual.Normal" and state.normal or state.tags
        end
        check(value==db and path:match(":K2_CancelAbility$"),"Only this preparation's native throw may be canceled")
        db.bIsActive=false;state.canceled=state.canceled+1
    end
    function backend:invoke_configured(value,path)
        check(value==passive and path==passivePath..":Force Cancel Projectile","Cleanup must use the owning passive's native projectile teardown")
        if state.cleanup_failure then error("native cleanup failed") end
        passive["SnakeDual Bullet"]=nil;state.tags=0;state.cleaned=state.cleaned+1
    end
    function backend:move_rejected(_,binding,reason) state.rejected[binding]=reason end
    function backend:fail_target(_,reason,binding) state.errors[binding]=reason end
    function backend:dispatch(target,phase,value,binding,bypass,settings)
        if target.ability_class==dbPath then
            check(target.activate_by_class and target.input_action=="/PBZInput/InputActions/IA_AttackH_Press.IA_AttackH_Press",
                "Projectile preparation must activate the original exact throw with its real input")
            state.starts=state.starts+1;state.throw_settings=settings;state.throw_bypass=bypass
            db.bIsActive=not state.refuse_throw
            return true,not state.refuse_throw
        end
        local handled,reason=Snake.prepare(self,target,phase,value,binding,bypass,settings)
        if handled~=nil then return true,handled,reason end
        state.targets[#state.targets+1]={target=target,settings=settings,bypass=bypass,binding=binding}
        return true,not state.refuse_target
    end
    function state:spawn()
        passive["SnakeDual Bullet"]=object(6);self.tags=1;self.normal=self.keep_sword and 1 or 0
    end
    function state:ready() db["Action Phase"]={TagName=FName("CharacterState.Action.Phase.Cancel")} end
    local target={kind="move",snake_projectile=true,move_id="move:Snake.RA",equipment_slot="Weapon.Slot.Main"}
    local settings={trigger="tap",restriction_bypasses={"predecessor"},damage_multiplier=2,startup_speed=3,recovery_speed=4}
    return backend,state,target,settings,db,passive
end
do
    local backend,state,target,settings=fixture()
    check(Snake.prepare(backend,target,"triggered",1,"A",true,settings)==true,"An absent projectile must queue its requested release attack")
    Snake.update(backend);check(#state.targets==0,"A label alone cannot complete projectile preparation")
    state:spawn();Snake.update(backend);check(#state.targets==0,"A spawned projectile must wait for the native throw's cancel window")
    state:ready();Snake.update(backend)
    check(#state.targets==1 and state.starts==1 and not backend.snake_preparation,"Native readiness must submit the requested move exactly once")
    check(state.targets[1].settings==settings and state.targets[1].bypass and state.throw_settings==settings and state.throw_bypass,
        "The original binding's separate multipliers and resource bypass must reach preparation and target")
    check(state.cleaned==0 and state.canceled==0,"An accepted release move must retain its real projectile")
end
do
    local backend,state,target,settings=fixture();state:spawn()
    check(Snake.prepare(backend,target,"triggered",1,"A",false,settings)==nil and state.starts==0,
        "An existing native projectile must be reused, including KeepSword's simultaneous Normal state")
    Snake.reset(backend);check(state.cleaned==0,"A reused projectile belongs to the original player action")
end
for _,outcome in ipairs({"canceled","reset","throw-refused","target-refused","ended-empty"}) do
    local backend,state,target,settings,db=fixture();state.refuse_throw=outcome=="throw-refused"
    local accepted,reason=Snake.prepare(backend,target,"triggered",1,"A",false,settings)
    if outcome=="throw-refused" then check(accepted==false and reason:find("refused",1,true),"Native throw refusal must be returned")
    elseif outcome=="canceled" then state:spawn();Snake.prepare(backend,target,"canceled",0,"A",false,settings)
    elseif outcome=="reset" then state:spawn();Snake.reset(backend)
    elseif outcome=="target-refused" then state.refuse_target=true;state:spawn();state:ready();Snake.update(backend)
    else db.bIsActive=false;Snake.update(backend);check(state.rejected.A:find("without a projectile",1,true),"A failed native throw must explain its missing object") end
    check(not backend.snake_preparation and state.cleaned==1,"Failure and cancellation must release the native projectile preparation")
end
do
    local backend,state,target,settings,db=fixture();db.bIsActive=true
    Snake.prepare(backend,target,"triggered",1,"A",false,settings);Snake.reset(backend)
    check(state.starts==0 and state.canceled==0 and state.cleaned==0,"A player's active native throw must never become cancellation-owned")
end
do
    local backend,state,target,settings=fixture()
    Snake.prepare(backend,target,"triggered",1,"A",false,settings)
    Snake.prepare(backend,target,"completed",0,"A",false,settings)
    check(backend.snake_preparation~=nil,"A released one-shot binding must complete native projectile preparation")
    state.cleanup_failure=true
    local ok=pcall(Snake.reset,backend)
    check(not ok and backend.snake_preparation~=nil,"A failed native cleanup must retain its state for a later cleanup attempt")
    state.cleanup_failure=false;Snake.reset(backend)
    check(not backend.snake_preparation,"Successful cleanup retry must release its preparation state")
end
do
    local backend,state,target,settings=fixture()
    Snake.prepare(backend,target,"triggered",1,"A",false,settings)
    local other={trigger="tap",restriction_bypasses={"predecessor"},damage_multiplier=7}
    Snake.prepare(backend,target,"triggered",1,"B",true,other)
    state:spawn();state:ready();Snake.update(backend)
    local found={};for _,request in ipairs(state.targets) do found[request.binding]=request end
    check(state.starts==1 and state.throw_settings==settings and not state.throw_bypass,"Concurrent requests must preserve the initial native throw's tuning")
    check(found.A.settings==settings and not found.A.bypass and found.B.settings==other and found.B.bypass,
        "Each queued release move must keep its own tuning and resource policy")
end
do
    local backend,state,target,settings,db,passive=fixture()
    target.snake_projectile,target.snake_normal=nil,true
    check(Snake.prepare(backend,target,"triggered",1,"A",false,settings)==nil and state.starts==0,
        "The equipped passive's native Normal state must satisfy a normal move without preparation")
    state:spawn()
    check(Snake.prepare(backend,target,"triggered",1,"A",false,settings)==true and state.starts==1,
        "An exact normal move must request the native DB retrieve when the short sword is thrown")
    state:ready();Snake.update(backend)
    check(#state.targets==0,"A native retrieve animation must not substitute for the actual Normal state")
    db.bIsActive=false;passive["SnakeDual Bullet"]=nil;state.tags=0;passive["Revive Bullet"]=object(20)
    Snake.update(backend)
    check(backend.snake_preparation and #state.targets==0,"Recall must wait for its real returning projectile after the retrieve ability ends")
    state.normal=1;passive["Revive Bullet"]=nil;Snake.update(backend)
    check(#state.targets==1 and not backend.snake_preparation and state.cleaned==0,
        "The exact normal move may start after native recall restores the two-sword state")
end
for _,outcome in ipairs({"cancel","throw-refused","target-refused"}) do
    local backend,state,target,settings=fixture()
    target.snake_projectile,target.snake_normal=nil,true
    state:spawn();state.refuse_throw=outcome=="throw-refused"
    Snake.prepare(backend,target,"triggered",1,"A",false,settings)
    if outcome=="cancel" then Snake.reset(backend)
    elseif outcome=="target-refused" then state:ready();state.normal=1;state.refuse_target=true;Snake.update(backend) end
    check(not backend.snake_preparation and state.cleaned==0,
        "Recall cancellation or refusal must never force-cancel the player's original projectile")
end
do
    local backend,state,target,settings=fixture()
    target.snake_projectile,target.snake_normal=nil,true
    state.keep_sword=true;state:spawn()
    check(Snake.prepare(backend,target,"triggered",1,"A",false,settings)==nil and state.starts==0,
        "KeepSword's real Normal state must remain usable while its real projectile is active")
end
print("SnakePreparationTests PASS "..checks)
