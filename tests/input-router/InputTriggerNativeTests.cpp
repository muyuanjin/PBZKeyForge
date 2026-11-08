#define main contextBaselineMain
#include "ScriptContextNativeTests.cpp"
#undef main

namespace
{
    struct TriggerFixture { void** table; unsigned char state,kind; bool blocking; int updates; };
    unsigned char originalTrigger(Object* object,Object*,const void* value,float delta)
    {
        assert(value && delta==.125f);
        auto& trigger=*reinterpret_cast<TriggerFixture*>(object);
        ++trigger.updates;
        return trigger.state;
    }
    unsigned char originalType(Object* object) { return reinterpret_cast<TriggerFixture*>(object)->kind; }
    bool originalBlocking(Object* object,unsigned char state)
    {
        auto& trigger=*reinterpret_cast<TriggerFixture*>(object);
        assert(state==trigger.state);
        return trigger.blocking;
    }
}

int main(int argc,char** argv)
{
    luaApi.load(argc>1 ? argv[1] : "C:\\Program Files\\Wireshark\\lua54.dll");
    auto* lua=luaApi.newstate();
    liveLuaState=lua;
    auto begin=[&](int(*function)(void*)) { luaApi.settop(lua,0); luaApi.pushcclosure(lua,function,0); };
    auto pointer=[&](const void* value) { luaApi.pushinteger(lua,reinterpret_cast<long long>(value)); };
    auto finish=[&](int arguments,int results) {
        assert(luaApi.pcall(lua,arguments,-1,0,0,nullptr)==0);
        assert(luaApi.gettop(lua)==results);
    };
    auto* code=static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_EXECUTE_READWRITE));
    assert(code);
    const unsigned char updateCode[]={0xff,0x90,8,0,0,0,0xc3};
    const unsigned char typeCode[]={0xff,0x90,0,0,0,0,0xc3};
    std::memcpy(code,updateCode,sizeof(updateCode));
    std::memcpy(code+32,typeCode,sizeof(typeCode));
    code[16]=code[48]=1;
    RUNTIME_FUNCTION entries[]={{0,sizeof(updateCode),16},{32,32+sizeof(typeCode),48}};
    assert(RtlAddFunctionTable(entries,2,reinterpret_cast<DWORD64>(code)));
    RC::Unreal::UFunction updateFunction,typeFunction,frameFunction;
    nativeExecs[&updateFunction]=reinterpret_cast<NativeExec>(code);
    nativeExecs[&typeFunction]=reinterpret_cast<NativeExec>(code+32);
    Object library,player,foreign;
    long long currentFrame=120;
    processEvents[&library]=[&](auto* function,void* parameters) {
        assert(function==&frameFunction);
        *static_cast<long long*>(parameters)=currentFrame;
    };
    void* table[]={reinterpret_cast<void*>(&originalType),reinterpret_cast<void*>(&originalTrigger),nullptr,
        reinterpret_cast<void*>(&originalBlocking)};
    TriggerFixture first{table,2,0,false,0},second{table,1,1,true,0},unwatched{table,0,2,false,0};
    auto watch=[&](TriggerFixture& trigger) {
        begin(pbz_input_watch_trigger); pointer(&trigger); pointer(&updateFunction); pointer(&typeFunction);
        pointer(&library); pointer(&frameFunction); finish(5,1);
        assert(luaApi.tointeger(lua,1,nullptr)==1);
    };
    auto query=[&](TriggerFixture& trigger,Object& input,long long frame,int count) {
        begin(pbz_input_trigger_state); pointer(&trigger); pointer(&input); luaApi.pushinteger(lua,frame); finish(3,count);
    };
    auto evaluate=[&](TriggerFixture& trigger,Object& input) {
        int value=17;
        auto result=reinterpret_cast<InputTriggerUpdate>(table[1])(reinterpret_cast<Object*>(&trigger),&input,&value,.125f);
        assert(result==trigger.state);
    };
    enabled.store(true);
    watch(first); watch(second);
    query(first,player,currentFrame,0);
    evaluate(first,player); query(first,player,currentFrame,3);
    assert(luaApi.tointeger(lua,1,nullptr)==2 && luaApi.tointeger(lua,2,nullptr)==0 && luaApi.tointeger(lua,3,nullptr)==0);
    evaluate(second,player); query(second,player,currentFrame,3);
    assert(luaApi.tointeger(lua,1,nullptr)==1 && luaApi.tointeger(lua,2,nullptr)==1 && luaApi.tointeger(lua,3,nullptr)==1);
    query(first,foreign,currentFrame,0); query(first,player,currentFrame+1,0);
    evaluate(unwatched,player); query(unwatched,player,currentFrame,0);
    ++currentFrame;
    evaluate(first,foreign); query(first,player,currentFrame,0); query(first,foreign,currentFrame,3);
    watch(first); query(first,foreign,currentFrame,0);
    enabled.store(false); evaluate(first,player); query(first,player,currentFrame,0);
    enabled.store(true); evaluate(first,player); query(first,player,currentFrame,3);
    assert(first.updates==4 && second.updates==1 && unwatched.updates==1);
    begin(pbz_input_reset_triggers); finish(0,1);
    assert(luaApi.tointeger(lua,1,nullptr)==1 && table[1]==reinterpret_cast<void*>(&originalTrigger));
    assert(inputTriggerObservations.empty() && inputTriggerHooks.empty());
    query(first,player,currentFrame,0);
    watch(first); evaluate(first,player); query(first,player,currentFrame,3);
    begin(pbz_input_reset_triggers); finish(0,1);
    RtlDeleteFunctionTable(entries);
    VirtualFree(code,0,MEM_RELEASE);
    luaApi.close(lua); liveLuaState=nullptr;
    std::cout << "InputTriggerNativeTests PASS: native dispatch, actual type/blocking, player/frame isolation, rewatch and reset through Lua exports\n";
    return 0;
}
