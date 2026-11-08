package.path="prototype/input-router/ue4ss/Scripts/?.lua;"..package.path
local Equipment=require("move-equipment")
local checks=0
local function check(value,message) checks=checks+1;assert(value,message) end
function FName(value) return value end
local function object(id,path)
    local result={path=path}
    function result:IsValid() return self.valid~=false end
    function result:GetAddress() return id end
    function result:GetFullName() return "Object "..self.path end
    function result:GetPropertyValue(name) return self[name] end
    return result
end
local function array(values)
    return setmetatable({GetArrayNum=function() return #values end},{__index=values})
end
local function fixture()
    local original=object(1,"/Weapons/Original.Original")
    local wanted=object(2,"/Weapons/Wanted.Wanted")
    local alternate=object(3,"/Weapons/Other.Other")
    local class=object(4,"/Abilities/Attack.Attack_C")
    local ability=object(5,"/Abilities/Default__Attack")
    function ability:GetClass() return class end
    ability.AbilityTags={GameplayTags=array({{TagName={ToString=function() return "Ability.Action.Hun.Test" end}}})}
    local specs={}
    local asc=object(6,"ASC");asc.ActivatableAbilities={Items=array(specs)}
    local component=object(7,"Weapons")
    local state={current=original,swaps={},index=1,armed=false,armChanges=0}
    local context={}
    function context:set_weapon_slot(value,slot,index,config)
        check(value==component and index==state.index,"Native component and original slot index must be preserved")
        state.swaps[#state.swaps+1]={slot=slot,config=config}
        state.current=config
        specs[1]={Ability=ability,ActiveCount=0}
        if state.fail then state.fail=false;error("native equipment failed after changing slot") end
    end
    local backend={pawn=object(8,"Pawn"),resource_costs={context=context,instances={}}}
    function backend:receiver() return asc end
    function backend:object(path)
        if path=="/Weapons/Wanted" then return wanted end
        if path=="/Weapons/Other" then return alternate end
        return component
    end
    function backend:invoke(value,path,tag,index,out)
        if path:match(":GetComponentByClass$") then return component end
        check(value==component,"Equipment calls must use the weapon component")
        if path:match(":GetCurrentWeaponIndex$") then return state.index end
        if path:match(":GetSlotWeapon$") then out.WeaponConfig=state.current;return true end
        if path:match(":IsArmed$") then return state.armed end
        if path:match(":SetArmed$") then state.armed=index;state.armChanges=state.armChanges+1;return end
        error(path)
    end
    local target={kind="move",ability_class=class.path,equipment_slot="Weapon.Slot.Main",equipment_configs={"/Weapons/Wanted"}}
    local policy={restriction_bypasses={"equipment"}}
    return backend,state,target,policy,specs,original,wanted,alternate
end
do
    local backend,state,target,policy,specs,original=fixture()
    check(Equipment.prepare(backend,target,{restriction_bypasses={"training"}}) and #state.swaps==0,"Unselected equipment must stay untouched")
    check(Equipment.prepare(backend,target,policy) and state.current~=original,"Equipment bypass must prepare the real config")
    check(Equipment.prepare(backend,target,policy) and #state.swaps==1,"Repeated requests for the same config must share the prepared slot")
    specs[1].ActiveCount=1;Equipment.update(backend)
    check(#state.swaps==1,"The weapon must remain installed for its active action")
    local different={kind="move",equipment_slot=target.equipment_slot,equipment_configs={"/Weapons/Other"}}
    check(not Equipment.prepare(backend,different,policy) and #state.swaps==1,"Another weapon cannot destroy a running prepared action")
    specs[1].ActiveCount=0
    local continuation=object(20,"Continuation");continuation.bIsActive=true
    backend.resource_costs.instances[20]={instance=continuation,restriction_target=target}
    Equipment.update(backend)
    check(#state.swaps==1,"A native continuation must retain the prepared weapon after the entry ability ends")
    continuation.bIsActive=false
    backend.complete_move_pending={test={spec=target}}
    Equipment.update(backend);check(#state.swaps==1,"A protected move awaiting its native phase must retain equipment")
    backend.complete_move_pending=nil;backend.native_move_inputs={test={target=target}}
    Equipment.update(backend);check(#state.swaps==1,"An input awaiting a real release must retain equipment")
    backend.native_move_inputs=nil;Equipment.update(backend)
    check(state.current==original and #state.swaps==2 and next(backend.move_equipment)==nil,"Native completion must restore the exact original config")
    Equipment.prepare(backend,target,policy);Equipment.reset(backend)
    check(state.current==original and #state.swaps==4,"Reset must release a pending equipment lease")
end
do
    local backend,state,target,policy,specs,original=fixture()
    state.current=backend:object(target.equipment_configs[1])
    check(Equipment.prepare(backend,target,policy) and #state.swaps==0,"Already equipped native config must not be rebuilt")
    state.current=original
    local class=object(30,"/Original/Action_C")
    local ability=object(31,"OriginalAction");function ability:GetClass() return class end
    ability.AbilityTags={GameplayTags=array({{TagName={ToString=function() return "Ability.Action.Hun.Normal" end}}})}
    specs[1]={Ability=ability,ActiveCount=1}
    check(not Equipment.prepare(backend,target,policy) and #state.swaps==0,"Temporary equipment must wait for the current action to finish")
end
do
    local backend,state,target,policy,specs,original=fixture()
    target.armed_state={slot="Weapon.Slot.Main",armed=true}
    check(Equipment.prepare(backend,target,policy) and not state.armed,"Equipment selection must not enable an unselected stance override")
    Equipment.reset(backend)
    check(not state.armed and state.armChanges==0,"Replacing an unarmed config must preserve the original armed state")
    policy.restriction_bypasses={"equipment","stance"}
    Equipment.prepare(backend,target,policy)
    check(state.armed and state.armChanges==1,"Stance bypass must arm the real weapon actor after installing its config")
    specs[1].ActiveCount=1;Equipment.update(backend)
    check(state.armed and state.current~=original,"A running prepared action must retain both equipment and armed state")
    specs[1].ActiveCount=0;Equipment.update(backend)
    check(not state.armed and state.current==original and state.armChanges==2,"Completion must restore the original unarmed configuration")
end
do
    local backend,state,target,policy,specs,original=fixture()
    policy.restriction_bypasses={"stance"}
    target.armed_state={slot="Weapon.Slot.Main",armed=true}
    Equipment.prepare(backend,target,policy)
    check(state.armed and state.current==original and #state.swaps==0,"Stance-only preparation must use the current equipment")
    Equipment.reset(backend)
    check(not state.armed and state.current==original,"Reset must restore an armed-state-only preparation")
    state.armed=true;target.armed_state.armed=false
    Equipment.prepare(backend,target,policy)
    check(not state.armed,"A draw-attack entrance must use the real unarmed actor state")
    Equipment.reset(backend)
    check(state.armed,"A draw preparation must restore the original armed state")
end
do
    local backend,state,target,policy=fixture()
    state.armed=true;policy.restriction_bypasses={"equipment","stance"}
    target.armed_state={slot="Weapon.Slot.Main",armed=true}
    Equipment.prepare(backend,target,policy);Equipment.reset(backend)
    check(state.armed and state.armChanges==0,"An already armed original must remain armed after temporary equipment restoration")
end
do
    local backend,state,target,policy,_,original=fixture()
    state.fail=true
    local ok=pcall(Equipment.prepare,backend,target,policy)
    check(not ok and backend.move_equipment[target.equipment_slot],"Partial native replacement must retain its restore operation")
    Equipment.reset(backend)
    check(state.current==original and next(backend.move_equipment)==nil,"Reset must restore even a partially failed preparation")
end
do
    local backend,state,target,policy,_,original=fixture()
    Equipment.prepare(backend,target,policy)
    state.fail=true
    check(not pcall(Equipment.reset,backend) and backend.move_equipment[target.equipment_slot],"Failed restoration must retain its original config for retry")
    Equipment.reset(backend)
    check(state.current==original and next(backend.move_equipment)==nil,"Restoration retry must finish without replacing its saved original")
end
for _,tag in ipairs({"Ability.Action.Hun.SpecialDash","Ability.Action.Hun.DeflectDash"}) do
    local backend,state=fixture()
    local target={kind="ability",tag=tag}
    check(Equipment.prepare(backend,target) and state.armed and state.armChanges==1,
        "Independent flash step must draw the current weapon before native qualification")
    Equipment.prepare(backend,target)
    check(state.armChanges==1 and #state.swaps==0,"An armed flash step must not redraw or replace equipment")
    Equipment.reset(backend)
    check(state.armed,"Natural flash-step drawing must remain armed after completion")
    state.armed=false
    Equipment.prepare(backend,target)
    check(state.armed and state.armChanges==2,"Sheathing again must not require a separate guard input")
end
do
    local backend,state=fixture()
    Equipment.prepare(backend,{kind="ability",tag="Ability.Action.Hun.Dash"})
    check(not state.armed and state.armChanges==0,"Ordinary dodge must preserve the current weapon stance")
end
do
    local backend,state,target,policy,specs,original=fixture()
    Equipment.prepare(backend,target,policy)
    local active=specs[1]
    active.ActiveCount=1
    local reads,scans=0,0
    local getClass=active.Ability.GetClass
    function active.Ability:GetClass() reads=reads+1;return getClass(self) end
    for index=2,130 do
        local unrelated=object(100+index,"Inactive")
        function unrelated:GetClass() error("Inactive grants must not be wrapped for equipment liveness") end
        specs[index]={Ability=unrelated,ActiveCount=0}
    end
    backend.active_ability_specs=function(_,class,all,count)
        scans=scans+1
        check(class==0 and all==true and count==0,"equipment must select all active grants without tag conversion")
        return active.ActiveCount>0 and "1 " or ""
    end
    for frame=1,120 do Equipment.update(backend) end
    check(scans==120 and reads==120 and #state.swaps==1,
        "130 grants must cost one Lua class read per active frame and retain the prepared weapon")
    active.ActiveCount=0;Equipment.update(backend)
    check(state.current==original and reads==120 and next(backend.move_equipment)==nil,
        "activity completion must restore the exact slot without reading inactive classes")
end

print("MoveEquipmentTests PASS "..checks)
