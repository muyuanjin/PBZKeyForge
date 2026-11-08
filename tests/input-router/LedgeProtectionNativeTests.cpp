#define PBZ_CONTEXT_API
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
static int protectionCalls=0;
static BOOL counted_protect(void* address,SIZE_T size,DWORD flags,DWORD* previous)
{ ++protectionCalls;return VirtualProtect(address,size,flags,previous); }
#define VirtualProtect counted_protect
#include "../../prototype/input-router/native/ledge-protection.cpp"
#include <cassert>
#include <iostream>
#include <string>
#include <chrono>

namespace
{
    struct Property : RC::Unreal::FProperty { int size = 4, offset = 8; } property, disableProperty;
    struct Movement { void** table; int force = 1; bool walk = true; int disable = 0; };
    long long argument = 0, result = 0;
    std::string failure;
    int expectedForce = 0, expectedDisable = 1, walkingCalls = 0, addedForce = 0;
    void walk(RC::Unreal::UObject* object, float delta, int iterations)
    {
        auto* value = reinterpret_cast<Movement*>(object);
        assert(value->force == expectedForce && delta == .016f && iterations == 2);
        assert(value->disable == expectedDisable);
        value->force += addedForce;
        ++walkingCalls;
    }
}
namespace RC::Unreal
{
    int& FProperty::GetElementSize() { return static_cast<Property*>(this)->size; }
    int& FProperty::GetOffset_Internal() { return static_cast<Property*>(this)->offset; }
    FProperty* UObject::GetPropertyByNameInChain(const wchar_t* name)
    {
        if (std::wcscmp(name,L"bForceEnableLedgeProtection") == 0) return &property;
        if (std::wcscmp(name,L"bForceDisableLedgeProtection") == 0) return &disableProperty;
        return nullptr;
    }
}
namespace RC::LuaMadeSimple
{
    long long Lua::get_integer(int) const { return argument; }
    void Lua::set_integer(long long value) const { result = value; }
    void Lua::set_string(const char* text,size_t size) const { failure.assign(text,size); }
}
int main()
{
    using namespace PBZLedgeProtection;
    disableProperty.offset = offsetof(Movement,disable);
    auto* code = static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE));
    auto** table = static_cast<void**>(VirtualAlloc(nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE));
    assert(code && table);
    // Forced protection returns false; otherwise return the walking flag.
    const unsigned char body[] = {0x83,0xb9,8,0,0,0,0,0x74,3,0x32,0xc0,0xc3,0xe9,0,0,0,0,0x0f,0xb6,0x41,12,0xc3};
    std::memcpy(code,body,sizeof(body));
    table[walkOffSlot] = code;
    table[walkingSlot] = reinterpret_cast<void*>(walk);
    DWORD protection;
    assert(VirtualProtect(code,4096,PAGE_EXECUTE_READ,&protection));
    assert(VirtualProtect(table,4096,PAGE_READONLY,&protection));
    Movement player{table},other{table};
    auto object = [](Movement& value) { return reinterpret_cast<RC::Unreal::UObject*>(&value); };
    auto allowed = [&](Movement& value) { return reinterpret_cast<Gate>(value.table[walkOffSlot])(object(value)); };
    assert(!allowed(player) && !allowed(other));
    set(object(player));
    assert(allowed(player) && !allowed(other));
    reinterpret_cast<Walking>(table[walkingSlot])(object(player),.016f,2);
    assert(player.force == 1 && player.disable == 0 && walkingCalls == 1);
    expectedForce = 1; expectedDisable = 0;
    reinterpret_cast<Walking>(table[walkingSlot])(object(other),.016f,2);
    assert(other.force == 1 && walkingCalls == 2);
    expectedForce = 0; expectedDisable = 1; addedForce = 2;
    reinterpret_cast<Walking>(table[walkingSlot])(object(player),.016f,2);
    assert(player.force == 3);
    player.force = 1; addedForce = 0;
    player.disable = 3; expectedDisable = 3;
    reinterpret_cast<Walking>(table[walkingSlot])(object(player),.016f,2);
    assert(player.disable == 3 && player.force == 1);
    player.disable = 0; expectedDisable = 1;
    const int beforeFrames=protectionCalls;
    for (int frame = 0; frame < 120; ++frame)
    {
        set(object(player));
        reinterpret_cast<Walking>(table[walkingSlot])(object(player),.016f,2);
        assert(player.disable == 0 && player.force == 1);
    }
    assert(protectionCalls==beforeFrames);
    assert(player.force == 1 && player.walk && other.force == 1);
    MEMORY_BASIC_INFORMATION memory{};
    assert(VirtualQuery(table,&memory,sizeof(memory)) && memory.Protect == PAGE_READONLY);
    set(object(player));
    assert(allowed(player));
    set(object(other));
    assert(!allowed(player) && allowed(other));
    reset();
    assert(table[walkOffSlot] == code && !allowed(player) && !allowed(other));
    assert(table[walkingSlot] == reinterpret_cast<void*>(walk));
    reset();
    player.force = 0; player.walk = false;
    assert(!allowed(player));
    argument = reinterpret_cast<long long>(&player);
    assert(pbz_ledge_walk_off(nullptr) == 1 && result == 1 && allowed(player));
    argument = 0;
    assert(pbz_ledge_walk_off(nullptr) == 1 && result == 1 && !allowed(player));
    player.walk = true;
    assert(allowed(player));
    property.size = 1;
    argument = reinterpret_cast<long long>(&player);
    assert(pbz_ledge_walk_off(nullptr) == 2 && result == 0 && !failure.empty() && table[walkOffSlot] == code);
    property.size = 4; property.offset = 16;
    assert(pbz_ledge_walk_off(nullptr) == 2 && result == 0 && table[walkOffSlot] == code);
    assert(!slot && !movement && !original);
    property.offset=offsetof(Movement,force);
    player.force=0;expectedForce=0;expectedDisable=0;
    constexpr int repeats=1000000;
    auto benchmark=[&](const char* label)
    {
        const auto start=std::chrono::steady_clock::now();
        const int before=protectionCalls;
        for (int repeat=0;repeat<repeats;++repeat)
            reinterpret_cast<Walking>(table[walkingSlot])(object(player),.016f,2);
        assert(protectionCalls==before);
        const auto elapsed=std::chrono::duration<double,std::nano>(std::chrono::steady_clock::now()-start).count();
        std::cout<<"LedgePhysicsBenchmark "<<label<<" ns/call="<<elapsed/repeats<<"\n";
    };
    benchmark("original");
    set(object(player));expectedDisable=1;
    benchmark("hooked");
    reset();
    const auto transitionStart=std::chrono::steady_clock::now();
    const int beforeTransitions=protectionCalls;
    for (int repeat=0;repeat<1000;++repeat) { set(object(player));reset(); }
    assert(protectionCalls-beforeTransitions==8000);
    std::cout<<"LedgeTransitionBenchmark us/cycle="
        <<std::chrono::duration<double,std::micro>(std::chrono::steady_clock::now()-transitionStart).count()/1000<<"\n";
    VirtualFree(table,0,MEM_RELEASE); VirtualFree(code,0,MEM_RELEASE);
    std::cout << "LedgeProtectionNativeTests PASS: local override, foreign actors, preserved flags, component change, restore, Lua export, unsupported layouts\n";
}
