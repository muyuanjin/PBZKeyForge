#define PBZ_CONTEXT_API
#include "../../prototype/input-router/native/input-observation.cpp"
#include <cassert>
#include <iostream>
#include <map>
#include <string>

namespace
{
    using namespace RC::Unreal;
    using PBZInputObservation::Array;
    using PBZInputObservation::Map;
    struct Type { std::map<std::wstring,FProperty*> fields; };
    struct Property : FProperty
    {
        int offset, size;
        TObjectPtr<UScriptStruct> structure{};
        FProperty* inner = nullptr;
        FScriptMapLayout map{};
        Property(int offset,int size) : offset(offset),size(size) {}
    };
    std::map<void*,Type*> objectTypes;
    std::vector<std::wstring> names;
    long long parameter = 0;
    std::vector<long long> parameters;
    size_t parameterIndex = 0;
    long long returnedInteger = 0;
    int reflections = 0;
    struct Api
    {
        void* (*newstate)(); void (*close)(void*); void (*pushinteger)(void*,long long);
        void (*pushboolean)(void*,int); const char* (*pushstring)(void*,const char*,size_t);
        void (*pushfunction)(void*,int(*)(void*),int); long long (*integer)(void*,int,int*);
        int (*boolean)(void*,int); const char* (*string)(void*,int,size_t*);
        void (*rotate)(void*,int,int); void (*settop)(void*,int); int (*gettop)(void*);
        int (*pcall)(void*,int,int,int,intptr_t,void*);
    } api;
    void* luaState(const RC::LuaMadeSimple::Lua* lua) { return *reinterpret_cast<void* const*>(lua); }
    std::string error;
    int nameSize = 8;
    uint64_t nameId(const FName& name) { uint64_t id;std::memcpy(&id,&name,sizeof(id));return id; }
    struct Settings { uint64_t name = 10; };
    struct Trigger { float threshold = .5f, hold = .3f, lastValue = 0; };
    struct Action { Settings* settings; Array triggers; };
    struct Row { Action* action; uint64_t key; unsigned char behavior; Settings* settings; Array modifiers, triggers; };
    struct Defaults { Array mappings; };
    struct Context { Defaults defaults; };
    struct Input { Array mappings; Map contexts; };
    Property* add(Type& type,const wchar_t* name,int offset,int size)
    { auto* p=new Property(offset,size);type.fields[name]=p;return p; }
    template<class T,size_t N> Array array(T (&items)[N])
    { return {reinterpret_cast<unsigned char*>(items),static_cast<int>(N),static_cast<int>(N)}; }
}

namespace RC::Unreal
{
    FName::FName(const wchar_t* name,EFindName,void*)
    { uint64_t id=names.size();names.push_back(name);std::memcpy(this,&id,sizeof(id)); }
    int FName::StaticSize() { return nameSize; }
    int& FProperty::GetElementSize() { return static_cast<Property*>(this)->size; }
    int& FProperty::GetOffset_Internal() { return static_cast<Property*>(this)->offset; }
    FProperty* UObject::GetPropertyByNameInChain(const wchar_t* name)
    { ++reflections;auto* type=objectTypes.at(this);auto it=type->fields.find(name);return it==type->fields.end()?nullptr:it->second; }
    FProperty* UStruct::FindProperty(FName name)
    { ++reflections;auto& fields=reinterpret_cast<Type*>(this)->fields;auto it=fields.find(names.at(nameId(name)));return it==fields.end()?nullptr:it->second; }
    TObjectPtr<UScriptStruct>& FStructProperty::GetStruct() { return reinterpret_cast<Property*>(this)->structure; }
    FProperty*& FArrayProperty::GetInner() { return reinterpret_cast<Property*>(this)->inner; }
    FScriptMapLayout& FMapProperty::GetMapLayout() { return reinterpret_cast<Property*>(this)->map; }
    FProperty*& FMapProperty::GetValueProp() { return reinterpret_cast<Property*>(this)->inner; }
}
namespace RC::LuaMadeSimple
{
    long long Lua::get_integer(int index) const
    {
        if (auto* state=luaState(this))
        {
            auto result=api.integer(state,index,nullptr);api.rotate(state,index,-1);api.settop(state,-2);return result;
        }
        return parameters.empty()?parameter:parameters.at(parameterIndex++);
    }
    void Lua::set_integer(long long value) const
    { if (auto* state=luaState(this)) api.pushinteger(state,value); else returnedInteger=value; }
    void Lua::set_string(const char* text,size_t size) const
    { if (auto* state=luaState(this)) api.pushstring(state,text,size); else error.assign(text,size); }
}

int main()
{
    using namespace PBZInputObservation;
    Type inputType,rowType,keyType,actionType,settingsType,triggerType,contextType,defaultsType;
    auto* rowLayout=new Property(0,sizeof(Row));rowLayout->structure.Ptr=reinterpret_cast<UScriptStruct*>(&rowType);
    auto* mappings=add(inputType,L"EnhancedActionMappings",offsetof(Input,mappings),sizeof(Array));mappings->inner=rowLayout;
    auto* contexts=add(inputType,L"AppliedInputContextData",offsetof(Input,contexts),sizeof(Map));contexts->map.SetLayout.Size=sizeof(void*);
    add(rowType,L"Action",offsetof(Row,action),sizeof(void*));
    auto* key=add(rowType,L"Key",offsetof(Row,key),sizeof(uint64_t));key->structure.Ptr=reinterpret_cast<UScriptStruct*>(&keyType);
    add(keyType,L"KeyName",0,sizeof(uint64_t));
    add(rowType,L"SettingBehavior",offsetof(Row,behavior),1);
    add(rowType,L"PlayerMappableKeySettings",offsetof(Row,settings),sizeof(void*));
    add(rowType,L"Modifiers",offsetof(Row,modifiers),sizeof(Array));
    add(rowType,L"Triggers",offsetof(Row,triggers),sizeof(Array));
    add(actionType,L"PlayerMappableKeySettings",offsetof(Action,settings),sizeof(void*));
    add(actionType,L"Triggers",offsetof(Action,triggers),sizeof(Array));
    add(settingsType,L"Name",offsetof(Settings,name),sizeof(uint64_t));
    add(triggerType,L"ActuationThreshold",offsetof(Trigger,threshold),sizeof(float));
    add(triggerType,L"HoldTimeThreshold",offsetof(Trigger,hold),sizeof(float));
    auto* defaults=add(contextType,L"DefaultKeyMappings",offsetof(Context,defaults),sizeof(Defaults));defaults->structure.Ptr=reinterpret_cast<UScriptStruct*>(&defaultsType);
    auto* templateRows=add(defaultsType,L"Mappings",offsetof(Defaults,mappings),sizeof(Array));templateRows->inner=rowLayout;
    Settings settings;
    Trigger trigger;
    void* triggerPointers[]={&trigger};
    Action action{&settings,{}};
    Row rows[]={{&action,5,0,nullptr,{},array(triggerPointers)}};
    Row templates[]={rows[0]};
    Context context{{array(templates)}};
    void* contextPointers[]={&context};
    Input input{array(rows),{}};
    input.contexts.data=reinterpret_cast<unsigned char*>(contextPointers);input.contexts.num=input.contexts.max=input.contexts.numBits=1;input.contexts.inlineBits[0]=1;
    objectTypes[&input]=&inputType;objectTypes[&action]=&actionType;objectTypes[&settings]=&settingsType;
    objectTypes[&trigger]=&triggerType;objectTypes[&context]=&contextType;
    observe(&input);
    for (int i=0;i<2000;++i) { trigger.lastValue=static_cast<float>(i);assert(snapshot.unchanged(&input)); }
    auto changed=[&](auto& value,auto replacement) { auto original=value;value=replacement;assert(!snapshot.unchanged(&input));value=original;assert(snapshot.unchanged(&input)); };
    changed(rows[0].key,uint64_t(9));changed(templates[0].key,uint64_t(9));changed(settings.name,uint64_t(19));
    changed(trigger.threshold,0.f);changed(trigger.hold,0.f);changed(rows[0].behavior,static_cast<unsigned char>(2));
    changed(rows[0].settings,&settings);changed(rows[0].action,static_cast<Action*>(nullptr));
    changed(action.settings,static_cast<Settings*>(nullptr));
    changed(action.triggers,array(triggerPointers));changed(rows[0].modifiers,array(triggerPointers));
    changed(input.contexts.num,0);changed(input.contexts.inlineBits[0],uint32_t(0));
    changed(contextPointers[0],static_cast<void*>(nullptr));changed(triggerPointers[0],static_cast<void*>(nullptr));
    auto* guarded=VirtualAlloc(nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);assert(guarded);
    rows[0].triggers={static_cast<unsigned char*>(guarded),1,1};*static_cast<void**>(guarded)=&trigger;
    observe(&input);
    rows[0].triggers={};VirtualFree(guarded,0,MEM_RELEASE);
    assert(!snapshot.unchanged(&input));
    observe(&input);assert(snapshot.unchanged(&input));
    auto original=input.mappings.data;input.mappings.data=nullptr;assert(!snapshot.unchanged(&input));input.mappings.data=original;
    auto* otherInput=&context;assert(!snapshot.unchanged(otherInput));
    parameter=reinterpret_cast<long long>(&input);nameSize=12;
    assert(pbz_input_mappings_observe(nullptr)==1 && !error.empty() && !snapshot.input);
    nameSize=8;assert(pbz_input_mappings_observe(nullptr)==0 && pbz_input_mappings_unchanged(nullptr)==1);
    input.mappings={};input.contexts={};observe(&input);assert(snapshot.unchanged(&input));
    // InputActionBinding.Bindings is an array; conditional blocks are a sparse map.
    struct AbilityTrigger { uint64_t tag; int untouched; };
    struct Block { Array actions, combos, interrupts; };
    struct Binding { Block normal; Map conditions; };
    struct BindingAsset { Array bindings; };
    struct Condition { uint64_t key; Block value; };
    Type assetType,bindingType,blockType,abilityTriggerType,tagType;
    auto* blockLayout=new Property(0,sizeof(Block));blockLayout->structure.Ptr=reinterpret_cast<UScriptStruct*>(&blockType);
    auto* abilityTriggerLayout=new Property(0,sizeof(AbilityTrigger));abilityTriggerLayout->structure.Ptr=reinterpret_cast<UScriptStruct*>(&abilityTriggerType);
    auto* bindingLayout=new Property(0,sizeof(Binding));bindingLayout->structure.Ptr=reinterpret_cast<UScriptStruct*>(&bindingType);
    add(assetType,L"Bindings",offsetof(BindingAsset,bindings),sizeof(Array))->inner=bindingLayout;
    add(bindingType,L"NormalData",offsetof(Binding,normal),sizeof(Block))->structure=blockLayout->structure;
    auto* conditional=add(bindingType,L"TagConditionData",offsetof(Binding,conditions),sizeof(Map));
    conditional->inner=blockLayout;conditional->map.ValueOffset=offsetof(Condition,value);conditional->map.SetLayout.Size=sizeof(Condition);
    add(blockType,L"Actions",offsetof(Block,actions),sizeof(Array))->inner=abilityTriggerLayout;
    add(blockType,L"Combos",offsetof(Block,combos),sizeof(Array))->inner=abilityTriggerLayout;
    add(blockType,L"InterruptLower",offsetof(Block,interrupts),sizeof(Array))->inner=abilityTriggerLayout;
    add(abilityTriggerType,L"TriggerTag",offsetof(AbilityTrigger,tag),8)->structure.Ptr=reinterpret_cast<UScriptStruct*>(&tagType);
    add(tagType,L"TagName",0,8);
    AbilityTrigger normalRows[]={{1,0},{2,0}},conditionRows[]={{3,0},{4,0}};
    Block normal{array(normalRows),array(normalRows),array(normalRows)};
    Condition conditionSlots[3]{};
    conditionSlots[0].value=conditionSlots[2].value={array(conditionRows),array(conditionRows),array(conditionRows)};
    uint32_t bits=5;
    Map conditions{};conditions.data=reinterpret_cast<unsigned char*>(conditionSlots);
    conditions.num=conditions.max=conditions.numBits=3;conditions.externalBits=&bits;
    Binding bindings[]={{normal,conditions}};
    BindingAsset asset{array(bindings)},secondAsset{array(bindings)};
    objectTypes[&asset]=objectTypes[&secondAsset]=&assetType;
    auto token=observeAbilities(&asset,0),secondToken=observeAbilities(&secondAsset,0);
    assert(token && secondToken!=token && abilitySnapshots.size()==2);
    reflections=0;
    for (int i=0;i<2000;++i)
    {
        normalRows[0].untouched=i;
        assert(abilitySnapshots.at(token).unchanged(&asset));
    }
    assert(reflections==0);
    for (auto* tag:{&normalRows[0].tag,&normalRows[1].tag,&conditionRows[0].tag,&conditionRows[1].tag})
    {
        ++*tag;assert(!abilitySnapshots.at(token).unchanged(&asset));
        assert(observeAbilities(&asset,token)==token);
    }
    bits=1;assert(!abilitySnapshots.at(token).unchanged(&asset));observeAbilities(&asset,token);
    bits=5;assert(!abilitySnapshots.at(token).unchanged(&asset));observeAbilities(&asset,token);
    // A removed sparse slot is never inspected, including its unusable arrays.
    bits=4;conditionSlots[0].value.actions.data=reinterpret_cast<unsigned char*>(1);
    observeAbilities(&asset,token);assert(abilitySnapshots.at(token).unchanged(&asset));
    guarded=VirtualAlloc(nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);assert(guarded);
    bindings[0].normal.actions={static_cast<unsigned char*>(guarded),1,1};
    *static_cast<AbilityTrigger*>(guarded)={17,0};observeAbilities(&asset,token);
    bindings[0].normal.actions={};VirtualFree(guarded,0,MEM_RELEASE);
    assert(!abilitySnapshots.at(token).unchanged(&asset));
    // All owning headers must be tested before touching a retired allocation.
    auto retired=[&](bool root) {
        guarded=VirtualAlloc(nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);assert(guarded);
        if (root) { *static_cast<Binding*>(guarded)=bindings[0];asset.bindings={static_cast<unsigned char*>(guarded),1,1}; }
        else { *static_cast<Condition*>(guarded)=conditionSlots[2];bindings[0].conditions.data=static_cast<unsigned char*>(guarded);bindings[0].conditions.num=1;bits=1; }
        observeAbilities(&asset,token);
        if (root) asset.bindings=array(bindings); else bindings[0].conditions={};
        VirtualFree(guarded,0,MEM_RELEASE);
        assert(!abilitySnapshots.at(token).unchanged(&asset));
    };
    retired(true);retired(false);
    parameters={reinterpret_cast<long long>(&asset),token};parameterIndex=0;
    assert(pbz_ability_bindings_observe(nullptr)==1 && returnedInteger==token && parameterIndex==2);
    parameters={token,reinterpret_cast<long long>(&asset)};parameterIndex=0;
    assert(pbz_ability_bindings_unchanged(nullptr)==1 && parameterIndex==2);
    parameters[1]=reinterpret_cast<long long>(&secondAsset);parameterIndex=0;
    assert(pbz_ability_bindings_unchanged(nullptr)==0);
    parameters={reinterpret_cast<long long>(&asset),token};parameterIndex=0;nameSize=12;
    assert(pbz_ability_bindings_observe(nullptr)==2 && returnedInteger==0 && !abilitySnapshots.count(token));
    nameSize=8;
    assert(abilitySnapshots.count(secondToken)==1);
    parameters={secondToken};parameterIndex=0;
    assert(pbz_ability_bindings_forget(nullptr)==0 && abilitySnapshots.empty());
    auto library=LoadLibraryA("C:/Program Files/Wireshark/lua54.dll");assert(library);
#define LOAD(field,name) api.field=reinterpret_cast<decltype(api.field)>(GetProcAddress(library,name));assert(api.field)
    LOAD(newstate,"luaL_newstate");LOAD(close,"lua_close");LOAD(pushinteger,"lua_pushinteger");
    LOAD(pushboolean,"lua_pushboolean");LOAD(pushstring,"lua_pushlstring");LOAD(pushfunction,"lua_pushcclosure");
    LOAD(integer,"lua_tointegerx");LOAD(boolean,"lua_toboolean");LOAD(string,"lua_tolstring");
    LOAD(rotate,"lua_rotate");LOAD(settop,"lua_settop");LOAD(gettop,"lua_gettop");LOAD(pcall,"lua_pcallk");
#undef LOAD
    auto* lua=api.newstate();assert(lua);
    api.pushfunction(lua,pbz_ability_bindings_observe,0);
    api.pushinteger(lua,reinterpret_cast<long long>(&asset));api.pushinteger(lua,0);
    assert(api.pcall(lua,2,-1,0,0,nullptr)==0 && api.gettop(lua)==1);
    token=api.integer(lua,1,nullptr);assert(token>0);
    auto compare=[&](bool expected) {
        api.settop(lua,0);api.pushfunction(lua,pbz_ability_bindings_unchanged,0);
        api.pushinteger(lua,token);api.pushinteger(lua,reinterpret_cast<long long>(&asset));api.pushboolean(lua,1);
        assert(api.pcall(lua,3,-1,0,0,nullptr)==0);
        assert(api.gettop(lua)==(expected?1:0));
        if (expected) assert(api.boolean(lua,1));
    };
    compare(true);normalRows[0].tag++;compare(false);
    api.settop(lua,0);api.pushfunction(lua,pbz_ability_bindings_forget,0);api.pushinteger(lua,token);
    assert(api.pcall(lua,1,-1,0,0,nullptr)==0 && api.gettop(lua)==0 && abilitySnapshots.empty());
    nameSize=12;
    api.pushfunction(lua,pbz_ability_bindings_observe,0);api.pushinteger(lua,reinterpret_cast<long long>(&asset));api.pushinteger(lua,0);
    assert(api.pcall(lua,2,-1,0,0,nullptr)==0 && api.gettop(lua)==2 && api.integer(lua,1,nullptr)==0);
    assert(std::string(api.string(lua,2,nullptr)).find("layout unsupported")!=std::string::npos);
    api.close(lua);
    std::cout << "InputObservationNativeTests PASS: 2000 stable frames, native edits, retired allocations, layout failure, empty contexts\n";
    std::cout << "AbilityBindingObservation PASS: 2000 reflection-free frames, six trigger arrays, sparse slots, independent owners, retired arrays/maps, bridge arguments, cleanup\n";
}
