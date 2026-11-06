"""CLI component lifecycle and live protocol, using isolated installations only.

Usage: python tests/CliLifecycleTests.py <cli.exe> <new-directory> [real-game-root]
The optional native EXE is hard-linked on its volume; only copied saves are edited.
A temporary PBZ process and simulated runtime acknowledgement test the CLI protocol,
not actual game input behavior. No game is launched or stopped.
"""
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import threading
import time

exe = Path(sys.argv[1]).resolve()
root = Path(sys.argv[2]).resolve()
root.mkdir(parents=True, exist_ok=False)
checks = 0


def check(value, message):
    global checks
    assert value, message
    checks += 1


def run(*args, request=None, code=0, timeout=15):
    result = subprocess.run([str(exe), *map(str, args)],
                            input=None if request is None else json.dumps(request, ensure_ascii=False),
                            encoding="utf-8", capture_output=True, timeout=timeout)
    check(result.returncode == code, f"{args}: exit={result.returncode}: {result.stdout} {result.stderr}")
    data = json.loads(result.stdout)
    check(data["ok"] == (code == 0), f"{args}: inconsistent ok/exit")
    return data


check("commands" in run(), "No-argument CLI did not print help and exit")
help_info = run("--help")
source = run("--version")["source"]
if source["recorded"]:
    check(source["revision"] in source["archive"], "Missing build source provenance")
    check(source["exact"] == (not source["modified"]), "Modified source advertised as exact")
else:
    check(not source["revision"] and not source["sourceRef"], "Source archive fabricated a revision")
    check(not source["exact"] and source["archive"] is None and source["checkout"] is None,
          "Unrecorded source advertised an exact source download")
check(source == help_info["source"], "Version/help disagree on source")
check(any("--gameplay-mod" in item for item in help_info["commands"]), "Gameplay Mod not discoverable")
check("default" in help_info["schema"]["triggers"], "Default trigger missing from schema")
check(any(field["type"] == "List<String>" for field in help_info["schema"]["rule"]), "Generic schema types are unreadable")
run("--unknown", code=2)
run("--selftest", code=2)
run("--list", "samples/vanilla-base.sav", "--typo", code=2)
run("--version", "extra", code=2)
run("--gameplay-mod", "new", "--action", "status", "--live", code=2)
run("--component", "new", "--action", "status", "--archive", "bad.zip", code=2)
run("--edit", "new", "--request", "-", "--dry-run", "--apply", request={"operations": []}, code=2)
bad = run("--edit", "new", "--request", "-", request={"operations": [
    {"op": "add", "function": "IA_Jump", "binding": {"Keys": ["J"], "Trigger": "hold", "Second": .8}}]}, code=2)
check(bad["operationIndex"] == 0 and "binding.Second" in bad["error"], "Invalid binding field silently defaulted")
run("--edit", "new", "--request", "-", request={"operations": [{"op": "enhancement", "enabled": True, "enable": False}]}, code=2)
run("--edit", "new", "--request", "-", request={"operations": [None]}, code=2)
run("--edit", "new", "--request", "-", request={"operations": None}, code=2)
unicode_request = {"operations": [{"op": "custom-add", "name": "测试引导", "target": {"Kind": "lua", "Code": "return true"}}]}
unicode_result = run("--edit", "new", "--request", "-", request=unicode_request)
check(unicode_result["configuration"]["CustomFunctions"][0]["Name"] == "测试引导", "UTF-8 stdin was corrupted")
bom = root / "bom.json"
bom.write_text(json.dumps(unicode_request, ensure_ascii=False), encoding="utf-8-sig")
check(run("--edit", "new", "--request", bom)["configuration"]["CustomFunctions"][0]["Name"] == "测试引导", "PowerShell UTF-8 BOM request was corrupted")

game = root / "游戏 with spaces"
runtime = game / "platform" / "steam"
runtime.mkdir(parents=True)
(runtime / "PBZ.exe").write_text("fixture, never launched")
paks = game / "ProjectPBZ" / "Content" / "Paks"
paks.mkdir(parents=True)
chunks = json.loads(Path("assets/perfect-deflect-no-rapid-click-penalty/layout.json").read_text(encoding="utf-8-sig"))["RequiredChunks"]
toc = b"-==--==--==--==-" + bytes([9, 0, 0, 0]) + struct.pack("<II", 176, len(chunks)) + bytes(148)
(paks / "Fixture.utoc").write_bytes(toc + b"".join(bytes.fromhex(chunk) for chunk in chunks))
foreign = paks / "~mods" / "OtherMod" / "untouched.pak"
foreign.parent.mkdir(parents=True)
foreign.write_bytes(b"other mod")

status = run("--gameplay-mod", game)
check(status["state"] == "Disabled" and status["supported"], "Gameplay default/support status wrong")
enabled = run("--gameplay-mod", game, "--action", "enable")
mod_dir = Path(enabled["directory"])
check(enabled["state"] == "Enabled" and len(list(mod_dir.iterdir())) == 3, "Gameplay enable missing containers")
before = {path: (path.read_bytes(), path.stat().st_mtime_ns) for path in mod_dir.iterdir()}
run("--gameplay-mod", game, "--action", "enable")
check(before == {path: (path.read_bytes(), path.stat().st_mtime_ns) for path in mod_dir.iterdir()}, "Repeated enable rewrote containers")
conflict = mod_dir / "foreign.txt"
conflict.write_bytes(b"keep")
run("--gameplay-mod", game, "--action", "disable", code=1)
check(conflict.read_bytes() == b"keep" and all(path.read_bytes() == content for path, (content, _) in before.items()), "Conflict refusal damaged Mod")
conflict.unlink()
run("--gameplay-mod", game, "--action", "disable")
check(not mod_dir.exists() and foreign.read_bytes() == b"other mod", "Disable lost other Mods or retained own files")
run("--gameplay-mod", game, "--action", "uninstall")

enabled = run("--component", game, "--action", "enable")
check(enabled["state"] == "Enabled" and enabled["loaderState"] == "Compatible", "Key enhancement enable failed")
configuration = Path(enabled["configuration"])
mod = Path(enabled["directory"])
original_config = configuration.read_bytes()
disabled = run("--component", game, "--action", "disable")
check(disabled["state"] == "Disabled" and configuration.exists(), "Disable did not preserve enhancement configuration")
run("--component", game, "--action", "enable")
check(configuration.read_bytes() == original_config, "Disable/enable lost enhancement settings")
update = run("--component", game, "--action", "update", "--archive", exe.parent / "key-enhancement-runtime.zip")
check(update["latestRuntime"]["Provider"] == "Manual" and update["state"] == "Enabled", "Manual runtime package not accepted or reported")
check(configuration.read_bytes() == original_config, "Runtime update changed settings")

save = game / "ProjectPBZ" / "Saved" / "EnhancedInputUserSettings.sav"
save.parent.mkdir(parents=True)
shutil.copyfile("samples/vanilla-base.sav", save)
save_before = save.read_bytes()
scheme = "InputUserSettings.Profiles.Default"
ordinary = {"operations": [{"op": "enhancement", "enabled": False},
                            {"op": "add", "function": "IA_Jump", "binding": {"Keys": ["J"]}}]}
applied = run("--edit", save, "--scheme", scheme, "--request", "-", "--apply", "--query", "IA_Jump", request=ordinary)
check(applied["result"]["bindingsSaved"] and applied["result"]["enhancementSaved"], "Ordinary Apply omitted a component")
check(Path(applied["result"]["inputBackup"]).read_bytes() == save_before, "Save backup missing or wrong")
check(save.read_bytes() != save_before, "Ordinary Apply did not edit native save")
run("--component", save, "--action", "enable")
readiness = run("--runtime-status", save)["readiness"]
check(not readiness["canApply"] and readiness["enhancementEnabled"], "Unauthenticated scheme advertised as ready to apply")
preview = run("--edit", save, "--scheme", scheme, "--request", "-", "--query", "IA_Jump",
              request={"operations": [{"op": "enhancement", "enabled": False}]})
check(preview["readiness"]["canApply"] and preview["readiness"]["enhancementEnabled"] and not preview["configuration"]["Enabled"],
      "Draft enable state replaced installed readiness or explicit scheme was ignored")

# A real native executable and authenticated settings are needed for live scheme
# selection. They are read-only sources; all subsequent writes target copies.
live_save = save
original_sources = {}
if len(sys.argv) > 3:
    real = Path(sys.argv[3]).resolve()
    original_sources = {real / "ProjectPBZ" / "Saved" / name: (real / "ProjectPBZ" / "Saved" / name).read_bytes()
                        for name in ("EnhancedInputUserSettings.sav", "PBZSharedSetting.sav")}
    live_game = root / "authenticated-game"
    live_runtime = live_game / "platform" / "steam"
    live_runtime.mkdir(parents=True)
    os.link(real / "platform" / "steam" / "PBZ.exe", live_runtime / "PBZ.exe")
    live_save = live_game / "ProjectPBZ" / "Saved" / "EnhancedInputUserSettings.sav"
    live_save.parent.mkdir(parents=True)
    for path, content in original_sources.items():
        (live_save.parent / path.name).write_bytes(content)
    live_installation = run("--component", live_game, "--action", "enable")
    mod = Path(live_installation["directory"])
    configuration = Path(live_installation["configuration"])
    active = run("--inspect", live_save, "--query", "IA_Jump")["activeScheme"]
    check(active is not None, "Copied settings were not authenticated")
    target = "PBZ.KeyProfile.Custom1" if active != "PBZ.KeyProfile.Custom1" else "PBZ.KeyProfile.Custom2"
    activation = run("--edit", live_save, "--scheme", target, "--request", "-", "--apply", "--query", "IA_Jump", request={"operations": [{"op": "activate"}]})
    check(activation["result"]["activationSaved"] and Path(activation["result"]["activationBackup"]).exists() and activation["activeScheme"] == target, "CLI scheme activation failed")

holder_source = root / "Holder.cs"
holder_source.write_text('class Holder { static void Main() { System.Console.WriteLine("ready"); System.Threading.Thread.Sleep(120000); } }')
holder_dir = root / "process"
holder_dir.mkdir()
holder_exe = holder_dir / "PBZ.exe"
compiler = Path(os.environ["WINDIR"]) / "Microsoft.NET" / "Framework64" / "v4.0.30319" / "csc.exe"
subprocess.run([str(compiler), "/nologo", f"/out:{holder_exe}", str(holder_source)], check=True)
holder = subprocess.Popen([str(holder_exe)], stdout=subprocess.PIPE, text=True)
stop = threading.Event()
acknowledge = threading.Event()
reject = threading.Event()
errors = []


def runtime_loop():
    handled = None
    try:
        while not stop.wait(.025):
            heartbeat = mod / "live-status.txt"
            pending = heartbeat.with_suffix(".pending")
            pending.write_text("PBZ-LIVE-1\nfixture\nready\n", encoding="utf-8")
            os.replace(pending, heartbeat)
            request_id = mod / "live-request.id"
            if acknowledge.is_set() and request_id.exists():
                current = request_id.read_text(encoding="utf-8-sig")
                if current != handled and not current.startswith("cancel-"):
                    response = "error\nfixture rejected" if reject.is_set() else "ok\n"
                    pending = mod / "live-response.pending"
                    pending.write_text(current + "\n" + response, encoding="utf-8")
                    os.replace(pending, mod / "live-response.txt")
                    handled = current
    except BaseException as error:
        errors.append(error)


thread = None
try:
    check(holder.stdout.readline().strip() == "ready", "PBZ guard fixture failed to start")
    running_save = live_save.read_bytes()
    run("--component", game, "--action", "disable", code=1)
    run("--gameplay-mod", game, "--action", "enable", code=1)
    run("--edit", save, "--scheme", scheme, "--request", "-", "--apply", request=ordinary, code=1)
    check(live_save.read_bytes() == running_save, "Running-game refusal changed a save")
    for operation in ({"op": "reset-all"}, {"op": "activate"}, {"op": "enhancement", "enabled": False}):
        run("--edit", save, "--scheme", scheme, "--request", "-", "--live", request={"operations": [operation]}, code=2)
    if len(sys.argv) > 3:
        status = run("--runtime-status", live_save)
        check(not status["readiness"]["canLiveApply"] and status["readiness"]["liveBlockedBy"] == "waiting_for_runtime", "Disconnected live state was reported ready")
        thread = threading.Thread(target=runtime_loop)
        thread.start()
        inspected = run("--inspect", live_save, "--query", "IA_Jump")
        jump = next(item for item in inspected["functions"] if item["Id"] == "IA_Jump")
        native_id = next(item["Id"] for item in jump["bindings"] if item["IsNative"])
        request = {"operations": [{"op": "replace", "id": native_id, "binding": {"Keys": ["F10"]}}]}
        config_before = configuration.read_bytes()
        shared = live_save.parent / "PBZSharedSetting.sav"
        shared_before = shared.read_bytes()
        preview = run("--edit", live_save, "--request", "-", "--live", "--dry-run", "--query", "IA_Jump", request=request)
        check(preview["dryRun"] and preview["readiness"]["canLiveApply"] and not preview["result"]["liveApplied"], "Live preview applied or readiness false")
        check(not (mod / "live-request.id").exists() and configuration.read_bytes() == config_before, "Live preview wrote runtime files")
        acknowledge.set()
        applied = run("--edit", live_save, "--request", "-", "--live", "--query", "IA_Jump", request=request)
        check(applied["result"]["liveApplied"] and applied["result"]["liveState"] == "confirmed" and applied["result"]["enhancementSaved"], "Live acknowledgement not confirmed/persisted")
        check(live_save.read_bytes() == running_save and shared.read_bytes() == shared_before, "Live apply wrote game saves")
        check(json.loads(configuration.read_text(encoding="utf-8-sig"))["NativeBindings"][0]["Keys"] == ["F10"], "Live native override not saved")
        updated = run("--inspect", live_save, "--query", "IA_Jump")
        runtime_id = next(item["Id"] for fn in updated["functions"] if fn["Id"] == "IA_Jump" for item in fn["bindings"] if item["IsRuntimeBinding"])
        request["operations"][0].update(id=runtime_id, binding={"Keys": ["F11"]})
        config_before = configuration.read_bytes()
        reject.set()
        failed = run("--edit", live_save, "--request", "-", "--live", request=request, code=1)
        check(failed["result"]["liveState"] == "unconfirmed" and not failed["result"]["enhancementSaved"] and configuration.read_bytes() == config_before, "Rejected live apply changed configuration")
        reject.clear()
        acknowledge.clear()
        failed = run("--edit", live_save, "--request", "-", "--live", request=request, code=1)
        check(failed["stage"] == "live_apply" and failed["result"]["liveState"] == "unconfirmed", "Timeout missing structured state")
        check((mod / "live-request.id").read_text().startswith("cancel-") and configuration.read_bytes() == config_before, "Timeout did not cancel pending request")
        check(all(path.read_bytes() == content for path, content in original_sources.items()), "Real player save changed")
finally:
    stop.set()
    if thread is not None:
        thread.join(timeout=3)
    if holder.poll() is None:
        holder.terminate()
    holder.wait(timeout=5)
check(not errors, f"Runtime fixture errors: {errors}")

clean = run("--component", game, "--action", "uninstall")
check(clean["uninstall"]["Complete"] and clean["uninstall"]["RemovedFiles"], "Uninstall details missing")
check(foreign.read_bytes() == b"other mod", "Enhancement uninstall changed unrelated Mod")
run("--component", game, "--action", "enable")
script = runtime / "ue4ss" / "Mods" / "PBZInputRouter" / "Scripts" / "main.lua"
script.write_bytes(script.read_bytes() + b"\n-- local modification\n")
partial = run("--component", game, "--action", "uninstall", code=1)
check(str(script) in partial["uninstall"]["RetainedFiles"] and script.exists(), "Partial uninstall claimed success or removed modified script")

shared_game = root / "shared-loader"
shared_runtime = shared_game / "platform" / "steam"
shared_runtime.mkdir(parents=True)
(shared_runtime / "PBZ.exe").write_text("fixture, never launched")
run("--component", shared_game, "--action", "enable")
other_mod = shared_runtime / "ue4ss" / "Mods" / "OtherMod" / "Scripts" / "main.lua"
other_mod.parent.mkdir(parents=True)
other_mod.write_text("-- another Mod")
(other_mod.parent.parent / "enabled.txt").write_bytes(b"")
shared_uninstall = run("--component", shared_game, "--action", "uninstall", code=1)
check(shared_uninstall["uninstall"]["SharedLoaderRetained"] and (shared_runtime / "dwmapi.dll").exists()
      and other_mod.read_text() == "-- another Mod", "Shared UE4SS loader or other Mod was removed")
print(f"CliLifecycleTests PASS {checks}; authenticated_live_protocol={'yes' if len(sys.argv) > 3 else 'not requested'}; gameplay=not launched")
