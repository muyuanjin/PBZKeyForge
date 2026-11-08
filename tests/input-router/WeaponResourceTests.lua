package.path="prototype/input-router/ue4ss/Scripts/?.lua;"..package.path
local checks,hooks,callbacks,objects=0,{},{},{}
local resources,actions,bullets={},{},{}
local current,asc,pawn,watched,prepared,reset=nil,nil,nil,{},false,false
local outerFunction,outerOwner
local function check(value,message) assert(value,message);checks=checks+1 end
local function parameter(value) return {get=function() return value end,set=function(_,nextValue) value=nextValue end} end
local function array(values) return setmetatable({GetArrayNum=function() return #values end},{__index=values}) end
function FName(value) return {ToString=function() return value end} end
local function object(path,address,properties)
    local value=properties or {}
    value.path=path
    function value:IsValid() return self.valid~=false end
    function value:GetAddress() return address end
    function value:GetFullName() return "Object "..path end
    function value:GetPropertyValue(name) return self[name] end
    function value:GetClass() return self.class end
    function value:IsA(class) return self.class==class or class==objects["/Script/GameplayAbilities.GameplayAbility"] end
    objects[path]=value
    return value
end
local nextAddress=100
local function loaded(path)
    if not objects[path] then nextAddress=nextAddress+1;object(path,nextAddress) end
    return objects[path]
end
function StaticFindObject(path) return (assert(objects[path],path)) end
function NotifyOnNewObject(path,callback) callbacks[path]=callback end
function FindObjects(_,class)
    check(class==objects["/Script/SGameCombatSystem.BulletActorBase"] or class==objects["/Script/SGameCombatSystem.BulletObjectBase"],
        "weapon resources discover bullet families without installing damage calculators")
    return {}
end
function RegisterHook(path,pre,post) hooks[path]={pre=pre,post=post};return 1,2 end
function UnregisterHook(path) hooks[path]=nil end
package.loadlib=function(_,name)
    if name=="pbz_script_context_enable" then return function() end end
    if name=="pbz_script_context_at" then return function(depth)
        if current and depth==1 then return current:GetFullName(),current:GetAddress(),"Function /Fixture:Attack" end
        if current and depth==2 and outerFunction then return current:GetFullName(),outerOwner or current:GetAddress(),outerFunction end
    end end
    if name=="pbz_script_context_watch_interface" then return function(address) watched[address]=true end end
    if name=="pbz_script_context_interface_object" then return function() error("owned resource queries use their native address") end end
    if name=="pbz_script_context_interface_address" then return function() return asc:GetAddress() end end
    if name=="pbz_ability_spec_indices" then return function() return "1" end end
    if name=="pbz_resource_prepare_actions" then return function(execute,getter)
        check(execute==loaded("/Script/LegoAbility.LegoAbility:ExecuteAction"):GetAddress()
            and getter==loaded("/Script/LegoAbility.LGAAction:GetAbility"):GetAddress(),"native action dispatch uses its reflected owner functions")
        prepared=true;return 1
    end end
    if name=="pbz_resource_watch_event" then return function(event,trigger)
        check(event==loaded("/Script/PBZCombatAsset.Default__BulletEvent_FengLeiConsumeEnergy"):GetAddress()
            and trigger==loaded("/Script/SGameCombatSystem.BulletEventBase:Trigger"):GetAddress(),"only the original pure energy cost event is intercepted")
        return 1
    end end
    if name=="pbz_resource_watch_action" then return function(address)
        check(prepared,"native action checks are prepared before an action is registered")
        actions[address]=true;return 1
    end end
    if name=="pbz_resource_ability" then return function(address,bypass) resources[address]=bypass or nil;return 1 end end
    if name=="pbz_damage_prepare_bullet" then return function(address)
        check(address==loaded("/Script/SGameCombatSystem.BulletInterface:Initialize"):GetAddress(),"resources prepare native bullet initialization")
        return 1
    end end
    if name=="pbz_damage_watch_bullet" then return function(address,base,interface,initialize)
        check(base==loaded("/Script/SGameCombatSystem.BulletActorBase"):GetAddress()
            and interface==loaded("/Script/SGameCombatSystem.BulletInterface"):GetAddress()
            and initialize==loaded("/Script/SGameCombatSystem.BulletInterface:Initialize"):GetAddress(),"new native bullets retain their resource launch policy")
        bullets[address]=true;return 1
    end end
    if name=="pbz_damage_forget_bullet" then return function(address) bullets[address]=nil;return 1 end end
    if name=="pbz_damage_reset" then return function() resources={};actions={};bullets={};reset=true;return 1 end end
    error("unexpected native resource import "..name)
end
for _,path in ipairs({"/Script/SGameCombatSystem.BulletActorBase","/Script/SGameCombatSystem.BulletObjectBase",
    "/Script/SGameCombatSystem.BulletInterface","/Script/SGameCombatSystem.BulletInterface:Initialize"}) do loaded(path) end
local path="/PBZCombatAsset/ActorData/Players/Hun/Abilities/BaiShanFengLeiJian/Default/GA_Hun_FengLeiJian_AAABB.GA_Hun_FengLeiJian_AAABB_C"
local class=object(path,1)
local ability=object("/Fixture.Ability",2,{class=class,bIsActive=false,AbilityTags={GameplayTags=array({})}})
local cdo=object("/Fixture.CDO",3,{class=class,AbilityTags={GameplayTags=array({})}})
local spec={Ability=cdo,ActiveCount=0,NonReplicatedInstances=array({ability}),ReplicatedInstances=array({})}
asc=object("/Fixture.ASC",4,{ActivatableAbilities={Items=array({spec})}})
pawn=object("/Fixture.Pawn",5)
local action=object("/Fixture.Action",6)
local timeline=object("/Fixture.Timeline",7,{Actions=array({{Action=action},{}})})
local names=array({FName("Main")})
local backend={pawn=pawn}
function backend:receiver() return asc end
function backend:report() end
function backend:object(path) return loaded(path) end
function backend:invoke(value,fn)
    if fn:find(":GetAllTimelineNames",1,true) then check(value==ability,"each binding registers its own timeline actions");return names end
    if fn:find(":GetTimelineByName",1,true) then return timeline end
    error(fn)
end
local Costs=require("resource-costs")
local cost=Costs.new(backend)
local target={ability_class=path}
local wind="CharacterState.AbilityState.BaiShanFengLeiJian.Wind"
local thunder="CharacterState.AbilityState.BaiShanFengLeiJian.Thunder"
local getTag="/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayTagCount"
local function tag(name,actual)
    local value=parameter(actual or 0)
    hooks[getTag].post(parameter(asc),value,parameter({TagName=FName(name)}))
    return value:get()
end
check(cost:activate(target,true,function()
    current=ability
    check(resources[ability:GetAddress()] and actions[action:GetAddress()],"full weapon resources are armed before the first native activation callback")
    check(tag(wind)==1 and tag(thunder)==1,"the owning attack's Blueprint checks see full wind and thunder")
    check(tag("CharacterState.WeaponUpgrade.Missing")==0,"resource bypass preserves unrelated release qualifications")
    local hasAll="/Script/GameplayTags.GameplayTagAssetInterface:HasAllMatchingGameplayTags"
    local values=parameter({GameplayTags=array({{TagName=FName(wind)},{TagName=FName(thunder)}})})
    hooks[hasAll].pre(nil,values);local result=parameter(false);hooks[hasAll].post(nil,result)
    check(result:get(),"all-resource Blueprint branches accept both full energy states")
    ability.bIsActive=true
    current=nil
    return true
end),"resource activation succeeds")
local bullet=object("/Fixture.Bullet",8,{class=objects["/Script/SGameCombatSystem.BulletActorBase"]})
callbacks["/Script/SGameCombatSystem.BulletActorBase"](bullet)
check(bullets[8],"resource snapshots also cover bullets constructed after activation")
ability.bIsActive=false;cost:update()
check(not resources[2],"native resource policy ends with the actual attack")
check(cost:activate(target,false,function()
    current=ability
    check(tag(thunder)==0 and not resources[2],"another binding to the same attack retains its real resource state")
    current=nil;return false
end)==false,"native refusal remains a refusal")
check(cost:activate(target,true,function() return false end)==false and not resources[2],"failed activation restores its resource policy")
local hold="/PBZCombatAsset/ActorData/Players/Hun/Abilities/BaiShanFengLeiJian/Hold/GA_Hun_FengLeiJian_B_Hold.GA_Hun_FengLeiJian_B_Hold_C"
local holdClass=object(hold,1)
ability.class,cdo.class=holdClass,holdClass;target.ability_class=hold
check(cost:activate(target,true,function()
    current=ability
    for _,energy in ipairs({0,.5,1}) do
        local actual=energy==1 and 1 or 0
        outerFunction="Function "..hold..":Added_1CC942FE4424EBA0DBE0A292363B2DFA"
        local ending=tag(thunder,actual)>0 and "EndFail" or nil
        check(not ending,"zero, partial and full energy enter native release preparation")
        outerFunction="Function "..hold..":OnTick_F49F244647F3D911EDC36F8CD34569D0"
        ending=tag(thunder,actual)>0 and "EndSuccess" or "Charging"
        check(ending=="EndSuccess","zero, partial and full energy select the successful native release")
    end
    outerOwner=999
    check(tag(thunder)==0,"another ability's Tick cannot choose this binding's successful release")
    outerOwner,outerFunction,current=nil,nil,nil
    return false
end)==false,"held release failure cleans up the full-energy state")
check(cost:activate(target,false,function()
    current=ability
    outerFunction="Function "..hold..":Added_1CC942FE4424EBA0DBE0A292363B2DFA"
    check(tag(thunder,1)==1,"ordinary full energy keeps its native held-state entry")
    outerFunction="Function "..hold..":OnTick_F49F244647F3D911EDC36F8CD34569D0"
    check(tag(thunder,0)==0,"ordinary zero energy keeps its original charging Tick")
    outerFunction,current=nil,nil
    return false
end)==false,"ordinary held release retains its real resource choices")
bullet.valid=false;cost:update()
check(not bullets[8],"destroyed bullets release their native resource snapshot")
cost:reset()
check(reset and not next(resources),"shutdown restores native resource hooks and policies")
print("WeaponResourceTests PASS "..checks)
