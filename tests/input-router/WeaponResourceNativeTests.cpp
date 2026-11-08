#define main contextBaselineMain
#include "ScriptContextNativeTests.cpp"
#undef main

namespace
{
    struct ResourceTestAction
    {
        void** table;
        Object* ability;
        ResourceTagContainer required;
        ResourceTagContainer ignored;
        bool qualified;
    };
    int actionCalls=0,eventCalls=0;
    bool originalResourceAction(Object* object)
    {
        ++actionCalls;
        auto& action=*reinterpret_cast<ResourceTestAction*>(object);
        if (!action.qualified) return false;
        for (int i=0;i<action.required.tags.Num;++i)
            if (fullWeaponResourceTag(action.required.tags.Data[i])) return false;
        return true;
    }
    void originalResourceEvent(Object*,const void*,const void*) { ++eventCalls; }
    void originalResourceBullet(NativeInterface*,const void*,Object*,Object*,Object*,bool) {}
}

int main(int argc,char** argv)
{
    luaApi.load(argc>1 ? argv[1] : "C:\\Program Files\\Wireshark\\lua54.dll");
    TestState state;
    state.lua=luaApi.newstate();
    liveLuaState=state.lua;
    auto begin=[&](int(*function)(void*))
    {
        luaApi.settop(state.lua,0);luaApi.pushcclosure(state.lua,function,0);
    };
    auto pointer=[&](const void* value) { luaApi.pushinteger(state.lua,reinterpret_cast<long long>(value)); };
    auto finish=[&](int arguments,int results)
    {
        assert(luaApi.pcall(state.lua,arguments,-1,0,0,nullptr)==0);
        assert(luaApi.gettop(state.lua)==results);
        assert(luaApi.tointeger(state.lua,1,nullptr)==1);
    };
    auto setResources=[&](Object* ability,bool bypass)
    {
        begin(pbz_resource_ability);pointer(ability);luaApi.pushboolean(state.lua,bypass);finish(2,1);
    };
    enabled.store(true);damageEnabled.store(true);
    auto* code=static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_EXECUTE_READWRITE));
    assert(code);
    const unsigned char execute[]={0x4c,0x8d,0x44,0x24,0x38,0x48,0x8b,0xce,0xe8,0,0,0,0,0x48,0x8b,0x4c,0x24,0x30,0xc3};
    std::memcpy(code,execute,sizeof(execute));
    int displacement=64-13;std::memcpy(code+9,&displacement,4);
    const unsigned char check[]={0x48,0x8b,0x02,0x48,0x8b,0xca,0xff,0x90,0x18,0,0,0,0x84,0xc0,0xc3};
    std::memcpy(code+64,check,sizeof(check));
    const unsigned char getter[]={0xe8,0,0,0,0,0x48,0x89,0x03,0xc3};
    std::memcpy(code+128,getter,sizeof(getter));displacement=256-133;std::memcpy(code+129,&displacement,4);
    const unsigned char getAbility[]={0x48,0x8b,0x41,0x08,0xc3};std::memcpy(code+256,getAbility,sizeof(getAbility));
    const unsigned char trigger[]={0xff,0x90,0x10,0,0,0,0xc3};std::memcpy(code+192,trigger,sizeof(trigger));
    code[300]=1;
    RUNTIME_FUNCTION entries[]={{0,sizeof(execute),300},{64,64+sizeof(check),300},{128,128+sizeof(getter),300},{192,192+sizeof(trigger),300}};
    assert(RtlAddFunctionTable(entries,4,reinterpret_cast<DWORD64>(code)));
    RC::Unreal::UFunction executeFunction,getterFunction,triggerFunction;
    nativeExecs[&executeFunction]=reinterpret_cast<NativeExec>(code);
    nativeExecs[&getterFunction]=reinterpret_cast<NativeExec>(code+128);
    nativeExecs[&triggerFunction]=reinterpret_cast<NativeExec>(code+192);
    begin(pbz_resource_prepare_actions);pointer(&executeFunction);pointer(&getterFunction);finish(2,1);
    assert(resourceActionSlot==3);
    void* actionTable[]{nullptr,nullptr,nullptr,reinterpret_cast<void*>(&originalResourceAction)};
    Object ability,otherAbility;
    RC::Unreal::FName requiredNames[]={RC::Unreal::FName(L"CharacterState.AbilityState.BaiShanFengLeiJian.Thunder",RC::Unreal::FNAME_Find,nullptr),
        RC::Unreal::FName(L"CharacterState.WeaponUpgrade.FengLei",RC::Unreal::FNAME_Find,nullptr)};
    ResourceTestAction action{actionTable,&ability,{{requiredNames,2,2},{nullptr,0,0}},{{nullptr,0,0},{nullptr,0,0}},true};
    RC::Unreal::FProperty activation,required,ignored,tags;
    Object requirementsType,tagsType;
    inheritedProperties[&action][L"ActivationRequirements"]=&activation;
    propertyOffsets[&activation]=offsetof(ResourceTestAction,required);propertySizes[&activation]=64;
    propertyStructs[&activation].Ptr=reinterpret_cast<RC::Unreal::UScriptStruct*>(&requirementsType);
    namedProperties[&requirementsType][L"RequireTags"]=&required;
    namedProperties[&requirementsType][L"IgnoreTags"]=&ignored;
    namedProperties[&tagsType][L"GameplayTags"]=&tags;
    propertyOffsets[&required]=0;propertyOffsets[&ignored]=32;propertyOffsets[&tags]=0;
    propertySizes[&required]=32;propertySizes[&ignored]=32;propertySizes[&tags]=16;
    propertyStructs[&required].Ptr=reinterpret_cast<RC::Unreal::UScriptStruct*>(&tagsType);
    propertyStructs[&ignored].Ptr=reinterpret_cast<RC::Unreal::UScriptStruct*>(&tagsType);
    begin(pbz_resource_watch_action);pointer(&action);finish(1,1);
    auto can=[&]() { return reinterpret_cast<ResourceActionCheck>(actionTable[3])(reinterpret_cast<Object*>(&action)); };
    assert(!can());setResources(&ability,true);assert(can());
    assert(action.required.tags.Data==requiredNames && action.required.tags.Num==2);
    action.qualified=false;assert(!can());action.qualified=true;
    auto calls=actionCalls;
    action.ignored.tags={requiredNames,1,1};assert(!can() && actionCalls==calls);
    action.ignored.tags={nullptr,0,0};
    action.ability=&otherAbility;assert(!can());action.ability=&ability;
    setResources(&ability,false);assert(!can());setResources(&ability,true);
    void* eventTable[]{nullptr,nullptr,reinterpret_cast<void*>(&originalResourceEvent)};
    NativeObject event{eventTable};
    begin(pbz_resource_watch_event);pointer(&event);pointer(&triggerFunction);finish(2,1);
    auto fire=[&](Object* bullet)
    {
        Object* interfaceValue[]{bullet,nullptr};
        reinterpret_cast<ResourceEventTrigger>(eventTable[2])(reinterpret_cast<Object*>(&event),interfaceValue,nullptr);
    };
    TestBullet<0x28> first,child;
    auto** nativeTable=*reinterpret_cast<void***>(&first.native);
    bulletInitializers[nativeTable]={originalResourceBullet,offsetof(TestBullet<0x28>,modules),2};
    first.native.owner=&first.object;child.native.owner=&child.object;
    initializeBullet(&first.native,nullptr,nullptr,&ability,nullptr,false);
    assert(bulletResources.at(&first.object));
    setResources(&ability,false);fire(&first.object);assert(eventCalls==0);
    Object* parent[]{&first.object,nullptr};
    initializeBullet(&child.native,parent,nullptr,&otherAbility,nullptr,false);
    assert(bulletResources.at(&child.object));fire(&child.object);assert(eventCalls==0);
    initializeBullet(&first.native,nullptr,nullptr,&otherAbility,nullptr,false);
    assert(!bulletResources.at(&first.object));fire(&first.object);assert(eventCalls==1);
    begin(pbz_damage_forget_bullet);pointer(&child.object);finish(1,1);
    fire(&child.object);assert(eventCalls==2);
    bulletInitializers.clear();
    begin(pbz_damage_reset);finish(0,1);
    assert(actionTable[3]==reinterpret_cast<void*>(&originalResourceAction));
    assert(eventTable[2]==reinterpret_cast<void*>(&originalResourceEvent));
    assert(abilityResources.empty() && bulletResources.empty());
    code[0]=0xcc;
    begin(pbz_resource_prepare_actions);pointer(&executeFunction);pointer(&getterFunction);
    assert(luaApi.pcall(state.lua,2,-1,0,0,nullptr)==0);
    assert(luaApi.gettop(state.lua)==2 && luaApi.tointeger(state.lua,1,nullptr)==0);
    assert(RtlDeleteFunctionTable(entries));VirtualFree(code,0,MEM_RELEASE);
    luaApi.close(state.lua);liveLuaState=nullptr;
    std::cout<<"WeaponResourceNativeTests PASS: native action qualification, branch isolation, launch snapshots, cost events and Lua exports\n";
}
