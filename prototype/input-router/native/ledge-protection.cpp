#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <stdexcept>

#ifndef PBZ_CONTEXT_API
#define PBZ_CONTEXT_API __declspec(dllimport)
#endif
namespace RC::Unreal
{
    class FProperty
    {
    public:
        PBZ_CONTEXT_API int& GetElementSize();
        PBZ_CONTEXT_API int& GetOffset_Internal();
    };
    class UObject
    {
    public:
        PBZ_CONTEXT_API FProperty* GetPropertyByNameInChain(const wchar_t*);
    };
}
namespace RC::LuaMadeSimple
{
    class Lua
    {
    public:
        PBZ_CONTEXT_API long long get_integer(int) const;
        PBZ_CONTEXT_API void set_integer(long long) const;
        PBZ_CONTEXT_API void set_string(const char*, size_t) const;
    };
}

namespace PBZLedgeProtection
{
    using Gate = bool(*)(RC::Unreal::UObject*);
    using Walking = void(*)(RC::Unreal::UObject*, float, int);
    constexpr size_t walkOffSlot = 0x8f8 / sizeof(void*);
    constexpr size_t walkingSlot = 0x918 / sizeof(void*);
    RC::Unreal::UObject* movement = nullptr;
    Gate original = nullptr;
    void** slot = nullptr;
    Walking originalWalking = nullptr;
    void** walking = nullptr;
    int forceOffset = 0;
    int disableOffset = 0;

    bool can_walk_off(RC::Unreal::UObject* object)
    { return object == movement || original(object); }

    void phys_walking(RC::Unreal::UObject* object, float delta, int iterations)
    {
        if (object != movement) { originalWalking(object, delta, iterations); return; }
        auto* force = reinterpret_cast<int*>(reinterpret_cast<unsigned char*>(object) + forceOffset);
        auto* disable = reinterpret_cast<int*>(reinterpret_cast<unsigned char*>(object) + disableOffset);
        const int saved = *force;
        const int acquired = *disable == 0 ? 1 : 0;
        *force = 0;
        *disable += acquired;
        struct Restore
        {
            int* force;
            int* disable;
            int saved, acquired;
            ~Restore()
            {
                *force += saved;
                if (acquired && *disable > 0) --*disable;
            }
        } restore{force, disable, saved, acquired};
        originalWalking(object, delta, iterations);
    }

    void write(void** address, void* value)
    {
        DWORD protection;
        if (!VirtualProtect(address, sizeof(void*), PAGE_READWRITE, &protection))
            throw std::runtime_error("Cannot update the movement edge gate");
        InterlockedExchangePointer(address, value);
        DWORD ignored;
        if (!VirtualProtect(address, sizeof(void*), protection, &ignored))
            throw std::runtime_error("Cannot restore movement gate page protection");
    }

    void reset()
    {
        if (walking)
        {
            if (*walking != reinterpret_cast<void*>(phys_walking))
                throw std::runtime_error("Walking edge gate was replaced by another owner");
            write(walking, reinterpret_cast<void*>(originalWalking));
            walking = nullptr;
            originalWalking = nullptr;
        }
        if (!slot) return;
        if (*slot != reinterpret_cast<void*>(can_walk_off))
            throw std::runtime_error("Movement edge gate was replaced by another owner");
        try { write(slot, reinterpret_cast<void*>(original)); }
        catch (...)
        {
            if (*slot == reinterpret_cast<void*>(original))
            { slot = nullptr; movement = nullptr; original = nullptr; }
            throw;
        }
        slot = nullptr; movement = nullptr; original = nullptr;
    }

    void set(RC::Unreal::UObject* object)
    {
        if (object == movement) return;
        reset();
        if (!object) return;
        auto* property = object->GetPropertyByNameInChain(L"bForceEnableLedgeProtection");
        auto* disableProperty = object->GetPropertyByNameInChain(L"bForceDisableLedgeProtection");
        if (!property || property->GetElementSize() != sizeof(int))
            throw std::runtime_error("Movement edge counter must be a reflected integer");
        if (!disableProperty || disableProperty->GetElementSize() != sizeof(int))
            throw std::runtime_error("Movement disable counter must be a reflected integer");
        auto** candidate = &(*reinterpret_cast<void***>(object))[walkOffSlot];
        auto** walkingCandidate = &(*reinterpret_cast<void***>(object))[walkingSlot];
        const auto* code = static_cast<const unsigned char*>(*candidate);
        int offset; std::memcpy(&offset, code + 2, sizeof(offset));
        const unsigned char tail[] = {0,0x74,3,0x32,0xc0,0xc3,0xe9};
        if (code[0] != 0x83 || code[1] != 0xb9 || offset != property->GetOffset_Internal()
            || std::memcmp(code + 6, tail, sizeof(tail)) != 0)
            throw std::runtime_error("Native CanWalkOffLedges layout is unsupported");
        original = reinterpret_cast<Gate>(*candidate);
        originalWalking = reinterpret_cast<Walking>(*walkingCandidate);
        forceOffset = offset;
        disableOffset = disableProperty->GetOffset_Internal();
        movement = object;
        slot = candidate;
        try
        {
            write(candidate, reinterpret_cast<void*>(can_walk_off));
            write(walkingCandidate, reinterpret_cast<void*>(phys_walking));
            walking = walkingCandidate;
        }
        catch (...)
        {
            if (*walkingCandidate == reinterpret_cast<void*>(phys_walking)) walking = walkingCandidate;
            if (*candidate != reinterpret_cast<void*>(can_walk_off))
            { slot = nullptr; movement = nullptr; original = nullptr; }
            throw;
        }
    }
}

extern "C" __declspec(dllexport) int pbz_ledge_walk_off(void* state)
{
    struct View { void* state; } view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    try
    {
        PBZLedgeProtection::set(reinterpret_cast<RC::Unreal::UObject*>(lua.get_integer(1)));
        lua.set_integer(1); return 1;
    }
    catch (const std::exception& failure)
    {
        lua.set_integer(0); lua.set_string(failure.what(), std::strlen(failure.what())); return 2;
    }
}
