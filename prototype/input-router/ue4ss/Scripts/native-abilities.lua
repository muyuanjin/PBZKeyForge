local NativeAbilities = {}
NativeAbilities.__index = NativeAbilities
local ObjectPool = require("object-pool")

local function each_trigger(asset, callback)
    local function visit(data)
        for _, name in ipairs({"Actions", "Combos", "InterruptLower"}) do
            local triggers = data[name]
            triggers:ForEach(function(_, entry) callback(entry:get()) end)
        end
    end
    local bindings = asset.Bindings
    bindings:ForEach(function(_, entry)
        local binding = entry:get()
        visit(binding.NormalData)
        local conditions = binding.TagConditionData
        conditions:ForEach(function(_, data) visit(data:get()) end)
    end)
end

function NativeAbilities.new(backend)
    local source = debug.getinfo(1,"S").source:sub(2):match("^(.*[/\\])")
    local dll = source and source:gsub("Scripts[/\\]$", "") .. "lib/pbzability-context.dll"
    local observe, unchanged, forget
    if dll then
        observe = package.loadlib(dll,"pbz_ability_bindings_observe")
        unchanged = package.loadlib(dll,"pbz_ability_bindings_unchanged")
        forget = package.loadlib(dll,"pbz_ability_bindings_forget")
    end
    return setmetatable({backend = backend, masked = {}, observations = {}, suppressed = {},
        observe_native = observe and unchanged and forget and observe,
        unchanged_native = unchanged, forget_native = forget}, NativeAbilities)
end

function NativeAbilities:apply(suppressed)
    if not next(suppressed) and not next(self.masked) then
        for assetAddress, token in pairs(self.observations) do
            self.forget_native(token)
            self.observations[assetAddress] = nil
        end
        self.suppressed = suppressed
        return
    end
    local changed = false
    for tag in pairs(suppressed) do if not self.suppressed[tag] then changed = true; break end end
    if not changed then
        for tag in pairs(self.suppressed) do if not suppressed[tag] then changed = true; break end end
    end
    local seen = {}
    for _, asset in ipairs(ObjectPool.get("InputActionBinding")) do
        if asset:IsValid() then
            local assetAddress = asset:GetAddress()
            seen[assetAddress] = true
            local token = self.observations[assetAddress]
            if not changed and token and self.unchanged_native(token,assetAddress,true) == true then goto continue end
            local saved = self.masked[assetAddress] or {}
            self.masked[assetAddress] = saved
            local visited = {}
            each_trigger(asset, function(trigger)
                local location = trigger:GetStructAddress()
                local current = trigger.TriggerTag.TagName:ToString()
                local original = current == "None" and saved[location] or current
                if original ~= "None" and suppressed[original] then
                    saved[location] = original
                    visited[location] = true
                    if current ~= "None" then trigger.TriggerTag.TagName = FName("None") end
                else
                    if current == "None" and saved[location] then
                        trigger.TriggerTag.TagName = FName(saved[location])
                    end
                    saved[location] = nil
                end
            end)
            for location in pairs(saved) do
                if not visited[location] then saved[location] = nil end
            end
            if not next(saved) then self.masked[assetAddress] = nil end
            if self.observe_native and next(suppressed) then
                local observed, failure = self.observe_native(assetAddress,token or 0)
                assert(observed and observed ~= 0, failure or "Native ability observation failed")
                self.observations[assetAddress] = observed
            end
            ::continue::
        end
    end
    for assetAddress in pairs(self.masked) do
        if not seen[assetAddress] then self.masked[assetAddress] = nil end
    end
    for assetAddress, token in pairs(self.observations) do
        if not seen[assetAddress] or not next(suppressed) then
            self.forget_native(token)
            self.observations[assetAddress] = nil
        end
    end
    self.suppressed = suppressed
end

function NativeAbilities:prepare()
    local suppressed = {}
    for _, tag in ipairs(self.backend.config.ability_suppressions or {}) do suppressed[tag] = true end
    self:apply(suppressed)
end

function NativeAbilities:restore()
    self:apply({})
end

return NativeAbilities
