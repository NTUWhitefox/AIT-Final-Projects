"""Author and verify the local Kobo traffic preview using an isolated Unity project."""
import argparse
import hashlib
from pathlib import Path
import shutil

from build_streetscape_comparison import BLENDER, CLONE, LOCAL, PROJECT, ROOT, UNITY, metadata, run


SOURCE = PROJECT / "Personal/KoboAssetSelection"
PEDESTRIANS = PROJECT / "Personal/PedestrianSource"
REPORTS = PROJECT / "Personal/KoboTraffic"
TARGET = LOCAL / "TrafficPreview"
TDAL = PROJECT / "Assets/MotorSimulatorAssets/Models/Test/scooter_d.FBX"
INPUTS = {
    "quaternius-car-a.glb": "bf00f2f0386a25aa310abc0424d22586e46a59ee6c737e6b375c97c9f01bd462",
    "quaternius-suv.glb": "1a9ce2bba813dca5005abab09715b01b8b5f4a9c48d7260463afdfeb876aa8b6",
}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--write-back", action="store_true")
    parser.add_argument("--check-only", action="store_true")
    args = parser.parse_args()
    clone_reports = CLONE / "Personal/KoboTraffic"
    clone_reports.mkdir(parents=True, exist_ok=True)
    if not args.check_only:
        if not (PROJECT / LOCAL / "Scenes/StreetscapeKobo.unity").is_file():
            raise FileNotFoundError("The local Kobo streetscape must be available to build its traffic preview")
        for name, sha in INPUTS.items():
            if hashlib.sha256((SOURCE / name).read_bytes()).hexdigest() != sha:
                raise ValueError(f"Unexpected GLB version for {name}")
        if hashlib.sha256(TDAL.read_bytes()).hexdigest() != "425527c076ba83302fe8892a232ae7337eeef5964687d72dbdbb3643f8d16375":
            raise ValueError("TDAL file changed. Recheck the scooter mesh groups and contact points.")
        run([BLENDER, "--background", "--factory-startup", "--python-exit-code", "1",
             "--python", ROOT / "Tools/Blender/prepare_kobo_traffic.py", "--", SOURCE,
             PROJECT / "Assets/MotorSimulatorAssets/Models/QuaterniusPedestrians", SOURCE / "Prepared", TDAL],
            REPORTS / "blender.log", 90)
        run([BLENDER, "--background", "--factory-startup", "--python-exit-code", "1",
             "--python", ROOT / "Tools/Blender/prepare_kobo_pedestrians.py", "--", PEDESTRIANS,
             PEDESTRIANS / "Prepared"], REPORTS / "pedestrian-blender.log", 150)
        for folder in ("Assets", "Packages", "ProjectSettings"):
            shutil.copytree(PROJECT / folder, CLONE / folder, dirs_exist_ok=True)
        model_dir = CLONE / TARGET / "Models"
        model_dir.mkdir(parents=True, exist_ok=True)
        for name in ("Sedan.fbx", "SUV.fbx", "HelmetedRider.fbx", "CommuterScooter.fbx", "ScooterFit.json"):
            shutil.copy2(SOURCE / "Prepared" / name, model_dir / name)
        shutil.copytree(PEDESTRIANS / "Prepared", CLONE / TARGET / "Pedestrians", dirs_exist_ok=True)
        metadata(CLONE / TARGET)
        (CLONE / TARGET / "Sources.md").write_text(
            "# Kobo traffic preview sources\n\n"
            "- Quaternius, CC0: [NormalCar1](https://poly.pizza/m/Cz6yDaUcM9) and [SUV](https://poly.pizza/m/xsMtZhBkxL). "
            "Roof height, lamp surfaces, paint and wheel pivots adapted.\n"
            "- Quaternius, CC0: [Ultimate Animated Character Pack](https://quaternius.com/packs/ultimatedanimatedcharacter). "
            "Casual2_Male posed and baked, helmet and visor added.\n"
            "- Quaternius, CC0: [Universal Base Characters Standard](https://quaternius.itch.io/universal-base-characters) and "
            "[Universal Animation Library Standard](https://quaternius.itch.io/universal-animation-library). "
            "Adult pedestrians use the free rigged bases with project clothing surfaces and resized silhouettes. "
            "UAL idle and walk were retargeted, grounded and baked with animation speed matched to path movement.\n"
            "- TDAL, user-supplied Scooter D FBX: [license agreement](https://www.tdal.tw/tw/webcontent/license_agreement). "
            "Original geometry split by steering and axle role, with new flat materials and grips. The bike and scene derivatives stay in the local project.\n"
            "- The scene references the installed Kobo buildings under their bundled license.\n",
            encoding="utf-8",
        )
        editor = CLONE / "Assets/Editor"
        editor.mkdir(exist_ok=True)
        names = ("KoboTrafficPreviewBuilder.cs", "KoboPedestrianBuilder.cs", "StreetscapeComparisonBuilder.cs", "UrbanVehiclePreviewBuilder.cs",
                 "GridCityBuilder.cs", "GridRoadGeometry.cs", "GridCityScenery.cs", "GridPedestrianBuilder.cs")
        for name in names:
            shutil.copy2(ROOT / "Tools/UnityValidation" / name, editor / name)
        print("Authoring and capturing Kobo traffic in Unity", flush=True)
        run([UNITY, "-batchmode", "-force-d3d11", "-projectPath", CLONE, "-executeMethod", "KoboTrafficPreviewBuilder.Run",
             "-quit", "-logFile", clone_reports / "author.log"], REPORTS / "author-process.log", 180)
        print((clone_reports / "authoring.txt").read_text(encoding="utf-8-sig"), flush=True)

    shutil.copy2(ROOT / "Tools/UnityValidation/StreetscapeComparisonPlayCheck.cs", CLONE / "Assets/Editor/StreetscapeComparisonPlayCheck.cs")
    report = clone_reports / "play-check.txt"
    report.unlink(missing_ok=True)
    run([UNITY, "-batchmode", "-nographics", "-projectPath", CLONE, "-executeMethod", "StreetscapeComparisonPlayCheck.RunKoboTraffic",
         "-logFile", clone_reports / "play.log"], REPORTS / "play-process.log", 145)
    result = report.read_text(encoding="utf-8-sig")
    if not result.startswith("PASS\n"):
        raise RuntimeError(result)
    print(result, flush=True)
    for path in clone_reports.iterdir():
        if path.suffix in {".png", ".txt"}:
            shutil.copy2(path, REPORTS / path.name)
    if args.write_back:
        shutil.copytree(CLONE / TARGET, PROJECT / TARGET, dirs_exist_ok=True)
        shutil.copy2(Path(str(CLONE / TARGET) + ".meta"), Path(str(PROJECT / TARGET) + ".meta"))
        print("Copied the verified traffic preview to the local Unity project.", flush=True)


if __name__ == "__main__":
    main()
