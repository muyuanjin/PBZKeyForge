local Pool = {}
local pools = {}

function Pool.clear()
    for _, pool in pairs(pools) do
        pool.objects, pool.pending, pool.rescan = {}, {}, true
    end
end

function Pool.get(name, classPath)
    if type(NotifyOnNewObject) ~= "function" then return FindAllOf(name) or {} end
    local pool = pools[name]
    if not pool then
        local initial = FindAllOf(name) or {}
        if not classPath then
            for _, object in ipairs(initial) do
                if object:IsValid() then
                    classPath = object:GetClass():GetFullName():gsub("^%S+ ", "")
                    break
                end
            end
        end
        if not classPath then return initial end
        pool = {objects={}, pending=initial}
        pools[name] = pool
        NotifyOnNewObject(classPath, function(object)
            -- Construction may run outside the game tick; inspect properties on the next tick.
            pool.pending[#pool.pending+1] = object
        end)
    end
    if pool.rescan then
        pool.pending = FindAllOf(name) or {}
        pool.rescan = false
    end
    local objects, count = pool.objects, 0
    local seen = #pool.pending > 0 and {} or nil
    for index = 1, #objects do
        local object = objects[index]
        if object:IsValid() then
            count = count + 1
            objects[count] = object
            if seen then seen[object:GetAddress()] = true end
        end
    end
    for index = #objects, count + 1, -1 do objects[index] = nil end
    if seen then
        for _, object in ipairs(pool.pending) do
            if object:IsValid() and not seen[object:GetAddress()] then
                objects[#objects+1], seen[object:GetAddress()] = object, true
            end
        end
        pool.pending = {}
    end
    return objects
end

return Pool
