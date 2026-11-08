local Selection = {}
local native, native_checked
local active_native, active_checked
local function valid(object) return object and object:IsValid() end

local function native_selector(backend, active)
    if active and backend.active_ability_specs then return backend.active_ability_specs end
    if backend.ability_specs then return backend.ability_specs end
    local context = backend.resource_costs and backend.resource_costs.context
    if active then
        if context and context.active_ability_specs then return context.active_ability_specs end
        if not active_checked then
            active_checked = true
            local scripts = debug.getinfo(1,"S").source:sub(2):match("^(.*[/\\])")
            active_native = package.loadlib(scripts:gsub("Scripts[/\\]$", "") .. "lib/pbzability-context.dll", "pbz_active_ability_spec_indices")
        end
        return active_native
    end
    if context and context.ability_specs then return context.ability_specs end
    if not native_checked then
        native_checked = true
        local scripts = debug.getinfo(1,"S").source:sub(2):match("^(.*[/\\])")
        native = package.loadlib(scripts:gsub("Scripts[/\\]$", "") .. "lib/pbzability-context.dll", "pbz_ability_spec_indices")
    end
    return native
end

function Selection.matcher(target)
    if target.ability_class_address then
        return function(ability) return ability:GetClass():GetAddress() == target.ability_class_address end,
            {class_address=target.ability_class_address}
    end
    if target.ability_class then
        local class = StaticFindObject(target.ability_class)
        local address = valid(class) and class:GetAddress()
        local selection = {class_address=address}
        return function(ability)
            local candidate = ability:GetClass()
            if address then return candidate:GetAddress() == address end
            -- A native activation can load the class after its pre-hook.
            if candidate:GetFullName():gsub("^%S+ ", "") == target.ability_class then
                address = candidate:GetAddress()
                selection.class_address = address
                return true
            end
            return false
        end, selection
    end
    local all, prefixes = not not target.activation_tags, {}
    local wanted = all and target.activation_tags or {}
    if not all then
        for _, tag in ipairs(target.resource_tags or {}) do wanted[#wanted+1] = tag end
        if target.tag then wanted[#wanted+1] = target.tag end
    end
    for index, tag in ipairs(wanted) do prefixes[index] = tag .. "." end
    return function(ability)
        if #wanted == 0 then return all end
        local tags = ability:GetPropertyValue("AbilityTags")
        if not tags then return false end
        local entries = tags.GameplayTags
        local matched, count = all and #wanted > 1 and {}, 0
        for index = 1, entries:GetArrayNum() do
            local tag = entries[index].TagName:ToString()
            for slot, name in ipairs(wanted) do
                if tag == name or tag:sub(1,#prefixes[slot]) == prefixes[slot] then
                    if not all or #wanted == 1 then return true end
                    if not matched[slot] then matched[slot], count = true, count + 1 end
                    if count == #wanted then return true end
                end
            end
        end
        return false
    end, {tags=wanted,all=all}
end


function Selection.each(backend,target,callback,matches,selection,native)
    local component = backend:receiver({receiver="ability_system"})
    if not valid(component) then return end
    if not selection then
        local matcher
        matcher,selection = Selection.matcher(target)
        matches = matches or matcher
    end
    local specs = component.ActivatableAbilities.Items
    local function visit(index,filtered)
        local spec = specs[index]
        if target.active_only and spec.ActiveCount == 0 then return end
        local ability = spec.Ability
        if valid(ability) and (filtered or matches(ability)) then callback(spec,ability) end
    end
    native = native or native_selector(backend,target.active_only)
    if native and (selection.class_address or selection.tags) then
        local tags = selection.tags or {}
        local indices,failure = native(component:GetAddress(),selection.class_address or 0,
            selection.all == true,#tags,table.unpack(tags))
        if failure then error(failure,0) end
        for index in indices:gmatch("%d+") do visit(tonumber(index),true) end
    else
        for index=1,specs:GetArrayNum() do visit(index,false) end
    end
end

function Selection.same(first,second)
    if first.class_address or second.class_address then return first.class_address == second.class_address end
    if not first.tags or not second.tags or first.all ~= second.all or #first.tags ~= #second.tags then return false end
    for index,tag in ipairs(first.tags) do if second.tags[index] ~= tag then return false end end
    return true
end

return Selection
