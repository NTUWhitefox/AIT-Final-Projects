"""Update only the derived Car26 prefab in an isolated Unity, with maneuver checks."""
from pathlib import Path
import shutil
import subprocess
import sys

ROOT=Path(__file__).resolve().parents[1]
PROJECT=ROOT/'VR-Motor-Street-Riding'
CLONE=PROJECT/'Personal/RepairValidationProject'
UNITY=Path(r'D:\GameMakingTools\UnityEditors\6000.3.21f1\Editor\Unity.exe')
for folder in ('Assets','Packages','ProjectSettings'):
    shutil.copytree(PROJECT/folder,CLONE/folder,dirs_exist_ok=True)
for name in ('UrbanVehiclePreviewBuilder.cs','RidingManeuverUpdate.cs'):
    shutil.copy2(ROOT/'Tools/UnityValidation'/name,CLONE/'Assets/Editor'/name)
log=CLONE/'Personal/maneuver-update.log'
result=subprocess.run([str(UNITY),'-batchmode','-force-d3d11','-projectPath',str(CLONE),
    '-executeMethod','RidingManeuverUpdate.Run','-quit','-logFile',str(log)],
    creationflags=subprocess.CREATE_NO_WINDOW,timeout=180)
if result.returncode:
    print(log.read_text(encoding='utf-8-sig',errors='replace')[-6500:])
    raise SystemExit(result.returncode)
print((CLONE/'Personal/VehiclePreview/maneuver-check.txt').read_text(encoding='utf-8-sig'))
if '--write-back' in sys.argv:
    relative=Path('Assets/MotorSimulatorAssets/CityPreviewGenerated/Car26_URP.prefab')
    shutil.copy2(CLONE/relative,PROJECT/relative)
    print('Updated only Car26_URP.prefab; scene layout and source models preserved.')
