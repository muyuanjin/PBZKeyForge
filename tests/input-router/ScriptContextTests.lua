package.path = "prototype/input-router/ue4ss/Scripts/?.lua;" .. package.path
local checks,hooks,watched,interfaceName = 0,{},{},nil
local damageModules,calculators,forgotten = {},{},{}
local notify,payloadNotify,bulletNotify,objectBulletNotify
local notificationCount = 0
local payloads,forgottenPayloads = {},{}
local nativeBullets,forgottenBullets,bulletSnapshots,abilityDamage = {},{},{},{}
local observation,reported
local interfaceAddress,interfaceAddressCalls
local emptyClass,emptyClassCalls={IsValid=function() return false end},0
local nativeHold,nativeEffect,nativeRemoval
local longCapture,longRemove,longRestore,longDiscard
local function check(value,message) assert(value,message);checks=checks+1 end
function NotifyOnNewObject(class,callback)
    notificationCount = notificationCount + 1
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
local objectBullet=object("SweepTraceCDO",35);objectBullet.class=objectBulletClass
first.class=bulletClass
objects["/Script/SGameAttributes.DamageCalculatorBase"]=calculatorClass
objects["/Script/SGameCombatSystem.BulletAttackModule"]=moduleClass
objects["/Script/SGameCombatSystem.BulletActorBase"]=bulletClass
objects["/Script/SGameCombatSystem.BulletObjectBase"]=objectBulletClass
objects["/Script/SGameCombatSystem.BulletInterface"]=object("BulletInterface",30)
objects["/Script/SGameCombatSystem.BulletInterface:Initialize"]=object("Initialize",31)
objects["/Script/SGameAttributes.DamagePreCalculationPayload:GetModifiedAttackData"]=object("Getter",22)
objects["/Script/SGameAttributes.DamageCalculatorBase:CalculateDamage"]=object("Calculator",23)
objects["/Script/EnhancedInput.Default__EnhancedInputLibrary"]=object("InputLibrary",70)
objects["/Script/EnhancedInput.EnhancedInputLibrary:MakeInputActionValueOfType"]=object("MakeInputValue",71)
objects["/Script/EnhancedInput.EnhancedInputSubsystemInterface:StartContinuousInputInjectionForAction"]=object("StartInput",72)
objects["/Script/GameplayAbilities.AbilitySystemComponent:MakeEffectContext"]=object("MakeContext",73)
objects["/Script/GameplayAbilities.AbilitySystemComponent:BP_ApplyGameplayEffectToSelf"]=object("ApplyEffect",74)
objects["/Script/GameplayAbilities.AbilitySystemComponent:RemoveActiveGameplayEffect"]=object("RemoveEffect",75)
objects["/Script/GameplayAbilities.AbilitySystemComponent:MakeOutgoingSpec"]=object("MakeSpec",82)
objects["/Script/GameplayAbilities.AbilitySystemComponent:BP_ApplyGameplayEffectSpecToSelf"]=object("ApplySpec",83)
objects["/Script/Engine.Default__GameplayStatics"]={GetTimeSeconds=function(_,component)
    check(component:GetAddress()==78,"Long resource duration uses its player's world")
    return 17.5
end}
local calculator=object("CalculatorCDO",24)
local existingActor=object("ExistingActor",39);existingActor.class=bulletClass
local existingObject=object("ExistingObject",40);existingObject.class=objectBulletClass
function FindAllOf(class)
    if class=="DamagePreCalculationPayload" then return {} end
    error("Damage discovery must include class defaults: "..class)
end
local damageQueries=0
function ForEachUObject() error("Damage discovery must not visit unrelated UObjects through Lua") end
function FindObjects(count,class,name,required,banned,exact)
    check(count==0 and name==nil and required==0 and banned==0 and exact==false,
        "damage discovery must include every derived instance and class default")
    damageQueries=damageQueries+1
    if class==calculatorClass then return {calculator} end
    if class==bulletClass then return {first,existingActor} end
    if class==objectBulletClass then return {objectBullet,existingObject} end
    error("unexpected damage class query")
end
local enabled,prepared
local chargeRates,chargeTicks,chargeLoads={},{},0
package.loadlib=function(_,name)
    if name=="pbz_input_start_hold" then return function(...) nativeHold={...};return 1 end end
    if name=="pbz_effect_apply_self" then return function(...) nativeEffect={...};return 1,123 end end
    if name=="pbz_effect_apply_with_requirements" then return function(...) nativeEffect={...};return 1,124 end end
    if name=="pbz_effect_remove" then return function(...) nativeRemoval={...};return 1,0 end end
    if name=="pbz_long_buff_capture" then return function(...) longCapture={...};return 1,321 end end
    if name=="pbz_long_buff_remove" then return function(...) longRemove={...};return 1,1 end end
    if name=="pbz_long_buff_restore" then return function(...) longRestore={...};return 1,1 end end
    if name=="pbz_long_buff_discard" then return function(...) longDiscard={...};return 1,1 end end
    if name=="pbz_ability_spec_indices" then return function() return "" end end
    if name=="pbz_script_context_empty_class" then return function() emptyClassCalls=emptyClassCalls+1;return emptyClass end end
    if name=="pbz_script_context_enable" then return function(value) enabled=value end end
    if name=="pbz_script_context_at" then return function() end end
    if name=="pbz_charge_watch_tick" then chargeLoads=chargeLoads+1;return function(address) chargeTicks[address]=(chargeTicks[address] or 0)+1;return 1 end end
    if name=="pbz_charge_ability" then chargeLoads=chargeLoads+1;return function(address,rate) chargeRates[address]=rate~=1 and rate or nil;return 1 end end
    if name=="pbz_script_context_watch_interface" then return function(address) watched[address]=true end end
    if name=="pbz_script_context_interface_object" then return function() return interfaceName end end
    if name=="pbz_script_context_interface_address" then return function()
        interfaceAddressCalls=(interfaceAddressCalls or 0)+1;return interfaceAddress end end
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
function StaticFindObject(name) return (assert(objects[name],name)) end
function RegisterHook(path,pre,post)
    check(prepared,"Initialize is prepared before UE4SS replaces the native exec with a dispatcher")
    check(watched[path=="initialize" and 10 or 11],"native interface capture is registered before its Lua consumer")
    hooks[path]={pre=pre,post=post};return 3,4
end
local Context=require("script-context")
local context=Context.new(function(message) reported=message end)
check(enabled,"context capture is enabled")
context:start_hold_input(object("Subsystem",76),object("Action",77))
check(table.concat(nativeHold,",")=="76,70,71,72,77","held input passes native objects without converting the opaque value in Lua")
local handle=context:apply_effect_self(object("ASC",78),object("EffectClass",79),2.5)
check(handle==123 and table.concat(nativeEffect,",")=="78,73,74,79,2.5","effect creation preserves its native context and returns the exact handle id")
check(not context:remove_effect(object("ASC",78),handle) and table.concat(nativeRemoval,",")=="78,75,123",
    "an already consumed effect returns false without changing its exact removal identity")
handle=context:apply_effect_self(object("ASC",78),object("EffectClass",79),1,{
    {object=object("Lv3Requirements",80),application=true}})
check(handle==124 and table.concat(nativeEffect,",")=="78,73,74,79,1,1,80",
    "native effect preparation clears only application requirements during apply")
local lowerFirst,lowerSecond=object("Lv1CDO",84),object("Lv2CDO",85)
lowerFirst.GetClass=function() return object("Lv1Class",86) end
lowerSecond.GetClass=function() return object("Lv2Class",87) end
local token=context:capture_long_buffs(object("ASC",78),{lowerFirst,lowerSecond})
check(token==321 and table.concat(longCapture,",")=="78,17.5,86,84,87,85","Long state captures exact loaded definitions and native classes")
context:remove_long_buffs(object("ASC",78),token)
check(table.concat(longRemove,",")=="78,75,321","Long state is removed only after its restoration token has been retained")
context:restore_long_buffs(object("ASC",78),token)
check(table.concat(longRestore,",")=="78,82,83,321,17.5" and longDiscard[1]==321,"successful Long restoration releases its native spec copies")
context:capture_long_buffs(object("ASC",78),{lowerSecond})
check(table.concat(longCapture,",")=="78,17.5,87,85,0,0","an unloaded lower tier is skipped")
context:capture_long_buffs(object("ASC",78),{})
check(table.concat(longCapture,",")=="78,17.5,0,0,0,0","no loaded lower tiers require no asset loading")
check(not next(nativeBullets) and not next(calculators) and not next(payloads),"context creation does not install damage hooks")
check(context:empty_class()==emptyClass and context:empty_class()==emptyClass and emptyClassCalls==1,
    "pure resource effects reuse a typed null class without enabling damage")
context:charge_rate(99,1)
check(chargeLoads==0,"ordinary bindings do not load independent charge controls")
local chargeAbility,chargeTick=object("ChargeAbility",97),object("ChargeTick",98)
context:charge_rate(chargeAbility,2,chargeTick)
context:charge_rate(chargeAbility,10,chargeTick)
check(chargeRates[97]==10 and chargeTicks[98]==1 and chargeLoads==2,
    "charge tuning registers its exact reflected callback once and updates the current instance")
context:charge_rate(97,1)
check(not chargeRates[97],"charge cleanup accepts a saved address without accessing a destroyed ability")
local untouched=setmetatable({}, {__index=function() error("Inactive damage must not inspect game objects") end})
check(notificationCount==0,"untuned and resource-only modes must not install damage creation listeners")
context:ability_damage(untouched,1)
context:update_damage()
context:enable_damage()
check(damageQueries==3,"damage discovery uses three native class queries")
context:enable_damage()
check(damageQueries==3 and notificationCount==4,"repeated tuning reuses damage discovery and creation listeners")
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
local owned,filteredPre,filteredPost=first,0,0
context:interface_hook(backend,"filtered",function(bullet)
    check(bullet==first,"filtered interface uses the expected live object")
    filteredPre=filteredPre+1
end,function() filteredPost=filteredPost+1 end,function() return owned end)
interfaceAddress=first:GetAddress()
interfaceName="Unexpected interface name lookup"
hooks.filtered.pre(invalidContext)
owned=nil;hooks.filtered.pre(invalidContext);hooks.filtered.post(invalidContext)
owned=second;hooks.filtered.pre(invalidContext);hooks.filtered.post(invalidContext)
hooks.filtered.post(invalidContext)
check(filteredPre==1 and filteredPost==1 and interfaceAddressCalls==2,
    "nested skipped interface calls must preserve the owning pre/post pair without resolving full names")
owned=nil
for index=1,1000 do hooks.filtered.pre(invalidContext);hooks.filtered.post(invalidContext) end
check(interfaceAddressCalls==2 and filteredPre==1 and filteredPost==1,
    "idle interface hooks must not cross the native address boundary or allocate object names")

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
