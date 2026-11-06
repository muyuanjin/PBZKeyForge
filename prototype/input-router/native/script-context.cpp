#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <atomic>
#include <functional>
#include <string>
#include <vector>

#ifndef PBZ_CONTEXT_API
#define PBZ_CONTEXT_API __declspec(dllimport)
#endif

namespace RC::Unreal
{
    class UObject
    {
    public:
        PBZ_CONTEXT_API std::wstring GetFullName(UObject* stopOuter = nullptr) const;
    };
    class UFunction;
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
        PBZ_CONTEXT_API void set_integer(long long value) const;
        PBZ_CONTEXT_API void set_string(const char* value, size_t length) const;
    };
}

namespace
{
    struct ScriptFrame { RC::Unreal::UObject* object; RC::Unreal::FFrame* frame; };
    thread_local std::vector<ScriptFrame> frames;
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

    // The pinned LuaMadeSimple leaf accessors use only the first member,
    // lua_State*. No Lua wrapper is constructed and no registry is accessed.
    struct LuaStateView { void* state; };
}

extern "C" __declspec(dllexport) int pbz_script_context_enable(void* state)
{
    LuaStateView view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    InitOnceExecuteOnce(&initialized, initialize, nullptr, nullptr);
    enabled.store(lua.get_bool(1), std::memory_order_relaxed);
    return 0;
}

// Returns the innermost Blueprint context first, with its exact object identity.
extern "C" __declspec(dllexport) int pbz_script_context_at(void* state)
{
    LuaStateView view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    auto depth = lua.get_integer(1);
    if (depth < 1 || static_cast<size_t>(depth) > frames.size()) return 0;
    auto& entry = frames[frames.size() - static_cast<size_t>(depth)];
    auto* object = entry.object;
    auto pushName = [&lua](auto* value) {
        auto name = value->GetFullName();
        int size = WideCharToMultiByte(CP_UTF8, 0, name.data(), static_cast<int>(name.size()), nullptr, 0, nullptr, nullptr);
        std::string utf8(size, '\0');
        WideCharToMultiByte(CP_UTF8, 0, name.data(), static_cast<int>(name.size()), utf8.data(), size, nullptr, nullptr);
        lua.set_string(utf8.data(), utf8.size());
    };
    pushName(object);
    lua.set_integer(reinterpret_cast<long long>(object));
    pushName(reinterpret_cast<RC::Unreal::UObject*>(entry.frame->Node()));
    return 3;
}
