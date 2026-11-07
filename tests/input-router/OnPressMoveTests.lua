package.path = "prototype/input-router/ue4ss/Scripts/?.lua;" .. package.path
local Router = require("router")
local checks = 0
local function check(value, message) assert(value, message); checks = checks + 1 end
for _,key in ipairs({"G","Gamepad_DPad_Down"}) do
    local requests = {move=0,ability=0,action=0,lua=0}
    local releases = 0
    local backend = {}
    function backend:dispatch(target,phase)
        if phase == "completed" then releases = releases + 1
        else requests[target.kind] = requests[target.kind] + 1 end
        return true
    end
    local targets = {move={kind="move"},ability={kind="ability",tag="Ability.Test"},
        action={kind="action",path="/Input.Test"},lua={kind="lua"}}
    local router = Router.new({targets=targets,bindings={
        {id="mixed",keys={key},trigger="press",outputs={"move","ability","action","lua"}}
    }},backend)
    router:step(0,{})
    router:step(.1,{[key]=1})
    router:step(.2,{[key]=1})
    router:step(.3,{[key]=1})
    check(requests.move == 3,"An on-press move must continue requests while held")
    check(requests.ability == 1 and requests.action == 1 and requests.lua == 1,
        "Other outputs in the same press rule must keep their single-press behavior")
    router:step(.4,{})
    router:step(.5,{})
    check(requests.move == 3,"Release must stop move requests")
    router:step(.6,{[key]=1})
    check(requests.move == 4 and requests.ability == 2 and requests.action == 2 and requests.lua == 2,
        "A fresh press must restart both held moves and single-press outputs")
end
print("OnPressMoveTests PASS " .. checks)
