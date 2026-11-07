#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <atomic>
#include <functional>
#include <string>
#include <vector>
#include <algorithm>

#ifndef PBZ_CONTEXT_API
#define PBZ_CONTEXT_API __declspec(dllimport)
#endif

namespace RC::Unreal
{
    class UObject
    {
    public:
        PBZ_CONTEXT_API std::wstring GetFullName(UObject* stopOuter = nullptr) const;
        PBZ_CONTEXT_API class FProperty* GetPropertyByName(const wchar_t* name);
        PBZ_CONTEXT_API class FProperty* GetPropertyByNameInChain(const wchar_t* name);
    };
    class UClass;
    struct FImplementedInterface { UClass* Class; int PointerOffset; bool bImplementedByK2; };
    template<int> class TSizedDefaultAllocator {};
    template<class T, class Allocator = TSizedDefaultAllocator<32>>
    class TArray { public: T* Data; int Num; int Max; };
    class UClass
    {
    public:
        PBZ_CONTEXT_API TArray<FImplementedInterface>& GetInterfaces();
    };
    struct FFrame;
    class UnrealScriptFunctionCallableContext
    {
    public:
        UObject* Context;
        FFrame& TheStack;
        void* RESULT_DECL;
    };
    class UFunction
    {
    public:
        using Callback = std::function<void(UnrealScriptFunctionCallableContext&, void*)>;
        PBZ_CONTEXT_API int RegisterPreHook(const Callback&, void*);
        PBZ_CONTEXT_API int RegisterPostHook(const Callback&, void*);
        PBZ_CONTEXT_API auto GetFuncPtr() -> void(*)(UObject*, FFrame&, void*);
    };
    struct FScriptMapLayout
    {
        int ValueOffset;
        struct { int HashNextIdOffset, HashIndexOffset, Size; struct { int Alignment, Size; } SparseArrayLayout; } SetLayout;
    };
    class FProperty
    {
    public:
        PBZ_CONTEXT_API int& GetElementSize();
        PBZ_CONTEXT_API int& GetOffset_Internal();
    };
    class FMapProperty
    {
    public:
        PBZ_CONTEXT_API FScriptMapLayout& GetMapLayout();
        PBZ_CONTEXT_API FProperty*& GetKeyProp();
        PBZ_CONTEXT_API FProperty*& GetValueProp();
    };
    class FName
    {
    public:
        PBZ_CONTEXT_API const std::wstring ToString() const;
    };
    struct FFrame
    {
        PBZ_CONTEXT_API UFunction*& Node();
    };
    namespace Hook
    {
        using ScriptCallback = std::function<void(UObject*, FFrame&, void*)>;
        PBZ_CONTEXT_API void RegisterProcessLocalScriptFunctionPreCallback(ScriptCallback);
        PBZ_CONTEXT_API void RegisterProcessLocalScriptFunctionPostCallback(ScriptCallback);
    }
}

namespace RC::LuaMadeSimple
{
    class Lua
    {
    public:
        PBZ_CONTEXT_API long long get_integer(int index) const;
        PBZ_CONTEXT_API bool get_bool(int index) const;
        PBZ_CONTEXT_API double get_number(int index) const;
        PBZ_CONTEXT_API void set_integer(long long value) const;
        PBZ_CONTEXT_API void set_string(const char* value, size_t length) const;
    };
}

namespace RC::LuaType
{
    struct PusherParams
    {
        const int operation;
        const RC::LuaMadeSimple::Lua& lua;
        RC::Unreal::UObject* base;
        void* data;
        RC::Unreal::FProperty* property;
        int stored_at_index = 1;
        bool create_new_if_get_non_trivial_local = true;
    };
    PBZ_CONTEXT_API void push_classproperty(const PusherParams&);
}

namespace
{
    struct ScriptFrame { RC::Unreal::UObject* object; RC::Unreal::FFrame* frame; };
    thread_local std::vector<ScriptFrame> frames;
    thread_local std::vector<ScriptFrame> interfaceFrames;
    std::vector<RC::Unreal::UFunction*> interfaceFunctions;
    std::vector<RC::Unreal::UFunction*> activeInterfaceFunctions;
    std::atomic<bool> enabled{false};
    INIT_ONCE initialized = INIT_ONCE_STATIC_INIT;

    BOOL CALLBACK initialize(PINIT_ONCE, PVOID, PVOID*)
    {
        // UE4SS owns the script callbacks for the process lifetime. Keep their
        // code loaded across Lua mod reloads; reset only disables collection.
        HMODULE module = nullptr;
        GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
            reinterpret_cast<LPCWSTR>(&initialize), &module);
        RC::Unreal::Hook::RegisterProcessLocalScriptFunctionPreCallback(
            [](auto* object, auto& frame, void*) {
                if (enabled.load(std::memory_order_relaxed)) frames.push_back({object, &frame});
            });
        RC::Unreal::Hook::RegisterProcessLocalScriptFunctionPostCallback(
            [](auto*, auto& frame, void*) {
                if (!frames.empty() && frames.back().frame == &frame) frames.pop_back();
            });
        return TRUE;
    }

    // The pinned accessors and existing UClass metatable path use the first
    // LuaMadeSimple member, lua_State*.
    struct LuaStateView { void* state; };

    // UE native interfaces expose _getUObject after their virtual destructor.
    // Context points at this interface, not the UObject at the allocation base.
    struct NativeInterface
    {
        virtual ~NativeInterface() = default;
        virtual RC::Unreal::UObject* _getUObject() const = 0;
    };

    void pushName(const RC::LuaMadeSimple::Lua& lua, RC::Unreal::UObject* value)
    {
        auto name = value->GetFullName();
        int size = WideCharToMultiByte(CP_UTF8, 0, name.data(), static_cast<int>(name.size()), nullptr, 0, nullptr, nullptr);
        std::string utf8(size, '\0');
        WideCharToMultiByte(CP_UTF8, 0, name.data(), static_cast<int>(name.size()), utf8.data(), size, nullptr, nullptr);
        lua.set_string(utf8.data(), utf8.size());
    }
}

extern "C" __declspec(dllexport) int pbz_script_context_empty_class(void* state)
{
    // The caller has read a UClass parameter, creating its metatable in this
    // Lua state. ClassProperty needs a typed null userdata to write nullptr.
    LuaStateView view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    RC::Unreal::UClass* empty = nullptr;
    RC::LuaType::PusherParams params{0,lua,nullptr,&empty,nullptr};
    RC::LuaType::push_classproperty(params);
    return 1;
}

extern "C" __declspec(dllexport) int pbz_script_context_enable(void* state)
{
    LuaStateView view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    auto active = lua.get_bool(1);
    if (active) InitOnceExecuteOnce(&initialized, initialize, nullptr, nullptr);
    enabled.store(active, std::memory_order_relaxed);
    if (!active)
    {
        activeInterfaceFunctions.clear();
        frames.clear();
        interfaceFrames.clear();
    }
    return 0;
}

// Returns the innermost Blueprint context first, with its exact object identity.
extern "C" __declspec(dllexport) int pbz_script_context_at(void* state)
{
    if (!enabled.load(std::memory_order_relaxed)) return 0;
    LuaStateView view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    auto depth = lua.get_integer(1);
    if (depth < 1 || static_cast<size_t>(depth) > frames.size()) return 0;
    auto& entry = frames[frames.size() - static_cast<size_t>(depth)];
    auto* object = entry.object;
    pushName(lua, object);
    lua.set_integer(reinterpret_cast<long long>(object));
    pushName(lua, reinterpret_cast<RC::Unreal::UObject*>(entry.frame->Node()));
    return 3;
}

extern "C" __declspec(dllexport) int pbz_script_context_watch_interface(void* state)
{
    LuaStateView view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    auto* function = reinterpret_cast<RC::Unreal::UFunction*>(lua.get_integer(1));
    if (std::find(activeInterfaceFunctions.begin(),activeInterfaceFunctions.end(),function)==activeInterfaceFunctions.end())
        activeInterfaceFunctions.push_back(function);
    for (auto* watched : interfaceFunctions) if (watched == function) return 0;
    function->RegisterPreHook([function](auto& context, void*) {
        if (!enabled.load(std::memory_order_relaxed)) return;
        if (std::find(activeInterfaceFunctions.begin(),activeInterfaceFunctions.end(),function)==activeInterfaceFunctions.end()) return;
        auto* object = reinterpret_cast<NativeInterface*>(context.Context)->_getUObject();
        interfaceFrames.push_back({object, &context.TheStack});
    }, nullptr);
    function->RegisterPostHook([](auto& context, void*) {
        if (!interfaceFrames.empty() && interfaceFrames.back().frame == &context.TheStack) interfaceFrames.pop_back();
    }, nullptr);
    interfaceFunctions.push_back(function);
    return 0;
}

extern "C" __declspec(dllexport) int pbz_script_context_interface_object(void* state)
{
    if (!enabled.load(std::memory_order_relaxed)) return 0;
    if (interfaceFrames.empty()) return 0;
    LuaStateView view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    pushName(lua, interfaceFrames.back().object);
    return 1;
}

#include "damage-scaling.inl"
