package.path = "prototype/input-router/ue4ss/Scripts/?.lua;" .. package.path
package.loaded["script-context"] = {}
local Costs = require("resource-costs")
local checks, nextAddress = 0, 0
local function check(value, message) assert(value, message); checks = checks + 1 end
local classes, counts = {}, {}
local function reset_counts() counts = {callbacks=0,getters=0,names=0,tags=0,reads=0} end
local function object(properties)
    nextAddress = nextAddress + 1
    local address, value = nextAddress, properties or {}
    function value:IsValid() return self.valid ~= false end
    function value:GetAddress() return address end
    function value:GetClass() return self.class end
    function value:GetFullName() counts.names=counts.names+1; return "Class " .. self.path end
    function value:GetPropertyValue(name) return self[name] end
    return value
end
local function array(items)
    return setmetatable({GetArrayNum=function() return #items end,ForEach=function(_, callback)
        for index, value in ipairs(items) do
            counts.callbacks = counts.callbacks + 1
            callback(index, {get=function() counts.getters=counts.getters+1; return value end})
        end
    end}, {__index=function(_, index) counts.reads=counts.reads+1; return items[index] end})
end
function StaticFindObject(path) return classes[path] end
local function tags(names)
    local entries = {}
    for _, name in ipairs(names) do
        entries[#entries+1] = {TagName={ToString=function() counts.tags=counts.tags+1; return name end}}
    end
    return {GameplayTags=array(entries)}
end
local function grant(path, names, active)
    local class = classes[path] or object({path=path})
    classes[path] = class
    local cdo = object({class=class,AbilityTags=tags(names)})
    local instance, replica = object({class=class}), object({class=class})
    return {Ability=cdo,ActiveCount=active and 1 or 0,
        NonReplicatedInstances=array({cdo,object({valid=false}),instance}),ReplicatedInstances=array({replica})}, instance, replica
end
local function fixture(module)
    classes = {}
    local specs = {}
    for index=1,128 do specs[index]=grant("/Player/Other_"..index,{"Ability.Other","State.Other"}) end
    local wanted, first, second = grant("/Player/Wanted",{"Ability.Attack.Heavy","State.Grounded"},true)
    specs[#specs+1] = wanted
    specs[#specs+1] = grant("/Player/PrefixCollision",{"Ability.Attacker","State.Grounded"})
    local component = object({ActivatableAbilities={Items=array(specs)}})
    local owner = setmetatable({backend={receiver=function() return component end}},module)
    local function select(target)
        local found, active = {}, {}
        owner:select(target,function(instance, running) found[instance:GetAddress()]=true;active[#active+1]=running end)
        return found,active
    end
    return owner,select,first,second,specs
end
reset_counts()
local owner,select,first,second,specs = fixture(Costs)
reset_counts()
local found,active = select({ability_class="/Player/Wanted"})
check(found[first:GetAddress()] and found[second:GetAddress()] and #active==2 and active[1] and active[2],
    "class selection preserves live instances and spec activity while excluding CDOs and invalid objects")
check(counts.names==0 and counts.callbacks==0 and counts.getters==0,
    "loaded class selection must use addresses and indexed native arrays")
reset_counts()
found,active = select({activation_tags={"Ability.Attack","State.Grounded","Ability.Attack"}})
check(found[first:GetAddress()] and found[second:GetAddress()] and #active==2,
    "all-tag selection must honor descendants, duplicate requirements and tag boundaries")
check(counts.tags<=#specs*2 and counts.callbacks==0,"all requested tags must share one native tag traversal")
found,active = select({resource_tags={"Ability.Missing","Ability.Attack"}})
check(found[first:GetAddress()] and #active==2,"resource tags retain any-match semantics")
found,active = select({activation_tags={}})
check(#active==#specs*2,"an empty activation tag container matches every granted ability")
found,active = select({resource_tags={}})
check(#active==0,"an empty resource selector matches no ability")
found,active = select({resource_tags={"Ability.Missing"},tag="Ability.Attack"})
check(found[first:GetAddress()] and #active==2,"a single tag remains an alternative to unmatched resource tags")
found,active = select({tag="Ability.Attack",activation_tags={"Ability.Missing"}})
check(#active==0,"activation tags retain precedence over a single tag")
found,active = select({tag="Ability.Attack",activation_tags=false})
check(found[first:GetAddress()] and #active==2,"disabled activation tags retain single-tag selection")

local nativeCalls=0
owner.context={ability_specs=function(component,class,all,count,...)
    nativeCalls=nativeCalls+1
    check(type(component)=="number","native selection receives the current component")
    if class~=0 then
        check(class==first:GetClass():GetAddress() and count==0 and all==false,"native class selection keeps class-address precedence")
    else
        check(all and count==2 and #{...}==2,"native tags retain all-match semantics and argument count")
    end
    return "129 "
end}
reset_counts()
found,active=select({ability_class="/Player/Wanted"})
check(found[first:GetAddress()] and #active==2 and counts.reads<16 and counts.tags==0,
    "native filtering must only wrap matched specs and instances in Lua")
found,active=select({activation_tags={"Ability.Attack","State.Grounded"}})
check(found[first:GetAddress()] and #active==2 and nativeCalls==2,"native filtering preserves live instance and activity results")
owner.context.ability_specs=function() return 0,"unsupported fixture layout" end
local ok,failure=pcall(select,{ability_class="/Player/Wanted"})
check(not ok and tostring(failure):find("unsupported fixture layout",1,true),"native layout errors propagate through target isolation")
owner.context=nil

local missing = {ability_class="/Player/Late"}
found,active = select(missing)
check(#active==0,"an unloaded class does not select another ability")
local late,lateInstance = grant(missing.ability_class,{"Ability.Late"})
specs[#specs+1] = late
found,active = select(missing)
check(found[lateInstance:GetAddress()] and #active==2,"a class granted after an unsuccessful lookup is retried")
classes[missing.ability_class] = nil
local replacement,replacementInstance = grant(missing.ability_class,{"Ability.Late"})
specs[#specs] = replacement
found,active = select(missing)
check(found[replacementInstance:GetAddress()] and not found[lateInstance:GetAddress()],
    "a later selection reads the current class and instance arrays")

local streaming = {ability_class="/Player/Streamed"}
owner.instances,owner.transfers = {},{}
owner.arm_transfer = function(_, instance, transfer) transfer.armed[instance:GetAddress()]=instance end
owner:begin_transfer({},streaming)
local streamed,streamedInstance = grant(streaming.ability_class,{"Ability.Streamed"})
specs[#specs+1]=streamed
check(owner:inherit(streamedInstance)==nil and owner.transfers[1].armed[streamedInstance:GetAddress()]==streamedInstance,
    "inheritance resolves a class loaded inside the native activation")
local inherited = 0
owner.arm_transfer = function() inherited=inherited+1 end
owner:end_transfer(true)
check(inherited==2 and #owner.transfers==0,"post-activation selection sees newly granted instances")

local baseline = os.getenv("PBZ_ABILITY_SELECTION_BASELINE")
do
    local Selection = require("ability-selection")
    local backend = owner.backend
    local activeTarget = {resource_tags={"Ability.Attack"},active_only=true}
    reset_counts()
    local matched=0
    Selection.each(backend,activeTarget,function(spec)
        check(spec.ActiveCount>0,"active selection must exclude inactive grants")
        matched=matched+1
    end)
    check(matched==1 and counts.tags==1,"Lua active selection must skip inactive ability tags")
    local calls=0
    backend.active_ability_specs=function() calls=calls+1;return "129 " end
    reset_counts()
    Selection.each(backend,activeTarget,function() matched=matched+1 end)
    check(calls==1 and counts.tags==0 and counts.reads==1,"native active selection must only read the matching active spec")
    specs[129].ActiveCount=0
    Selection.each(backend,activeTarget,function() error("an inactive replacement cannot be visited") end)
    specs[129].ActiveCount=1
    backend.active_ability_specs=nil
end
if baseline then
    for _, implementation in ipairs({{name="before",module=dofile(baseline)},{name="after",module=Costs}}) do
        local _, run = fixture(implementation.module)
        for _,target in ipairs({{ability_class="/Player/Wanted"},{activation_tags={"Ability","State"}}}) do
            reset_counts()
            local started = os.clock()
            for index=1,200 do run(target) end
            print(string.format("AbilitySelectionBenchmark %s %s: Lua-ms/call=%.4f callbacks=%d getters=%d class-names=%d tag-names=%d",
                implementation.name,target.ability_class and "class" or "tags",(os.clock()-started)*1000/200,
                counts.callbacks,counts.getters,counts.names,counts.tags))
        end
    end
end
print("AbilitySelectionTests PASS " .. checks)
