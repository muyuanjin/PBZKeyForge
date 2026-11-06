#define WIN32_LEAN_AND_MEAN
#include <windows.h>

// Called as is_foreground(true). Lua keeps its argument on the C function stack.
// Returning one yields that boolean; returning zero yields no results.
extern "C" __declspec(dllexport) int pbz_input_is_foreground(void*)
{
    DWORD processId = 0;
    GetWindowThreadProcessId(GetForegroundWindow(), &processId);
    return processId == GetCurrentProcessId() ? 1 : 0;
}

// Lua calls each probe with true on the stack. One result returns that value;
// zero results represents a released key. No Lua runtime linkage is required.
#define PBZ_KEY(value) extern "C" __declspec(dllexport) int pbz_input_key_##value(void*) \
{ return (GetAsyncKeyState(value) & 0x8000) != 0 ? 1 : 0; }
#include "physical-keys.inc"
