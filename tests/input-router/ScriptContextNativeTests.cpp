#define PBZ_CONTEXT_API
#include "../../prototype/input-router/native/script-context.cpp"
#include <cassert>
#include <iostream>
#include <unordered_map>

namespace
{
    struct TestState { long long depth = 1; bool enabled = true; long long address = 0; std::vector<std::string> names; };
    RC::Unreal::Hook::ScriptCallback pre, post;
    std::unordered_map<const void*, std::wstring> names;
    std::unordered_map<void*, RC::Unreal::UFunction*> nodes;
    const TestState* test(const RC::LuaMadeSimple::Lua* lua) { return *reinterpret_cast<TestState* const*>(lua); }
}
namespace RC::Unreal
{
    std::wstring UObject::GetFullName(UObject*) const { return names.at(this); }
    UFunction*& FFrame::Node() { return nodes.at(this); }
    namespace Hook
    {
        void RegisterProcessLocalScriptFunctionPreCallback(ScriptCallback callback) { pre = std::move(callback); }
        void RegisterProcessLocalScriptFunctionPostCallback(ScriptCallback callback) { post = std::move(callback); }
    }
}
namespace RC::LuaMadeSimple
{
    long long Lua::get_integer(int index) const { assert(index == 1); return test(this)->depth; }
    bool Lua::get_bool(int index) const { assert(index == 1); return test(this)->enabled; }
    void Lua::set_integer(long long value) const { const_cast<TestState*>(test(this))->address = value; }
    void Lua::set_string(const char* text, size_t length) const { const_cast<TestState*>(test(this))->names.emplace_back(text,length); }
}
int main()
{
    TestState state;
    assert(pbz_script_context_enable(&state) == 0);
    RC::Unreal::UObject outer, inner, outerFn, innerFn;
    RC::Unreal::FFrame outerFrame, innerFrame;
    names[&outer] = L"GA /Level.Player.Ability";
    names[&inner] = L"GAA /Level.Player.Action";
    names[&outerFn] = L"Function /Base:Activate";
    names[&innerFn] = L"Function /Action:Comsume";
    nodes[&outerFrame] = reinterpret_cast<RC::Unreal::UFunction*>(&outerFn);
    nodes[&innerFrame] = reinterpret_cast<RC::Unreal::UFunction*>(&innerFn);
    assert(pbz_script_context_at(&state) == 0);
    pre(&outer,outerFrame,nullptr);
    pre(&inner,innerFrame,nullptr);
    assert(pbz_script_context_at(&state) == 3);
    assert(state.address == reinterpret_cast<long long>(&inner));
    assert(state.names[0] == "GAA /Level.Player.Action" && state.names[1] == "Function /Action:Comsume");
    state.depth = 2; state.names.clear();
    assert(pbz_script_context_at(&state) == 3);
    assert(state.address == reinterpret_cast<long long>(&outer));
    post(&inner,innerFrame,nullptr);
    assert(pbz_script_context_at(&state) == 0);
    state.depth = 1;
    state.enabled = false; pbz_script_context_enable(&state);
    pre(&inner,innerFrame,nullptr); post(&inner,innerFrame,nullptr);
    assert(pbz_script_context_at(&state) == 3 && state.address == reinterpret_cast<long long>(&outer));
    post(&outer,outerFrame,nullptr);
    assert(pbz_script_context_at(&state) == 0);
    state.enabled = true; pbz_script_context_enable(&state);
    pre(&outer,outerFrame,nullptr); post(&outer,outerFrame,nullptr);
    assert(pbz_script_context_at(&state) == 0);
    std::cout << "ScriptContextNativeTests PASS\n";
}
