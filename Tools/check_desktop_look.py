"""Check Desktop mouse-look in an isolated batch Editor, without OS input or UI control."""
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "VR-Motor-Street-Riding"
CLONE = PROJECT / "Personal/RepairValidationProject"
UNITY = Path(r"D:\GameMakingTools\UnityEditors\6000.3.21f1\Editor\Unity.exe")
OUT = CLONE / "Personal/DesktopLook"

for folder in ("Assets", "Packages", "ProjectSettings"):
    shutil.copytree(PROJECT / folder, CLONE / folder, dirs_exist_ok=True)
editor = CLONE / "Assets/Editor"
editor.mkdir(exist_ok=True)
shutil.copy2(ROOT / "Tools/UnityValidation/DesktopLookPlayCheck.cs", editor / "DesktopLookPlayCheck.cs")
probe = CLONE / "Assets/GridValidation"
probe.mkdir(exist_ok=True)
shutil.copy2(ROOT / "Tools/UnityValidation/DesktopLookPointer.cs", probe / "DesktopLookPointer.cs")
# A -batchmode Editor has no window and cannot retain native pointer lock. Replace
# only that OS boundary in the clone; use the real camera, input and session loop.
display = CLONE / "Assets/MotorSimulatorAssets/Scripts/RideDisplay.cs"
display.write_text("using Cursor = AIT.Validation.DesktopLookPointer;\n" + display.read_text(encoding="utf-8-sig"), encoding="utf-8")
OUT.mkdir(parents=True, exist_ok=True)
report = OUT / "play-check.txt"
report.unlink(missing_ok=True)
try:
    result = subprocess.run([
        str(UNITY), "-batchmode", "-nographics", "-projectPath", str(CLONE),
        "-executeMethod", "DesktopLookPlayCheck.Run", "-logFile", str(OUT / "play.log"),
    ], creationflags=subprocess.CREATE_NO_WINDOW, timeout=180)
finally:
    shutil.copy2(PROJECT / display.relative_to(CLONE), display)
if report.exists():
    print(report.read_text(encoding="utf-8-sig"))
if result.returncode or not report.exists():
    print((OUT / "play.log").read_text(encoding="utf-8-sig", errors="replace")[-7000:])
    raise SystemExit(result.returncode or 1)
