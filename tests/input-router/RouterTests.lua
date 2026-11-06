local Router = dofile("prototype/input-router/ue4ss/Scripts/router.lua")
local count = 0
local function check(ok, message)
    assert(ok, message)
    count=count+1
end
local function rig(bindings, targets)
    local backend={ events={} }
    function backend:allowed(when, context) return context and context[when] == true end
    function backend:dispatch(target, phase, value, id)
        self.events[#self.events+1]={target=target.path or target.tag,phase=phase,value=value,id=id}
    end
    targets=targets or { a={kind="action",path="a",value_type="bool"} }
    local router=Router.new({targets=targets,bindings=bindings},backend)
    local function step(t, snapshot, context)
        backend.events={}
        router:step(t,snapshot,context)
        return backend.events
    end
    return router,backend,step
end

do
    local bindings={}
    for i=1,128 do bindings[i]={id="b"..i,keys={"K"..i},outputs={"a"}} end
    local router,backend,step=rig(bindings)
    check(#router:keys()==128,"128 bindings must not use save slots")
    check(step(0,{K1=1})[1].phase=="started","first source begins action")
    check(step(0.1,{K128=1})[1].phase=="triggered","changing sources must not release held action")
    check(step(0.2,{})[1].phase=="completed","last source release completes action")
    check(#step(0.3,{})==0,"completion must not repeat")
end

do
    local router,backend,step=rig({
        {id="combo",keys={"LB","A"},trigger="press",consume={"A"},outputs={"dash"}},
        {id="guard",keys={"LB"},outputs={"a"}},
        {id="jump",keys={"A"},trigger="press",outputs={"jump"}}
    },{a={kind="action",path="guard",value_type="bool"},
        dash={kind="ability",tag="dash"},jump={kind="action",path="jump",value_type="bool"}})
    local e=step(0,{LB=1,A=1})
    check(#e==2,"chord must consume jump and preserve guard")
    check(e[1].target=="dash","chord dispatches intended ability")
    e=step(0.1,{LB=1,A=1})
    check(#e==1 and e[1].target=="guard","held chord must not retrigger ability or jump")
    e=step(0.2,{A=1})
    check(#e==1 and e[1].phase=="completed","releasing chord modifier must not produce a delayed jump")
end

do
    local router,backend,step=rig({{id="hold",keys={"K"},trigger="hold",seconds=0.3,outputs={"a"}}})
    check(#step(0,{K=1})==0,"hold must wait")
    check(#step(0.29,{K=1})==0,"hold threshold")
    check(step(0.3,{K=1})[1].phase=="started","hold begins at threshold")
    check(step(0.4,{})[1].phase=="completed","hold release")
end

do
    local _,_,step=rig({{id="tap",keys={"K"},trigger="tap",seconds=0.25,outputs={"a"}}})
    step(0,{K=1})
    check(step(0.1,{})[1].phase=="started","short tap")
    check(step(0.2,{})[1].phase=="completed","tap action must release next frame")
    step(0.3,{K=1})
    check(#step(0.6,{})==0,"long hold must not count as tap")
end

do
    local _,_,step=rig({{id="double",keys={"K"},trigger="multi_tap",count=2,interval=0.3,outputs={"a"}}})
    check(#step(0,{K=1})==0,"first tap waits")
    step(0.05,{})
    check(step(0.1,{K=1})[1].phase=="started","second tap fires")
    step(0.15,{})
    check(#step(1,{K=1})==0,"expired tap count must reset")
end

do
    local _,_,step=rig({{id="sequence",trigger="sequence",steps={"A","B","A"},interval=0.3,outputs={"a"}}})
    step(0,{A=1}); step(0.1,{B=1})
    check(step(0.2,{A=1})[1].phase=="started","ordered sequence fires")
    step(0.3,{}); step(0.4,{A=1}); step(1,{B=1})
    check(#step(1.1,{A=1})==0,"sequence timeout must discard partial progress")
end

do
    local router,backend,step=rig({{id="axis",axis="LeftX",keys={"Shift"},trigger="axis",threshold=0.1,
        outputs={{target="a",value={x=0,y=1}}}}},{a={kind="action",path="move",clamp=1}})
    check(#step(0,{LeftX=-0.8})==0,"axis chord requires modifier")
    local e=step(0.1,{LeftX=-0.8,Shift=1})
    check(e[1].value.y==-0.8,"signed analog magnitude and vector swizzle")
    check(step(0.2,{LeftX=0.01,Shift=1})[1].phase=="completed","axis deadzone releases")
end

do
    local router,backend,step=rig({{id="guarded",keys={"K"},when="playing",outputs={"a"}}},
        {a={kind="action",path="movement",resume_continuous=true}})
    check(#step(0,{K=1},{playing=false})==0,"guard rejects input")
    check(step(0.1,{K=1},{playing=true})[1].phase=="started","guard can enable held action")
    check(step(0.2,{K=1},{playing=false})[1].phase=="canceled","guard transition must cancel without firing release-trigger actions")
    step(0.3,{K=1},{playing=true})
    backend.events={}; router:reset()
    check(#backend.events==1 and backend.events[1].phase=="completed","reload or focus loss releases live output")
    check(#step(0.4,{}, {playing=true})==0,"reset leaves no stale output")
end

do
    local _,_,step=rig({{id="toggle",keys={"K"},trigger="toggle",outputs={"a"}}})
    check(step(0,{K=1})[1].phase=="started","toggle on")
    check(step(0.1,{})[1].phase=="triggered","toggle survives release")
    check(step(0.2,{K=1})[1].phase=="completed","toggle off")
end

do
    local _,_,step=rig({{id="pulse",keys={"K"},trigger="pulse",interval=0.2,outputs={"a"}}})
    check(step(0,{K=1})[1].phase=="started","pulse immediate")
    check(step(0.1,{K=1})[1].phase=="completed","pulse has a falling edge")
    check(step(0.2,{K=1})[1].phase=="started","pulse repeats")
end

do
    local _,_,step=rig({{id="multi",keys={"K"},trigger="press",outputs={"a","event","call"}}},
        {a={kind="action",path="a"},event={kind="event",tag="event"},call={kind="function",path="call"}})
    check(#step(0,{K=1})==3,"one key can trigger several target types")
    local e=step(0.1,{K=1})
    check(#e==1 and e[1].phase=="completed","function and event calls must not repeat while held")
end

do
    local ok=pcall(Router.new,{targets={},bindings={{id="bad",keys={"K"},outputs={"unknown"}}}},{})
    check(not ok,"unknown output must fail configuration loading")
    local _,_,step=rig({{id="custom",watch={"K"},evaluate=function(now,snapshot,context,st)
        return {active=snapshot.K==1 and context.ready,pulse=true,value=1}
    end,outputs={"a"}}})
    check(step(0,{K=1},{ready=true})[1].phase=="started","custom stateful trigger extension")
end

do
    local config = dofile("prototype/input-router/examples/demo.lua")
    local _,_,step = rig(config.bindings, config.targets)
    local events = step(0, {F10=1})
    check(#events == 2, "Default guard key must start deflect and stance together")
    events = step(0.1, {F10=1})
    check(#events == 2, "Held guard must sustain stance and release the deflect pulse")
    check(step(0.2, {})[1].phase == "completed", "Guard release must end the stance output")
end

do
    local _,_,step = rig({{id="vector", keys={"Stick"}, watch={"X","Y"},
        outputs={"a"}, evaluate=function(now, snapshot)
            return {active=true, value={x=snapshot.X,y=snapshot.Y,z=0}}
        end}}, {a={kind="action",path="move"}})
    local events = step(0,{X=0.6,Y=-0.8})
    check(#events==1 and events[1].value.x==0.6 and events[1].value.y==-0.8,
        "One vector sample must reach one modifier/trigger instance")
end

do
    local _,_,step = rig({
        {id="first",keys={"A"},outputs={"a"}},
        {id="second",keys={"B"},outputs={"alias"}}
    }, {a={kind="action",path="same"},alias={kind="action",path="same"}})
    local e=step(0,{A=1,B=1})
    check(#e==1 and e[1].value.x==2,"Aliases of one native action must aggregate")
    e=step(0.1,{B=1})
    check(#e==1 and e[1].phase=="triggered","Releasing one alias must not release another")
    e=step(0.2,{})
    check(#e==1 and e[1].phase=="completed","Native action must complete once")
end

do
    local router,backend,step=rig({{id="a",keys={"A"},outputs={"a"}}})
    step(0,{A=1})
    local dispatch=backend.dispatch
    function backend:dispatch(target,phase,value,id)
        if self.failRelease and phase=="completed" then return false end
        return dispatch(self,target,phase,value,id)
    end
    backend.failRelease=true
    check(not pcall(function() router:reset() end),"Failed release must fail reset")
    check(next(router.previous)~=nil,"Failed reset must retain pending release")
    backend.failRelease=false
    backend.events={}
    router:reset()
    check(#backend.events==1 and backend.events[1].phase=="completed" and next(router.previous)==nil,
        "Retry must release the retained native action")
end

do
    local _,backend,step=rig({
        {id="first",keys={"A"},outputs={"a"}},
        {id="second",keys={"B"},outputs={"b"}}
    },{a={kind="action",path="same",pipeline="first"},b={kind="action",path="same",pipeline="second"}})
    function backend:pipeline_identity(target) return target.pipeline end
    check(#step(0,{A=1,B=1})==2,"Distinct native modifier pipelines must be preserved")
    local e=step(.1,{B=1})
    check(#e==2 and e[1].phase=="triggered" and e[2].phase=="completed","The retired pipeline must receive zero while its sibling remains active")
    e=step(.2,{})
    check(#e==1 and e[1].phase=="completed","Final native completion must not duplicate pipelines")
end

do
    local config=dofile("prototype/input-router/ue4ss/Scripts/router-config.lua")
    check(config.enabled==false and #config.bindings==0 and next(config.targets)==nil,
        "Installed default must contain no demo rules")
end

do
    local _,backend,step=rig({
        {id="chord",keys={"LB","A"},trigger="press",consume={"A"},priority=10,outputs={"chord"}},
        {id="custom-single",keys={"A"},trigger="down",outputs={"a"}}
    },{chord={kind="ability",tag="chord"},a={kind="action",path="jump"}})
    check(#step(0,{LB=1,A=1}) == 1, "consuming chord must block an added custom single-key relation")
    check(#step(.1,{A=1}) == 0, "a custom single-key relation fired late after the chord modifier was released")
    step(.2,{})
    local events=step(.3,{A=1})
    check(#events == 1 and events[1].target == "jump", "fresh custom single-key input remained consumed after physical release")
end

for _,kind in ipairs({"release","tap"}) do
    local _,_,step=rig({
        {id="chord",keys={"LB","A"},trigger="press",consume={"A"},priority=10,outputs={"chord"}},
        {id="single",keys={"A"},trigger=kind,seconds=.5,outputs={"a"}}
    },{chord={kind="ability",tag="chord"},a={kind="action",path="single"}})
    step(0,{LB=1,A=1});step(.1,{A=1})
    check(#step(.2,{}) == 0, "physical release of a consumed key replayed its " .. kind .. " relation")
    step(.3,{A=1})
    check(#step(.4,{}) == 1, "a fresh " .. kind .. " relation could not rearm after consume")
end

do
    local _,backend,step=rig({
        {id="override-axis",axis="RightX",trigger="axis",consume=true,priority=10,outputs={"a"}},
        {id="original-axis",axis="RightX",trigger="axis",outputs={"other"}}
    },{a={kind="action",path="new"},other={kind="action",path="old"}})
    function backend:reserve_inputs(keys) self.reserved=keys end
    local events=step(0,{RightX=.8})
    check(#events==1 and events[1].target=="new","Axis consumption must block a lower priority axis binding")
    check(backend.reserved.RightX==true,"Axis consumption must reserve the native axis key")
end

do
    local backend={events={}}
    function backend:key_dimension() return 2 end
    function backend:dispatch(_,phase,value) self.events[#self.events+1]={phase=phase,value=value} end
    local config={targets={move={kind="action",path="move"}},bindings={
        {id="stick",axis="Gamepad_Left2D",trigger="axis",outputs={"move"}}}}
    local router=Router.new(config,backend)
    check(#router:keys()==3,"Vector axes must sample every signed component")
    router:step(0,{Gamepad_Left2D=.8,["Gamepad_Left2D@1"]=-.6,["Gamepad_Left2D@2"]=.8},{})
    check(backend.events[1].value.x==-.6 and backend.events[1].value.y==.8,
        "A 2D axis must retain its direction and signed components")
    config.bindings[1].outputs={{target="move",value={x=0,y=-1}}}
    router=Router.new(config,backend); backend.events={}
    router:step(0,{Gamepad_Left2D=.8,["Gamepad_Left2D@1"]=-.6,["Gamepad_Left2D@2"]=.8},{})
    check(backend.events[1].value.x==0 and backend.events[1].value.y==-.8,
        "An explicit vector must apply its direction to the axis magnitude")
end

do
    local _,backend,step=rig({
        {id="sequence",keys={},trigger="sequence",steps={{"LeftControl","J"},{"O"}},consume=true,priority=10,outputs={"a"}},
        {id="single",keys={"J"},outputs={"b"}}
    },{a={kind="action",path="a"},b={kind="action",path="b"}})
    function backend:reserve_inputs(keys) self.reserved=keys end
    local events=step(0,{LeftControl=1,J=1})
    check(#events==0 and backend.reserved.LeftControl and backend.reserved.J and backend.reserved.O,
        "A consuming sequence must reserve every step's keys and block the single-key action")
    step(.1,{})
    events=step(.2,{O=1})
    check(#events==1 and events[1].target=="a", "Consuming sequence did not complete after reserving its steps")
end

do
    local _,backend,step=rig({
        {id="higher",keys={"J"},consume=true,priority=20,outputs={"a"}},
        {id="sequence",keys={},trigger="sequence",steps={"J","O"},consume=true,outputs={"b"}}
    },{a={kind="action",path="a"},b={kind="action",path="b"}})
    function backend:reserve_inputs(keys) self.reserved=keys end
    step(0,{J=1})
    check(backend.reserved.J and not backend.reserved.O, "A sequence bypassed a higher priority rule that consumed its step")
    step(.1,{})
    local events=step(.2,{O=1})
    check(#events==0, "A blocked sequence retained progress from a consumed step")
end

do
    local _,backend,step=rig({
        {id="positive",keys={"A"},outputs={{target="a",value=1}}},
        {id="negative",keys={"B"},outputs={{target="a",value=-1}}},
        {id="sibling",keys={"C"},outputs={"b"}}
    },{a={kind="action",path="same",pipeline="first"},b={kind="action",path="same",pipeline="second"}})
    function backend:pipeline_identity(target) return target.pipeline end
    check(#step(0,{A=1,C=1}) == 2, "both independent pipelines must start")
    local events=step(.1,{A=1,B=1,C=1})
    check(#events == 2 and events[2].phase == "completed", "a pipeline whose contributions cancel must receive zero while its sibling continues")
    check(#step(.2,{A=1,B=1,C=1}) == 1, "canceled vector sum must not leave an active pipeline or repeat completion")
end

do
    local target={kind="action",path="hold",pipeline="first"}
    local router,backend,step=rig({{id="held",keys={"E"},trigger="hold",seconds=.5,outputs={"a"}}},{a=target})
    step(0,{E=1});step(.4,{E=1})
    local state=router.rules[1].state
    router=router:reconfigure({targets={a=target,extra={kind="action",path="extra"}},bindings={
        {id="held",keys={"E"},trigger="hold",seconds=.5,outputs={"a"}},
        {id="new",keys={"F"},trigger="press",outputs={"extra"}}
    }},{E=1},{})
    check(router.rules[1].state == state, "adding an unrelated rule must preserve a running hold timer")
    backend.events={};router:step(.6,{E=1},{})
    check(#backend.events == 1 and backend.events[1].phase == "started", "unrelated reconfiguration interrupted the held input threshold")
    backend.events={};router=router:reconfigure({targets={},bindings={}},{E=1},{})
    check(#backend.events == 1 and backend.events[1].phase == "canceled" and next(router.previous) == nil, "retired pipeline did not cancel exactly once during reconfiguration")
end

do
    local _,_,step=rig({{id="conditional",keys={"E"},when="allowed",outputs={"a","move"}}},
        {a={kind="action",path="execute"},move={kind="action",path="move",resume_continuous=true}})
    check(#step(0,{E=1},{allowed=true}) == 2, "conditional input must start both eligible outputs")
    local events=step(.1,{E=1},{allowed=false})
    check(#events == 2 and events[1].phase == "canceled" and events[2].phase == "canceled", "condition cancellation sent physical release")
    events=step(.2,{E=1},{allowed=true})
    check(#events == 1 and events[1].target == "move", "condition recovery replayed a held one-shot action")
    step(.3,{}, {allowed=true})
    check(#step(.4,{E=1},{allowed=true}) == 2, "conditional one-shot input did not rearm after physical release")
end

do
    local evaluations = 0
    local _,_,step=rig({
        {id="higher", keys={"LB","A"}, trigger="press", consume={"A"}, priority=10, outputs={"higher"}},
        {id="script", watch={"A"}, evaluate=function(_, snapshot)
            evaluations = evaluations + 1
            local active = (snapshot.A or 0) > 0
            return {active=active, pulse=active, value=1}
        end, outputs={"lower"}},
        {id="unrelated", watch={"B"}, evaluate=function(_, snapshot)
            local active = (snapshot.B or 0) > 0
            return {active=active, pulse=active, value=1}
        end, outputs={"other"}}
    }, {higher={kind="lua",path="higher"}, lower={kind="lua",path="lower"}, other={kind="lua",path="other"}})
    local events=step(0,{LB=1,A=1,B=1})
    check(#events == 2 and events[1].target == "higher" and events[2].target == "other" and evaluations == 0,
        "watch-only script evaluated or emitted output despite higher priority consumption")
    check(#step(.1,{A=1}) == 0 and evaluations == 0, "watch-only script bypassed consumption while its key was still held")
    check(#step(.2,{}) == 0, "consumed script emitted a release output")
    events=step(.3,{A=1})
    check(#events == 1 and events[1].target == "lower", "watch-only script did not rearm after physical release")
end

do
    local _,_,step=rig({
        {id="script", watch={"A"}, consume=true, priority=10, evaluate=function(_,snapshot)
            local active=(snapshot.A or 0)>0
            return {active=active,pulse=active,reserve=active,value=1}
        end, outputs={"script"}},
        {id="single",keys={"A"},trigger="press",outputs={"single"}}
    },{script={kind="lua",path="script"},single={kind="lua",path="single"}})
    local events=step(0,{A=1})
    check(#events == 1 and events[1].target == "script", "consume-all ignored a script's watched input")
end


do
    local _,_,step=rig({
        {id="tap",input_group="split",keys={"J"},trigger="tap",seconds=.3,consume=true,outputs={"tap"}},
        {id="hold",input_group="split",keys={"J"},trigger="hold",seconds=.3,consume=true,outputs={"hold"}},
        {id="native",keys={"J"},trigger="press",outputs={"native"}}
    },{tap={kind="event",tag="tap"},hold={kind="event",tag="hold"},native={kind="event",tag="native"}})
    check(#step(0,{J=1})==0,"split input must reserve without triggering native input")
    local e=step(.1,{})
    check(#e==1 and e[1].target=="tap","consuming tap prevented paired tap")
    step(.2,{J=1})
    e=step(.51,{J=1})
    check(#e==1 and e[1].target=="hold","consuming tap prevented paired hold")
    check(#step(.6,{})==0,"held split input also emitted tap")
end

do
    local _,_,step=rig({{id="positive",axis="X",axis_direction="positive",trigger="axis",outputs={"a"}}})
    check(#step(0,{X=-1})==0,"positive axis accepted negative direction")
    check(step(.1,{X=1})[1].phase=="started","positive axis rejected positive direction")
end

print("RouterTests: " .. count .. " assertions passed")
