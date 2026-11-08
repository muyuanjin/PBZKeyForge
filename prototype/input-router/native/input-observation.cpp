#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <vector>
#include <unordered_map>
#include <stdexcept>

#ifndef PBZ_CONTEXT_API
#define PBZ_CONTEXT_API __declspec(dllimport)
#endif

namespace RC::Unreal
{
    enum EFindName { FNAME_Find, FNAME_Add };
    class FName
    {
        uint32_t data[2];
    public:
        PBZ_CONTEXT_API FName(const wchar_t*, EFindName, void*);
        PBZ_CONTEXT_API static int StaticSize();
    };
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
    class UStruct
    {
    public:
        PBZ_CONTEXT_API FProperty* FindProperty(FName);
    };
    class UScriptStruct;
    template<class T> class TObjectPtr { public: T* Ptr; };
    class FStructProperty
    {
    public:
        PBZ_CONTEXT_API TObjectPtr<UScriptStruct>& GetStruct();
    };
    class FArrayProperty
    {
    public:
        PBZ_CONTEXT_API FProperty*& GetInner();
    };
    struct FScriptMapLayout
    {
        int ValueOffset;
        struct { int HashNextIdOffset, HashIndexOffset, Size; struct { int Alignment, Size; } SparseArrayLayout; } SetLayout;
    };
    class FMapProperty
    {
    public:
        PBZ_CONTEXT_API FScriptMapLayout& GetMapLayout();
        PBZ_CONTEXT_API FProperty*& GetValueProp();
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

namespace PBZInputObservation
{
    using namespace RC::Unreal;
    struct Array { unsigned char* data; int num, max; };
    struct Map
    {
        unsigned char* data;
        int num, max;
        uint32_t inlineBits[4];
        uint32_t* externalBits;
        int numBits, maxBits;
        int firstFreeIndex, numFreeIndices;
    };
    struct Span { const void* address; size_t offset, size; };
    struct Snapshot
    {
        const void* input = nullptr;
        std::vector<Span> spans;
        std::vector<unsigned char> bytes;

        void watch(const void* address, size_t size)
        {
            if (!size) return;
            auto offset = bytes.size();
            auto* source = static_cast<const unsigned char*>(address);
            bytes.insert(bytes.end(), source, source + size);
            spans.push_back({address,offset,size});
        }
        bool unchanged(const void* current) const
        {
            if (!input || input != current) return false;
            // Headers and owning pointers precede their allocations. Stop before
            // touching a retired allocation when a container or reference changes.
            for (const auto& span : spans)
                if (std::memcmp(span.address,bytes.data()+span.offset,span.size) != 0) return false;
            return true;
        }
    };

    std::unordered_map<void*, Snapshot> snapshots;
    std::unordered_map<long long, Snapshot> abilitySnapshots;
    long long nextAbilitySnapshot = 0;

    unsigned char* field(void* object, FProperty* property)
    {
        if (!property) throw std::runtime_error("Input observation property unavailable");
        return static_cast<unsigned char*>(object) + property->GetOffset_Internal();
    }
    FProperty* property(void* object, const wchar_t* name)
    {
        auto* result = static_cast<UObject*>(object)->GetPropertyByNameInChain(name);
        if (!result) throw std::runtime_error("Input observation object layout unavailable");
        return result;
    }
    FProperty* member(FProperty* structure, const wchar_t* name)
    {
        auto* type = reinterpret_cast<FStructProperty*>(structure)->GetStruct().Ptr;
        auto* result = reinterpret_cast<UStruct*>(type)->FindProperty(FName(name,FNAME_Find,nullptr));
        if (!result) throw std::runtime_error("Input observation struct layout unavailable");
        return result;
    }
    void scalar(Snapshot& out, void* object, FProperty* value)
    { out.watch(field(object,value),value->GetElementSize()); }
    void pointers(Snapshot& out, void* object, FProperty* value, bool thresholds)
    {
        auto& array = *reinterpret_cast<Array*>(field(object,value));
        out.watch(&array,sizeof(array));
        out.watch(array.data,array.num*sizeof(void*));
        if (!thresholds) return;
        for (int index=0; index<array.num; ++index)
        {
            void* trigger = reinterpret_cast<void**>(array.data)[index];
            scalar(out,trigger,property(trigger,L"ActuationThreshold"));
            auto* hold = static_cast<UObject*>(trigger)->GetPropertyByNameInChain(L"HoldTimeThreshold");
            if (hold) scalar(out,trigger,hold);
        }
    }
    void rows(Snapshot& out, void* object, FProperty* value)
    {
        auto& array = *reinterpret_cast<Array*>(field(object,value));
        out.watch(&array,sizeof(array));
        auto* inner = reinterpret_cast<FArrayProperty*>(value)->GetInner();
        auto* action = member(inner,L"Action");
        auto* key = member(inner,L"Key");
        auto* keyName = member(key,L"KeyName");
        auto* behavior = member(inner,L"SettingBehavior");
        auto* settings = member(inner,L"PlayerMappableKeySettings");
        auto* modifiers = member(inner,L"Modifiers");
        auto* triggers = member(inner,L"Triggers");
        for (int index=0; index<array.num; ++index)
        {
            auto* row = array.data + index*inner->GetElementSize();
            scalar(out,row,action);
            auto* inputAction = *reinterpret_cast<void**>(field(row,action));
            scalar(out,field(row,key),keyName);
            scalar(out,row,behavior);
            scalar(out,row,settings);
            auto* inherited = property(inputAction,L"PlayerMappableKeySettings");
            scalar(out,inputAction,inherited);
            auto choice = *field(row,behavior);
            auto* keySettings = choice==0 ? *reinterpret_cast<void**>(field(inputAction,inherited))
                : choice==1 ? *reinterpret_cast<void**>(field(row,settings)) : nullptr;
            if (keySettings) scalar(out,keySettings,property(keySettings,L"Name"));
            pointers(out,row,modifiers,false);
            pointers(out,row,triggers,true);
            pointers(out,inputAction,property(inputAction,L"Triggers"),false);
        }
    }
    void observe(void* input)
    {
        if (FName::StaticSize()!=sizeof(FName)) throw std::runtime_error("Input observation FName layout unsupported");
        Snapshot next;
        next.input = input;
        rows(next,input,property(input,L"EnhancedActionMappings"));
        auto* contextsProperty = property(input,L"AppliedInputContextData");
        auto contextStride = reinterpret_cast<FMapProperty*>(contextsProperty)->GetMapLayout().SetLayout.Size;
        if (contextStride<sizeof(void*)) throw std::runtime_error("Input context map layout unsupported");
        auto& contexts = *reinterpret_cast<Map*>(field(input,contextsProperty));
        next.watch(&contexts,sizeof(contexts));
        auto* bits = contexts.externalBits ? contexts.externalBits : contexts.inlineBits;
        if (contexts.externalBits) next.watch(bits,((contexts.numBits+31)/32)*sizeof(uint32_t));
        for (int index=0; index<contexts.num; ++index)
        {
            if (!(bits[index/32] & (uint32_t(1)<<(index%32)))) continue;
            auto* slot = contexts.data + index*contextStride;
            next.watch(slot,sizeof(void*));
            auto* context = *reinterpret_cast<void**>(slot);
            auto* defaults = property(context,L"DefaultKeyMappings");
            rows(next,field(context,defaults),member(defaults,L"Mappings"));
        }
        snapshots[input] = std::move(next);
    }

    void triggerBlock(Snapshot& out, void* block, FProperty* structure)
    {
        for (auto* name : {L"Actions", L"Combos", L"InterruptLower"})
        {
            auto* value = member(structure,name);
            auto& array = *reinterpret_cast<Array*>(field(block,value));
            out.watch(&array,sizeof(array));
            auto* inner = reinterpret_cast<FArrayProperty*>(value)->GetInner();
            auto* tag = member(inner,L"TriggerTag");
            auto* tagName = member(tag,L"TagName");
            for (int index=0; index<array.num; ++index)
                scalar(out,field(array.data+index*inner->GetElementSize(),tag),tagName);
        }
    }

    long long observeAbilities(void* asset, long long token)
    {
        if (FName::StaticSize()!=sizeof(FName)) throw std::runtime_error("Ability observation FName layout unsupported");
        Snapshot next;
        next.input = asset;
        auto* bindings = property(asset,L"Bindings");
        auto& array = *reinterpret_cast<Array*>(field(asset,bindings));
        next.watch(&array,sizeof(array));
        auto* binding = reinterpret_cast<FArrayProperty*>(bindings)->GetInner();
        auto* normal = member(binding,L"NormalData");
        auto* conditions = member(binding,L"TagConditionData");
        auto* mapProperty = reinterpret_cast<FMapProperty*>(conditions);
        auto layout = mapProperty->GetMapLayout();
        auto* value = mapProperty->GetValueProp();
        for (int index=0; index<array.num; ++index)
        {
            auto* entry = array.data+index*binding->GetElementSize();
            triggerBlock(next,field(entry,normal),normal);
            auto& map = *reinterpret_cast<Map*>(field(entry,conditions));
            next.watch(&map,sizeof(map));
            auto* bits = map.externalBits ? map.externalBits : map.inlineBits;
            if (map.externalBits) next.watch(bits,((map.numBits+31)/32)*sizeof(uint32_t));
            for (int slot=0; slot<map.num; ++slot)
                if (bits[slot/32] & (uint32_t(1)<<(slot%32)))
                    triggerBlock(next,map.data+slot*layout.SetLayout.Size+layout.ValueOffset,value);
        }
        if (!token) token = ++nextAbilitySnapshot;
        abilitySnapshots[token] = std::move(next);
        return token;
    }
}

extern "C" __declspec(dllexport) int pbz_input_mappings_observe(void* state)
{
    struct LuaView { void* state; } view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    try { PBZInputObservation::observe(reinterpret_cast<void*>(lua.get_integer(1))); }
    catch (const std::exception& failure)
    {
        PBZInputObservation::snapshots.erase(reinterpret_cast<void*>(lua.get_integer(1)));
        lua.set_string(failure.what(),std::strlen(failure.what()));
        return 1;
    }
    return 0;
}

// Lua supplies true as the last argument; one return value preserves it.
extern "C" __declspec(dllexport) int pbz_input_mappings_unchanged(void* state)
{
    struct LuaView { void* state; } view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    auto* input = reinterpret_cast<void*>(lua.get_integer(1));
    auto found = PBZInputObservation::snapshots.find(input);
    return found != PBZInputObservation::snapshots.end() && found->second.unchanged(input) ? 1 : 0;
}

extern "C" __declspec(dllexport) int pbz_input_mappings_forget(void* state)
{
    struct LuaView { void* state; } view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    auto* input = reinterpret_cast<void*>(lua.get_integer(1));
    if (input) PBZInputObservation::snapshots.erase(input);
    else PBZInputObservation::snapshots.clear();
    return 0;
}

extern "C" __declspec(dllexport) int pbz_ability_bindings_observe(void* state)
{
    struct LuaView { void* state; } view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    auto* asset = reinterpret_cast<void*>(lua.get_integer(1));
    auto token = lua.get_integer(1);
    try
    {
        lua.set_integer(PBZInputObservation::observeAbilities(asset,token));
        return 1;
    }
    catch (const std::exception& failure)
    {
        PBZInputObservation::abilitySnapshots.erase(token);
        lua.set_integer(0);
        lua.set_string(failure.what(),std::strlen(failure.what()));
        return 2;
    }
}

extern "C" __declspec(dllexport) int pbz_ability_bindings_unchanged(void* state)
{
    struct LuaView { void* state; } view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    auto token = lua.get_integer(1);
    auto* asset = reinterpret_cast<void*>(lua.get_integer(1));
    const auto& snapshots = PBZInputObservation::abilitySnapshots;
    auto found = snapshots.find(token);
    // Lua supplies true after the consumed arguments.
    return found!=snapshots.end() && found->second.unchanged(asset) ? 1 : 0;
}

extern "C" __declspec(dllexport) int pbz_ability_bindings_forget(void* state)
{
    struct LuaView { void* state; } view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    PBZInputObservation::abilitySnapshots.erase(lua.get_integer(1));
    return 0;
}
