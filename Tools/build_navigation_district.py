"""Bake and ride the non-grid district in the local Unity validation project."""
import argparse
from pathlib import Path
import shutil

from build_streetscape_comparison import CLONE, LOCAL, PROJECT, ROOT, UNITY, run


AUTHORING = ROOT / "Tools/UnityValidation"
DISTRICT = LOCAL / "NavigationDistrict"
REPORTS = Path("Personal/NavigationDistrict")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--write-back", action="store_true")
    parser.add_argument("--check-only", action="store_true")
    args = parser.parse_args()
    source = PROJECT / LOCAL / "TrafficPreview/KoboTraffic.unity"
    if not source.is_file():
        raise FileNotFoundError(f"Build the local Kobo traffic scene first: {source}")

    reports = CLONE / REPORTS
    reports.mkdir(parents=True, exist_ok=True)
    editor = CLONE / "Assets/Editor"
    if not args.check_only:
        for folder in ("Assets", "Packages", "ProjectSettings"):
            shutil.copytree(PROJECT / folder, CLONE / folder, dirs_exist_ok=True)
        editor.mkdir(exist_ok=True)
        (reports.parent / "VehiclePreview").mkdir(exist_ok=True)
        for name in ("NavigationDistrictBuilder.cs", "GridRoadGeometry.cs", "UrbanVehiclePreviewBuilder.cs"):
            shutil.copy2(AUTHORING / name, editor / name)
        shutil.copy2(AUTHORING / "NavigationDistrictLayout.json", reports / "layout.json")
        run([UNITY, "-batchmode", "-force-d3d11", "-projectPath", CLONE, "-executeMethod",
             "NavigationDistrictBuilder.Run", "-quit", "-logFile", reports / "author.log"],
            reports / "author-process.log", 160)
        print((reports / "authoring.txt").read_text(encoding="utf-8-sig"), flush=True)

    shutil.copy2(AUTHORING / "NavigationDistrictPlayCheck.cs", editor / "NavigationDistrictPlayCheck.cs")
    report = reports / "play-check.txt"
    report.unlink(missing_ok=True)
    run([UNITY, "-batchmode", "-nographics", "-projectPath", CLONE, "-executeMethod",
         "NavigationDistrictPlayCheck.Run", "-logFile", reports / "play.log"], reports / "play-process.log", 145)
    result = report.read_text(encoding="utf-8-sig")
    if not result.startswith("PASS\n"):
        raise RuntimeError(result)
    print(result, flush=True)

    destination = PROJECT / REPORTS
    destination.mkdir(parents=True, exist_ok=True)
    for name in ("authoring.txt", "play-check.txt", "overview.png", "two-turns.png", "right-turns.png"):
        shutil.copy2(reports / name, destination / name)
    if args.write_back:
        shutil.copytree(CLONE / DISTRICT, PROJECT / DISTRICT, dirs_exist_ok=True)
        metadata = Path(str(DISTRICT) + ".meta")
        shutil.copy2(CLONE / metadata, PROJECT / metadata)
        print("Installed the verified navigation district scene.", flush=True)


if __name__ == "__main__":
    main()
