#define PBZ_CONTEXT_API
#include "../../prototype/input-router/native/script-context.cpp"
#include <cassert>
#include <cstdlib>
#include <iostream>
#include <unordered_map>

namespace
{
    struct TestState { long long depth = 1; bool enabled = true; long long address = 0; std::vector<std::string> names; void* lua = nullptr; };
    void* liveLuaState = nullptr;
    struct LuaApi
    {
        void* (*newstate)();
        void (*close)(void*);
        void (*pushinteger)(void*, long long);
        void (*pushnumber)(void*, double);
        void (*pushboolean)(void*, int);
        void (*pushcclosure)(void*, int(*)(void*), int);
        const char* (*pushlstring)(void*, const char*, size_t);
        long long (*tointeger)(void*, int, int*);
        double (*tonumber)(void*, int, int*);
        void (*rotate)(void*, int, int);
        void (*settop)(void*, int);
        int (*gettop)(void*);
        int (*toboolean)(void*, int);
        const char* (*tolstring)(void*, int, size_t*);
        int (*pcall)(void*, int, int, int, intptr_t, void*);
        void* (*newuserdata)(void*, size_t, int);
        void* (*touserdata)(void*, int);
        void load(const char* path)
        {
            auto library = LoadLibraryA(path);
            assert(library);
#define LUA_FUNCTION(field, name) field = reinterpret_cast<decltype(field)>(GetProcAddress(library, name)); assert(field)
            LUA_FUNCTION(newstate, "luaL_newstate");
            LUA_FUNCTION(close, "lua_close");
            LUA_FUNCTION(pushinteger, "lua_pushinteger");
            LUA_FUNCTION(pushnumber, "lua_pushnumber");
            LUA_FUNCTION(pushboolean, "lua_pushboolean");
            LUA_FUNCTION(pushcclosure, "lua_pushcclosure");
            LUA_FUNCTION(pushlstring, "lua_pushlstring");
            LUA_FUNCTION(tointeger, "lua_tointegerx");
            LUA_FUNCTION(tonumber, "lua_tonumberx");
            LUA_FUNCTION(rotate, "lua_rotate");
            LUA_FUNCTION(settop, "lua_settop");
            LUA_FUNCTION(gettop, "lua_gettop");
            LUA_FUNCTION(toboolean, "lua_toboolean");
            LUA_FUNCTION(tolstring, "lua_tolstring");
            LUA_FUNCTION(pcall, "lua_pcallk");
            LUA_FUNCTION(newuserdata, "lua_newuserdatauv");
            LUA_FUNCTION(touserdata, "lua_touserdata");
#undef LUA_FUNCTION
        }
        void remove(void* state, int index) { rotate(state, index, -1); settop(state, -2); }
    } luaApi;
    RC::Unreal::Hook::ScriptCallback pre, post;
    std::unordered_map<const void*, std::wstring> names;
    std::unordered_map<void*, RC::Unreal::UFunction*> nodes;
    std::unordered_map<void*, unsigned char*> locals;
    using NativeExec = void(*)(RC::Unreal::UObject*, RC::Unreal::FFrame&, void*);
    std::unordered_map<const void*, NativeExec> nativeExecs;
    std::unordered_map<const void*,unsigned short> parameterSizes;
    std::unordered_map<const void*,std::function<void(RC::Unreal::UFunction*,void*)>> processEvents;
    std::unordered_map<const void*, RC::Unreal::FProperty*> properties;
    std::vector<RC::Unreal::UFunction::Callback> interfacePre, interfacePost;
    std::unordered_map<const void*,std::unordered_map<std::wstring,RC::Unreal::FProperty*>> namedProperties;
    std::unordered_map<const void*,std::unordered_map<std::wstring,RC::Unreal::FProperty*>> inheritedProperties;
    std::unordered_map<const void*,int> propertyOffsets, propertySizes;
    std::unordered_map<const void*,RC::Unreal::TObjectPtr<RC::Unreal::UScriptStruct>> propertyStructs;
    std::unordered_map<uint64_t,std::wstring> constructedNames;
    std::unordered_map<const void*,RC::Unreal::TArray<RC::Unreal::FImplementedInterface>> implementedInterfaces;
    const TestState* test(const RC::LuaMadeSimple::Lua* lua) { return *reinterpret_cast<TestState* const*>(lua); }
    void* stack(const RC::LuaMadeSimple::Lua* lua)
    {
        auto* state = *reinterpret_cast<void* const*>(lua);
        return state == liveLuaState ? state : static_cast<TestState*>(state)->lua;
    }
    RC::Unreal::FScriptMapLayout mapLayout{8, {12, 16, 20, {4, 20}}};
    int propertySize = 4;
}
namespace RC::Unreal
{
    std::wstring UObject::GetFullName(UObject*) const { return names.at(this); }
    void UObject::ProcessEvent(UFunction* function,void* parameters) { processEvents.at(this)(function,parameters); }
    UFunction*& FFrame::Node() { return nodes.at(this); }
    unsigned char*& FFrame::Locals() { return locals.at(this); }
    FName::FName(const wchar_t* value,EFindName,void*)
    {
        uint64_t identity=1;
        for (const auto& entry:constructedNames)
            if (entry.second==value) { std::memcpy(this,&entry.first,sizeof(entry.first));return; }
        while (constructedNames.count(identity)) ++identity;
        constructedNames[identity]=value;
        std::memcpy(this,&identity,sizeof(identity));
    }
    FProperty* UStruct::FindProperty(FName name) { return namedProperties[this][name.ToString()]; }
    TObjectPtr<UScriptStruct>& FStructProperty::GetStruct() { return propertyStructs[this]; }
    int UFunction::RegisterPreHook(const Callback& callback, void*) { interfacePre.push_back(callback); return 1; }
    int UFunction::RegisterPostHook(const Callback& callback, void*) { interfacePost.push_back(callback); return 2; }
    auto UFunction::GetFuncPtr() -> void(*)(UObject*, FFrame&, void*) { return nativeExecs[this]; }
    unsigned short& UFunction::GetParmsSize() { return parameterSizes[this]; }
    FProperty* UObject::GetPropertyByName(const wchar_t* name)
    {
        auto own = namedProperties.find(this);
        if (own!=namedProperties.end())
        {
            auto found=own->second.find(name);
            if (found!=own->second.end()) return found->second;
        }
        auto field=properties.find(this);
        return field==properties.end() ? nullptr : field->second;
    }
    FProperty* UObject::GetPropertyByNameInChain(const wchar_t* name)
    {
        if (auto* own=GetPropertyByName(name)) return own;
        auto base=inheritedProperties.find(this);
        if (base==inheritedProperties.end()) return nullptr;
        auto found=base->second.find(name);
        return found==base->second.end() ? nullptr : found->second;
    }

    int& FProperty::GetOffset_Internal() { return propertyOffsets[this]; }
    TArray<FImplementedInterface>& UClass::GetInterfaces() { return implementedInterfaces[this]; }
    int& FProperty::GetElementSize() { auto found=propertySizes.find(this); if (found!=propertySizes.end()) return found->second; static int tagSize = 8; return this == reinterpret_cast<FProperty*>(1) ? tagSize : propertySize; }
    FScriptMapLayout& FMapProperty::GetMapLayout() { return mapLayout; }
    FProperty*& FMapProperty::GetKeyProp() { static FProperty* value = reinterpret_cast<FProperty*>(1); return value; }
    FProperty*& FMapProperty::GetValueProp() { static FProperty* value = reinterpret_cast<FProperty*>(2); return value; }
    const std::wstring FName::ToString() const
    {
        auto explicitName=names.find(this);
        if (explicitName!=names.end()) return explicitName->second;
        uint64_t identity;
        std::memcpy(&identity,this,sizeof(identity));
        return constructedNames.at(identity);
    }
    namespace Hook
    {
        void RegisterProcessLocalScriptFunctionPreCallback(ScriptCallback callback) { pre = std::move(callback); }
        void RegisterProcessLocalScriptFunctionPostCallback(ScriptCallback callback) { post = std::move(callback); }
    }
}
namespace RC::LuaMadeSimple
{
    long long Lua::get_integer(int index) const
    {
        auto* lua = stack(this);
        if (!lua) { assert(index == 1); return test(this)->depth; }
        auto value = luaApi.tointeger(lua, index, nullptr);
        luaApi.remove(lua, index);
        return value;
    }
    bool Lua::get_bool(int index) const
    {
        auto* lua = stack(this);
        if (!lua) { assert(index == 1); return test(this)->enabled; }
        bool value = luaApi.toboolean(lua, index) != 0;
        luaApi.remove(lua, index);
        return value;
    }
    double Lua::get_number(int index) const
    {
        auto* lua = stack(this);
        assert(lua);
        auto value = luaApi.tonumber(lua, index, nullptr);
        luaApi.remove(lua, index);
        return value;
    }
    void Lua::set_integer(long long value) const
    {
        auto* lua = stack(this);
        if (*reinterpret_cast<void* const*>(this) != liveLuaState) const_cast<TestState*>(test(this))->address = value;
        if (lua) luaApi.pushinteger(lua, value);
    }
    void Lua::set_number(double value) const { luaApi.pushnumber(stack(this), value); }
    void Lua::set_string(const char* text, size_t length) const
    {
        auto* lua = stack(this);
        if (*reinterpret_cast<void* const*>(this) != liveLuaState) const_cast<TestState*>(test(this))->names.emplace_back(text,length);
        if (lua) luaApi.pushlstring(lua, text, length);
    }
}

namespace RC::LuaType
{
    void push_classproperty(const PusherParams& params)
    {
        static_assert(sizeof(PusherParams)==0x30);
        static_assert(offsetof(PusherParams,data)==0x18);
        const RC::LuaMadeSimple::Lua* reference = nullptr;
        std::memcpy(&reference,reinterpret_cast<const unsigned char*>(&params)+0x8,sizeof(reference));
        assert(reference==&params.lua);
        assert(params.operation==0 && !params.base && !params.property);
        assert(params.data && !*static_cast<RC::Unreal::UClass**>(params.data));
        auto* state = stack(&params.lua);
        auto** value = static_cast<RC::Unreal::UClass**>(luaApi.newuserdata(state,sizeof(RC::Unreal::UClass*),0));
        *value = *static_cast<RC::Unreal::UClass**>(params.data);
    }
}

namespace
{
    struct NativeObject { void** table; };
    struct DamagePair { uint64_t tag; float value; int hashNext, hashIndex; };
    static_assert(sizeof(DamagePair) == 24);
    struct TestPayload { Object* source; void* nativeInterface; DamageMap map; };
    struct TestBulletInterface : NativeInterface
    {
        Object* owner;
        Object* _getUObject() const override { return owner; }
    };
    struct TestModule { Object* bullet; void* nativeInterface; };
    template<size_t InterfaceOffset> struct TestBullet
    {
        Object object;
        unsigned char padding[InterfaceOffset-sizeof(Object)];
        TestBulletInterface native;
        RC::Unreal::TArray<Object*> modules{};
        TestModule module{};
        Object* modulePointer = nullptr;
    };
    Object* initializationCalculator = nullptr;
    TestPayload* initializationPayload = nullptr;
    DamageMap* initializationOutput = nullptr;
    std::unordered_map<Object*,Object*> initializationModules;
    void originalBulletInitialize(NativeInterface* native, const void*, Object*, Object*, Object*, bool)
    {
        auto* object = native->_getUObject();
        auto& module = initializationModules.at(object);
        *reinterpret_cast<TestModule*>(module) = {object,native};
        auto* property = object->GetPropertyByNameInChain(L"Modules");
        auto& modules = *reinterpret_cast<RC::Unreal::TArray<Object*>*>(reinterpret_cast<unsigned char*>(object)+property->GetOffset_Internal());
        modules = {&module,1,1};
        initializationPayload->source = module;
        auto calculate = reinterpret_cast<DamageCalculator>((*reinterpret_cast<void***>(initializationCalculator))[0]);
        calculate(initializationCalculator,nullptr,nullptr,nullptr,nullptr,&initializationPayload->map,nullptr,initializationOutput);
    }
    int calculatorCalls = 0;
    bool nestedCalculation = false;
    DamageMap* nestedOutput = nullptr;
    const void* nestedInput = nullptr;
    void originalCalculator(Object* self, const void*, Object*, Object*, const void*, const void* input, const void*, void* output)
    {
        ++calculatorCalls;
        auto& map = *static_cast<DamageMap*>(output);
        auto* bits = map.externalBits ? map.externalBits : map.inlineBits;
        for (int i = 0; i < map.num; ++i)
            if (bits[i / 32] & (uint32_t(1) << (i % 32)))
                reinterpret_cast<DamagePair*>(map.data)[i].value = 10;
        if (nestedCalculation)
        {
            nestedCalculation = false;
            reinterpret_cast<DamageCalculator>((*reinterpret_cast<void***>(self))[0])(
                self, nullptr, nullptr, nullptr, nullptr, nestedInput, nullptr, nestedOutput);
        }
    }
    void nativeDamageTests(TestState& state)
    {
        void* calcTable[] = {reinterpret_cast<void*>(&originalCalculator)};
        NativeObject first{}, second{}, calculator{calcTable};
        auto* module = reinterpret_cast<Object*>(&first);
        auto* ordinary = reinterpret_cast<Object*>(&second);
        auto* calc = reinterpret_cast<Object*>(&calculator);
        DamagePair data[5]{};
        DamageMap output{reinterpret_cast<unsigned char*>(data), 5, 5, {0x1b}, nullptr, 5, 128, 2, 1};
        names[&data[0]] = L"Damage.Damage.Target.GeneralDamage";
        names[&data[1]] = L"Damage.Damage.Target.GeneralDamage.Fire";
        names[&data[3]] = L"Damage.Attribute.Target.Anger";
        names[&data[4]] = L"Damage.Damage.Target.GeneralDamageBonus";
        watchModule(module,3);
        watchCalculator(calc,0,{sizeof(DamagePair),offsetof(DamagePair,value)});
        auto calculate = reinterpret_cast<DamageCalculator>(calcTable[0]);
        TestPayload payload{module}, other{ordinary};
        damagePayloads[&payload.map] = {reinterpret_cast<Object*>(&payload),0};
        damagePayloads[&other.map] = {reinterpret_cast<Object*>(&other),0};
        auto run = [&] { calculate(calc,nullptr,nullptr,nullptr,nullptr,&payload.map,nullptr,&output); };
        run(); assert(data[0].value == 30 && data[1].value == 30);
        assert(data[3].value == 10 && data[4].value == 10);
        run(); assert(data[0].value == 30); // Every hit of this payload retains its source.
        payload.source = ordinary; run(); assert(data[0].value == 10);
        payload.source = nullptr; run(); assert(data[0].value == 10);
        watchModule(ordinary,7); payload.source = ordinary; run(); assert(data[0].value == 70);
        payload.source = module;
        watchModule(module,1); run(); assert(data[0].value == 10);
        watchModule(module,0); run(); assert(data[0].value == 0 && data[3].value == 10);
        watchModule(module,3);
        DamagePair nestedData{};
        names[&nestedData] = names[&data[0]];
        DamageMap nestedMap{reinterpret_cast<unsigned char*>(&nestedData),1,1,{1},nullptr,1,128,-1,0};
        nestedOutput = &nestedMap;
        nestedInput = &other.map;
        nestedCalculation = true;
        run(); assert(nestedData.value == 70 && data[0].value == 30);
        nestedInput = &payload.map; nestedCalculation = true;
        run(); assert(nestedData.value == 10 && data[0].value == 30); // Delegated calculator does not square scaling.
        uint64_t ordinaryInput = 0;
        nestedInput = &ordinaryInput; nestedCalculation = true;
        run(); assert(nestedData.value == 10 && data[0].value == 30);
        DamagePair many[130]{};
        uint32_t bits[5]{0,0,0,0,2};
        names[&many[129]] = names[&data[0]];
        DamageMap external{reinterpret_cast<unsigned char*>(many),130,130,{},bits,130,160,0,129};
        calculate(calc,nullptr,nullptr,nullptr,nullptr,&payload.map,nullptr,&external);
        assert(many[129].value == 30);
        state.depth = reinterpret_cast<long long>(module);
        assert(pbz_damage_forget_module(&state) == 1);
        run(); assert(data[0].value == 10);
        watchModule(module,3);
        state.depth = reinterpret_cast<long long>(&payload);
        assert(pbz_damage_forget_payload(&state) == 1);
        run(); assert(data[0].value == 10);
        pbz_damage_reset(&state);
        assert(moduleDamage.empty() && damagePayloads.empty() && calcTable[0]==reinterpret_cast<void*>(&originalCalculator));
        bool rejected = false;
        RC::Unreal::UFunction invalid;
        try { virtualSlot(&invalid); } catch (const std::runtime_error&) { rejected = true; }
        assert(rejected && calculatorCalls > 0);
        damageCalculators.clear();
    }

    void nativeLuaStackTests(const char* luaDll)
    {
        luaApi.load(luaDll);
        TestState state;
        state.lua = luaApi.newstate();
        assert(state.lua);
        liveLuaState = state.lua;
        auto begin = [&](int(*function)(void*))
        {
            luaApi.settop(state.lua, 0);
            luaApi.pushcclosure(state.lua, function, 0);
        };
        auto finish = [&](int arguments, int results)
        {
            assert(luaApi.pcall(state.lua, arguments, -1, 0, 0, nullptr) == 0);
            assert(luaApi.gettop(state.lua) == results);
        };
        auto pointer = [&](const void* value) { luaApi.pushinteger(state.lua, reinterpret_cast<long long>(value)); };
        begin(pbz_script_context_enable); luaApi.pushboolean(state.lua, 0); finish(1, 0);
        assert(!enabled.load());
        begin(pbz_script_context_empty_class); finish(0,1);
        auto** emptyClass = static_cast<RC::Unreal::UClass**>(luaApi.touserdata(state.lua,1));
        assert(emptyClass && !*emptyClass && !enabled.load() && !damageEnabled.load());
        begin(pbz_script_context_enable); luaApi.pushboolean(state.lua, 1); finish(1, 0);
        assert(enabled.load());
        RC::Unreal::UObject owner, function;
        RC::Unreal::FFrame frame;
        names[&owner] = L"Ability /Level.Player.Test";
        names[&function] = L"Function /Fixture:Execute";
        nodes[&frame] = reinterpret_cast<RC::Unreal::UFunction*>(&function);
        pre(&owner, frame, nullptr);
        begin(pbz_script_context_at); luaApi.pushinteger(state.lua, 1); finish(1, 3);
        assert(std::string(luaApi.tolstring(state.lua, 1, nullptr)) == "Ability /Level.Player.Test");
        assert(luaApi.tointeger(state.lua, 2, nullptr) == reinterpret_cast<long long>(&owner));
        assert(std::string(luaApi.tolstring(state.lua, 3, nullptr)) == "Function /Fixture:Execute");
        post(&owner, frame, nullptr);
        begin(pbz_script_context_at); luaApi.pushinteger(state.lua, 1); finish(1, 0);
        RC::Unreal::UFunction interfaceFunction;
        auto registered = interfacePre.size();
        begin(pbz_script_context_watch_interface); pointer(&interfaceFunction); finish(1, 0);
        begin(pbz_script_context_watch_interface); pointer(&interfaceFunction); finish(1, 0);
        assert(interfacePre.size() == registered + 1);
        struct Bullet : NativeInterface
        {
            RC::Unreal::UObject owner;
            RC::Unreal::UObject* _getUObject() const override { return const_cast<RC::Unreal::UObject*>(&owner); }
        } bullet;
        names[&bullet.owner] = L"Bullet /Level.Projectile";
        RC::Unreal::UnrealScriptFunctionCallableContext context{
            reinterpret_cast<RC::Unreal::UObject*>(static_cast<NativeInterface*>(&bullet)), frame, nullptr};
        begin(pbz_script_context_interface_address); finish(0, 0);
        interfacePre.back()(context, nullptr);
        begin(pbz_script_context_interface_address); finish(0, 1);
        assert(luaApi.tointeger(state.lua,1,nullptr) == reinterpret_cast<long long>(&bullet.owner));
        begin(pbz_script_context_interface_object); finish(0, 1);
        assert(std::string(luaApi.tolstring(state.lua, 1, nullptr)) == "Bullet /Level.Projectile");
        interfacePost.back()(context, nullptr);
        begin(pbz_script_context_interface_object); finish(0, 0);
        begin(pbz_script_context_interface_address); finish(0, 0);
        RC::Unreal::UFunction chargeFunction, unrelatedFunction, invalidChargeFunction;
        RC::Unreal::FProperty deltaProperty, invalidDeltaProperty;
        struct TickParameters { int marker = 19; float delta = .125f; } tickParameters, nestedParameters;
        propertyOffsets[&deltaProperty]=offsetof(TickParameters,delta);
        propertySizes[&deltaProperty]=sizeof(float);
        propertySizes[&invalidDeltaProperty]=sizeof(double);
        namedProperties[&chargeFunction][L"DeltaTime"]=&deltaProperty;
        namedProperties[&invalidChargeFunction][L"DeltaTime"]=&invalidDeltaProperty;
        begin(pbz_charge_watch_tick); pointer(&invalidChargeFunction); finish(1,1);
        assert(luaApi.tolstring(state.lua,1,nullptr) && !chargeTicks.count(&invalidChargeFunction));
        begin(pbz_charge_watch_tick); pointer(&chargeFunction); finish(1,1);
        assert(luaApi.tointeger(state.lua,1,nullptr)==1);
        RC::Unreal::FFrame tickFrame, nestedTickFrame;
        RC::Unreal::UObject ordinaryOwner;
        nodes[&tickFrame]=nodes[&nestedTickFrame]=&chargeFunction;
        locals[&tickFrame]=reinterpret_cast<unsigned char*>(&tickParameters);
        locals[&nestedTickFrame]=reinterpret_cast<unsigned char*>(&nestedParameters);
        auto chargeRate = [&](double rate) {
            begin(pbz_charge_ability); pointer(&owner); luaApi.pushnumber(state.lua,rate); finish(2,1);
            assert(luaApi.tointeger(state.lua,1,nullptr)==1);
        };
        for (double rate : {.5,2.,10.})
        {
            chargeRate(rate);
            pre(&owner,tickFrame,nullptr);
            assert(tickParameters.marker==19 && tickParameters.delta==.125f*rate);
            assert(tickParameters.delta*350*.34==.125*rate*350*.34);
            pre(&ordinaryOwner,nestedTickFrame,nullptr);
            assert(nestedParameters.delta==.125f);
            post(&ordinaryOwner,nestedTickFrame,nullptr);
            assert(tickParameters.delta==.125f*rate);
            post(&owner,tickFrame,nullptr);
            assert(tickParameters.delta==.125f && chargeTickFrames.empty());
        }
        nodes[&tickFrame]=&unrelatedFunction;
        pre(&owner,tickFrame,nullptr); post(&owner,tickFrame,nullptr);
        assert(tickParameters.delta==.125f);
        nodes[&tickFrame]=&chargeFunction;
        chargeRate(1);
        pre(&owner,tickFrame,nullptr); post(&owner,tickFrame,nullptr);
        assert(tickParameters.delta==.125f && chargeRates.empty());
        chargeRate(2);
        begin(pbz_script_context_enable); luaApi.pushboolean(state.lua,0); finish(1,0);
        pre(&owner,tickFrame,nullptr); post(&owner,tickFrame,nullptr);
        assert(tickParameters.delta==.125f && chargeRates.empty() && chargeTicks.empty());
        begin(pbz_script_context_enable); luaApi.pushboolean(state.lua,1); finish(1,0);
        auto* code = static_cast<unsigned char*>(VirtualAlloc(nullptr, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
        assert(code);
        const unsigned char dispatch[] = {0xff, 0x90, 0, 0, 0, 0, 0xc3};
        std::memcpy(code, dispatch, sizeof(dispatch));
        code[16] = 1; // Minimal UNWIND_INFO for the bounded native exec fixture.
        const unsigned char initializeDispatch[] = {0x4c,0x8b,0x90,16,0,0,0,0x41,0xff,0xd2,0xc3};
        std::memcpy(code+128,initializeDispatch,sizeof(initializeDispatch)); code[144]=1;
        RUNTIME_FUNCTION entry[]{{0,sizeof(dispatch),16},{128,128+sizeof(initializeDispatch),144}};
        assert(RtlAddFunctionTable(entry,2,reinterpret_cast<DWORD64>(code)));
        RC::Unreal::UFunction getterFunction, calculatorFunction;
        nativeExecs[&getterFunction] = reinterpret_cast<NativeExec>(code);
        nativeExecs[&calculatorFunction] = reinterpret_cast<NativeExec>(code);
        RC::Unreal::FMapProperty map;
        properties[&calculatorFunction] = reinterpret_cast<RC::Unreal::FProperty*>(&map);
        mapLayout = {offsetof(DamagePair, value), {0, 0, sizeof(DamagePair), {alignof(DamagePair), sizeof(DamagePair)}}};
        void* calculatorTable[] = {reinterpret_cast<void*>(&originalCalculator)};
        NativeObject first{}, second{}, calculator{calculatorTable};
        auto* module = reinterpret_cast<Object*>(&first);
        auto* ordinary = reinterpret_cast<Object*>(&second);
        auto* calc = reinterpret_cast<Object*>(&calculator);
        auto watch = [&](Object* object, double multiplier)
        {
            begin(pbz_damage_watch_module);
            pointer(object); luaApi.pushnumber(state.lua, multiplier);
            finish(2, 1);
            assert(luaApi.gettop(state.lua) == 1 && luaApi.tointeger(state.lua, 1, nullptr) == 1);
        };
        watch(module, 11);
        assert(moduleDamage.at(module) == 11);
        begin(pbz_damage_watch_calculator);
        pointer(calc); pointer(&calculatorFunction);
        finish(2, 1);
        assert(luaApi.gettop(state.lua) == 1 && luaApi.tointeger(state.lua, 1, nullptr) == 1);
        assert(damageCalculators.at(calculatorTable).layout.stride == sizeof(DamagePair));
        DamagePair pair{};
        names[&pair] = L"Damage.Damage.Target.GeneralDamage";
        DamageMap output{reinterpret_cast<unsigned char*>(&pair), 1, 1, {1}, nullptr, 1, 128, -1, 0};
        const unsigned char payloadGetter[] = {0x48,0x8b,0x42,0x20,0x45,0x33,0xc9,0x48,0x85,0xc0,0x41,0x0f,0x95,0xc1,
            0x4c,0x03,0xc8,0x4c,0x89,0x4a,0x20,0x48,0x8d,0x91,16,0,0,0,0x49,0x8b,0xc8,0xe9,0,0,0,0};
        std::memcpy(code+64,payloadGetter,sizeof(payloadGetter));
        nativeExecs[&getterFunction] = reinterpret_cast<NativeExec>(code+64);
        TestPayload payload{module};
        inheritedProperties[&payload][L"DamageSource"] = reinterpret_cast<RC::Unreal::FProperty*>(3);
        auto* payloadObject = reinterpret_cast<Object*>(&payload);
        assert(!payloadObject->GetPropertyByName(L"DamageSource"));
        assert(payloadObject->GetPropertyByNameInChain(L"DamageSource") == reinterpret_cast<RC::Unreal::FProperty*>(3));
        begin(pbz_damage_watch_payload); pointer(&payload); pointer(&getterFunction); finish(2,1);
        assert(luaApi.tointeger(state.lua,1,nullptr) == 1 && damagePayloads.count(&payload.map));
        propertySizes[reinterpret_cast<RC::Unreal::FProperty*>(3)] = sizeof(void*)*2;
        begin(pbz_damage_payload_multiplier); pointer(&payload); finish(1,1);
        assert(luaApi.tonumber(state.lua,1,nullptr)==11);
        watch(module,.25);
        begin(pbz_damage_payload_multiplier); pointer(&payload); finish(1,1);
        assert(luaApi.tonumber(state.lua,1,nullptr)==.25);
        payload.source=ordinary;
        begin(pbz_damage_payload_multiplier); pointer(&payload); finish(1,1);
        assert(luaApi.tonumber(state.lua,1,nullptr)==1);
        payload.source=module;
        watch(module,11);
        auto hit = [&](Object* object)
        {
            payload.source = object;
            reinterpret_cast<DamageCalculator>(calculatorTable[0])(calc, nullptr, nullptr, nullptr, nullptr, &payload.map, nullptr, &output);
        };
        hit(module); assert(pair.value == 110);
        watch(ordinary, 1); hit(ordinary); assert(pair.value == 10);
        watch(module, 0.25); hit(module); assert(pair.value == 2.5);
        begin(pbz_damage_forget_module); pointer(module);
        finish(1, 1);
        assert(luaApi.gettop(state.lua) == 1 && luaApi.tointeger(state.lua, 1, nullptr) == 1);
        hit(module); assert(pair.value == 10);
        begin(pbz_damage_watch_module);
        luaApi.pushinteger(state.lua, 0); luaApi.pushnumber(state.lua, 11);
        finish(2, 2);
        assert(luaApi.gettop(state.lua) == 2 && luaApi.tointeger(state.lua, 1, nullptr) == 0);
        begin(pbz_damage_watch_calculator);
        pointer(calc); luaApi.pushinteger(state.lua, 0);
        finish(2, 2);
        assert(luaApi.gettop(state.lua) == 2 && luaApi.tointeger(state.lua, 1, nullptr) == 0);
        RC::Unreal::UClass bulletBase, objectBulletBase, bulletInterface;
        RC::Unreal::UFunction initializeFunction;
        nativeExecs[&initializeFunction]=reinterpret_cast<NativeExec>(code+128);
        TestBullet<0x2b0> firstBullet, normalBullet;
        TestBullet<0x28> childBullet;
        auto offset = reinterpret_cast<unsigned char*>(&firstBullet.native)-reinterpret_cast<unsigned char*>(&firstBullet.object);
        RC::Unreal::FImplementedInterface implemented{&bulletInterface,static_cast<int>(offset),false};
        implementedInterfaces[&bulletBase]={&implemented,1,1};
        auto objectOffset = reinterpret_cast<unsigned char*>(&childBullet.native)-reinterpret_cast<unsigned char*>(&childBullet.object);
        assert(offset==0x2b0 && objectOffset==0x28);
        RC::Unreal::FImplementedInterface objectImplemented{&bulletInterface,static_cast<int>(objectOffset),false};
        implementedInterfaces[&objectBulletBase]={&objectImplemented,1,1};
        RC::Unreal::FProperty modulesProperty, objectModulesProperty, ownerProperty;
        propertyOffsets[&modulesProperty]=static_cast<int>(reinterpret_cast<unsigned char*>(&firstBullet.modules)-reinterpret_cast<unsigned char*>(&firstBullet.object));
        propertyOffsets[&objectModulesProperty]=static_cast<int>(reinterpret_cast<unsigned char*>(&childBullet.modules)-reinterpret_cast<unsigned char*>(&childBullet.object));
        propertyOffsets[&ownerProperty]=0; propertySizes[&ownerProperty]=16;
        auto** originalTable = *reinterpret_cast<void***>(&firstBullet.native);
        void* bulletTable[]{originalTable[0],originalTable[1],reinterpret_cast<void*>(&originalBulletInitialize)};
        void* objectBulletTable[]{originalTable[0],originalTable[1],reinterpret_cast<void*>(&originalBulletInitialize)};
        auto prepare = [&](auto& bullet, void** table, RC::Unreal::FProperty* modules)
        {
            bullet.native.owner=&bullet.object;
            *reinterpret_cast<void***>(&bullet.native)=table;
            inheritedProperties[&bullet.object][L"Modules"]=modules;
            inheritedProperties[&bullet.module][L"Bullet"]=&ownerProperty;
            initializationModules[&bullet.object]=reinterpret_cast<Object*>(&bullet.module);
        };
        prepare(firstBullet,bulletTable,&modulesProperty);
        prepare(normalBullet,bulletTable,&modulesProperty);
        prepare(childBullet,objectBulletTable,&objectModulesProperty);
        assert(!firstBullet.object.GetPropertyByName(L"Modules"));
        assert(!childBullet.object.GetPropertyByName(L"Modules"));
        assert(firstBullet.object.GetPropertyByNameInChain(L"Modules")==&modulesProperty);
        assert(childBullet.object.GetPropertyByNameInChain(L"Modules")==&objectModulesProperty);
        auto* firstModule = reinterpret_cast<Object*>(&firstBullet.module);
        auto* childModule = reinterpret_cast<Object*>(&childBullet.module);
        assert(!firstModule->GetPropertyByName(L"Bullet"));
        assert(!childModule->GetPropertyByName(L"Bullet"));
        assert(firstModule->GetPropertyByNameInChain(L"Bullet")==&ownerProperty);
        assert(childModule->GetPropertyByNameInChain(L"Bullet")==&ownerProperty);
        begin(pbz_damage_prepare_bullet); pointer(&initializeFunction); finish(1,1);
        assert(luaApi.tointeger(state.lua,1,nullptr)==1 && bulletInitializeSlots.at(&initializeFunction)==2);
        assert(bulletTable[2]==reinterpret_cast<void*>(&originalBulletInitialize));
        code[128]=0xcc; // RegisterHook replaces the exec before the first bullet is constructed.
        begin(pbz_damage_watch_bullet); pointer(&firstBullet.object); pointer(&bulletBase); pointer(&bulletInterface); pointer(&initializeFunction); finish(4,1);
        assert(luaApi.tointeger(state.lua,1,nullptr)==1 && bulletTable[2]!=reinterpret_cast<void*>(&originalBulletInitialize));
        begin(pbz_damage_prepare_bullet); pointer(&initializeFunction); finish(1,1);
        assert(luaApi.tointeger(state.lua,1,nullptr)==1); // Re-enable never decodes the UE4SS dispatcher.
        begin(pbz_damage_watch_bullet); pointer(&childBullet.object); pointer(&objectBulletBase); pointer(&bulletInterface); pointer(&initializeFunction); finish(4,1);
        assert(luaApi.tointeger(state.lua,1,nullptr)==1);
        Object ability;
        auto setAbility = [&](double value) {
            begin(pbz_damage_ability); pointer(&ability); luaApi.pushnumber(state.lua,value); finish(2,1);
            assert(luaApi.tointeger(state.lua,1,nullptr)==1);
        };
        initializationCalculator=calc; initializationPayload=&payload; initializationOutput=&output;
        auto launch = [&](auto& bullet, const void* parent, Object* source) {
            auto initialize = reinterpret_cast<BulletInitialize>((*reinterpret_cast<void***>(&bullet.native))[2]);
            initialize(&bullet.native,parent,nullptr,source,nullptr,true);
        };
        setAbility(100);
        launch(childBullet,nullptr,&ability);
        assert(pair.value==1000 && bulletDamage.at(&childBullet.object)==100);
        begin(pbz_damage_observation); finish(0,1);
        auto observation = std::string(luaApi.tolstring(state.lua,1,nullptr));
        assert(observation.find("multiplier=100 before=10 after=1000")!=std::string::npos);
        assert(damageObservation.calculatorHooks > 0 && damageObservation.bulletHooks == 2);
        assert(observation.find("calculatorHooks="+std::to_string(damageObservation.calculatorHooks))!=std::string::npos);
        assert(observation.find("bulletHooks=2 registeredPayloads=1")!=std::string::npos);
        launch(firstBullet,nullptr,&ability);
        assert(pair.value==1000); // Damage occurs inside native Initialize before its return.
        assert(moduleDamage.at(reinterpret_cast<Object*>(&firstBullet.module))==100);
        setAbility(2);
        payload.source=reinterpret_cast<Object*>(&firstBullet.module);
        reinterpret_cast<DamageCalculator>(calculatorTable[0])(calc,nullptr,nullptr,nullptr,nullptr,&payload.map,nullptr,&output);
        assert(pair.value==1000); // The old projectile keeps its launch snapshot.
        void* parent[]{&firstBullet.object,&firstBullet.native};
        launch(childBullet,parent,&ability);
        assert(pair.value==1000 && bulletDamage.at(&childBullet.object)==100);
        begin(pbz_damage_bullet_snapshot); pointer(&childBullet.object); luaApi.pushnumber(state.lua,7); finish(2,1);
        assert(luaApi.tointeger(state.lua,1,nullptr)==1 && bulletDamage.at(&childBullet.object)==7);
        void* childParent[]{&childBullet.object,&childBullet.native};
        launch(normalBullet,childParent,&ability); assert(pair.value==70);
        launch(firstBullet,nullptr,nullptr); assert(pair.value==10 && !moduleDamage.count(reinterpret_cast<Object*>(&firstBullet.module)));
        void* ordinaryParent[]{&firstBullet.object,&firstBullet.native};
        launch(childBullet,ordinaryParent,&ability); assert(pair.value==10);
        begin(pbz_damage_forget_bullet); pointer(&normalBullet.object); finish(1,1);
        assert(luaApi.tointeger(state.lua,1,nullptr)==1 && !bulletDamage.count(&normalBullet.object) && !moduleDamage.count(reinterpret_cast<Object*>(&normalBullet.module)));
        begin(pbz_damage_reset); finish(0,1);
        assert(bulletDamage.empty() && abilityDamage.empty() && moduleBullets.empty());
        assert(calculatorTable[0]==reinterpret_cast<void*>(&originalCalculator));
        assert(bulletTable[2]==reinterpret_cast<void*>(&originalBulletInitialize));
        assert(objectBulletTable[2]==reinterpret_cast<void*>(&originalBulletInitialize));
        begin(pbz_damage_observation); finish(0,0);
        assert(luaApi.gettop(state.lua)==0);
        begin(pbz_script_context_enable); luaApi.pushboolean(state.lua,0); finish(1,0);
        launch(firstBullet,nullptr,&ability);
        assert(pair.value==10 && bulletDamage.empty() && moduleDamage.empty() && moduleBullets.empty());
        begin(pbz_script_context_enable); luaApi.pushboolean(state.lua,1); finish(1,0);
        // Re-enable the public hooks after reset restored every virtual slot.
        begin(pbz_damage_watch_calculator); pointer(calc); pointer(&calculatorFunction); finish(2,1);
        begin(pbz_damage_watch_bullet); pointer(&firstBullet.object); pointer(&bulletBase); pointer(&bulletInterface); pointer(&initializeFunction); finish(4,1);
        begin(pbz_damage_watch_bullet); pointer(&childBullet.object); pointer(&objectBulletBase); pointer(&bulletInterface); pointer(&initializeFunction); finish(4,1);
        begin(pbz_damage_watch_payload); pointer(&payload); pointer(&getterFunction); finish(2,1);
        setAbility(100); launch(firstBullet,nullptr,&ability); assert(pair.value==1000);
        setAbility(1); assert(abilityDamage.empty());
        begin(pbz_damage_forget_bullet); pointer(&firstBullet.object); finish(1,1);
        begin(pbz_damage_forget_bullet); luaApi.pushinteger(state.lua,0x123); finish(1,1);
        assert(!moduleDamage.count(reinterpret_cast<Object*>(&firstBullet.module)));

        code[64] = 0xcc;
        begin(pbz_damage_watch_payload); pointer(&payload); pointer(&getterFunction); finish(2,2);
        assert(luaApi.tointeger(state.lua,1,nullptr) == 0);
        begin(pbz_damage_forget_payload); pointer(&payload); finish(1,1);
        assert(luaApi.tointeger(state.lua,1,nullptr) == 1 && damagePayloads.empty());
        begin(pbz_damage_reset); finish(0, 1);
        assert(luaApi.tointeger(state.lua, 1, nullptr) == 1 && moduleDamage.empty() && damagePayloads.empty());
        luaApi.close(state.lua);
        liveLuaState = nullptr;
        damageCalculators.clear(); bulletInitializers.clear(); bulletInitializeSlots.clear();
        nativeExecs.clear(); properties.clear();
        assert(RtlDeleteFunctionTable(entry));
        VirtualFree(code, 0, MEM_RELEASE);
        std::cout << "All native context exports PASS through Lua C calls: argument consumption, return ordering, interface capture and damage isolation\n";
    }
}
void nativeStanceTests()
{
    using namespace RC::Unreal;
    UObject owner,otherOwner,passive,otherPassive,library;
    UFunction add,remove,down,release,otherFunction;
    FProperty ownerProperty,tagProperty,replicateProperty;
    propertyOffsets[&ownerProperty]=0; propertySizes[&ownerProperty]=sizeof(void*);
    propertyOffsets[&tagProperty]=8; propertySizes[&tagProperty]=sizeof(FName);
    propertyOffsets[&replicateProperty]=16; propertySizes[&replicateProperty]=sizeof(bool);
    for (auto* function : {&add,&remove})
    {
        auto& fields=namedProperties[function];
        fields[L"AbilitySystem"]=&ownerProperty;
        fields[L"GameplayTag"]=&tagProperty;
        fields[L"bShouldReplicate"]=&replicateProperty;
        parameterSizes[function]=24;
    }
    int count=0,otherCount=0,unrelatedCount=0;
    processEvents[&library]=[&](UFunction* function,void* raw) {
        auto* bytes=static_cast<unsigned char*>(raw);
        auto* component=*reinterpret_cast<UObject**>(bytes);
        if (!component) return;
        auto name=reinterpret_cast<FName*>(bytes+8)->ToString();
        int& value=name!=L"CharacterState.AbilityState.Stance.HoldingStanceKey" ? unrelatedCount : component==&owner ? count : otherCount;
        value=(std::max)(0,value+(function==&add ? 1 : -1));
        assert(!*reinterpret_cast<bool*>(bytes+16));
    };
    TestState state; state.lua=luaApi.newstate(); liveLuaState=state.lua;
    auto begin=[&](int(*function)(void*)) { luaApi.settop(state.lua,0);luaApi.pushcclosure(state.lua,function,0); };
    auto pointer=[&](const void* value) { luaApi.pushinteger(state.lua,reinterpret_cast<long long>(value)); };
    auto finish=[&](int arguments) {
        assert(luaApi.pcall(state.lua,arguments,-1,0,0,nullptr)==0);
        assert(luaApi.gettop(state.lua)==1 && luaApi.tointeger(state.lua,1,nullptr)==1);
    };
    auto prepare=[&](bool held) {
        begin(pbz_stance_prepare);
        for (auto* object : {&owner,&passive,&library}) pointer(object);
        for (auto* function : {&add,&remove,&down,&release}) pointer(function);
        luaApi.pushboolean(state.lua,held);luaApi.pushinteger(state.lua,count);finish(9);
    };
    auto registered=interfacePre.size();
    prepare(false);
    assert(interfacePre.size()==registered+2 && stanceActive() && count==0);
    auto call=[&](UFunction* function,UObject* component,UObject* caller,UFunction* callerFunction,const wchar_t* tag=L"CharacterState.AbilityState.Stance.HoldingStanceKey") {
        FFrame scriptFrame,nativeFrame;
        nodes[&scriptFrame]=callerFunction;
        unsigned char parameters[24]{};
        *reinterpret_cast<UObject**>(parameters)=component;
        auto name=FName(tag,FNAME_Add,nullptr);std::memcpy(parameters+8,&name,sizeof(name));
        locals[&nativeFrame]=parameters;
        UnrealScriptFunctionCallableContext context{&library,nativeFrame,nullptr};
        pre(caller,scriptFrame,nullptr);
        auto index=registered+(function==&add ? 0 : 1);
        interfacePre[index](context,nullptr);
        library.ProcessEvent(function,parameters);
        interfacePost[index](context,nullptr);
        assert(*reinterpret_cast<UObject**>(parameters)==component);
        post(caller,scriptFrame,nullptr);
        assert(stanceFrames.empty() && suppressedStanceCalls.empty());
    };
    call(&add,&owner,&passive,&down);assert(count==0);
    prepare(true);assert(count==1);
    call(&add,&owner,&passive,&down);assert(count==1);
    call(&remove,&owner,&passive,&release);assert(count==1);
    prepare(true);assert(count==1 && interfacePre.size()==registered+2);
    prepare(false);assert(count==0);
    call(&add,&owner,&passive,&down);assert(count==0);
    prepare(true);assert(count==1);
    prepare(false);assert(count==0);
    call(&remove,&owner,&passive,&release);assert(count==0);
    call(&add,&otherOwner,&passive,&down);assert(otherCount==1 && count==0);
    call(&add,&owner,&otherPassive,&down);assert(count==1);
    call(&add,&owner,&passive,&otherFunction);assert(count==2);
    call(&add,&owner,&passive,&down,L"Unrelated.Tag");assert(unrelatedCount==1 && count==2);
    prepare(true);assert(count==3);
    prepare(false);assert(count==2);
    begin(pbz_stance_restore);pointer(&owner);luaApi.pushboolean(state.lua,true);finish(2);
    assert(!stanceActive() && count==3);
    call(&remove,&owner,&passive,&release);assert(count==2);
    prepare(true);assert(count==2);
    begin(pbz_stance_restore);pointer(&owner);luaApi.pushboolean(state.lua,false);finish(2);
    assert(!stanceActive() && count==1);
    count=1;prepare(false);assert(count==0);
    begin(pbz_stance_restore);pointer(nullptr);luaApi.pushboolean(state.lua,false);finish(2);
    assert(!stanceActive() && count==0);
    luaApi.close(state.lua);liveLuaState=nullptr;
}

int main(int argc, char** argv)
{
    _set_error_mode(_OUT_TO_STDERR);
    TestState state;
    assert(pbz_script_context_enable(&state) == 0);
    RC::Unreal::UObject outer, inner, outerFn, innerFn;
    RC::Unreal::FFrame outerFrame, innerFrame;
    names[&outer] = L"GA /Level.Player.Ability";
    names[&inner] = L"GAA /Level.Player.Action";
    names[&outerFn] = L"Function /Base:Activate";
    names[&innerFn] = L"Function /Action:Comsume";
    nodes[&outerFrame] = reinterpret_cast<RC::Unreal::UFunction*>(&outerFn);
    nodes[&innerFrame] = reinterpret_cast<RC::Unreal::UFunction*>(&innerFn);
    assert(pbz_script_context_at(&state) == 0);
    pre(&outer,outerFrame,nullptr);
    pre(&inner,innerFrame,nullptr);
    assert(pbz_script_context_at(&state) == 3);
    assert(state.address == reinterpret_cast<long long>(&inner));
    assert(state.names[0] == "GAA /Level.Player.Action" && state.names[1] == "Function /Action:Comsume");
    state.depth = 2; state.names.clear();
    assert(pbz_script_context_at(&state) == 3);
    assert(state.address == reinterpret_cast<long long>(&outer));
    post(&inner,innerFrame,nullptr);
    assert(pbz_script_context_at(&state) == 0);
    state.depth = 1;
    state.enabled = false; pbz_script_context_enable(&state);
    pre(&inner,innerFrame,nullptr); post(&inner,innerFrame,nullptr);
    assert(pbz_script_context_at(&state) == 0 && frames.empty());
    post(&outer,outerFrame,nullptr);
    assert(pbz_script_context_at(&state) == 0);
    state.enabled = true; pbz_script_context_enable(&state);
    pre(&outer,outerFrame,nullptr); post(&outer,outerFrame,nullptr);
    assert(pbz_script_context_at(&state) == 0);
    RC::Unreal::UFunction initialization;
    state.depth = reinterpret_cast<long long>(&initialization);
    pbz_script_context_watch_interface(&state);
    pbz_script_context_watch_interface(&state);
    assert(interfacePre.size() == 1 && interfacePost.size() == 1);
    struct Owner : NativeInterface
    {
        RC::Unreal::UObject object;
        RC::Unreal::UObject* _getUObject() const override { return const_cast<RC::Unreal::UObject*>(&object); }
    } first, second;
    assert(static_cast<void*>(static_cast<NativeInterface*>(&first)) != static_cast<void*>(&first.object));
    names[&first.object] = L"Bullet /Level.First";
    names[&second.object] = L"Bullet /Level.Second";
    RC::Unreal::UnrealScriptFunctionCallableContext firstCall{
        reinterpret_cast<RC::Unreal::UObject*>(static_cast<NativeInterface*>(&first)),outerFrame,nullptr};
    RC::Unreal::UnrealScriptFunctionCallableContext secondCall{
        reinterpret_cast<RC::Unreal::UObject*>(static_cast<NativeInterface*>(&second)),innerFrame,nullptr};
    assert(pbz_script_context_interface_object(&state) == 0);
    interfacePre[0](firstCall,nullptr);
    assert(firstCall.Context != &first.object);
    state.names.clear();
    assert(pbz_script_context_interface_object(&state) == 1 && state.names.back() == "Bullet /Level.First");
    interfacePre[0](secondCall,nullptr);
    assert(pbz_script_context_interface_object(&state) == 1 && state.names.back() == "Bullet /Level.Second");
    interfacePost[0](secondCall,nullptr);
    assert(pbz_script_context_interface_object(&state) == 1 && state.names.back() == "Bullet /Level.First");
    interfacePost[0](firstCall,nullptr);
    assert(pbz_script_context_interface_object(&state) == 0);
    state.enabled = false; pbz_script_context_enable(&state);
    interfacePre[0](firstCall,nullptr); interfacePost[0](firstCall,nullptr);
    assert(pbz_script_context_interface_object(&state) == 0);
    state.enabled = true; pbz_script_context_enable(&state);
    pbz_script_context_watch_interface(&state);
    assert(interfacePre.size() == 1);
    interfacePre[0](firstCall,nullptr);
    assert(pbz_script_context_interface_object(&state) == 1 && state.names.back() == "Bullet /Level.First");
    interfacePost[0](firstCall,nullptr);
    nativeDamageTests(state);
    nativeLuaStackTests(argc > 1 ? argv[1] : "C:\\Program Files\\Wireshark\\lua54.dll");
    nativeStanceTests();
    std::cout << "ScriptContextNativeTests PASS\n";
    return 0;
}
