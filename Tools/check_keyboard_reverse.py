"""Check S braking/reverse in the local comparison scene; never operates the desktop."""
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "VR-Motor-Street-Riding"
CLONE = PROJECT / "Personal/RepairValidationProject"
UNITY = Path(r"D:\GameMakingTools\UnityEditors\6000.3.21f1\Editor\Unity.exe")
SCENE = Path("Assets/MotorSimulatorAssets/BuildingComparisonLocal/Scenes/StreetscapeKobo.unity")
if not (PROJECT / SCENE).exists():
    raise SystemExit("Build the local streetscape comparison first (Tools/build_streetscape_comparison.py).")
for folder in ("Assets", "Packages", "ProjectSettings"):
    shutil.copytree(PROJECT / folder, CLONE / folder, dirs_exist_ok=True)
editor = CLONE / "Assets/Editor"
editor.mkdir(exist_ok=True)
shutil.copy2(ROOT / "Tools/UnityValidation/KeyboardReversePlayCheck.cs", editor / "KeyboardReversePlayCheck.cs")
out = CLONE / "Personal/KeyboardReverse"
out.mkdir(parents=True, exist_ok=True)
report = out / "play-check.txt"
report.unlink(missing_ok=True)
result = subprocess.run([
    str(UNITY), "-batchmode", "-nographics", "-projectPath", str(CLONE),
    "-executeMethod", "KeyboardReversePlayCheck.Run", "-logFile", str(out / "play.log"),
], creationflags=subprocess.CREATE_NO_WINDOW, timeout=180)
if report.exists():
    print(report.read_text(encoding="utf-8-sig"))
if result.returncode or not report.exists():
    print((out / "play.log").read_text(encoding="utf-8-sig", errors="replace")[-7000:])
    raise SystemExit(result.returncode or 1)
