#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <string>
#include <string_view>
#include <vector>
#include <stdexcept>
#include <algorithm>
#include <cstring>

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
        PBZ_CONTEXT_API const std::wstring ToString() const;
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
    class UClass;
    class UObjectBase
    {
    public:
        PBZ_CONTEXT_API UClass*& GetClassPrivate();
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
}
namespace RC::LuaMadeSimple
{
    class Lua
    {
    public:
        PBZ_CONTEXT_API long long get_integer(int) const;
        PBZ_CONTEXT_API bool get_bool(int) const;
        PBZ_CONTEXT_API std::string_view get_string(int) const;
        PBZ_CONTEXT_API void set_integer(long long) const;
        PBZ_CONTEXT_API void set_string(const char*, size_t) const;
    };
}

namespace PBZAbilitySelection
{
    using namespace RC::Unreal;
    struct Array { unsigned char* data; int num, max; };
    unsigned char* field(void* object, FProperty* property)
    {
        if (!property) throw std::runtime_error("Ability selection property unavailable");
        return static_cast<unsigned char*>(object)+property->GetOffset_Internal();
    }
    FProperty* member(FProperty* property, const wchar_t* name)
    {
        if (!property) throw std::runtime_error("Ability selection layout unavailable");
        auto* type = reinterpret_cast<FStructProperty*>(property)->GetStruct().Ptr;
        auto* result = reinterpret_cast<UStruct*>(type)->FindProperty(FName(name,FNAME_Find,nullptr));
        if (!result) throw std::runtime_error("Ability selection member unavailable");
        return result;
    }
    std::wstring wide(std::string_view text)
    {
        int size = MultiByteToWideChar(CP_UTF8,0,text.data(),static_cast<int>(text.size()),nullptr,0);
        std::wstring result(size,L'\0');
        MultiByteToWideChar(CP_UTF8,0,text.data(),static_cast<int>(text.size()),result.data(),size);
        return result;
    }
    bool matches(const std::wstring& tag, const std::wstring& wanted)
    {
        return tag == wanted || tag.size()>wanted.size() && tag.compare(0,wanted.size(),wanted)==0 && tag[wanted.size()]==L'.';
    }
    std::string select(void* component, UClass* target, const std::vector<std::wstring>& wanted, bool all, bool activeOnly = false)
    {
        if (!component) throw std::runtime_error("Ability system unavailable");
        if (!target && wanted.empty() && !all) return {};
        if (FName::StaticSize()!=sizeof(FName)) throw std::runtime_error("Ability selection FName layout unsupported");
        auto* grants = static_cast<UObject*>(component)->GetPropertyByNameInChain(L"ActivatableAbilities");
        auto* items = member(grants,L"Items");
        auto* spec = reinterpret_cast<FArrayProperty*>(items)->GetInner();
        auto* abilityProperty = member(spec,L"Ability");
        auto* activeProperty = activeOnly ? member(spec,L"ActiveCount") : nullptr;
        if (activeProperty && activeProperty->GetElementSize()!=sizeof(uint8_t))
            throw std::runtime_error("Ability active count layout unsupported");
        auto& array = *reinterpret_cast<Array*>(field(field(component,grants),items));
        std::string result;
        FProperty *tagsProperty=nullptr, *tagsArray=nullptr, *tagName=nullptr;
        int tagStride=0;
        std::vector<bool> found(wanted.size());
        for (int index=0; index<array.num; ++index)
        {
            auto* entry = array.data+index*spec->GetElementSize();
            if (activeOnly && *field(entry,activeProperty)==0) continue;
            auto* ability = *reinterpret_cast<UObject**>(field(entry,abilityProperty));
            if (!ability) continue;
            bool selected = target ? reinterpret_cast<UObjectBase*>(ability)->GetClassPrivate()==target : wanted.empty();
            if (!target && !wanted.empty())
            {
                // Every spec's Ability derives from GameplayAbility; the inherited
                // AbilityTags layout is shared. Resolve it only for this scan.
                if (!tagsProperty)
                {
                    tagsProperty = ability->GetPropertyByNameInChain(L"AbilityTags");
                    tagsArray = member(tagsProperty,L"GameplayTags");
                    auto* tag = reinterpret_cast<FArrayProperty*>(tagsArray)->GetInner();
                    tagName = member(tag,L"TagName");
                    tagStride = tag->GetElementSize();
                }
                auto& tags = *reinterpret_cast<Array*>(field(field(ability,tagsProperty),tagsArray));
                std::fill(found.begin(),found.end(),false);
                size_t count=0;
                for (int slot=0; slot<tags.num && !selected; ++slot)
                {
                    auto name = reinterpret_cast<FName*>(field(tags.data+slot*tagStride,tagName))->ToString();
                    for (size_t required=0; required<wanted.size(); ++required)
                    {
                        if (found[required] || !matches(name,wanted[required])) continue;
                        found[required]=true;
                        ++count;
                        if (!all || count==wanted.size()) { selected=true; break; }
                    }
                }
            }
            if (selected) { result+=std::to_string(index+1); result+=' '; }
        }
        return result;
    }
}

static int ability_spec_indices(void* state, bool activeOnly)
{
    struct LuaView { void* state; } view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    try
    {
        auto* component = reinterpret_cast<void*>(lua.get_integer(1));
        auto* target = reinterpret_cast<RC::Unreal::UClass*>(lua.get_integer(1));
        bool all = lua.get_bool(1);
        auto count = lua.get_integer(1);
        std::vector<std::wstring> tags;
        for (long long index=0; index<count; ++index) tags.push_back(PBZAbilitySelection::wide(lua.get_string(1)));
        auto result = PBZAbilitySelection::select(component,target,tags,all,activeOnly);
        lua.set_string(result.data(),result.size());
        return 1;
    }
    catch (const std::exception& failure)
    {
        lua.set_integer(0);
        lua.set_string(failure.what(),std::strlen(failure.what()));
        return 2;
    }
}

extern "C" __declspec(dllexport) int pbz_ability_spec_indices(void* state)
{ return ability_spec_indices(state,false); }

extern "C" __declspec(dllexport) int pbz_active_ability_spec_indices(void* state)
{ return ability_spec_indices(state,true); }
