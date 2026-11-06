local config=dofile("dist/key-enhancement-test-config.lua")
assert(config.enabled and config.marker=="preserved")
assert(config.unified_native and #config.native_actions == 40)
local gameplay={};for _,path in ipairs(config.native_actions) do gameplay[path]=true end
assert(gameplay["/SGameLocomotionDriver/Input/IA_KeyboardMoveInput"] and gameplay["/SGameLocomotionDriver/Input/IA_Sprint"])
assert(not gameplay["/Game/PBZContent/Input/InputActions/IA_Map"])
assert(#config.bindings==1 and #config.suppressions==2 and #config.hooks==1)
local binding=config.bindings[1]
assert(type(binding.evaluate)=="function" and type(binding.when)=="function")
local kinds={}
for _, output in ipairs(binding.outputs) do
    local target=config.targets[output.target]
    kinds[target.kind]=target
end
assert(kinds.property.value==false)
assert(kinds["function"].args[1]=='quote" slash\\ line\n')
assert(type(kinds.lua.run)=="function")
assert(kinds.ability.tag=="Ability.Action.Test" and kinds.ability.input_action=="/Input/IA_Jump.IA_Jump")
assert(kinds.event.payload.EventMagnitude==2 and config.hooks[1].evaluate()==false)
assert(config.ability_suppressions[1]=="Ability.Action.Hun.SpecialDash"
    and config.ability_suppressions[2]=="Ability.Action.Hun.DeflectDash")
for _, output in ipairs(binding.outputs) do
    local target=config.targets[output.target]
    if target.kind=="action" then assert(output.value.x==-1 and output.value.y==0) end
    if target.kind=="mapping" then assert(output.value.x==0 and output.value.y==-2 and output.value.z==0) end
end
local backend={events={}}
function backend:allowed(condition,context) return condition(context) end
function backend:dispatch(target,phase,value,id)
    self.events[#self.events+1]={target=target,phase=phase,value=value,id=id}
    if target.kind=="lua" then assert(target.run(self,{binding=id})=="replace") end
    return true
end
local Router=dofile("prototype/input-router/ue4ss/Scripts/router.lua")
local router=Router.new(config,backend)
router:step(0,{K=1},{gameplay=true})
assert(#backend.events==7)
backend.events={}
router:step(.1,{}, {gameplay=true})
assert(#backend.events==1 and backend.events[1].phase=="completed")
print("CompiledConfigurationTests: generated Lua, custom evaluator, typed outputs and release passed")
