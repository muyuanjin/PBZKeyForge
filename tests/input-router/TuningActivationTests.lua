package.path = "prototype/input-router/ue4ss/Scripts/?.lua;" .. package.path
local checks,hooks,registrations,failAt,contexts = 0,{},0,nil,{}
local function check(value,message) assert(value,message);checks=checks+1 end
function RegisterHook(path,pre,post)
    registrations=registrations+1
    if registrations==failAt then error("unsupported updated function") end
    check(not hooks[path],"a hook must not be installed twice")
    hooks[path]={pre=pre,post=post};return registrations,registrations+100
end
function UnregisterHook(path) hooks[path]=nil end
package.loaded["script-context"]={new=function()
    local context={}
    contexts[#contexts+1]=context
    function context:enable_damage() self.damage=true end
    function context:ability_damage() end
    function context:reset() self.closed=true;self.damage=false end
    function context:interface_hook(_,path,pre,post) return RegisterHook(path,pre,post) end
    return context
end}
local Costs=require("resource-costs")
local Backend=require("backend")
local function backend()
    return {report=function() end,receiver=function() end,object=function() error("Unexpected game object access") end}
end
local function prefix(value)
    for path in pairs(hooks) do if path:find(value,1,true) then return true end end
    return false
end
local ordinary=backend()
check(Costs.with(ordinary,{},false,function() return "ordinary" end,{damage_multiplier=1,startup_speed=1,recovery_speed=1})=="ordinary",
    "explicit defaults retain the original callback")
check(not ordinary.resource_costs and #contexts==0 and not next(hooks),"defaults load no context and install no tuning hooks")

for _,settings in ipairs({{super_armor=true},{invincible=true}}) do
    local protection=Costs.new(backend())
    protection:enable(false,settings)
    check(not prefix("ModifyCharacterAttributeValue") and not prefix("LegoAbility") and not prefix("BulletInterface"),
        "protection-only settings install no resource, speed or damage hooks")
    check(not contexts[#contexts].damage,"protection-only settings do not enable native damage")
    protection:reset()
end
local resources=Costs.new(backend())
resources:enable(true,{})
check(prefix("ModifyCharacterAttributeValue") and not prefix("LegoAbility") and not prefix("BulletInterface"),
    "resource bypass installs neither speed nor bullet hooks")
check(not contexts[#contexts].damage,"resource bypass does not enable native damage")
local before=registrations
resources:enable(true,{})
check(registrations==before,"repeated resource requests reuse their hooks")
resources:reset();check(not next(hooks),"resource reset removes all Lua hooks")

local speeds=Costs.new(backend())
speeds:enable(false,{startup_speed=2})
check(prefix("LegoAbility") and not prefix("ModifyCharacterAttributeValue") and not prefix("BulletInterface"),
    "speed settings install neither resource nor bullet hooks")
check(not contexts[#contexts].damage,"speed settings do not enable native damage")
speeds:reset()

local damage=Costs.new(backend())
damage:enable(false,{damage_multiplier=100})
check(prefix("BulletInterface") and not prefix("LegoAbility") and not prefix("ModifyCharacterAttributeValue"),
    "damage settings install neither resource nor speed hooks")
check(contexts[#contexts].damage,"damage settings explicitly enable native damage")
damage:enable(true,{startup_speed=2,damage_multiplier=100})
check(prefix("LegoAbility") and prefix("ModifyCharacterAttributeValue"),"later features can join an existing context")
local failedRestore={IsValid=function() return true end}
damage.instances[1]={task=failedRestore,applied_rate=2,base_rate=1}
damage.set_rate=function() error("updated timeline rejected") end
check(not pcall(damage.reset,damage),"failed timeline restoration is reported")
check(damage.closed and contexts[#contexts].closed and not contexts[#contexts].damage and not next(hooks),
    "failed timeline restoration still disables native damage and removes every Lua hook")

failAt=registrations+3
check(not pcall(Costs.new,backend()),"partial constructor failure propagates")
check(contexts[#contexts].closed and not next(hooks),"partial constructor failure cleans its context and earlier hooks")
failAt=nil
local partial=Costs.new(backend())
failAt=registrations+2
check(not pcall(partial.enable,partial,true,{}),"feature installation failure propagates")
check(partial.closed and contexts[#contexts].closed and not next(hooks),"partial feature installation removes existing and new hooks")
failAt=nil
local partialDamage=Costs.new(backend())
failAt=registrations+2
check(not pcall(partialDamage.enable,partialDamage,false,{damage_multiplier=100}),"partial damage registration failure propagates")
check(partialDamage.closed and contexts[#contexts].closed and not contexts[#contexts].damage and not next(hooks),
    "failure after native damage activation disables it and removes earlier bullet hooks")
failAt=nil

local asynchronous=Costs.new(backend())
asynchronous.context.failure="watch_damage_bullet: Bullet Initialize virtual dispatch is unsupported"
asynchronous.context.closed=true
local resumed,message=pcall(asynchronous.update,asynchronous)
check(not resumed and message==asynchronous.context.failure,"a later scheduler pause exposes the original asynchronous failure")
check(asynchronous.closed and not next(hooks),"an asynchronously disabled context removes the remaining tuning hooks")
local failedCleanup=Costs.new(backend())
failedCleanup.context.closed=true
failedCleanup.context.failure="primary native registration failure"
failedCleanup.instances[1]={task=failedRestore,applied_rate=2,base_rate=1}
failedCleanup.set_rate=function() error("secondary timeline restoration failure") end
local clean,combined=pcall(failedCleanup.update,failedCleanup)
check(not clean and combined:find("primary native registration failure",1,true)
    and combined:find("secondary timeline restoration failure",1,true),
    "A failed cleanup retains both the original registration error and the restoration failure")
check(not next(hooks),"Cleanup errors still remove all remaining Lua hooks")

local dispatch=setmetatable({config={enabled=true},state={ready=true},invoke=function() return true end},Backend)
local action={IsValid=function() return true end,GetAddress=function() return 5 end,GetFullName=function() return "InputAction /Input/Attack.Attack" end}
check(dispatch:dispatch({kind="action",object=action},"triggered",nil,"default",false,{damage_multiplier=1,startup_speed=1,recovery_speed=1}),
    "direct InputAction dispatch accepts explicit defaults")
check(not dispatch.resource_costs,"direct InputAction defaults do not create tuning")
dispatch.native_input={coverage=setmetatable({}, {__index=function() error("Untuned input must not enumerate coverage") end})}
dispatch:prepare_input_settings({})
dispatch:prepare_input_settings({{target={kind="action",object=action},settings={damage_multiplier=1}}})
check(not dispatch.native_tuning_actions,"idle and untuned frames skip native tuning coverage")
dispatch.native_input={coverage={{action="/Input/Attack",key="MouseWheelAxis",owner="game"}}}
dispatch:prepare_input_settings({{target={kind="action",object=action},bypass_resources=true,settings={damage_multiplier=100}}})
check(not dispatch.resource_costs,"an action retaining a native input does not install unused tuning")
dispatch.config.enabled=false
check(not dispatch:dispatch({kind="action",object=action},"triggered",nil,"disabled",true,{damage_multiplier=100}),
    "disabled enhancement rejects tuned dispatch before touching game objects")
check(not dispatch.resource_costs and not next(hooks),"disabled dispatch leaves every tuning hook absent")

do
    local owner=setmetatable({config={enabled=true},native_input={coverage={
        {action="/Input/Attack",key="MouseWheelAxis",owner="game"}}}},Backend)
    local request={{target={kind="action",object=action},bypass_resources=true,settings={damage_multiplier=3}}}
    owner:prepare_input_settings(request)
    local initial=owner.native_tuning_actions
    check(initial["/Input/Attack"] and not owner.resource_costs,"native inputs retain their ordinary tuning policy")
    owner:prepare_input_settings(request)
    check(owner.native_tuning_actions==initial,"stable frames reuse the native tuning coverage")
    owner.native_input.coverage={{action="/Input/Attack",key="E",owner="scheduler"}}
    owner:prepare_input_settings(request)
    check(owner.native_tuning_actions~=initial and owner.resource_costs.inputs[5].damage==3,
        "new native ownership enables the requested binding tuning")
    owner.resource_costs.inputs={}
    owner.native_input.coverage={{action="/Input/Attack",key="MouseWheelAxis",owner="game"}}
    owner:prepare_input_settings(request)
    check(not owner.resource_costs.inputs[5].bypass and owner.resource_costs.inputs[5].damage==1,
        "returning native ownership immediately restores ordinary tuning")
    owner.resource_costs:reset()
end

do
    local owner=setmetatable(backend(),Backend)
    owner.captured={}
    local nativeRestores=0
    owner.native_input={restore=function() nativeRestores=nativeRestores+1 end}
    function owner:release_holder() end
    local cost=Costs.new(owner);owner.resource_costs=cost
    local tasks={
        {IsValid=function() return true end,rate=4},
        {IsValid=function() return true end,rate=3},
        {IsValid=function() return true end,rate=2}
    }
    local rateCalls,damageCalls,unregisterCalls={},{},{}
    for index,task in ipairs(tasks) do
        cost.instances[index]={task=task,applied_rate=task.rate,base_rate=1}
    end
    function cost:set_rate(task,rate)
        rateCalls[task]=(rateCalls[task] or 0)+1
        if task==tasks[1] and rateCalls[task]==1 then error("temporary timeline failure") end
        task.rate=rate
    end
    function cost.context:ability_damage(address)
        damageCalls[address]=(damageCalls[address] or 0)+1
        if address==2 and damageCalls[address]==1 then error("temporary damage cleanup failure") end
    end
    local resetContext,calls=cost.context.reset,0
    function cost.context:reset()
        resetContext(self);calls=calls+1
        if calls==1 then error("temporary native reset failure") end
    end
    local unregister,failedPath=UnregisterHook,next(cost.hooks)
    UnregisterHook=function(path,...)
        unregisterCalls[path]=(unregisterCalls[path] or 0)+1
        if path==failedPath and unregisterCalls[path]==1 then error("temporary hook removal failure") end
        unregister(path,...)
    end
    local ok,message=pcall(owner.shutdown,owner)
    check(not ok and message:find("temporary timeline failure",1,true)
        and message:find("temporary damage cleanup failure",1,true)
        and message:find("temporary native reset failure",1,true)
        and message:find("temporary hook removal failure",1,true),
        "shutdown must report all independent cleanup failures")
    check(owner.resource_costs==cost and cost.closed and nativeRestores==1,
        "failed tuning cleanup retains its owner while native input restoration proceeds")
    check(cost.instances[1] and cost.instances[2] and not cost.instances[3],
        "only failed activation cleanup remains pending")
    check(cost.instances[2].applied_rate==nil and tasks[2].rate==1 and tasks[3].rate==1,
        "successful rate restoration is retained when damage cleanup fails")
    check(cost.hooks[failedPath] and next(hooks)==failedPath and next(hooks,failedPath)==nil,
        "only the failed hook remains pending and installed")
    check(pcall(owner.shutdown,owner,true) and not owner.resource_costs and nativeRestores==2,
        "retry finishes pending cleanup before releasing the tuning owner")
    check(tasks[1].rate==1 and rateCalls[tasks[1]]==2 and rateCalls[tasks[2]]==1 and rateCalls[tasks[3]]==1,
        "retry restores failed rates without repeating completed restorations")
    check(next(cost.instances)==nil and next(cost.hooks)==nil and next(hooks)==nil and calls==2,
        "retry clears all activation, Hook and native context cleanup")
    for path,count in pairs(unregisterCalls) do
        check(count==(path==failedPath and 2 or 1),"successful Hook removal must not repeat")
    end
    UnregisterHook=unregister
end

for _,stage in ipairs({"constructor","feature"}) do
    local owner=backend()
    local cost
    if stage=="feature" then cost=Costs.new(owner);owner.resource_costs=cost end
    local unregister,failed=UnregisterHook,false
    UnregisterHook=function(path,...)
        if not failed then failed=true;error("temporary Hook cleanup failure") end
        unregister(path,...)
    end
    failAt=registrations+3
    local ok
    if stage=="constructor" then ok=pcall(Costs.new,owner)
    else ok=pcall(cost.enable,cost,true,{}) end
    failAt=nil
    check(not ok and owner.resource_costs and owner.resource_costs.closed and next(owner.resource_costs.hooks),
        "failed "..stage.." cleanup must remain owned for retry")
    check(pcall(owner.resource_costs.reset,owner.resource_costs) and next(hooks)==nil,
        "failed "..stage.." cleanup can remove its remaining Hook on retry")
    UnregisterHook=unregister
end
print("TuningActivationTests PASS "..checks)
