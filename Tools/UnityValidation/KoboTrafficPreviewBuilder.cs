// Offline authoring for the local Kobo traffic preview.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIT.Riding;
using AIT.Riding.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class KoboTrafficPreviewBuilder
{
    const string Root = "Assets/MotorSimulatorAssets/BuildingComparisonLocal/TrafficPreview";
    const string Output = "Personal/KoboTraffic";
    const string Models = Root + "/Models/";
    [Serializable] sealed class ScooterFit
    {
        public Vector3 front, rear, steering, grip;
        public float nose, width, length, radius, wheelbase;
    }
    static readonly Dictionary<string, Material> palette = new Dictionary<string, Material>();
    static readonly List<string> report = new List<string>();

    public static void Run()
    {
        Directory.CreateDirectory(Root + "/Materials");
        Directory.CreateDirectory(Output);
        Directory.CreateDirectory("Personal/VehiclePreview");
        AssetDatabase.Refresh();
        foreach (var model in new[] { "Sedan", "SUV", "HelmetedRider", "CommuterScooter" })
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(Models + model + ".fbx");
            importer.isReadable = true;
            importer.useFileScale = true;
            importer.globalScale = 1;
            importer.importAnimation = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.SaveAndReimport();
        }

        EditorSceneManager.OpenScene(RideScenes.StreetscapeKoboPath);
        var session = Object.FindFirstObjectByType<RideTestSession>();
        var runtime = Object.FindFirstObjectByType<CityRoadRuntime>();
        var original = Object.FindObjectsByType<CityTrafficVehicle>(FindObjectsSortMode.None).OrderBy(v => v.name).ToArray();
        Require(original.Length == 2, "Expected the two Kobo comparison traffic routes");
        var presentation = session.GetComponent<RidePresentation>();
        var fit = JsonUtility.FromJson<ScooterFit>(File.ReadAllText(Models + "ScooterFit.json"));
        InstallPlayerScooter(presentation, fit);
        SetStreetEyeDefaults(session);

        var sedan = MakeCar("Sedan", original[0], new Vector3(4.875f, 0, -64));
        var suv = MakeCar("SUV", original[1], new Vector3(1.625f, 0, -68));
        var bike = MakeScooter(original[0], fit, new Vector3(4.875f, 0, -78));
        foreach (var vehicle in original) Object.DestroyImmediate(vehicle.gameObject);
        Object.DestroyImmediate(GameObject.Find("Shared parked car and occluder"));

        var sidewalk = KoboPedestrianBuilder.Install(Root, runtime, report).gameObject;

        session.course.stages[0].name = "Kobo 交通街景";
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            Require(r.sharedMaterials.All(m => m && m.shader && m.shader.name != "Hidden/InternalErrorShader"), "Missing material on " + r.name);
        Require(bike.steeringAssembly && bike.wheels[0].parent == bike.steeringAssembly, "Scooter assembly is incomplete");
        Require(sedan.wheelbase > 2 && suv.wheelbase > 2, "Car wheel axes are reversed or at the wrong scale");
        foreach (var vehicle in new[] { sedan, suv, bike })
            Require(session.roadNetwork.IsDrivable(vehicle.transform.position, .2f), "Traffic spawn outside asphalt");
        CheckStreetScale(session, sedan, suv, bike, sidewalk);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(session.gameObject.scene, RideScenes.KoboTrafficPath);
        File.WriteAllLines(Output + "/authoring.txt", report);
        Capture(session, presentation, bike, sedan, suv, sidewalk);
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static void SetStreetEyeDefaults(RideTestSession session)
    {
        // Keep this street comparison independent from the other riding scenes
        // and from the user's saved seat and hardware profiles.
        var settings = Object.Instantiate(session.settingsTemplate);
        settings.seatedEyePosition = new Vector3(0, 1.38f, -.28f);
        settings.desktopLookPitch = 8;
        const string path = Root + "/StreetRideSettings.asset";
        var stored = AssetDatabase.LoadAssetAtPath<RideTestSettings>(path);
        if (stored)
        {
            EditorUtility.CopySerialized(settings, stored);
            EditorUtility.SetDirty(stored);
            Object.DestroyImmediate(settings);
        }
        else
        {
            stored = settings;
            AssetDatabase.CreateAsset(stored, path);
        }
        session.settingsTemplate = stored;
    }

    static void CheckStreetScale(RideTestSession session, CityTrafficVehicle sedan, CityTrafficVehicle suv,
                                 CityTrafficVehicle scooter, GameObject adult)
    {
        float eye = session.settingsTemplate.seatedEyePosition.y;
        float sedanRoof = sedan.GetComponent<BoxCollider>().size.y;
        float suvRoof = suv.GetComponent<BoxCollider>().size.y;
        float helmet = scooter.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "Helmet").bounds.max.y;
        Require(eye < sedanRoof && sedanRoof < suvRoof && helmet <= suvRoof + .02f, "Street scale does not match the reference heights");
        var road = session.roadNetwork;
        report.Add($"Street scale: eye {eye:F2} m, sedan {sedanRoof:F2} m, SUV {suvRoof:F2} m, seated helmet {helmet:F2} m");
        report.Add($"Street cross section: {road.laneWidth:F2} m lane, {2 * road.HalfRoad:F2} m asphalt, {road.sidewalkWidth:F2} m sidewalk, {road.curbHeight:F2} m curb");
        var frontage = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
            .First(t => t.name == "tb_shop_house_3f_01");
        var facade = frontage.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled).ToArray();
        float roof = facade.Max(r => r.bounds.max.y);
        float foundation = facade.Min(r => r.bounds.min.y);
        report.Add($"Visual references: adult mesh {KoboPedestrianBuilder.BodyBounds(adult).size.y:F2} m; 3-floor shop house {roof - foundation:F2} m including foundation");
    }

    static GameObject InstantiateAt(string path, string name, Transform parent)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Require(source, "Missing asset " + path);
        var instance = Object.Instantiate(source, parent);
        instance.name = name;
        return instance;
    }

    static Material Matt(string name, string hex, float smoothness = .10f, bool brake = false)
    {
        if (palette.TryGetValue(name, out var found)) return found;
        string path = Root + "/Materials/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        Require(ColorUtility.TryParseHtmlString(hex, out var colour), "Invalid colour " + hex);
        mat.SetColor("_BaseColor", colour);
        mat.SetFloat("_Metallic", 0);
        mat.SetFloat("_Smoothness", smoothness);
        if (brake)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.black);
        }
        EditorUtility.SetDirty(mat);
        palette.Add(name, mat);
        return mat;
    }

    static void Paint(Renderer renderer, Material material)
        => renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => material).ToArray();

    static Material ScooterMaterial(string name, bool npc)
    {
        if (name.Contains("BrakeLight")) return Matt("Brake", "#B23A3F", .24f, true);
        if (name.EndsWith("_Amber")) return Matt("Amber", "#D9A62E", .2f);
        if (name.EndsWith("_Headlamp")) return Matt("Headlamp", "#F3E9D6", .27f);
        if (name.EndsWith("_Paint"))
            return npc ? Matt("Ochre shell", "#CFAB66", .19f) : Matt("Teal shell", "#4D9F94", .19f);
        if (name.Contains("_Grip") || name.EndsWith("_Rubber")) return Matt("Rubber", "#292A2B");
        if (name.EndsWith("_Dash") || name.EndsWith("_Seat") || name.EndsWith("_Trim")) return Matt("Trim", "#34353A");
        if (name.StartsWith("Glass")) return Matt("Windows", "#343F4C", .26f);
        if (name.EndsWith("_Metal") || name.EndsWith("_Hub")) return Matt("Engine and fork", "#555D61", .19f);
        return Matt("Metal", "#879599", .22f);
    }

    static MeshRenderer[] InstallScooter(Transform body, Transform steering, Transform front, Transform rear, bool npc)
    {
        var prefab = InstantiateAt(Models + "CommuterScooter.fbx", "TDAL commuter scooter", body);
        var renderers = prefab.GetComponentsInChildren<MeshRenderer>();
        foreach (var renderer in renderers)
        {
            var name = renderer.name;
            Transform parent = name.StartsWith("FrontWheel_") ? front : name.StartsWith("RearWheel_") ? rear :
                name.StartsWith("Steer_") || name.StartsWith("Glass") ? steering : prefab.transform;
            renderer.transform.SetParent(parent, true);
            Paint(renderer, ScooterMaterial(name, npc));
            if (!npc) renderer.gameObject.layer = 2;
        }
        Require(renderers.Any(r => r.name == "Body_Paint" && r.transform.parent == prefab.transform) &&
                renderers.Any(r => r.name == "Steer_Paint" && r.transform.parent == steering) &&
                renderers.Any(r => r.name == "FrontWheel_Rubber" && r.transform.parent == front) &&
                renderers.Any(r => r.name == "RearWheel_Rubber" && r.transform.parent == rear), "Scooter parts were not split correctly");
        foreach (var (name, wheel) in new[] { ("FrontWheel_Rubber", front), ("RearWheel_Rubber", rear) })
        {
            var tyre = renderers.Single(r => r.name == name);
            Require(Vector3.Distance(tyre.bounds.center, wheel.position) < .01f, "Misaligned " + name + " axle");
        }
        return renderers;
    }

    static void InstallPlayerScooter(RidePresentation presentation, ScooterFit fit)
    {
        var body = presentation.session.vehicleVisual;
        var steering = presentation.handlebars;
        var mirrors = body.GetComponentsInChildren<RideMirror>(true);
        var handContainer = steering.Find("Static rider hands");
        Require(handContainer && handContainer.childCount == 2 && mirrors.Length == 2, "Player cockpit is incomplete");
        var old = body.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.StartsWith("Scooter_")).ToArray();
        var hands = handContainer.Cast<Transform>().Select(hand =>
        {
            bool left = hand.name.StartsWith("Left");
            var grip = old.Single(r => r.name == (left ? "Scooter_rubber_handgrip_1" : "Scooter_rubber_handgrip_-1"));
            return (hand, left, offset: body.InverseTransformPoint(hand.position) - body.InverseTransformPoint(grip.bounds.center));
        }).ToArray();
        foreach (var renderer in old) Object.DestroyImmediate(renderer.gameObject);
        var retired = body.Find("Innerscene CC0 Urban Scooter");
        if (retired) Object.DestroyImmediate(retired.gameObject);
        steering.localPosition = fit.steering;
        presentation.frontWheel.localPosition = fit.front - fit.steering;
        presentation.rearWheel.localPosition = fit.rear;
        presentation.wheelRadius = fit.radius;
        var parts = InstallScooter(body, steering, presentation.frontWheel, presentation.rearWheel, false);
        foreach (var (hand, left, offset) in hands)
        {
            var grip = parts.Single(r => r.name == (left ? "Steer_GripLeft" : "Steer_GripRight"));
            hand.position = grip.bounds.center + body.TransformVector(offset);
        }
        foreach (var mirror in mirrors)
        {
            // Reuse the rear-view cameras. The shaped TDAL mesh is the screen
            // so the reflection has the same silhouette as the mirror.
            var glass = parts.Single(r => r.name == (mirror.name.StartsWith("Left") ? "GlassLeft" : "GlassRight"));
            Object.DestroyImmediate(mirror.screen);
            Object.DestroyImmediate(mirror.GetComponent<MeshFilter>());
            mirror.screen = glass;
            mirror.mirrorCamera.transform.position = glass.bounds.center;
        }
        presentation.speedDisplay.transform.SetPositionAndRotation(body.TransformPoint(new Vector3(0, 1.068f, .244f)),
            body.rotation * Quaternion.Euler(62, 0, 0));
        int triangles = parts.Select(r => r.GetComponent<MeshFilter>().sharedMesh)
            .Sum(m => Enumerable.Range(0, m.subMeshCount).Sum(i => (int)m.GetIndexCount(i) / 3));
        report.Add($"TDAL scooter: {fit.length:F2} m long, {fit.width:F2} m wide with mirrors, {fit.wheelbase:F2} m wheelbase; {triangles} triangles");
    }

    static Transform Pivot(string name, Transform parent, Vector3 localPosition)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = localPosition;
        return t;
    }

    static CityTrafficVehicle MakeCar(string name, CityTrafficVehicle route, Vector3 start)
    {
        var root = Pivot(name, route.transform.parent, Vector3.zero);
        var model = InstantiateAt(Root + "/Models/" + name + ".fbx", "Coachwork and wheels", root);
        var wheels = new List<Transform>();
        var renderers = model.GetComponentsInChildren<MeshRenderer>();
        var originalMaterials = renderers.SelectMany(r => r.sharedMaterials).Select(m => m.name).Distinct().ToArray();
        Require(originalMaterials.Any(m => m.Split('.')[0] == (name == "Sedan" ? "Blue" : "White")),
            name + " coachwork material not found: " + string.Join(", ", originalMaterials));
        model.transform.position += Vector3.up * (root.position.y - renderers.Min(r => r.bounds.min.y));
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        int triangles = renderers.Select(r => r.GetComponent<MeshFilter>().sharedMesh)
            .Sum(m => Enumerable.Range(0, m.subMeshCount).Sum(i => (int)m.GetIndexCount(i) / 3));
        report.Add($"{name} bounds L/W/H: {bounds.size.z:F2}/{bounds.size.x:F2}/{bounds.size.y:F2} m; {triangles} triangles");
        foreach (var r in renderers)
        {
            r.sharedMaterials = r.sharedMaterials.Select(m =>
            {
                switch (m.name.Split('.')[0])
                {
                    case "Blue": return Matt("Indigo sedan", "#556D90", .22f);
                    case "White": return Matt("Cream SUV", "#E6DBCA", .22f);
                    case "Windows": return Matt("Windows", "#343F4C", .26f);
                    case "Grey": return Matt("Metal", "#879599", .22f);
                    case "Headlights": return Matt("Headlamp", "#F3E9D6", .27f);
                    case "TailLights":
                    case "BrakeLight": return Matt("Brake", "#B23A3F", .24f, true);
                    default: return Matt("Rubber", "#292A2B");
                }
            }).ToArray();
            if (!r.name.StartsWith("Front") && !r.name.StartsWith("RearAxle")) continue;
            var pivot = Pivot(r.name + " pivot", root, root.InverseTransformPoint(r.bounds.center));
            r.transform.SetParent(pivot, true);
            wheels.Add(pivot);
        }
        Require(wheels.Count == 3, "Expected three axle meshes for " + name);
        float rear = wheels.Single(w => w.name.Contains("Rear")).localPosition.z;
        float front = wheels.Where(w => w.name.Contains("Front")).Average(w => w.localPosition.z);
        float radius = wheels[0].GetComponentInChildren<MeshRenderer>().bounds.extents.y;
        float nose = renderers.Max(r => r.bounds.max.z) - root.position.z;
        var box = root.gameObject.AddComponent<BoxCollider>();
        box.center = root.InverseTransformPoint(bounds.center);
        box.size = bounds.size;
        return AttachRoute(root, route, wheels.ToArray(), rear, front, radius, nose, start, 20);
    }

    static CityTrafficVehicle MakeScooter(CityTrafficVehicle route, ScooterFit fit, Vector3 start)
    {
        var root = Pivot("Scooter with rider", route.transform.parent, Vector3.zero);
        var bank = Pivot("Scooter and rider lean", root, Vector3.zero);
        var steering = Pivot("Scooter steering", bank, fit.steering);
        var front = Pivot("Front wheel", steering, fit.front - fit.steering);
        var rear = Pivot("Rear wheel", bank, fit.rear);
        InstallScooter(bank, steering, front, rear, true);
        var helmeted = InstantiateAt(Root + "/Models/HelmetedRider.fbx", "Helmeted rider", bank);
        foreach (var r in helmeted.GetComponentsInChildren<MeshRenderer>())
        {
            if (r.name == "Helmet") Paint(r, Matt("Cream helmet", "#EFE6D7", .18f));
            else if (r.name == "Visor") Paint(r, Matt("Windows", "#343F4C", .26f));
            else if (r.name.StartsWith("Shoe") || r.name.StartsWith("Glove")) Paint(r, Matt("Trim", "#34353A"));
            else
                r.sharedMaterials = r.sharedMaterials.Select(m => m.name.Contains("Shirt") ? Matt("Rider jacket", "#4D7774") :
                    m.name.Contains("Skin") ? Matt("Rider skin", "#BF8965") : Matt("Trousers", "#3C4B58")).ToArray();
        }
        var riderBounds = helmeted.GetComponentsInChildren<Renderer>();
        float riderTop = riderBounds.Max(r => r.bounds.max.y) - root.position.y;
        Require(riderTop > 1.5f && riderTop < 1.80f, "Rider pose scale is wrong");
        report.Add($"Rider helmet top: {riderTop:F2} m above the road");

        var box = root.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0, .65f, 0);
        box.size = new Vector3(fit.width, 1.30f, fit.length);
        var bike = AttachRoute(root, route, new[] { front, rear }, fit.rear.z, fit.front.z, fit.radius, fit.nose, start, 19);
        bike.steeringAssembly = steering;
        bike.leaningVisual = bank;
        return bike;
    }

    static CityTrafficVehicle AttachRoute(Transform root, CityTrafficVehicle original, Transform[] wheels, float rear, float front, float radius, float nose, Vector3 start, float speed)
    {
        var vehicle = root.gameObject.AddComponent<CityTrafficVehicle>();
        vehicle.session = original.session;
        vehicle.wheels = wheels;
        vehicle.pathPoints = original.pathPoints;
        vehicle.pathYaw = original.pathYaw;
        vehicle.pathCurvature = original.pathCurvature;
        vehicle.rearAxleZ = rear;
        vehicle.wheelbase = front - rear;
        vehicle.wheelRadius = radius;
        vehicle.cruiseKph = speed;
        vehicle.brakeEmission = new Color(.8f, .07f, .035f);
        vehicle.turns = original.turns.Select(t => new CityTrafficTurn
        {
            junction = t.junction, direction = t.direction, start = t.start, end = t.end, approachYaw = t.approachYaw,
            stop = t.stop + 2.56f - original.rearAxleZ - (nose - rear)
        }).ToArray();
        Vector3 rearTarget = start + Vector3.forward * rear;
        float best = float.PositiveInfinity;
        float distance = 0;
        for (int i = 1; i < vehicle.pathPoints.Length; i++)
        {
            var from = vehicle.pathPoints[i - 1];
            var to = vehicle.pathPoints[i];
            float error = (rearTarget - from).sqrMagnitude;
            if (error < best) { best = error; vehicle.initialProgress = distance; }
            distance += Vector3.Distance(from, to);
        }
        vehicle.Initialise();
        report.Add($"{root.name}: wheelbase={vehicle.wheelbase:F2} m, radius={radius:F2} m, spawn={root.position:F2}, position error={Mathf.Sqrt(best):F2} m");
        return vehicle;
    }

    static void Capture(RideTestSession session, RidePresentation presentation, CityTrafficVehicle bike,
                        CityTrafficVehicle sedan, CityTrafficVehicle suv, GameObject adult)
    {
        if (presentation.pausedPanel) presentation.pausedPanel.SetActive(false);
        var temporary = new List<Object>();
        session.headCamera.enabled = false;
        foreach (var pedestrian in Object.FindObjectsByType<CityPedestrian>(FindObjectsSortMode.None).Where(p => p.useSidewalkRoute))
            pedestrian.walkClip.SampleAnimation(pedestrian.animationPlayer.gameObject, .27f);
        session.vehicleRoot.position = bike.transform.position - Vector3.forward * 7;
        foreach (var mirror in session.GetComponentsInChildren<RideMirror>(true))
        {
            mirror.enabled = false;
            var rt = new RenderTexture(512, 512, 24);
            temporary.Add(rt);
            rt.Create();
            var material = new Material(mirror.screenShader);
            temporary.Add(material);
            material.SetTexture("_BaseMap", rt);
            material.SetTextureScale("_BaseMap", new Vector2(-1, 1));
            material.SetTextureOffset("_BaseMap", Vector2.right);
            mirror.screen.sharedMaterial = material;
            mirror.mirrorCamera.GetUniversalAdditionalCameraData().allowXRRendering = false;
            RenderPipeline.SubmitRenderRequest(mirror.mirrorCamera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
        }
        var eye = session.vehicleRoot.TransformPoint(session.settingsTemplate.seatedEyePosition);
        Shot("rider", eye, eye + Quaternion.Euler(session.settingsTemplate.desktopLookPitch, 0, 0) * Vector3.forward * 25, 85);
        session.vehicleVisual.gameObject.SetActive(false);
        var p = bike.transform.position;
        Shot("scooter-side", p + new Vector3(5.4f, 1.63f, -2), p + Vector3.up, 46);
        Shot("scooter-front", p + new Vector3(-2.8f, 1.65f, 3.8f), p + Vector3.up, 46);
        bike.steeringAssembly.localRotation = Quaternion.Euler(0, 21, 0);
        Shot("scooter-steered", p + new Vector3(-2.8f, 1.65f, 3.8f), p + Vector3.up, 46);
        bike.steeringAssembly.localRotation = Quaternion.identity;
        Shot("traffic-and-facades", new Vector3(-2.65f, 1.95f, -86), new Vector3(5.5f, 1.4f, -70), 73);
        Shot("cars", new Vector3(8.2f, 1.75f, -72.4f), new Vector3(3.4f, .84f, -65.6f), 63);
        Shot("cars-front", new Vector3(7.6f, 1.7f, -57), new Vector3(2.65f, .94f, -65.7f), 60);
        var walkPosition = adult.transform.position;
        Shot("pedestrian-sidewalk", walkPosition + new Vector3(-3.2f, 1.42f, 2.9f), walkPosition + Vector3.up * .90f, 45);
        // Sample actual walk poses for the crossing view without altering the saved routes.
        var crossingActors = Object.FindObjectsByType<CityPedestrian>(FindObjectsSortMode.None).Where(ped => !ped.useSidewalkRoute).OrderBy(ped => ped.name).ToArray();
        for (int i = 0; i < crossingActors.Length; i++)
        {
            var ped = crossingActors[i];
            ped.transform.SetPositionAndRotation(new Vector3(-2 + i * 3, 0, -session.roadNetwork.crossingDistance), Quaternion.Euler(0, 90, 0));
            ped.walkClip.SampleAnimation(ped.animationPlayer.gameObject, .23f + i * .29f);
        }
        var red = AssetDatabase.LoadAssetAtPath<Material>("Assets/MotorSimulatorAssets/GridCityGenerated/SignalRed.mat");
        foreach (var signal in Object.FindObjectsByType<CitySignalView>(FindObjectsSortMode.None).Where(s => s.node == 4))
            signal.lamps.sharedMaterial = red;
        Shot("pedestrian-crossing", new Vector3(4.875f, 1.38f, -session.roadNetwork.crossingDistance - 8), new Vector3(-.7f, 1.20f, -session.roadNetwork.crossingDistance), 56);
        // A temporary lineup at the same distance removes the depth ambiguity
        // from the first-person height comparison. These placements happen only after saving.
        const float lineupZ = -78;
        sedan.transform.SetPositionAndRotation(new Vector3(-1.625f, 0, lineupZ), Quaternion.identity);
        suv.transform.SetPositionAndRotation(new Vector3(1.625f, 0, lineupZ), Quaternion.identity);
        bike.transform.SetPositionAndRotation(new Vector3(4.875f, 0, lineupZ), Quaternion.identity);
        var adultPosition = adult.transform.position;
        adultPosition.z = lineupZ;
        adult.transform.position = adultPosition;
        var level = new Vector3(2.7f, session.settingsTemplate.seatedEyePosition.y, -88);
        Shot("scale-eye-level", level, level + Vector3.forward * 10, 52);
        foreach (var item in temporary) Object.DestroyImmediate(item);
    }

    static void Shot(string name, Vector3 from, Vector3 at, float fov)
        => UrbanVehiclePreviewBuilder.Capture("../KoboTraffic/" + name + ".png", from, at, fov);
}
