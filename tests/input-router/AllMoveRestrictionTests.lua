package.path="prototype/input-router/ue4ss/Scripts/?.lua;"..package.path
local Restrictions=require("move-restrictions")
local checks,entrances,scopes=0,0,0
local function check(value,message) checks=checks+1;assert(value,message) end
function FName(name) return {ToString=function() return name end} end
local function array(values)
    return setmetatable({GetArrayNum=function() return #values end},{__index=values})
end
local function container(names)
    local values={}
    for _,name in ipairs(names) do values[#values+1]={TagName=FName(name)} end
    return {GameplayTags=array(values)}
end
local function names(value)
    local result={}
    for index=1,value.GameplayTags:GetArrayNum() do result[#result+1]=value.GameplayTags[index].TagName:ToString() end
    return table.concat(result,"\n")
end
local function object(address,data)
    data=data or {}
    function data:IsValid() return true end
    function data:GetAddress() return address end
    function data:GetClass() return self.class end
    function data:GetPropertyValue(name) return self[name] end
    function data:SetPropertyValue(name,value)
        local list={}
        for _,tag in ipairs(value.GameplayTags) do list[#list+1]=tag.TagName:ToString() end
        self[name]=container(list)
    end
    function data:GetFullName() return "Class "..(self.path or "/Fixture/Restriction") end
    return data
end
local kinds={"equipment","training","space","stance","predecessor","cooldown"}
local nativeStates={"Ability.Action.ShaqiReinforced","CharacterState.AbilityState.Mojian.HasProjectile",
    "CharacterState.AbilityState.Mojian.ProjectileFull","CharacterState.AbilityState.HuoLiuXing.CanJumpTimeline",
    "CharacterState.AbilityState.Horizontal.Overcharged","CharacterState.AbilityState.Double.BlockClone",
    "CharacterState.AbilityState.Dash.Forbid","CharacterState.Stand.Break","CharacterState.Dead"}
local alternativeTags={
    ["Ability.Action.Hun.Dash"]={"Ability.Action.Hun.Dash","Ability.Action.Hun.Heavy.DashAtk01","Ability.Action.Hun.Heavy.DrawDashAtk01"},
    ["Ability.Action.Hun.Heavy.Atk03"]={"CharacterState.AbilityState.LuanYing.SnapSoul"},
    ["Ability.Action.Hun.Light.A.Normal"]={"CharacterState.AbilityState.LuanYing.SnapSoul"},
    ["Ability.Action.Hun.Heavy.SlitheringSword.DB1"]={"Ability.Action.Hun.Heavy.SlitheringSword.DB3"},
    ["Ability.Action.Hun.Heavy.SlitheringSword.DB3"]={"Ability.Action.Hun.Heavy.SlitheringSword.DB1","Ability.Action.Hun.Heavy.SlitheringSword.DB2"},
    ["Ability.Action.Hun.Light.Long.AAAAA"]={"Ability.Action.Hun.Heavy.Long.BSup"},
    ["Ability.Action.Hun.Light.SlitheringSword.DA"]={"Ability.Action.Hun.Light.SlitheringSword.DA"},
    ["Ability.Action.Hun.ShachiAbility.MoJi.AAA"]={"CharacterState.WeaponUpgrade.MoJian.Evolution"}}

local root=os.getenv("PBZ_ALL_MOVE_CONTRACTS") or "dist/all-move-audit/contracts"
for _,device in ipairs({"keyboard","pad"}) do
    local config=dofile(root.."/all-restrictions-"..device..".lua")
    local deviceCount=0
    for _,target in pairs(config.targets) do
        check(target.kind=="ability" or target.kind=="move","The full restriction matrix must contain combat entrances")
        local label=target.move_id or "ability:"..target.tag
        local groups=assert(target.restriction_groups,"Missing compiled qualification groups: "..label)
        local required,blocked={},{}
        for _,kind in ipairs(kinds) do
            check(groups[kind]~=nil,"Missing restriction category: "..label..":"..kind)
            for _,name in ipairs(groups[kind].required_tags) do required[#required+1]=name end
            for _,name in ipairs(groups[kind].ignored_tags) do blocked[#blocked+1]=name end
        end
        for _,name in ipairs(nativeStates) do required[#required+1]=name end
        local class=object(11,{path=target.ability_class or "/Fixture/Restriction.Attack_C"})
        function StaticFindObject(path) return path==target.ability_class and class or nil end
        local cdo=object(12,{class=class,AbilityTags=container({target.tag}),
            ActivationRequiredTags=container(required),ActivationBlockedTags=container(blocked)})
        local primary=object(13,{class=class,ActivationRequiredTags=container(required),ActivationBlockedTags=container(blocked)})
        local asc=object(14,{ActivatableAbilities={Items=array({{Ability=cdo,ActiveCount=0,
            NonReplicatedInstances=array({primary}),ReplicatedInstances=array({})}})}})
        local backend={config=config,receiver=function() return asc end,
            ability_specs=function(address,classAddress,all,count,...)
                check(address==asc:GetAddress() and (classAddress==0 or classAddress==class:GetAddress()),
                    "The actual qualification selection changed its player or exact class: "..label)
                return "1 "
            end}
        local originalRequired,originalBlocked=names(cdo.ActivationRequiredTags),names(cdo.ActivationBlockedTags)
        for _,kind in ipairs(kinds) do
            local settings={restriction_bypasses={kind}}
            local state={instance=primary,restriction_target=target,restriction_bypasses={kind}}
            Restrictions.protect(backend,state)
            for _,name in ipairs(nativeStates) do
                check(Restrictions.override(state,name)==nil,"Qualification bypass fabricated native state: "..label..":"..name)
                check(Restrictions.query(backend,state,name,0)==0 and Restrictions.query(backend,state,name,2)==2,
                    "Actual native readiness must retain both unavailable and available results: "..label..":"..name)
            end
            if target.kind=="ability" then
                for _,name in ipairs(alternativeTags[target.tag] or {}) do
                    -- The fixture's native fields do not assign a mode to an alternative.
                    -- Its actual GAS field can still establish its own scoped mode.
                    check(Restrictions.override(state,name)==nil,"Opposite native routes forced one branch: "..label..":"..name)
                end
            end
            Restrictions.with(backend,target,settings,function()
                scopes=scopes+1
                for _,property in ipairs({"ActivationRequiredTags","ActivationBlockedTags"}) do
                    local mode=property=="ActivationRequiredTags" and "required_tags" or "ignored_tags"
                    local retained={}
                    for index=1,primary[property].GameplayTags:GetArrayNum() do
                        retained[primary[property].GameplayTags[index].TagName:ToString()]=true
                    end
                    for _,category in ipairs(kinds) do
                        for _,name in ipairs(groups[category][mode]) do
                            check((retained[name]==nil)==(category==kind),"A selected category changed another category: "..label..":"..kind..":"..name)
                            local expected=category==kind and (mode=="required_tags" and 1 or 0) or nil
                            check(Restrictions.override(state,name)==expected,"Native continuation lost the binding category: "..label..":"..name)
                        end
                    end
                    for _,name in ipairs(nativeStates) do
                        if mode=="required_tags" then check(retained[name],"A qualification scope removed real readiness: "..label..":"..name) end
                    end
                end
            end)
            check(names(cdo.ActivationRequiredTags)==originalRequired and names(cdo.ActivationBlockedTags)==originalBlocked
                and names(primary.ActivationRequiredTags)==originalRequired and names(primary.ActivationBlockedTags)==originalBlocked,
                "The native template and actual primary instance must restore synchronously: "..label..":"..kind)
            check(backend.move_restriction_scopes==nil,"A qualification scope leaked to the next binding: "..label)
        end
        if target.kind=="ability" then
            for _,name in ipairs(alternativeTags[target.tag] or {}) do
                local category=assert(config.restriction_tags[name],"An alternative lost its native GAS qualification category: "..name)
                for _,property in ipairs({"ActivationRequiredTags","ActivationBlockedTags"}) do
                    primary.ActivationRequiredTags=container(required)
                    primary.ActivationBlockedTags=container(blocked)
                    local list={}
                    local original=property=="ActivationRequiredTags" and required or blocked
                    for _,tag in ipairs(original) do list[#list+1]=tag end
                    list[#list+1]=name
                    primary[property]=container(list)
                    local state={instance=primary,restriction_target=target,restriction_bypasses={category}}
                    Restrictions.protect(backend,state)
                    check(Restrictions.override(state,name)==(property=="ActivationRequiredTags" and 1 or 0),
                        "The actual GAS field must choose its own mode for an alternative: "..label..":"..name)
                end
            end
        end
        deviceCount=deviceCount+1
    end
    check(deviceCount>0,"The full compiled restriction matrix is empty")
    entrances=entrances+deviceCount
end
print("AllMoveRestrictionTests PASS "..checks.."; entrances="..entrances.."; individual-category scopes="..scopes)
