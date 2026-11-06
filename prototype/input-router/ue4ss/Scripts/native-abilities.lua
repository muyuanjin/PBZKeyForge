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
    return setmetatable({backend = backend, masked = {}}, NativeAbilities)
end

function NativeAbilities:apply(suppressed)
    if not next(suppressed) and not next(self.masked) then return end
    local seen = {}
    for _, asset in ipairs(ObjectPool.get("InputActionBinding")) do
        if asset:IsValid() then
            local assetAddress = asset:GetAddress()
            local saved = self.masked[assetAddress] or {}
            self.masked[assetAddress] = saved
            local visited = {}
            seen[assetAddress] = true
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
        end
    end
    for assetAddress in pairs(self.masked) do
        if not seen[assetAddress] then self.masked[assetAddress] = nil end
    end
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
