namespace
{
    using ResourceActionCheck = bool(*)(Object*);
    using ResourceActionAbility = Object*(*)(Object*);
    using ResourceEventTrigger = void(*)(Object*,const void*,const void*);
    struct ResourceActionHook { ResourceActionCheck original; int requiredOffset; int ignoredOffset; size_t slot; };
    struct ResourceEventHook { ResourceEventTrigger original; size_t slot; };
    struct ResourceTagContainer
    {
        RC::Unreal::TArray<RC::Unreal::FName> tags;
        RC::Unreal::TArray<RC::Unreal::FName> parents;
    };
    static_assert(sizeof(ResourceTagContainer)==32);
    std::unordered_map<void**,ResourceActionHook> resourceActionHooks;
    std::unordered_map<void**,ResourceEventHook> resourceEventHooks;
    ResourceActionAbility resourceActionAbility = nullptr;
    size_t resourceActionSlot = 0;

    std::pair<unsigned char*,size_t> resourceFunctionCode(void* pointer)
    {
        auto* code = static_cast<unsigned char*>(pointer);
        DWORD64 image = 0;
        auto* entry = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(code),&image,nullptr);
        if (!entry || image+entry->BeginAddress!=reinterpret_cast<DWORD64>(code))
            throw std::runtime_error("Resource native function has no complete function extent");
        return {code,static_cast<size_t>(entry->EndAddress-entry->BeginAddress)};
    }

    void prepareResourceActions(RC::Unreal::UFunction* execute,RC::Unreal::UFunction* getter)
    {
        auto [code,length] = resourceFunctionCode(reinterpret_cast<void*>(getter->GetFuncPtr()));
        ResourceActionAbility ability = nullptr;
        size_t matches = 0;
        for (size_t i=0;i+8<=length;++i)
        {
            if (code[i]!=0xe8 || code[i+5]!=0x48 || code[i+6]!=0x89 || code[i+7]!=0x03) continue;
            int displacement;
            std::memcpy(&displacement,code+i+1,sizeof(displacement));
            ability=reinterpret_cast<ResourceActionAbility>(code+i+5+displacement);
            ++matches;
        }
        if (matches!=1) throw std::runtime_error("Action ability getter native dispatch is unsupported");
        std::tie(code,length) = resourceFunctionCode(reinterpret_cast<void*>(execute->GetFuncPtr()));
        unsigned char* body = nullptr;
        matches = 0;
        const unsigned char before[] = {0x4c,0x8d,0x44,0x24,0x38,0x48,0x8b,0xce,0xe8};
        const unsigned char after[] = {0x48,0x8b,0x4c,0x24,0x30};
        for (size_t i=0;i+sizeof(before)+4+sizeof(after)<=length;++i)
        {
            if (std::memcmp(code+i,before,sizeof(before)) ||
                std::memcmp(code+i+sizeof(before)+4,after,sizeof(after))) continue;
            int displacement;
            std::memcpy(&displacement,code+i+sizeof(before),sizeof(displacement));
            body=code+i+sizeof(before)+4+displacement;
            ++matches;
        }
        if (matches!=1) throw std::runtime_error("Ability action execution native dispatch is unsupported");
        std::tie(code,length) = resourceFunctionCode(body);
        size_t slot = 0;
        matches = 0;
        const unsigned char check[] = {0x48,0x8b,0x02,0x48,0x8b,0xca,0xff,0x90};
        for (size_t i=0;i+sizeof(check)+6<=length;++i)
        {
            if (std::memcmp(code+i,check,sizeof(check)) || code[i+sizeof(check)+4]!=0x84 || code[i+sizeof(check)+5]!=0xc0) continue;
            int displacement;
            std::memcpy(&displacement,code+i+sizeof(check),sizeof(displacement));
            if (displacement<0 || displacement%sizeof(void*)) continue;
            slot=static_cast<size_t>(displacement)/sizeof(void*);
            ++matches;
        }
        if (matches!=1) throw std::runtime_error("Action resource qualification virtual dispatch is unsupported");
        std::lock_guard<std::mutex> lock(damageMutex);
        resourceActionAbility=ability;
        resourceActionSlot=slot;
    }

    bool fullWeaponResourceTag(const RC::Unreal::FName& tag)
    {
        auto name=tag.ToString();
        return name==L"CharacterState.AbilityState.BaiShanFengLeiJian.Wind"
            || name==L"CharacterState.AbilityState.BaiShanFengLeiJian.Thunder";
    }

    bool resourceActionCanActivate(Object* action)
    {
        ResourceActionHook hook;
        ResourceActionAbility getter;
        {
            std::lock_guard<std::mutex> lock(damageMutex);
            hook=resourceActionHooks.at(*reinterpret_cast<void***>(action));
            getter=resourceActionAbility;
        }
        auto* ability=getter(action);
        bool bypass=false;
        {
            std::lock_guard<std::mutex> lock(damageMutex);
            bypass=enabled.load(std::memory_order_relaxed) && abilityResources.find(ability)!=abilityResources.end();
        }
        if (!bypass) return hook.original(action);
        auto& required=*reinterpret_cast<ResourceTagContainer*>(reinterpret_cast<unsigned char*>(action)+hook.requiredOffset);
        auto& ignored=*reinterpret_cast<ResourceTagContainer*>(reinterpret_cast<unsigned char*>(action)+hook.ignoredOffset);
        for (int i=0;i<ignored.tags.Num;++i)
            if (fullWeaponResourceTag(ignored.tags.Data[i])) return false;
        std::vector<RC::Unreal::FName> retained;
        for (int i=0;i<required.tags.Num;++i)
            if (!fullWeaponResourceTag(required.tags.Data[i])) retained.push_back(required.tags.Data[i]);
        if (retained.size()==static_cast<size_t>(required.tags.Num)) return hook.original(action);
        struct RestoreRequirements
        {
            ResourceTagContainer& target;
            ResourceTagContainer original;
            ~RestoreRequirements() { target=original; }
        } restore{required,required};
        required={{retained.data(),static_cast<int>(retained.size()),static_cast<int>(retained.size())},{nullptr,0,0}};
        return hook.original(action);
    }

    void resourceEventTrigger(Object* event,const void* bullet,const void* data)
    {
        ResourceEventHook hook;
        bool bypass=false;
        {
            std::lock_guard<std::mutex> lock(damageMutex);
            hook=resourceEventHooks.at(*reinterpret_cast<void***>(event));
            auto source=bullet ? bulletResources.find(*static_cast<Object* const*>(bullet)) : bulletResources.end();
            bypass=enabled.load(std::memory_order_relaxed) && source!=bulletResources.end() && source->second;
        }
        if (!bypass) hook.original(event,bullet,data);
    }

    void watchResourceAction(Object* action)
    {
        std::lock_guard<std::mutex> lock(damageMutex);
        if (!resourceActionAbility || !resourceActionSlot) throw std::runtime_error("Action resource dispatch has not been prepared");
        auto** table=*reinterpret_cast<void***>(action);
        auto seen=resourceActionHooks.find(table);
        const bool existed=seen!=resourceActionHooks.end();
        if (seen!=resourceActionHooks.end() && table[resourceActionSlot]==reinterpret_cast<void*>(&resourceActionCanActivate)) return;
        auto* property=action->GetPropertyByNameInChain(L"ActivationRequirements");
        if (!property) throw std::runtime_error("Action activation requirements are unavailable");
        auto* type=reinterpret_cast<RC::Unreal::FStructProperty*>(property)->GetStruct().Ptr;
        auto member=[&](const wchar_t* name)
        {
            auto* value=reinterpret_cast<RC::Unreal::UStruct*>(type)->FindProperty(RC::Unreal::FName(name,RC::Unreal::FNAME_Find,nullptr));
            if (!value || value->GetElementSize()!=sizeof(ResourceTagContainer))
                throw std::runtime_error("Action resource tag requirements have an unsupported native layout");
            auto* tagsType=reinterpret_cast<RC::Unreal::FStructProperty*>(value)->GetStruct().Ptr;
            auto* tags=reinterpret_cast<RC::Unreal::UStruct*>(tagsType)->FindProperty(RC::Unreal::FName(L"GameplayTags",RC::Unreal::FNAME_Find,nullptr));
            if (!tags || tags->GetOffset_Internal()!=0 || tags->GetElementSize()!=sizeof(RC::Unreal::TArray<RC::Unreal::FName>))
                throw std::runtime_error("Resource tag container has an unsupported native layout");
            return property->GetOffset_Internal()+value->GetOffset_Internal();
        };
        auto required=member(L"RequireTags"),ignored=member(L"IgnoreTags");
        auto previous=seen!=resourceActionHooks.end() ? seen->second : ResourceActionHook{};
        resourceActionHooks[table]={reinterpret_cast<ResourceActionCheck>(table[resourceActionSlot]),required,ignored,resourceActionSlot};
        try { replaceVirtual(table,resourceActionSlot,reinterpret_cast<void*>(&resourceActionCanActivate)); }
        catch (...)
        {
            if (existed) resourceActionHooks[table]=previous;
            else resourceActionHooks.erase(table);
            throw;
        }
    }

    void watchResourceEvent(Object* event,RC::Unreal::UFunction* trigger)
    {
        auto slot=virtualSlot(trigger);
        std::lock_guard<std::mutex> lock(damageMutex);
        auto** table=*reinterpret_cast<void***>(event);
        auto seen=resourceEventHooks.find(table);
        const bool existed=seen!=resourceEventHooks.end();
        if (seen!=resourceEventHooks.end() && table[slot]==reinterpret_cast<void*>(&resourceEventTrigger)) return;
        auto previous=seen!=resourceEventHooks.end() ? seen->second : ResourceEventHook{};
        resourceEventHooks[table]={reinterpret_cast<ResourceEventTrigger>(table[slot]),slot};
        try { replaceVirtual(table,slot,reinterpret_cast<void*>(&resourceEventTrigger)); }
        catch (...)
        {
            if (existed) resourceEventHooks[table]=previous;
            else resourceEventHooks.erase(table);
            throw;
        }
    }
}

extern "C" __declspec(dllexport) int pbz_resource_prepare_actions(void* state)
{
    LuaStateView view{state};
    auto& lua=*reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    try
    {
        auto* getter=reinterpret_cast<RC::Unreal::UFunction*>(lua.get_integer(2));
        auto* execute=reinterpret_cast<RC::Unreal::UFunction*>(lua.get_integer(1));
        if (!execute || !getter) throw std::runtime_error("Action resource preparation needs native execution and ability getter functions");
        prepareResourceActions(execute,getter);
        lua.set_integer(1);return 1;
    }
    catch (const std::exception& error) { lua.set_integer(0);lua.set_string(error.what(),std::strlen(error.what()));return 2; }
}

extern "C" __declspec(dllexport) int pbz_resource_watch_action(void* state)
{
    LuaStateView view{state};
    auto& lua=*reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    try
    {
        auto* action=reinterpret_cast<Object*>(lua.get_integer(1));
        if (!action) throw std::runtime_error("Resource action is invalid");
        watchResourceAction(action);lua.set_integer(1);return 1;
    }
    catch (const std::exception& error) { lua.set_integer(0);lua.set_string(error.what(),std::strlen(error.what()));return 2; }
}

extern "C" __declspec(dllexport) int pbz_resource_watch_event(void* state)
{
    LuaStateView view{state};
    auto& lua=*reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    try
    {
        auto* trigger=reinterpret_cast<RC::Unreal::UFunction*>(lua.get_integer(2));
        auto* event=reinterpret_cast<Object*>(lua.get_integer(1));
        if (!trigger || !event) throw std::runtime_error("Resource event is invalid");
        watchResourceEvent(event,trigger);lua.set_integer(1);return 1;
    }
    catch (const std::exception& error) { lua.set_integer(0);lua.set_string(error.what(),std::strlen(error.what()));return 2; }
}

extern "C" __declspec(dllexport) int pbz_resource_ability(void* state)
{
    LuaStateView view{state};
    auto& lua=*reinterpret_cast<const RC::LuaMadeSimple::Lua*>(&view);
    auto bypass=lua.get_bool(2);
    auto* ability=reinterpret_cast<Object*>(lua.get_integer(1));
    if (!ability) { lua.set_integer(0);return 1; }
    std::lock_guard<std::mutex> lock(damageMutex);
    if (bypass) abilityResources[ability]=true;
    else abilityResources.erase(ability);
    lua.set_integer(1);return 1;
}
