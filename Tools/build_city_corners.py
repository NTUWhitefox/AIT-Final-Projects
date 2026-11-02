"""Author/render City Corners in an ignored Unity clone, without operating the user's editor."""
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

ROOT=Path(__file__).resolve().parents[1]
PROJECT=ROOT/'VR-Motor-Street-Riding'
CLONE=PROJECT/'Personal/RepairValidationProject'
UNITY=Path(r'D:\GameMakingTools\UnityEditors\6000.3.21f1\Editor\Unity.exe')
for script in (PROJECT/'Assets/MotorSimulatorAssets/Scripts').glob('*.cs'):
    meta=Path(str(script)+'.meta')
    if not meta.exists():meta.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n',encoding='utf-8')
for folder in ('Assets','Packages','ProjectSettings'):
    shutil.copytree(PROJECT/folder,CLONE/folder,dirs_exist_ok=True)
for name in ('UrbanVehiclePreviewBuilder.cs','CityCornersBuilder.cs','CityCornersPlayCheck.cs'):
    shutil.copy2(ROOT/'Tools/UnityValidation'/name,CLONE/'Assets/Editor'/name)
log=CLONE/'Personal/city-authoring.log'
r=subprocess.run([str(UNITY),'-batchmode','-force-d3d11','-projectPath',str(CLONE),'-executeMethod','CityCornersBuilder.Run','-quit','-logFile',str(log)],creationflags=subprocess.CREATE_NO_WINDOW,timeout=240)
if r.returncode:
    print(log.read_text(encoding='utf-8-sig',errors='replace')[-6500:]);raise SystemExit(r.returncode)
print((CLONE/'Personal/VehiclePreview/city-authoring.txt').read_text(encoding='utf-8-sig'))
if '--check' in sys.argv:
    log=CLONE/'Personal/city-play.log'
    report=CLONE/'Personal/VehiclePreview/city-play.txt'
    report.unlink(missing_ok=True)
    r=subprocess.run([str(UNITY),'-batchmode','-nographics','-projectPath',str(CLONE),'-executeMethod','CityCornersPlayCheck.Run','-logFile',str(log)],creationflags=subprocess.CREATE_NO_WINDOW,timeout=150)
    if report.exists():print(report.read_text(encoding='utf-8-sig'))
    if r.returncode or not report.exists():
        print(log.read_text(encoding='utf-8-sig',errors='replace')[-5000:]);raise SystemExit(r.returncode or 1)
if '--write-back' in sys.argv:
    rel=Path('Assets/MotorSimulatorAssets/CityCornersGenerated')
    shutil.copytree(CLONE/rel,PROJECT/rel,dirs_exist_ok=True)
    shutil.copy2(CLONE/Path(str(rel)+'.meta'),PROJECT/Path(str(rel)+'.meta'))
    for name in ('CityCorners.unity','CityCorners.unity.meta','FirstRideTest.unity','UrbanVehiclePreview.unity'):
        shutil.copy2(CLONE/'Assets/Scenes'/name,PROJECT/'Assets/Scenes'/name)
    print('Copied City Corners, fixed hand meshes, and hand/speed-board updates to the existing scenes.')
