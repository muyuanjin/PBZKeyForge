package.path="prototype/input-router/ue4ss/Scripts/?.lua;"..package.path
local Backend,Inputs,Equipment=require("backend"),require("move-inputs"),require("move-equipment")
local checks=0
local function check(value,message) checks=checks+1;assert(value,message) end
function FName(value) return value end
local function object(id,properties)
    local result=properties or {}
    function result:IsValid() return true end
    function result:GetAddress() return id end
    function result:GetFullName() return "Object "..(self.path or tostring(id)) end
    function result:GetPropertyValue(name) return self[name] end
    return result
end
local function array(values)
    return setmetatable({GetArrayNum=function() return #values end},{__index=values})
end
local function fixture(equipped,armed)
    local state={equipped=equipped,armed=armed,starts=0,stops=0,activations=0,slots=0,reports={}}
    local original=object(1,{path="/Fixture/Original.Original"})
    local wanted=object(2,{path="/Fixture/Bow.Bow"})
    local class=object(3,{path="/Fixture/BowAbility.BowAbility_C"})
    local ability=object(4,{AbilityTags={GameplayTags=array({{TagName={ToString=function() return "Ability.Action.Fixture" end}}})}})
    function ability:GetClass() return class end
    local empty=array({})
    local spec={Ability=ability,ActiveCount=0,NonReplicatedInstances=empty,ReplicatedInstances=empty}
    local asc=object(5,{ActivatableAbilities={Items=equipped and array({spec}) or empty}})
    local component,action,subsystem,value=object(6),object(7),object(8),object(9)
    local current=equipped and wanted or original
    local target={kind="ability",tag="Ability.Action.Fixture",ability_class=class.path,
        native_hold_inputs={"/Fixture/Hold.Hold"},equipment_configs={"/Fixture/Bow"},
        equipment_slot="Weapon.Slot.ShaChi",armed_state={slot="Weapon.Slot.ShaChi",armed=true}}
    local backend=setmetatable({config={enabled=true},state={ready=true},pawn=object(10),subsystem=subsystem},Backend)
    backend.missing={}
    backend.log=function(message) state.reports[#state.reports+1]=message end
    function backend:receiver(request) return request.receiver=="ability_system" and asc or component end
    function backend:object(path) return path==target.equipment_configs[1] and wanted or action end
    function backend:library() return action end
    function backend:target_failed() return false end
    function backend:target_succeeded() end
    function backend:fail_target(_,reason) error(reason) end
    local context={}
    function context:start_hold_input(owner,heldAction)
        check(owner==subsystem and heldAction==action,"Native Hold must receive its real subsystem and action")
        check(state.equipped and state.armed,"Native input handlers must be prepared before Hold injection")
        state.requested=true;state.starts=state.starts+1
    end
    function context:set_weapon_slot(_,slot,index,config)
        check(slot==target.equipment_slot and index==0,"Equipment must use its real selected slot")
        current=config;state.equipped=config==wanted;state.slots=state.slots+1
        asc.ActivatableAbilities.Items=state.equipped and array({spec}) or empty
    end
    backend.resource_costs={context=context,activate=function(_,_,_,callback) return callback() end}
    function backend:invoke(_,path,...)
        if path:match(":GetComponentByClass$") then return component
        elseif path:match(":GetCurrentWeaponIndex$") then return 0
        elseif path:match(":GetSlotWeapon$") then local args={...};args[3].WeaponConfig=current;return true
        elseif path:match(":IsArmed$") then return state.armed
        elseif path:match(":SetArmed$") then local args={...};state.armed=args[2]
        elseif path:match(":MakeInputActionValueOfType$") or path:match(":StartContinuousInputInjectionForAction$") then
            error("Opaque native input values must never roundtrip through Lua reflection")
        elseif path:match(":StopContinuousInputInjectionForAction$") then state.requested=false;state.stops=state.stops+1
        elseif path:match(":GetSGameInputComponent$") then return component
        elseif path:match(":IsHolding$") then return state.held==true
        elseif path:match(":TryActivateExternalActionAbilityInterruptLowerByTag$") then
            check(state.equipped and state.armed and state.held,"Activation must follow actual equipment, stance and native input")
            state.attempts=(state.attempts or 0)+1
            if state.refused then return false end
            state.activations=state.activations+1;spec.ActiveCount=1;return true
        else error(path) end
    end
    function state:advance() self.held=self.equipped and self.armed and self.requested end
    return state,backend,target,spec
end
for _,initial in ipairs({{false,false},{true,false},{true,true}}) do
    for _,outcome in ipairs({"activate","refused","cancel"}) do
        local state,backend,target,spec=fixture(initial[1],initial[2])
        local settings={trigger="down",restriction_bypasses={"equipment","stance"}}
        Inputs.dispatch(backend,target,"triggered",1,"binding",false,settings)
        check(state.starts==1 and state.activations==0,"Cold preparation must wait one native input update")
        Inputs.dispatch(backend,target,"triggered",1,"binding",false,settings)
        check(#state.reports==1 and state.reports[1]:find(target.native_hold_inputs[1],1,true)
            and state.reports[1]:find(target.ability_class,1,true),"A native Hold wait must explain its input and target once")
        if outcome=="cancel" then Inputs.dispatch(backend,target,"canceled",0,"binding",false,settings)
        else
            state.refused=outcome=="refused";state:advance()
            if state.refused then
                local accepted,result,reason=Inputs.dispatch(backend,target,"triggered",1,"binding",false,settings)
                check(accepted and result==false and reason:find("activation refused",1,true),
                    "Native Hold activation refusal must retain its reason through the preparation handoff")
            else Inputs.prepare(backend,{{target=target,binding="binding"}}) end
            check(state.activations==(outcome=="activate" and 1 or 0),"Prepared request must retain native activation refusal")
            spec.ActiveCount=0;Inputs.reset(backend)
        end
        Equipment.update(backend)
        check(state.stops==1 and not next(backend.native_move_inputs),"Completion must release every Hold owner")
        check(state.equipped==initial[1] and state.armed==initial[2] and not next(backend.move_equipment or {}),
            "Failure, cancellation and completion must restore temporary equipment and stance")
    end
end
do
    local state,backend,target=fixture(true,true)
    local original=Equipment.prepare
    Equipment.prepare=function() return false,"Another prepared weapon is still in use" end
    local accepted,result,reason=Inputs.dispatch(backend,target,"triggered",1,"binding",false,{trigger="down"})
    Equipment.prepare=original
    check(accepted and result==false and reason=="Another prepared weapon is still in use" and state.starts==0,
        "Equipment refusal must reach the binding diagnostic without starting a native Hold")
end
do
    local state,backend,target=fixture(true,true)
    local settings={trigger="down"}
    Inputs.dispatch(backend,target,"triggered",1,"binding",false,settings)
    state:advance();state.refused=true
    Inputs.prepare(backend,{{target=target,binding="binding"}})
    local accepted,result,reason=Inputs.dispatch(backend,target,"triggered",1,"binding",false,settings)
    check(accepted and result==false and reason and state.attempts==1,
        "A refusal completed during preparation must not retry again in the same scheduler frame")
    state:advance();state.refused=false
    Inputs.prepare(backend,{{target=target,binding="binding"}})
    Inputs.dispatch(backend,target,"triggered",1,"binding",false,settings)
    state:advance();Inputs.prepare(backend,{{target=target,binding="binding"}})
    check(state.attempts==2 and state.activations==1,"A new input frame must retry normally after qualification recovers")
    Inputs.reset(backend)
end
print("ColdNativePreparationTests PASS "..checks)
