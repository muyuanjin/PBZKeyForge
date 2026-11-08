#define PBZ_CONTEXT_API
#include "../../prototype/input-router/native/ability-selection.cpp"
#include <cassert>
#include <cstring>
#include <iostream>
#include <map>
#include <chrono>

namespace
{
    using namespace RC::Unreal;
    using PBZAbilitySelection::Array;
    struct Type { std::map<std::wstring,FProperty*> fields; };
    struct Property : FProperty
    {
        int offset, size;
        TObjectPtr<UScriptStruct> structure{};
        FProperty* inner=nullptr;
        Property(int offset,int size) : offset(offset),size(size) {}
    };
    struct Tag { uint64_t name; };
    struct Tags { int padding=123; Array entries{}; };
    struct Ability { UClass* type; int padding=456; Tags tags; };
    struct Spec { int padding=789; Ability* ability; uint8_t active=0; };
    struct Grants { double padding=0; Array items{}; };
    struct Component { int padding=987; Grants grants; };
    std::map<void*,Type*> objectTypes;
    std::vector<std::wstring> names;
    int nameSize=8, checks=0, tagConversions=0;
    struct Api
    {
        void* (*newstate)(); void (*close)(void*); void (*pushinteger)(void*,long long);
        void (*pushboolean)(void*,int); const char* (*pushstring)(void*,const char*,size_t);
        void (*pushfunction)(void*,int(*)(void*),int); long long (*integer)(void*,int,int*);
        int (*boolean)(void*,int); const char* (*string)(void*,int,size_t*);
        void (*rotate)(void*,int,int); void (*settop)(void*,int); int (*gettop)(void*);
        int (*pcall)(void*,int,int,int,intptr_t,void*);
    } api;
    uint64_t id(const FName& name) { uint64_t result; std::memcpy(&result,&name,sizeof(result)); return result; }
    Property* add(Type& type,const wchar_t* name,int offset,int size)
    { auto* p=new Property(offset,size); type.fields[name]=p;return p; }
    template<class T,size_t N> Array array(T (&values)[N])
    { return {reinterpret_cast<unsigned char*>(values),int(N),int(N)}; }
    void check(bool value) { assert(value);++checks; }
    void* state(const RC::LuaMadeSimple::Lua* lua) { return *reinterpret_cast<void* const*>(lua); }
    void remove(void* lua,int index) { api.rotate(lua,index,-1);api.settop(lua,-2); }
}
namespace RC::Unreal
{
    FName::FName(const wchar_t* text,EFindName,void*)
    { uint64_t value=names.size();names.push_back(text);std::memcpy(this,&value,sizeof(value)); }
    int FName::StaticSize() { return nameSize; }
    const std::wstring FName::ToString() const { ++tagConversions; return names.at(id(*this)); }
    int& FProperty::GetElementSize() { return static_cast<Property*>(this)->size; }
    int& FProperty::GetOffset_Internal() { return static_cast<Property*>(this)->offset; }
    FProperty* UObject::GetPropertyByNameInChain(const wchar_t* name)
    { auto& fields=objectTypes.at(this)->fields;auto it=fields.find(name);return it==fields.end()?nullptr:it->second; }
    FProperty* UStruct::FindProperty(FName name)
    { auto& fields=reinterpret_cast<Type*>(this)->fields;auto it=fields.find(names.at(id(name)));return it==fields.end()?nullptr:it->second; }
    TObjectPtr<UScriptStruct>& FStructProperty::GetStruct() { return reinterpret_cast<Property*>(this)->structure; }
    FProperty*& FArrayProperty::GetInner() { return reinterpret_cast<Property*>(this)->inner; }
    UClass*& UObjectBase::GetClassPrivate() { return reinterpret_cast<Ability*>(this)->type; }
}
namespace RC::LuaMadeSimple
{
    long long Lua::get_integer(int index) const { auto value=api.integer(state(this),index,nullptr);remove(state(this),index);return value; }
    bool Lua::get_bool(int index) const { bool value=api.boolean(state(this),index)!=0;remove(state(this),index);return value; }
    std::string_view Lua::get_string(int index) const
    { size_t size;auto* value=api.string(state(this),index,&size);remove(state(this),index);return {value,size}; }
    void Lua::set_integer(long long value) const { api.pushinteger(state(this),value); }
    void Lua::set_string(const char* value,size_t size) const { api.pushstring(state(this),value,size); }
}

int main(int argc,char** argv)
{
    auto library=LoadLibraryA(argc>1?argv[1]:"C:/Program Files/Wireshark/lua54.dll");assert(library);
#define LOAD(field,name) api.field=reinterpret_cast<decltype(api.field)>(GetProcAddress(library,name));assert(api.field)
    LOAD(newstate,"luaL_newstate");LOAD(close,"lua_close");LOAD(pushinteger,"lua_pushinteger");
    LOAD(pushboolean,"lua_pushboolean");LOAD(pushstring,"lua_pushlstring");LOAD(pushfunction,"lua_pushcclosure");
    LOAD(integer,"lua_tointegerx");LOAD(boolean,"lua_toboolean");LOAD(string,"lua_tolstring");
    LOAD(rotate,"lua_rotate");LOAD(settop,"lua_settop");LOAD(gettop,"lua_gettop");LOAD(pcall,"lua_pcallk");
#undef LOAD
    Type componentType,grantsType,specType,abilityType,tagsType,tagType;
    auto* grants=add(componentType,L"ActivatableAbilities",offsetof(Component,grants),sizeof(Grants));
    grants->structure.Ptr=reinterpret_cast<UScriptStruct*>(&grantsType);
    auto* items=add(grantsType,L"Items",offsetof(Grants,items),sizeof(Array));
    auto* spec=new Property(0,sizeof(Spec));spec->structure.Ptr=reinterpret_cast<UScriptStruct*>(&specType);items->inner=spec;
    add(specType,L"Ability",offsetof(Spec,ability),sizeof(void*));
    auto* activeProperty=add(specType,L"ActiveCount",offsetof(Spec,active),sizeof(uint8_t));
    auto* tags=add(abilityType,L"AbilityTags",offsetof(Ability,tags),sizeof(Tags));
    tags->structure.Ptr=reinterpret_cast<UScriptStruct*>(&tagsType);
    auto* entries=add(tagsType,L"GameplayTags",offsetof(Tags,entries),sizeof(Array));
    auto* tag=new Property(0,sizeof(Tag));tag->structure.Ptr=reinterpret_cast<UScriptStruct*>(&tagType);entries->inner=tag;
    add(tagType,L"TagName",offsetof(Tag,name),sizeof(FName));
    int firstClass=0,secondClass=0;
    FName attack(L"Ability.Attack.Heavy",FNAME_Add,nullptr),ground(L"State.Grounded",FNAME_Add,nullptr),collision(L"Ability.Attacker",FNAME_Add,nullptr);
    Tag firstTags[]={{id(attack)},{id(ground)}},secondTags[]={{id(collision)}};
    Ability first{reinterpret_cast<UClass*>(&firstClass)},second{reinterpret_cast<UClass*>(&secondClass)};
    first.tags.entries=array(firstTags);second.tags.entries=array(secondTags);
    Spec specs[]={{0,&first},{0,&second},{0,nullptr},{0,&first}};
    Component component;component.grants.items=array(specs);
    objectTypes[&component]=&componentType;objectTypes[&first]=objectTypes[&second]=&abilityType;
    using PBZAbilitySelection::select;
    check(select(&component,first.type,{},false)=="1 4 ");
    check(select(&component,nullptr,{L"Ability.Attack"},false)=="1 4 ");
    check(select(&component,nullptr,{L"Ability.Attack",L"State.Grounded",L"Ability.Attack"},true)=="1 4 ");
    check(select(&component,nullptr,{L"Ability.Missing",L"State.Grounded"},false)=="1 4 ");
    check(select(&component,nullptr,{L"Ability.Missing",L"State.Grounded"},true).empty());
    check(select(&component,nullptr,{},true)=="1 2 4 ");
    check(select(&component,nullptr,{},false).empty());
    // A later call sees replaced grants and tag storage, without invalidation state.
    specs[0].ability=&second;component.grants.items.num=2;
    check(select(&component,first.type,{},false).empty());
    check(select(&component,second.type,{},false)=="1 2 ");
    second.tags.entries=array(firstTags);
    check(select(&component,nullptr,{L"Ability.Attack"},false)=="1 2 ");
    auto* lua=api.newstate();assert(lua);
    auto invoke=[&](UClass* type,bool all,std::vector<std::string> wanted,bool activeOnly=false) {
        api.settop(lua,0);api.pushfunction(lua,activeOnly?pbz_active_ability_spec_indices:pbz_ability_spec_indices,0);
        api.pushinteger(lua,reinterpret_cast<long long>(&component));api.pushinteger(lua,reinterpret_cast<long long>(type));
        api.pushboolean(lua,all);api.pushinteger(lua,wanted.size());
        for (auto& text:wanted) api.pushstring(lua,text.data(),text.size());
        check(api.pcall(lua,4+int(wanted.size()),-1,0,0,nullptr)==0);
    };
    invoke(second.type,false,{});check(api.gettop(lua)==1 && std::string(api.string(lua,1,nullptr))=="1 2 ");
    invoke(nullptr,true,{"Ability.Attack","State.Grounded"});check(api.gettop(lua)==1 && std::string(api.string(lua,1,nullptr))=="1 2 ");
    invoke(nullptr,false,{"Missing"});check(api.gettop(lua)==1 && std::string(api.string(lua,1,nullptr)).empty());
    tagConversions=0;
    invoke(nullptr,false,{"Ability.Attack"},true);
    check(api.gettop(lua)==1 && std::string(api.string(lua,1,nullptr)).empty() && tagConversions==0);
    specs[1].active=2;
    invoke(nullptr,false,{"Ability.Attack"},true);
    check(api.gettop(lua)==1 && std::string(api.string(lua,1,nullptr))=="2 " && tagConversions==1);
    specs[0].active=1;specs[1].active=0;
    invoke(second.type,false,{},true);
    check(api.gettop(lua)==1 && std::string(api.string(lua,1,nullptr))=="1 ");
    activeProperty->size=4;
    invoke(nullptr,false,{"Ability.Attack"},true);
    check(api.gettop(lua)==2 && std::string(api.string(lua,2,nullptr)).find("active count layout unsupported")!=std::string::npos);
    activeProperty->size=1;
    nameSize=12;invoke(second.type,false,{});
    check(api.gettop(lua)==2 && api.integer(lua,1,nullptr)==0 && std::string(api.string(lua,2,nullptr)).find("layout unsupported")!=std::string::npos);
    nameSize=8;specType.fields.erase(L"Ability");invoke(second.type,false,{});
    check(api.gettop(lua)==2 && api.integer(lua,1,nullptr)==0 && std::string(api.string(lua,2,nullptr)).find("member unavailable")!=std::string::npos);
    nameSize=8;specType.fields[L"Ability"]=new Property(offsetof(Spec,ability),sizeof(void*));
    std::vector<Spec> workload(130,Spec{0,&second});workload[128].ability=&first;
    second.tags.entries=array(secondTags);
    component.grants.items={reinterpret_cast<unsigned char*>(workload.data()),130,130};
    for (bool byClass:{true,false})
    {
        auto before=std::chrono::steady_clock::now();
        size_t bytes=0;
        for (int repeat=0; repeat<1000; ++repeat)
            bytes+=select(&component,byClass?first.type:nullptr,byClass?std::vector<std::wstring>{}:std::vector<std::wstring>{L"Ability.Attack",L"State.Grounded"},true).size();
        check(bytes==4000);
        auto elapsed=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-before).count();
        std::cout<<"AbilitySelectionNativeBenchmark "<<(byClass?"class":"tags")<<" ms/call="<<elapsed/1000<<"\n";
    }
    const std::vector<std::wstring> dashTags={L"Ability.Action.Hun.Dash",L"Ability.Action.Hun.ContinuousDash",
        L"Ability.Action.Hun.SpecialDash",L"Ability.Action.Hun.PerfectDodge",L"Ability.Action.Hun.DeflectDash",L"Ability.Action.Hun.AirDash"};
    FName dash(L"Ability.Action.Hun.Dash.Forward",FNAME_Add,nullptr);
    Tag dashTag[]={{id(dash)}};
    first.tags.entries=array(dashTag);
    for (bool activeOnly:{false,true})
    {
        tagConversions=0;
        const auto before=std::chrono::steady_clock::now();
        size_t bytes=0;
        for (int repeat=0;repeat<10000;++repeat)
            bytes+=select(&component,nullptr,dashTags,false,activeOnly).size();
        check(bytes==(activeOnly?0:40000));
        check(tagConversions==(activeOnly?0:1300000));
        const auto elapsed=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-before).count();
        std::cout<<"LedgeSelectionBenchmark "<<(activeOnly?"active":"all")<<" ms/call="<<elapsed/10000<<" tag-conversions="<<tagConversions<<"\n";
    }
    workload[128].active=1;
    tagConversions=0;
    check(select(&component,nullptr,dashTags,false,true)=="129 " && tagConversions==1);
    api.close(lua);
    std::cout<<"AbilitySelectionNativeTests PASS "<<checks<<"\n";
}
