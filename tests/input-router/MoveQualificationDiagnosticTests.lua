package.path="prototype/input-router/ue4ss/Scripts/?.lua;"..package.path
local Moves=require("complete-moves")
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
    return setmetatable({GetArrayNum=function() return #values end,ForEach=function(_,callback)
        for index,value in ipairs(values) do callback(index,{get=function() return value end}) end
    end},{__index=values})
end
local path="/Fixture/Ability.Ability_C"
local function fixture()
    local class=object(1)
    function class:GetFullName() return "Class "..path end
    local cdo,instance=object(2),object(3,{bIsActive=false})
    function cdo:GetClass() return class end
    function instance:GetClass() return class end
    local spec={Ability=cdo,ActiveCount=0,NonReplicatedInstances=array({instance}),ReplicatedInstances=array({})}
    local asc=object(4,{ActivatableAbilities={Items=array({spec})}})
    local backend={calls=0,tags={},pawn=object(5),component=asc,receiver_object=object(6),granted=true}
    function StaticFindObject() return not backend.class_missing and class or nil end
    function backend:receiver(request)
        if request.receiver=="ability_system" then return self.component end
        return self.receiver_object
    end
    function backend:object() return class end
    function backend:invoke(_,name,...)
        if name:match(":GetGameplayTagCount$") then return self.tags[(...).TagName:ToString()] or 0
        elseif name:match(":GetComponentByClass$") then return object(7)
        elseif name:match(":GetResolvedExecutionType$") then return {TagName=FName("Execution.Other")}
        elseif name:find(":TryActivateExternalActionAbility",1,true) then self.calls=self.calls+1;return false
        else error(name) end
    end
    return backend,{kind="move",tag="Ability.Fixture",ability_class=path,input_action="/Fixture/Input.Input"},spec,instance
end
local function denied(change,expected)
    local backend,target,spec,instance=fixture()
    change(backend,target,spec,instance)
    local result,reason=Moves.dispatch(backend,target,"started",false)
    check(result==false,"Qualification diagnostic must preserve the refusal boolean")
    check(type(reason)=="string" and reason:find(expected,1,true),"Refusal must explain the failed native qualification: "..tostring(reason))
    return backend
end
denied(function(backend) backend.component=nil end,"ability system component unavailable")
denied(function(_,target) target.required_tags={"State.Required"} end,"missing required tag: State.Required")
denied(function(_,target) target.activation_required_tags={"State.Initial"} end,"missing required tag: State.Initial")
denied(function(backend,target) target.ignored_tags={"State.Blocked"};backend.tags["State.Blocked"]=1 end,"blocked by tag: State.Blocked")
denied(function(_,target) target.activation_any_tags={"State.One","State.Two"} end,"missing any required tag: State.One, State.Two")
denied(function(_,target) target.execution_type="Execution.Wanted";target.execution_channel="Channel" end,
    "resolved execution type mismatch: expected Execution.Wanted, got Execution.Other")
denied(function(_,target) target.active_ability_classes={path};target.entry_point=path..":Entry" end,"required predecessor ability is not active or ready")
denied(function(_,target,spec) target.entry_point=path..":Entry";spec.NonReplicatedInstances=array({}) end,"exact ability instance not granted or ambiguous: "..path)
denied(function(_,_,spec,instance) spec.ActiveCount=1;instance.bIsActive=true end,"active ability cannot reenter: "..path)
denied(function(backend,target) backend.class_missing=true;target.activate_by_class=true end,"ability class unavailable: "..path)
denied(function(backend,target) backend.component.ActivatableAbilities.Items=array({});target.activate_by_class=true end,"exact ability class not granted: "..path)
local backend=denied(function() end,"native activation refused")
check(backend.calls==1,"A native refusal must still call the game's activation entry once")
local waiting,target=fixture()
local result,reason=Moves.dispatch(waiting,target,"completed",false)
check(result==true and reason==nil and waiting.calls==0,"Non-activation phases must stay successful and silent")
print("MoveQualificationDiagnosticTests PASS "..checks)
