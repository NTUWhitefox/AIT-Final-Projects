"""Prepare supplied building archives and author comparison scenes in an ignored Unity clone.

python Tools/build_streetscape_comparison.py --write-back
Native models, derivatives and local scenes are excluded from git per source licences.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import uuid
import zipfile

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "VR-Motor-Street-Riding"
LOCAL = Path("Assets/MotorSimulatorAssets/BuildingComparisonLocal")
BASE = PROJECT / "Personal/BuildingComparison"
CLONE = PROJECT / "Personal/RepairValidationProject"
UNITY = Path(r"D:\GameMakingTools\UnityEditors\6000.3.21f1\Editor\Unity.exe")
BLENDER = Path(r"D:\GameMakingTools\blender-4.5.16-windows-x64\blender-4.5.16-windows-x64\blender.exe")
ADDON_COMMIT = "37ad42098d2b8b16b88695a9d7d117f33b57c4f3"
ADDON_SHA = "3001542d9e046822fe903ba94d2111a23694b637a29b27eefb0094d14320f7ff"


def extract(archive, destination):
    destination.mkdir(parents=True, exist_ok=True)
    root = destination.resolve()
    with zipfile.ZipFile(archive) as z:
        for member in z.infolist():
            if not (root / member.filename).resolve().is_relative_to(root):
                raise ValueError("Archive escapes destination: " + member.filename)
            if (member.external_attr >> 16) & 0o170000 == 0o120000:
                raise ValueError("Archive contains a symlink")
        z.extractall(destination)


def metadata(folder):
    for file in [folder, *sorted(folder.rglob("*"))]:
        if file.suffix == ".meta":
            continue
        meta = Path(str(file) + ".meta")
        if not meta.exists():
            identity = "ait-streetscape-v1/" + file.relative_to(folder.parent).as_posix()
            meta.write_text("fileFormatVersion: 2\nguid: " + uuid.uuid5(uuid.NAMESPACE_URL, identity).hex + "\n", encoding="utf-8")


def run(command, log, timeout):
    log.parent.mkdir(parents=True, exist_ok=True)
    with log.open("w", encoding="utf-8") as stream:
        result = subprocess.run(list(map(str, command)), stdout=stream, stderr=subprocess.STDOUT,
                                creationflags=subprocess.CREATE_NO_WINDOW, timeout=timeout)
    if result.returncode:
        actual = Path(command[command.index("-logFile") + 1]) if "-logFile" in command else log
        print(actual.read_text(encoding="utf-8-sig", errors="replace")[-7000:])
        raise SystemExit(result.returncode)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--write-back", action="store_true")
    parser.add_argument("--write-back-only", action="store_true")
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--blender", type=Path, default=BLENDER)
    args = parser.parse_args()
    BASE.mkdir(parents=True, exist_ok=True)
    exclude = ROOT / ".git/info/exclude"
    body = exclude.read_text(encoding="utf-8-sig")
    for rule in ("/VR-Motor-Street-Riding/" + LOCAL.as_posix() + "/", "/VR-Motor-Street-Riding/" + LOCAL.as_posix() + ".meta"):
        if rule not in body.splitlines():
            body += "\n" + rule + "\n"
    exclude.write_text(body, encoding="utf-8")
    if not args.write_back_only:
        archives = [ROOT / "tempSpace/tai-wan-buildings-pack.zip", *sorted((ROOT / "tempSpace").glob("lowpolyjapanesetownbuildings_fbx*.zip"))]
        if len(archives) != 2 or not all(p.exists() for p in archives):
            raise FileNotFoundError("Expected the supplied Pasha ZIP and one Kobo FBX ZIP in tempSpace")
        if hashlib.sha256(archives[0].read_bytes()).hexdigest() != "4e98bdb8cec6bda9eb4c60b6206731bde6fd0e377fdb159cfb29897196f346e4":
            raise ValueError("Pasha archive changed; re-check connected groups and facade corrections before conversion")
        for archive in archives:
            extract(archive, BASE / "Source" / archive.stem)
        pasha = BASE / "Source/tai-wan-buildings-pack/source"
        extract(pasha / "Mo_3ds.zip", pasha / "Mo_3ds")
        kobo = BASE / "Source" / archives[1].stem / "LowPolyJapaneseTownBuildings"
        tools = BASE / "Tools"
        addon_zip = tools / "io_scene_3ds.zip"
        if not addon_zip.exists():
            import urllib.request
            tools.mkdir(exist_ok=True)
            urllib.request.urlretrieve("https://codeload.github.com/nrgsille76/io_scene_3ds/zip/" + ADDON_COMMIT, addon_zip)
        if hashlib.sha256(addon_zip.read_bytes()).hexdigest() != ADDON_SHA:
            raise ValueError("3DS importer archive checksum mismatch")
        extract(addon_zip, tools)
        print("Converting Pasha 3DS and inspecting Kobo FBX with Blender in background", flush=True)
        run([args.blender, "--background", "--factory-startup", "--python", ROOT / "Tools/Blender/prepare_building_comparison.py", "--",
             "--pasha", next((pasha / "Mo_3ds").glob("*.3ds")), "--kobo", kobo,
             "--addon", tools / ("io_scene_3ds-" + ADDON_COMMIT) / "source", "--out", BASE / "Inspection", "--export", BASE / "Prepared"], BASE / "blender-export.log", 180)
        target = BASE / "Prepared/Kobo"
        for folder in ("models", "palette"):
            shutil.copytree(kobo / folder, target / folder, dirs_exist_ok=True)
        for name in ("README.md", "SIZES.txt", "PIVOTS.txt", "manifest.json", "CHANGELOG.txt"):
            shutil.copy2(kobo / name, target / name)
        sources = {
            "date": "2026-11-25", "blender": str(args.blender), "importer_commit": ADDON_COMMIT,
            "archives": [{"name": p.name, "sha256": hashlib.sha256(p.read_bytes()).hexdigest()} for p in archives],
            "pasha_url": "https://sketchfab.com/3d-models/tai-wan-buildings-pack-e40921ac50604b97b4d8f48cf13a25b3",
            "pasha_license": "Sketchfab Free Standard; original ZIP supplied by user; not CC0",
            "kobo_url": "https://www.fab.com/listings/72f9e3a7-9519-4494-86db-359b56635b52",
            "kobo_license": "Fab Standard per bundled README; raw/modified asset files may not be put in public repositories",
            "scope": "Local comparison only. Source textures/styles preserved; 3DS scale inferred as inches; no third-party Editor scripts or LODs imported."
        }
        (BASE / "Prepared/source.json").write_text(json.dumps(sources, ensure_ascii=False, indent=2), encoding="utf-8")
        for folder in ("Assets", "Packages", "ProjectSettings"):
            shutil.copytree(PROJECT / folder, CLONE / folder, dirs_exist_ok=True)
        shutil.copytree(BASE / "Prepared", CLONE / LOCAL, dirs_exist_ok=True)
        metadata(CLONE / LOCAL)
        editor = CLONE / "Assets/Editor"; editor.mkdir(exist_ok=True)
        for name in ("StreetscapeComparisonBuilder.cs", "GridCityBuilder.cs", "GridRoadGeometry.cs", "GridCityScenery.cs", "GridPedestrianBuilder.cs", "UrbanVehiclePreviewBuilder.cs"):
            shutil.copy2(ROOT / "Tools/UnityValidation" / name, editor / name)
        print("Authoring and capturing both streetscapes in the isolated Unity clone", flush=True)
        run([UNITY, "-batchmode", "-force-d3d11", "-projectPath", CLONE, "-executeMethod", "StreetscapeComparisonBuilder.Run", "-quit",
             "-logFile", CLONE / "Personal/BuildingComparison/author.log"], BASE / "unity-process.log", 300)
        print((CLONE / "Personal/BuildingComparison/authoring.txt").read_text(encoding="utf-8-sig"), flush=True)
    if args.check:
        shutil.copy2(ROOT / "Tools/UnityValidation/StreetscapeComparisonPlayCheck.cs", CLONE / "Assets/Editor/StreetscapeComparisonPlayCheck.cs")
        (CLONE / "Personal/BuildingComparison/play-check.txt").unlink(missing_ok=True)
        run([UNITY, "-batchmode", "-nographics", "-projectPath", CLONE, "-executeMethod", "StreetscapeComparisonPlayCheck.Run",
             "-logFile", CLONE / "Personal/BuildingComparison/play.log"], BASE / "play-process.log", 150)
        print((CLONE / "Personal/BuildingComparison/play-check.txt").read_text(encoding="utf-8-sig"), flush=True)
    if args.write_back or args.write_back_only:
        shutil.copytree(CLONE / LOCAL, PROJECT / LOCAL, dirs_exist_ok=True)
        shutil.copy2(Path(str(CLONE / LOCAL) + ".meta"), Path(str(PROJECT / LOCAL) + ".meta"))
        print("Wrote both local comparison scenes, prefabs and converted assets; sources remain excluded from git.", flush=True)


if __name__ == "__main__":
    main()
