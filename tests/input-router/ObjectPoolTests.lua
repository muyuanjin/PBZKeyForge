local Pool = dofile("prototype/input-router/ue4ss/Scripts/object-pool.lua")
local passed, scans, listeners = 0, {}, {}
local function check(value, message) assert(value, message); passed=passed+1 end
local function object(id)
    return {IsValid=function(self) return not self.invalid end, GetAddress=function() return id end,
        GetClass=function() return {GetFullName=function() return "Class /Script/Game.Binding" end} end}
end
local loaded={EditableText={object(1)},Binding={}}
function FindAllOf(name) scans[name]=(scans[name] or 0)+1;return loaded[name] end
function NotifyOnNewObject(path,callback)
    check(not listeners[path], "registered duplicate creation listeners for a class")
    listeners[path]=callback
end
local initial=Pool.get("EditableText","/Script/UMG.EditableText")
check(#initial==1, "existing text widget was omitted")
for _=1,120 do
    check(Pool.get("EditableText","/Script/UMG.EditableText") == initial,
        "stable frames must reuse the object list")
end
check(scans.EditableText==1, "stable frames rediscovered all text widgets")
local created=object(2)
listeners["/Script/UMG.EditableText"](created)
local updated=Pool.get("EditableText","/Script/UMG.EditableText")
check(#updated==2 and updated[2]==created, "new widget did not enter the next frame's pool")
listeners["/Script/UMG.EditableText"](created)
check(#Pool.get("EditableText","/Script/UMG.EditableText")==2, "duplicate notifications duplicated an object")
initial[1].invalid=true
check(#Pool.get("EditableText","/Script/UMG.EditableText")==1, "destroyed widget remained in the pool")
check(#Pool.get("Binding")==0 and not listeners["/Script/Game.Binding"], "empty asset enumeration fabricated a class")
loaded.Binding={object(3)}
check(#Pool.get("Binding")==1 and listeners["/Script/Game.Binding"], "late-loaded asset class was not observed")
listeners["/Script/Game.Binding"](object(4))
check(#Pool.get("Binding")==2 and scans.Binding==2, "late-created asset forced another global search")
loaded.EditableText={object(5)}
local before=scans.EditableText
Pool.clear()
local rebuilt=Pool.get("EditableText","/Script/UMG.EditableText")
check(#rebuilt==1 and rebuilt[1]:GetAddress()==5 and scans.EditableText==before+1,
    "generation reset retained or failed to rediscover pooled objects")
check(listeners["/Script/UMG.EditableText"]~=nil,"generation reset removed the live creation listener")
print("ObjectPoolTests: " .. passed .. " assertions passed")
