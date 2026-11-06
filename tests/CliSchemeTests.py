"""Exercise independent enhancement schemes through the public offline CLI."""
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


def run(*args, operations=None, code=0):
    result = subprocess.run(
        [str(exe), *map(str, args)], capture_output=True, encoding="utf-8",
        input=None if operations is None else json.dumps({"operations": operations}, ensure_ascii=False))
    check(result.returncode == code, f"exit={result.returncode}: {result.stdout} {result.stderr}")
    data = json.loads(result.stdout)
    check(data["ok"] == (code == 0), "inconsistent ok/exit")
    return data


def edit(source, target=None, operations=None, code=0):
    args = ["--edit", source, "--request", "-"]
    if target is not None:
        args.extend(["--out", target])
    return run(*args, operations=operations or [], code=code)


def has(data, key):
    return any(key in item["Keys"] for item in data["configuration"]["NativeBindings"]
               if item["FunctionId"] == "IA_Jump" and item["Device"] == "keyboard")


baseline = output / "native.zip"
edit("new", baseline)
created = output / "created.zip"
data = edit("new", created, [
    {"op": "enhancement", "enabled": True},
    {"op": "scheme-add", "name": "乱莺剑"},
    {"op": "add", "function": "IA_Jump", "binding": {"Keys": ["F9"]}},
    {"op": "add", "function": "ability:Ability.Action.Hun.SpecialDash",
     "binding": {"Keys": ["G"], "Trigger": "press"}},
    {"op": "add", "function": "scheme:next", "binding": {"Keys": ["F6"], "Trigger": "press"}},
])
added = data["selectedScheme"]
native = data["schemes"][0]
check(len(data["schemes"]) == 4 and added.startswith("scheme_"), "scheme-add did not select a new scheme")
check(has(data, "F9") and any(rule["Keys"] == ["G"] for rule in data["configuration"]["Rules"]),
      "new scheme lost native or enhanced edits")
with zipfile.ZipFile(baseline) as before, zipfile.ZipFile(created) as after:
    check(before.read("EnhancedInputUserSettings.sav") == after.read("EnhancedInputUserSettings.sav"),
          "enhanced CLI editing changed native save bytes")

reopened = run("--inspect", created)
check(reopened["configuration"] == data["configuration"], "ZIP reopen lost independent schemes")
other = run("--inspect", created, "--scheme", native)
check(not has(other, "F9") and not any(rule["Keys"] == ["G"] for rule in other["configuration"]["Rules"]),
      "ordinary or enhanced binding leaked to another scheme")
check(any(rule["Keys"] == ["F6"] for rule in other["configuration"]["Rules"]), "management binding was not shared")
check(other["activeScheme"] == data["activeScheme"], "browsing activated a scheme")
edited = output / "edited.zip"
data = edit(created, edited, [
    {"op": "scheme-select", "scheme": native},
    {"op": "add", "function": "IA_Jump", "binding": {"Keys": ["F10"]}},
    {"op": "scheme-select", "scheme": added},
    {"op": "scheme-rename", "scheme": added, "name": "乱莺剑 · 实战"},
])
check(has(data, "F9") and not has(data, "F10"), "batch scheme selection mixed binding contents")
check(has(run("--inspect", edited, "--scheme", native), "F10"), "batch lost previous scheme edits")
check(any(item["Name"] == "乱莺剑 · 实战" for item in data["schemeNames"]), "scheme rename was not persisted")
share = run("--share", edited)
shared = share["preset"]["KeyEnhancement"]
check(not shared["Schemes"] and not shared["SchemeSwitchBindings"]
      and not any(out["Kind"] == "scheme" for rule in shared["Rules"] for out in rule["Outputs"]),
      "single scheme share contains library or management bindings")
first_preset = output / "first-preset.zip"
first = edit("new", first_preset, [
    {"op": "preset", "share": share["share"], "mode": "Replace"},
    {"op": "scheme-add", "name": "预设副本"},
])
check(first["configuration"]["Enabled"] and len(first["schemes"]) == 4 and has(first, "F9"),
      "first enhanced preset did not initialize schemes before the next operation")
check(run("--inspect", first_preset)["selectedScheme"] == first["selectedScheme"], "first preset library did not reopen")
with zipfile.ZipFile(baseline) as before, zipfile.ZipFile(first_preset) as after:
    check(before.read("EnhancedInputUserSettings.sav") == after.read("EnhancedInputUserSettings.sav"),
          "first enhanced preset changed native save bytes")
status = run("--runtime-status", edited)
check(status["activeScheme"] == data["activeScheme"] and "nativeActiveScheme" in status,
      "runtime status confused native and enhanced identities")

disabled = output / "disabled.zip"
edit(edited, disabled, [{"op": "enhancement", "enabled": False}])
for source in ("new", disabled):
    for operation in (
        {"op": "scheme-add", "name": "不可用"},
        {"op": "scheme-select", "scheme": added},
        {"op": "scheme-rename", "scheme": added, "name": "不可用"},
        {"op": "scheme-delete", "scheme": added},
    ):
        failed = output / "rejected.zip"
        result = edit(source, failed, [operation], code=1)
        check(result["stage"] == "plan" and not failed.exists(), "disabled scheme operation exported data")
restored = edit(disabled, operations=[{"op": "enhancement", "enabled": True}])
check(restored["selectedScheme"] == added and has(restored, "F9") and not has(restored, "F10"),
      "mode toggle lost enhanced selection or contents")
removed = output / "removed.zip"
data = edit(edited, removed, [{"op": "scheme-delete", "scheme": added}])
check(len(data["schemes"]) == 3 and added not in data["schemes"], "scheme deletion did not persist")
check(len(run("--inspect", removed)["schemes"]) == 3, "deleted scheme reappeared after reopen")
edit(edited, operations=[{"op": "scheme-delete", "scheme": native}], code=1)
print(f"CliSchemeTests: {checks} checks passed")
