// Offline city authoring/validation. Never imported into main Assets or AIT menus.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using AIT.Riding;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class GridCityBuilder
{
    public const string Bootstrap = "Assets/Scenes/GridCity.unity";
    public const string Root = "Assets/MotorSimulatorAssets/GridCityGenerated";
    public const string ChunkFolder = "Assets/Scenes/GridCityChunks";
    public const string WorldPath = Root + "/CityWorld.asset";
    public const string NetworkPath = Root + "/RoadNetwork.asset";
    const string Output = "Personal/GridCity";
    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    static readonly List<string> report = new List<string>();
    static CityRoadNetwork network;
    static StreamingWorldDefinition world;
    static GridRoadGeometry.Map map;
    static Mesh signalLensMesh;
    public static void Run()
    {
        Directory.CreateDirectory(Root + "/Modules"); Directory.CreateDirectory(ChunkFolder); Directory.CreateDirectory(Output); Directory.CreateDirectory("Personal/VehiclePreview"); AssetDatabase.Refresh();
        Materials(); network = LoadOrCreate<CityRoadNetwork>(NetworkPath); BuildNetwork(network);
        world = LoadOrCreate<StreamingWorldDefinition>(WorldPath); world.worldId = network.networkId; world.contentVersion = network.version;
        world.chunkSize = 256; world.gridOrigin = network.origin; world.worldBounds = new Bounds(network.origin + new Vector3(512, 0, 512), new Vector3(1024, 256, 1024));
        world.chunks = Enumerable.Range(0, 16).Select(i => new StreamingChunkDefinition { id = $"G{i % 4:00}_{i / 4:00}", cell = new Vector2Int(i % 4, i / 4),
            scenePath = ChunkFolder + $"/G{i % 4:00}_{i / 4:00}.unity", bounds = new Bounds(network.origin + new Vector3(i % 4 * 256 + 128, 0, i / 4 * 256 + 128), new Vector3(256, 256, 256)) }).ToArray();
        network.Route(network.routeNodes, network.routeLanes, out var route, out var routeYaw, out _, out _, out _);
        // Start on a straight with an unobstructed seated view, not in the conflict area.
        var desiredStart = network.nodes[network.Index(2, 1)].position + new Vector3(network.LaneOffset(1), 0, 52);
        int start = Enumerable.Range(0, route.Length - 1).OrderBy(i => (route[i] - desiredStart).sqrMagnitude).First();
        world.routePoints = Enumerable.Range(0, route.Length - 1).Select(i => route[(start + i) % (route.Length - 1)]).ToArray();
        world.roads = network.links.Select(link => new BlockoutRoadDefinition { id = link.id, width = network.HalfRoad * 2,
            centreline = new[] { network.nodes[link.from].position, network.nodes[link.to].position } }).ToArray();
        EditorUtility.SetDirty(network); EditorUtility.SetDirty(world); AssetDatabase.SaveAssets();
        ValidateNetwork();
        map = new GridRoadGeometry.Map(network.origin);
        foreach (var node in network.nodes) GridRoadGeometry.Junction(map, network, node);
        foreach (var link in network.links)
        {
            var direction = (network.nodes[link.to].position - network.nodes[link.from].position).normalized;
            GridRoadGeometry.Straight(map, network, network.nodes[link.from].position + direction * network.halfJunction, network.nodes[link.to].position - direction * network.halfJunction);
        }
        GridCityScenery.Build(map, network, report);
        for (int i = 0; i < 16; i++) BuildChunk(i);
        BuildModules(); BuildBootstrap(routeYaw[start]);
        AssetDatabase.SaveAssets();
        var scenes = EditorBuildSettings.scenes.ToList();
        foreach (string path in new[] { Bootstrap }.Concat(world.chunks.Select(c => c.scenePath)))
            if (!scenes.Any(s => s.path == path)) scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        File.WriteAllLines(Output + "/authoring.txt", report);
        Capture();
    }
    static T LoadOrCreate<T>(string path) where T : ScriptableObject
    { var asset = AssetDatabase.LoadAssetAtPath<T>(path); if (!asset) { asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); } return asset; }
    static void BuildNetwork(CityRoadNetwork n)
    {
        n.origin = new Vector3(-256, 0, -512); n.count = 8; n.networkId = "grid-city-01"; n.version = "1";
        var nodes = new List<CityRoadNode>(); var links = new List<CityRoadLink>(); var lanes = new List<CityLaneDefinition>();
        for (int z = 0; z < n.count; z++) for (int x = 0; x < n.count; x++)
        {
            int arms = (z < n.count - 1 ? 1 : 0) | (x < n.count - 1 ? 2 : 0) | (z > 0 ? 4 : 0) | (x > 0 ? 8 : 0);
            nodes.Add(new CityRoadNode { id = $"J{x:00}_{z:00}", cell = new Vector2Int(x, z), position = n.origin + new Vector3(n.nodeOffset + x * n.spacing, 0, n.nodeOffset + z * n.spacing),
                arms = arms, boundary = GridRoadGeometry.Boundary(n, arms) });
        }
        n.nodes = nodes.ToArray();
        for (int z = 0; z < n.count; z++) for (int x = 0; x < n.count; x++)
        {
            int from = n.Index(x, z);
            foreach (int to in new[] { x < n.count - 1 ? n.Index(x + 1, z) : -1, z < n.count - 1 ? n.Index(x, z + 1) : -1 })
            {
                if (to < 0) continue; var link = new CityRoadLink { id = $"E_{n.nodes[from].id}_{n.nodes[to].id}", from = from, to = to }; links.Add(link);
                foreach (bool reverse in new[] { false, true }) for (int lane = 0; lane < 2; lane++)
                {
                    int a = reverse ? to : from, b = reverse ? from : to; int arm = n.ArmFromTo(a, b);
                    lanes.Add(new CityLaneDefinition { id = $"L_{n.nodes[a].id}_{n.nodes[b].id}_{lane}", from = a, to = b, lane = lane, start = n.Port(a, arm, lane, false), end = n.Port(b, (arm + 2) % 4, lane, true) });
                }
            }
        }
        n.links = links.ToArray(); n.lanes = lanes.ToArray(); BuildMovements(n);
        var route = new List<int>();
        for (int z = 1; z <= 5; z++) route.Add(n.Index(2, z));
        for (int x = 3; x <= 6; x++) route.Add(n.Index(x, 5));
        for (int z = 4; z >= 1; z--) route.Add(n.Index(6, z));
        for (int x = 5; x >= 3; x--) route.Add(n.Index(x, 1));
        n.routeNodes = route.ToArray(); n.routeLanes = route.Select(_ => 1).ToArray();
    }
    static void BuildMovements(CityRoadNetwork n)
    {
        var movements = new List<CityMovementDefinition>();
        for (int node = 0; node < n.nodes.Length; node++) for (int incoming = 0; incoming < 4; incoming++)
        {
            if (!n.HasArm(node, incoming)) continue;
            var exits = Enumerable.Range(0, 4).Where(a => a != incoming && n.HasArm(node, a)).ToArray();
            foreach (int outgoing in exits) for (int lane = 0; lane < 2; lane++)
            {
                int turn = outgoing == (incoming + 2) % 4 ? 0 : outgoing == (incoming + 3) % 4 ? 1 : -1;
                if (exits.Length > 1 && (turn > 0 && lane == 0 || turn < 0 && lane == 1)) continue;
                var position = n.nodes[node].position; var forward = -CityRoadNetwork.Direction(incoming); var right = CityRoadNetwork.Right(forward);
                var p = new List<Vector3>(); var y = new List<float>(); var c = new List<float>(); float heading = (incoming * 90 + 180) % 360;
                void Add(Vector3 q, float yaw, float curvature) { if (p.Count > 0 && Vector3.Distance(p[p.Count - 1], q) < .0001f) return; p.Add(q); y.Add(yaw); c.Add(curvature); }
                var start = n.Port(node, incoming, lane, true); var end = n.Port(node, outgoing, lane, false);
                if (turn == 0)
                { int steps = Mathf.CeilToInt(Vector3.Distance(start, end) / .4f); for (int i = 0; i <= steps; i++) Add(Vector3.Lerp(start, end, (float)i / steps), heading, 0); }
                else
                {
                    float tangent = n.HalfRoad + n.cornerRadius, radius = tangent - turn * n.LaneOffset(lane);
                    var begin = position + right * n.LaneOffset(lane) - forward * tangent;
                    int steps = Mathf.CeilToInt(Vector3.Distance(start, begin) / .4f);
                    for (int i = 0; i <= steps; i++) Add(Vector3.Lerp(start, begin, (float)i / steps), heading, 0);
                    var centre = begin + right * turn * radius; var radial = begin - centre;
                    int arcs = Mathf.CeilToInt(radius * Mathf.PI / 2 / .2f);
                    for (int i = 1; i <= arcs; i++) Add(centre + Quaternion.Euler(0, turn * 90f * i / arcs, 0) * radial, heading + turn * 90f * i / arcs, turn / radius);
                    var arcEnd = p[p.Count - 1]; steps = Mathf.CeilToInt(Vector3.Distance(arcEnd, end) / .4f);
                    for (int i = 1; i <= steps; i++) Add(Vector3.Lerp(arcEnd, end, (float)i / steps), heading + turn * 90, 0);
                }
                movements.Add(new CityMovementDefinition { id = $"M_{n.nodes[node].id}_{incoming}_{outgoing}_{lane}", node = node, incomingArm = incoming, outgoingArm = outgoing, lane = lane, turn = turn,
                    stopLine = position - forward * n.stopDistance + right * n.LaneOffset(lane), points = p.ToArray(), yaw = y.ToArray(), curvature = c.ToArray() });
            }
        }
        n.movements = movements.ToArray();
    }
    static void ValidateNetwork()
    {
        if (network.nodes.Select(n => n.id).Distinct().Count() != network.nodes.Length) throw new Exception("Duplicate junction identity");
        foreach (var node in network.nodes)
        {
            var owner = world.CellAt(node.position); float minX = world.gridOrigin.x + owner.x * 256, minZ = world.gridOrigin.z + owner.y * 256;
            foreach (var p in node.boundary) if (node.position.x + p.x < minX || node.position.x + p.x > minX + 256 || node.position.z + p.z < minZ || node.position.z + p.z > minZ + 256) throw new Exception("Junction crosses its owner boundary");
        }
        foreach (var movement in network.movements)
            foreach (var p in movement.points) if (!network.IsDrivable(p, .4f)) throw new Exception("Movement leaves road: " + movement.id + " " + p);
        foreach (var lane in network.lanes)
            for (int i = 0; i <= 16; i++) if (!network.IsDrivable(Vector3.Lerp(lane.start, lane.end, i / 16f), .6f)) throw new Exception("Lane crosses sidewalk: " + lane.id);
        float length = 0; for (int i = 0; i < world.routePoints.Length; i++) { var p = world.routePoints[i]; if (!network.IsDrivable(p, .4f)) throw new Exception("Rider route leaves asphalt"); length += Vector3.Distance(p, world.routePoints[(i + 1) % world.routePoints.Length]); }
        report.Add($"Shared road source: {network.nodes.Length} junctions, {network.links.Length} links, {network.lanes.Length} directional lanes, {network.movements.Length} allowed movements. Rider loop {length:F1} m.");
        report.Add("All junction footprints owned wholly by one chunk; lane/turn/rider paths lie on asphalt.");
    }
    static void Materials()
    {
        Material Mat(string name, Color colour, bool paint = false)
        {
            string path = Root + "/" + name + ".mat"; var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find(paint ? "AIT/Road Paint" : "Universal Render Pipeline/Lit"); if (!shader) throw new Exception("Road shader missing");
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); } material.shader = shader;
            material.SetColor("_BaseColor", colour); if (!paint) material.SetFloat("_Smoothness", .08f); material.enableInstancing = true; EditorUtility.SetDirty(material); return material;
        }
        materials["Asphalt"] = Mat("Asphalt", Color.white); materials["Pavement"] = Mat("Pavement", Color.white);
        materials["Curb"] = Mat("Curb", new Color(.57f, .58f, .56f)); materials["Curb"].SetFloat("_Cull", 0);
        materials["White"] = Mat("White", new Color(.87f, .88f, .84f), true); materials["Yellow"] = Mat("Yellow", new Color(1, .71f, .06f), true);
        materials["Green"] = Mat("Green", new Color(.13f, .37f, .29f), true); materials["Land"] = Mat("Land", new Color(.3f, .33f, .3f));
        materials["Green"].renderQueue = 2005; // base colour below white border/icon paint
        materials["Stencil"] = Mat("Stencil", new Color(.87f, .88f, .84f), true);
        const string stencilPath = "Assets/MotorSimulatorAssets/Models/CitySignage/MotorcycleStencil.png";
        var stencilImporter = AssetImporter.GetAtPath(stencilPath) as TextureImporter;
        if (!stencilImporter) throw new Exception("Motorcycle marking texture missing");
        if (stencilImporter.textureShape != TextureImporterShape.Texture2D || stencilImporter.anisoLevel != 8)
        { stencilImporter.textureShape = TextureImporterShape.Texture2D; stencilImporter.anisoLevel = 8; stencilImporter.mipmapEnabled = true; stencilImporter.alphaIsTransparency = true; stencilImporter.wrapMode = TextureWrapMode.Clamp; stencilImporter.SaveAndReimport(); }
        materials["Stencil"].SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(stencilPath)); EditorUtility.SetDirty(materials["Stencil"]);
        materials["SignalMetal"] = Mat("SignalMetal", new Color(.18f, .2f, .21f));
        foreach (string name in new[] { "SignalRed", "SignalGreen" })
        {
            string path = Root + "/" + name + ".mat"; var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find("AIT/City Signal")); AssetDatabase.CreateAsset(material, path); }
            material.SetFloat("_Phase", name == "SignalGreen" ? 2 : 0); materials[name] = material; EditorUtility.SetDirty(material);
        }
        Texture("Asphalt", false); Texture("Pavement", true);
        GridCityScenery.Materials(materials);
    }
    static void Texture(string name, bool paving)
    {
        const int size = 512; var texture = new Texture2D(size, size, TextureFormat.RGB24, true); var random = new System.Random(7331); var pixels = new Color[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float grain = (float)random.NextDouble() * .018f; float value;
            if (paving) { bool joint = x % 128 < 3 || y % 128 < 3; value = (joint ? .29f : .48f) + grain; }
            else value = .14f + grain + Mathf.PerlinNoise(x / 46f, y / 46f) * .014f;
            pixels[y * size + x] = new Color(value, value * 1.01f, value * 1.02f);
        }
        texture.SetPixels(pixels); texture.Apply(); string path = Root + "/" + name + ".png"; File.WriteAllBytes(path, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path); var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.wrapMode = TextureWrapMode.Repeat; importer.mipmapEnabled = true; importer.anisoLevel = 8; importer.textureCompression = TextureImporterCompression.CompressedHQ; importer.SaveAndReimport();
        materials[name].SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path)); EditorUtility.SetDirty(materials[name]);
    }
    static void BuildChunk(int index)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); var definition = world.chunks[index];
        var root = new GameObject(definition.id); var marker = root.AddComponent<WorldChunkMarker>(); marker.worldId = world.worldId; marker.chunkId = definition.id;
        var content = new GameObject("Road, sidewalk and collision"); content.transform.SetParent(root.transform); marker.contentRoot = content;
        var floor = content.AddComponent<BoxCollider>(); floor.center = definition.bounds.center + Vector3.down * .05f; floor.size = new Vector3(256, .1f, 256);
        var ground = new GridRoadGeometry.Surface(); var b = definition.bounds;
        ground.Polygon(new List<Vector3> { new Vector3(b.min.x, -.003f, b.min.z), new Vector3(b.min.x, -.003f, b.max.z), new Vector3(b.max.x, -.003f, b.max.z), new Vector3(b.max.x, -.003f, b.min.z) }, 1);
        AddMesh("Land", content.transform, SaveMesh(ground.Mesh("Land"), definition.id + "_Land"));
        foreach (var pair in map.cells[index])
        {
            var mesh = SaveMesh(pair.Value.Mesh(pair.Key), definition.id + "_" + pair.Key);
            var obj = AddMesh(pair.Key, content.transform, mesh);
            if (pair.Key == "Pavement") obj.AddComponent<MeshCollider>().sharedMesh = mesh;
        }
        AddSignals(content.transform, definition);
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), definition.scenePath);
        report.Add(definition.id + ": static environment triangles=" + map.cells[index].Values.Sum(s => s.triangles.Count / 3) + "; material groups=" + map.cells[index].Count);
    }
    static void AddSignals(Transform parent, StreamingChunkDefinition definition)
    {
        var metal = new GridRoadGeometry.Surface();
        var lensMesh = signalLensMesh;
        if (!lensMesh)
        {
            var vertices = new List<Vector3>(); var colours = new List<Color>(); var triangles = new List<int>();
            for (int light = 0; light < 3; light++)
            {
                int first = vertices.Count; var colour = light == 0 ? new Color(1, .035f, .012f, 0) : light == 1 ? new Color(1, .58f, .02f, .5f) : new Color(.025f, 1, .2f, 1);
                var centre = new Vector3((1 - light) * .4f, 0, 0); vertices.Add(centre); colours.Add(colour);
                for (int k = 0; k <= 20; k++) { float angle = k * Mathf.PI / 10; vertices.Add(centre + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * .16f); colours.Add(colour); }
                for (int k = 0; k < 20; k++) triangles.AddRange(new[] { first, first + k + 1, first + k + 2 });
            }
            var mesh = new Mesh { name = "Horizontal signal lenses" }; mesh.SetVertices(vertices); mesh.SetColors(colours); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); lensMesh = signalLensMesh = SaveMesh(mesh, "SignalLenses_v2");
        }
        void Box(Vector3 centre, Vector3 size, Quaternion rotation)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); var source = cube.GetComponent<MeshFilter>().sharedMesh;
            var vertices = source.vertices; var triangles = source.triangles;
            for (int j = 0; j < triangles.Length; j += 3)
            {
                int first = metal.vertices.Count;
                for (int k = 0; k < 3; k++) { var v = centre + rotation * Vector3.Scale(vertices[triangles[j + k]], size); metal.vertices.Add(v); metal.uv.Add(Vector2.zero); }
                metal.triangles.AddRange(new[] { first, first + 1, first + 2 });
            }
            UnityEngine.Object.DestroyImmediate(cube);
        }
        for (int i = 0; i < network.nodes.Length; i++)
        {
            var node = network.nodes[i]; if (!definition.bounds.Contains(node.position)) continue;
            for (int arm = 0; arm < 4; arm++) if (network.HasArm(i, arm))
            {
                var outward = CityRoadNetwork.Direction(arm); var right = CityRoadNetwork.Right(-outward);
                var pole = node.position + outward * 11.8f + right * 8.25f; var centre = node.position + outward * 11.8f + right * network.LaneOffset(1) + Vector3.up * 4.6f;
                var rotation = Quaternion.LookRotation(outward);
                Box(pole + Vector3.up * 2.3f, new Vector3(.14f, 4.6f, .14f), Quaternion.identity);
                Box((pole + Vector3.up * 4.6f + centre) / 2, new Vector3(3.5f, .12f, .12f), rotation);
                Box(centre - outward * .08f, new Vector3(1.28f, .46f, .24f), rotation);
                var obj = new GameObject(node.id + " signal " + arm); obj.transform.SetParent(parent); obj.transform.SetPositionAndRotation(centre + outward * .05f, rotation);
                obj.AddComponent<MeshFilter>().sharedMesh = lensMesh; var renderer = obj.AddComponent<MeshRenderer>(); renderer.sharedMaterial = materials[arm % 2 == 0 ? "SignalGreen" : "SignalRed"]; renderer.shadowCastingMode = ShadowCastingMode.Off;
                var view = obj.AddComponent<CitySignalView>(); view.node = i; view.northSouth = arm % 2 == 0; view.lamps = renderer;
            }
        }
        AddMesh("SignalMetal", parent, SaveMesh(metal.Mesh("Signal poles"), definition.id + "_Signals"));
    }
    static GameObject AddMesh(string material, Transform parent, Mesh mesh)
    {
        var obj = new GameObject(material); obj.transform.SetParent(parent, false); obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = obj.AddComponent<MeshRenderer>(); renderer.sharedMaterial = materials[material];
        renderer.shadowCastingMode = material.StartsWith("Facade") || material == "Roof" || material == "Leaves" ? ShadowCastingMode.On : ShadowCastingMode.Off; return obj;
    }
    static Mesh SaveMesh(Mesh mesh, string name)
    {
        string path = Root + "/" + name + ".asset"; var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (old) { EditorUtility.CopySerialized(mesh, old); UnityEngine.Object.DestroyImmediate(mesh); EditorUtility.SetDirty(old); return old; }
        AssetDatabase.CreateAsset(mesh, path); return mesh;
    }
    static void BuildModules()
    {
        foreach (var pair in new[] { ("Crossing", 15), ("T_Junction", 11), ("Corner", 3), ("End", 1), ("Straight8m", 5) })
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); var root = new GameObject(pair.Item1);
            var temporary = UnityEngine.Object.Instantiate(network); var node = new CityRoadNode { id = "Module", position = Vector3.zero, arms = pair.Item2, boundary = GridRoadGeometry.Boundary(network, pair.Item2) };
            temporary.nodes = new[] { node }; BuildMovements(temporary); var local = new GridRoadGeometry.Map(new Vector3(-128, 0, -128), 1);
            if (pair.Item1 == "Straight8m") GridRoadGeometry.Straight(local, temporary, new Vector3(0, 0, -4), new Vector3(0, 0, 4));
            else GridRoadGeometry.Junction(local, temporary, node);
            foreach (var surface in local.cells[0]) AddMesh(surface.Key, root.transform, SaveMesh(surface.Value.Mesh(pair.Item1 + " " + surface.Key), "Module_" + pair.Item1 + "_" + surface.Key));
            PrefabUtility.SaveAsPrefabAsset(root, Root + "/Modules/" + pair.Item1 + ".prefab"); UnityEngine.Object.DestroyImmediate(temporary);
        }
    }
    static void BuildBootstrap(float spawnYaw)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/CityCorners.unity"); var session = UnityEngine.Object.FindFirstObjectByType<RideTestSession>();
        foreach (var obj in SceneManager.GetActiveScene().GetRootGameObjects()) if (obj != session.gameObject) UnityEngine.Object.DestroyImmediate(obj);
        var root = new GameObject("Grid city runtime"); var course = root.AddComponent<RideTestCourse>(); session.course = course; session.roadNetwork = network;
        course.stages = new[] { new RideTestStage { name = "城市道路 / 約 2 km 環線", freeRoam = true, spawnPosition = world.routePoints[0], spawnYaw = spawnYaw, centreline = Array.Empty<Vector3>() } };
        course.streetSegmentEndpoints = network.links.SelectMany(link => new[] { network.nodes[link.from].position, network.nodes[link.to].position }).ToArray();
        session.vehicleRoot.SetPositionAndRotation(world.routePoints[0], Quaternion.Euler(0, spawnYaw, 0));
        var streamer = root.AddComponent<RideWorldStreamer>(); streamer.session = session; streamer.world = world; session.worldStreamer = streamer;
        var route = root.AddComponent<BlockoutRouteProgress>(); route.session = session; route.world = world; route.checkpointRadius = 3;
        var runtime = root.AddComponent<CityRoadRuntime>(); runtime.session = session; runtime.network = network; runtime.junctions = new CityJunction[network.nodes.Length];
        for (int i = 0; i < network.nodes.Length; i++)
        {
            var obj = new GameObject(network.nodes[i].id); obj.transform.SetParent(root.transform); obj.transform.position = network.nodes[i].position;
            var junction = obj.AddComponent<CityJunction>(); junction.session = session; junction.signalControlled = true; runtime.junctions[i] = junction;
        }
        BuildTraffic(runtime);
        GridPedestrianBuilder.Build(runtime, report);
        streamer.proxyRoot = new GameObject("Distant road silhouettes").transform; streamer.proxyRoot.SetParent(root.transform);
        foreach (var chunk in world.chunks)
        {
            var proxy = new GameObject(chunk.id).transform; proxy.SetParent(streamer.proxyRoot);
            foreach (string category in new[] { "Land", "Asphalt", "Pavement", "Curb", "Facade0", "Facade1", "Facade2", "Facade3", "Roof", "Leaves", "Trunks" })
            { var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/" + chunk.id + "_" + category + ".asset"); if (mesh) AddMesh(category, proxy, mesh).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off; }
        }
        UrbanVehiclePreviewBuilder.Lighting(); RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 300; RenderSettings.fogEndDistance = 700; RenderSettings.fogColor = new Color(.61f, .69f, .75f);
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Bootstrap);
    }
    static void BuildTraffic(CityRoadRuntime runtime)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MotorSimulatorAssets/CityPreviewGenerated/Car26_URP.prefab");
        var loops = new[] {
            new[] { network.Index(2,1), network.Index(2,2), network.Index(3,2), network.Index(3,1) },
            new[] { network.Index(3,3), network.Index(4,3), network.Index(4,4), network.Index(3,4) },
            new[] { network.Index(5,2), network.Index(5,3), network.Index(6,3), network.Index(6,2) } };
        for (int i = 0; i < 6; i++)
        {
            var sequence = loops[i / 2]; int lane = i / 2 == 1 ? 0 : 1;
            var obj = (GameObject)PrefabUtility.InstantiatePrefab(prefab); obj.name = "City car " + i; obj.transform.SetParent(runtime.transform);
            var old = obj.GetComponent<PreviewRoadVehicle>(); var wheels = old.wheels; UnityEngine.Object.DestroyImmediate(old);
            var car = obj.AddComponent<CityTrafficVehicle>(); car.session = runtime.session; car.wheels = wheels;
            car.rearAxleZ = wheels.Where(w => w.name.Contains("Rear")).Average(w => obj.transform.InverseTransformPoint(w.position).z);
            car.wheelbase = wheels.Where(w => w.name.Contains("Front")).Average(w => obj.transform.InverseTransformPoint(w.position).z) - car.rearAxleZ;
            car.wheelRadius = wheels.SelectMany(w => w.GetComponentsInChildren<MeshRenderer>()).Where(r => r.enabled).Average(r => r.bounds.extents.y);
            network.Route(sequence, sequence.Select(_ => lane).ToArray(), out car.pathPoints, out car.pathYaw, out car.pathCurvature, out var starts, out var ends);
            car.turns = Enumerable.Range(0, sequence.Length).Select(k => {
                int current = sequence[k], previous = sequence[(k + sequence.Length - 1) % sequence.Length], next = sequence[(k + 1) % sequence.Length];
                var movement = network.Movement(current, network.ArmFromTo(current, previous), network.ArmFromTo(current, next), lane);
                return new CityTrafficTurn { junction = runtime.junctions[current], start = starts[k], end = ends[k],
                    stop = starts[k] + network.halfJunction - network.stopDistance - (2.56f - car.rearAxleZ) - .5f,
                    approachYaw = movement.yaw[0], direction = movement.turn };
            }).ToArray();
            float length = 0; for (int k = 1; k < car.pathPoints.Length; k++) length += Vector3.Distance(car.pathPoints[k - 1], car.pathPoints[k]);
            car.initialProgress = i % 2 == 0 ? 80 : length / 2 + 80; car.cruiseKph = 20;
            car.Initialise();
            for (float s = 0; s < car.Length; s += .35f)
            {
                car.Sample(s, out var rear, out float yaw, out _); var rotation = Quaternion.Euler(0, yaw, 0); var centre = rear - rotation * Vector3.forward * car.rearAxleZ;
                foreach (float x in new[] { -1.06f, 1.06f }) foreach (float z in new[] { -2.56f, 2.56f })
                    if (!network.IsDrivable(centre + rotation * new Vector3(x, 0, z))) throw new Exception(obj.name + " body crosses curb at " + s);
            }
            var visibility = obj.AddComponent<CityActorVisibility>(); visibility.session = runtime.session;
            report.Add(obj.name + $": {car.Length:F1} m controlled loop; full car footprint on asphalt; rear axle stop uses rendered stop-line distance.");
        }
    }
    static void Capture()
    {
        var session = UnityEngine.Object.FindFirstObjectByType<RideTestSession>(); session.GetComponent<RidePresentation>().pausedPanel.SetActive(false);
        // Add the actual nearby chunks for road/paint inspection, not just proxies.
        foreach (var chunk in world.chunks) if (chunk.cell.x <= 2 && chunk.cell.y <= 2)
        {
            EditorSceneManager.OpenScene(chunk.scenePath, OpenSceneMode.Additive); var proxy = session.worldStreamer.proxyRoot.Find(chunk.id); if (proxy) proxy.gameObject.SetActive(false);
        }
        var temporaryObjects = new List<UnityEngine.Object>();
        foreach (var mirror in session.GetComponentsInChildren<RideMirror>())
        {
            var rt = new RenderTexture(512, 512, 24); rt.Create(); temporaryObjects.Add(rt);
            var material = new Material(mirror.screenShader); material.SetTexture("_BaseMap", rt); material.SetTextureScale("_BaseMap", new Vector2(-1, 1)); material.SetTextureOffset("_BaseMap", Vector2.right); temporaryObjects.Add(material);
            mirror.screen.sharedMaterial = material;
            mirror.mirrorCamera.GetUniversalAdditionalCameraData().allowXRRendering = false;
            RenderPipeline.SubmitRenderRequest(mirror.mirrorCamera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
        }
        var node = network.nodes[network.Index(2, 2)].position;
        var pedestrians = UnityEngine.Object.FindObjectsByType<CityPedestrian>(FindObjectsSortMode.None);
        if (pedestrians.Length > 0)
        {
            var person = pedestrians[0]; var renderers = person.GetComponentsInChildren<SkinnedMeshRenderer>();
            File.AppendAllText(Output + "/authoring.txt", "Pedestrian photo pose " + person.transform.position + "; bounds=" + string.Join(" / ", renderers.Select(r => r.bounds.ToString())) + "\n");
            UrbanVehiclePreviewBuilder.Capture("../GridCity/pedestrian.png", person.transform.position + person.transform.forward * 3.2f + Vector3.up * 1.5f, person.transform.position + Vector3.up, 52);
        }
        UrbanVehiclePreviewBuilder.Capture("../GridCity/junction.png", node + new Vector3(25, 38, -36), node, 62);
        var spawn = world.routePoints[0]; var forward = Quaternion.Euler(0, session.course.stages[0].spawnYaw, 0) * Vector3.forward;
        UrbanVehiclePreviewBuilder.Capture("../GridCity/rider.png", spawn + new Vector3(0, 1.35f, 0) - forward * .28f, spawn + forward * 35 + Vector3.up, 75);
        UrbanVehiclePreviewBuilder.Capture("../GridCity/road-close.png", node + new Vector3(4.875f, 1.35f, -29), node + new Vector3(4.875f, 0, -10), 75);
        RenderSettings.fog = false;
        UrbanVehiclePreviewBuilder.Capture("../GridCity/overview.png", network.origin + new Vector3(1130, 1000, -220), network.origin + new Vector3(512, 0, 512), 62, 2600);
        foreach (var obj in temporaryObjects) UnityEngine.Object.DestroyImmediate(obj);
    }
}
