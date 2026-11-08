package.path="prototype/input-router/ue4ss/Scripts/?.lua;"..package.path
local NativeInput=require("native-input")
package.loaded["script-context"]={}
local Costs=require("resource-costs")
local checks,hooks=0,{}
local function check(value,message) assert(value,message);checks=checks+1 end
local function object(id,class)
    return {IsValid=function() return true end,GetAddress=function() return id end,
        GetClass=function() return class or "Object" end}
end
local function param(value) return {get=function() return value end} end
function FName(value) return {ToString=function() return value end} end
function RegisterHook(path,pre,post)
    check(not hooks[path],"no duplicate registration")
    hooks[path]={pre=pre,post=post};return 1,2
end
function UnregisterHook(path) hooks[path]=nil end
local action,component,player,foreign=object(1),object(2),object(3),object(4)
local frame,physical=10,{}
local controller=object(5);controller.PlayerInput=player
local backend={controller=controller,library=function() return {} end,
    receiver=function(_,spec) return spec.receiver=="ability_input" and component or nil end}
function backend:invoke(receiver,path,key)
    if path:find(":GetFrameCount",1,true) then return frame end
    if path:find(":GetTriggerType",1,true) then return receiver.kind end
    check(path:find(":GetInputVectorKeyState",1,true) and receiver==self.controller,"only raw native key reads")
    return {X=physical[key.KeyName:ToString()] or 0,Y=0,Z=0}
end
local native=NativeInput.new(backend);backend.native_input=native
local function source(key,triggers)
    return {object=action,key=key,active_key=key,owner="uncovered",triggers=triggers or {}}
end
local policy={bypass=true,startup=2,recovery=3,damage=4,super_armor=true,invincible=true,
    restriction_bypasses={"movement"}}
local observations={}
local context={}
function context:watch_input_trigger(_,trigger) observations[trigger:GetAddress()]={} end
function context:input_trigger_state(trigger,input,queryFrame)
    local result=observations[trigger:GetAddress()]
    if result and result.frame==queryFrame and result.player==input then return result.state,result.kind,result.blocked end
end
local costs=setmetatable({backend=backend,context=context,hooks={},inputs={[1]=policy},instances={},transfers={}},Costs)
function costs:owner() return nil end
function costs:begin_transfer(value,target) self.selected=value;self.transfers={{source=value,target=nil}} end
function costs:end_transfer() self.transfers={} end
costs:install_transfer_hooks()
local transfer=hooks["/Script/SGameInputSystem.AbilityInputComponent:TryActivateExternalActionAbilityByTag"]
local function activate(wanted,message)
    transfer.pre(param(component),param({TagName=FName("Ability.Attack")}),param(action))
    check(costs.selected==wanted,message)
    transfer.post(param(component),param(true))
end
native.coverage={source("MouseScrollUp")};native:prepare_tuning(costs)
activate(policy,"idle native wheel preserves the enhanced binding's complete parameter group")
physical.MouseScrollUp=1
activate(nil,"a participating native wheel selects ordinary parameters for the shared aggregate")
physical.MouseScrollUp=0;frame=frame+1
activate(policy,"the next enhanced activation immediately regains its parameters")
native.coverage[1].active_key="None";physical.MouseScrollUp=1
activate(policy,"masked native mappings cannot claim the activation")
native.coverage[1].active_key="MouseScrollDown"
activate(policy,"native runtime remaps poll the replacement key")
physical.MouseScrollDown=-1
activate(nil,"the remapped key retains native parameters")

local explicit=object(20,"InputTriggerTouchpadSwipe");explicit.kind=0
local implicit=object(21,"InputTriggerChordAction");implicit.kind=1
local blocker=object(22,"InputTriggerChordBlocker");blocker.kind=2
native.coverage={source("PBZ_Touchpad_SwipeUp",{explicit,implicit,blocker})}
native:prepare_tuning(costs)
local function observed(trigger,state,input)
    if observations[trigger:GetAddress()] then
        observations[trigger:GetAddress()]={frame=frame,player=input or player,state=state,kind=trigger.kind,
            blocked=trigger==blocker and state==2 or trigger==implicit and state~=2}
    end
end
physical.PBZ_Touchpad_SwipeUp=1
observed(explicit,0);observed(implicit,2);observed(blocker,0)
activate(policy,"touching without a recognized swipe does not cancel enhanced tuning")
observed(explicit,1)
activate(policy,"an ongoing gesture does not masquerade as a recognized swipe")
observed(explicit,2);observed(implicit,1)
activate(policy,"an unsatisfied native chord cannot claim the activation")
observed(implicit,2);observed(blocker,2)
activate(policy,"a native chord blocker suppresses the native source")
observed(blocker,0)
activate(nil,"a recognized native gesture and satisfied chord select native parameters")
frame=frame+1
activate(policy,"previous frame recognition cannot contaminate a later activation")
observed(explicit,2,foreign);observed(implicit,2);observed(blocker,0)
activate(policy,"another player's trigger evaluation cannot claim the local activation")
observed(object(99,"InputTriggerTouchpadSwipe"),2)
activate(policy,"a cloned or unrelated trigger evaluation cannot claim the native mapping")
observed(explicit,2)
activate(nil,"local native recognition is observed without reevaluating its trigger")
native.coverage={source("PBZ_Touchpad_SwipeUp",{explicit})};native:prepare_tuning(costs)
activate(policy,"mapping rebuild invalidates old recognition")
physical.PBZ_Touchpad_SwipeUp=0;observed(explicit,2)
activate(nil,"release and temporal triggers can recognize a native event with zero raw value")
native:prepare_tuning(costs)
native:restore()
activate(policy,"restoring native input removes observation ownership")
for path in pairs(costs.hooks) do UnregisterHook(path) end
check(not next(hooks),"all transfer hooks are owned by tuning cleanup")
print("InputActionProvenanceTests PASS "..checks)
