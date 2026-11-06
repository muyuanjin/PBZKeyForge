"""Exercise the public CLI with copies and structured requests; no game installation is modified."""
import json
import pathlib
import subprocess
import sys
import zipfile

exe = pathlib.Path(sys.argv[1]).resolve()
output = pathlib.Path(sys.argv[2]).resolve()
output.mkdir(parents=True, exist_ok=True)
checks = 0

def check(value, message):
    global checks
    assert value, message
    checks += 1

def run(*args, request=None, code=0):
    result = subprocess.run([str(exe), *map(str, args)], input=None if request is None else json.dumps(request, ensure_ascii=False),
                            encoding="utf-8", capture_output=True)
    check(result.returncode == code, f"{args}: exit={result.returncode}: {result.stdout} {result.stderr}")
    data = json.loads(result.stdout)
    check(data["ok"] == (code == 0), f"{args}: inconsistent ok/exit")
    return data

help_info = run("--agent-help")
check("function(now,snapshot,context,state)" in help_info["semantics"]["scripts"], "EvaluateLua help swaps context and persistent state")
check("reset-all()" in help_info["operations"] and "hooks" in help_info["semantics"], "Agent help omits supported editing schema")
catalog = run("--catalog")
check(len(catalog["functions"]) > 330 and len(catalog["devices"]) >= 2, "Catalog omits enhanced functions or controllers")
keys = run("--keys")
check("NumPadOne" in keys["keyboard"] and "Gamepad_LeftX" in keys["axes"], "Keys lack UE identities or axes")
run("--presets")
run("--inspect", "new", "--typo", code=2)
run("--inspect", "new", "--apply", code=2)
run("--inspect", "--json", code=2)
run("--inspect", "new", "--device", code=2)
run("--inspect", "new", "--device", "keyboard", "--device", "Xbox", code=2)
run("--edit", "new", "--out", code=2)
run("--keys", "extra", code=2)
base = run("--inspect", "new", "--query", "IA_Jump")
jump = next(item for item in base["functions"] if item["Id"] == "IA_Jump")
native_id = jump["bindings"][0]["Id"]
request = {"operations": [
    {"op": "add", "function": "IA_Jump", "binding": {"Keys": ["J"], "Trigger": "default"}},
    {"op": "add-functions", "functions": ["IA_Jump", "IA_Guard"], "binding": {"Keys": ["RightControl", "K"], "Trigger": "hold", "Seconds": .4}},
    {"op": "add", "function": "IA_Dodge", "device": "Xbox", "binding": {"Keys": ["Gamepad_LeftShoulder", "Gamepad_FaceButton_Bottom"], "Trigger": "press"}},
    {"op": "hook-set", "hook": {"path": "/Script/Test:Return", "value": {"object": "/Game/Test.Object"}}}
]}
preview = run("--edit", "new", "--request", "-", request=request)
check(preview["dryRun"] and not preview["result"]["exported"] and len(preview["configuration"]["Rules"]) == 2, "Dry run changed output or lost advanced rules")
check(not list(output.glob("*.zip")), "Dry run exported a bundle")
bundle = output / "bindings.zip"
result = run("--edit", "new", "--request", "-", "--out", bundle, request=request)
check(result["result"]["exported"] and bundle.exists(), "Bundle export failed")
restored = run("--inspect", bundle)
check(restored["configuration"] == result["configuration"], "Bundle changed enhancement config")
# IDs are generated once per request; compare fixed input content across independently edited drafts.
check(any(entry["Keys"] == ["J"] for fn in restored["functions"] if fn["Id"] == "IA_Jump" for entry in fn["bindings"]), "Bundle lost native save edit")
share = run("--share", bundle, "--name", "AI 配置", "--scope", "keyboard")
check(share["share"].startswith("PBZ2:") and all(not key.startswith("Gamepad_") for rule in share["preset"]["KeyEnhancement"]["Rules"] for key in rule["Keys"]), "Keyboard share contains controller inputs")
shared = run("--edit", "new", "--request", "-", request={"operations": [{"op": "preset", "share": share["share"], "mode": "Append"}]})
check(len(shared["configuration"]["Rules"]) == 1, "Share import lost grouped binding")
scoped_config = json.loads(json.dumps(restored["configuration"]))
scoped_config["NativeBindings"] = [
    {"FunctionId": "IA_Jump", "Device": "keyboard", "Keys": ["U"]},
    {"FunctionId": "IA_Jump", "Device": "gamepad", "Keys": ["Gamepad_FaceButton_Top"]}]
scoped_config["Suppressions"] = [
    {"ActionPath": "/Game/Test", "Key": "U", "FunctionId": None},
    {"ActionPath": "/Game/Test", "Key": "Gamepad_FaceButton_Top", "FunctionId": None}]
scoped_source = output / "scoped-source.zip"
run("--edit", bundle, "--request", "-", "--out", scoped_source,
    request={"operations": [{"op": "configuration", "value": scoped_config}]})
for scope, other_device, other_keys in [
        ("keyboard", "gamepad", ["Gamepad_LeftShoulder", "Gamepad_FaceButton_Bottom"]),
        ("Xbox", "keyboard", ["RightControl", "K"])]:
    scoped_share = run("--share", scoped_source, "--scope", scope)
    for mode in ("Replace", "OnlyPreset"):
        destination = output / f"scoped-{scope}-{mode}.zip"
        replaced = run("--edit", scoped_source, "--request", "-", "--out", destination, request={"operations": [
            {"op": "preset", "share": scoped_share["share"], "mode": mode}]})
        retained = [rule for rule in restored["configuration"]["Rules"] if rule["Keys"] == other_keys]
        check(retained and all(rule in replaced["configuration"]["Rules"] for rule in retained),
              f"{mode} of {scope} preset lost rules from {other_device}")
        check(all(item in replaced["configuration"]["NativeBindings"] for item in scoped_config["NativeBindings"]), "Scoped preset lost explicit native overrides")
        check(all(item in replaced["configuration"]["Suppressions"] for item in scoped_config["Suppressions"]), "Scoped preset lost keyed suppressions")
        check(run("--inspect", destination)["configuration"] == replaced["configuration"], "Scoped preset ZIP lost retained configuration")
ps = run("--inspect", "new", "--device", "PS", "--query", "IA_Jump")
ps_jump = next(entry for fn in ps["functions"] if fn["Id"] == "IA_Jump" for entry in fn["bindings"] if entry["IsNative"])
ps_source = output / "ps-source.zip"
run("--edit", "new", "--device", "PS", "--request", "-", "--out", ps_source, request={"operations": [
    {"op": "replace", "id": ps_jump["Id"], "binding": {"Keys": ["Gamepad_FaceButton_Top"], "Trigger": "default"}}]})
incoming = json.loads(json.dumps(restored["configuration"]))
incoming["Rules"], incoming["Hooks"] = [], []
incoming["NativeBindings"] = [{"FunctionId": "IA_Jump", "Device": "gamepad", "Keys": ["Gamepad_FaceButton_Left"]}]
incoming_source = output / "incoming-pad.zip"
run("--edit", "new", "--request", "-", "--out", incoming_source,
    request={"operations": [{"op": "configuration", "value": incoming}]})
incoming_share = run("--share", incoming_source)
appended = run("--edit", ps_source, "--device", "PS", "--request", "-", request={"operations": [
    {"op": "preset", "share": incoming_share["share"], "mode": "Append"}]})
check(next(item["Keys"] for item in appended["configuration"]["NativeBindings"] if item["Device"] == "gamepad") ==
      ["Gamepad_FaceButton_Top", "Gamepad_FaceButton_Left"], "Preset plan ignored the selected controller family")
fail_path = output / "must-not-exist.zip"
run("--edit", bundle, "--request", "-", "--out", fail_path, request={"operations": request["operations"] + [{"op": "missing-operation"}]}, code=2)
check(not fail_path.exists(), "Invalid batch partially exported")
run("--edit", "new", "--request", "-", "--out", output / "bad.json", request=request, code=1)
check(not (output / "bad.json").exists(), "JSON silently discarded native drafts")
run("--edit", "new", "--request", "-", "--apply", "--out", fail_path, request=request, code=2)
run("--edit", "samples/vanilla-base.sav", "--request", "-", request={"operations": []}, code=2)
run("--edit", "samples/vanilla-base.sav", "--scheme", "InputUserSettings.Profiles.Default", "--request", "-", request={"operations": []})
copy_id = next(entry["Id"] for fn in restored["functions"] if fn["Id"] == "IA_Jump" for entry in fn["bindings"] if not entry["IsNative"])
changed = run("--edit", bundle, "--request", "-", request={"operations": [
    {"op": "copy", "id": copy_id, "function": "IA_Dodge"},
    {"op": "disable", "id": copy_id},
    {"op": "move-key", "from": "W", "to": "T"}
]})
check(any(rule["Keys"] == ["T"] for rule in changed["configuration"]["Rules"]), "CLI whole-key move omits fixed movement")
binding_reset = run("--edit", bundle, "--request", "-", request={"operations": [{"op": "restore-bindings"}]})
check("restore-bindings()" in help_info["operations"], "CLI help omits binding-only restore")
check(binding_reset["configuration"]["Enabled"] == restored["configuration"]["Enabled"]
      and not binding_reset["configuration"]["Rules"] and not binding_reset["configuration"]["Suppressions"], "Binding-only reset loses enable intent or retains binding rules")
check(binding_reset["configuration"]["CustomFunctions"] == restored["configuration"]["CustomFunctions"]
      and binding_reset["configuration"]["Hooks"] == restored["configuration"]["Hooks"], "Binding-only restore removes custom definitions")
check(next(item for item in binding_reset["configuration"]["NativeBindings"] if item["FunctionId"] == "IA_Jump" and item["Device"] == "keyboard")["Keys"] == ["SpaceBar"], "Binding-only restore omits factory inputs")
reset = run("--edit", bundle, "--request", "-", request={"operations": [{"op": "reset-all"}]})
check(not reset["configuration"]["Enabled"] and not reset["configuration"]["Rules"] and not reset["configuration"]["Hooks"], "CLI reset omitted enhancement settings")

# Exporting a document must not replace a runtime config independently of its Lua file.
game = output / "export-game"
runtime = game / "platform" / "steam"
runtime.mkdir(parents=True, exist_ok=True)
(runtime / "PBZ.exe").write_text("fixture, never launched", encoding="utf-8")
for mod in (runtime / "ue4ss" / "Mods" / "PBZInputRouter", runtime / "CustomMods" / "PBZInputRouter"):
    (mod / "Scripts").mkdir(parents=True, exist_ok=True)
    target = mod / "configuration.json"
    lua = mod / "Scripts" / "router-config.lua"
    original_config = json.dumps(restored["configuration"]).encode("utf-8")
    target.write_bytes(original_config)
    lua.write_bytes(b"return { enabled = false }")
    before = {path: path.read_bytes() for path in mod.rglob("*") if path.is_file()}
    for source in ("new", bundle, target):
        rejected = run("--edit", source, "--request", "-", "--out", target.parent / ".." / "PBZInputRouter" / "CONFIGURATION.JSON",
                       request={"operations": []}, code=1)
        check("--apply" in rejected["error"] and "--live" in rejected["error"], "Runtime export rejection omits the supported apply paths")
        check(before == {path: path.read_bytes() for path in mod.rglob("*") if path.is_file()}, "Rejected export changed runtime files")
    target.unlink()
    run("--edit", "new", "--request", "-", "--out", target, request={"operations": []}, code=1)
    check(not target.exists() and lua.read_bytes() == before[lua], "Export created a missing runtime config")
copy_path = output / "configuration.json"
copied = run("--edit", "new", "--request", "-", "--out", copy_path, request={"operations": []})
check(copied["result"]["exported"] and copy_path.exists(), "Ordinary JSON export was rejected")

# Global reset owns every scheme in both the SAV and ZIP output formats.
default_scheme = "InputUserSettings.Profiles.Default"
custom_schemes = ["PBZ.KeyProfile.Custom1", "PBZ.KeyProfile.Custom2"]
source = "new"
for index, scheme in enumerate(custom_schemes):
    destination = output / f"custom-{index}.sav"
    pad = run("--inspect", source, "--scheme", scheme, "--device", "Xbox", "--query", "IA_Jump")
    pad_id = next(entry["Id"] for fn in pad["functions"] if fn["Id"] == "IA_Jump" for entry in fn["bindings"] if entry["IsNative"])
    run("--edit", source, "--scheme", scheme, "--request", "-", "--out", destination, request={"operations": [
        {"op": "add", "function": "IA_Jump", "binding": {"Keys": ["J" if index == 0 else "K"], "Trigger": "default"}},
        {"op": "replace", "id": pad_id, "device": "Xbox", "binding": {"Keys": ["Gamepad_FaceButton_Top"], "Trigger": "default"}}]})
    source = destination
source_bytes = source.read_bytes()
for extension in ("sav", "zip"):
    destination = output / f"global-reset.{extension}"
    run("--edit", source, "--scheme", default_scheme, "--request", "-", "--out", destination,
        request={"operations": [{"op": "reset-all"}]})
    for scheme in [default_scheme, *custom_schemes]:
        for device, expected in (("keyboard", ["SpaceBar"]), ("Xbox", ["Gamepad_FaceButton_Bottom"])):
            inspected = run("--inspect", destination, "--scheme", scheme, "--device", device, "--query", "IA_Jump")
            actual = [key for fn in inspected["functions"] if fn["Id"] == "IA_Jump" for entry in fn["bindings"] for key in entry["Keys"]]
            check(sorted(set(actual)) == expected, f"{extension} global reset missed {scheme}/{device}: {actual}")
check(source.read_bytes() == source_bytes, "Global reset export modified its source save")
print(f"CliAgentTests PASS {checks}")
