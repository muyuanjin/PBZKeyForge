#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <mutex>
#include <unordered_map>
#include <stdexcept>
#include <sstream>

namespace
{
    using Object = RC::Unreal::UObject;
    using DamageCalculator = void(*)(Object*, const void*, Object*, Object*, const void*, const void*, const void*, void*);
    struct DamageLayout { int stride; int valueOffset; };
    struct CalculatorHook { DamageCalculator original; DamageLayout layout; size_t slot; };
    std::mutex damageMutex;
    std::atomic<bool> damageEnabled{false};
    std::unordered_map<void**, CalculatorHook> damageCalculators;
    std::unordered_map<Object*, double> moduleDamage;
    std::unordered_map<Object*, double> abilityDamage, bulletDamage;
    std::unordered_map<Object*,Object*> moduleBullets;
    std::unordered_map<RC::Unreal::UFunction*,size_t> bulletInitializeSlots;
    using BulletInitialize = void(*)(NativeInterface*, const void*, Object*, Object*, Object*, bool);
    struct BulletHook { BulletInitialize original; int modulesOffset; size_t slot; };
    std::unordered_map<void**, BulletHook> bulletInitializers;
    struct DamagePayload { Object* object; int sourceOffset; };
    std::unordered_map<const void*, DamagePayload> damagePayloads;
    thread_local std::vector<const void*> calculatingDamage;
    struct DamageObservation
    {
        bool active = false;
        uint64_t launches = 0, calculations = 0, payloads = 0, sources = 0, values = 0;
        uint64_t calculatorHooks = 0, bulletHooks = 0;
        double multiplier = 1;
        float before = 0, after = 0;
    } damageObservation;

    // FScriptMap's sparse array uses the default four-word inline bit allocator.
    // Pair stride and value offset come from the reflected FMapProperty.
    struct DamageMap
    {
        unsigned char* data;
        int num, max;
        uint32_t inlineBits[4];
        uint32_t* externalBits;
        int numBits, maxBits;
        int firstFreeIndex, numFreeIndices;
    };
    static_assert(offsetof(DamageMap, externalBits) == 0x20);
    static_assert(offsetof(DamageMap, numBits) == 0x28);

    void scaleDamage(void* output, DamageLayout layout, double multiplier)
    {
        auto& map = *static_cast<DamageMap*>(output);
        const auto* bits = map.externalBits ? map.externalBits : map.inlineBits;
        const std::wstring root = L"Damage.Damage.Target.GeneralDamage";
        for (int i = 0; i < map.num; ++i)
        {
            if (!(bits[i / 32] & (uint32_t(1) << (i % 32)))) continue;
            auto* pair = map.data + layout.stride * i;
            auto tag = reinterpret_cast<RC::Unreal::FName*>(pair)->ToString();
            if (tag != root && !(tag.size() > root.size() && tag.compare(0, root.size(), root) == 0 && tag[root.size()] == L'.')) continue;
            auto* value = reinterpret_cast<float*>(pair + layout.valueOffset);
            auto before = *value;
            *value = static_cast<float>(*value * multiplier);
            std::lock_guard<std::mutex> lock(damageMutex);
            ++damageObservation.values;
            damageObservation.multiplier = multiplier;
            damageObservation.before = before;
            damageObservation.after = *value;
        }
    }

    void calculateDamage(Object* calculator, const void* attributes, Object* attacker, Object* target,
        const void* sourceTags, const void* attackData, const void* damageSource, void* output)
    {
        CalculatorHook hook;
        {
            std::lock_guard<std::mutex> lock(damageMutex);
            hook = damageCalculators.at(*reinterpret_cast<void***>(calculator));
        }
        if (!enabled.load(std::memory_order_relaxed) || !damageEnabled.load(std::memory_order_relaxed))
        {
            hook.original(calculator,attributes,attacker,target,sourceTags,attackData,damageSource,output);
            return;
        }
        double multiplier = 1;
        const bool outer = std::find(calculatingDamage.begin(), calculatingDamage.end(), attackData) == calculatingDamage.end();
        if (outer && enabled.load(std::memory_order_relaxed))
        {
            std::lock_guard<std::mutex> lock(damageMutex);
            if (damageObservation.active) ++damageObservation.calculations;
            auto payload = damagePayloads.find(attackData);
            if (payload != damagePayloads.end())
            {
                if (damageObservation.active) ++damageObservation.payloads;
                // FScriptInterface stores the UObject first, then its native interface.
                auto* source = *reinterpret_cast<Object**>(reinterpret_cast<unsigned char*>(payload->second.object) + payload->second.sourceOffset);
                auto setting = moduleDamage.find(source);
                if (setting != moduleDamage.end()) multiplier = setting->second;
                else if (source)
                {
                    // Newly created modules can deal damage inside Initialize,
                    // before the outer bullet initialization has returned.
                    auto* property = source->GetPropertyByNameInChain(L"Bullet");
                    if (property && property->GetElementSize() == sizeof(void*)*2)
                    {
                        auto* owner = *reinterpret_cast<Object**>(reinterpret_cast<unsigned char*>(source)+property->GetOffset_Internal());
                        auto snapshot = bulletDamage.find(owner);
                        if (snapshot != bulletDamage.end()) multiplier = snapshot->second;
                    }
                }
            }
        }
        if (multiplier != 1)
        {
            std::lock_guard<std::mutex> lock(damageMutex);
            ++damageObservation.sources;
        }
        struct CalculationScope
        {
            CalculationScope(const void* input) { calculatingDamage.push_back(input); }
            ~CalculationScope() { calculatingDamage.pop_back(); }
        } scope(attackData);
        hook.original(calculator, attributes, attacker, target, sourceTags, attackData, damageSource, output);
        if (multiplier != 1 && enabled.load(std::memory_order_relaxed) && damageEnabled.load(std::memory_order_relaxed))
            scaleDamage(output, hook.layout, multiplier);
    }

    int payloadMapOffset(RC::Unreal::UFunction* function)
    {
        auto* code = reinterpret_cast<const unsigned char*>(function->GetFuncPtr());
        // The leaf exec advances FFrame::Code, takes its map member and tail-calls
        // the engine map copy. Match the complete instruction sequence.
        const unsigned char prefix[] = {0x48,0x8b,0x42,0x20,0x45,0x33,0xc9,0x48,0x85,0xc0,0x41,0x0f,0x95,0xc1,
            0x4c,0x03,0xc8,0x4c,0x89,0x4a,0x20,0x48,0x8d,0x91};
        const unsigned char suffix[] = {0x49,0x8b,0xc8,0xe9};
        if (!code || std::memcmp(code,prefix,sizeof(prefix)) || std::memcmp(code+28,suffix,sizeof(suffix)))
            throw std::runtime_error("Damage payload map getter native layout is unsupported");
        int offset;
        std::memcpy(&offset,code+sizeof(prefix),sizeof(offset));
        if (offset <= 0) throw std::runtime_error("Damage payload map offset is invalid");
        return offset;
    }

    void watchPayload(Object* payload, RC::Unreal::UFunction* getter)
    {
        auto* property = payload->GetPropertyByNameInChain(L"DamageSource");
        if (!property) throw std::runtime_error("Damage payload has no DamageSource interface");
        auto offset = payloadMapOffset(getter);
        std::lock_guard<std::mutex> lock(damageMutex);
        damagePayloads[reinterpret_cast<unsigned char*>(payload)+offset] = {payload,property->GetOffset_Internal()};
    }

    // Native UHT exec thunks dispatch through one call [rax+virtual-slot].
    // Restrict inspection to the function's PE unwind extent, never adjacent code.
    size_t virtualSlot(RC::Unreal::UFunction* function)
    {
        auto* code = reinterpret_cast<unsigned char*>(function->GetFuncPtr());
        DWORD64 image = 0;
        auto* entry = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(code), &image, nullptr);
        if (!entry || image + entry->BeginAddress != reinterpret_cast<DWORD64>(code))
            throw std::runtime_error("Damage native exec has no function extent");
        size_t result = 0, matches = 0;
        auto length = static_cast<size_t>(entry->EndAddress - entry->BeginAddress);
        for (size_t i = 0; i + 6 <= length; ++i)
        {
            if (code[i] != 0xff || code[i + 1] != 0x90) continue;
            int displacement;
            std::memcpy(&displacement, code + i + 2, sizeof(displacement));
            if (displacement < 0 || displacement % sizeof(void*) != 0) continue;
            result = static_cast<size_t>(displacement) / sizeof(void*);
            ++matches;
        }
        if (matches != 1) throw std::runtime_error("Damage native exec virtual dispatch is ambiguous");
        return result;
    }

    void replaceVirtual(void** table, size_t slot, void* replacement)
    {
        DWORD oldProtection;
        if (!VirtualProtect(table + slot, sizeof(void*), PAGE_READWRITE, &oldProtection))
            throw std::runtime_error("Damage virtual dispatch is not writable");
        InterlockedExchangePointer(table + slot, replacement);
        DWORD ignored;
        VirtualProtect(table + slot, sizeof(void*), oldProtection, &ignored);
    }

    void watchModule(Object* module, double multiplier)
    {
        std::lock_guard<std::mutex> lock(damageMutex);
        if (multiplier == 1) moduleDamage.erase(module);
        else moduleDamage[module] = multiplier;
    }

    void forgetBulletModules(Object* object)
    {
        for (auto it=moduleBullets.begin(); it!=moduleBullets.end();)
            if (it->second==object) { moduleDamage.erase(it->first); it=moduleBullets.erase(it); }
            else ++it;
    }

    void rememberBulletModules(Object* object, int modulesOffset, double multiplier)
    {
        auto& modules = *reinterpret_cast<RC::Unreal::TArray<Object*>*>(reinterpret_cast<unsigned char*>(object)+modulesOffset);
        for (int i=0; i<modules.Num; ++i)
        {
            auto* module = modules.Data[i];
            if (!module) continue;
            moduleBullets[module]=object;
            if (multiplier == 1) moduleDamage.erase(module);
            else moduleDamage[module] = multiplier;
        }
    }

    void initializeBullet(NativeInterface* instance, const void* parent, Object* avatar, Object* ability, Object* data, bool local)
    {
        BulletHook hook;
        {
            std::lock_guard<std::mutex> lock(damageMutex);
            hook = bulletInitializers.at(*reinterpret_cast<void***>(instance));
        }
        if (!enabled.load(std::memory_order_relaxed) || !damageEnabled.load(std::memory_order_relaxed))
        {
            hook.original(instance,parent,avatar,ability,data,local);
            return;
        }
        auto* object = instance->_getUObject();
        double multiplier = 1;
        {
            std::lock_guard<std::mutex> lock(damageMutex);
            auto inherited = parent ? bulletDamage.find(*static_cast<Object* const*>(parent)) : bulletDamage.end();
            auto activation = abilityDamage.find(ability);
            if (inherited != bulletDamage.end()) multiplier = inherited->second;
            else if (activation != abilityDamage.end()) multiplier = activation->second;
            bulletDamage[object] = multiplier;
            if (multiplier != 1) ++damageObservation.launches;
            forgetBulletModules(object);
            rememberBulletModules(object,hook.modulesOffset,multiplier);
        }
        hook.original(instance,parent,avatar,ability,data,local);
        if (!enabled.load(std::memory_order_relaxed) || !damageEnabled.load(std::memory_order_relaxed)) return;
        std::lock_guard<std::mutex> lock(damageMutex);
        // Start callbacks may assign a binding to an already prepared bullet.
        auto snapshot = bulletDamage.find(object);
        rememberBulletModules(object,hook.modulesOffset,snapshot==bulletDamage.end() ? 1 : snapshot->second);
    }

    size_t bulletInitializeSlot(RC::Unreal::UFunction* function)
    {
        auto* code = reinterpret_cast<unsigned char*>(function->GetFuncPtr());
        DWORD64 image = 0;
        auto* entry = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(code),&image,nullptr);
        if (!entry || image+entry->BeginAddress != reinterpret_cast<DWORD64>(code))
            throw std::runtime_error("Bullet Initialize native exec has no function extent");
        auto length = static_cast<size_t>(entry->EndAddress-entry->BeginAddress);
        size_t matches = 0, slot = 0;
        bool callsTarget = false;
        for (size_t i=0; i+3<=length; ++i)
        {
            if (code[i]==0x41 && code[i+1]==0xff && code[i+2]==0xd2) callsTarget=true;
            if (i+7<=length && code[i]==0x4c && code[i+1]==0x8b && code[i+2]==0x90)
            {
                int displacement;
                std::memcpy(&displacement,code+i+3,sizeof(displacement));
                if (displacement<0 || displacement%sizeof(void*)) continue;
                slot=displacement/sizeof(void*); ++matches;
            }
        }
        if (!callsTarget || matches!=1) throw std::runtime_error("Bullet Initialize virtual dispatch is unsupported");
        return slot;
    }

    void watchBullet(Object* object, RC::Unreal::UClass* baseClass, RC::Unreal::UClass* interfaceClass, RC::Unreal::UFunction* function)
    {
        int interfaceOffset = 0;
        auto& interfaces = baseClass->GetInterfaces();
        for (int i=0; i<interfaces.Num; ++i)
            if (interfaces.Data[i].Class == interfaceClass && !interfaces.Data[i].bImplementedByK2)
                interfaceOffset=interfaces.Data[i].PointerOffset;
        auto* modules = object->GetPropertyByNameInChain(L"Modules");
        if (interfaceOffset<=0) throw std::runtime_error("Bullet native interface offset is unavailable");
        if (!modules) throw std::runtime_error("Bullet inherited Modules property is unavailable");
        auto** table = *reinterpret_cast<void***>(reinterpret_cast<unsigned char*>(object)+interfaceOffset);
        std::lock_guard<std::mutex> lock(damageMutex);
        auto cached = bulletInitializeSlots.find(function);
        if (cached==bulletInitializeSlots.end()) cached=bulletInitializeSlots.emplace(function,bulletInitializeSlot(function)).first;
        auto slot = cached->second;
        auto watched = bulletInitializers.find(table);
        const bool existed = watched != bulletInitializers.end();
        if (existed && table[slot] == reinterpret_cast<void*>(&initializeBullet)) return;
        auto previous = existed ? watched->second : BulletHook{};
        bulletInitializers[table] = {reinterpret_cast<BulletInitialize>(table[slot]),modules->GetOffset_Internal(),slot};
        try { replaceVirtual(table,slot,reinterpret_cast<void*>(&initializeBullet)); }
        catch (...)
        {
            if (existed) bulletInitializers[table] = previous;
            else bulletInitializers.erase(table);
            throw;
        }
        damageEnabled.store(true,std::memory_order_relaxed);
        ++damageObservation.bulletHooks;
    }

    void watchCalculator(Object* calculator, size_t slot, DamageLayout layout)
    {
        std::lock_guard<std::mutex> lock(damageMutex);
        auto** table = *reinterpret_cast<void***>(calculator);
        auto watched = damageCalculators.find(table);
        const bool existed = watched != damageCalculators.end();
        if (existed && table[slot] == reinterpret_cast<void*>(&calculateDamage)) return;
        auto previous = existed ? watched->second : CalculatorHook{};
        damageCalculators[table] = {reinterpret_cast<DamageCalculator>(table[slot]),layout,slot};
        try { replaceVirtual(table,slot,reinterpret_cast<void*>(&calculateDamage)); }
        catch (...)
        {
            if (existed) damageCalculators[table] = previous;
            else damageCalculators.erase(table);
            throw;
        }
        damageEnabled.store(true,std::memory_order_relaxed);
        ++damageObservation.calculatorHooks;
    }
}

extern "C" __declspec(dllexport) int pbz_damage_prepare_bullet(void* state)
{
    // UFunction hooks replace the exec pointer. Resolve it before registration,
    // even when no bullet CDO or instance has been constructed yet.
    LuaStateView view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    try
    {
        auto* function = reinterpret_cast<RC::Unreal::UFunction*>(lua.get_integer(1));
        if (!function) throw std::runtime_error("Bullet Initialize function is invalid");
        std::lock_guard<std::mutex> lock(damageMutex);
        if (bulletInitializeSlots.find(function) == bulletInitializeSlots.end())
            bulletInitializeSlots.emplace(function,bulletInitializeSlot(function));
        lua.set_integer(1);
        return 1;
    }
    catch (const std::exception& error)
    {
        lua.set_integer(0);
        lua.set_string(error.what(),std::strlen(error.what()));
        return 2;
    }
}

extern "C" __declspec(dllexport) int pbz_damage_watch_module(void* state)
{
    LuaStateView view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    try
    {
        // LuaMadeSimple getters remove their argument from the Lua stack.
        auto multiplier = lua.get_number(2);
        auto* module = reinterpret_cast<Object*>(lua.get_integer(1));
        if (!module || !std::isfinite(multiplier) || multiplier < 0)
            throw std::runtime_error("Damage module or multiplier is invalid");
        watchModule(module, multiplier);
        lua.set_integer(1);
        return 1;
    }
    catch (const std::exception& error)
    {
        lua.set_integer(0);
        lua.set_string(error.what(), std::strlen(error.what()));
        return 2;
    }
}

extern "C" __declspec(dllexport) int pbz_damage_watch_calculator(void* state)
{
    LuaStateView view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    try
    {
        auto* function = reinterpret_cast<RC::Unreal::UFunction*>(lua.get_integer(2));
        auto* calculator = reinterpret_cast<Object*>(lua.get_integer(1));
        if (!calculator || !function) throw std::runtime_error("Damage calculator is invalid");
        auto* property = reinterpret_cast<RC::Unreal::FMapProperty*>(
            reinterpret_cast<Object*>(function)->GetPropertyByName(L"DamageData"));
        if (!property || property->GetKeyProp()->GetElementSize() != 8 || property->GetValueProp()->GetElementSize() != 4)
            throw std::runtime_error("DamageData must be a gameplay-tag/float map");
        auto& layout = property->GetMapLayout();
        watchCalculator(calculator, virtualSlot(function), {layout.SetLayout.SparseArrayLayout.Size, layout.ValueOffset});
        lua.set_integer(1);
        return 1;
    }
    catch (const std::exception& error)
    {
        lua.set_integer(0);
        lua.set_string(error.what(), std::strlen(error.what()));
        return 2;
    }
}

extern "C" __declspec(dllexport) int pbz_damage_forget_module(void* state)
{
    LuaStateView view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    std::lock_guard<std::mutex> lock(damageMutex);
    auto* object = reinterpret_cast<Object*>(lua.get_integer(1));
    moduleDamage.erase(object);
    moduleBullets.erase(object);
    lua.set_integer(1);
    return 1;
}

extern "C" __declspec(dllexport) int pbz_damage_reset(void* state)
{
    LuaStateView view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    std::lock_guard<std::mutex> lock(damageMutex);
    damageEnabled.store(false,std::memory_order_relaxed);
    std::string failures;
    auto restore = [&](void** table, size_t slot, void* replacement, void* original)
    {
        if (table[slot] != replacement) return;
        try { replaceVirtual(table,slot,original); }
        catch (const std::exception& error)
        {
            if (!failures.empty()) failures += "\n";
            failures += error.what();
        }
    };
    for (auto& entry : damageCalculators)
        restore(entry.first,entry.second.slot,reinterpret_cast<void*>(&calculateDamage),reinterpret_cast<void*>(entry.second.original));
    for (auto& entry : bulletInitializers)
        restore(entry.first,entry.second.slot,reinterpret_cast<void*>(&initializeBullet),reinterpret_cast<void*>(entry.second.original));
    moduleDamage.clear();
    abilityDamage.clear();
    bulletDamage.clear();
    moduleBullets.clear();
    damagePayloads.clear();
    damageObservation = {};
    if (!failures.empty())
    {
        lua.set_integer(0);
        lua.set_string(failures.data(),failures.size());
        return 2;
    }
    lua.set_integer(1);
    return 1;
}

extern "C" __declspec(dllexport) int pbz_damage_watch_payload(void* state)
{
    LuaStateView view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    try
    {
        auto* getter = reinterpret_cast<RC::Unreal::UFunction*>(lua.get_integer(2));
        auto* payload = reinterpret_cast<Object*>(lua.get_integer(1));
        if (!payload || !getter) throw std::runtime_error("Damage payload or getter is invalid");
        watchPayload(payload,getter);
        lua.set_integer(1);
        return 1;
    }
    catch (const std::exception& error)
    {
        lua.set_integer(0);
        lua.set_string(error.what(),std::strlen(error.what()));
        return 2;
    }
}

extern "C" __declspec(dllexport) int pbz_damage_forget_payload(void* state)
{
    LuaStateView view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    auto* payload = reinterpret_cast<Object*>(lua.get_integer(1));
    std::lock_guard<std::mutex> lock(damageMutex);
    for (auto it=damagePayloads.begin(); it!=damagePayloads.end();)
        if (it->second.object == payload) it=damagePayloads.erase(it); else ++it;
    lua.set_integer(1);
    return 1;
}

extern "C" __declspec(dllexport) int pbz_damage_watch_bullet(void* state)
{
    LuaStateView view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    try
    {
        auto* function = reinterpret_cast<RC::Unreal::UFunction*>(lua.get_integer(4));
        auto* interfaceClass = reinterpret_cast<RC::Unreal::UClass*>(lua.get_integer(3));
        auto* baseClass = reinterpret_cast<RC::Unreal::UClass*>(lua.get_integer(2));
        auto* object = reinterpret_cast<Object*>(lua.get_integer(1));
        if (!object || !baseClass || !interfaceClass || !function) throw std::runtime_error("Bullet hook arguments are invalid");
        watchBullet(object,baseClass,interfaceClass,function);
        lua.set_integer(1); return 1;
    }
    catch (const std::exception& error)
    {
        lua.set_integer(0); lua.set_string(error.what(),std::strlen(error.what())); return 2;
    }
}

extern "C" __declspec(dllexport) int pbz_damage_ability(void* state)
{
    LuaStateView view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    auto multiplier = lua.get_number(2);
    auto* ability = reinterpret_cast<Object*>(lua.get_integer(1));
    if (!ability || !std::isfinite(multiplier) || multiplier<0) { lua.set_integer(0); return 1; }
    std::lock_guard<std::mutex> lock(damageMutex);
    if (multiplier==1) abilityDamage.erase(ability);
    else { abilityDamage[ability]=multiplier; damageObservation.active=true; }
    lua.set_integer(1); return 1;
}

extern "C" __declspec(dllexport) int pbz_damage_bullet_snapshot(void* state)
{
    LuaStateView view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    auto multiplier = lua.get_number(2);
    auto* object = reinterpret_cast<Object*>(lua.get_integer(1));
    if (!object || !std::isfinite(multiplier) || multiplier<0) { lua.set_integer(0); return 1; }
    auto* modules = object->GetPropertyByNameInChain(L"Modules");
    if (!modules) { lua.set_integer(0); return 1; }
    std::lock_guard<std::mutex> lock(damageMutex);
    bulletDamage[object]=multiplier;
    if (multiplier!=1) damageObservation.active=true;
    forgetBulletModules(object);
    rememberBulletModules(object,modules->GetOffset_Internal(),multiplier);
    lua.set_integer(1); return 1;
}

extern "C" __declspec(dllexport) int pbz_damage_forget_bullet(void* state)
{
    LuaStateView view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    auto* object = reinterpret_cast<Object*>(lua.get_integer(1));
    std::lock_guard<std::mutex> lock(damageMutex);
    bulletDamage.erase(object);
    forgetBulletModules(object);
    lua.set_integer(1); return 1;
}

extern "C" __declspec(dllexport) int pbz_damage_observation(void* state)
{
    std::lock_guard<std::mutex> lock(damageMutex);
    if (!damageObservation.active) return 0;
    std::ostringstream line;
    line << "Damage scaling (calculator output): launches=" << damageObservation.launches
        << " calculatorHooks=" << damageObservation.calculatorHooks << " bulletHooks=" << damageObservation.bulletHooks
        << " registeredPayloads=" << damagePayloads.size()
        << " calculations=" << damageObservation.calculations << " payloads=" << damageObservation.payloads
        << " sources=" << damageObservation.sources << " values=" << damageObservation.values
        << " multiplier=" << damageObservation.multiplier << " before=" << damageObservation.before
        << " after=" << damageObservation.after;
    auto message = line.str();
    LuaStateView view{state};
    reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view)->set_string(message.data(),message.size());
    return 1;
}
