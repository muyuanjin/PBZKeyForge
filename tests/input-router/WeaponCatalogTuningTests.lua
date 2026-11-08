package.path = "prototype/input-router/ue4ss/Scripts/?.lua;" .. package.path
local checks, hooks, current, currentFunction = 0, {}, nil, "Function /Fixture:NativeAttack"
local function check(value, message) assert(value, message); checks = checks + 1 end
local function parameter(value)
    return {get=function() return value end, set=function(_,nextValue) value=nextValue end}
end
local function array(values)
    return setmetatable({GetArrayNum=function() return #values end,ForEach=function(_,callback)
        for index,value in ipairs(values) do callback(index,parameter(value)) end
    end},{__index=values})
end
function FName(value) return {ToString=function() return value end} end
local function object(address, properties)
    local value = properties or {}
    function value:IsValid() return self.valid ~= false end
    function value:GetAddress() return address end
    function value:GetPropertyValue(key) return self[key] end
    function value:GetFullName() return "Class " .. (self.path or self.class.path) end
    function value:GetClass() return self.class end
    function value:IsA() return true end
    return value
end
local classes = {}
function StaticFindObject(path) return classes[path] end
function RegisterHook(path, pre, post) hooks[path]={pre=pre,post=post}; return 1,2 end
function UnregisterHook(path) hooks[path]=nil end
local nativeDamage = {}
package.loaded["script-context"] = {new=function() return {
    enable_damage=function() end,
    interface_hook=function(_,_,path,pre,post)
        return RegisterHook(path,function(context,...) return pre(context:get(),...) end,post)
    end,
    owner=function(_,states,resolver)
        return current and (states[current:GetAddress()] or resolver(current)),currentFunction
    end,
    at=function() return current and current:GetFullName(),current and current:GetAddress(),currentFunction end,
    ability_damage=function(_,instance,multiplier)
        nativeDamage[type(instance)=="number" and instance or instance:GetAddress()]=multiplier
    end,
    damage_bullet=function() error("Ordinary native projectile snapshots must not be overwritten") end,
    charge_rate=function() end, update_damage=function() end, reset=function() end
} end}
local Moves = require("complete-moves")
local Router = require("router")
local prefix = "/Script/SGameInputSystem.AbilityInputComponent:"
local modify = "/Script/SGameAttributes.AttributeUtils:ModifyCharacterAttributeValue"
local read = "/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayAttributeValue"
local play = "/Script/LegoAbility.LegoAbility:PlayTimeline"
local initialize = "/Script/SGameCombatSystem.BulletInterface:Initialize"
local chargeWindows = {
    ["/PBZCombatAsset/ActorData/Players/Hun/Abilities/ZhanMaBlade/GA_Hun_ZhanMa_DB.GA_Hun_ZhanMa_DB_C"]=true,
    ["/PBZCombatAsset/ActorData/Players/Hun/Abilities/BronzeSword/GA_Hun_BronzeSword_B_Hold.GA_Hun_BronzeSword_B_Hold_C"]=true,
    ["/PBZCombatAsset/ActorData/Players/Hun/Abilities/BaiShanFengLeiJian/Hold/GA_Hun_FengLeiJian_B_Hold.GA_Hun_FengLeiJian_B_Hold_C"]=true
}

local function fixture(target)
    classes, hooks, nativeDamage, current = {}, {}, {}, nil
    local selected = object(101,{path=target.ability_class})
    local successorClass = object(102,{path="/Fixture/NativeSuccessor.NativeSuccessor_C"})
    local otherClass = object(103,{path="/Fixture/SameTagNeighbor.SameTagNeighbor_C"})
    classes[target.ability_class],classes[successorClass.path] = selected,successorClass
    local tags={GameplayTags=array({{TagName=FName(target.tag or "Ability.Fixture")}})}
    local attack = object(1,{class=selected,AbilityTags=tags,bIsActive=false})
    local successor = object(2,{class=successorClass,AbilityTags=tags,bIsActive=false})
    local other = object(3,{class=otherClass,AbilityTags=tags,bIsActive=false})
    local specs = {}
    for index,ability in ipairs({attack,successor,other}) do
        specs[index]={Ability=object(10+index,{class=ability.class,AbilityTags=tags}),ActiveCount=0,
            NonReplicatedInstances=array({ability}),ReplicatedInstances=array({})}
    end
    local asc = object(20,{ActivatableAbilities={Items=array(specs)}})
    local inputReceiver,input,pawn,attributeOwner = object(21),object(22),object(23),object(24)
    local maxima = {Shachi_Max={CurrentValue=173},SpiritShadow_Max={CurrentValue=11},UltimateChargeCount_Max={CurrentValue=4}}
    local attributeSet = object(25,maxima)
    local timelines,tasks={},{}
    for _,ability in ipairs({attack,successor,other}) do
        local address=ability:GetAddress()
        timelines[address]=object(30+address,{bAutoPlay=true})
        tasks[address]=object(40+address,{Timeline=timelines[address],rate=1.25,CurrentTime=0})
    end
    local backend={pawn=pawn,calls=0,expectedDamage=2.5,expectedBypass=true,limitQueries=0}
    function backend:report() end
    function backend:receiver(value) return value.receiver=="ability_input" and inputReceiver or asc end
    function backend:object(path,kind)
        if path==target.input_action then
            check(kind=="InputAction","The configured native InputAction reaches exact activation")
            return input
        end
        return object(90,{path=path})
    end
    local function query(resource, initial, component)
        local result=parameter(initial)
        hooks[read].post(parameter(component or asc),result,parameter({AttributeName=resource,AttributeOwner=attributeOwner}))
        return result:get()
    end
    local function fee()
        local result=parameter(-7)
        hooks[modify].pre(nil,parameter(pawn),parameter({AttributeName="Shachi"}),parameter(0),result)
        return result:get()
    end
    local function snapshot(ability)
        local bullet=object(80,{class=object(81,{path="/Fixture/NativeBullet"})})
        hooks[initialize].pre(parameter(bullet),parameter(nil),parameter(pawn),parameter(ability))
        bullet.multiplier=nativeDamage[ability:GetAddress()] or 1
        return bullet
    end
    function backend:invoke(value,path,a,b,priority)
        if path:match(":GetGameplayTagCount$") then
            for _,ignored in ipairs(target.ignored_tags or {}) do if a.TagName:ToString()==ignored then return 0 end end
            return 1
        elseif path==prefix.."TryActivateExternalActionAbilityByClass" then
            check(value==inputReceiver and a==selected and b==input and priority==target.priority,
                "Tuning retains the requested existing class, native action and priority")
            self.calls=self.calls+1
            current=attack
            check(fee()==(self.expectedBypass and 0 or -7),"Resource fees belong to this binding's actual activation")
            for _,name in ipairs({"Shachi","SpiritShadow","UltimateChargeCount"}) do
                local maximum=maxima[name.."_Max"]
                check(query(name,0)==(self.expectedBypass and maximum.CurrentValue or 0),
                    "Eligibility and proportional effects use the current actual maximum: "..name)
            end
            attack.bIsActive,specs[1].ActiveCount=true,1
            hooks[play].pre(parameter(attack),parameter(timelines[1]),parameter(1.25))
            self.bullet=snapshot(attack)
            check(self.bullet.multiplier==self.expectedDamage,"Native projectile initialization sees this binding's damage multiplier")
            return true
        elseif path:match(":GetAttributeSet$") then
            check(value==asc and a==attributeOwner,"Full resource effects must query the player's actual AttributeSet")
            self.limitQueries=self.limitQueries+1
            return attributeSet
        elseif path:match(":GetPlayTimelineTask$") then return tasks[value:GetAddress()]
        elseif path:match(":GetTimelineByName$") then return timelines[value:GetAddress()]
        elseif path:match(":SetPlayRate$") then value.rate=a; return end
        error("Unexpected tuning operation: "..path)
    end
    function backend:dispatch(value,phase,_,bindingId,bypass,settings)
        return Moves.dispatch(self,value,phase,bypass,settings,bindingId)
    end
    return backend,attack,successor,other,specs,tasks,timelines,maxima,query,fee,snapshot,inputReceiver,successorClass
end

local root=os.getenv("PBZ_WEAPON_MOVE_CHECK") or "dist/weapon-move-check"
local exercised=0
for _,device in ipairs({"keyboard","pad"}) do
    local config=assert(loadfile(root.."/weapon-"..device.."-tuned.lua"))()
    for _,binding in ipairs(config.bindings) do
        for _,output in ipairs(binding.outputs or {}) do
            local target=assert(config.targets[output.target])
            local backend,attack,successor,other,specs,tasks,timelines,maxima,query,fee,snapshot,inputReceiver,successorClass=fixture(target)
            check(not target.entry_point and target.activate_by_class,"This sweep covers native exact-class entrances")
            local single={targets=config.targets,bindings={binding}}
            local router=Router.new(single,backend)
            local key=device=="pad" and "Gamepad_DPad_Down" or "G"
            router:step(0,{})
            router:step(.1,{[key]=1})
            check(backend.calls==1,"The real scheduler and complete-move executor activate every new catalog class")
            local cost=assert(backend.resource_costs)
            local state=assert(cost.instances[attack:GetAddress()])
            check(state.damage==2.5 and state.startup==1.5 and state.recovery==.75 and state.bypass,
                "Damage, independent phase speeds and resource bypass reach the selected instance")
            check(not cost.instances[other:GetAddress()],"A same-tag neighbor cannot inherit the selected class's effects")
            current=other
            check(query("Shachi",0)==0 and fee()==-7,"Another ability retains its real resources and costs")
            current=attack
            maxima.Shachi_Max.CurrentValue=217
            check(query("Shachi",0)==217,"Full-resource effects follow a changed player capacity")
            for _,phase in ipairs({{"Start",1.875},{"Act",1.25},{"Cancel",.9375}}) do
                attack["Action Phase"]={TagName=FName("CharacterState.Action.Phase."..phase[1])}
                cost:update()
                local expectedRate=chargeWindows[target.ability_class] and 1.875 or phase[2]
                check(tasks[1].rate==expectedRate,"Native charge preparation and action phase speeds retain their distinct timing")
            end
            local firstBullet=backend.bullet
            current=attack
            local continuation=hooks[prefix.."ForceActivateExternalActionAbilityByClass"]
            continuation.pre(parameter(inputReceiver),parameter(successorClass))
            current=successor
            check(query("SpiritShadow",0)==11 and fee()==0,"Native continuation retains full resources and bypass before its first effect")
            successor.bIsActive,specs[2].ActiveCount=true,1
            hooks[play].pre(parameter(successor),parameter(timelines[2]),parameter(1.25))
            continuation.post(parameter(inputReceiver),parameter(true))
            local inherited=assert(cost.instances[successor:GetAddress()])
            check(inherited.damage==2.5 and inherited.startup==1.5 and inherited.recovery==.75 and inherited.bypass,
                "A native follow-up retains all settings from this activation")
            local followupBullet=snapshot(successor)
            check(followupBullet.multiplier==2.5,"Continuation projectile snapshots inherit the activation's damage")
            for _,phase in ipairs({{"Start",1.875},{"Act",1.25},{"Cancel",.9375}}) do
                successor["Action Phase"]={TagName=FName("CharacterState.Action.Phase."..phase[1])}
                cost:update()
                check(tasks[2].rate==phase[2],"The follow-up preserves independent startup, attack and recovery timing")
            end
            attack.bIsActive,successor.bIsActive,specs[1].ActiveCount,specs[2].ActiveCount=false,false,0,0
            cost:update()
            check(not next(cost.instances) and nativeDamage[1]==1 and nativeDamage[2]==1,
                "Ending both abilities clears their binding effects")
            check(tasks[1].rate==1.25,"Ending restores the original timeline speed")
            check(firstBullet.multiplier==2.5 and followupBullet.multiplier==2.5,
                "Already initialized native projectiles retain their activation snapshot after ability cleanup")
            current=attack
            check(query("Shachi",0)==0 and fee()==-7,"Ended effects cannot leak resource bypass into later work")
            backend.expectedDamage,backend.expectedBypass=1.25,false
            check(Moves.dispatch(backend,target,"triggered",false,{damage_multiplier=1.25,startup_speed=2,recovery_speed=.9}),
                "Another binding can normally activate the same class with its own parameters")
            state=assert(cost.instances[1])
            check(state.damage==1.25 and state.startup==2 and state.recovery==.9 and not state.bypass,
                "The next binding replaces previous effects with its own damage and phase speeds")
            check(firstBullet.multiplier==2.5 and backend.bullet.multiplier==1.25,
                "Earlier and later bindings keep independent projectile damage snapshots")
            attack.bIsActive,specs[1].ActiveCount=false,0
            cost:update();cost:reset();Moves.reset(backend)
            check(not next(hooks),"Reset releases the exact-class activation's hooks and tuning state")
            exercised=exercised+1
        end
    end
end
check(exercised>200,"Exercise every added weapon and shadow class on both input devices")
print("WeaponCatalogTuningTests PASS "..checks.."; exact-class bindings="..exercised)
