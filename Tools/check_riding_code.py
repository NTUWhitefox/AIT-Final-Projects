"""Compile against this project's existing Unity compiler responses, without UI.

Writes only Personal/CodeChecks. Does not launch, focus or send input to Unity.
Requires an already imported Unity project (Library/Bee compiler responses).
"""
import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "VR-Motor-Street-Riding"
OUT = PROJECT / "Personal/CodeChecks"
OUT.mkdir(parents=True, exist_ok=True)
RSP = next((PROJECT / "Library/Bee/artifacts").glob("*/Assembly-CSharp.rsp"))
original = RSP.read_text(encoding="utf-8-sig")
engine_reference = next(line for line in original.splitlines() if "Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll" in line)
editor = Path(re.search(r'"(.+?/Editor)/Data/', engine_reference.replace("\\", "/"))[1])
dotnet = editor / "Data/NetCoreRuntime/dotnet.exe"
csc = editor / "Data/DotNetSdkRoslyn/csc.dll"


def run_rsp(path):
    result = subprocess.run([str(dotnet), "exec", str(csc), "@" + str(path)], cwd=PROJECT,
                            capture_output=True, text=True, encoding="utf-8", errors="replace")
    log = result.stdout + result.stderr
    path.with_suffix(".log").write_text(log, encoding="utf-8")
    print(log.strip())
    if result.returncode:
        raise SystemExit(result.returncode)


for assembly, is_editor in [("Assembly-CSharp", False), ("Assembly-CSharp-Editor", True)]:
    source = RSP.with_name(assembly + ".rsp").read_text(encoding="utf-8-sig")
    source = "\n".join(line for line in source.splitlines()
                       if not (line.startswith('"') and line.endswith('.cs"')
                               and not (PROJECT / line.strip('"')).exists()))
    source = re.sub(r'(?m)^-out:.*$', f'-out:"{(OUT / (assembly + ".dll")).as_posix()}"', source)
    source = re.sub(r'(?m)^-refout:.*$', f'-refout:"{(OUT / (assembly + ".ref.dll")).as_posix()}"', source)
    if is_editor:
        source = re.sub(r'(?m)^-r:"[^"]*/Assembly-CSharp.ref.dll"$',
                        f'-r:"{(OUT / "Assembly-CSharp.ref.dll").as_posix()}"', source)
    for file in (PROJECT / "Assets/MotorSimulatorAssets").rglob("*.cs"):
        if ("Editor" in file.relative_to(PROJECT).parts) != is_editor:
            continue
        relative = file.relative_to(PROJECT).as_posix()
        if f'"{relative}"' not in source.replace("\\", "/"):
            source += f'\n"{relative}"\n'
    output = OUT / (assembly + ".rsp")
    output.write_text(source, encoding="utf-8")
    run_rsp(output)
    print("PASS compiled", assembly)

# The classification regression is pure managed C#: run the actual implementation,
# including the real OBJ group names, without Unity/native engine initialization.
test = OUT / "PartChecks.cs"
test.write_text('''using System;
using System.IO;
using AIT.Riding;
static class PartChecks {
 static void Check(string name, RidePartGroup expected) {
   if (RideVehicleParts.Classify(name)!=expected) throw new Exception(name+" misclassified");
 }
 static void Main(string[] args) {
   Check("Scooter_shaped_leg_shield_with_front_wheel_clearance",RidePartGroup.Body);
   Check("Scooter_lofted_rear_body_with_real_wheel_opening",RidePartGroup.Body);
   Check("Scooter_front_curved_mudguard",RidePartGroup.Steering);
   Check("Scooter_front_lower_fork_casting_1",RidePartGroup.Steering);
   Check("Scooter_front_wheel_rounded_tyre_carcass",RidePartGroup.FrontWheel);
   Check("Scooter_rear_wheel_rounded_tyre_carcass",RidePartGroup.RearWheel);
   Check("Scooter_front_wheel_seated_through_axle",RidePartGroup.Steering);
   Check("Scooter_rear_wheel_seated_through_axle",RidePartGroup.Body);
   foreach(string line in File.ReadLines(args[0])) if(line.StartsWith("g ")) {
     string name=line.Substring(2); Console.WriteLine(RideVehicleParts.Classify(name)+"\\t"+name);
   }
 }
}''', encoding="utf-8")
refs = "\n".join(line for line in original.splitlines() if line.startswith("-r:"))
test_rsp = OUT / "PartChecks.rsp"
test_rsp.write_text(f'-target:exe\n-nostdlib\n-langversion:9.0\n-out:"{(OUT / "PartChecks.dll").as_posix()}"\n'
                    + refs + f'\n"{(PROJECT / "Assets/MotorSimulatorAssets/Scripts/RideVehicleParts.cs").as_posix()}"\n"{test.as_posix()}"', encoding="utf-8")
run_rsp(test_rsp)
runtime_version = sorted((editor / "Data/NetCoreRuntime/shared/Microsoft.NETCore.App").iterdir())[-1].name
(OUT / "PartChecks.runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {
    "tfm": "net" + ".".join(runtime_version.split(".")[:2]),
    "framework": {"name": "Microsoft.NETCore.App", "version": runtime_version}}}))
result = subprocess.run([str(dotnet), str(OUT / "PartChecks.dll"),
    str(PROJECT / "Assets/MotorSimulatorAssets/Models/UrbanScooter/UrbanScooter.obj")], capture_output=True, text=True, encoding="utf-8", errors="replace")
if result.returncode:
    print(result.stdout, result.stderr)
    raise SystemExit(result.returncode)
parts = {name: group for group, name in (line.split("\t") for line in result.stdout.splitlines())}
(OUT / "part-groups.json").write_text(json.dumps(parts, indent=2), encoding="utf-8")
print("PASS 8 classification regressions and", len(parts), "real model groups")
