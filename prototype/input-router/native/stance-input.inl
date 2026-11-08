namespace
{
    struct StanceMutator
    {
        RC::Unreal::UFunction* function;
        int ownerOffset,tagOffset,replicateOffset;
    };
    struct StanceGate
    {
        RC::Unreal::UObject* owner = nullptr;
        RC::Unreal::UObject* passive = nullptr;
        RC::Unreal::UObject* library = nullptr;
        RC::Unreal::UFunction* down = nullptr;
        RC::Unreal::UFunction* release = nullptr;
        StanceMutator add{},remove{};
        bool contribution = false;
    } stanceGate;
    struct SuppressedStanceCall
    {
        RC::Unreal::FFrame* frame;
        RC::Unreal::UObject** argument;
        RC::Unreal::UObject* original;
    };
    std::vector<RC::Unreal::UFunction*> stanceHooks;
    thread_local std::vector<RC::Unreal::FFrame*> stanceFrames;
    thread_local std::vector<SuppressedStanceCall> suppressedStanceCalls;
    thread_local bool applyingStance = false;

    bool stanceActive() { return stanceGate.owner != nullptr; }

    void beginStanceFrame(RC::Unreal::UObject* object,RC::Unreal::FFrame& frame)
    {
        if (object == stanceGate.passive && stanceActive()
            && (frame.Node() == stanceGate.down || frame.Node() == stanceGate.release))
            stanceFrames.push_back(&frame);
    }
    void endStanceFrame(RC::Unreal::FFrame& frame)
    {
        if (!stanceFrames.empty() && stanceFrames.back() == &frame) stanceFrames.pop_back();
    }

    StanceMutator stanceMutator(RC::Unreal::UFunction* function)
    {
        using namespace RC::Unreal;
        auto* structure = reinterpret_cast<UStruct*>(function);
        auto* owner = structure->FindProperty(FName(L"AbilitySystem",FNAME_Find,nullptr));
        auto* tag = structure->FindProperty(FName(L"GameplayTag",FNAME_Find,nullptr));
        auto* replicate = structure->FindProperty(FName(L"bShouldReplicate",FNAME_Find,nullptr));
        if (!owner || !tag || !replicate || owner->GetElementSize() != sizeof(void*)
            || tag->GetElementSize() != sizeof(FName) || replicate->GetElementSize() != sizeof(bool))
            throw std::runtime_error("Stance gameplay-tag mutator layout unavailable");
        return {function,owner->GetOffset_Internal(),tag->GetOffset_Internal(),replicate->GetOffset_Internal()};
    }

    void watchStanceMutator(const StanceMutator& mutator)
    {
        if (std::find(stanceHooks.begin(),stanceHooks.end(),mutator.function) != stanceHooks.end()) return;
        mutator.function->RegisterPreHook([mutator](auto& context,void*) {
            if (!stanceActive() || stanceFrames.empty() || applyingStance) return;
            auto* parameters = context.TheStack.Locals();
            auto** owner = reinterpret_cast<RC::Unreal::UObject**>(parameters+mutator.ownerOffset);
            if (*owner != stanceGate.owner) return;
            auto* tag = reinterpret_cast<RC::Unreal::FName*>(parameters+mutator.tagOffset);
            if (tag->ToString() != L"CharacterState.AbilityState.Stance.HoldingStanceKey") return;
            suppressedStanceCalls.push_back({&context.TheStack,owner,*owner});
            // Both game mutators return immediately for a null AbilitySystem.
            *owner = nullptr;
        },nullptr);
        mutator.function->RegisterPostHook([](auto& context,void*) {
            if (suppressedStanceCalls.empty() || suppressedStanceCalls.back().frame != &context.TheStack) return;
            auto saved = suppressedStanceCalls.back();
            *saved.argument = saved.original;
            suppressedStanceCalls.pop_back();
        },nullptr);
        stanceHooks.push_back(mutator.function);
    }

    void stanceContribution(bool held)
    {
        if (held == stanceGate.contribution) return;
        const auto& mutator = held ? stanceGate.add : stanceGate.remove;
        std::vector<unsigned char> parameters(mutator.function->GetParmsSize(),0);
        *reinterpret_cast<RC::Unreal::UObject**>(parameters.data()+mutator.ownerOffset) = stanceGate.owner;
        auto tag = RC::Unreal::FName(L"CharacterState.AbilityState.Stance.HoldingStanceKey",RC::Unreal::FNAME_Add,nullptr);
        std::memcpy(parameters.data()+mutator.tagOffset,&tag,sizeof(tag));
        *reinterpret_cast<bool*>(parameters.data()+mutator.replicateOffset) = false;
        struct Applying
        {
            bool previous = applyingStance;
            Applying() { applyingStance = true; }
            ~Applying() { applyingStance = previous; }
        } applying;
        stanceGate.library->ProcessEvent(mutator.function,parameters.data());
        stanceGate.contribution = held;
    }
}

extern "C" __declspec(dllexport) int pbz_stance_prepare(void* state)
{
    LuaStateView view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    try
    {
        auto* owner = reinterpret_cast<RC::Unreal::UObject*>(lua.get_integer(1));
        auto* passive = reinterpret_cast<RC::Unreal::UObject*>(lua.get_integer(1));
        auto* library = reinterpret_cast<RC::Unreal::UObject*>(lua.get_integer(1));
        auto* add = reinterpret_cast<RC::Unreal::UFunction*>(lua.get_integer(1));
        auto* remove = reinterpret_cast<RC::Unreal::UFunction*>(lua.get_integer(1));
        auto* down = reinterpret_cast<RC::Unreal::UFunction*>(lua.get_integer(1));
        auto* release = reinterpret_cast<RC::Unreal::UFunction*>(lua.get_integer(1));
        auto held = lua.get_bool(1);
        auto count = lua.get_integer(1);
        if (!owner || !passive || !library || !add || !remove || !down || !release)
            throw std::runtime_error("Stance input context unavailable");
        if (stanceGate.owner && (stanceGate.owner != owner || stanceGate.passive != passive))
            throw std::runtime_error("Previous stance input context must be restored");
        if (!stanceGate.owner)
        {
            auto addMutator = stanceMutator(add),removeMutator = stanceMutator(remove);
            InitOnceExecuteOnce(&initialized,initialize,nullptr,nullptr);
            watchStanceMutator(addMutator);
            watchStanceMutator(removeMutator);
            stanceGate = {owner,passive,library,down,release,addMutator,removeMutator,count > 0};
        }
        stanceContribution(held);
        lua.set_integer(1);
        return 1;
    }
    catch (const std::exception& error)
    {
        lua.set_integer(0);
        lua.set_string(error.what(),std::strlen(error.what()));
        return 2;
    }
}

extern "C" __declspec(dllexport) int pbz_stance_restore(void* state)
{
    LuaStateView view{state};
    auto& lua = *reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    try
    {
        auto* owner = reinterpret_cast<RC::Unreal::UObject*>(lua.get_integer(1));
        auto held = lua.get_bool(1);
        if (owner && stanceGate.owner == owner) stanceContribution(held);
        stanceGate = {};
        stanceFrames.clear();
        lua.set_integer(1);
        return 1;
    }
    catch (const std::exception& error)
    {
        lua.set_integer(0);
        lua.set_string(error.what(),std::strlen(error.what()));
        return 2;
    }
}
