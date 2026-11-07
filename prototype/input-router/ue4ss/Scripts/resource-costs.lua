local Context = require("script-context")
local Costs = {}
Costs.__index = Costs

local resources = {UltimateChargeCount=true, Shachi=true, SpiritShadow=true}
-- These instant effects contain only a resource deduction. Other effects keep
-- their original class, including resource gains and resource-based attacks.
local resource_effects = {
    ["/PBZCombatAsset/ActorData/Players/Hun/Weapon/SlitheringSword/UpgradeData/GE_SlitheringSword_EvalutionCombo_CostShachi.GE_SlitheringSword_EvalutionCombo_CostShachi_C"]=true,
    ["/PBZCombatAsset/ActorData/Players/Hun/Weapon/ChaosBlade/GE_Chaos_SwitchShaQiCost.GE_Chaos_SwitchShaQiCost_C"]=true
}
local function valid(object) return object and object:IsValid() end
local function class_path(object) return object:GetClass():GetFullName():gsub("^%S+ ", "") end
local function attribute_name(attribute)
    local name = attribute:get().AttributeName
    return type(name) == "string" and name or name:ToString()
end

local function tags_match(tags, wanted)
    if not tags then return false end
    local found = false
    tags.GameplayTags:ForEach(function(_, item)
        local tag = item:get().TagName:ToString()
        for _, target in ipairs(wanted) do
            if tag == target or tag:sub(1,#target+1) == target .. "." then found = true end
        end
    end)
    return found
end

local function target_matches(ability,target)
    if target.ability_class then return class_path(ability) == target.ability_class end
    local tags = ability:GetPropertyValue("AbilityTags")
    if target.activation_tags then
        for _,tag in ipairs(target.activation_tags) do if not tags_match(tags,{tag}) then return false end end
        return true
    end
    return target.resource_tags and tags_match(tags,target.resource_tags)
        or target.tag and tags_match(tags,{target.tag})
end

-- These reads decide whether a fee can be paid. Keeping the original fee
-- preserves reinforcement, charge accumulation and Actual Shachi Consumed.
local fee_sites = {
    ["Function /PBZCombatAsset/ActorData/Players/Hun/Abilities/ShachiAbility/Lute/GA_Hun_Lute_SKill.GA_Hun_Lute_SKill_C:ExecuteUbergraph_GA_Hun_Lute_SKill"]=true,
    ["Function /PBZCombatAsset/ActorData/Players/Hun/Abilities/ShachiAbility/BrutalAxe/GA_BrutalAxe_Normal.GA_BrutalAxe_Normal_C:ExecuteUbergraph_GA_BrutalAxe_Normal"]=true,
    ["Function /PBZCombatAsset/Prototypes/Actions/GAA_ShachiAbilityShachi.GAA_ShachiAbilityShachi_C:Received_ActionTick"]=true,
    ["Function /PBZCombatAsset/Prototypes/Abilities/Base/BP_HeavyAttack_Air_Base.BP_HeavyAttack_Air_Base_C:Play Heavy Attack Timeline"]=true,
    ["Function /MagCombatAbilities/Abilities/BP_GlobalActionAbility_Base.BP_GlobalActionAbility_Base_C:K2_CanActivateAbility"]=true,
    ["Function /PBZCombatAsset/ActorData/Players/Hun/Abilities/SlitheringSword/Attack/GA_Hun_SlitheringSword_EvalutionCombo_New.GA_Hun_SlitheringSword_EvalutionCombo_New_C:K2_CanActivateAbility"]=true,
    ["Function /PBZCombatAsset/ActorData/Players/Hun/Abilities/ZhanMaBlade/GA_Hun_ZhanMa_C.GA_Hun_ZhanMa_C_C:ExecuteUbergraph_GA_Hun_ZhanMa_C"]=true
}
local function fee_read(fn)
    return fee_sites[fn] or fn:match(":ExecuteUbergraph_BP_CombatArt_Base$")
        or fn:match(":Apply Shachi Cost And Endure$")
        or fn:match(":Bullet Enough$") or fn:match(":Shachi Enough$")
        or fn:match(":Comsume$") or fn:match(":IsShaqiEnough$")
        or fn:match(":HasEnoughBullet$")
        or fn:match(":ExecuteUbergraph_GA_Hun_FengLeiJian_B_Hold$")
end

function Costs.new(backend)
    local self = setmetatable({backend=backend, instances={}, inputs={}, transfers={}, hooks={}, frame=0},Costs)
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
            local owned = backend:receiver({receiver=receiver})
            local is_owned = valid(owned) and component:get():GetAddress() == owned:GetAddress()
            local source = is_owned and self:owner()
            local activation = self.activating and self.transfers[#self.transfers]
            local initial = not source and activation
            if not source and is_owned and activation then source = activation.source end
            if not source and is_owned and receiver == "ability_input" and not self.activating and input and valid(input:get()) then
                source = self.inputs[input:get():GetAddress()]
            end
            self:begin_transfer(source and not source.callback_only and source,selector(argument:get()))
            if initial then
                local nested = self.transfers[#self.transfers]
                nested.phase_window = activation.phase_window
                for _,state in pairs(nested.armed) do state.phase_window = nested.phase_window end
            end
        end,function(_,result) self:end_transfer(result:get()) end)
    end
    local function by_class(value)
        return valid(value) and {ability_class=value:GetFullName():gsub("^%S+ ", "")} or {}
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
        value.GameplayTags:ForEach(function(_,item) tags[#tags+1]=item:get().TagName:ToString() end)
        return {activation_tags=tags}
    end)
end

function Costs:install_resource_hooks()
    local function hook(...) return self:hook(...) end
    hook("/Script/GameplayAbilities.GameplayAbility:BP_ApplyGameplayEffectToOwner",function(ability,effectClass)
        local effect = effectClass:get()
        if not valid(effect) or not resource_effects[effect:GetFullName():gsub("^%S+ ","")] then return end
        local instance = ability:get()
        if not valid(instance) then return end
        local address = instance:GetAddress()
        local state = self.instances[address] or self:owner()
        if state and state.bypass and state.instance:GetAddress() == address then
            effectClass:set(self.context:empty_class())
        end
    end)
    hook("/Script/SGameAttributes.AttributeUtils:ModifyCharacterAttributeValue",function(_,actor,attribute,operation,value)
        local name = attribute_name(attribute)
        if not resources[name] or operation:get() ~= 0 or value:get() >= 0 then return end
        local is_owned = valid(self.backend.pawn) and actor:get():GetAddress() == self.backend.pawn:GetAddress()
        local state,fn
        if is_owned then state,fn = self:owner()
        else state,fn = self.context:owner(self.instances,function() return false end) end
        self:observe_cost("write",actor:get(),nil,state,fn,
            not is_owned and "actor-mismatch" or not state and "owner-missing" or not state.bypass and "owner-pays" or "applied",
            nil,name,value:get(),operation:get())
        if not is_owned then return end
        if state and state.bypass then value:set(0) end
    end)
    local function query(component,attribute,original)
        local name = attribute_name(attribute)
        if not resources[name] then return end
        local owned = self.backend:receiver({receiver="ability_system"})
        local is_owned = valid(owned) and component:get():GetAddress() == owned:GetAddress()
        local state,fn
        if is_owned then state,fn = self:owner()
        else state,fn = self.context:owner(self.instances,function() return false end) end
        self:observe_cost("read",nil,component:get(),state,fn,
            not is_owned and "asc-mismatch" or not state and "owner-missing" or not state.bypass and "owner-pays"
                or (not fn or not fee_read(fn)) and "fee-site-mismatch" or "applied",owned,name,original:get())
        if not is_owned then return end
        if state and state.bypass and fn and fee_read(fn) then original:set(1000000000) end
    end
    hook("/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayAttributeValue",function() end,
        function(component,original,attribute) return query(component,attribute,original) end)
    hook("/Script/GameplayAbilities.AbilitySystemBlueprintLibrary:GetFloatAttributeFromAbilitySystemComponent",function() end,
        function(_,original,component,attribute) return query(component,attribute,original) end)
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
    local function timeline_rate(ability,timeline,rate)
        local state = self:inherit(ability)
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
        if not self:inherit(object) then return end
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
    if not source and not (state and state.bypass and fn and fee_read(fn)) then return end
    local contextName,contextAddress,current = self.context.at(1)
    fn = fn or current
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
    self:select(target,function(instance,active)
        if active or instance:GetPropertyValue("bIsActive") then transfer.active[instance:GetAddress()]=true
        elseif source then self:arm_transfer(instance,transfer)
        else self:restore(instance:GetAddress()) end
    end)
end

function Costs:arm_transfer(instance,transfer)
    local address,source = instance:GetAddress(),transfer.source
    local previous = self.instances[address]
    if transfer.active[address] or previous and (transfer.armed[address] or previous == source) then return previous end
    if previous then
        if not previous.activated then transfer.previous[address]=previous end
        self:restore(address)
    end
    local state = self:arm(instance,source.bypass,{damage_multiplier=source.damage,
        startup_speed=source.startup,recovery_speed=source.recovery})
    state.phase_window = transfer.phase_window
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
    if not transfer or not transfer.source or transfer.active[address]
        or previous and (previous.activated and instance:GetPropertyValue("bIsActive")
            or transfer.armed[address] or previous == transfer.source) then return previous end
    if not target_matches(instance,transfer.target) then return previous end
    return self:arm_transfer(instance,transfer)
end

function Costs:end_transfer(succeeded)
    local transfer = self.transfers[#self.transfers]
    if transfer.source then
        if succeeded then self:select(transfer.target,function(instance) self:arm_transfer(instance,transfer) end) end
        for address,armed in pairs(transfer.armed) do
            local state = self.instances[address]
            if state == armed then
                if succeeded and state.instance:GetPropertyValue("bIsActive") then state.activated=true
                else
                    self:restore(address)
                    local previous = transfer.previous[address]
                    self.instances[address] = previous
                    if previous then self.context:ability_damage(address,previous.damage) end
                end
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
    return settings and ((settings.damage_multiplier or 1) ~= 1 or (settings.startup_speed or 1) ~= 1 or (settings.recovery_speed or 1) ~= 1)
end

function Costs.with(backend,target,bypass,callback,settings)
    if (bypass or Costs.tuned(settings)) and not backend.resource_costs then backend.resource_costs = Costs.new(backend) end
    if backend.resource_costs then return backend.resource_costs:activate(target,bypass,callback,settings) end
    return callback()
end

function Costs.with_active(backend,instance,bypass,callback,settings,callbackOnly)
    if (bypass or Costs.tuned(settings)) and not backend.resource_costs then backend.resource_costs = Costs.new(backend) end
    local self = backend.resource_costs
    if not self then return callback() end
    local address = instance:GetAddress()
    local previous = self.instances[address]
    self:restore(address)
    local state = self:arm(instance,bypass,settings)
    state.activated, state.callback_only = true,callbackOnly
    local ok,result = pcall(callback)
    if callbackOnly or not ok or result == false or not instance:GetPropertyValue("bIsActive") then
        self:restore(address)
        self.instances[address] = previous
        if previous then
            self.context:ability_damage(address,previous.damage)
            previous.applied_rate = nil
        end
    end
    if not ok then error(result,0) end
    return result
end

function Costs:restore(address)
    local state = self.instances[address]
    if state and valid(state.task) and state.applied_rate then
        self:set_rate(state.task,state.base_rate)
        state.applied_rate = nil
    end
    self.context:ability_damage(address,1)
    self.instances[address] = nil
end

function Costs:arm(instance,bypass,settings)
    self:enable(bypass,settings)
    local address = instance:GetAddress()
    local state = self.instances[address]
    if state then state.frame = self.frame; return state end
    state = {instance=instance, frame=self.frame, bypass=bypass, damage=settings and settings.damage_multiplier or 1,
        startup=settings and settings.startup_speed or 1,recovery=settings and settings.recovery_speed or 1}
    self.instances[address] = state
    self.context:ability_damage(instance,state.damage)
    return state
end

function Costs:select(target, callback)
    local component = self.backend:receiver({receiver="ability_system"})
    if not valid(component) then return end
    component.ActivatableAbilities.Items:ForEach(function(_, parameter)
        local spec = parameter:get()
        if not valid(spec.Ability) then return end
        if not target_matches(spec.Ability,target) then return end
        for _, instances in ipairs({spec.NonReplicatedInstances,spec.ReplicatedInstances}) do
            instances:ForEach(function(_, item)
                local instance = item:get()
                if valid(instance) and instance:GetAddress() ~= spec.Ability:GetAddress() then callback(instance,spec.ActiveCount > 0) end
            end)
        end
    end)
end

function Costs:activate(target, bypass, callback,settings)
    self:enable(bypass,settings)
    settings = settings or {}
    self:begin_transfer({bypass=bypass,damage=settings.damage_multiplier or 1,
        startup=settings.startup_speed or 1,recovery=settings.recovery_speed or 1},target)
    local transfer = self.transfers[#self.transfers]
    transfer.phase_window = target.phase_window
    for _,state in pairs(transfer.armed) do state.phase_window = target.phase_window end
    self.activating = (self.activating or 0)+1
    local ok, result = pcall(callback)
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
        startup=settings.startup_speed or 1,recovery=settings.recovery_speed or 1,rank=rank or math.huge}
end

function Costs:update()
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
        if not rate then return end
        state.task, state.base_rate, state.applied_rate = task,rate,nil
    end
    local phase = state.instance:GetPropertyValue("Action Phase")
    local name = phase and phase.TagName:ToString()
    local multiplier = name == "CharacterState.Action.Phase.Start" and state.startup
        or name == "CharacterState.Action.Phase.Cancel" and state.recovery or 1
    local window = state.phase_window
    if window and valid(timeline) then
        local expected = self.backend:invoke(state.instance,"/Script/LegoAbility.LegoAbility:GetTimelineByName",FName(window.timeline))
        if valid(expected) and timeline:GetAddress() == expected:GetAddress() then
            local time = task:GetPropertyValue("CurrentTime")
            if time >= window.entry_time then
                multiplier = time < window.attack_start and state.startup
                    or time >= window.attack_end and state.recovery or 1
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

function Costs:reset()
    self.closed = true
    local failures = {}
    local function attempt(callback)
        local ok,failure = pcall(callback)
        if not ok then failures[#failures+1] = tostring(failure) end
    end
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
