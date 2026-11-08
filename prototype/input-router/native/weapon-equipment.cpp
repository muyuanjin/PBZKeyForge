#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>
#include <string_view>
#include <vector>
#include <cstring>
#include <stdexcept>

#ifndef PBZ_CONTEXT_API
#define PBZ_CONTEXT_API __declspec(dllimport)
#endif
namespace RC::Unreal
{
    enum EFindName { FNAME_Find, FNAME_Add };
    class FName
    {
        unsigned int data[2];
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
    class UClass;
    class UFunction;
    class UObject
    {
    public:
        PBZ_CONTEXT_API FProperty* GetPropertyByNameInChain(const wchar_t*);
        PBZ_CONTEXT_API void ProcessEvent(UFunction*,void*);
    };
    class UObjectBase
    {
    public:
        PBZ_CONTEXT_API UClass*& GetClassPrivate();
    };
    class UStruct
    {
    public:
        PBZ_CONTEXT_API UStruct*& GetSuperStruct();
    };
    class UFunction
    {
    public:
        PBZ_CONTEXT_API unsigned short& GetParmsSize();
    };
    template<int> class TSizedDefaultAllocator {};
    template<class T,class A=TSizedDefaultAllocator<32>> class TArray
    { public: T* Data; int Num; int Max; };
    struct FImplementedInterface { UClass* Class; int PointerOffset; bool bImplementedByK2; };
    class UClass
    {
    public:
        PBZ_CONTEXT_API TArray<FImplementedInterface>& GetInterfaces();
    };
    template<class T> class TObjectPtr { public: T* Ptr; };
    class FInterfaceProperty
    {
    public:
        PBZ_CONTEXT_API TObjectPtr<UClass>& GetInterfaceClass();
    };
    class UScriptStruct;
    class FStructProperty
    {
    public:
        PBZ_CONTEXT_API TObjectPtr<UScriptStruct>& GetStruct();
    };
}
namespace RC::LuaMadeSimple
{
    class Lua
    {
    public:
        PBZ_CONTEXT_API long long get_integer(int) const;
        PBZ_CONTEXT_API std::string_view get_string(int) const;
        PBZ_CONTEXT_API void set_integer(long long) const;
        PBZ_CONTEXT_API void set_string(const char*,size_t) const;
    };
}
namespace PBZWeaponEquipment
{
    using namespace RC::Unreal;
    struct Interface { UObject* object; void* address; };
    FProperty* field(void* owner,const wchar_t* name)
    {
        auto* property=reinterpret_cast<UObject*>(owner)->GetPropertyByNameInChain(name);
        if (!property) throw std::runtime_error("Weapon equipment parameter is absent");
        return property;
    }
    void write(std::vector<unsigned char>& data,FProperty* property,const void* value,size_t size)
    {
        int offset=property->GetOffset_Internal();
        if (property->GetElementSize()!=size || offset<0 || size_t(offset)+size>data.size())
            throw std::runtime_error("Weapon equipment parameter layout is unsupported");
        std::memcpy(data.data()+offset,value,size);
    }
    void set(UObject* component,UFunction* function,const std::wstring& slot,int index,UObject* config)
    {
        if (!component || !function || !config) throw std::runtime_error("Weapon equipment object is absent");
        auto* configProperty=field(function,L"Config");
        auto* interfaceClass=reinterpret_cast<FInterfaceProperty*>(configProperty)->GetInterfaceClass().Ptr;
        Interface value{config,nullptr};
        auto* type=reinterpret_cast<UObjectBase*>(config)->GetClassPrivate();
        for (;type;type=reinterpret_cast<UClass*>(reinterpret_cast<UStruct*>(type)->GetSuperStruct()))
        {
            auto& interfaces=type->GetInterfaces();
            for (int n=0;n<interfaces.Num;++n)
            {
                const auto& implementation=interfaces.Data[n];
                if (implementation.Class==interfaceClass && !implementation.bImplementedByK2)
                    value.address=reinterpret_cast<unsigned char*>(config)+implementation.PointerOffset;
            }
            if (value.address) break;
        }
        if (!value.address) throw std::runtime_error("Weapon config has no native CharacterWeaponConfig interface");
        std::vector<unsigned char> parameters(function->GetParmsSize(),0);
        auto* slotProperty=field(function,L"SlotTag");
        auto* tagType=reinterpret_cast<FStructProperty*>(slotProperty)->GetStruct().Ptr;
        auto* tagName=field(tagType,L"TagName");
        FName tag(slot.c_str(),FNAME_Find,nullptr);
        if (FName::StaticSize()!=sizeof(tag) || tagName->GetElementSize()!=sizeof(tag)
            || tagName->GetOffset_Internal()!=0 || slotProperty->GetElementSize()!=sizeof(tag))
            throw std::runtime_error("Weapon slot must be a reflected gameplay tag");
        write(parameters,slotProperty,&tag,sizeof(tag));
        write(parameters,field(function,L"Index"),&index,sizeof(index));
        write(parameters,configProperty,&value,sizeof(value));
        component->ProcessEvent(function,parameters.data());
    }
}
extern "C" __declspec(dllexport) int pbz_weapon_set_slot(void* state)
{
    struct View { void* state; } view{state};
    auto& lua=*reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    try
    {
        auto* component=reinterpret_cast<RC::Unreal::UObject*>(lua.get_integer(1));
        auto* function=reinterpret_cast<RC::Unreal::UFunction*>(lua.get_integer(1));
        auto slot=lua.get_string(1);
        std::wstring name(slot.begin(),slot.end());
        int index=static_cast<int>(lua.get_integer(1));
        auto* config=reinterpret_cast<RC::Unreal::UObject*>(lua.get_integer(1));
        PBZWeaponEquipment::set(component,function,name,index,config);
        lua.set_integer(1);
        return 1;
    }
    catch (const std::exception& failure)
    {
        lua.set_integer(0);
        lua.set_string(failure.what(),std::strlen(failure.what()));
        return 2;
    }
}
