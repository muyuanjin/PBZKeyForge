local Context = {}
Context.__index = Context

function Context.new()
    local scripts = debug.getinfo(1,"S").source:sub(2):match("^(.*[/\\])")
    local path = scripts:gsub("Scripts[/\\]$", "") .. "lib/pbzability-context.dll"
    local enable, failure = package.loadlib(path,"pbz_script_context_enable")
    assert(enable, "Ability context unavailable: " .. tostring(failure))
    local at = assert(package.loadlib(path,"pbz_script_context_at"))
    enable(true)
    return setmetatable({enable=enable, at=at},Context)
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
    self.enable(false)
end

return Context
