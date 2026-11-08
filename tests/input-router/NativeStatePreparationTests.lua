package.path = "prototype/input-router/ue4ss/Scripts/?.lua;"..package.path
local Preparation = require("move-preparation")
local checks = 0
local function check(value,message) checks=checks+1;assert(value,message) end
function FName(value) return value end
local function object(id,path)
    return {IsValid=function() return true end,GetAddress=function() return id end,
        GetFullName=function() return "Object "..path end}
end
local function array(values)
    return setmetatable({GetArrayNum=function() return #values end},{__index=values})
end
local function fixture()
    local classes = {A=object(1,"/A.A_C"),B=object(2,"/B.B_C")}
    function StaticFindObject(path)
        for _,class in pairs(classes) do if class:GetFullName()=="Object "..path then return class end end
    end
    local specs={}
    for _,class in ipairs({classes.A,classes.B}) do
        local ability=object(10+#specs,"CDO")
        function ability:GetClass() return class end
        specs[#specs+1]={Ability=ability,ActiveCount=0}
    end
    local component=object(20,"ASC")
    component.ActivatableAbilities={Items=array(specs)}
    local state={tags={Equipment=true},applied={},removed={},next=0}
    local context={}
    function context:apply_effect_self(asc,effect,level,scopes)
        check(asc==component and level==1,"Native effect must apply to the player's ASC at native level")
        if state.failApply then error("effect application failed") end
        state.next=state.next+1
        state.applied[#state.applied+1]=effect
        state.scopes=scopes
        if not state.noTag then state.tags.Buff=true end
        return state.next
    end
    function context:remove_effect(asc,handle)
        check(asc==component,"Only the original ASC owns the effect handle")
        if state.failRemove then error("effect cleanup failed") end
        state.removed[#state.removed+1]=handle
        state.tags.Buff=nil
        return not state.consumed
    end
    local backend={resource_costs={context=context,instances={}}}
    function backend:receiver() return component end
    function backend:object(path) return path end
    function backend:invoke(asc,path,tag)
        check(asc==component and path:match(":GetGameplayTagCount$"),"Qualification must use the reflected native tag count")
        return state.tags[tag.TagName] and 1 or 0
    end
    local effect={effectClass="/GE.GE_C",qualificationTag="Buff",equipmentTag="Equipment",requiredBypassKinds={"predecessor"}}
    local first={kind="move",ability_class="/A.A_C",native_state_effects={effect}}
    local second={kind="ability",ability_class="/B.B_C",native_state_effects={effect}}
    return backend,state,first,second,specs,{restriction_bypasses={"predecessor"}}
end

do
    local backend,state,target,_,_,policy=fixture()
    Preparation.begin(backend,target,{restriction_bypasses={"stance"}},"A")
    check(#state.applied==0,"An unselected restriction must not grant a buff")
    state.tags.Equipment=nil
    Preparation.begin(backend,target,policy,"A")
    check(#state.applied==0,"A shared attack must not prepare another weapon's effect")
    state.tags.Equipment=true;state.tags.Buff=true
    Preparation.begin(backend,target,policy,"A");Preparation.reset(backend)
    check(#state.applied==0 and #state.removed==0 and state.tags.Buff,"The player's original effect must stay untouched")
end
do
    local backend,state,target,_,specs,policy=fixture()
    local scope=Preparation.begin(backend,target,policy,"A")
    specs[1].ActiveCount=1;Preparation.finish(backend,scope,true);Preparation.update(backend)
    check(#state.applied==1 and #state.removed==0,"The native effect must last through its active attack")
    local repeated=Preparation.begin(backend,target,policy,"A")
    Preparation.finish(backend,repeated,false)
    check(#state.applied==1 and #state.removed==0,"A rejected repeat must preserve the already running attack's effect")
    specs[1].ActiveCount=0
    backend.complete_move_pending={A={spec=target}};Preparation.update(backend)
    check(#state.removed==0,"A prepared native phase must retain its real effect")
    backend.complete_move_pending=nil;backend.native_move_inputs={A={target=target}};Preparation.update(backend)
    check(#state.removed==0,"Native input preparation must retain its effect")
    backend.native_move_inputs=nil;backend.snake_preparation={requests={A={target=target}}};Preparation.update(backend)
    check(#state.removed==0,"Real projectile preparation must retain its effect")
    backend.snake_preparation=nil
    local continuation=object(99,"Continuation")
    function continuation:GetPropertyValue() return true end
    backend.resource_costs.instances[99]={instance=continuation,restriction_target=target};Preparation.update(backend)
    check(#state.removed==0,"Native continuations must retain this attack's effect")
    backend.resource_costs.instances={};Preparation.update(backend)
    check(#state.removed==1 and next(backend.move_preparation)==nil,"Completion must remove only the tool's handle")
end
do
    local backend,state,first,second,specs,policy=fixture()
    Preparation.finish(backend,Preparation.begin(backend,first,policy,"A"),true)
    specs[1].ActiveCount=1
    local other=Preparation.begin(backend,second,policy,"B")
    Preparation.finish(backend,other,false)
    check(#state.applied==1 and #state.removed==0,"A second binding's failure must not remove the shared active effect")
    Preparation.finish(backend,Preparation.begin(backend,second,policy,"B"),true)
    specs[2].ActiveCount=1;specs[1].ActiveCount=0;Preparation.update(backend)
    check(#state.removed==0,"A second accepted attack must retain shared effect ownership")
    specs[2].ActiveCount=0;Preparation.update(backend)
    check(#state.removed==1,"The last completed attack must release the single shared handle")
end
do
    local backend,state,target,_,_,policy=fixture()
    Preparation.finish(backend,Preparation.begin(backend,target,policy,"A"),false)
    check(#state.removed==1,"A native activation refusal must immediately restore the added effect")
    state.noTag=true
    local scope,reason=Preparation.begin(backend,target,policy,"A")
    check(scope==nil and reason:match("Buff") and #state.removed==2,"Failed qualification must report its tag and roll back its effect")
    state.noTag=false
    Preparation.finish(backend,Preparation.begin(backend,target,policy,"A"),true)
    state.failRemove=true
    check(not pcall(Preparation.reset,backend) and next(backend.move_preparation),"Failed native cleanup must remain retryable")
    state.failRemove=false;Preparation.reset(backend)
    check(next(backend.move_preparation)==nil,"Cleanup retry must release the retained handle")
    Preparation.finish(backend,Preparation.begin(backend,target,policy,"A"),true)
    state.consumed=true;Preparation.reset(backend)
    check(next(backend.move_preparation)==nil,"An effect consumed by the game needs no second removal")
end
do
    local backend,state,target,_,_,policy=fixture()
    target.native_state_effects[2]={effectClass="/Other.Other_C",qualificationTag="Other",requiredBypassKinds={"predecessor"}}
    local apply=backend.resource_costs.context.apply_effect_self
    function backend.resource_costs.context:apply_effect_self(component,effect,level)
        if effect=="/Other.Other_C" then error("second effect failed") end
        return apply(self,component,effect,level)
    end
    check(not pcall(Preparation.begin,backend,target,policy,"A") and #state.removed==1,
        "A later preparation exception must undo effects already applied in the same request")
end
do
    local backend,state,target,_,specs,policy=fixture()
    specs[1].ActiveCount=1
    Preparation.finish(backend,Preparation.begin(backend,target,policy,"new-binding"),false)
    check(#state.removed==1,"A refused binding must not add its effect to an already active original attack")
end
do
    local backend,state,target,_,_,policy=fixture()
    target.native_state_effects[1].requirementScopes={
        {effectClass="/GE.GE_C",application=true}
    }
    local requirementClass="/Script/GameplayAbilities.TargetTagRequirementsGameplayEffectComponent"
    local components,objects={},{}
    for index,path in ipairs({"/GE.GE_C","/Lower.Lower_C"}) do
        local component=object(200+index,path..":Requirements")
        function component:IsA(class) return class==requirementClass end
        local other=object(300+index,path..":Charge")
        function other:IsA() return false end
        local cdo=object(400+index,path..":Default")
        function cdo:GetPropertyValue(name)
            check(name=="GEComponents","Requirement scopes must use the real effect component array")
            return array({other,component})
        end
        local class=object(500+index,path)
        function class:GetCDO() return cdo end
        objects[path]=class;components[index]=component
    end
    function backend:object(path) return objects[path] or path end
    local scope=Preparation.begin(backend,target,policy,"long")
    check(#state.scopes==1 and state.scopes[1].object==components[1]
        and state.scopes[1].application and not state.scopes[1].removal,
        "The new tier must relax application while retaining its native weapon removal requirements")
    Preparation.finish(backend,scope,false)
    check(#state.removed==1,"A scoped native effect must retain normal handle cleanup")
end
do
    local backend,state,target,_,_,policy=fixture()
    target.native_state_effects[1].replacedEffectClasses={"/Lower.Lower_C","/Unloaded.Unloaded_C"}
    local lower=object(900,"/Lower:Default")
    local class=object(901,"/Lower.Lower_C")
    function class:GetCDO() return lower end
    local find=StaticFindObject
    function StaticFindObject(path) if path=="/Lower.Lower_C" then return class end;return find(path) end
    local context=backend.resource_costs.context
    local original={level=7.5,remaining=12.25}
    state.original=original
    function context:capture_long_buffs(component,definitions)
        check(#definitions==1 and definitions[1]==lower,"Only loaded original tier definitions belong in the native capture")
        check(state.original==original,"Capture must precede mutation of the player's original tier")
        return original
    end
    function context:remove_long_buffs(component,token)
        local entry=next(backend.move_preparation) and select(2,next(backend.move_preparation))
        check(entry and entry.long_buffs==token,"The restore token must be retained before native tier removal")
        state.original=nil
        if state.failSuspend then error("native tier removal failed") end
    end
    function context:restore_long_buffs(component,token)
        check(not state.tags.Buff,"The full tier must be removed before restoring the original tier")
        if state.failRestore then error("native tier restore failed") end
        check(token.level==7.5 and token.remaining==12.25,"Restoration must retain the captured native level and remaining time")
        state.original=token
    end
    local apply=context.apply_effect_self
    function context:apply_effect_self(component,effect,level,scopes)
        check(state.original==nil,"The original tier must not stack with the full tier")
        return apply(self,component,effect,level,scopes)
    end
    local scope=Preparation.begin(backend,target,policy,"long")
    check(state.original==nil and state.tags.Buff,"The attack must use one real full tier")
    state.failRestore=true
    check(not pcall(Preparation.finish,backend,scope,false) and next(backend.move_preparation),
        "A failed original tier restoration must retain its native token for retry")
    state.failRestore=false
    scope=Preparation.begin(backend,target,policy,"long")
    check(state.original==nil and state.tags.Buff,"A new request must finish prior restoration before preparing the full tier again")
    Preparation.finish(backend,scope,false)
    check(state.original==original and next(backend.move_preparation)==nil,"Refusal must restore the original tier and release the token")
    state.failSuspend=true
    check(not pcall(Preparation.begin,backend,target,policy,"long") and state.original==original,
        "Partial native tier removal must recover from the already recorded token")
    state.failSuspend=false
    Preparation.finish(backend,Preparation.begin(backend,target,policy,"long"),true)
    local entry=select(2,next(backend.move_preparation))
    function entry.component:IsValid() return false end
    function context:discard_long_buffs(token)
        check(token==original,"An expired pawn must discard only its own captured resource state")
        state.discarded=true
    end
    Preparation.reset(backend)
    check(state.discarded and next(backend.move_preparation)==nil,
        "Pawn destruction must release native snapshots without applying effects to the old ASC")
end
print("PASS NativeStatePreparationTests "..checks)
