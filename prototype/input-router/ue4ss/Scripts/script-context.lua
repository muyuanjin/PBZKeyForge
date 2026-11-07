local Context = {}
Context.__index = Context
local activeContext
local function native_ok(result,message) assert(result == 1,message) end

local function new_damage_object(method,object)
    local self = activeContext
    if not self or not self.damage_enabled then return end
    local ok,failure = pcall(self[method],self,object)
    if not ok then
        self.failure = method..": "..tostring(failure)
        local cleaned,cleanup = pcall(self.reset,self)
        local message = self.failure..(cleaned and "" or "\n"..tostring(cleanup))
        self.failure = message
        if self.report then self.report("Damage hooks disabled: "..message) end
        error(message,0)
    end
end

-- Notifications share the Mod's lifetime. Their callbacks access objects only
-- while damage scaling is enabled.
NotifyOnNewObject("/Script/SGameAttributes.DamageCalculatorBase",function(object)
    new_damage_object("watch_damage_calculator",object)
end)
NotifyOnNewObject("/Script/SGameAttributes.DamagePreCalculationPayload",function(object)
    new_damage_object("watch_damage_payload",object)
end)
NotifyOnNewObject("/Script/SGameCombatSystem.BulletActorBase",function(object)
    new_damage_object("watch_damage_bullet",object)
end)
NotifyOnNewObject("/Script/SGameCombatSystem.BulletObjectBase",function(object)
    new_damage_object("watch_damage_bullet",object)
end)

function Context.new(report)
    local scripts = debug.getinfo(1,"S").source:sub(2):match("^(.*[/\\])")
    local path = scripts:gsub("Scripts[/\\]$", "") .. "lib/pbzability-context.dll"
    local enable, failure = package.loadlib(path,"pbz_script_context_enable")
    assert(enable, "Ability context unavailable: " .. tostring(failure))
    local at = assert(package.loadlib(path,"pbz_script_context_at"))
    local watchInterface = assert(package.loadlib(path,"pbz_script_context_watch_interface"))
    local interfaceObject = assert(package.loadlib(path,"pbz_script_context_interface_object"))
    local self = setmetatable({enable=enable,at=at,watch_interface=watchInterface,interface_name=interfaceObject,
        damage_modules={},damage_payloads={},damage_bullets={},path=path,report=report,observation_frame=0},Context)
    enable(true)
    activeContext = self
    return self
end

function Context:enable_damage()
    if self.damage_enabled then return end
    assert(not self.closed,"Ability context is closed")
    local ok,failure = pcall(function()
        local function load(name) return assert(package.loadlib(self.path,name)) end
        self.watch_module = load("pbz_damage_watch_module")
        self.watch_calculator = load("pbz_damage_watch_calculator")
        self.watch_payload = load("pbz_damage_watch_payload")
        self.forget_payload = load("pbz_damage_forget_payload")
        self.watch_bullet = load("pbz_damage_watch_bullet")
        self.prepare_bullet = load("pbz_damage_prepare_bullet")
        self.forget_bullet = load("pbz_damage_forget_bullet")
        self.bullet_snapshot = load("pbz_damage_bullet_snapshot")
        self.set_ability_damage = load("pbz_damage_ability")
        self.reset_damage = load("pbz_damage_reset")
        self.forget_module = load("pbz_damage_forget_module")
        self.damage_observation = load("pbz_damage_observation")
        self.payload_function = StaticFindObject("/Script/SGameAttributes.DamagePreCalculationPayload:GetModifiedAttackData")
        self.calculator_function = StaticFindObject("/Script/SGameAttributes.DamageCalculatorBase:CalculateDamage")
        self.module_class = StaticFindObject("/Script/SGameCombatSystem.BulletAttackModule")
        self.bullet_classes = {
            StaticFindObject("/Script/SGameCombatSystem.BulletActorBase"),
            StaticFindObject("/Script/SGameCombatSystem.BulletObjectBase")
        }
        self.bullet_interface_class = StaticFindObject("/Script/SGameCombatSystem.BulletInterface")
        self.bullet_initialize_function = StaticFindObject("/Script/SGameCombatSystem.BulletInterface:Initialize")
        native_ok(self.prepare_bullet(self.bullet_initialize_function:GetAddress()))
        self.damage_enabled = true
        local calculatorClass = StaticFindObject("/Script/SGameAttributes.DamageCalculatorBase")
        local classClass = StaticFindObject("/Script/CoreUObject.Class")
        ForEachUObject(function(class)
            if class:IsValid() and class:IsA(classClass) then
                if class:IsChildOf(calculatorClass) then
                    local object = class:GetCDO()
                    if object then self:watch_damage_calculator(object) end
                end
                for _,base in ipairs(self.bullet_classes) do
                    if class:IsChildOf(base) then
                        local object = class:GetCDO()
                        if object then self:watch_damage_bullet(object) end
                        break
                    end
                end
            end
        end)
        for _,name in ipairs({"BulletActorBase","BulletObjectBase"}) do
            for _,object in ipairs(FindAllOf(name) or {}) do self:watch_damage_bullet(object) end
        end
        for _,object in ipairs(FindAllOf("DamagePreCalculationPayload") or {}) do self:watch_damage_payload(object) end
    end)
    if not ok then
        local cleaned,cleanup = pcall(self.reset,self)
        error(tostring(failure)..(cleaned and "" or "\n"..tostring(cleanup)),0)
    end
end

function Context:bullet_base(object)
    if object and object:IsValid() then
        for _,base in ipairs(self.bullet_classes) do
            if object:IsA(base) then return base end
        end
    end
end

function Context:empty_class()
    if not self.empty_class_value then
        local make = assert(package.loadlib(self.path,"pbz_script_context_empty_class"))
        self.empty_class_value = make()
    end
    return self.empty_class_value
end

function Context:damage_bullet(bullet,actor,multiplier)
    if not self.damage_enabled then return end
    self:update_damage()
    local seen = {}
    for _,object in ipairs({bullet,actor}) do
        if self:bullet_base(object) and not seen[object:GetAddress()] then
            seen[object:GetAddress()] = true
            native_ok(self.bullet_snapshot(object:GetAddress(),multiplier))
            self.damage_bullets[object:GetAddress()] = object
            local modules = object:GetPropertyValue("Modules")
            if modules then modules:ForEach(function(_,parameter)
                local module = parameter:get()
                if module and module:IsValid() and module:IsA(self.module_class) then
                    local address = module:GetAddress()
                    native_ok(self.watch_module(address,multiplier))
                    self.damage_modules[address] = multiplier ~= 1 and module or nil
                end
            end) end
        end
    end
end

function Context:ability_damage(instanceOrAddress,multiplier)
    if not self.damage_enabled then
        if multiplier == 1 then return end
        self:enable_damage()
    end
    local address = type(instanceOrAddress) == "number" and instanceOrAddress or instanceOrAddress:GetAddress()
    native_ok(self.set_ability_damage(address,multiplier))
end

function Context:watch_damage_bullet(object)
    if not self.damage_enabled then return end
    local base = self:bullet_base(object)
    if not base then return end
    local address = object:GetAddress()
    native_ok(self.watch_bullet(address,base:GetAddress(),self.bullet_interface_class:GetAddress(),
        self.bullet_initialize_function:GetAddress()))
    self.damage_bullets[address] = object
end

function Context:watch_damage_calculator(object)
    if not self.damage_enabled then return end
    if object:IsValid() then native_ok(self.watch_calculator(object:GetAddress(),self.calculator_function:GetAddress())) end
end

function Context:watch_damage_payload(object)
    if not self.damage_enabled then return end
    if not object:IsValid() then return end
    local address = object:GetAddress()
    native_ok(self.watch_payload(address,self.payload_function:GetAddress()))
    self.damage_payloads[address] = object
end

function Context:update_damage()
    if not self.damage_enabled then return end
    self.observation_frame = self.observation_frame+1
    if self.report and self.observation_frame % 120 == 0 then
        local message = self.damage_observation()
        if message then self.report(message) end
    end
    for address,object in pairs(self.damage_bullets) do
        if not object:IsValid() then
            native_ok(self.forget_bullet(address))
            self.damage_bullets[address] = nil
        end
    end
    for address,object in pairs(self.damage_payloads) do
        if not object:IsValid() then
            native_ok(self.forget_payload(address))
            self.damage_payloads[address] = nil
        end
    end
    for address,module in pairs(self.damage_modules) do
        if not module:IsValid() then
            native_ok(self.forget_module(address))
            self.damage_modules[address] = nil
        end
    end
end

function Context:interface_hook(backend,path,pre,post)
    self.watch_interface(backend:object(path):GetAddress())
    return RegisterHook(path,function(_, ...)
        if self.closed then return end
        local object = StaticFindObject(self.interface_name():gsub("^%S+ ",""))
        return pre(object, ...)
    end,post and function(...) if not self.closed then return post(...) end end)
end

function Context:owner(lookup, resolve)
    local depth = 1
    while true do
        local name, address, fn = self.at(depth)
        if not address then return end
        if lookup[address] then return lookup[address], fn end
        local object = StaticFindObject(name:gsub("^%S+ ",""))
        if object and object:IsValid() then
            local owner = resolve(object)
            if owner == false then return end
            if owner then return owner, fn end
        end
        depth = depth+1
    end
end

function Context:reset()
    self.closed = true
    self.damage_enabled = false
    if activeContext == self then activeContext = nil end
    self.enable(false)
    if self.reset_damage then native_ok(self.reset_damage()) end
    self.damage_modules,self.damage_payloads,self.damage_bullets = {},{},{}
end

return Context
