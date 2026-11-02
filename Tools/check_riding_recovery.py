"""Run native Unity recovery checks in an ignored, isolated project, with no GUI.

Does not open or control the working project's Unity editor or initialize an HMD.
Reports under Personal/RepairValidationProject/Personal. Requires installed Unity.
"""
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "VR-Motor-Street-Riding"
CLONE = PROJECT / "Personal/RepairValidationProject"
UNITY = Path(r"D:\GameMakingTools\UnityEditors\6000.3.21f1\Editor\Unity.exe")

# Refresh source assets/settings as well as scripts so reruns test the current scene.
for directory in ("Assets", "Packages", "ProjectSettings"):
    shutil.copytree(PROJECT / directory, CLONE / directory, dirs_exist_ok=True)
cache = PROJECT / "Library/PackageCache"
if cache.exists() and not (CLONE / "Library/PackageCache").exists():
    shutil.copytree(cache, CLONE / "Library/PackageCache")
for obsolete in ("RideSettingsForm.cs", "RideSettingsForm.cs.meta"):
    (CLONE / "Assets/MotorSimulatorAssets/Scripts" / obsolete).unlink(missing_ok=True)

(CLONE / "Assets/Editor").mkdir(exist_ok=True)
shutil.copy2(ROOT / "Tools/RideRecoveryBatch.cs", CLONE / "Assets/Editor/RideRecoveryBatch.cs")
for obsolete in ("FirstRideValidation.cs", "FirstRideValidation.cs.meta", "FirstRidePlaySmoke.cs", "FirstRidePlaySmoke.cs.meta", "FirstRideSceneBuilder.cs", "FirstRideSceneBuilder.cs.meta"):
    (CLONE / "Assets/MotorSimulatorAssets/Editor" / obsolete).unlink(missing_ok=True)
for helper in ("FirstRideValidation.cs", "FirstRideSceneBuilder.cs"):
    shutil.copy2(ROOT / "Tools/UnityValidation" / helper, CLONE / "Assets/Editor" / helper)
(CLONE / "Personal").mkdir(exist_ok=True)

for method, report, quit_after in (("Run", "recovery-results.txt", True),
                                    ("RunPlay", "recovery-play-results.txt", False)):
    log = CLONE / "Personal" / (method + ".log")
    output = CLONE / "Personal" / report
    output.unlink(missing_ok=True)
    args = [str(UNITY), "-batchmode", "-nographics", "-projectPath", str(CLONE),
            "-executeMethod", "RideRecoveryBatch." + method, "-logFile", str(log)]
    if quit_after:
        args.append("-quit")
    print("Running native Unity", method, "in isolated project (no GUI)", flush=True)
    result = subprocess.run(args, creationflags=subprocess.CREATE_NO_WINDOW, timeout=150)
    if output.exists():
        print(output.read_text(encoding="utf-8-sig"), flush=True)
    if result.returncode or not output.exists():
        print(log.read_text(encoding="utf-8-sig", errors="replace")[-14000:], flush=True)
        raise SystemExit(result.returncode or 1)
