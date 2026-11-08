package.loaded["script-context"] = {new=function() error("Unexpected tuning in an untuned fixture") end}
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
local hooks, nextHook = {}, 0
function RegisterHook(path,callback)
    check(path=="/PBZCombatAsset/Prototypes/BPL_PBZCombatUtils.BPL_PBZCombatUtils_C:Can Active Switch Attack",
        "Only the native switch qualification may be overridden")
    nextHook=nextHook+1;hooks[nextHook]=callback;return nextHook,nextHook
end
function UnregisterHook(_,first) hooks[first]=nil end
function FName(value) return {ToString=function() return value end} end
local classes, nextClass = {}, 1000
function StaticFindObject(path)
    if not classes[path] then
        nextClass = nextClass + 1
        local address = nextClass
        classes[path] = {IsValid=function() return true end,GetAddress=function() return address end,
            GetFullName=function() return "BlueprintGeneratedClass " .. path end}
    end
    return classes[path]
end
local function object(address,properties)
    local value = properties or {}
    function value:IsValid() return true end
    function value:GetAddress() return address end
    function value:GetPropertyValue(name) return self[name] end
    function value:GetClass()
        return StaticFindObject(self.class)
    end
    return value
end
local function array(values)
    return setmetatable({GetArrayNum=function() return #values end,ForEach=function(_,callback)
        for index,value in ipairs(values) do callback(index,{get=function() return value end}) end
    end},{__index=values})
end
local function fixture(target,missing)
    local class = target.ability_class or "/Fixture/"..target.tag
    local tags = {GameplayTags=array({{TagName=FName(target.tag)}})}
    local instance = object(1,{class=class,bIsActive=false,AbilityTags=tags})
    local cdo = object(2,{class=class,AbilityTags=tags})
    local spec = {Ability=cdo,ActiveCount=0,NonReplicatedInstances=array({instance}),ReplicatedInstances=array({})}
    local other = object(3,{class="/Other/SameTagAttack.SameTagAttack_C",AbilityTags={GameplayTags=array({})}})
    local asc = object(4,{ActivatableAbilities={Items=array(missing and {{Ability=other,ActiveCount=0}}
        or {{Ability=other,ActiveCount=0},spec})}})
    local receiver,input = object(5),object(6)
    local backend = {activations=0,entries=0,tag_counts={},pawn=object(8)}
    local switching = target.tag:find("Ability.Action.Hun.Weapon.Switch.",1,true)==1
    function backend:qualification(avatar,ability)
        for _,callback in pairs(hooks) do
            local result = callback(nil,{get=function() return avatar end},{get=function() return ability end})
            if result~=nil then return result end
        end
    end
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
        elseif path=="/Script/SGameInputSystem.AbilityInputComponent:TryActivateExternalActionAbilityByClass"
            or path=="/Script/SGameInputSystem.AbilityInputComponent:TryActivateExternalActionAbilityInterruptLowerByTag" then
            check(value==receiver and (target.ability_class and a:GetFullName()=="BlueprintGeneratedClass "..target.ability_class
                or not target.ability_class and a.TagName:ToString()==target.tag)
                and b==input and priority==target.priority,
                "A shared tag must activate only the requested existing class with its original input and priority")
            self.activations=self.activations+1
            if switching then
                if self:qualification(self.wrong_avatar and object(9) or self.pawn,instance)~=true then return false end
                check(self:qualification(self.pawn,other)==nil,"An unrelated granted class must keep its original switch qualification")
            end
            if self.throw then error("Native activation failed") end
            if self.reject then return false end
            if instance.bIsActive then return false end
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
    return backend,instance,spec
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
        "Native form, resource or counter rejection must remain a rejection")
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
        local instance,spec
        backend,instance,spec = fixture(target)
        check(backend:qualification(backend.pawn,instance)==nil,
            "Native switching outside a direct binding must retain its original qualification")
        check(Moves.dispatch(backend,target,"triggered") and backend.weapon_switch_scope==nil,
            "A direct switch must work with the same weapon selected or the switch cooldown running")
        check(backend:qualification(backend.pawn,instance)==nil,"The switch override must end with the activation call")
        check(not Moves.dispatch(backend,target,"triggered") and backend.activations==1,
            "Holding a switch binding must not restart its active attack")
        instance.bIsActive,spec.ActiveCount=false,0
        check(Moves.dispatch(backend,target,"triggered") and backend.activations==2,
            "Holding a switch binding must allow the next attack after completion")
        backend = fixture(target);backend.wrong_avatar=true
        check(not Moves.dispatch(backend,target,"triggered"),"A switch qualification for another avatar must remain unchanged")
        backend = fixture(target);backend.throw=true
        check(not pcall(Moves.dispatch,backend,target,"triggered") and backend.weapon_switch_scope==nil,
            "A failed native call must restore the switch qualification before reporting the failure")
        backend.throw=nil
        check(Moves.dispatch(backend,target,"triggered"),"A failed call must allow a later direct switch attempt")
        Moves.reset(backend)
        check(not backend.weapon_switch_hook and not backend.weapon_switch_scope,
            "Runtime reset must remove the switch hook and its activation scope")
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

do
    local Backend,Router = require("backend"),require("router")
    local directory = os.getenv("PBZ_SCHEME_FIXTURES") or "dist/catalog-input-pipelines"
    local covered = {}
    for _,device in ipairs({"keyboard","pad"}) do
        local compiled = dofile(directory.."/"..device.."-abilities.lua")
        for _,binding in ipairs(compiled.bindings) do
            local output = binding.outputs[1]
            local target = compiled.targets[type(output)=="table" and output.target or output]
            if binding.default_trigger and target.tag:find("Ability.Action.Hun.Weapon.Switch.",1,true)==1 then
                covered[target.tag]=true
                local backend,instance,spec = fixture(target)
                local configuration = {enabled=true,targets=compiled.targets,bindings={binding}}
                setmetatable(backend,Backend)
                backend.config,backend.state,backend.missing=configuration,{ready=true},{}
                backend.log=function(message) assert(message:match("^Move not activated %[") ~= nil,message) end
                function backend:key_dimension() return 1 end
                local router = Router.new(configuration,backend)
                local held = {[binding.keys[1]]=1}
                router:step(0,held,{})
                check(backend.activations==1 and instance.bIsActive,
                    "Compiled direct switch must use scoped qualification on "..device..": "..target.tag)
                check(not backend.weapon_switch_scope,"Generic switch qualification must also end synchronously")
                router:step(.01,held,{})
                check(backend.activations==2 and instance.bIsActive,"Native activation must reject an attack already in progress")
                instance.bIsActive,spec.ActiveCount=false,0
                router:step(.02,held,{})
                check(backend.activations==3 and instance.bIsActive,"Holding must retry after the native attack ends")
                router:step(.03,{},{})
                check(backend.activations==3,"Releasing a generic switch key must not request another attack")
                Moves.reset(backend)
                backend,instance,spec = fixture(target,true)
                setmetatable(backend,Backend)
                backend.config,backend.state,backend.missing=configuration,{ready=true},{}
                backend.log=function(message) assert(message:match("^Move not activated %[") ~= nil,message) end
                local ok,result = backend:dispatch(target,"triggered",nil,binding.id)
                check(ok and result==false,"Generic switch must reject a class absent from the actual ability grants")
                Moves.reset(backend)
            end
        end
    end
    for _,kind in ipairs({"Normal","Dash","Air"}) do
        check(covered["Ability.Action.Hun.Weapon.Switch."..kind],"Compiled switch coverage lost "..kind)
    end
end
print("PASS: "..checks.." compiled special weapon checks")
