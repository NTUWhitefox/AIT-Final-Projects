// Offline asset adaptation and scene authoring. Keep outside the working project's Assets.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using AIT.Riding;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class StreetscapeComparisonBuilder
{
    public const string Root = "Assets/MotorSimulatorAssets/BuildingComparisonLocal";
    public const string Output = "Personal/BuildingComparison";
    const string Existing = "Assets/MotorSimulatorAssets/GridCityGenerated";
    [Serializable] class PashaMaterial { public string name, texture; public float[] colour; }
    [Serializable] class PashaManifest { public PashaMaterial[] materials; }
    static readonly List<string> report = new List<string>();
    static readonly Dictionary<string, Material> pashaMaterials = new Dictionary<string, Material>();
    static readonly Dictionary<string, Material> koboMaterials = new Dictionary<string, Material>();
    static CityRoadNetwork network;
    static Mesh lens;

    public static void Run()
    {
        Directory.CreateDirectory(Root + "/Generated"); Directory.CreateDirectory(Root + "/Prefabs");
        Directory.CreateDirectory(Root + "/Scenes"); Directory.CreateDirectory(Output); Directory.CreateDirectory("Personal/VehiclePreview"); AssetDatabase.Refresh();
        PrepareMaterials(); PreparePrefabs(); BuildRoadSource();
        foreach (string variant in new[] { "Pasha", "Kobo" }) BuildScene(variant);
        File.WriteAllLines(Output + "/authoring.txt", report); AssetDatabase.SaveAssets();
    }
    static void Save(UnityEngine.Object asset, string path)
    {
        var old = AssetDatabase.LoadMainAssetAtPath(path);
        if (old) { EditorUtility.CopySerialized(asset, old); UnityEngine.Object.DestroyImmediate(asset); EditorUtility.SetDirty(old); }
        else AssetDatabase.CreateAsset(asset, path);
    }
    static Material Material(string name, string texture, Color colour, bool alpha = false)
    {
        string path = Root + "/Generated/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", colour); material.SetColor("_Color", colour);
        material.SetFloat("_Smoothness", .08f); material.SetFloat("_Metallic", 0); material.SetFloat("_Cull", 0); material.enableInstancing = true;
        if (texture != null)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texture); if (!tex) throw new Exception("Missing texture: " + texture);
            material.SetTexture("_BaseMap", tex); material.SetTexture("_MainTex", tex);
        }
        if (alpha)
        {
            material.SetFloat("_Surface", 1); material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.renderQueue = 3000;
        }
        EditorUtility.SetDirty(material); return material;
    }
    static void TextureSettings(string path, bool palette)
    {
        var t = AssetImporter.GetAtPath(path) as TextureImporter; if (!t) throw new Exception(path);
        t.textureShape = TextureImporterShape.Texture2D; t.textureType = TextureImporterType.Default; t.sRGBTexture = true;
        t.filterMode = palette ? FilterMode.Point : FilterMode.Trilinear; t.mipmapEnabled = !palette; t.anisoLevel = palette ? 0 : 8;
        t.textureCompression = TextureImporterCompression.Uncompressed; t.maxTextureSize = 2048; t.wrapMode = palette ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        t.SaveAndReimport();
    }
    static void PrepareMaterials()
    {
        foreach (var file in Directory.GetFiles(Root + "/Pasha/Textures")) if (!file.EndsWith(".meta")) TextureSettings(file.Replace('\\', '/'), false);
        var manifest = JsonUtility.FromJson<PashaManifest>(File.ReadAllText(Root + "/Pasha/manifest.json"));
        foreach (var source in manifest.materials)
        {
            bool textured = !string.IsNullOrEmpty(source.texture);
            var colour = textured ? Color.white : new Color(source.colour[0], source.colour[1], source.colour[2]);
            pashaMaterials[source.name] = Material("Pasha_" + source.name, textured ? Root + "/Pasha/Textures/" + source.texture : null, colour);
        }
        string palette = Root + "/Kobo/palette/palette32_tone.png";
        foreach (var file in Directory.GetFiles(Root + "/Kobo/palette", "*.png")) TextureSettings(file.Replace('\\', '/'), file.EndsWith("palette32_tone.png"));
        foreach (string name in new[] { "palette32", "palette32_WindowGlow", "Kobo_Emissive", "Kobo_Glass", "Poster_Paper", "Kobo_Shadow" })
        {
            string texture = name == "Poster_Paper" ? Root + "/Kobo/palette/poster_paper_blank.png" : name == "Kobo_Shadow" ? Root + "/Kobo/palette/shadow_blob.png" : palette;
            var material = Material("Kobo_" + name, texture, name == "Kobo_Glass" ? new Color(1, 1, 1, .22f) : Color.white, name == "Kobo_Glass" || name == "Kobo_Shadow");
            if (name == "Kobo_Emissive") { material.EnableKeyword("_EMISSION"); material.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(palette)); material.SetColor("_EmissionColor", Color.white * .15f); }
            koboMaterials[name] = material;
        }
    }
    static void PreparePrefabs()
    {
        foreach (string variant in new[] { "Pasha", "Kobo" })
        {
            Directory.CreateDirectory(Root + "/Prefabs/" + variant); AssetDatabase.Refresh();
            foreach (string file in Directory.GetFiles(Root + "/" + variant + "/" + (variant == "Pasha" ? "Models" : "models"), "*.fbx").OrderBy(p => p))
            {
                string path = file.Replace('\\', '/'); var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.useFileScale = true; importer.globalScale = 1; importer.importNormals = ModelImporterNormals.Import;
                importer.importAnimation = false; importer.importCameras = false; importer.importLights = false; importer.isReadable = true;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
                importer.SaveAndReimport();
                var root = new GameObject(Path.GetFileNameWithoutExtension(path));
                var model = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path)); model.transform.SetParent(root.transform, false);
                var palette = variant == "Pasha" ? pashaMaterials : koboMaterials;
                foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer.name.StartsWith("UCX_") || renderer.name.EndsWith("_shadow")) { renderer.gameObject.SetActive(false); continue; }
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(source => {
                        if (!source || !palette.TryGetValue(source.name, out var replacement)) throw new Exception("Unmapped material: " + path + "/" + source?.name);
                        return replacement;
                    }).ToArray();
                }
                var b = Bounds(root); if (b.size.y < .2f || b.size.y > 90 || b.size.x > 60 || b.size.z > 60) throw new Exception("Implausible imported dimensions: " + path + " " + b.size);
                // The source foundations can extend below the plot level. Keep that intended ground origin.
                var collision = root.AddComponent<BoxCollider>(); collision.center = b.center; collision.size = b.size;
                string target = Root + "/Prefabs/" + variant + "/" + root.name + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(root, target);
                report.Add(variant + " " + root.name + ": " + b.size.ToString("F2") + " m, " + Triangles(root) + " triangles, " + root.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).SelectMany(r => r.sharedMaterials).Distinct().Count() + " materials.");
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
    static Bounds Bounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray(); if (renderers.Length == 0) throw new Exception("No visible meshes: " + root.name);
        var b = renderers[0].bounds; foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds); return b;
    }
    static int Triangles(GameObject root) => root.GetComponentsInChildren<MeshFilter>().Where(f => f.GetComponent<MeshRenderer>().enabled).Sum(f => (int)f.sharedMesh.GetIndexCount(0) / 3 + Enumerable.Range(1, f.sharedMesh.subMeshCount - 1).Sum(i => (int)f.sharedMesh.GetIndexCount(i) / 3));
    static void BuildRoadSource()
    {
        var n = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<CityRoadNetwork>(Existing + "/RoadNetwork.asset"));
        n.name = "Streetscape common road"; n.networkId = "streetscape-comparison-01"; n.version = "1"; n.origin = new Vector3(-192, 0, -192); n.count = 3;
        var nodes = new List<CityRoadNode>(); var links = new List<CityRoadLink>(); var lanes = new List<CityLaneDefinition>();
        for (int z = 0; z < 3; z++) for (int x = 0; x < 3; x++)
        {
            int arms = (z < 2 ? 1 : 0) | (x < 2 ? 2 : 0) | (z > 0 ? 4 : 0) | (x > 0 ? 8 : 0);
            nodes.Add(new CityRoadNode { id = $"C{x}_{z}", cell = new Vector2Int(x, z), position = new Vector3((x - 1) * 128, 0, (z - 1) * 128), arms = arms, boundary = GridRoadGeometry.Boundary(n, arms) });
        }
        n.nodes = nodes.ToArray();
        for (int z = 0; z < 3; z++) for (int x = 0; x < 3; x++)
        {
            int from = n.Index(x, z);
            foreach (int to in new[] { x < 2 ? n.Index(x + 1, z) : -1, z < 2 ? n.Index(x, z + 1) : -1 })
            {
                if (to < 0) continue; links.Add(new CityRoadLink { id = $"L{from}_{to}", from = from, to = to });
                foreach (bool reverse in new[] { false, true }) for (int lane = 0; lane < 2; lane++)
                {
                    int a = reverse ? to : from, b = reverse ? from : to; int arm = n.ArmFromTo(a, b);
                    lanes.Add(new CityLaneDefinition { id = $"D{a}_{b}_{lane}", from = a, to = b, lane = lane, start = n.Port(a, arm, lane, false), end = n.Port(b, (arm + 2) % 4, lane, true) });
                }
            }
        }
        n.links = links.ToArray(); n.lanes = lanes.ToArray(); n.routeNodes = new[] { 0, 3, 6, 7, 8, 5, 2, 1 }; n.routeLanes = n.routeNodes.Select(_ => 1).ToArray(); GridCityBuilder.BuildMovements(n);
        Save(n, Root + "/Generated/RoadNetwork.asset"); network = AssetDatabase.LoadAssetAtPath<CityRoadNetwork>(Root + "/Generated/RoadNetwork.asset");
        var map = new GridRoadGeometry.Map(network.origin, 1, 384);
        foreach (var node in network.nodes) GridRoadGeometry.Junction(map, network, node);
        foreach (var link in network.links)
        {
            var direction = (network.nodes[link.to].position - network.nodes[link.from].position).normalized;
            GridRoadGeometry.Straight(map, network, network.nodes[link.from].position + direction * network.halfJunction, network.nodes[link.to].position - direction * network.halfJunction);
        }
        foreach (var surface in map.cells[0]) Save(surface.Value.Mesh(surface.Key), Root + "/Generated/Road_" + surface.Key + ".asset");
        foreach (var lane in network.lanes) for (int i = 0; i <= 20; i++) if (!network.IsDrivable(Vector3.Lerp(lane.start, lane.end, i / 20f), .3f)) throw new Exception("Road connection left asphalt");
        report.Add("Shared road: 9 junctions, 12 links; same road mesh, lighting, rider, cars and pedestrians in both versions. Approximately 100 m frontage per block; buildings retain their own widths and massing, not a controlled fidelity experiment.");
    }
    static void BuildScene(string variant)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/CityCorners.unity");
        var session = UnityEngine.Object.FindFirstObjectByType<RideTestSession>();
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) if (root != session.gameObject) UnityEngine.Object.DestroyImmediate(root);
        var world = new GameObject(variant + " streetscape"); var course = world.AddComponent<RideTestCourse>(); session.course = course; session.worldStreamer = null; session.roadNetwork = network;
        course.stages = new[] { new RideTestStage { name = variant + " 街景 / 自由騎行", freeRoam = true, spawnPosition = new Vector3(4.875f, 0, -92), spawnYaw = 0, centreline = Array.Empty<Vector3>() } };
        course.streetSegmentEndpoints = network.links.SelectMany(l => new[] { network.nodes[l.from].position, network.nodes[l.to].position }).ToArray();
        session.vehicleRoot.SetPositionAndRotation(course.stages[0].spawnPosition, Quaternion.identity);
        var ground = Cube("Ground", world.transform, new Vector3(0, -.1f, 0), new Vector3(384, .15f, 384), AssetDatabase.LoadAssetAtPath<Material>(Existing + "/Land.mat"));
        foreach (var mesh in Directory.GetFiles(Root + "/Generated", "Road_*.asset"))
        {
            string key = Path.GetFileNameWithoutExtension(mesh).Substring(5); var obj = new GameObject(key); obj.transform.SetParent(world.transform);
            obj.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(mesh.Replace('\\', '/'));
            var r = obj.AddComponent<MeshRenderer>(); r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Existing + "/" + key + ".mat"); r.shadowCastingMode = ShadowCastingMode.Off;
        }
        var runtime = world.AddComponent<CityRoadRuntime>(); runtime.session = session; runtime.network = network; runtime.junctions = new CityJunction[9];
        for (int i = 0; i < 9; i++)
        {
            var obj = new GameObject(network.nodes[i].id); obj.transform.SetParent(world.transform); obj.transform.position = network.nodes[i].position;
            var j = obj.AddComponent<CityJunction>(); j.session = session; j.signalControlled = true; runtime.junctions[i] = j; Signals(obj.transform, i);
        }
        Traffic(runtime); Pedestrians(runtime); Buildings(world.transform, variant);
        var parked = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MotorSimulatorAssets/CityPreviewGenerated/Car26_URP.prefab"));
        parked.name = "Shared parked car and occluder"; parked.transform.SetParent(world.transform); parked.transform.SetPositionAndRotation(new Vector3(-8.6f, 0, -37), Quaternion.Euler(0, 180, 0));
        UnityEngine.Object.DestroyImmediate(parked.GetComponent<PreviewRoadVehicle>());
        UrbanVehiclePreviewBuilder.Lighting(); RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 180; RenderSettings.fogEndDistance = 360;
        RenderSettings.fogColor = new Color(.61f, .69f, .75f);
        string path = Root + "/Scenes/Streetscape" + variant + ".unity"; EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), path); AssetDatabase.SaveAssets();
        ValidateScene(session, variant);
        Capture(variant, session);
    }
    static GameObject Cube(string name, Transform parent, Vector3 position, Vector3 size, Material material)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube); obj.name = name; obj.transform.SetParent(parent); obj.transform.position = position; obj.transform.localScale = size;
        obj.GetComponent<MeshRenderer>().sharedMaterial = material; UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>()); return obj;
    }
    static void Signals(Transform parent, int node)
    {
        if (!lens) lens = AssetDatabase.LoadAssetAtPath<Mesh>(Existing + "/SignalLenses_v2.asset");
        var metal = AssetDatabase.LoadAssetAtPath<Material>(Existing + "/SignalMetal.mat");
        for (int arm = 0; arm < 4; arm++) if (network.HasArm(node, arm))
        {
            var outward = CityRoadNetwork.Direction(arm); var right = CityRoadNetwork.Right(-outward);
            var pole = network.nodes[node].position + outward * 11.8f + right * 8.25f;
            var centre = network.nodes[node].position + outward * 11.8f + right * network.LaneOffset(1) + Vector3.up * 4.6f;
            var rotation = Quaternion.LookRotation(outward);
            Cube("Pole", parent, pole + Vector3.up * 2.3f, new Vector3(.14f, 4.6f, .14f), metal);
            var beam = Cube("Beam", parent, (pole + Vector3.up * 4.6f + centre) / 2, new Vector3(3.5f, .12f, .12f), metal); beam.transform.rotation = rotation;
            var housing = Cube("Housing", parent, centre - outward * .08f, new Vector3(1.28f, .46f, .24f), metal); housing.transform.rotation = rotation;
            var obj = new GameObject("Signal " + arm); obj.transform.SetParent(parent); obj.transform.SetPositionAndRotation(centre + outward * .05f, rotation);
            obj.AddComponent<MeshFilter>().sharedMesh = lens; var renderer = obj.AddComponent<MeshRenderer>(); renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Existing + "/" + (arm % 2 == 0 ? "SignalGreen" : "SignalRed") + ".mat");
            var signal = obj.AddComponent<CitySignalView>(); signal.node = node; signal.northSouth = arm % 2 == 0; signal.lamps = renderer;
        }
    }
    static void Buildings(Transform parent, string variant)
    {
        string[] names = variant == "Pasha" ? new[] { "Pasha_Block_04", "Pasha_Block_07", "Pasha_Block_08", "Pasha_Block_05", "Pasha_Block_06" }
            : new[] { "tb_shop_house_3f_01", "tb_pencil_building_6f_01", "tb_kanban_shop_2f_01", "tb_mansion_5f_01", "tb_tenant_building_7f_01", "tb_warehouse_workshop_01", "tb_wooden_apartment_2f_01", "tb_office_building_8f_01" };
        var prefabs = names.Select(n => AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/" + variant + "/" + n + ".prefab")).ToArray();
        var rows = new GameObject("Building rows"); rows.transform.SetParent(parent); int count = 0;
        for (int side = -1; side <= 1; side += 2) for (int block = 0; block < 2; block++)
        {
            float cursor = block == 0 ? -114 : 14, end = cursor + 100; int pick = (side + 1 + block * 2) % prefabs.Length;
            var rotation = Quaternion.Euler(0, side > 0 ? 270 : 90, 0);
            while (cursor < end)
            {
                var prefab = prefabs[pick++ % prefabs.Length];
                var scratch = (GameObject)PrefabUtility.InstantiatePrefab(prefab); var b = Bounds(scratch); float width = b.size.x;
                if (cursor + width > end)
                { UnityEngine.Object.DestroyImmediate(scratch); break; }
                scratch.name = variant + " frontage " + count++; scratch.transform.SetParent(rows.transform);
                // Source fronts face +Z after FBX import. Align the nearest protrusion behind the same sidewalk edge.
                var target = new Vector3(side * 10.4f, network.curbHeight, cursor + width / 2);
                scratch.transform.SetPositionAndRotation(target - rotation * new Vector3(b.center.x, 0, b.max.z), rotation);
                if (Bounds(scratch).min.y < -.6f) throw new Exception("Building is buried below foundation tolerance: " + scratch.name);
                cursor += width + .2f;
            }
        }
        report.Add(variant + " frontage instances=" + count + "; visible building triangles=" + Triangles(rows) + "; shared-material count=" + rows.GetComponentsInChildren<MeshRenderer>().SelectMany(r => r.sharedMaterials).Distinct().Count());
    }
    static void Traffic(CityRoadRuntime runtime)
    {
        var loops = new[] { new[] { 1, 4, 5, 2 }, new[] { 1, 4, 3, 0 } };
        for (int i = 0; i < 2; i++)
        {
            var obj = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MotorSimulatorAssets/CityPreviewGenerated/Car26_URP.prefab")); obj.name = "Shared traffic " + i; obj.transform.SetParent(runtime.transform);
            var old = obj.GetComponent<PreviewRoadVehicle>(); var wheels = old.wheels; UnityEngine.Object.DestroyImmediate(old);
            var car = obj.AddComponent<CityTrafficVehicle>(); car.session = runtime.session; car.wheels = wheels;
            car.rearAxleZ = wheels.Where(w => w.name.Contains("Rear")).Average(w => obj.transform.InverseTransformPoint(w.position).z);
            car.wheelbase = wheels.Where(w => w.name.Contains("Front")).Average(w => obj.transform.InverseTransformPoint(w.position).z) - car.rearAxleZ;
            car.wheelRadius = wheels.SelectMany(w => w.GetComponentsInChildren<MeshRenderer>()).Where(r => r.enabled).Average(r => r.bounds.extents.y);
            int lane = i == 0 ? 1 : 0; var sequence = loops[i];
            network.Route(sequence, sequence.Select(_ => lane).ToArray(), out car.pathPoints, out car.pathYaw, out car.pathCurvature, out var starts, out var ends);
            car.turns = Enumerable.Range(0, sequence.Length).Select(k => { int current = sequence[k], previous = sequence[(k + 3) % 4], next = sequence[(k + 1) % 4]; var movement = network.Movement(current, network.ArmFromTo(current, previous), network.ArmFromTo(current, next), lane);
                return new CityTrafficTurn { junction = runtime.junctions[current], start = starts[k], end = ends[k], stop = starts[k] + network.halfJunction - network.stopDistance - (2.56f - car.rearAxleZ) - .5f, approachYaw = movement.yaw[0], direction = movement.turn }; }).ToArray();
            car.initialProgress = i == 0 ? 85 : 300; car.cruiseKph = 20; car.Initialise();
        }
    }
    static void Pedestrians(CityRoadRuntime runtime)
    {
        for (int i = 0; i < 2; i++)
        {
            string name = i == 0 ? "Casual_Male" : "Casual_Female";
            var obj = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Existing + "/" + name + "_Pedestrian.prefab")); obj.name = "Shared pedestrian " + i; obj.transform.SetParent(runtime.transform);
            var ped = obj.GetComponent<CityPedestrian>(); ped.actorId = "SC_P" + i; ped.runtime = runtime; ped.node = 4; ped.arm = i; ped.initialWait = i * 2;
            var outward = CityRoadNetwork.Direction(i); var across = CityRoadNetwork.Right(-outward);
            obj.transform.SetPositionAndRotation(outward * network.crossingDistance - across * (network.HalfRoad + network.sidewalkWidth / 2) + Vector3.up * network.curbHeight, Quaternion.LookRotation(across));
        }
    }
    static void ValidateScene(RideTestSession session, string variant)
    {
        if (!network.IsDrivable(session.course.stages[0].spawnPosition, .3f)) throw new Exception("Spawn outside roadway");
        foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            foreach (var material in renderer.sharedMaterials) if (!material || !material.shader || material.shader.name == "Hidden/InternalErrorShader") throw new Exception("Broken material: " + renderer.name);
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0) throw new Exception("Missing script: " + t.name);
        report.Add(variant + ": scene/material/source checks passed; rider/XR/menu unchanged; native building texture styles preserved.");
    }
    static void Capture(string variant, RideTestSession session)
    {
        var presentation = session.GetComponent<RidePresentation>(); if (presentation.pausedPanel) presentation.pausedPanel.SetActive(false);
        session.headCamera.enabled = false;
        foreach (var mirror in session.GetComponentsInChildren<RideMirror>(true)) mirror.enabled = false;
        var temporary = new List<UnityEngine.Object>();
        foreach (var mirror in session.GetComponentsInChildren<RideMirror>())
        {
            var rt = new RenderTexture(512, 512, 24); rt.Create(); temporary.Add(rt);
            var material = new Material(mirror.screenShader); material.SetTexture("_BaseMap", rt); material.SetTextureScale("_BaseMap", new Vector2(-1, 1)); material.SetTextureOffset("_BaseMap", Vector2.right); temporary.Add(material);
            mirror.screen.sharedMaterial = material; mirror.mirrorCamera.GetUniversalAdditionalCameraData().allowXRRendering = false;
        }
        foreach (string name in new[] { "rider", "side", "junction", "overview" })
        {
            Vector3 position, target; float fov = 75;
            if (name == "rider") { session.vehicleRoot.position = new Vector3(4.875f, 0, -74); position = session.vehicleRoot.TransformPoint(session.settingsTemplate.seatedEyePosition); target = position + Quaternion.Euler(18, 0, 0) * Vector3.forward * 40; fov = 85; }
            else if (name == "side") { session.vehicleRoot.position = new Vector3(4.875f, 0, -60); position = session.vehicleRoot.TransformPoint(session.settingsTemplate.seatedEyePosition); target = new Vector3(14, 4, -48); }
            else if (name == "junction") { position = new Vector3(4.875f, 1.35f, -29); target = new Vector3(4.875f, 1.1f, 25); }
            else { position = new Vector3(67, 78, -112); target = new Vector3(0, 4, -40); fov = 65; }
            foreach (var mirror in session.GetComponentsInChildren<RideMirror>())
                RenderPipeline.SubmitRenderRequest(mirror.mirrorCamera, new UniversalRenderPipeline.SingleCameraRequest { destination = (RenderTexture)mirror.screen.sharedMaterial.GetTexture("_BaseMap") });
            UrbanVehiclePreviewBuilder.Capture("../BuildingComparison/" + variant + "-" + name + ".png", position, target, fov);
        }
        foreach (var item in temporary) UnityEngine.Object.DestroyImmediate(item);
    }
}
