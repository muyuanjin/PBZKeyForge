package.path = "prototype/input-router/ue4ss/Scripts/?.lua;" .. package.path
local Moves = require("complete-moves")
local config = assert(loadfile((...) or "dist/complete-move-catalog-check/phase-moves.lua"))()
local targets = {}
for _,target in pairs(config.targets) do targets[target.move_id] = target end
local checks = 0
local function check(value,message)
    assert(value,message)
    checks = checks + 1
end
function FName(value) return {ToString=function() return value end} end
local function object(address,properties)
    local value = properties or {}
    function value:IsValid() return true end
    function value:GetAddress() return address end
    function value:GetPropertyValue(name) return self[name] end
    function value:GetClass()
        return object(address+100,{GetFullName=function() return "BlueprintGeneratedClass "..self.class end})
    end
    return value
end
local function array(values)
    return {ForEach=function(_,callback)
        for index,value in ipairs(values) do callback(index,{get=function() return value end}) end
    end}
end
local function fixture(target,missing)
    local instance = object(1,{class=target.ability_class,bIsActive=false})
    local cdo = object(2,{class=target.ability_class})
    local spec = {Ability=cdo,ActiveCount=0,NonReplicatedInstances=array({instance}),ReplicatedInstances=array({})}
    local other = object(3,{class="/Other/SameTagAttack.SameTagAttack_C"})
    local asc = object(4,{ActivatableAbilities={Items=array(missing and {{Ability=other,ActiveCount=0}}
        or {{Ability=other,ActiveCount=0},spec})}})
    local receiver,input = object(5),object(6)
    local backend = {activations=0,entries=0,tag_counts={}}
    for _,tags in ipairs({target.required_tags or {},target.activation_required_tags or {}}) do
        for _,tag in ipairs(tags) do backend.tag_counts[tag] = 1 end
    end
    function backend:receiver(value) return value.receiver=="ability_system" and asc or receiver end
    function backend:object(path)
        if path==target.entry_point then return object(7) end
        check(path==target.input_action,"The compiled native input action must reach the executor unchanged")
        return input
    end
    function backend:invoke(value,path,a,b,priority)
        if path=="/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayTagCount" then
            check(value==asc,"Native context tags must be read from the current owner's ASC")
            return self.tag_counts[a.TagName:ToString()] or 0
        elseif path=="/Script/SGameInputSystem.AbilityInputComponent:TryActivateExternalActionAbilityByClass" then
            check(value==receiver and a:GetFullName()=="BlueprintGeneratedClass "..target.ability_class
                and b==input and priority==target.priority,
                "A shared tag must activate only the requested existing class with its original input and priority")
            self.activations=self.activations+1
            if self.reject then return false end
            instance.bIsActive,spec.ActiveCount=true,1
            for _,tag in ipairs(target.activation_required_tags or {}) do self.tag_counts[tag]=0 end
            return true
        elseif target.entry_point and path==target.entry_point then
            check(value==instance and instance.bIsActive and a==0,
                "Early release must invoke original OnRelease(0) on its normally activated Start instance")
            self.entries=self.entries+1
            return
        end
        error("Unexpected activation, charge reset, virtual input or stage bypass: "..path)
    end
    return backend,instance
end
local ids = {
    "move:Ability.Action.Hun.Heavy.GuiTouDao.BHold.EarlyRelease",
    "move:WeaponSpecial.GA_Hun_Chaos_SwitchFormFire_SWT",
    "move:WeaponSpecial.GA_Hun_Chaos_SwitchFormIce_SWT",
    "move:WeaponSpecial.GA_Hun_Chaos_SwitchFormPhysic_SWT",
    "move:WeaponSpecial.GA_Hun_DualHook_DB_SWT",
    "move:WeaponSpecial.GA_Hun_ShuangYue_DB_SWT",
    "move:WeaponSwitch.ChaosBlade",
    "move:WeaponSwitch.DualHook",
    "move:WeaponSwitch.ShuangYue",
    "move:Ability.Action.Hun.Heavy.Horizontal.NoCostB",
}
for _,id in ipairs(ids) do
    local target = assert(targets[id],"Compiled catalog lost special weapon entry: "..id)
    check(target.activate_by_class and not target.force,"Special weapon entries retain normal exact-class activation")
    local backend = fixture(target)
    check(Moves.dispatch(backend,target,"triggered") and backend.activations==1,
        "A qualified granted special move must reach normal native activation")
    check(backend.entries==(target.entry_point and 1 or 0),"Only an explicit early-release route may enter a callback")
    backend = fixture(target);backend.reject=true
    check(not Moves.dispatch(backend,target,"triggered") and backend.activations==1 and backend.entries==0,
        "Native form, resource, cooldown or counter rejection must remain a rejection")
    backend = fixture(target,true)
    check(not Moves.dispatch(backend,target,"triggered") and backend.activations==0,
        "An unavailable switch or leaf class must never be granted or replaced with its same-tag neighbor")
    backend = fixture(target)
    check(Moves.dispatch(backend,target,"completed") and backend.activations==0,
        "Releasing a binding must not request another special move")
    if id:find("move:WeaponSwitch.",1,true) then
        check(#target.required_tags==1 and target.required_tags[1]=="CharacterState.Space.Ground"
            and target.input_action=="/SGameCombatSystem/Weapon/Input/IA_ChangeWeapon.IA_ChangeWeapon"
            and not target.entry_point,
            "The original weapon-switch wrapper must own slot, target weapon and its native transfer")
    end
end
do
    local target = targets["move:Ability.Action.Hun.Heavy.GuiTouDao.BHold.EarlyRelease"]
    check(target.entry_point==target.ability_class..":OnRelease_5FFC903E46B76F3B1F78F7B3A63C7FB1"
        and target.ability_class:find("GA_Hun_GuiTouBlade_B_Hold_Start",1,true)
        and #target.entry_arguments==1 and target.entry_arguments[1]==0,
        "Early release must preserve the Start's paid native selection of both UnCharge branches")
end
do
    local target = targets["move:Ability.Action.Hun.Heavy.Horizontal.NoCostB"]
    check(#target.activation_required_tags==1 and target.activation_required_tags[1]=="CharacterState.AbilityState.Horizontal.Charging",
        "The zero-cost follow-up must require its real already-paid charging source")
    local backend = fixture(target)
    backend.tag_counts["CharacterState.AbilityState.Horizontal.Charging"]=0
    check(not Moves.dispatch(backend,target,"triggered") and backend.activations==0,
        "A free-standing keypress must never create a free heavy attack")
    backend = fixture(target)
    check(Moves.dispatch(backend,target,"triggered") and backend.activations==1
        and backend.tag_counts["CharacterState.AbilityState.Horizontal.Charging"]==0,
        "The original charging context may end during the qualified transfer")
end
print("PASS: "..checks.." compiled special weapon checks")
