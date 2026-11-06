local Router = dofile("prototype/input-router/ue4ss/Scripts/router.lua")
local count = 0
local function check(value, message) assert(value, message); count = count + 1 end
local function rig(config, binding)
    config.targets = {edited={kind="lua",path="edited"}, single={kind="lua",path="single"}}
    binding.outputs, binding.priority = {"edited"}, 10
    config.bindings = {binding, {id="single",keys={"J"},trigger="press",outputs={"single"}}}
    local backend = {events={}}
    function backend:key_dimension() return 2 end
    function backend:reserve_inputs(keys) self.reserved=keys end
    function backend:dispatch(target) self.events[#self.events+1]=target.path end
    return Router.new(config,backend), backend
end
local axisConfig = dofile("dist/expanded-binding-ux-check/axis-config.lua")
local axis, backend = rig(axisConfig,axisConfig.bindings[1])
axis:step(0,{Gamepad_Left2D=1,["Gamepad_Left2D@1"]=1,J=1},{})
check(table.concat(backend.events,",") == "edited,single", "Edited axis retained a hidden sequence requirement or blocked J")
check(backend.reserved.Gamepad_Left2D and not backend.reserved.J and not backend.reserved.O, "Axis consumed its previous sequence keys")
local watchConfig = dofile("dist/expanded-binding-ux-check/watch-config.lua")
local binding
for _, candidate in ipairs(watchConfig.bindings) do if candidate.evaluate then binding=candidate end end
local watch, backend = rig(watchConfig,binding)
watch:step(0,{J=1},{})
check(table.concat(backend.events,",")=="edited", "Watch-only editor configuration failed to consume J")
check(backend.reserved.J, "Watch-only editor configuration did not reserve its monitored input")
backend.events={};watch:step(.1,{},{});watch:step(.2,{J=1},{})
check(table.concat(backend.events,",")=="edited", "Edited watch input failed to rearm after physical release")
print("EditedBindingTests PASS " .. count)
