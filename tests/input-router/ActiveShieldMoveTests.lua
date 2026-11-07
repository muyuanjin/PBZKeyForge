package.loaded["script-context"] = {new=function() error("Unexpected tuning in an untuned fixture") end}
package.path = "prototype/input-router/ue4ss/Scripts/?.lua;" .. package.path
local Moves = require("complete-moves")
local checks = 0
local function check(value, message)
    assert(value, message)
    checks = checks + 1
end
function FName(value) return {ToString=function() return value end} end
local function array(items)
    return {ForEach=function(_, callback)
        for index, item in ipairs(items) do callback(index,{get=function() return item end}) end
    end}
end
local class = "/PBZCombatAsset/ActorData/Players/Hun/Abilities/ShachiAbility/ZhuoBiDun/GA_ZhuoBiDun.GA_ZhuoBiDun_C"
local physical = "PhysicalTotal_5_DCBBD9C24A42FAEF773E3380E04B6FEB"
local shachi = "ShachiTotal_7_91654B4148BC8D0C41424AA11B894507"
local move = {kind="move",ability_class=class,active_ability_classes={class},entry_point=class..":ToRelease",
    entry_when={timeline="Loop",property_sum_above={properties={{"ElementalChargeData",physical},
        {"ElementalChargeData",shachi}},threshold_property="Thredhold"}}}
local function object(address, properties)
    local value = properties or {}
    function value:IsValid() return self.valid ~= false end
    function value:GetAddress() return address end
    function value:GetPropertyValue(name) return self[name] end
    function value:GetClass() return {GetFullName=function() return "BlueprintGeneratedClass "..(self.class or class) end} end
    return value
end
local function fixture()
    local cdo = object(1)
    local instance = object(2,{bIsActive=true,Thredhold=100,ElementalChargeData={[physical]=60,[shachi]=50}})
    local spec = {Ability=cdo,ActiveCount=1,NonReplicatedInstances=array({instance}),ReplicatedInstances=array({instance})}
    local component = object(3,{ActivatableAbilities={Items=array({spec})}})
    local loop, other = object(4), object(5)
    local task = object(6,{Timeline=loop,CurrentTime=1})
    local backend = {released=0}
    function backend:receiver(target)
        check(target.receiver=="ability_system","Active shield must inspect its owner's ASC")
        return component
    end
    function backend:object(path)
        check(path==move.entry_point,"Only the original release callback is resolved")
        return object(7)
    end
    function backend:invoke(target,path,arg)
        check(target==instance,"Only the granted active owner can be inspected or released")
        if path=="/Script/LegoAbility.LegoAbility:GetPlayTimelineTask" then return task end
        if path=="/Script/LegoAbility.LegoAbility:GetTimelineByName" then
            check(arg:ToString()=="Loop","The shield must still be holding")
            return loop
        end
        if path==move.entry_point then
            if self.fail then error("native release failure") end
            self.released=self.released+1
            task.Timeline=other
            return
        end
        error("Active shield must never activate, cancel, inject input or reset charge: "..path)
    end
    return backend,instance,spec,task,other,cdo
end
do
    local backend,instance = fixture()
    check(Moves.dispatch(backend,move,"triggered") and backend.released==1,
        "Combined physical and shachi absorption above the native threshold must release once")
    check(instance.ElementalChargeData[physical]==60 and instance.ElementalChargeData[shachi]==50,
        "Original absorbed charge must reach the native release unchanged")
    check(not Moves.dispatch(backend,move,"triggered") and backend.released==1,
        "Holding the binding must not replay an already entered counterattack")
    Moves.reset(backend)
    check(instance.bIsActive,"Reset must preserve the naturally owned shield ability")
end
for _,amount in ipairs({0,99,100}) do
    local backend,instance = fixture()
    instance.ElementalChargeData[physical]=amount
    instance.ElementalChargeData[shachi]=0
    check(not Moves.dispatch(backend,move,"triggered") and backend.released==0 and instance.bIsActive,
        "Equal or insufficient real absorption must preserve the shield without releasing")
end
for _,condition in ipairs({"inactive","inactiveSpec","cdo","unrelated","otherTimeline","missingTask"}) do
    local backend,instance,spec,task,other,cdo = fixture()
    if condition=="inactive" then instance.bIsActive=false
    elseif condition=="inactiveSpec" then spec.ActiveCount=0
    elseif condition=="cdo" then spec.NonReplicatedInstances=array({cdo});spec.ReplicatedInstances=array({cdo})
    elseif condition=="unrelated" then instance.class="/Other/Shield_C"
    elseif condition=="otherTimeline" then task.Timeline=other
    else task.valid=false end
    check(not Moves.dispatch(backend,move,"triggered") and backend.released==0,
        "Only an existing active qualified shield in Loop can counter: "..condition)
end
do
    local backend,instance = fixture();backend.fail=true
    check(not pcall(Moves.dispatch,backend,move,"triggered") and instance.bIsActive,
        "A callback error must not cancel or reset a naturally owned shield")
    check(not backend.complete_move_pending,"Active release must not acquire pending lifecycle ownership")
end
print("PASS: "..checks.." active shield counter checks")
