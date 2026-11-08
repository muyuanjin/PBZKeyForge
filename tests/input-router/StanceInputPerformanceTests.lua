local scripts = "prototype/input-router/ue4ss/Scripts/"
local currentPath = scripts .. "stance-input.lua"
local stancePath = "/PBZDeflectSystem/Input/IA_Stance"
local frames = 2000
local zero = {X=0,Y=0,Z=0}
local nextAddress = 100

local function check(value, message)
    assert(value, message)
end

function FName(value)
    return {ToString=function() return value end}
end

local function object(path)
    nextAddress = nextAddress + 1
    local address = nextAddress
    local value = {}
    function value:IsValid() return true end
    function value:GetAddress() return address end
    function value:GetFullName() return "Object " .. path end
    return value
end

local function counted_entry(action, owner, key, triggers, onRead)
    local value = {action=action, owner=owner, active_key=key, triggers=triggers}
    return setmetatable({}, {__index=function(_,name)
        if name == "action" and onRead then onRead() end
        return value[name]
    end})
end

local function fixture(module, triggerScenario)
    local owner,passive,other = object("Owner"),object("Passive"),object("Other")
    local controller,playerInput = object("Controller"),object("PlayerInput")
    controller.PlayerInput = playerInput
    local watched,watchCalls,prepareCalls,stateCalls = {},0,0,0
    local objects,coverage = {},{}
    local reads,objectCalls,libraryCalls,invokeCalls = 0,0,0,0
    local countSource=function() reads=reads+1 end
    for index=1,63 do
        coverage[index]=counted_entry("/Game/Input/" .. index,"game","None",{},countSource)
    end
    local triggerObject=triggerScenario and object("Trigger") or nil
    local triggers = triggerObject and {triggerObject} or {}
    coverage[64]=counted_entry(stancePath,"game","RightMouseButton",triggers,countSource)
    local native = {coverage=coverage}
    function native:engine_frame()
        return self.backend:invoke(self.frame_library,self.frame_path)
    end
    local backend = {dynamic_config={separate_stance=true},controller=controller,native_input=native,
        currentFrame=1}
    native.backend = backend
    native.frame_library=object("FrameLibrary")
    native.frame_path="/Script/Engine.KismetSystemLibrary:GetFrameCount"
    function backend:object(path)
        objectCalls=objectCalls+1
        objects[path]=objects[path] or object(path)
        return objects[path]
    end
    function backend:library(name)
        libraryCalls=libraryCalls+1
        return self:object("/Script/" .. name:gsub("%.", ".Default__", 1))
    end
    function backend:receiver() return owner end
    function backend:invoke(receiver,path)
        invokeCalls=invokeCalls+1
        if path:find(":GetAbilitySystemComponentFromActorInfo",1,true) then
            return receiver==passive and owner or other
        end
        if path:find(":GetGameplayTagCount",1,true) then return 0 end
        if path:find(":Break Forbid Stance",1,true) then return false end
        if path:find(":GetFrameCount",1,true) then return backend.currentFrame end
        if path:find(":GetInputVectorKeyState",1,true) then return zero end
        error("unexpected stance benchmark call " .. path)
    end
    FindAllOf=function() return {other,passive} end
    local originalLoadlib=package.loadlib
    package.loadlib=function(_,name)
        if name=="pbz_stance_prepare" then
            return function(asc,selected)
                check(asc==owner:GetAddress() and selected==passive:GetAddress(),"stance context changed")
                prepareCalls=prepareCalls+1
                return 1
            end
        end
        if name=="pbz_stance_restore" then return function() return 1 end end
        if name=="pbz_input_watch_trigger" then
            return function(trigger)
                check(trigger==triggerObject:GetAddress(),"another trigger was observed")
                watchCalls=watchCalls+1
                watched[trigger]=true
                return 1
            end
        end
        if name=="pbz_input_trigger_state" then
            return function(trigger)
                stateCalls=stateCalls+1
                if watched[trigger] then return 2,0,0 end
            end
        end
        error("unexpected stance library lookup " .. name)
    end
    local ok,errorMessage=pcall(function()
        module.prepare(backend,{})
        reads,objectCalls,libraryCalls,invokeCalls,prepareCalls,watchCalls,stateCalls=0,0,0,0,0,0,0
        for index=1,frames do
            backend.currentFrame=index
            module.prepare(backend,{})
        end
    end)
    package.loadlib=originalLoadlib
    if not ok then error(errorMessage,0) end
    return {source_reads=reads,object_lookups=objectCalls,library_lookups=libraryCalls,
        native_invokes=invokeCalls,prepare_calls=prepareCalls,watch_calls=watchCalls,
        trigger_queries=stateCalls}
end

local function run(path,triggerScenario)
    local module=dofile(path)
    local result=fixture(module,triggerScenario)
    result.module=module
    return result
end

local baseline=os.getenv("PBZ_STANCE_BENCHMARK_BASELINE")
for _,case in ipairs({{name="standby",trigger=false},{name="trigger",trigger=true}}) do
    local before=baseline and run(baseline,case.trigger) or nil
    local after=run(currentPath,case.trigger)
    local function report(name,value)
        print(string.format("StanceInputBenchmark %s %s: frames=%d source-reads=%d object-lookups=%d library-lookups=%d native-invokes=%d prepare-calls=%d watch-calls=%d trigger-queries=%d",
            case.name,name,frames,value.source_reads,value.object_lookups,value.library_lookups,value.native_invokes,
            value.prepare_calls,value.watch_calls,value.trigger_queries))
    end
    if before then report("before",before) end
    report("after",after)
    check(after.source_reads == 0,"a stable stance frame rescanned native coverage")
    check(after.object_lookups == 0 and after.library_lookups == 0,
        "a stable stance frame rebuilt static native context")
    check(after.prepare_calls == 0,"a stable stance frame replayed its native contribution")
    if case.trigger then
        check(after.watch_calls == 0,"a stable trigger observation was registered again")
        check(after.trigger_queries == frames,"trigger observation was not sampled exactly once per frame")
    else
        check(after.watch_calls == 0 and after.trigger_queries == 0,"an untriggered stance source queried trigger state")
    end
end

print("StanceInputPerformanceTests PASS")
