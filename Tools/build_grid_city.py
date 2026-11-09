"""Author/verify Grid City in an ignored clone; no interaction with the user's Editor."""
from pathlib import Path
import re, shutil, subprocess, sys, uuid

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / 'VR-Motor-Street-Riding'
CLONE = PROJECT / 'Personal/RepairValidationProject'
UNITY = Path(r'D:\GameMakingTools\UnityEditors\6000.3.21f1\Editor\Unity.exe')
OUT = CLONE / 'Personal/GridCity'
for folder in ('Scripts', 'Shaders'):
    for f in (PROJECT / 'Assets/MotorSimulatorAssets' / folder).glob('*'):
        if f.is_file() and f.suffix in ('.cs', '.shader'):
            meta = Path(str(f) + '.meta')
            if not meta.exists():
                meta.write_text('fileFormatVersion: 2\nguid: ' + uuid.uuid4().hex + '\n', encoding='utf-8')
for f in [*(PROJECT / 'Assets/MotorSimulatorAssets/Models/QuaterniusPedestrians').glob('*'), *(PROJECT / 'Assets/MotorSimulatorAssets/Models/CitySignage').glob('*')]:
    if f.is_file() and f.suffix != '.meta':
        meta = Path(str(f) + '.meta')
        if not meta.exists():
            meta.write_text('fileFormatVersion: 2\nguid: ' + uuid.uuid4().hex + '\n', encoding='utf-8')
if '--write-back-only' not in sys.argv:
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROJECT / folder, CLONE / folder, dirs_exist_ok=True)
    # Remove only our superseded character/mesh derivatives from this new city.
    generated = CLONE / 'Assets/MotorSimulatorAssets/GridCityGenerated'
    for pattern in ('OldClassy_Male*', 'Human_OldClassy_Male*', 'SignalLenses.asset*'):
        for obsolete in generated.glob(pattern):
            if obsolete.is_file() and obsolete.resolve().parent == generated.resolve():
                obsolete.unlink()
    for suffix in ('', '.meta'):
        obsolete = CLONE / 'Assets/MotorSimulatorAssets/Models/QuaterniusPedestrians' / ('OldClassy_Male.fbx' + suffix)
        obsolete.unlink(missing_ok=True)
    for name in ('BasicScene.unity', 'FirstRideTest.unity', 'SampleScene.unity'):
        if not (PROJECT / 'Assets/Scenes' / name).exists():
            for suffix in ('', '.meta'):
                (CLONE / 'Assets/Scenes' / (name + suffix)).unlink(missing_ok=True)
    for name in ('GridCityBuilder.cs', 'GridRoadGeometry.cs', 'GridPedestrianBuilder.cs', 'GridCityScenery.cs', 'UrbanVehiclePreviewBuilder.cs'):
        shutil.copy2(ROOT / 'Tools/UnityValidation' / name, CLONE / 'Assets/Editor' / name)
    OUT.mkdir(parents=True, exist_ok=True)
    log = OUT / 'author.log'
    r = subprocess.run([str(UNITY), '-batchmode', '-force-d3d11', '-projectPath', str(CLONE),
                        '-executeMethod', 'GridCityBuilder.Run', '-quit', '-logFile', str(log)],
                       creationflags=subprocess.CREATE_NO_WINDOW, timeout=300)
    if r.returncode:
        print(log.read_text(encoding='utf-8-sig', errors='replace')[-10000:])
        raise SystemExit(r.returncode)
    print((OUT / 'authoring.txt').read_text(encoding='utf-8-sig'), flush=True)

if '--check-player' in sys.argv:
    probe = CLONE / 'Assets/GridValidation'
    probe.mkdir(exist_ok=True)
    shutil.copy2(ROOT / 'Tools/UnityValidation/GridCityPlayerProbe.cs', probe / 'GridCityPlayerProbe.cs')
    shutil.copy2(ROOT / 'Tools/UnityValidation/GridCityAcceptanceBuild.cs', CLONE / 'Assets/Editor/GridCityAcceptanceBuild.cs')
    log = OUT / 'build.log'
    print('Building isolated Grid City Player', flush=True)
    r = subprocess.run([str(UNITY), '-batchmode', '-nographics', '-projectPath', str(CLONE), '-executeMethod', 'GridCityAcceptanceBuild.Run', '-quit', '-logFile', str(log)], creationflags=subprocess.CREATE_NO_WINDOW, timeout=360)
    if r.returncode:
        print(log.read_text(encoding='utf-8-sig', errors='replace')[-8000:]); raise SystemExit(r.returncode)
    report = OUT / 'player-check.txt'
    report.unlink(missing_ok=True)
    print('Riding the full city loop in the built Player', flush=True)
    r = subprocess.run([str(OUT / 'Player/GridCity.exe'), '-batchmode', '-nographics', '-ait-grid-probe', '-ait-probe-report', str(report), '-logFile', str(OUT / 'player.log')], creationflags=subprocess.CREATE_NO_WINDOW, timeout=230)
    if report.exists():
        print(report.read_text(encoding='utf-8-sig'), flush=True)
    if r.returncode or not report.exists():
        print((OUT / 'player.log').read_text(encoding='utf-8-sig', errors='replace')[-6000:]); raise SystemExit(r.returncode or 1)

if '--write-back' in sys.argv or '--write-back-only' in sys.argv:
    for rel in map(Path, ['Assets/MotorSimulatorAssets/GridCityGenerated', 'Assets/Scenes/GridCityChunks']):
        shutil.copytree(CLONE / rel, PROJECT / rel, dirs_exist_ok=True)
        shutil.copy2(CLONE / Path(str(rel) + '.meta'), PROJECT / Path(str(rel) + '.meta'))
    for name in ('GridCity.unity', 'GridCity.unity.meta'):
        shutil.copy2(CLONE / 'Assets/Scenes' / name, PROJECT / 'Assets/Scenes' / name)
    for meta in (CLONE / 'Assets/MotorSimulatorAssets/Models/QuaterniusPedestrians').glob('*.fbx.meta'):
        shutil.copy2(meta, PROJECT / meta.relative_to(CLONE))
    for meta in (CLONE / 'Assets/MotorSimulatorAssets/Models/CitySignage').glob('*.png.meta'):
        shutil.copy2(meta, PROJECT / meta.relative_to(CLONE))
    settings = PROJECT / 'ProjectSettings/EditorBuildSettings.asset'
    body = settings.read_text(encoding='utf-8-sig')
    if '  m_configObjects:' not in body:
        raise ValueError('Unexpected build settings format')
    additions = []
    for scene in [PROJECT / 'Assets/Scenes/GridCity.unity', *sorted((PROJECT / 'Assets/Scenes/GridCityChunks').glob('*.unity'))]:
        path = scene.relative_to(PROJECT).as_posix()
        if re.search(r'^    path: ' + re.escape(path) + r'$', body, re.M):
            continue
        guid = re.search(r'^guid: ([0-9a-f]+)$', Path(str(scene) + '.meta').read_text(), re.M).group(1)
        additions.append(f'  - enabled: 1\n    path: {path}\n    guid: {guid}\n')
    settings.write_text(body.replace('  m_configObjects:', ''.join(additions) + '  m_configObjects:', 1), encoding='utf-8')
    print('Copied Grid City and 16 chunks; preserved existing build entries.', flush=True)
