"""Execute the Lua routing tests with an installed Lua 5.4 runtime DLL."""
import argparse
import ctypes
import os
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("script", nargs="?", default="tests/input-router/RouterTests.lua")
    parser.add_argument("--lua-dll", default=os.environ.get("PBZ_LUA_DLL"))
    args = parser.parse_args()
    candidates = [args.lua_dll, r"C:\Program Files\Wireshark\lua54.dll"]
    runtime = next((Path(p) for p in candidates if p and Path(p).is_file()), None)
    if runtime is None:
        parser.error("Pass --lua-dll with an installed Lua 5.4 DLL")
    lua = ctypes.CDLL(str(runtime))
    lua.luaL_newstate.restype = ctypes.c_void_p
    lua.luaL_openlibs.argtypes = [ctypes.c_void_p]
    lua.luaL_loadfilex.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_char_p]
    lua.lua_pcallk.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int,
                              ctypes.c_int, ctypes.c_ssize_t, ctypes.c_void_p]
    lua.lua_tolstring.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.POINTER(ctypes.c_size_t)]
    lua.lua_tolstring.restype = ctypes.c_char_p
    lua.lua_close.argtypes = [ctypes.c_void_p]
    state = lua.luaL_newstate()
    if not state:
        raise MemoryError("luaL_newstate failed")
    try:
        lua.luaL_openlibs(state)
        result = lua.luaL_loadfilex(state, str(Path(args.script)).encode("utf-8"), None)
        if result == 0:
            result = lua.lua_pcallk(state, 0, -1, 0, 0, None)
        if result:
            raise RuntimeError(lua.lua_tolstring(state, -1, None).decode("utf-8"))
    finally:
        lua.lua_close(state)


if __name__ == "__main__":
    main()
