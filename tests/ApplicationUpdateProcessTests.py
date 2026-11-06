"""Exercise the application's update helper using temporary executable stand-ins."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

app = Path(sys.argv[1]).resolve()
root = Path(sys.argv[2]).resolve()
root.mkdir(parents=True, exist_ok=False)
target = root / "installed"
stage = root / "staging"
payload = stage / "payload"
target.mkdir()
payload.mkdir(parents=True)
cli = app.with_name("PBZKeyForge-cli.exe")
version = json.loads(subprocess.check_output([str(cli), "--agent-help"], encoding="utf-8"))["version"]
ready, stop, restarted = (root / name for name in ("parent-ready", "parent-stop", "restarted"))
source = root / "Program.cs"
source.write_text(
    'using System; using System.IO; using System.Threading; using System.Reflection;\n'
    f'[assembly:AssemblyVersion("{version}.0")]\n'
    'class Program { static void Main(string[] args) {\n'
    'if(args.Length>0 && args[0]=="parent") { '
    f'File.WriteAllText({json.dumps(str(ready))}, "ready"); '
    f'while(!File.Exists({json.dumps(str(stop))})) Thread.Sleep(30); '
    f'}} else File.WriteAllText({json.dumps(str(restarted))}, "restarted"); '
    '} }', encoding="utf-8-sig"
)
compiler = Path(os.environ["WINDIR"]) / "Microsoft.NET/Framework64/v4.0.30319/csc.exe"
names = ["PBZKeyForge.exe", "PBZKeyForge-cli.exe"]
for name in names:
    subprocess.run([str(compiler), "/nologo", "/target:winexe", f"/out:{payload / name}", str(source)], check=True)
    (target / name).write_text("previous program")
(target / "personal-preset.json").write_text("keep my preset")
helper = stage / "update-helper.exe"
shutil.copyfile(app, helper)
parent = subprocess.Popen([str(payload / names[0]), "parent"])
updater = None
try:
    deadline = time.monotonic() + 5
    while not ready.exists() and time.monotonic() < deadline:
        time.sleep(.03)
    assert ready.exists(), "Parent stand-in did not start"
    manifest = stage / "update.json"
    manifest.write_text(json.dumps(dict(Directory=str(target), Staging=str(stage), Version=version, Parent=parent.pid, Files=names)), encoding="utf-8")
    updater = subprocess.Popen([str(helper), "--finish-update", str(manifest)])
    time.sleep(1)
    assert parent.poll() is None and updater.poll() is None, "Helper did not wait for the parent"
    assert (target / names[0]).read_text() == "previous program", "Helper replaced a running parent's installation"
    stop.write_text("stop")
    parent.wait(timeout=5)
    assert updater.wait(timeout=15) == 0, "Helper failed to update"
    deadline = time.monotonic() + 5
    while not restarted.exists() and time.monotonic() < deadline:
        time.sleep(.03)
    assert restarted.exists(), "Updated application did not restart"
    assert (stage / "result.txt").read_text() == "ok", "Missing update confirmation"
    for name in names:
        assert (target / name).read_bytes() == (payload / name).read_bytes(), name
    assert (target / "personal-preset.json").read_text() == "keep my preset", "User preset changed"
    print("ApplicationUpdateProcessTests PASS: parent wait, both executable replacements, restart, preset preservation")
finally:
    stop.write_text("stop")
    if parent.poll() is None:
        parent.terminate()
        parent.wait(timeout=5)
    if updater is not None and updater.poll() is None:
        updater.terminate()
        updater.wait(timeout=5)
