local Pool = {}
local pools = {}

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
    local objects, seen = {}, {}
    for _, list in ipairs({pool.objects, pool.pending}) do
        for _, object in ipairs(list) do
            if object:IsValid() then
                local id = object:GetAddress()
                if not seen[id] then objects[#objects+1], seen[id] = object, true end
            end
        end
    end
    pool.objects, pool.pending = objects, {}
    return objects
end

return Pool
