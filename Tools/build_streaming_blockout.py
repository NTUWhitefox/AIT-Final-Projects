"""Author a primitive streamed world in an ignored clone; never operate Unity UI."""
from pathlib import Path
import re,shutil,subprocess,sys,uuid
ROOT=Path(__file__).resolve().parents[1]
PROJECT=ROOT/'VR-Motor-Street-Riding'
CLONE=PROJECT/'Personal/RepairValidationProject'
UNITY=Path(r'D:\GameMakingTools\UnityEditors\6000.3.21f1\Editor\Unity.exe')
for f in (PROJECT/'Assets/MotorSimulatorAssets/Scripts').glob('*.cs'):
    m=Path(str(f)+'.meta')
    if not m.exists():m.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n',encoding='utf-8')
for folder in ('Assets','Packages','ProjectSettings'):
    shutil.copytree(PROJECT/folder,CLONE/folder,dirs_exist_ok=True)
# These scenes were moved by the user. Do not leave old duplicate GUIDs in
# the reused validation clone or let them rewrite the user's build paths.
for name in ('BasicScene.unity','FirstRideTest.unity','SampleScene.unity'):
    if not (PROJECT/'Assets/Scenes'/name).exists():
        for suffix in ('','.meta'):(CLONE/'Assets/Scenes'/(name+suffix)).unlink(missing_ok=True)
for name in ('StreamingBlockoutBuilder.cs','UrbanVehiclePreviewBuilder.cs'):
    shutil.copy2(ROOT/'Tools/UnityValidation'/name,CLONE/'Assets/Editor'/name)
log=CLONE/'Personal/streaming-author.log'
r=subprocess.run([str(UNITY),'-batchmode','-force-d3d11','-projectPath',str(CLONE),'-executeMethod','StreamingBlockoutBuilder.Run','-quit','-logFile',str(log)],creationflags=subprocess.CREATE_NO_WINDOW,timeout=240)
if r.returncode:
    print(log.read_text(encoding='utf-8-sig',errors='replace')[-6500:]);raise SystemExit(r.returncode)
print((CLONE/'Personal/StreamingBlockout/authoring.txt').read_text(encoding='utf-8-sig'))
if '--check-player' in sys.argv:
    probe=CLONE/'Assets/StreamingValidation'
    probe.mkdir(exist_ok=True)
    shutil.copy2(ROOT/'Tools/UnityValidation/StreamingPlayerProbe.cs',probe/'StreamingPlayerProbe.cs')
    shutil.copy2(ROOT/'Tools/UnityValidation/StreamingAcceptanceBuild.cs',CLONE/'Assets/Editor/StreamingAcceptanceBuild.cs')
    log=CLONE/'Personal/streaming-build.log'
    print('Building isolated Windows acceptance Player',flush=True)
    r=subprocess.run([str(UNITY),'-batchmode','-nographics','-projectPath',str(CLONE),'-executeMethod','StreamingAcceptanceBuild.Run','-quit','-logFile',str(log)],creationflags=subprocess.CREATE_NO_WINDOW,timeout=360)
    if r.returncode:
        print(log.read_text(encoding='utf-8-sig',errors='replace')[-7000:]);raise SystemExit(r.returncode)
    report=CLONE/'Personal/StreamingBlockout/player-check.txt'
    report.unlink(missing_ok=True)
    print('Driving and checking real scene streaming in the built Player',flush=True)
    r=subprocess.run([str(CLONE/'Personal/StreamingBlockout/Player/StreamingBlockout.exe'),'-batchmode','-nographics','-ait-stream-probe','-ait-probe-report',str(report),'-logFile',str(CLONE/'Personal/StreamingBlockout/player.log')],creationflags=subprocess.CREATE_NO_WINDOW,timeout=190)
    if report.exists():print(report.read_text(encoding='utf-8-sig'),flush=True)
    if r.returncode or not report.exists():
        print((CLONE/'Personal/StreamingBlockout/player.log').read_text(encoding='utf-8-sig',errors='replace')[-6500:]);raise SystemExit(r.returncode or 1)
if '--write-back' in sys.argv:
    for rel in map(Path,['Assets/MotorSimulatorAssets/StreamingBlockoutGenerated','Assets/Scenes/StreamingBlockoutChunks']):
        shutil.copytree(CLONE/rel,PROJECT/rel,dirs_exist_ok=True)
        shutil.copy2(CLONE/Path(str(rel)+'.meta'),PROJECT/Path(str(rel)+'.meta'))
    for name in ('StreamingBlockout.unity','StreamingBlockout.unity.meta'):
        shutil.copy2(CLONE/'Assets/Scenes'/name,PROJECT/'Assets/Scenes'/name)
    # Unity may normalize paths by GUID in a reused clone. Preserve the user's
    # original entries/settings verbatim and append only our new scene paths.
    settings=PROJECT/'ProjectSettings/EditorBuildSettings.asset'
    body=settings.read_text(encoding='utf-8-sig')
    if '  m_configObjects:' not in body:raise ValueError('Unexpected EditorBuildSettings layout')
    additions=[]
    for scene in [PROJECT/'Assets/Scenes/StreamingBlockout.unity',*sorted((PROJECT/'Assets/Scenes/StreamingBlockoutChunks').glob('*.unity'))]:
        path=scene.relative_to(PROJECT).as_posix()
        if re.search(r'^    path: '+re.escape(path)+r'$',body,re.M):continue
        guid=re.search(r'^guid: ([0-9a-f]+)$',Path(str(scene)+'.meta').read_text(),re.M).group(1)
        additions.append(f'  - enabled: 1\n    path: {path}\n    guid: {guid}\n')
    body=body.replace('  m_configObjects:', ''.join(additions)+'  m_configObjects:',1)
    settings.write_text(body,encoding='utf-8')
    print('Copied the blockout bootstrap, 16 chunk scenes, generated data/meshes and appended build-scene entries.')
