local Context = require("script-context")
local Restrictions = require("move-restrictions")
local Selection = require("ability-selection")
local Costs = {}
Costs.__index = Costs

local resources = {UltimateChargeCount="UltimateChargeCount_Max", Shachi="Shachi_Max", SpiritShadow="SpiritShadow_Max"}
-- These recovery abilities pay by overriding Shachi instead of subtracting it.
local resource_resets = {
    ["/PBZCombatAsset/ActorData/Players/Hun/Abilities/HeiMoShang/GA_Hun_HeiMoShang_Reborn.GA_Hun_HeiMoShang_Reborn_C"]=true,
    ["/PBZCombatAsset/ActorData/Players/Hun/Abilities/ShachiAbility/XuKongHuaShan/GA_XuKongHuaShan_Reborn.GA_XuKongHuaShan_Reborn_C"]=true
}
-- Use the native status tags without the resource and damage-reduction
-- modifiers bundled in some GameplayEffects. Each activation owns one count.
local protection_tags = {
    super_armor="CharacterState.Hitreact.Endure.Stand.Ultimate",
    invincible="CharacterState.Stand.Invulnerable.Debug"
}
-- Zhan Ma DB enters Act after 0.2s while still charging. Stop startup tuning
-- at its first bullet action so both attacks retain their native timing.
local startup_windows = {
    ["/PBZCombatAsset/ActorData/Players/Hun/Abilities/ZhanMaBlade/GA_Hun_ZhanMa_DB.GA_Hun_ZhanMa_DB_C"]=
        {timeline="Main",entry_time=0,attack_start=1.3485949039459229},
    ["/PBZCombatAsset/ActorData/Players/Hun/Abilities/BronzeSword/GA_Hun_BronzeSword_B_Hold.GA_Hun_BronzeSword_B_Hold_C"]=
        {timeline="Main",entry_time=0},
    ["/PBZCombatAsset/ActorData/Players/Hun/Abilities/BaiShanFengLeiJian/Hold/GA_Hun_FengLeiJian_B_Hold.GA_Hun_FengLeiJian_B_Hold_C"]=
        {timeline="Main",entry_time=0}
}
local charge_ticks = {
    ["/PBZCombatAsset/ActorData/Players/Hun/Abilities/BaiShanFengLeiJian/Hold/GA_Hun_FengLeiJian_B_Hold.GA_Hun_FengLeiJian_B_Hold_C"]=
        "/PBZCombatAsset/ActorData/Players/Hun/Abilities/BaiShanFengLeiJian/Hold/GA_Hun_FengLeiJian_B_Hold.GA_Hun_FengLeiJian_B_Hold_C:OnTick_F49F244647F3D911EDC36F8CD34569D0"
}
-- These instant effects contain only a resource deduction. Other effects keep
-- their original class, including resource gains and resource-based attacks.
local resource_effects = {
    ["/PBZCombatAsset/ActorData/Players/Hun/Weapon/SlitheringSword/UpgradeData/GE_SlitheringSword_EvalutionCombo_CostShachi.GE_SlitheringSword_EvalutionCombo_CostShachi_C"]=true,
    ["/PBZCombatAsset/ActorData/Players/Hun/Weapon/ChaosBlade/GE_Chaos_SwitchShaQiCost.GE_Chaos_SwitchShaQiCost_C"]=true,
    ["/PBZCombatAsset/Prototypes/Abilities/Base/GE/GE_Cost_Shachi_ToZero.GE_Cost_Shachi_ToZero_C"]=true,
    ["/PBZCombatAsset/ActorData/Players/Hun/Weapon/ZiMingJian/GameEffects/GE_ZiMingJian_CostShaChi.GE_ZiMingJian_CostShaChi_C"]=true
}
local function valid(object) return object and object:IsValid() end
local function class_path(object) return object:GetClass():GetFullName():gsub("^%S+ ", "") end
local function attribute_name(attribute)
    local name = attribute:get().AttributeName
    return type(name) == "string" and name or name:ToString()
end

local target_matcher = Selection.matcher

local function untuned(source)
    return source and not source.bypass and source.damage == 1 and source.startup == 1 and source.recovery == 1
        and not source.super_armor and not source.invincible and #(source.restriction_bypasses or {}) == 0
end

local function resource_maximum(backend,component,attribute,name)
    local owner = attribute:get().AttributeOwner
    if not valid(owner) then return end
    local set = backend:invoke(component,"/Script/GameplayAbilities.AbilitySystemComponent:GetAttributeSet",owner)
    if not valid(set) then return end
    local value = set:GetPropertyValue(resources[name])
    return value and value.CurrentValue
end

local function dual_axe_maximum(backend)
    local component = backend:receiver({receiver="ability_system"})
    if not valid(component) then return end
    local class = backend:object("/Script/PBZCombatAsset.GA_TianShaShuangYue_EnergyChargePassiveBase")
    if not valid(class) then return end
    local specs = component.ActivatableAbilities.Items
    for index = 1, specs:GetArrayNum() do
        local spec = specs[index]
        if spec.ActiveCount > 0 and valid(spec.Ability) and spec.Ability:IsA(class) then
            for _,instances in ipairs({spec.NonReplicatedInstances,spec.ReplicatedInstances}) do
                for slot = 1, instances:GetArrayNum() do
                    local passive = instances[slot]
                    if valid(passive) and passive:GetPropertyValue("bIsActive") then
                        local first, second = passive:GetPropertyValue("ChargeObjectPhase1"),passive:GetPropertyValue("ChargeObjectPhase2")
                        if valid(first) and valid(second) then
                            return backend:invoke(first,"/Script/PBZCombatAsset.UIChargeObject:GetMax")
                                + backend:invoke(second,"/Script/PBZCombatAsset.UIChargeObject:GetMax")
                        end
                    end
                end
            end
        end
    end
end

function Costs.new(backend)
    local self = setmetatable({backend=backend, generation=backend.generation,
        instances={}, inputs={}, transfers={}, hooks={}, frame=0},Costs)
    local ok,failure = pcall(function()
        self.context = Context.new(function(message) backend:report("damage-scaling",message) end)
        self:install_transfer_hooks()
    end)
    if not ok then
        local cleaned,cleanup = pcall(self.reset,self)
        if not cleaned then backend.resource_costs = self end
        error(tostring(failure)..(cleaned and "" or "\n"..tostring(cleanup)),0)
    end
    return self
end

function Costs:hook(path,pre,post)
    if self.hooks[path] then return end
    local function guarded(callback)
        if callback then return function(...)
            if not self.closed then return callback(...) end
        end end
    end
    self.hooks[path] = {RegisterHook(path,guarded(pre),guarded(post))}
end

function Costs:install_transfer_hooks()
    local backend = self.backend
    local function hook(...) return self:hook(...) end
    local function transfer(path,receiver,selector)
        hook(path,function(component,argument,input)
            if not self.activating and not next(self.instances)
                and (receiver ~= "ability_input" or not input or not next(self.inputs)) then
                self:begin_transfer(nil, nil)
                return
            end
            local owned = backend:receiver({receiver=receiver})
            local is_owned = valid(owned) and component:get():GetAddress() == owned:GetAddress()
            local source = is_owned and self:owner()
            local activation = self.activating and self.transfers[#self.transfers]
            local initial = not source and activation
            if not source and is_owned and activation then source = activation.source end
            if not source and is_owned and receiver == "ability_input" and not self.activating and input and valid(input:get()) then
                source = self.inputs[input:get():GetAddress()]
                if source and backend.native_input and backend.native_input:native_contributes(input:get()) then source = nil end
            end
            self:begin_transfer(source and not source.callback_only and source,is_owned and selector(argument:get()) or nil)
            local current = self.transfers[#self.transfers]
            if current.source and current.target and #(current.source.restriction_bypasses or {})>0 then
                local target = {}
                for key,value in pairs(current.target) do target[key] = value end
                target.restriction_groups = current.source.restriction_target and current.source.restriction_target.restriction_groups
                current.restrictions = Restrictions.begin(backend,target,current.source)
            end
            if initial then
                local nested = self.transfers[#self.transfers]
                nested.phase_window = activation.phase_window
                nested.phase_windows = activation.phase_windows
                for _,state in pairs(nested.armed) do
                    state.phase_window, state.phase_windows = nested.phase_window,nested.phase_windows
                end
            end
        end,function(_,result) self:end_transfer(result:get()) end)
    end
    local function by_class(value)
        return valid(value) and {ability_class_address=value:GetAddress()} or {}
    end
    local function by_tag(value) return {resource_tags={value.TagName:ToString()}} end
    for _,name in ipairs({"ForceActivateExternalActionAbilityByClass","TryActivateExternalActionAbilityByClass","ForceActivateExternalActionAbilityByClassWithEvent"}) do
        transfer("/Script/SGameInputSystem.AbilityInputComponent:"..name,"ability_input",by_class)
    end
    for _,name in ipairs({"ForceActivateExternalActionAbilityByTag","TryActivateExternalActionAbilityByTag","TryActivateExternalActionAbilityInterruptLowerByTag"}) do
        transfer("/Script/SGameInputSystem.AbilityInputComponent:"..name,"ability_input",by_tag)
    end
    transfer("/Script/GameplayAbilities.AbilitySystemComponent:TryActivateAbilityByClass","ability_system",by_class)
    transfer("/Script/GameplayAbilities.AbilitySystemComponent:TryActivateAbilitiesByTag","ability_system",function(value)
        local tags = {}
        local entries = value.GameplayTags
        for index = 1, entries:GetArrayNum() do tags[#tags+1] = entries[index].TagName:ToString() end
        return {activation_tags=tags}
    end)
end

function Costs:install_resource_hooks()
    local function hook(...) return self:hook(...) end
    local function pure_cost(effectClass)
        local effect = effectClass:get()
        return valid(effect) and resource_effects[effect:GetFullName():gsub("^%S+ ","")]
    end
    local function ability_effect(ability,effectClass)
        if not pure_cost(effectClass) then return end
        local instance = ability:get()
        if not valid(instance) then return end
        local address = instance:GetAddress()
        local state = self.instances[address] or self:owner()
        if state and state.bypass and state.instance:GetAddress() == address then
            effectClass:set(self.context:empty_class())
        end
    end
    hook("/Script/GameplayAbilities.GameplayAbility:BP_ApplyGameplayEffectToOwner",ability_effect)
    hook("/Script/GameplayAbilities.GameplayAbility:MakeOutgoingGameplayEffectSpec",ability_effect)
    hook("/Script/GameplayAbilities.AbilitySystemComponent:BP_ApplyGameplayEffectToSelf",function(component,effectClass)
        if not pure_cost(effectClass) then return end
        local owned = self.backend:receiver({receiver="ability_system"})
        if not valid(owned) or not valid(component:get()) or component:get():GetAddress() ~= owned:GetAddress() then return end
        local state = self:owner()
        if state and state.bypass then effectClass:set(self.context:empty_class()) end
    end)
    hook("/Script/SGameAttributes.AttributeUtils:ModifyCharacterAttributeValue",function(_,actor,attribute,operation,value)
        if not next(self.instances) and #self.transfers == 0 then return end
        local name = attribute_name(attribute)
        if not resources[name] then return end
        local additive = operation:get() == 0 and value:get() < 0
        local reset = name == "Shachi" and operation:get() == 3
        if not additive and not reset then return end
        local is_owned = valid(self.backend.pawn) and valid(actor:get())
            and actor:get():GetAddress() == self.backend.pawn:GetAddress()
        local state,fn
        if is_owned then state,fn = self:owner()
        else state,fn = self.context:owner(self.instances,function() return false end) end
        if reset and (not state or not resource_resets[class_path(state.instance)]) then return end
        self:observe_cost("write",actor:get(),nil,state,fn,
            not is_owned and "actor-mismatch" or not state and "owner-missing" or not state.bypass and "owner-pays" or "applied",
            nil,name,value:get(),operation:get())
        if not is_owned then return end
        if state and state.bypass then
            if reset then operation:set(0) end
            value:set(0)
        end
    end)
    local function query(component,attribute,original)
        if not next(self.instances) and #self.transfers == 0 then return end
        local name = attribute_name(attribute)
        if not resources[name] then return end
        local owned = self.backend:receiver({receiver="ability_system"})
        local queried = component:get()
        local is_owned = valid(owned) and valid(queried) and queried:GetAddress() == owned:GetAddress()
        local state,fn
        if is_owned then state,fn = self:owner()
        else state,fn = self.context:owner(self.instances,function() return false end) end
        self:observe_cost("read",nil,component:get(),state,fn,
            not is_owned and "asc-mismatch" or not state and "owner-missing" or not state.bypass and "owner-pays"
                or "applied",owned,name,original:get())
        if not is_owned then return end
        if state and state.bypass then
            local maximum = resource_maximum(self.backend,owned,attribute,name)
            if maximum ~= nil then original:set(maximum) end
        end
    end
    hook("/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayAttributeValue",function() end,
        function(component,original,attribute) return query(component,attribute,original) end)
    hook("/Script/GameplayAbilities.AbilitySystemBlueprintLibrary:GetFloatAttributeFromAbilitySystemComponent",function() end,
        function(_,original,component,attribute) return query(component,attribute,original) end)
    local function actor_query(_,original,actor,attribute)
        if not next(self.instances) and #self.transfers == 0 then return end
        if not valid(self.backend.pawn) or not valid(actor:get())
            or actor:get():GetAddress() ~= self.backend.pawn:GetAddress() then return end
        return query({get=function() return self.backend:receiver({receiver="ability_system"}) end},attribute,original)
    end
    hook("/Script/SGameAttributes.AttributeUtils:GetCharacterAttributeValue",function() end,actor_query)
    hook("/Script/GameplayAbilities.AbilitySystemBlueprintLibrary:GetFloatAttribute",function() end,actor_query)
    hook("/Script/GameplayAbilities.GameplayAbility:SendGameplayEvent",function(ability,eventTag,payload)
        if eventTag:get().TagName:ToString() ~= "GameplayEvent.Ability.TianShaShuangYue.ConsumeAll" then return end
        local instance = ability:get()
        if not valid(instance) then return end
        local state = self:inherit(instance)
        if not state or not state.bypass then return end
        local value = payload:get().OptionalObject
        local expected = instance:GetPropertyValue("Payload")
        local class = self.backend:object("/Script/PBZCombatAsset.TianShaShuangYue_ConsumeAllPayload")
        if not valid(class) or not valid(value) or not value:IsA(class)
            or not valid(expected) or expected:GetAddress() ~= value:GetAddress() then return end
        local maximum = dual_axe_maximum(self.backend)
        if maximum == nil then return end
        value:SetPropertyValue("ConsumeValue",maximum)
        eventTag:set({TagName=FName("None")})
    end)
end

function Costs:install_damage_hooks()
    local backend = self.backend
    self.context:enable_damage()
    local initialize = "/Script/SGameCombatSystem.BulletInterface:Initialize"
    self.hooks[initialize] = {self.context:interface_hook(backend,initialize,function(_,parent,avatar,ability)
        self:inherit(ability:get())
    end)}
    local start = "/Script/SGameCombatSystem.BulletInterface:Start"
    self.hooks[start] = {self.context:interface_hook(backend,start,function(bullet)
        local ability = backend:invoke(bullet,"/Script/SGameCombatSystem.BulletInterface:GetSourceAbility")
        local state = self:inherit(ability)
        if state and state.callback_only then self:remember_bullet(bullet,state.damage) end
    end)}
end

function Costs:install_speed_hooks()
    local backend = self.backend
    local function hook(...) return self:hook(...) end
    hook("/Script/SGameInputSystem.AbilityTask_WaitSInputHold:CreateTask",function(_,ability,_,duration)
        local instance = ability:get()
        if not valid(instance) or not startup_windows[class_path(instance)] then return end
        local transfer = self.transfers[#self.transfers]
        local state
        if transfer and transfer.active[instance:GetAddress()] then state = transfer.source
        else state = self.instances[instance:GetAddress()] end
        if state and state.startup ~= 1 then duration:set(duration:get()/state.startup) end
    end)
    local function timeline_rate(ability,timeline,rate)
        local state = self:inherit(ability)
        local transfer = valid(ability) and self.transfers[#self.transfers]
        local active = transfer and transfer.active[ability:GetAddress()]
        if type(active) == "table" and valid(timeline) then active.rates[timeline:GetAddress()] = rate end
        if not state or not valid(timeline) then return end
        state.timeline_rates = state.timeline_rates or {}
        state.timeline_rates[timeline:GetAddress()] = rate
    end
    hook("/Script/LegoAbility.LGATask_PlayTimeline:PlayTimeline",function(_,ability,_,timeline,rate)
        timeline_rate(ability:get(),timeline:get(),rate:get())
    end)
    hook("/Script/LegoAbility.LegoAbility:PlayTimeline",function(ability,timeline,rate)
        timeline_rate(ability:get(),timeline:get(),rate:get())
    end)
    hook("/Script/LegoAbility.LegoAbility:PlayTimelineByName",function(ability,name,rate)
        local object = ability:get()
        local transfer = self.transfers[#self.transfers]
        if not self:inherit(object) and not (valid(object) and transfer
            and type(transfer.active[object:GetAddress()]) == "table") then return end
        timeline_rate(object,backend:invoke(object,"/Script/LegoAbility.LegoAbility:GetTimelineByName",name:get()),rate:get())
    end)
    hook("/Script/LegoAbility.LGATask_PlayTimeline:SetPlayRate",function(context,rate)
        if self.setting_rate then return end
        for _,state in pairs(self.instances) do
            if valid(state.task) and state.task:GetAddress() == context:get():GetAddress() then
                state.base_rate, state.applied_rate = rate:get(),nil
            end
        end
    end)
end

function Costs:enable(bypass,settings)
    assert(not self.closed,"Binding tuning is closed")
    settings = settings or {}
    local ok,failure = pcall(function()
        if (settings.resource_tags or #(settings.restriction_bypasses or {}) > 0) and not self.restrictions_enabled then
            self:hook("/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayTagCount",function() end,
                function(component,original,tag)
                    if not next(self.instances) and #self.transfers==0 then return end
                    local owned=self.backend:receiver({receiver="ability_system"})
                    if not valid(owned) or component:get():GetAddress() ~= owned:GetAddress() then return end
                    local state=self:owner()
                    if not Restrictions.active(state) then return end
                    original:set(Restrictions.query(self.backend,state,tag:get().TagName:ToString(),original:get()))
                end)
            for _,method in ipairs({"HasMatchingGameplayTag","HasAnyMatchingGameplayTags","HasAllMatchingGameplayTags"}) do
                local path="/Script/GameplayTags.GameplayTagAssetInterface:"..method
                local calls={}
                self.hooks[path]={self.context:interface_hook(self.backend,path,function(object,tags)
                    local owned=self.backend:receiver({receiver="ability_system"})
                    local pawn=self.backend.pawn
                    local state
                    if valid(owned) and valid(object) and (object:GetAddress()==owned:GetAddress()
                        or valid(pawn) and object:GetAddress()==pawn:GetAddress()) then state=self:owner() end
                    if not Restrictions.active(state) then calls[#calls+1]=false;return end
                    local call={state=state,component=owned,tags={},all=method=="HasAllMatchingGameplayTags"}
                    local function consider(name)
                        local value=Restrictions.override(state,name)
                        if value~=nil then
                            call.affected=true
                            if value==1 then call.required=true end
                            if value==0 then call.blocked=true end
                        else call.tags[#call.tags+1]=name end
                    end
                    local value=tags:get()
                    if method=="HasMatchingGameplayTag" then consider(value.TagName:ToString())
                    else
                        for index=1,value.GameplayTags:GetArrayNum() do
                            consider(value.GameplayTags[index].TagName:ToString())
                        end
                    end
                    calls[#calls+1]=call
                end,function(_,original)
                    local call=table.remove(calls)
                    if not call or not call.affected then return end
                    if call.all then
                        if call.blocked then original:set(false);return end
                        if original:get() then return end
                        for _,name in ipairs(call.tags) do
                            if self.backend:invoke(call.component,
                                "/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayTagCount",{TagName=FName(name)})<=0 then
                                original:set(false);return
                            end
                        end
                        original:set(true);return
                    end
                    if call.required then original:set(true);return end
                    if not original:get() then return end
                    local result=false
                    for _,name in ipairs(call.tags) do
                        if self.backend:invoke(call.component,
                            "/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayTagCount",{TagName=FName(name)})>0 then
                            result=true;break
                        end
                    end
                    original:set(result)
                end,function()
                    if not next(self.instances) and #self.transfers==0 then return end
                    return self.backend:receiver({receiver="ability_system"}),self.backend.pawn
                end)}
            end
            self.restrictions_enabled = true
        end
        if bypass and not self.resources_enabled then
            self:install_resource_hooks()
            self.resources_enabled = true
        end
        if ((settings.startup_speed or 1) ~= 1 or (settings.recovery_speed or 1) ~= 1) and not self.speed_enabled then
            self:install_speed_hooks()
            self.speed_enabled = true
        end
        if (settings.damage_multiplier or 1) ~= 1 and not self.damage_enabled then
            self:install_damage_hooks()
            self.damage_enabled = true
        end
    end)
    if not ok then
        local cleaned,cleanup = pcall(self.reset,self)
        if not cleaned then self.backend.resource_costs = self
        elseif self.backend.resource_costs == self then self.backend.resource_costs = nil end
        error(tostring(failure)..(cleaned and "" or "\n"..tostring(cleanup)),0)
    end
end

function Costs:observe_cost(kind,actor,component,state,fn,cause,owned,attribute,value,operation)
    local source
    if self.activating then
        for _,transfer in ipairs(self.transfers) do
            if transfer.source and transfer.source.bypass then source = transfer.source end
        end
    end
    if not source and not (state and state.bypass) then return end
    fn = fn or select(3,self.context.at(1)) or "unknown"
    local key = kind..":"..tostring(cause)..":"..fn..":"..attribute
    self.observations = self.observations or {}
    if self.observations[key] then return end
    self.observations[key] = true
    local contextName,contextAddress = self.context.at(1)
    local ownerName,ownerAddress
    if state and valid(state.instance) then
        ownerName,ownerAddress = state.instance:GetFullName(),state.instance:GetAddress()
    end
    local function address(object) return valid(object) and tostring(object:GetAddress()) or "nil" end
    owned = owned or self.backend:receiver({receiver="ability_system"})
    fn = fn or "unknown"
    self.backend:report("resource-cost:"..kind..":"..tostring(cause)..":"..fn,
        "Resource bypass "..kind.." "..tostring(cause)..": actor="..address(actor).." pawn="..address(self.backend.pawn)
            .." ASC="..address(component).." ownedASC="..address(owned).." owner="..tostring(state and state.bypass)
            .." ownerAddress="..tostring(ownerAddress).." ownerName=["..tostring(ownerName).."] attribute="..attribute
            ..(kind == "write" and " op="..tostring(operation).." value="..tostring(value) or " original="..tostring(value))
            .." contextAddress="..tostring(contextAddress).." contextName=["..tostring(contextName).."]"
            .." activating="..tostring(self.activating).." source="..tostring(source and source.bypass).." fn="..fn)
end

function Costs:remember_bullet(bullet,multiplier)
    local actor = self.backend:invoke(bullet,"/Script/SGameCombatSystem.BulletInterface:GetActor")
    self.context:damage_bullet(bullet,actor,multiplier)
end

function Costs:begin_transfer(source,target)
    local transfer = {source=source,target=target,armed={},active={},previous={}}
    self.transfers[#self.transfers+1] = transfer
    if not target or not source and not next(self.instances) then return end
    if untuned(source) and not next(self.instances) then transfer.passive=true; return end
    transfer.matches,transfer.selection = target_matcher(target)
    if not source then
        for address, state in pairs(self.instances) do
            local instance = state.instance
            if valid(instance) and transfer.matches(instance) then
                if not instance:GetPropertyValue("bIsActive") then self:restore(address)
                elseif instance:IsA(self.backend:object("/Script/LegoAbility.LegoAbility")) then
                    local task = self.backend:invoke(instance,"/Script/LegoAbility.LegoAbility:GetPlayTimelineTask")
                    transfer.active[address] = {instance=instance,task=valid(task) and task:GetAddress() or false,rates={}}
                end
            end
        end
        return
    end
    self:select(target,function(instance,active)
        if active or instance:GetPropertyValue("bIsActive") then
            local snapshot = true
            if source and instance:IsA(self.backend:object("/Script/LegoAbility.LegoAbility")) then
                local task = self.backend:invoke(instance,"/Script/LegoAbility.LegoAbility:GetPlayTimelineTask")
                snapshot = {task=valid(task) and task:GetAddress() or false,rates={}}
            end
            transfer.active[instance:GetAddress()] = snapshot
        elseif source then self:arm_transfer(instance,transfer)
        elseif self.instances[instance:GetAddress()] then self:restore(instance:GetAddress()) end
    end,transfer.matches,transfer.selection)
end

function Costs:arm_transfer(instance,transfer)
    local address,source = instance:GetAddress(),transfer.source
    local previous = self.instances[address]
    local active, restartedTask = transfer.active[address]
    if active then
        -- Per-actor instances can end and restart inside one native request.
        -- The new playback task separates that execution from the active one.
        if type(active) ~= "table" then return previous end
        local task = self.backend:invoke(instance,"/Script/LegoAbility.LegoAbility:GetPlayTimelineTask")
        if not valid(task) or task:GetAddress() == active.task then return previous end
        restartedTask = task:GetAddress()
        if previous and previous.activation_task == restartedTask then return previous end
        transfer.active[address] = nil
    end
    if previous and (transfer.armed[address] or not restartedTask and previous == source) then return previous end
    if previous then
        if not previous.activated then transfer.previous[address]=previous end
        self:restore(address)
    end
    local state = self:arm(instance,source.bypass,{damage_multiplier=source.damage,
        startup_speed=source.startup,recovery_speed=source.recovery,
        super_armor=source.super_armor,invincible=source.invincible,
        restriction_bypasses=source.restriction_bypasses,restriction_target=source.restriction_target})
    state.phase_window = transfer.phase_window
    state.phase_windows = transfer.phase_windows
    state.activation_task = restartedTask
    if restartedTask then state.timeline_rates = active.rates end
    transfer.armed[address] = state
    return state
end

-- Native activation may create the release instance inside the call. Resolve
-- that instance before its first fee read, timeline or projectile initialization.
function Costs:inherit(instance)
    if not valid(instance) then return end
    local address = instance:GetAddress()
    local previous = self.instances[address]
    local transfer = self.transfers[#self.transfers]
    if not transfer or transfer.passive or not transfer.source or transfer.active[address]
        or previous and (previous.activated and instance:GetPropertyValue("bIsActive")
            or transfer.armed[address] or previous == transfer.source) then return previous end
    if not transfer.matches(instance) then return previous end
    return self:arm_transfer(instance,transfer)
end

function Costs:end_transfer(succeeded)
    if self.generation~=self.backend.generation then
        self:discard()
        return
    end
    local transfer = self.transfers[#self.transfers]
    if not transfer then return end
    Restrictions.finish(self.backend,transfer.restrictions)
    if transfer.passive then
        if not next(self.instances) then table.remove(self.transfers); return end
        transfer.passive = nil
        transfer.matches,transfer.selection = target_matcher(transfer.target)
    end
    if transfer.source then
        if succeeded then self:select(transfer.target,function(instance) self:arm_transfer(instance,transfer) end,transfer.matches,transfer.selection) end
        for address,armed in pairs(transfer.armed) do
            local state = self.instances[address]
            if state == armed then
                if succeeded and state.instance:GetPropertyValue("bIsActive") then state.activated=true
                else
                    self:restore(address)
                    local previous = transfer.previous[address]
                    self.instances[address] = previous
                    if previous then
                        self.context:ability_damage(address,previous.damage)
                        if previous.native_resources then self.context:ability_resources(self.backend,previous.instance,true) end
                        if previous.charge_tick then self.context:charge_rate(address,previous.startup,previous.charge_tick) end
                        self:protect(previous)
                        Restrictions.protect(self.backend,previous)
                    end
                end
            end
        end
    elseif succeeded then
        for address,active in pairs(transfer.active) do
            if valid(active.instance) then
                local task = self.backend:invoke(active.instance,"/Script/LegoAbility.LegoAbility:GetPlayTimelineTask")
                if valid(task) and task:GetAddress() ~= active.task then self:restore(address) end
            end
        end
    end
    table.remove(self.transfers)
end

function Costs:owner()
    if self.resolving or not next(self.instances) and #self.transfers == 0 then return end
    self.resolving = true
    local ok, state, fn = pcall(function()
        return self.context:owner(self.instances,function(object)
            if object:IsA(self.backend:object("/Script/GameplayAbilities.GameplayAbility")) then return self:inherit(object) or false end
            if object:IsA(self.backend:object("/Script/LegoAbility.LGAAction")) then
                local ability = self.backend:invoke(object,"/Script/LegoAbility.LGAAction:GetAbility")
                return self:inherit(ability) or false
            end
        end)
    end)
    self.resolving = false
    if not ok then error(state,0) end
    return state, fn
end

function Costs.tuned(settings)
    return settings and ((settings.damage_multiplier or 1) ~= 1 or (settings.startup_speed or 1) ~= 1
        or (settings.recovery_speed or 1) ~= 1 or settings.super_armor == true or settings.invincible == true
        or #(settings.restriction_bypasses or {}) > 0)
end

function Costs.with(backend,target,bypass,callback,settings)
    if (bypass or Costs.tuned(settings)) and not backend.resource_costs then backend.resource_costs = Costs.new(backend) end
    if backend.resource_costs then return backend.resource_costs:activate(target,bypass,callback,settings) end
    return callback()
end

function Costs.with_active(backend,instance,bypass,callback,settings,callbackOnly,phaseWindow,phaseWindows)
    if (bypass or Costs.tuned(settings)) and not backend.resource_costs then backend.resource_costs = Costs.new(backend) end
    local self = backend.resource_costs
    if not self then return callback() end
    local address = instance:GetAddress()
    local previous = self.instances[address]
    self:restore(address)
    local state = self:arm(instance,bypass,settings)
    state.activated, state.callback_only = true,callbackOnly
    state.phase_window, state.phase_windows = phaseWindow,phaseWindows
    local ok,result = pcall(callback)
    if callbackOnly or not ok or result == false or not instance:GetPropertyValue("bIsActive") then
        self:restore(address)
        self.instances[address] = previous
        if previous then
            self.context:ability_damage(address,previous.damage)
            if previous.native_resources then self.context:ability_resources(self.backend,previous.instance,true) end
            if previous.charge_tick then self.context:charge_rate(address,previous.startup,previous.charge_tick) end
            self:protect(previous)
            Restrictions.protect(self.backend,previous)
            previous.applied_rate = nil
        end
    end
    if not ok then error(result,0) end
    return result
end

function Costs:restore(address)
    local state = self.instances[address]
    if state and state.native_resources then self.context:ability_resources(self.backend,address,false) end
    if state and state.charge_tick then self.context:charge_rate(address,1) end
    if state and state.protection then
        for name,tag in pairs(state.protection) do
            if valid(state.protection_owner) then
                self.backend:invoke(self.backend:library("SGameUtils.AbilityUtils"),
                    "/Script/SGameUtils.AbilityUtils:RemoveLooseGameplayTagForSystem",state.protection_owner,tag,false)
            end
            state.protection[name] = nil
        end
    end
    if state and valid(state.task) and state.applied_rate then
        self:set_rate(state.task,state.base_rate)
        state.applied_rate = nil
    end
    self.context:ability_damage(address,1)
    self.instances[address] = nil
end

function Costs:protect(state)
    if not state.super_armor and not state.invincible then return end
    local ok,failure = pcall(function()
        for name,path in pairs(protection_tags) do
            if state[name] and not (state.protection and state.protection[name]) then
                if not valid(state.protection_owner) then
                    state.protection_owner = self.backend:receiver({receiver="ability_system"})
                end
                assert(valid(state.protection_owner),"Move protection requires the player's ability system")
                local tag = {TagName=FName(path)}
                self.backend:invoke(self.backend:library("SGameUtils.AbilityUtils"),
                    "/Script/SGameUtils.AbilityUtils:AddLooseGameplayTagForSystem",state.protection_owner,tag,false)
                state.protection = state.protection or {}
                state.protection[name] = tag
            end
        end
    end)
    if not ok then
        local cleaned,cleanup = pcall(self.reset,self)
        if not cleaned then self.backend.resource_costs = self
        elseif self.backend.resource_costs == self then self.backend.resource_costs = nil end
        error(tostring(failure)..(cleaned and "" or "\n"..tostring(cleanup)),0)
    end
end

function Costs:arm(instance,bypass,settings)
    self:enable(bypass,settings)
    local address = instance:GetAddress()
    local state = self.instances[address]
    if state then state.frame = self.frame; return state end
    state = {instance=instance, frame=self.frame, bypass=bypass, damage=settings and settings.damage_multiplier or 1,
        startup=settings and settings.startup_speed or 1,recovery=settings and settings.recovery_speed or 1,
        super_armor=settings and settings.super_armor,invincible=settings and settings.invincible,
        restriction_bypasses=settings and settings.restriction_bypasses,restriction_target=settings and settings.restriction_target}
    self.instances[address] = state
    if bypass and class_path(instance):find("/BaiShanFengLeiJian/",1,true) then
        state.native_resources = true
        state.resource_tags = {
            ["CharacterState.AbilityState.BaiShanFengLeiJian.Wind"]=1,
            ["CharacterState.AbilityState.BaiShanFengLeiJian.Thunder"]=1
        }
        local tick=charge_ticks[class_path(instance)]
        if tick then
            -- The held-state entry rejects an already charged sword. Its Tick
            -- checks the same tag to enter the successful release timeline.
            local entry=class_path(instance)..":Added_1CC942FE4424EBA0DBE0A292363B2DFA"
            state.resource_tags["CharacterState.AbilityState.BaiShanFengLeiJian.Thunder"]=function()
                local depth,preparing=1,false
                while true do
                    local _,owner,fn=self.context.at(depth)
                    if not owner then return preparing and 0 or nil end
                    if owner==instance:GetAddress() then
                        if fn=="Function "..tick then return 1 end
                        if fn=="Function "..entry then preparing=true end
                    end
                    depth=depth+1
                end
            end
        end
        self:enable(false,{resource_tags=state.resource_tags})
        self.context:ability_resources(self.backend,instance,true)
    end
    if state.startup ~= 1 then
        local path = class_path(instance)
        state.startup_window = startup_windows[path]
        if charge_ticks[path] then state.charge_tick = self.backend:object(charge_ticks[path]) end
    end
    self.context:ability_damage(instance,state.damage)
    if state.charge_tick then self.context:charge_rate(instance,state.startup,state.charge_tick) end
    self:protect(state)
    Restrictions.protect(self.backend,state)
    return state
end

function Costs:select(target, callback, matches, selection)
    Selection.each(self.backend,target,function(spec,ability)
        local cdo,active = ability:GetAddress(),spec.ActiveCount>0
        for _,instances in ipairs({spec.NonReplicatedInstances,spec.ReplicatedInstances}) do
            for index=1,instances:GetArrayNum() do
                local instance=instances[index]
                if valid(instance) and instance:GetAddress()~=cdo then callback(instance,active) end
            end
        end
    end,matches,selection,self.context and self.context.ability_specs)
end

function Costs:activate(target, bypass, callback,settings)
    self:enable(bypass,settings)
    settings = settings or {}
    self:begin_transfer({bypass=bypass,damage=settings.damage_multiplier or 1,
        startup=settings.startup_speed or 1,recovery=settings.recovery_speed or 1,
        super_armor=settings.super_armor,invincible=settings.invincible,
        restriction_bypasses=settings.restriction_bypasses,restriction_target=target},target)
    local transfer = self.transfers[#self.transfers]
    transfer.phase_window = target.phase_window
    transfer.phase_windows = target.phase_windows
    for _,state in pairs(transfer.armed) do
        state.phase_window, state.phase_windows = target.phase_window,target.phase_windows
    end
    self.activating = (self.activating or 0)+1
    local ok, result = pcall(Restrictions.with,self.backend,target,settings,callback)
    self.activating = self.activating-1
    if self.activating == 0 then self.activating = nil end
    self:end_transfer(ok and result ~= false)
    if not ok then error(result,0) end
    return result
end

function Costs:input(target,bypass,settings,rank)
    local action = target.object or self.backend:object(target.path,"InputAction")
    local previous = self.inputs[action:GetAddress()]
    if previous and previous.rank <= (rank or math.huge) then return end
    self:enable(bypass,settings)
    settings = settings or {}
    self.inputs[action:GetAddress()] = {bypass=bypass,damage=settings.damage_multiplier or 1,
        startup=settings.startup_speed or 1,recovery=settings.recovery_speed or 1,rank=rank or math.huge,
        super_armor=settings.super_armor,invincible=settings.invincible,
        restriction_bypasses=settings.restriction_bypasses,restriction_target=target}
    if self.backend.native_input then self.backend.native_input:prepare_tuning(self) end
end

function Costs:update()
    if self.generation ~= self.backend.generation then
        local backend = self.backend
        self:discard()
        if backend.resource_costs == self then backend.resource_costs = nil end
        return
    end
    if self.context.closed then
        local failure = self.context.failure or "Binding tuning stopped after its native context was disabled"
        local cleaned,cleanup = pcall(self.reset,self)
        error(failure..(cleaned and "" or "\n"..tostring(cleanup)),0)
    end
    self.frame = self.frame+1
    self.inputs = {}
    self.context:update_damage()
    for address, state in pairs(self.instances) do
        if not valid(state.instance) or not state.instance:GetPropertyValue("bIsActive")
            and (state.activated or state.frame < self.frame-1) then self:restore(address)
        elseif state.instance:GetPropertyValue("bIsActive") then
            state.activated = true
            if state.startup ~= 1 or state.recovery ~= 1 then self:update_speed(state) end
        end
    end
end

function Costs:update_speed(state)
    if state.speed_supported == nil then
        state.speed_supported = state.instance:IsA(self.backend:object("/Script/LegoAbility.LegoAbility"))
    end
    if not state.speed_supported then return end
    local task = self.backend:invoke(state.instance,"/Script/LegoAbility.LegoAbility:GetPlayTimelineTask")
    if not valid(task) then return end
    local timeline = task:GetPropertyValue("Timeline")
    if not valid(state.task) or state.task:GetAddress() ~= task:GetAddress() then
        if valid(state.task) and state.applied_rate then
            self:set_rate(state.task,state.base_rate)
        end
        local rate = valid(timeline) and state.timeline_rates and state.timeline_rates[timeline:GetAddress()]
        -- LegoAbility starts bAutoPlay timelines directly in C++ at rate 1;
        -- that activation does not pass through the reflected playback hooks.
        if not rate and valid(timeline) and timeline:GetPropertyValue("bAutoPlay") then rate = 1 end
        if not rate then return end
        state.task, state.base_rate, state.applied_rate = task,rate,nil
    end
    local phase = state.instance:GetPropertyValue("Action Phase")
    local name = phase and phase.TagName:ToString()
    local multiplier = name == "CharacterState.Action.Phase.Start" and state.startup
        or name == "CharacterState.Action.Phase.Cancel" and state.recovery or 1
    for _, window in ipairs(state.phase_windows or {state.phase_window or state.startup_window}) do
        if window and valid(timeline) then
            local expected = self.backend:invoke(state.instance,"/Script/LegoAbility.LegoAbility:GetTimelineByName",FName(window.timeline))
            if valid(expected) and timeline:GetAddress() == expected:GetAddress() then
                local time = task:GetPropertyValue("CurrentTime")
                if time >= window.entry_time then
                    if not window.attack_start or time < window.attack_start then multiplier = state.startup
                    elseif window.attack_end then multiplier = time >= window.attack_end and state.recovery or 1 end
                end
                break
            end
        end
    end
    local rate = state.base_rate*multiplier
    if state.applied_rate ~= rate then
        self:set_rate(task,rate)
        state.applied_rate = rate
    end
end

function Costs:set_rate(task,rate)
    self.setting_rate = true
    local ok,failure = pcall(self.backend.invoke,self.backend,task,"/Script/LegoAbility.LGATask_PlayTimeline:SetPlayRate",rate)
    self.setting_rate = false
    if not ok then error(failure,0) end
end

function Costs:suspend()
    self.inputs = {}
end

function Costs:discard()
    self.closed = true
    self.inputs, self.transfers, self.instances = {}, {}, {}
    if self.context and self.context.discard then pcall(self.context.discard,self.context) end
    self.context = nil
    for path,ids in pairs(self.hooks) do
        pcall(UnregisterHook,path,ids[1],ids[2])
    end
    self.hooks = {}
    if self.backend.resource_costs==self then self.backend.resource_costs=nil end
end

function Costs:reset()
    if self.generation~=self.backend.generation then
        self:discard()
        return
    end
    self.closed = true
    local failures = {}
    local function attempt(callback)
        local ok,failure = pcall(callback)
        if not ok then failures[#failures+1] = tostring(failure) end
    end
    for index=#self.transfers,1,-1 do attempt(function() Restrictions.finish(self.backend,self.transfers[index].restrictions) end) end
    for address in pairs(self.instances) do attempt(function() self:restore(address) end) end
    self.inputs, self.transfers = {}, {}
    if self.context then attempt(function() self.context:reset() end) end
    for path,ids in pairs(self.hooks) do
        attempt(function()
            UnregisterHook(path,ids[1],ids[2])
            self.hooks[path] = nil
        end)
    end
    if #failures > 0 then error(table.concat(failures,"\n"),0) end
end

return Costs
