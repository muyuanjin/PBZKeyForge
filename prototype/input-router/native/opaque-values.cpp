#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <vector>
#include <initializer_list>
#include <stdexcept>
#include <cstring>
#include <string>
#include <memory>
#include <unordered_map>

#ifndef PBZ_CONTEXT_API
#define PBZ_CONTEXT_API __declspec(dllimport)
#endif
namespace RC::Unreal
{
    class UFunction;
    class UObject
    {
    public:
        PBZ_CONTEXT_API class FProperty* GetPropertyByNameInChain(const wchar_t*);
        PBZ_CONTEXT_API void ProcessEvent(UFunction*,void*);
    };
    class UFunction
    {
    public:
        PBZ_CONTEXT_API unsigned short& GetParmsSize();
    };
    class FProperty
    {
    public:
        PBZ_CONTEXT_API int& GetElementSize();
        PBZ_CONTEXT_API int& GetOffset_Internal();
        PBZ_CONTEXT_API void InitializeValue(void*) const;
        PBZ_CONTEXT_API void DestroyValue(void*) const;
        PBZ_CONTEXT_API void CopyCompleteValue(void*,const void*) const;
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
        PBZ_CONTEXT_API double get_number(int) const;
        PBZ_CONTEXT_API void set_integer(long long) const;
        PBZ_CONTEXT_API void set_string(const char*,size_t) const;
    };
}
namespace PBZOpaqueValues
{
    using namespace RC::Unreal;
    FProperty* field(void* owner,const wchar_t* name)
    {
        auto* result=reinterpret_cast<UObject*>(owner)->GetPropertyByNameInChain(name);
        if (!result) throw std::runtime_error("Native value property is absent");
        return result;
    }
    struct Parameters
    {
        UFunction* function;
        std::vector<unsigned char> bytes;
        std::vector<FProperty*> initialized;
        Parameters(UFunction* fn,std::initializer_list<const wchar_t*> names) : function(fn),bytes(fn->GetParmsSize(),0)
        {
            try
            {
                for (auto* name:names)
                {
                    auto* property=field(fn,name);
                    property->InitializeValue(at(property));
                    initialized.push_back(property);
                }
            }
            catch (...) { destroy(); throw; }
        }
        ~Parameters() { destroy(); }
        void destroy()
        {
            for (auto it=initialized.rbegin();it!=initialized.rend();++it) (*it)->DestroyValue(at(*it));
            initialized.clear();
        }
        void* at(FProperty* property)
        {
            int offset=property->GetOffset_Internal(),size=property->GetElementSize();
            if (offset<0 || size<0 || size_t(offset)+size>bytes.size())
                throw std::runtime_error("Native value parameter layout is unsupported");
            return bytes.data()+offset;
        }
        void* at(const wchar_t* name) { return at(field(function,name)); }
        template<class T> void write(const wchar_t* name,const T& value)
        {
            auto* property=field(function,name);
            if (property->GetElementSize()!=sizeof(T)) throw std::runtime_error("Native value parameter type differs");
            std::memcpy(at(property),&value,sizeof(T));
        }
        void copy(const wchar_t* name,Parameters& source,const wchar_t* sourceName)
        {
            auto* target=field(function,name);
            auto* original=field(source.function,sourceName);
            if (reinterpret_cast<FStructProperty*>(target)->GetStruct().Ptr!=reinterpret_cast<FStructProperty*>(original)->GetStruct().Ptr)
                throw std::runtime_error("Native value struct types differ");
            target->CopyCompleteValue(at(target),source.at(original));
        }
        void invoke(UObject* object) { object->ProcessEvent(function,bytes.data()); }
    };
    FProperty* handle_id(FProperty* property)
    {
        auto* type=reinterpret_cast<FStructProperty*>(property)->GetStruct().Ptr;
        auto* id=field(type,L"Handle");
        if (id->GetElementSize()!=sizeof(int) || id->GetOffset_Internal()<0
            || id->GetOffset_Internal()+sizeof(int)>size_t(property->GetElementSize()))
            throw std::runtime_error("Gameplay effect handle layout is unsupported");
        return id;
    }
    int apply(UObject* component,UFunction* make,UFunction* apply,UObject* effect,float level)
    {
        Parameters context(make,{L"ReturnValue"});
        context.invoke(component);
        Parameters request(apply,{L"GameplayEffectClass",L"Level",L"EffectContext",L"ReturnValue"});
        request.write(L"GameplayEffectClass",effect);
        request.write(L"Level",level);
        request.copy(L"EffectContext",context,L"ReturnValue");
        request.invoke(component);
        auto* result=field(apply,L"ReturnValue");
        int id;
        std::memcpy(&id,static_cast<unsigned char*>(request.at(result))+handle_id(result)->GetOffset_Internal(),sizeof(id));
        return id;
    }
    struct EffectRequirements
    {
        struct Value
        {
            FProperty* property;
            void* destination;
            std::vector<unsigned char> original,empty;
            Value(UObject* object,const wchar_t* name) : property(field(object,name)),
                destination(reinterpret_cast<unsigned char*>(object)+property->GetOffset_Internal()),
                original(property->GetElementSize()),empty(property->GetElementSize())
            {
                property->InitializeValue(original.data());
                try
                {
                    property->CopyCompleteValue(original.data(),destination);
                    property->InitializeValue(empty.data());
                }
                catch (...) { property->DestroyValue(original.data());throw; }
                property->CopyCompleteValue(destination,empty.data());
            }
            ~Value()
            {
                property->CopyCompleteValue(destination,original.data());
                property->DestroyValue(empty.data());
                property->DestroyValue(original.data());
            }
        };
        std::vector<std::unique_ptr<Value>> values;
        void add(UObject* object)
        {
            values.emplace_back(new Value(object,L"ApplicationTagRequirements"));
        }
    };
    bool remove(UObject* component,UFunction* remove,int id)
    {
        Parameters request(remove,{L"Handle",L"StacksToRemove",L"ReturnValue"});
        auto* property=field(remove,L"Handle");
        std::memcpy(static_cast<unsigned char*>(request.at(property))+handle_id(property)->GetOffset_Internal(),&id,sizeof(id));
        request.write(L"StacksToRemove",-1);
        request.invoke(component);
        bool result;
        std::memcpy(&result,request.at(L"ReturnValue"),sizeof(result));
        return result;
    }
    void* structure(FProperty* property)
    { return reinterpret_cast<FStructProperty*>(property)->GetStruct().Ptr; }
    void* member(void* data,FProperty* property)
    { return static_cast<unsigned char*>(data)+property->GetOffset_Internal(); }
    template<class T> T read(void* data,void* type,const wchar_t* name)
    {
        auto* property=field(type,name);
        if (property->GetElementSize()!=sizeof(T)) throw std::runtime_error("Long buff field type differs");
        T result;std::memcpy(&result,member(data,property),sizeof(result));return result;
    }
    struct LongBuffSpec
    {
        FProperty* property;
        std::vector<unsigned char> data;
        UObject* effectClass;
        int handle;
        double expires;
        bool removed=false,restored=false;
        LongBuffSpec(FProperty* prop,void* source,UObject* cls,int id,double expiry)
            :property(prop),data(prop->GetElementSize()),effectClass(cls),handle(id),expires(expiry)
        {
            property->InitializeValue(data.data());
            try { property->CopyCompleteValue(data.data(),source); }
            catch (...) { property->DestroyValue(data.data());throw; }
        }
        ~LongBuffSpec() { property->DestroyValue(data.data()); }
    };
    struct LongBuffState { UObject* component;std::vector<std::unique_ptr<LongBuffSpec>> specs; };
    std::unordered_map<long long,std::unique_ptr<LongBuffState>> longBuffStates;
    long long nextLongBuffState=0;
    long long captureLongBuffs(UObject* component,double now,UObject* firstClass,UObject* firstDefinition,
        UObject* secondClass,UObject* secondDefinition)
    {
        auto* effects=field(component,L"ActiveGameplayEffects");
        auto* list=field(structure(effects),L"GameplayEffects_Internal");
        auto* element=reinterpret_cast<FArrayProperty*>(list)->GetInner();
        auto* type=structure(element);
        auto* spec=field(type,L"Spec");
        if (spec->GetOffset_Internal()!=0x18) throw std::runtime_error("Long buff active-effect layout differs");
        struct Array { unsigned char* data;int count,capacity; };
        auto& array=*static_cast<Array*>(member(member(component,effects),list));
        auto saved=std::make_unique<LongBuffState>();saved->component=component;
        for (int index=0;index<array.count;++index)
        {
            auto* active=array.data+index*element->GetElementSize();
            auto* value=member(active,spec);auto* specType=structure(spec);
            auto* definition=read<UObject*>(value,specType,L"Def");
            UObject* cls=definition==firstDefinition?firstClass:definition==secondDefinition?secondClass:nullptr;
            if (!cls) continue;
            auto duration=read<float>(value,specType,L"Duration");
            auto start=read<float>(active,type,L"StartWorldTime");
            int id;std::memcpy(&id,active+0xc,sizeof(id));
            saved->specs.emplace_back(new LongBuffSpec(spec,value,cls,id,duration<0?-1:double(start)+duration));
        }
        auto token=++nextLongBuffState;longBuffStates.emplace(token,std::move(saved));return token;
    }
    LongBuffState& longBuffState(UObject* component,long long token)
    {
        auto found=longBuffStates.find(token);
        if (found==longBuffStates.end() || found->second->component!=component)
            throw std::runtime_error("Long buff restoration state is unavailable");
        return *found->second;
    }
    void removeLongBuffs(UObject* component,UFunction* function,long long token)
    {
        for (auto& spec:longBuffState(component,token).specs)
            if (!spec->removed)
            {
                if (!remove(component,function,spec->handle)) throw std::runtime_error("Long buff native suspension failed");
                spec->removed=true;
            }
    }
    struct LongBuffDuration
    {
        float* value;
        float original;
        LongBuffDuration(void* definition,void* magnitudeType,float remaining)
        {
            auto* scalable=field(magnitudeType,L"ScalableFloatMagnitude");
            auto* scalar=field(structure(scalable),L"Value");
            if (scalar->GetElementSize()!=sizeof(float)) throw std::runtime_error("Long buff duration type differs");
            value=static_cast<float*>(member(member(definition,scalable),scalar));
            original=*value;*value=remaining;
        }
        ~LongBuffDuration() { *value=original; }
    };
    void restoreLongBuffs(UObject* component,UFunction* make,UFunction* applyFn,long long token,double now)
    {
        for (auto& saved:longBuffState(component,token).specs)
        {
            if (!saved->removed || saved->restored) continue;
            if (saved->expires>=0 && saved->expires<=now) { saved->restored=true;continue; }
            auto* specType=structure(saved->property);
            Parameters outgoing(make,{L"GameplayEffectClass",L"Level",L"Context",L"ReturnValue"});
            outgoing.write(L"GameplayEffectClass",saved->effectClass);
            outgoing.write(L"Level",read<float>(saved->data.data(),specType,L"Level"));
            auto* effectContext=field(specType,L"EffectContext");
            field(make,L"Context")->CopyCompleteValue(outgoing.at(L"Context"),member(saved->data.data(),effectContext));
            outgoing.invoke(component);
            if (field(make,L"ReturnValue")->GetElementSize()!=sizeof(void*)*2)
                throw std::runtime_error("Long buff spec handle layout differs");
            void* newSpec;std::memcpy(&newSpec,outgoing.at(L"ReturnValue"),sizeof(newSpec));
            if (!newSpec) throw std::runtime_error("Long buff native spec creation failed");
            saved->property->CopyCompleteValue(newSpec,saved->data.data());
            auto* duration=field(specType,L"Duration");
            float remaining=saved->expires<0?-1.f:static_cast<float>(saved->expires-now);
            std::memcpy(member(newSpec,duration),&remaining,sizeof(remaining));
            auto* definition=read<UObject*>(saved->data.data(),specType,L"Def");
            auto* magnitude=field(definition,L"DurationMagnitude");
            LongBuffDuration originalDuration(member(definition,magnitude),structure(magnitude),remaining);
            Parameters request(applyFn,{L"SpecHandle",L"ReturnValue"});
            request.copy(L"SpecHandle",outgoing,L"ReturnValue");request.invoke(component);
            auto* result=field(applyFn,L"ReturnValue");
            int id;std::memcpy(&id,static_cast<unsigned char*>(request.at(result))+handle_id(result)->GetOffset_Internal(),sizeof(id));
            if (id<0) throw std::runtime_error("Long buff native restoration failed");
            saved->restored=true;
        }
    }
    void hold(UObject* subsystem,UObject* library,UFunction* make,UFunction* inject,UObject* action)
    {
        Parameters value(make,{L"X",L"Y",L"Z",L"ValueType",L"ReturnValue"});
        value.write(L"X",1.0);
        value.invoke(library);
        Parameters request(inject,{L"Action",L"RawValue",L"Modifiers",L"Triggers"});
        request.write(L"Action",action);
        request.copy(L"RawValue",value,L"ReturnValue");
        request.invoke(subsystem);
    }
    struct LuaView { void* state; };
    template<class Operation> int call(void* state,Operation operation)
    {
        LuaView view{state};
        auto& lua=*reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
        try { auto result=operation(lua);lua.set_integer(1);lua.set_integer(result);return 2; }
        catch (const std::exception& failure)
        { lua.set_integer(0);lua.set_string(failure.what(),std::strlen(failure.what()));return 2; }
    }
    template<class T> T* pointer(const RC::LuaMadeSimple::Lua& lua) { return reinterpret_cast<T*>(lua.get_integer(1)); }
}
extern "C" __declspec(dllexport) int pbz_effect_apply_self(void* state)
{
    using namespace PBZOpaqueValues;
    return call(state,[](const auto& lua) {
        auto* component=pointer<UObject>(lua);auto* make=pointer<UFunction>(lua);auto* applyFn=pointer<UFunction>(lua);
        auto* effect=pointer<UObject>(lua);float level=static_cast<float>(lua.get_number(1));
        return apply(component,make,applyFn,effect,level);
    });
}
extern "C" __declspec(dllexport) int pbz_effect_apply_with_requirements(void* state)
{
    using namespace PBZOpaqueValues;
    return call(state,[](const auto& lua) {
        auto* component=pointer<UObject>(lua);auto* make=pointer<UFunction>(lua);auto* applyFn=pointer<UFunction>(lua);
        auto* effect=pointer<UObject>(lua);float level=static_cast<float>(lua.get_number(1));
        auto count=lua.get_integer(1);
        EffectRequirements requirements;
        for (long long index=0;index<count;++index)
        {
            requirements.add(pointer<UObject>(lua));
        }
        return apply(component,make,applyFn,effect,level);
    });
}
extern "C" __declspec(dllexport) int pbz_effect_remove(void* state)
{
    using namespace PBZOpaqueValues;
    return call(state,[](const auto& lua) {
        auto* component=pointer<UObject>(lua);auto* function=pointer<UFunction>(lua);int id=static_cast<int>(lua.get_integer(1));
        return remove(component,function,id);
    });
}
extern "C" __declspec(dllexport) int pbz_long_buff_capture(void* state)
{
    using namespace PBZOpaqueValues;
    return call(state,[](const auto& lua) {
        auto* component=pointer<UObject>(lua);double now=lua.get_number(1);
        auto* firstClass=pointer<UObject>(lua);auto* firstDefinition=pointer<UObject>(lua);
        auto* secondClass=pointer<UObject>(lua);auto* secondDefinition=pointer<UObject>(lua);
        return captureLongBuffs(component,now,firstClass,firstDefinition,secondClass,secondDefinition);
    });
}
extern "C" __declspec(dllexport) int pbz_long_buff_remove(void* state)
{
    using namespace PBZOpaqueValues;
    return call(state,[](const auto& lua) {
        auto* component=pointer<UObject>(lua);auto* function=pointer<UFunction>(lua);auto token=lua.get_integer(1);
        removeLongBuffs(component,function,token);return 1;
    });
}
extern "C" __declspec(dllexport) int pbz_long_buff_restore(void* state)
{
    using namespace PBZOpaqueValues;
    return call(state,[](const auto& lua) {
        auto* component=pointer<UObject>(lua);auto* make=pointer<UFunction>(lua);auto* applyFn=pointer<UFunction>(lua);
        auto token=lua.get_integer(1);double now=lua.get_number(1);
        restoreLongBuffs(component,make,applyFn,token,now);return 1;
    });
}
extern "C" __declspec(dllexport) int pbz_long_buff_discard(void* state)
{
    using namespace PBZOpaqueValues;
    return call(state,[](const auto& lua) { longBuffStates.erase(lua.get_integer(1));return 1; });
}
extern "C" __declspec(dllexport) int pbz_input_start_hold(void* state)
{
    using namespace PBZOpaqueValues;
    return call(state,[](const auto& lua) {
        auto* subsystem=pointer<UObject>(lua);auto* library=pointer<UObject>(lua);
        auto* make=pointer<UFunction>(lua);auto* inject=pointer<UFunction>(lua);auto* action=pointer<UObject>(lua);
        hold(subsystem,library,make,inject,action);return 1;
    });
}
