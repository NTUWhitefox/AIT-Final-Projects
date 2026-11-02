"""Author the reviewable vehicle street in an isolated, noninteractive Unity process.

Run without arguments to inspect/render. --write-back copies only authored assets,
scene and our texture import settings to the working project after review.
"""
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "VR-Motor-Street-Riding"
CLONE = PROJECT / "Personal/RepairValidationProject"
UNITY = Path(r"D:\GameMakingTools\UnityEditors\6000.3.21f1\Editor\Unity.exe")
for directory in ("Assets", "Packages", "ProjectSettings"):
    shutil.copytree(PROJECT / directory, CLONE / directory, dirs_exist_ok=True)
helpers = CLONE / "Assets/Editor"
helpers.mkdir(exist_ok=True)
shutil.copy2(ROOT / "Tools/UnityValidation/UrbanVehiclePreviewBuilder.cs", helpers / "UrbanVehiclePreviewBuilder.cs")
shutil.copy2(ROOT / "Tools/UnityValidation/VehiclePreviewPlayCheck.cs", helpers / "VehiclePreviewPlayCheck.cs")
log = CLONE / "Personal/vehicle-authoring.log"
if "--play-only" not in sys.argv:
    print("Authoring/rendering vehicle preview in isolated Unity (batch mode, no XR)", flush=True)
    result = subprocess.run([str(UNITY), "-batchmode", "-force-d3d11", "-projectPath", str(CLONE),
                             "-executeMethod", "UrbanVehiclePreviewBuilder.Run", "-quit", "-logFile", str(log)],
                            creationflags=subprocess.CREATE_NO_WINDOW, timeout=300)
    if result.returncode:
        print(log.read_text(encoding="utf-8-sig", errors="replace")[-6000:])
        raise SystemExit(result.returncode)
print((CLONE / "Personal/VehiclePreview/asset-notes.txt").read_text(encoding="utf-8-sig"))
print("Previews:", CLONE / "Personal/VehiclePreview", flush=True)
if "--check" in sys.argv or "--play-only" in sys.argv:
    check_log = CLONE / "Personal/vehicle-play.log"
    report = CLONE / "Personal/VehiclePreview/play-check.txt"
    report.unlink(missing_ok=True)
    result = subprocess.run([str(UNITY), "-batchmode", "-nographics", "-projectPath", str(CLONE),
                             "-executeMethod", "VehiclePreviewPlayCheck.Run", "-logFile", str(check_log)],
                            creationflags=subprocess.CREATE_NO_WINDOW, timeout=120)
    if report.exists():
        print(report.read_text(encoding="utf-8-sig"), flush=True)
    if result.returncode or not report.exists():
        print(check_log.read_text(encoding="utf-8-sig", errors="replace")[-6000:])
        raise SystemExit(result.returncode or 1)
if "--write-back" in sys.argv:
    relative = Path("Assets/MotorSimulatorAssets/CityPreviewGenerated")
    shutil.copytree(CLONE / relative, PROJECT / relative, dirs_exist_ok=True)
    shutil.copy2(CLONE / Path(str(relative) + ".meta"), PROJECT / Path(str(relative) + ".meta"))
    for name in ("UrbanVehiclePreview.unity", "UrbanVehiclePreview.unity.meta"):
        shutil.copy2(CLONE / "Assets/Scenes" / name, PROJECT / "Assets/Scenes" / name)
    # Preserve all supplied FBX import settings. Only our extracted textures get
    # the normal-map type, colour-space and 2K limit configured during authoring.
    relative = Path("Assets/MotorSimulatorAssets/Models/Test/ScooterD_Textures")
    for meta in (CLONE / relative).glob("*.meta"):
        shutil.copy2(meta, PROJECT / relative / meta.name)
    print("Copied authored prefabs, materials and scene into working project.")
