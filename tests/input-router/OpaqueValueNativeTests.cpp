#define PBZ_CONTEXT_API
#include "../../prototype/input-router/native/opaque-values.cpp"
#include <cassert>
#include <map>
#include <iostream>

namespace
{
    using namespace RC::Unreal;
    struct Type { std::map<std::wstring,FProperty*> fields; };
    struct Property : FProperty
    {
        int offset,size;
        TObjectPtr<UScriptStruct> type{};
        int initialized=0,destroyed=0,copied=0;
        bool requirements=false;
        bool spec=false,specHandle=false;
        FProperty* inner=nullptr;
        Property(int offset,int size) : offset(offset),size(size) {}
    };
    struct Function { unsigned short size;Type type;int kind; };
    struct HiddenContext { unsigned long long first,second; };
    struct InputValue { double x,y,z;unsigned char type; };
    struct Handle { int id;bool applied; };
    struct TestSpec { UObject* definition=nullptr;float level=1,duration=0;HiddenContext context{};std::vector<int> captured; };
    using TestSpecHandle=std::shared_ptr<TestSpec>;
    struct ActiveEffect { char header[12];int handle;int owner;int padding;TestSpec spec;float start; };
    struct ActiveArray { ActiveEffect* data;int count,capacity; };
    struct EffectContainer { ActiveArray effects; };
    struct TestASC { EffectContainer effects; };
    struct Scalable { float value=40; };
    struct Magnitude { Scalable scalable; };
    struct Definition { Magnitude magnitude; };
    float expectedRemaining=0;
    bool failRestore=false;
    int restored=0;
    struct Requirements { std::vector<int> query;int required=0; };
    struct RequirementComponent { Requirements application,removal; };
    RequirementComponent *preparedEffect=nullptr,*existingEffect=nullptr;
    std::map<void*,Type*> types;
    std::vector<Property*> properties;
    int checks=0,calls=0;
    bool failApply=false,invalidApply=false,removeSucceeded=true;
    UObject *expectedEffect,*expectedAction;
    void check(bool condition) { assert(condition);++checks; }
    Property* add(Type& type,const wchar_t* name,int offset,int size,Type* structure=nullptr)
    {
        auto* value=new Property(offset,size);value->type.Ptr=reinterpret_cast<UScriptStruct*>(structure);
        type.fields[name]=value;properties.push_back(value);return value;
    }
    Function function(int kind,unsigned short size)
    { Function result{size,{},kind};return result; }
    template<class T> T read(void* bytes,Type& type,const wchar_t* name)
    { T value;auto* p=static_cast<Property*>(type.fields.at(name));std::memcpy(&value,static_cast<unsigned char*>(bytes)+p->offset,sizeof(value));return value; }
    template<class T> void write(void* bytes,Type& type,const wchar_t* name,const T& value)
    { auto* p=static_cast<Property*>(type.fields.at(name));std::memcpy(static_cast<unsigned char*>(bytes)+p->offset,&value,sizeof(value)); }
}
namespace RC::Unreal
{
    FProperty* UObject::GetPropertyByNameInChain(const wchar_t* name)
    { auto& fields=types.at(this)->fields;auto found=fields.find(name);return found==fields.end()?nullptr:found->second; }
    unsigned short& UFunction::GetParmsSize() { return reinterpret_cast<Function*>(this)->size; }
    int& FProperty::GetElementSize() { return static_cast<Property*>(this)->size; }
    int& FProperty::GetOffset_Internal() { return static_cast<Property*>(this)->offset; }
    void FProperty::InitializeValue(void* value) const
    {
        auto* p=const_cast<Property*>(static_cast<const Property*>(this));++p->initialized;
        if (p->requirements) new(value) Requirements();else std::memset(value,0,p->size);
        if (p->spec) new(value) TestSpec();
        if (p->specHandle) new(value) TestSpecHandle();
    }
    void FProperty::DestroyValue(void* value) const
    {
        auto* p=const_cast<Property*>(static_cast<const Property*>(this));++p->destroyed;
        if (p->requirements) static_cast<Requirements*>(value)->~Requirements();
        if (p->spec) static_cast<TestSpec*>(value)->~TestSpec();
        if (p->specHandle) static_cast<TestSpecHandle*>(value)->~TestSpecHandle();
    }
    void FProperty::CopyCompleteValue(void* target,const void* source) const
    {
        auto* p=const_cast<Property*>(static_cast<const Property*>(this));++p->copied;
        if (p->requirements) *static_cast<Requirements*>(target)=*static_cast<const Requirements*>(source);
        else if (p->spec) *static_cast<TestSpec*>(target)=*static_cast<const TestSpec*>(source);
        else if (p->specHandle) *static_cast<TestSpecHandle*>(target)=*static_cast<const TestSpecHandle*>(source);
        else std::memcpy(target,source,p->size);
    }
    TObjectPtr<UScriptStruct>& FStructProperty::GetStruct() { return reinterpret_cast<Property*>(this)->type; }
    FProperty*& FArrayProperty::GetInner() { return reinterpret_cast<Property*>(this)->inner; }
    void UObject::ProcessEvent(UFunction* pointer,void* bytes)
    {
        ++calls;auto& fn=*reinterpret_cast<Function*>(pointer);
        if (fn.kind==1) write(bytes,fn.type,L"ReturnValue",HiddenContext{0x12345678abcdefULL,0xfedcba87654321ULL});
        else if (fn.kind==2)
        {
            check(read<UObject*>(bytes,fn.type,L"GameplayEffectClass")==expectedEffect);
            check(read<float>(bytes,fn.type,L"Level")==2.5f);
            auto context=read<HiddenContext>(bytes,fn.type,L"EffectContext");
            check(context.first==0x12345678abcdefULL && context.second==0xfedcba87654321ULL);
            if (preparedEffect)
            {
                check(preparedEffect->application.query.empty() && preparedEffect->application.required==0);
                check(preparedEffect->removal.query==std::vector<int>({4,5}) && preparedEffect->removal.required==6);
                check(existingEffect->removal.query==std::vector<int>({10,11}) && existingEffect->removal.required==12);
                check(existingEffect->application.query==std::vector<int>({7,8}) && existingEffect->application.required==9);
            }
            if (failApply) throw std::runtime_error("apply rejected");
            write(bytes,fn.type,L"ReturnValue",Handle{invalidApply?-1:73,!invalidApply});
        }
        else if (fn.kind==3)
        {
            check(read<Handle>(bytes,fn.type,L"Handle").id==73);
            check(read<int>(bytes,fn.type,L"StacksToRemove")==-1);
            write(bytes,fn.type,L"ReturnValue",removeSucceeded);
        }
        else if (fn.kind==4)
        {
            check(read<double>(bytes,fn.type,L"X")==1 && read<double>(bytes,fn.type,L"Y")==0 && read<double>(bytes,fn.type,L"Z")==0);
            check(read<unsigned char>(bytes,fn.type,L"ValueType")==0);
            write(bytes,fn.type,L"ReturnValue",InputValue{1,0,0,0});
        }
        else if (fn.kind==5)
        {
            check(read<UObject*>(bytes,fn.type,L"Action")==expectedAction);
            auto value=read<InputValue>(bytes,fn.type,L"RawValue");
            check(value.x==1 && value.y==0 && value.z==0 && value.type==0);
            struct Array { void* data;int count,capacity; };
            check(read<Array>(bytes,fn.type,L"Modifiers").count==0 && read<Array>(bytes,fn.type,L"Triggers").count==0);
        }
        else if (fn.kind==6)
        {
            check(read<float>(bytes,fn.type,L"Level")==3.5f);
            auto context=read<HiddenContext>(bytes,fn.type,L"Context");
            check(context.first==0x1234 && context.second==0x9876);
            auto* property=fn.type.fields.at(L"ReturnValue");
            auto* output=reinterpret_cast<TestSpecHandle*>(static_cast<unsigned char*>(bytes)+property->GetOffset_Internal());
            *output=std::make_shared<TestSpec>();
        }
        else if (fn.kind==7)
        {
            auto* property=fn.type.fields.at(L"SpecHandle");
            auto& value=*reinterpret_cast<TestSpecHandle*>(static_cast<unsigned char*>(bytes)+property->GetOffset_Internal());
            value->duration=reinterpret_cast<Definition*>(value->definition)->magnitude.scalable.value;
            check(value && value->level==3.5f && value->duration==expectedRemaining);
            check(value->context.first==0x1234 && value->context.second==0x9876 && value->captured==std::vector<int>({41,42}));
            if (failRestore) throw std::runtime_error("restoration rejected");
            ++restored;
            write(bytes,fn.type,L"ReturnValue",Handle{74,true});
        }
    }
}
namespace RC::LuaMadeSimple
{
    long long Lua::get_integer(int) const { return 0; }
    double Lua::get_number(int) const { return 0; }
    void Lua::set_integer(long long) const {}
    void Lua::set_string(const char*,size_t) const {}
}
int main()
{
    using namespace PBZOpaqueValues;
    Type contextType,handleType,inputType;
    types[&contextType]=&contextType;types[&handleType]=&handleType;types[&inputType]=&inputType;
    add(handleType,L"Handle",0,sizeof(int));
    auto make=function(1,16);add(make.type,L"ReturnValue",0,16,&contextType);
    auto applyFn=function(2,48);
    add(applyFn.type,L"GameplayEffectClass",0,8);add(applyFn.type,L"Level",8,4);
    auto* context=add(applyFn.type,L"EffectContext",16,16,&contextType);add(applyFn.type,L"ReturnValue",32,8,&handleType);
    auto removeFn=function(3,16);add(removeFn.type,L"Handle",0,8,&handleType);add(removeFn.type,L"StacksToRemove",8,4);add(removeFn.type,L"ReturnValue",12,1);
    auto makeInput=function(4,64);add(makeInput.type,L"X",0,8);add(makeInput.type,L"Y",8,8);add(makeInput.type,L"Z",16,8);add(makeInput.type,L"ValueType",24,1);add(makeInput.type,L"ReturnValue",32,32,&inputType);
    auto inject=function(5,72);add(inject.type,L"Action",0,8);auto* raw=add(inject.type,L"RawValue",8,32,&inputType);add(inject.type,L"Modifiers",40,16);add(inject.type,L"Triggers",56,16);
    for (auto* fn:{&make,&applyFn,&removeFn,&makeInput,&inject}) types[fn]=&fn->type;
    int component,effect,action,library;
    expectedEffect=reinterpret_cast<UObject*>(&effect);expectedAction=reinterpret_cast<UObject*>(&action);
    auto* receiver=reinterpret_cast<UObject*>(&component);
    check(apply(receiver,reinterpret_cast<UFunction*>(&make),reinterpret_cast<UFunction*>(&applyFn),expectedEffect,2.5f)==73);
    check(context->copied==1);
    check(remove(receiver,reinterpret_cast<UFunction*>(&removeFn),73));
    hold(receiver,reinterpret_cast<UObject*>(&library),reinterpret_cast<UFunction*>(&makeInput),reinterpret_cast<UFunction*>(&inject),expectedAction);
    check(raw->copied==1 && calls==5);
    failApply=true;
    try { apply(receiver,reinterpret_cast<UFunction*>(&make),reinterpret_cast<UFunction*>(&applyFn),expectedEffect,2.5f);assert(false); }
    catch (const std::runtime_error&) {}
    for (auto* property:properties) check(property->initialized==property->destroyed);
    RequirementComponent prepared,existing;
    prepared.application.query={1,2};prepared.application.required=3;
    prepared.removal.query={4,5};prepared.removal.required=6;
    existing.application.query={7,8};existing.application.required=9;
    existing.removal.query={10,11};existing.removal.required=12;
    Type requirementType;
    auto* appRequirement=add(requirementType,L"ApplicationTagRequirements",offsetof(RequirementComponent,application),sizeof(Requirements));
    auto* removeRequirement=add(requirementType,L"RemovalTagRequirements",offsetof(RequirementComponent,removal),sizeof(Requirements));
    appRequirement->requirements=true;removeRequirement->requirements=true;
    types[&prepared]=&requirementType;types[&existing]=&requirementType;
    preparedEffect=&prepared;existingEffect=&existing;
    for (bool fail:{false,true})
    {
        failApply=fail;
        try
        {
            EffectRequirements scope;
            scope.add(reinterpret_cast<UObject*>(&prepared));
            apply(receiver,reinterpret_cast<UFunction*>(&make),reinterpret_cast<UFunction*>(&applyFn),expectedEffect,2.5f);
            check(!fail);
        }
        catch (const std::runtime_error&) { check(fail); }
        check(prepared.application.query==std::vector<int>({1,2}) && prepared.application.required==3);
        check(prepared.removal.query==std::vector<int>({4,5}) && prepared.removal.required==6);
        check(existing.application.query==std::vector<int>({7,8}) && existing.application.required==9);
        check(existing.removal.query==std::vector<int>({10,11}) && existing.removal.required==12);
        for (auto* property:properties) check(property->initialized==property->destroyed);
    }
    preparedEffect=nullptr;existingEffect=nullptr;failApply=false;
    failApply=false;invalidApply=true;
    check(apply(receiver,reinterpret_cast<UFunction*>(&make),reinterpret_cast<UFunction*>(&applyFn),expectedEffect,2.5f)==-1);
    for (auto* property:properties) check(property->initialized==property->destroyed);
    removeSucceeded=false;
    check(!remove(receiver,reinterpret_cast<UFunction*>(&removeFn),73));
    for (auto* property:properties) check(property->initialized==property->destroyed);
    raw->type.Ptr=reinterpret_cast<UScriptStruct*>(&contextType);
    try { hold(receiver,reinterpret_cast<UObject*>(&library),reinterpret_cast<UFunction*>(&makeInput),reinterpret_cast<UFunction*>(&inject),expectedAction);assert(false); }
    catch (const std::runtime_error&) {}
    for (auto* property:properties) check(property->initialized==property->destroyed);
    Type specType,activeType,containerType,ascType,specHandleType,definitionType,magnitudeType,scalableType;
    for (auto* type:{&specType,&activeType,&containerType,&ascType,&specHandleType,&definitionType,&magnitudeType,&scalableType}) types[type]=type;
    add(definitionType,L"DurationMagnitude",offsetof(Definition,magnitude),sizeof(Magnitude),&magnitudeType);
    add(magnitudeType,L"ScalableFloatMagnitude",offsetof(Magnitude,scalable),sizeof(Scalable),&scalableType);
    add(scalableType,L"Value",offsetof(Scalable,value),sizeof(float));
    add(specType,L"Def",offsetof(TestSpec,definition),sizeof(UObject*));
    add(specType,L"Level",offsetof(TestSpec,level),sizeof(float));
    add(specType,L"Duration",offsetof(TestSpec,duration),sizeof(float));
    add(specType,L"EffectContext",offsetof(TestSpec,context),sizeof(HiddenContext),&contextType);
    auto* specProperty=add(activeType,L"Spec",offsetof(ActiveEffect,spec),sizeof(TestSpec),&specType);specProperty->spec=true;
    add(activeType,L"StartWorldTime",offsetof(ActiveEffect,start),sizeof(float));
    auto* activeElement=add(activeType,L"Element",0,sizeof(ActiveEffect),&activeType);
    auto* internal=add(containerType,L"GameplayEffects_Internal",offsetof(EffectContainer,effects),sizeof(ActiveArray));internal->inner=activeElement;
    add(ascType,L"ActiveGameplayEffects",offsetof(TestASC,effects),sizeof(EffectContainer),&containerType);
    auto makeSpec=function(6,48);
    add(makeSpec.type,L"GameplayEffectClass",0,8);add(makeSpec.type,L"Level",8,4);
    add(makeSpec.type,L"Context",16,16,&contextType);
    auto* outgoing=add(makeSpec.type,L"ReturnValue",32,16,&specHandleType);outgoing->specHandle=true;
    auto applySpec=function(7,24);
    auto* incoming=add(applySpec.type,L"SpecHandle",0,16,&specHandleType);incoming->specHandle=true;
    add(applySpec.type,L"ReturnValue",16,8,&handleType);
    types[&makeSpec]=&makeSpec.type;types[&applySpec]=&applySpec.type;
    TestASC asc;types[&asc]=&ascType;
    Definition firstDefinition,secondDefinition;
    types[&firstDefinition]=&definitionType;types[&secondDefinition]=&definitionType;
    ActiveEffect active{};active.handle=73;active.start=10;
    active.spec.definition=reinterpret_cast<UObject*>(&firstDefinition);
    active.spec.level=3.5;active.spec.duration=40;active.spec.context={0x1234,0x9876};active.spec.captured={41,42};
    asc.effects.effects={&active,1,1};
    auto* longASC=reinterpret_cast<UObject*>(&asc);
    auto capture=[&]() { return captureLongBuffs(longASC,12.5,expectedEffect,reinterpret_cast<UObject*>(&firstDefinition),
        expectedEffect,reinterpret_cast<UObject*>(&secondDefinition)); };
    auto token=capture();
    check(longBuffState(longASC,token).specs.size()==1 && !longBuffState(longASC,token).specs[0]->removed);
    active.spec.captured={99};
    try { removeLongBuffs(longASC,reinterpret_cast<UFunction*>(&removeFn),token);assert(false); }
    catch (const std::runtime_error&) {}
    check(!longBuffState(longASC,token).specs[0]->removed);
    removeSucceeded=true;
    removeLongBuffs(longASC,reinterpret_cast<UFunction*>(&removeFn),token);
    check(longBuffState(longASC,token).specs[0]->removed);
    expectedRemaining=32.5f;failRestore=true;
    try { restoreLongBuffs(longASC,reinterpret_cast<UFunction*>(&makeSpec),reinterpret_cast<UFunction*>(&applySpec),token,17.5);assert(false); }
    catch (const std::runtime_error&) {}
    check(!longBuffState(longASC,token).specs[0]->restored && restored==0);
    check(firstDefinition.magnitude.scalable.value==40);
    failRestore=false;
    restoreLongBuffs(longASC,reinterpret_cast<UFunction*>(&makeSpec),reinterpret_cast<UFunction*>(&applySpec),token,17.5);
    restoreLongBuffs(longASC,reinterpret_cast<UFunction*>(&makeSpec),reinterpret_cast<UFunction*>(&applySpec),token,17.5);
    check(restored==1);
    check(firstDefinition.magnitude.scalable.value==40);
    longBuffStates.erase(token);
    token=capture();removeLongBuffs(longASC,reinterpret_cast<UFunction*>(&removeFn),token);
    restoreLongBuffs(longASC,reinterpret_cast<UFunction*>(&makeSpec),reinterpret_cast<UFunction*>(&applySpec),token,50);
    check(restored==1 && longBuffState(longASC,token).specs[0]->restored);
    longBuffStates.erase(token);
    for (auto* property:properties) check(property->initialized==property->destroyed);
    std::cout<<"OpaqueValueNativeTests PASS "<<checks<<"\n";
}
