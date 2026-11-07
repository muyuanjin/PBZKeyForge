package.path = "prototype/input-router/ue4ss/Scripts/?.lua;" .. package.path
local checks,hooks,watched,interfaceName = 0,{},{},nil
local damageModules,calculators,forgotten = {},{},{}
local notify,payloadNotify,bulletNotify,objectBulletNotify
local payloads,forgottenPayloads = {},{}
local nativeBullets,forgottenBullets,bulletSnapshots,abilityDamage = {},{},{},{}
local observation,reported
local emptyClass,emptyClassCalls={IsValid=function() return false end},0
local function check(value,message) assert(value,message);checks=checks+1 end
function NotifyOnNewObject(class,callback)
    if class=="/Script/SGameAttributes.DamageCalculatorBase" then notify=callback
    elseif class=="/Script/SGameAttributes.DamagePreCalculationPayload" then payloadNotify=callback
    elseif class=="/Script/SGameCombatSystem.BulletActorBase" then bulletNotify=callback
    elseif class=="/Script/SGameCombatSystem.BulletObjectBase" then objectBulletNotify=callback
    else error(class) end
end
local function object(name,address)
    return {IsValid=function(self) return self.valid ~= false end,GetAddress=function() return address end,
        GetFullName=function() return name end,GetPropertyValue=function(self,key) return self[key] end,
        IsA=function(self,class) return self.class==class end}
end
local first,second=object("Bullet /Level.First",1),object("Bullet /Level.Second",2)
local objects={["/Level.First"]=first,["/Level.Second"]=second}
local calculatorClass=object("CalculatorClass",20)
local moduleClass=object("ModuleClass",21)
local bulletClass=object("BulletClass",27)
local objectBulletClass=object("ObjectBulletClass",34)
local classClass=object("Class",38)
local objectBullet=object("SweepTraceCDO",35);objectBullet.class=objectBulletClass
first.class=bulletClass
objects["/Script/SGameAttributes.DamageCalculatorBase"]=calculatorClass
objects["/Script/SGameCombatSystem.BulletAttackModule"]=moduleClass
objects["/Script/SGameCombatSystem.BulletActorBase"]=bulletClass
objects["/Script/SGameCombatSystem.BulletObjectBase"]=objectBulletClass
objects["/Script/CoreUObject.Class"]=classClass
objects["/Script/SGameCombatSystem.BulletInterface"]=object("BulletInterface",30)
objects["/Script/SGameCombatSystem.BulletInterface:Initialize"]=object("Initialize",31)
objects["/Script/SGameAttributes.DamagePreCalculationPayload:GetModifiedAttackData"]=object("Getter",22)
objects["/Script/SGameAttributes.DamageCalculatorBase:CalculateDamage"]=object("Calculator",23)
local calculator=object("CalculatorCDO",24)
local existingActor=object("ExistingActor",39);existingActor.class=bulletClass
local existingObject=object("ExistingObject",40);existingObject.class=objectBulletClass
function FindAllOf(class)
    if class=="DamagePreCalculationPayload" then return {} end
    if class=="BulletActorBase" then return {existingActor} end
    if class=="BulletObjectBase" then return {existingObject} end
    error("Class discovery must not use FindAllOf, which filters UClass: "..class)
end
function ForEachUObject(callback)
    local function class(base,cdo)
        return {IsValid=function() return true end,IsA=function(_,kind) return kind==classClass end,
            IsChildOf=function(_,kind) return kind==base end,GetCDO=function() return cdo end}
    end
    for _,value in ipairs({class(calculatorClass,calculator),class(bulletClass,first),
        class(objectBulletClass,objectBullet),object("Unrelated",41)}) do callback(value) end
end
local enabled,prepared
package.loadlib=function(_,name)
    if name=="pbz_script_context_empty_class" then return function() emptyClassCalls=emptyClassCalls+1;return emptyClass end end
    if name=="pbz_script_context_enable" then return function(value) enabled=value end end
    if name=="pbz_script_context_at" then return function() end end
    if name=="pbz_script_context_watch_interface" then return function(address) watched[address]=true end end
    if name=="pbz_script_context_interface_object" then return function() return interfaceName end end
    if name=="pbz_damage_watch_module" then return function(address,multiplier)
        damageModules[address]=multiplier~=1 and multiplier or nil;return 1
    end end
    if name=="pbz_damage_watch_calculator" then return function(address,fn)
        check(fn==23,"calculator hook uses its reflected native function")
        calculators[address]=true;return 1
    end end
    if name=="pbz_damage_forget_module" then return function(address)
        forgotten[address]=true;damageModules[address]=nil;return 1
    end end
    if name=="pbz_damage_watch_payload" then return function(address,fn)
        check(fn==22,"payload maps use their reflected native getter")
        payloads[address]=true;return 1
    end end
    if name=="pbz_damage_prepare_bullet" then return function(fn)
        check(fn==31,"the original Initialize is prepared before any bullet instance is required")
        prepared=true;return 1
    end end
    if name=="pbz_damage_forget_payload" then return function(address)
        forgottenPayloads[address]=true;payloads[address]=nil;return 1
    end end
    if name=="pbz_damage_watch_bullet" then return function(address,base,interface,fn)
        check(prepared,"the native Initialize slot is prepared before enumerating bullet classes")
        check((base==27 or base==34) and interface==30 and fn==31,"native initialization uses the reflected bullet base, interface and entry")
        nativeBullets[address]=base;return 1
    end end
    if name=="pbz_damage_forget_bullet" then return function(address)
        forgottenBullets[address]=true;nativeBullets[address]=nil;return 1
    end end
    if name=="pbz_damage_bullet_snapshot" then return function(address,multiplier)
        bulletSnapshots[address]=multiplier;return 1
    end end
    if name=="pbz_damage_ability" then return function(address,multiplier)
        abilityDamage[address]=multiplier~=1 and multiplier or nil;return 1
    end end
    if name=="pbz_damage_reset" then return function() damageModules={};return 1 end end
    if name=="pbz_damage_observation" then return function() return observation end end
    error(name)
end
function StaticFindObject(name) return assert(objects[name],name) end
function RegisterHook(path,pre,post)
    check(prepared,"Initialize is prepared before UE4SS replaces the native exec with a dispatcher")
    check(watched[path=="initialize" and 10 or 11],"native interface capture is registered before its Lua consumer")
    hooks[path]={pre=pre,post=post};return 3,4
end
local Context=require("script-context")
local context=Context.new(function(message) reported=message end)
check(enabled,"context capture is enabled")
check(not next(nativeBullets) and not next(calculators) and not next(payloads),"context creation does not install damage hooks")
check(context:empty_class()==emptyClass and context:empty_class()==emptyClass and emptyClassCalls==1,
    "pure resource effects reuse a typed null class without enabling damage")
local untouched=setmetatable({}, {__index=function() error("Inactive damage must not inspect game objects") end})
notify(untouched);payloadNotify(untouched);bulletNotify(untouched);objectBulletNotify(untouched)
context:ability_damage(untouched,1)
context:update_damage()
context:enable_damage()
check(nativeBullets[1],"loaded bullet classes receive the native initialization hook before spawning")
check(nativeBullets[35]==34,"loaded SweepTrace and Overlap classes use their object bullet base")
check(nativeBullets[39]==27 and nativeBullets[40]==34,"both pre-existing bullet families enter native lifecycle tracking")
existingActor.valid=false;existingObject.valid=false
context:update_damage()
check(forgottenBullets[39] and forgottenBullets[40],"pre-existing bullets lose their native snapshots when destroyed")
local nativeBullet=object("NativeBullet",32)
nativeBullet.class=bulletClass
bulletNotify(nativeBullet)
check(nativeBullets[32],"a new native projectile is hooked before its direct initialization")
local nativeObjectBullet=object("NativeSweepTrace",36);nativeObjectBullet.class=objectBulletClass
objectBulletNotify(nativeObjectBullet)
check(nativeBullets[36]==34,"a new native object bullet is hooked with its own interface layout")
nativeBullet.valid=false
context:update_damage()
check(forgottenBullets[32] and not nativeBullets[32],"expired native projectile snapshots are forgotten")
local ability=object("Ability",33)
context:ability_damage(ability,100)
check(abilityDamage[33]==100,"native projectile creation receives the current binding's ability snapshot")
function ability:GetAddress() error("Destroyed ability cannot be accessed") end
context:ability_damage(33,1)
check(not abilityDamage[33],"ability cleanup uses its saved address after destruction")
local payload=object("Payload",29)
payloadNotify(payload)
check(payloads[29],"a new payload is registered synchronously before same-frame native damage calculation")
payload.valid=false
context:update_damage()
check(forgottenPayloads[29] and not payloads[29],"expired payloads are forgotten")
local backend={object=function(_,path) return object(path,path=="initialize" and 10 or 11) end}
local pending,completed={},{}
local a,b=context:interface_hook(backend,"initialize",function(bullet,value)
    check(bullet==first or bullet==second,"pre-hook receives the owning UObject")
    pending[#pending+1]={bullet=bullet,value=value}
end,function()
    completed[#completed+1]=table.remove(pending)
end)
check(a==3 and b==4,"interface hook preserves unregister identities")
local invalidContext={get=function() error("native interface Context is not a UObject in either hook") end}
interfaceName=first:GetFullName();hooks.initialize.pre(invalidContext,7)
interfaceName=second:GetFullName();hooks.initialize.pre(invalidContext,9)
hooks.initialize.post(invalidContext)
hooks.initialize.post(invalidContext)
check(completed[1].bullet==second and completed[1].value==9,"nested initialization keeps its own projectile and arguments")
check(completed[2].bullet==first and completed[2].value==7,"outer initialization retains its projectile after the nested call")
local started
context:interface_hook(backend,"start",function(bullet) started=bullet end)
interfaceName=first:GetFullName();hooks.start.pre(invalidContext)
check(started==first,"projectile launch resolves its owner without reading interface Context")
check(calculators[24],"existing calculator class defaults are hooked")
notify(object("LaterCalculator",25))
check(calculators[25],"a new calculator is hooked before its same-frame damage calculation")
local module=object("AttackModule",26);module.class=moduleClass
first.Modules={ForEach=function(_,callback) callback(1,{get=function() return module end}) end}
context:damage_bullet(first,first,11)
check(calculators[25] and damageModules[26]==11,"new calculators and this projectile receive its launch setting")
check(bulletSnapshots[1]==11,"Lua projectile launches also update native parent snapshots")
local objectModule=object("ObjectAttackModule",37);objectModule.class=moduleClass
objectBullet.Modules={ForEach=function(_,callback) callback(1,{get=function() return objectModule end}) end}
context:damage_bullet(objectBullet,nil,100)
check(bulletSnapshots[35]==100 and damageModules[37]==100,"object bullets receive callback launch settings without an Actor")
local otherBullet=object("OtherBullet",28)
function otherBullet:GetPropertyValue() error("This interface implementation has no Modules property") end
context:damage_bullet(otherBullet,otherBullet,7)
check(damageModules[26]==11,"non-actor bullet interfaces never read an unrelated Modules property")
context:damage_bullet(otherBullet,first,5)
check(damageModules[26]==5,"an interface implementation can resolve its BulletActorBase owner")
context:damage_bullet(first,first,1)
check(not damageModules[26],"ordinary reuse removes the previous projectile multiplier")
context:damage_bullet(first,first,3)
module.valid=false
context:update_damage()
check(forgotten[26] and not damageModules[26],"expired modules are forgotten without reading their native vtable")
module.valid=true
local watch=context.watch_module
context.watch_module=function() return 0,"module hook rejected" end
local ok,message=pcall(context.damage_bullet,context,first,first,11)
check(not ok and message:find("module hook rejected",1,true),"native zero return rejects a failed module hook")
context.watch_module=watch
observation="Damage scaling (calculator output): multiplier=100 before=10 after=1000"
for i=1,120 do context:update_damage() end
check(reported==observation,"damage observations reach the runtime logger during updates")
context:reset();check(not enabled,"reset disables native capture")
notify(untouched);payloadNotify(untouched);bulletNotify(untouched);objectBulletNotify(untouched)
context:update_damage()
local failed=Context.new()
failed.watch_damage_bullet=function() error("updated bullet layout rejected") end
check(not pcall(failed.enable_damage,failed) and failed.closed and not enabled,
    "partial damage initialization disables capture and rolls back native hooks")
notify(untouched);payloadNotify(untouched);bulletNotify(untouched);objectBulletNotify(untouched)
local lateFailure=Context.new()
lateFailure:enable_damage()
lateFailure.watch_damage_bullet=function() error("updated derived bullet rejected") end
check(not pcall(objectBulletNotify,untouched) and lateFailure.closed and not enabled,
    "a new unsupported class disables and cleans previously installed damage hooks")
check(lateFailure.failure and lateFailure.failure:find("updated derived bullet rejected",1,true),
    "asynchronous registration failure retains its original cause after context reset")
notify(untouched);payloadNotify(untouched);bulletNotify(untouched);objectBulletNotify(untouched)
print("ScriptContextTests PASS "..checks)
