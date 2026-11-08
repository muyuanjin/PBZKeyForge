#define PBZ_CONTEXT_API
#include "../../prototype/input-router/native/weapon-equipment.cpp"
#include <cassert>
#include <algorithm>
#include <cstdint>
#include <iostream>
#include <map>

namespace
{
    using namespace RC::Unreal;
    struct Type
    {
        std::map<std::wstring,FProperty*> fields;
        UStruct* parent=nullptr;
        TArray<FImplementedInterface> interfaces{};
    };
    struct Property : FProperty
    {
        int offset,size;
        TObjectPtr<UClass> interfaceClass{};
        TObjectPtr<UScriptStruct> structure{};
        Property(int offset,int size) : offset(offset),size(size) {}
    };
    struct Function { unsigned short size; Type fields; };
    struct Config { UClass* type; unsigned char storage[80]{}; };
    struct BoundConfig { UObject* object; void* address; };
    struct Component { std::map<std::pair<std::wstring,int>,BoundConfig> slots; };
    struct Event
    {
        UObject* component;
        UFunction* function;
        std::wstring slot;
        int index;
        BoundConfig config;
        std::vector<unsigned char> bytes;
    };
    struct Api
    {
        void* (*newstate)();
        void (*close)(void*);
        void (*pushinteger)(void*,long long);
        const char* (*pushstring)(void*,const char*,size_t);
        void (*pushfunction)(void*,int(*)(void*),int);
        long long (*integer)(void*,int,int*);
        const char* (*string)(void*,int,size_t*);
        void (*rotate)(void*,int,int);
        void (*settop)(void*,int);
        int (*gettop)(void*);
        int (*pcall)(void*,int,int,int,intptr_t,void*);
    } api;
    std::map<void*,Type*> objectTypes;
    std::vector<std::wstring> names;
    std::vector<Event> events;
    int nameSize=8,checks=0;
    void check(bool value) { assert(value);++checks; }
    void* state(const RC::LuaMadeSimple::Lua* lua) { return *reinterpret_cast<void* const*>(lua); }
    void remove(void* lua,int index) { api.rotate(lua,index,-1);api.settop(lua,-2); }
    uint64_t nameId(const void* value)
    { uint64_t result;std::memcpy(&result,value,sizeof(result));return result; }
    template<class T> T read(const void* data,int offset)
    { T result;std::memcpy(&result,static_cast<const unsigned char*>(data)+offset,sizeof(result));return result; }
    Property* property(Type& type,const wchar_t* name,int offset,int size)
    {
        auto* result=new Property(offset,size);
        type.fields[name]=result;
        return result;
    }
    Property* property(Function& function,const wchar_t* name)
    { return static_cast<Property*>(function.fields.fields.at(name)); }
    void layout(Function& function,Type& tag,UClass* interfaceClass,int slot,int index,int config,int size)
    {
        function.size=static_cast<unsigned short>(size);
        auto* slotProperty=property(function.fields,L"SlotTag",slot,sizeof(FName));
        slotProperty->structure.Ptr=reinterpret_cast<UScriptStruct*>(&tag);
        property(function.fields,L"Index",index,sizeof(int));
        property(function.fields,L"Config",config,sizeof(BoundConfig))->interfaceClass.Ptr=interfaceClass;
        objectTypes[&function]=&function.fields;
    }
    void expectEvent(Component& component,Function& function,const wchar_t* slot,int index,Config& config,int offset)
    {
        const auto& event=events.back();
        check(event.component==reinterpret_cast<UObject*>(&component));
        check(event.function==reinterpret_cast<UFunction*>(&function));
        check(event.slot==slot && event.index==index);
        check(event.config.object==reinterpret_cast<UObject*>(&config));
        check(event.config.address==reinterpret_cast<unsigned char*>(&config)+offset);
        check(event.config.address!=event.config.object);
        auto bound=component.slots.at({slot,index});
        check(bound.object==event.config.object && bound.address==event.config.address);
        check(event.bytes.size()==function.size);
        std::vector<bool> used(function.size,false);
        for (const auto* name:{L"SlotTag",L"Index",L"Config"})
        {
            auto* field=property(function,name);
            for (int n=0;n<field->size;++n) used[field->offset+n]=true;
        }
        for (size_t n=0;n<used.size();++n) if (!used[n]) check(event.bytes[n]==0);
    }
}

namespace RC::Unreal
{
    FName::FName(const wchar_t* text,EFindName mode,void*)
    {
        check(mode==FNAME_Find);
        auto found=std::find(names.begin(),names.end(),text);
        check(found!=names.end());
        uint64_t value=found-names.begin();
        std::memcpy(this,&value,sizeof(value));
    }
    int FName::StaticSize() { return nameSize; }
    int& FProperty::GetElementSize() { return static_cast<Property*>(this)->size; }
    int& FProperty::GetOffset_Internal() { return static_cast<Property*>(this)->offset; }
    FProperty* UObject::GetPropertyByNameInChain(const wchar_t* name)
    {
        auto& fields=objectTypes.at(this)->fields;
        auto found=fields.find(name);
        return found==fields.end()?nullptr:found->second;
    }
    UClass*& UObjectBase::GetClassPrivate() { return reinterpret_cast<Config*>(this)->type; }
    UStruct*& UStruct::GetSuperStruct() { return reinterpret_cast<Type*>(this)->parent; }
    TArray<FImplementedInterface>& UClass::GetInterfaces() { return reinterpret_cast<Type*>(this)->interfaces; }
    unsigned short& UFunction::GetParmsSize() { return reinterpret_cast<Function*>(this)->size; }
    TObjectPtr<UClass>& FInterfaceProperty::GetInterfaceClass()
    { return reinterpret_cast<Property*>(this)->interfaceClass; }
    TObjectPtr<UScriptStruct>& FStructProperty::GetStruct()
    { return reinterpret_cast<Property*>(this)->structure; }
    void UObject::ProcessEvent(UFunction* target,void* data)
    {
        auto& function=*reinterpret_cast<Function*>(target);
        auto slot=names.at(nameId(static_cast<unsigned char*>(data)+property(function,L"SlotTag")->offset));
        auto index=read<int>(data,property(function,L"Index")->offset);
        auto config=read<BoundConfig>(data,property(function,L"Config")->offset);
        auto* bytes=static_cast<unsigned char*>(data);
        events.push_back({this,target,slot,index,config,{bytes,bytes+function.size}});
        reinterpret_cast<Component*>(this)->slots[{slot,index}]=config;
    }
}
namespace RC::LuaMadeSimple
{
    long long Lua::get_integer(int index) const
    { auto value=api.integer(state(this),index,nullptr);remove(state(this),index);return value; }
    std::string_view Lua::get_string(int index) const
    { size_t size;auto* value=api.string(state(this),index,&size);remove(state(this),index);return {value,size}; }
    void Lua::set_integer(long long value) const { api.pushinteger(state(this),value); }
    void Lua::set_string(const char* value,size_t size) const { api.pushstring(state(this),value,size); }
}

int main(int argc,char** argv)
{
    auto library=LoadLibraryA(argc>1?argv[1]:"C:/Program Files/Wireshark/lua54.dll");check(library!=nullptr);
#define LOAD(field,name) api.field=reinterpret_cast<decltype(api.field)>(GetProcAddress(library,name));check(api.field!=nullptr)
    LOAD(newstate,"luaL_newstate");LOAD(close,"lua_close");LOAD(pushinteger,"lua_pushinteger");
    LOAD(pushstring,"lua_pushlstring");LOAD(pushfunction,"lua_pushcclosure");LOAD(integer,"lua_tointegerx");
    LOAD(string,"lua_tolstring");LOAD(rotate,"lua_rotate");LOAD(settop,"lua_settop");LOAD(gettop,"lua_gettop");
    LOAD(pcall,"lua_pcallk");
#undef LOAD
    int wantedInterface,otherInterface;
    auto* wanted=reinterpret_cast<UClass*>(&wantedInterface);
    auto* other=reinterpret_cast<UClass*>(&otherInterface);
    Type tag,direct,parent,derived,missing,k2;
    objectTypes[&tag]=&tag;
    auto* tagName=property(tag,L"TagName",0,sizeof(FName));
    FImplementedInterface directInterfaces[]={{other,16,false},{wanted,40,false}};
    FImplementedInterface parentInterfaces[]={{wanted,56,false}};
    FImplementedInterface derivedInterfaces[]={{other,24,false}};
    FImplementedInterface k2Interfaces[]={{wanted,0,true}};
    direct.interfaces={directInterfaces,2,2};parent.interfaces={parentInterfaces,1,1};
    derived.interfaces={derivedInterfaces,1,1};derived.parent=reinterpret_cast<UStruct*>(&parent);
    k2.interfaces={k2Interfaces,1,1};
    Config original{reinterpret_cast<UClass*>(&derived)},replacement{reinterpret_cast<UClass*>(&direct)},
        absent{reinterpret_cast<UClass*>(&missing)},blueprintOnly{reinterpret_cast<UClass*>(&k2)};
    Component component;
    component.slots[{L"WeaponSlot.Main",0}]={reinterpret_cast<UObject*>(&original),reinterpret_cast<unsigned char*>(&original)+56};
    component.slots[{L"WeaponSlot.Main",1}]=component.slots.at({L"WeaponSlot.Main",0});
    component.slots[{L"WeaponSlot.Shadow",0}]=component.slots.at({L"WeaponSlot.Main",0});
    auto untouched=component.slots;
    names={L"WeaponSlot.Main",L"WeaponSlot.Shadow"};
    Function first,second;
    layout(first,tag,wanted,0,8,16,40);
    layout(second,tag,wanted,24,0,8,48);
    auto* lua=api.newstate();check(lua!=nullptr);
    auto invoke=[&](Component* owner,Function* function,const std::string& slot,int index,Config* config) {
        api.settop(lua,0);api.pushfunction(lua,pbz_weapon_set_slot,0);
        api.pushinteger(lua,reinterpret_cast<long long>(owner));
        api.pushinteger(lua,reinterpret_cast<long long>(function));
        api.pushstring(lua,slot.data(),slot.size());api.pushinteger(lua,index);
        api.pushinteger(lua,reinterpret_cast<long long>(config));
        check(api.pcall(lua,5,-1,0,0,nullptr)==0);
    };
    auto success=[&]() { check(api.gettop(lua)==1 && api.integer(lua,1,nullptr)==1); };
    auto rejected=[&](size_t previous,const char* error) {
        check(api.gettop(lua)==2 && api.integer(lua,1,nullptr)==0);
        check(std::string(api.string(lua,2,nullptr)).find(error)!=std::string::npos);
        check(events.size()==previous);
        check(component.slots.at({L"WeaponSlot.Main",1}).object==untouched.at({L"WeaponSlot.Main",1}).object);
        check(component.slots.at({L"WeaponSlot.Shadow",0}).object==untouched.at({L"WeaponSlot.Shadow",0}).object);
    };

    invoke(&component,&first,"WeaponSlot.Main",0,&replacement);success();
    expectEvent(component,first,L"WeaponSlot.Main",0,replacement,40);
    check(events.size()==1);
    check(component.slots.at({L"WeaponSlot.Main",1}).object==untouched.at({L"WeaponSlot.Main",1}).object);
    check(component.slots.at({L"WeaponSlot.Shadow",0}).object==untouched.at({L"WeaponSlot.Shadow",0}).object);
    // Restoration is an ordinary second reflected call with the original config.
    invoke(&component,&first,"WeaponSlot.Main",0,&original);success();
    expectEvent(component,first,L"WeaponSlot.Main",0,original,56);
    check(component.slots.at({L"WeaponSlot.Main",0}).object==untouched.at({L"WeaponSlot.Main",0}).object);
    check(component.slots.at({L"WeaponSlot.Main",0}).address==untouched.at({L"WeaponSlot.Main",0}).address);
    invoke(&component,&second,"WeaponSlot.Shadow",1,&original);success();
    expectEvent(component,second,L"WeaponSlot.Shadow",1,original,56);
    invoke(&component,&second,"WeaponSlot.Main",2,&replacement);success();
    expectEvent(component,second,L"WeaponSlot.Main",2,replacement,40);
    // The most-derived native interface offset wins over a matching ancestor.
    auto savedDirectParent=direct.parent;
    direct.parent=reinterpret_cast<UStruct*>(&parent);
    invoke(&component,&first,"WeaponSlot.Main",0,&replacement);success();
    expectEvent(component,first,L"WeaponSlot.Main",0,replacement,40);
    direct.parent=savedDirectParent;

    size_t previous=events.size();
    invoke(&component,&first,"WeaponSlot.Main",0,&absent);rejected(previous,"no native");
    invoke(&component,&first,"WeaponSlot.Main",0,&blueprintOnly);rejected(previous,"no native");
    invoke(nullptr,&first,"WeaponSlot.Main",0,&replacement);rejected(previous,"object is absent");
    invoke(&component,nullptr,"WeaponSlot.Main",0,&replacement);rejected(previous,"object is absent");
    invoke(&component,&first,"WeaponSlot.Main",0,nullptr);rejected(previous,"object is absent");
    for (const auto* name:{L"SlotTag",L"Index",L"Config"})
    {
        auto* field=property(first,name);
        first.fields.fields.erase(name);
        invoke(&component,&first,"WeaponSlot.Main",0,&replacement);rejected(previous,"parameter is absent");
        first.fields.fields[name]=field;
        int saved=field->size;field->size=saved+4;
        invoke(&component,&first,"WeaponSlot.Main",0,&replacement);
        rejected(previous,std::wstring(name)==L"SlotTag"?"reflected gameplay tag":"layout is unsupported");
        field->size=saved;
        saved=field->offset;field->offset=-1;
        invoke(&component,&first,"WeaponSlot.Main",0,&replacement);rejected(previous,"layout is unsupported");
        field->offset=first.size-1;
        invoke(&component,&first,"WeaponSlot.Main",0,&replacement);rejected(previous,"layout is unsupported");
        field->offset=saved;
    }
    first.size=20;
    invoke(&component,&first,"WeaponSlot.Main",0,&replacement);rejected(previous,"layout is unsupported");
    first.size=40;
    nameSize=12;
    invoke(&component,&first,"WeaponSlot.Main",0,&replacement);rejected(previous,"reflected gameplay tag");
    nameSize=8;tagName->offset=4;
    invoke(&component,&first,"WeaponSlot.Main",0,&replacement);rejected(previous,"reflected gameplay tag");
    tagName->offset=0;tagName->size=12;
    invoke(&component,&first,"WeaponSlot.Main",0,&replacement);rejected(previous,"reflected gameplay tag");
    tagName->size=8;tag.fields.erase(L"TagName");
    invoke(&component,&first,"WeaponSlot.Main",0,&replacement);rejected(previous,"parameter is absent");
    tag.fields[L"TagName"]=tagName;
    invoke(&component,&second,"WeaponSlot.Main",0,&original);success();
    expectEvent(component,second,L"WeaponSlot.Main",0,original,56);
    check(events.size()==previous+1);
    api.close(lua);
    std::cout<<"WeaponEquipmentNativeTests PASS "<<checks
        <<": Lua C calls, two-pointer interfaces, inherited offsets, reflected layouts and restoration\n";
}
