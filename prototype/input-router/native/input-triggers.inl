namespace
{
    using InputTriggerUpdate = unsigned char(*)(Object*,Object*,const void*,float);
    using InputTriggerType = unsigned char(*)(Object*);
    using InputTriggerBlocking = bool(*)(Object*,unsigned char);
    struct InputTriggerHook { InputTriggerUpdate original; size_t updateSlot,typeSlot; };
    struct InputTriggerObservation
    {
        Object* frameLibrary;
        RC::Unreal::UFunction* frameFunction;
        Object* playerInput = nullptr;
        long long frame = 0;
        unsigned char state = 0,kind = 0;
        bool blocked = false,valid = false;
    };
    std::mutex inputTriggerMutex;
    std::unordered_map<void**,InputTriggerHook> inputTriggerHooks;
    std::unordered_map<Object*,InputTriggerObservation> inputTriggerObservations;

    unsigned char observeInputTrigger(Object* trigger,Object* playerInput,const void* value,float delta)
    {
        InputTriggerHook hook;
        {
            std::lock_guard<std::mutex> lock(inputTriggerMutex);
            hook=inputTriggerHooks.at(*reinterpret_cast<void***>(trigger));
        }
        auto result=hook.original(trigger,playerInput,value,delta);
        if (!enabled.load(std::memory_order_relaxed)) return result;
        InputTriggerObservation observed;
        {
            std::lock_guard<std::mutex> lock(inputTriggerMutex);
            auto entry=inputTriggerObservations.find(trigger);
            if (entry==inputTriggerObservations.end()) return result;
            observed=entry->second;
        }
        auto** table=*reinterpret_cast<void***>(trigger);
        observed.kind=reinterpret_cast<InputTriggerType>(table[hook.typeSlot])(trigger);
        // FTriggerStateTracker calls IsBlocking two virtual slots after UpdateState.
        observed.blocked=reinterpret_cast<InputTriggerBlocking>(table[hook.updateSlot+2])(trigger,result);
        observed.frameLibrary->ProcessEvent(observed.frameFunction,&observed.frame);
        observed.playerInput=playerInput;
        observed.state=result;
        observed.valid=true;
        {
            std::lock_guard<std::mutex> lock(inputTriggerMutex);
            auto entry=inputTriggerObservations.find(trigger);
            if (entry!=inputTriggerObservations.end()) entry->second=observed;
        }
        return result;
    }

    void watchInputTrigger(Object* trigger,RC::Unreal::UFunction* update,RC::Unreal::UFunction* type,
        Object* frameLibrary,RC::Unreal::UFunction* frameFunction)
    {
        auto updateSlot=virtualSlot(update),typeSlot=virtualSlot(type);
        auto** table=*reinterpret_cast<void***>(trigger);
        std::lock_guard<std::mutex> lock(inputTriggerMutex);
        auto entry=inputTriggerHooks.find(table);
        if (entry==inputTriggerHooks.end())
        {
            inputTriggerHooks.emplace(table,InputTriggerHook{
                reinterpret_cast<InputTriggerUpdate>(table[updateSlot]),updateSlot,typeSlot});
            try { replaceVirtual(table,updateSlot,reinterpret_cast<void*>(&observeInputTrigger)); }
            catch (...) { inputTriggerHooks.erase(table); throw; }
        }
        inputTriggerObservations[trigger]={frameLibrary,frameFunction};
    }
}

extern "C" __declspec(dllexport) int pbz_input_watch_trigger(void* state)
{
    LuaStateView view{state};
    auto& lua=*reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    try
    {
        auto* trigger=reinterpret_cast<Object*>(lua.get_integer(1));
        auto* update=reinterpret_cast<RC::Unreal::UFunction*>(lua.get_integer(1));
        auto* type=reinterpret_cast<RC::Unreal::UFunction*>(lua.get_integer(1));
        auto* library=reinterpret_cast<Object*>(lua.get_integer(1));
        auto* frame=reinterpret_cast<RC::Unreal::UFunction*>(lua.get_integer(1));
        if (!trigger || !update || !type || !library || !frame)
            throw std::runtime_error("Input trigger observation arguments are invalid");
        watchInputTrigger(trigger,update,type,library,frame);
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

extern "C" __declspec(dllexport) int pbz_input_trigger_state(void* state)
{
    LuaStateView view{state};
    auto& lua=*reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    auto* trigger=reinterpret_cast<Object*>(lua.get_integer(1));
    auto* playerInput=reinterpret_cast<Object*>(lua.get_integer(1));
    auto frame=lua.get_integer(1);
    if (!enabled.load(std::memory_order_relaxed)) return 0;
    std::lock_guard<std::mutex> lock(inputTriggerMutex);
    auto entry=inputTriggerObservations.find(trigger);
    if (entry==inputTriggerObservations.end()) return 0;
    const auto& observed=entry->second;
    if (!observed.valid || observed.playerInput!=playerInput || observed.frame!=frame) return 0;
    lua.set_integer(observed.state);
    lua.set_integer(observed.kind);
    lua.set_integer(observed.blocked ? 1 : 0);
    return 3;
}

extern "C" __declspec(dllexport) int pbz_input_reset_triggers(void* state)
{
    LuaStateView view{state};
    auto& lua=*reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    std::lock_guard<std::mutex> lock(inputTriggerMutex);
    std::string failure;
    for (auto entry=inputTriggerHooks.begin();entry!=inputTriggerHooks.end();)
    {
        try
        {
            replaceVirtual(entry->first,entry->second.updateSlot,reinterpret_cast<void*>(entry->second.original));
            entry=inputTriggerHooks.erase(entry);
        }
        catch (const std::exception& error) { failure+=error.what(); ++entry; }
    }
    inputTriggerObservations.clear();
    lua.set_integer(failure.empty() ? 1 : 0);
    if (failure.empty()) return 1;
    lua.set_string(failure.data(),failure.size());
    return 2;
}
