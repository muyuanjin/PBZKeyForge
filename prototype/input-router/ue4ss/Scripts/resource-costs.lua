local Context = require("script-context")
local Costs = {}
Costs.__index = Costs

local resources = {UltimateChargeCount=true, Shachi=true, SpiritShadow=true}
local function valid(object) return object and object:IsValid() end
local function class_path(object) return object:GetClass():GetFullName():gsub("^%S+ ", "") end
local function is_resource(attribute)
    local name = attribute:get().AttributeName
    return resources[type(name) == "string" and name or name:ToString()]
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
local function fee_read(fn)
    return fn:match(":ExecuteUbergraph_BP_CombatArt_Base$")
        or fn:match(":Apply Shachi Cost And Endure$")
        or fn:match(":Bullet Enough$") or fn:match(":Shachi Enough$")
        or fn:match(":Comsume$") or fn:match(":IsShaqiEnough$")
        or fn:match(":HasEnoughBullet$")
        or fn:match(":ExecuteUbergraph_GA_Hun_FengLeiJian_B_Hold$")
end

function Costs.new(backend)
    local self = setmetatable({backend=backend, instances={}, bullets={}, damage_sources={}, transfers={}, hooks={}, frame=0, context=Context.new()},Costs)
    local function hook(path,pre,post)
        self.hooks[path] = {RegisterHook(path,pre,post)}
    end
    local function transfer(path,receiver,selector)
        hook(path,function(component,argument)
            local owned = backend:receiver({receiver=receiver})
            local source = valid(owned) and component:get():GetAddress() == owned:GetAddress() and self:owner()
            self:begin_transfer(source and not source.callback_only and source,selector(argument:get()))
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
    hook("/Script/SGameAttributes.AttributeUtils:ModifyCharacterAttributeValue",function(_,actor,attribute,operation,value)
        if not is_resource(attribute) or operation:get() ~= 0 or value:get() >= 0 then return end
        if not valid(self.backend.pawn) or actor:get():GetAddress() ~= self.backend.pawn:GetAddress() then return end
        local state = self:owner()
        if state and state.bypass then value:set(0) end
    end)
    local function query(component,attribute)
        if not is_resource(attribute) then return end
        local owned = self.backend:receiver({receiver="ability_system"})
        if not valid(owned) or component:get():GetAddress() ~= owned:GetAddress() then return end
        local state, fn = self:owner()
        if state and state.bypass and fn and fee_read(fn) then return 1000000000 end
    end
    hook("/Script/GameplayAbilities.AbilitySystemComponent:GetGameplayAttributeValue",function() end,
        function(component,original,attribute) return query(component,attribute) end)
    hook("/Script/GameplayAbilities.AbilitySystemBlueprintLibrary:GetFloatAttributeFromAbilitySystemComponent",function() end,
        function(_,original,component,attribute) return query(component,attribute) end)
    hook("/Script/SGameCombatSystem.BulletInterface:Initialize",function() end,
        function(context,parent,avatar,ability)
            local bullet, source = context:get(), ability:get()
            local state = self:inherit(source)
            local inherited = valid(parent:get()) and self.bullets[parent:get():GetAddress()]
            self:remember_bullet(bullet,inherited and inherited.damage or state and state.damage or 1)
        end)
    hook("/Script/SGameCombatSystem.BulletInterface:Start",function(context)
        local bullet = context:get()
        local ability = backend:invoke(bullet,"/Script/SGameCombatSystem.BulletInterface:GetSourceAbility")
        local state = self:inherit(ability)
        if state and state.callback_only then self:remember_bullet(bullet,state.damage) end
    end)
    -- CalculateDamage receives a GameplayTag. The surrounding damage entry
    -- owns the interface object, including calls that enter TakeDamage directly
    -- from native BulletAttackModule code without its reflected thunk.
    local function begin_damage(source)
        self.damage_sources[#self.damage_sources+1] = self:damage_settings(source) or false
    end
    local function end_damage() table.remove(self.damage_sources) end
    for _, name in ipairs({"DealDamageToAvatar","DealDamageCustom"}) do
        hook("/Script/SGameCombatSystem.BulletAttackModule:" .. name,
            function(context) begin_damage(context:get()) end,end_damage)
    end
    hook("/Script/SGameAttributes.AttributeComponent:TakeDamage",
        function(_,source) begin_damage(source:get()) end,end_damage)
    hook("/Script/SGameAttributes.DamageCalculatorBase:CalculateDamage",function() end,
        function(_,attributes,attacker,target,tags,attackData,sourceTag,damageData)
            local owned = backend:receiver({receiver="ability_system"})
            if not valid(owned) or not valid(attacker:get()) or attacker:get():GetAddress() ~= owned:GetAddress() then return end
            local settings = self.damage_sources[#self.damage_sources]
            if not settings or settings.damage == 1 then return end
            damageData:get():ForEach(function(key,value)
                local tag = key:get().TagName:ToString()
                if tag == "Damage.Damage.Target.GeneralDamage" or tag:sub(1,35) == "Damage.Damage.Target.GeneralDamage." then
                    value:set(value:get()*settings.damage)
                end
            end)
        end)
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
    return self
end

function Costs:remember_bullet(bullet,multiplier)
    self.bullets[bullet:GetAddress()] = {instance=bullet,damage=multiplier}
    local actor = self.backend:invoke(bullet,"/Script/SGameCombatSystem.BulletInterface:GetActor")
    if valid(actor) then self.bullets[actor:GetAddress()] = {instance=actor,damage=multiplier} end
end

function Costs:damage_settings(source)
    if not valid(source) then return end
    local actor = self.backend:invoke(source,"/Script/SGameAttributes.DamageSourceInterface:GetDamageSourceActor")
    local snapshot = valid(actor) and self.bullets[actor:GetAddress()]
    if snapshot then return snapshot end
    local ability = self.backend:invoke(source,"/Script/SGameAttributes.DamageSourceInterface:GetDamageSourceAbility")
    return self:inherit(ability)
end

function Costs:begin_transfer(source,target)
    local transfer = {source=source,target=target,armed={},active={},previous={}}
    self.transfers[#self.transfers+1] = transfer
    if not source then return end
    self:select(target,function(instance,active)
        if active or instance:GetPropertyValue("bIsActive") then transfer.active[instance:GetAddress()]=true
        else self:arm_transfer(instance,transfer) end
    end)
end

function Costs:arm_transfer(instance,transfer)
    local address,source = instance:GetAddress(),transfer.source
    local previous = self.instances[address]
    if transfer.active[address] or previous and (previous.activated or transfer.armed[address] or previous == source) then return previous end
    if previous then transfer.previous[address]=previous;self:restore(address) end
    local state = self:arm(instance,source.bypass,{damage_multiplier=source.damage,
        startup_speed=source.startup,recovery_speed=source.recovery})
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
        or previous and (previous.activated or transfer.armed[address] or previous == transfer.source) then return previous end
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
                    self.instances[address] = transfer.previous[address]
                end
            end
        end
    end
    table.remove(self.transfers)
end

function Costs:owner()
    if self.resolving or not next(self.instances) then return end
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

local function tuned(settings)
    return settings and ((settings.damage_multiplier or 1) ~= 1 or (settings.startup_speed or 1) ~= 1 or (settings.recovery_speed or 1) ~= 1)
end

function Costs.with(backend,target,bypass,callback,settings)
    if (bypass or tuned(settings)) and not backend.resource_costs then backend.resource_costs = Costs.new(backend) end
    if backend.resource_costs then return backend.resource_costs:activate(target,bypass,callback,settings) end
    return callback()
end

function Costs.with_active(backend,instance,bypass,callback,settings,callbackOnly)
    if (bypass or tuned(settings)) and not backend.resource_costs then backend.resource_costs = Costs.new(backend) end
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
        if previous then previous.applied_rate = nil end
    end
    if not ok then error(result,0) end
    return result
end

function Costs:restore(address)
    local state = self.instances[address]
    if state and valid(state.task) and state.applied_rate then
        self:set_rate(state.task,state.base_rate)
    end
    self.instances[address] = nil
end

function Costs:arm(instance,bypass,settings)
    local address = instance:GetAddress()
    local state = self.instances[address]
    if state then state.frame = self.frame; return state end
    state = {instance=instance, frame=self.frame, bypass=bypass, damage=settings and settings.damage_multiplier or 1,
        startup=settings and settings.startup_speed or 1,recovery=settings and settings.recovery_speed or 1}
    self.instances[address] = state
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
    local armed = {}
    self:select(target,function(instance,active)
        if not active and not instance:GetPropertyValue("bIsActive") then
            local address = instance:GetAddress()
            local state = self.instances[address]
            if state and state.input_frame == self.frame then armed[address] = true
            else
                self:restore(address)
                if bypass or tuned(settings) then self:arm(instance,bypass,settings); armed[address] = true end
            end
        end
    end)
    local ok, result = pcall(callback)
    for address in pairs(armed) do
        local state = self.instances[address]
        if not ok or result == false or not state.instance:GetPropertyValue("bIsActive") then self:restore(address)
        else state.activated = true end
    end
    if not ok then error(result,0) end
    return result
end

function Costs:input(target,bypass,settings,rank)
    local action = target.object or self.backend:object(target.path,"InputAction")
    local path = action:GetFullName():gsub("^%S+ ", ""):gsub("%.[^./]+$", "")
    local tags = (self.backend.config.resource_inputs or {})[path]
    if not tags then return end
    self:prepare({resource_tags=tags},bypass,settings,rank)
end

function Costs:prepare(target,bypass,settings,rank)
    self:select(target,function(instance,active)
        if not active and not instance:GetPropertyValue("bIsActive") then
            local address = instance:GetAddress()
            local state = self.instances[address]
            if not state or state.input_frame ~= self.frame then
                self:restore(address)
                state = self:arm(instance,bypass,settings)
                state.input_frame, state.input_rank = self.frame,rank or math.huge
                state.input_tuned = tuned(settings)
            else
                state.bypass = state.bypass or bypass
                if tuned(settings) and (not state.input_tuned or (rank or math.huge) < state.input_rank) then
                    state.damage,state.startup,state.recovery = settings.damage_multiplier or 1,settings.startup_speed or 1,settings.recovery_speed or 1
                    state.input_tuned,state.input_rank = true,rank or math.huge
                end
            end
        end
    end)
end

function Costs:update()
    self.frame = self.frame+1
    for address, state in pairs(self.instances) do
        if not valid(state.instance) or not state.instance:GetPropertyValue("bIsActive")
            and (state.activated or state.frame < self.frame-1) then self:restore(address)
        elseif state.instance:GetPropertyValue("bIsActive") then
            state.activated = true
            if state.startup ~= 1 or state.recovery ~= 1 then self:update_speed(state) end
        end
    end
    for address,bullet in pairs(self.bullets) do if not valid(bullet.instance) then self.bullets[address]=nil end end
end

function Costs:update_speed(state)
    if state.speed_supported == nil then
        state.speed_supported = state.instance:IsA(self.backend:object("/Script/LegoAbility.LegoAbility"))
    end
    if not state.speed_supported then return end
    local task = self.backend:invoke(state.instance,"/Script/LegoAbility.LegoAbility:GetPlayTimelineTask")
    if not valid(task) then return end
    if not valid(state.task) or state.task:GetAddress() ~= task:GetAddress() then
        if valid(state.task) and state.applied_rate then
            self:set_rate(state.task,state.base_rate)
        end
        local timeline = task:GetPropertyValue("Timeline")
        local rate = valid(timeline) and state.timeline_rates and state.timeline_rates[timeline:GetAddress()]
        if not rate then return end
        state.task, state.base_rate, state.applied_rate = task,rate,nil
    end
    local phase = state.instance:GetPropertyValue("Action Phase")
    local name = phase and phase.TagName:ToString()
    local multiplier = name == "CharacterState.Action.Phase.Start" and state.startup
        or name == "CharacterState.Action.Phase.Cancel" and state.recovery or 1
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

function Costs:reset()
    for address in pairs(self.instances) do self:restore(address) end
    self.bullets = {}
    self.damage_sources = {}
    self.transfers = {}
    for path, ids in pairs(self.hooks) do UnregisterHook(path,ids[1],ids[2]) end
    self.hooks = {}
    self.context:reset()
end

return Costs
