local Router=dofile("prototype/input-router/ue4ss/Scripts/router.lua")
local directory=os.getenv("PBZ_ALL_MOVE_CONFIG") or "dist/all-move-audit/contracts"
local checks,entrances=0,0
local function check(value,message) assert(value,message);checks=checks+1 end
for _,device in ipairs({"keyboard","pad"}) do
    local config=dofile(directory.."/all-moves-"..device..".lua")
    for _,shared in ipairs(config.bindings) do
      for _,output in ipairs(shared.outputs) do
        local binding={};for key,value in pairs(shared) do binding[key]=value end
        binding.outputs={output}
        local backend={calls=0}
        local target=config.targets[output.target]
        function backend:dispatch(actual,phase,value,id,bypass,settings)
            self.calls=self.calls+1
            check(actual==target and id==binding.id and phase=="triggered","Every move must dispatch its own compiled identity")
            check(settings==binding and bypass==binding.bypass_resources,"Resource and restriction settings must belong to the dispatched binding")
            check(settings.damage_multiplier==binding.damage_multiplier and settings.startup_speed==binding.startup_speed
                and settings.recovery_speed==binding.recovery_speed,"Damage and both phase speeds must reach dispatch together")
            return true
        end
        local router=Router.new({enabled=true,targets=config.targets,bindings={binding}},backend)
        local keys={[binding.keys[1]]=1}
        router:step(0,keys,{})
        check(backend.calls==1,"A configured move failed on its first press")
        router:step(.1,keys,{})
        check(backend.calls==2,"A default move stopped requesting while held")
        router:step(.2,{},{});router:step(.3,{},{})
        check(backend.calls==2,"A released binding continued requesting attacks")
        entrances=entrances+1
      end
    end
end
print("AllMoveBindingContractTests PASS "..checks.."; compiled entrances="..entrances)
