// Offline authoring. The working project's Assets contain the baked scene only.
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
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public sealed class NavigationDistrictBuilder
{
    const string Output = "Personal/NavigationDistrict";
    const string Root = "Assets/MotorSimulatorAssets/BuildingComparisonLocal/NavigationDistrict";
    const string GridMaterials = "Assets/MotorSimulatorAssets/GridCityGenerated/";
    const string KoboPrefabs = "Assets/MotorSimulatorAssets/BuildingComparisonLocal/Prefabs/Kobo/";
    const float Curb = .15f;
    const float JunctionSetback = 17f;

    [Serializable] sealed class Layout
    {
        public Node[] junctions;
        public Street[] streets;
        public Route[] routes;
    }

    [Serializable] sealed class Node
    {
        public string id;
        public float x, north;
        public Vector3 Centre => World(x, north);
        [NonSerialized] public readonly List<Arm> arms = new List<Arm>();
    }

    [Serializable] sealed class Street
    {
        public string from, to;
        public float width, sidewalk, controlX, controlNorth;
        public int lanesPerDirection;
        public bool curved, raisedSidewalk;
        [NonSerialized] public Node a, b;
        [NonSerialized] public readonly List<Section> sections = new List<Section>();
        public float HalfWidth => width / 2;
        public float LaneCentre => lanesPerDirection == 2 ? 1.625f : width / 4;
        public float SidewalkHeight => raisedSidewalk ? Curb : 0;

        public Section Sample(float t)
        {
            Vector3 start = a.Centre, end = b.Centre;
            if (!curved) return new Section(Vector3.Lerp(start, end, t), (end - start).normalized);
            Vector3 guide = World(controlX, controlNorth);
            Vector3 left = Vector3.Lerp(start, guide, t);
            Vector3 right = Vector3.Lerp(guide, end, t);
            return new Section(Vector3.Lerp(left, right, t), (right - left).normalized);
        }

        public float Portal(bool start)
        {
            Node node = start ? a : b;
            if (node.arms.Count == 1) return start ? 0 : 1;
            Vector3 outward = Sample(start ? 0 : 1).forward * (start ? 1 : -1);
            float lo = 0, hi = .5f;
            for (int i = 0; i < 22; i++)
            {
                float d = (lo + hi) / 2;
                Vector3 point = Sample(start ? d : 1 - d).centre;
                if (Vector3.Dot(point - node.Centre, outward) < JunctionSetback) lo = d;
                else hi = d;
            }
            return start ? hi : 1 - hi;
        }

        public void BuildSections()
        {
            float begin = Portal(true), end = Portal(false);
            if (end <= begin) throw new InvalidOperationException($"Portals overlap on {from}/{to}");
            sections.Add(Sample(begin));
            Subdivide(begin, end, 0);
        }

        void Subdivide(float a, float b, int depth)
        {
            var first = Sample(a);
            var last = Sample(b);
            float mid = (a + b) / 2;
            var middle = Sample(mid);
            float reach = HalfWidth + sidewalk;
            bool accurate = new[] { -reach, reach }.All(offset => (
                middle.At(offset) - (first.At(offset) + last.At(offset)) / 2).sqrMagnitude <= .0025f);
            if (accurate || depth == 8)
            {
                sections.Add(last);
                return;
            }
            Subdivide(a, mid, depth + 1);
            Subdivide(mid, b, depth + 1);
        }
    }

    readonly struct Section
    {
        public readonly Vector3 centre, forward, right;
        public Section(Vector3 centre, Vector3 forward)
        {
            this.centre = centre;
            this.forward = forward;
            right = CityRoadNetwork.Right(forward);
        }
        public Vector3 At(float offset, float height = 0) => centre + right * offset + Vector3.up * height;
    }

    sealed class Arm
    {
        public readonly Street street;
        public readonly bool atStart;
        public Arm(Street street, bool atStart) { this.street = street; this.atStart = atStart; }
        public Section Port
        {
            get
            {
                var sample = atStart ? street.sections[0] : street.sections[street.sections.Count - 1];
                return new Section(sample.centre, atStart ? sample.forward : -sample.forward);
            }
        }
        public float Width(bool outer) => street.HalfWidth + (outer ? street.sidewalk : 0);
    }

    [Serializable] sealed class Route
    {
        public string id;
        public string[] junctions;
        public Vector3[] samples;
    }

    readonly Dictionary<string, GridRoadGeometry.Surface> surfaces = new Dictionary<string, GridRoadGeometry.Surface>();
    readonly List<CityRoadArea> roadAreas = new List<CityRoadArea>();
    readonly List<CityRoadArea> frontageBuffers = new List<CityRoadArea>();
    readonly List<Vector3> barrierEndpoints = new List<Vector3>();
    static Vector3 World(float x, float north) => new Vector3(x - 340, 0, north - 75);

    public static void Run() => new NavigationDistrictBuilder().Build();

    void Build()
    {
        Directory.CreateDirectory(Root);
        AssetDatabase.Refresh();
        var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(Output + "/layout.json"));
        var nodes = layout.junctions.ToDictionary(node => node.id);
        foreach (var street in layout.streets)
        {
            street.a = nodes[street.from];
            street.b = nodes[street.to];
            street.a.arms.Add(new Arm(street, true));
            street.b.arms.Add(new Arm(street, false));
        }
        foreach (var street in layout.streets) { street.BuildSections(); BuildStreet(street); }
        foreach (var node in layout.junctions) BuildJunction(node);

        var network = ScriptableObject.CreateInstance<CityRoadNetwork>();
        network.networkId = "navigation-district";
        network.version = "3";
        network.authoredRoadAreas = roadAreas.ToArray();
        network.roadBarrierEndpoints = barrierEndpoints.ToArray();
        network = Save(network, Root + "/RoadNetwork.asset");
        foreach (var route in layout.routes) BakeRoute(route, nodes, network);

        EditorSceneManager.OpenScene(RideScenes.KoboTrafficPath);
        var session = Object.FindFirstObjectByType<RideTestSession>();
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            if (root != session.gameObject && !root.GetComponent<Light>()) Object.DestroyImmediate(root);
        var district = new GameObject("Navigation district");
        var course = district.AddComponent<RideTestCourse>();
        session.course = course;
        session.roadNetwork = network;
        session.worldStreamer = null;
        var first = layout.routes[0].samples;
        Vector3 start = first[0];
        float yaw = Quaternion.LookRotation(first[1] - start).eulerAngles.y;
        course.stages = new[] { new RideTestStage { name = "不規則街區", freeRoam = true, spawnPosition = start, spawnYaw = yaw, centreline = first } };
        course.streetSegmentEndpoints = layout.streets.SelectMany(street => street.sections.Zip(street.sections.Skip(1),
            (a, b) => new[] {a.centre, b.centre}).SelectMany(pair => pair)).ToArray();
        session.vehicleRoot.SetPositionAndRotation(start, Quaternion.Euler(0, yaw, 0));
        RenderSettings.fog = true;
        RenderSettings.fogStartDistance = 170;
        RenderSettings.fogEndDistance = 420;

        Box(district.transform, "Base", World(350, 74) + Vector3.down * .102f, new Vector3(740, .19f, 395), Material("Land"));
        int roadTriangles = 0;
        foreach (var entry in surfaces)
        {
            var mesh = SaveSurface(entry.Key, entry.Value);
            var surface = new GameObject(entry.Key);
            surface.transform.SetParent(district.transform, false);
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = surface.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Material(entry.Key);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            roadTriangles += (int)mesh.GetIndexCount(0) / 3;
        }
        string frontageReport = BuildFrontages(district.transform, layout.streets);
        if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), RideScenes.NavigationDistrictPath))
            throw new InvalidOperationException("Could not save the district scene");
        AssetDatabase.SaveAssets();
        File.WriteAllText(Output + "/paths.json", JsonUtility.ToJson(layout, false));
        File.WriteAllLines(Output + "/authoring.txt", new[]
        {
            $"{layout.streets.Count(street => !street.curved)} straight streets, {layout.streets.Count(street => street.curved)} curved streets, {layout.junctions.Count(node => node.arms.Count > 2)} junctions, {roadTriangles} road, curb and paint triangles.",
            $"{layout.streets.Count(street => !street.raisedSidewalk)} streets without raised sidewalks; {barrierEndpoints.Count / 2} invisible boundary segments.",
            $"{Vector3.Distance(nodes["near"].Centre, nodes["far"].Centre):F1} m between left-turn options; {Vector3.Distance(nodes["east-junction"].Centre, nodes["east"].Centre):F0} m east arterial.",
            $"{Vector3.Distance(nodes["first-right"].Centre, nodes["second-right"].Centre):F1} m between right-turn options.",
            frontageReport,
            string.Join("; ", layout.routes.Select(route => $"{route.id}: {route.samples.Zip(route.samples.Skip(1), Vector3.Distance).Sum():F1} m; inside asphalt with clearance"))
        });
        Capture(session);
    }

    void BuildStreet(Street street)
    {
        float h = street.HalfWidth;
        var sections = street.sections;
        float distance = 0;
        for (int i = 1; i < sections.Count; i++)
        {
            var a = sections[i - 1];
            var b = sections[i];
            Asphalt(a.At(-h), a.At(h), b.At(h), b.At(-h));
            float sidewalkHeight = street.SidewalkHeight;
            RoadEdge(a.At(-h), b.At(-h), a.At(-h - street.sidewalk), b.At(-h - street.sidewalk), sidewalkHeight, sidewalkHeight);
            RoadEdge(b.At(h), a.At(h), b.At(h + street.sidewalk), a.At(h + street.sidewalk), sidewalkHeight, sidewalkHeight);
            float length = Vector3.Distance(a.centre, b.centre);
            if (street.width >= 7)
            {
                Paint(a, b, street.lanesPerDirection == 2 ? -.13f : 0, .11f);
                if (street.lanesPerDirection == 2) Paint(a, b, .13f, .11f);
            }
            foreach (float side in new[] {-1f, 1f})
            {
                if (street.width >= 9) Paint(a, b, side * (h - .32f), .11f, "White");
                if (street.lanesPerDirection != 2) continue;
                float end = distance + length;
                for (float dash = Mathf.Floor(distance / 8) * 8; dash < end; dash += 8)
                {
                    float lo = (Mathf.Max(distance, dash + 1) - distance) / length;
                    float hi = (Mathf.Min(end, dash + 4) - distance) / length;
                    if (hi > lo) Paint(a, b, side * 3.25f, .14f, "White", lo, hi);
                }
            }
            distance += length;
        }
    }

    void BuildJunction(Node node)
    {
        var sorted = node.arms.OrderBy(arm => Mathf.Atan2(arm.Port.forward.x, arm.Port.forward.z)).ToArray();
        if (sorted.Length == 1)
        {
            var end = sorted[0];
            var p = end.Port;
            float h = end.Width(false);
            Vector3 extension = -p.forward * end.street.sidewalk;
            RoadEdge(p.At(h), p.At(-h), p.At(h + end.street.sidewalk) + extension,
                p.At(-h - end.street.sidewalk) + extension, end.street.SidewalkHeight, end.street.SidewalkHeight);
            return;
        }
        var boundary = new List<Vector3>();
        for (int i = 0; i < sorted.Length; ++i)
        {
            var arm = sorted[i];
            var next = sorted[(i + 1) % sorted.Length];
            boundary.Add(arm.Port.At(-arm.Width(false)));
            var inner = Corner(arm, next, false, out bool innerTangent);
            var outer = Corner(arm, next, true, out bool outerTangent);
            float available = new[] { inner, outer }.Min(points =>
                Mathf.Min(Vector3.Distance(points[1], points[0]), Vector3.Distance(points[1], points[2])));
            float fillet = innerTangent && outerTangent ? Mathf.Min(4.5f, available * .8f) : 0;
            Vector3[] curb = CornerSamples(inner, fillet);
            Vector3[] walk = CornerSamples(outer, fillet);
            float heightA = arm.street.SidewalkHeight, heightB = next.street.SidewalkHeight;
            float length = Mathf.Max(.001f, curb.Zip(curb.Skip(1), Vector3.Distance).Sum());
            float distance = 0;
            for (int step = 0; step < curb.Length - 1; step++)
            {
                boundary.Add(curb[step]);
                float from = distance / length;
                distance += Vector3.Distance(curb[step], curb[step + 1]);
                float to = distance / length;
                RoadEdge(curb[step], curb[step + 1], walk[step], walk[step + 1],
                    Mathf.Lerp(heightA, heightB, from), Mathf.Lerp(heightA, heightB, to));
            }
            if (sorted.Length >= 3 && arm.street.lanesPerDirection == 1) MarkSideStreet(arm);
        }
        roadAreas.Add(Area(boundary.ToArray()));
        Vector3 centre = node.Centre;
        Vector3 raise = Vector3.up * GridRoadGeometry.AsphaltY;
        for (int i = 0; i < boundary.Count; i++)
        {
            Vector3 a = boundary[i];
            Vector3 b = boundary[(i + 1) % boundary.Count];
            float area = Vector3.Cross(a - centre, b - centre).y;
            if (area < -.001f) throw new InvalidOperationException($"Overlapping approaches at {node.id}");
            if (area > .001f) Polygon("Asphalt", centre + raise, a + raise, b + raise);
        }
    }

    static Vector3[] CornerSamples(Vector3[] control, float fillet)
    {
        if (fillet < .08f) return new[] { control[0], control[2] };
        var curve = new[]
        {
            Vector3.MoveTowards(control[1], control[0], fillet),
            control[1],
            Vector3.MoveTowards(control[1], control[2], fillet)
        };
        var corners = new Vector3[9];
        corners[0] = control[0];
        corners[corners.Length - 1] = control[2];
        for (int i = 0; i <= 6; i++) corners[i + 1] = Bezier(curve, i / 6f);
        return corners;
    }

    static Vector3[] Corner(Arm a, Arm b, bool outer, out bool tangent)
    {
        var from = a.Port.At(a.Width(outer));
        var to = b.Port.At(-b.Width(outer));
        var d = a.Port.forward;
        var e = b.Port.forward;
        Vector3 control = (from + to) / 2;
        tangent = false;
        float cross = Vector3.Cross(d, e).y;
        // Outer corners of bends have a clockwise gap over 180 degrees.
        if (Mathf.Abs(cross) > .12f)
        {
            float u = Vector3.Cross(to - from, e).y / cross;
            float v = Vector3.Cross(to - from, d).y / cross;
            if (u <= 0 && v <= 0 && u >= -40 && v >= -40)
            {
                control = from + d * u;
                tangent = true;
            }
        }
        return new[] { from, control, to };
    }

    void MarkSideStreet(Arm arm)
    {
        var port = arm.Port;
        // Painted give-way bar on the inbound half and a zebra across the side street.
        for (float x = -arm.Width(false) + .65f; x < arm.Width(false) - .45f; x += .85f)
            MarkRect(port, x, 1.55f, .42f, 2.1f);
        for (float x = -arm.Width(false) + .62f; x < -.24f; x += .72f)
            MarkRect(port, x, 3.25f, .45f, .28f);
    }

    void MarkRect(Section p, float offset, float distance, float width, float length)
    {
        var a = new Section(p.centre + p.forward * (distance - length / 2), p.forward);
        var b = new Section(p.centre + p.forward * (distance + length / 2), p.forward);
        Paint(a, b, offset, width, "White");
    }

    void Paint(Section a, Section b, float offset, float width, string key = "Yellow", float lo = 0, float hi = 1)
    {
        float left = offset - width / 2, right = offset + width / 2, y = GridRoadGeometry.PaintY;
        Polygon(key, Vector3.Lerp(a.At(left, y), b.At(left, y), lo), Vector3.Lerp(a.At(right, y), b.At(right, y), lo),
            Vector3.Lerp(a.At(right, y), b.At(right, y), hi), Vector3.Lerp(a.At(left, y), b.At(left, y), hi));
    }

    void Asphalt(params Vector3[] points)
    {
        roadAreas.Add(Area(points));
        Polygon("Asphalt", points.Select(p => p + Vector3.up * GridRoadGeometry.AsphaltY).ToArray());
    }

    void RoadEdge(Vector3 a, Vector3 b, Vector3 outerA, Vector3 outerB, float heightA, float heightB)
    {
        barrierEndpoints.Add(a);
        barrierEndpoints.Add(b);
        frontageBuffers.Add(Area(new[] {a, b, outerB, outerA}));
        if (heightA <= 0 && heightB <= 0) return;
        var ground = Vector3.up * GridRoadGeometry.AsphaltY;
        var raisedA = Vector3.up * Mathf.Max(GridRoadGeometry.AsphaltY, heightA);
        var raisedB = Vector3.up * Mathf.Max(GridRoadGeometry.AsphaltY, heightB);
        Polygon("Pavement", a + raisedA, b + raisedB, outerB + raisedB, outerA + raisedA);
        if (heightA <= GridRoadGeometry.AsphaltY)
            Polygon("Curb", b + ground, a + ground, b + raisedB);
        else if (heightB <= GridRoadGeometry.AsphaltY)
            Polygon("Curb", b + ground, a + ground, a + raisedA);
        else
            Polygon("Curb", b + ground, a + ground, a + raisedA, b + raisedB);
    }

    void Polygon(string key, params Vector3[] points)
    {
        if (!surfaces.TryGetValue(key, out var surface))
            surfaces.Add(key, surface = new GridRoadGeometry.Surface());
        surface.Polygon(points.ToList(), key == "Asphalt" ? .125f : .5f);
    }

    static void BakeRoute(Route route, Dictionary<string, Node> nodes, CityRoadNetwork network)
    {
        var result = new List<Vector3>();
        for (int i = 1; i < route.junctions.Length; i++)
        {
            Node from = nodes[route.junctions[i - 1]], to = nodes[route.junctions[i]];
            var arm = from.arms.Single(candidate => candidate.street.a == to || candidate.street.b == to);
            var street = arm.street;
            var offset = street.LaneCentre * (arm.atStart ? 1 : -1);
            var sections = arm.atStart ? street.sections.AsEnumerable() : street.sections.AsEnumerable().Reverse();
            var lane = sections.Select(sample => sample.At(offset)).ToArray();
            if (result.Count == 0) lane[0] += arm.Port.forward * 18;
            if (i == route.junctions.Length - 1) lane[lane.Length - 1] -= (lane[lane.Length - 1] - lane[lane.Length - 2]).normalized * 18;
            if (result.Count > 0)
            {
                Vector3 a = result[result.Count - 1], b = lane[0];
                var inDirection = (a - result[result.Count - 2]).normalized;
                var outDirection = (lane[1] - b).normalized;
                var connector = TurnConnector(a, b, inDirection, outDirection, network);
                if (connector == null)
                    throw new InvalidOperationException($"Route {route.id} cannot fit its turn at {from.id}");
                result.AddRange(connector.Skip(1).Take(connector.Length - 2));
            }
            result.AddRange(lane);
        }
        for (int i = 1; i < result.Count; i++)
        {
            int checks = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(result[i - 1], result[i]) / .4f));
            for (int j = 0; j <= checks; j++)
            {
                var point = Vector3.Lerp(result[i - 1], result[i], j / (float)checks);
                if (!network.IsDrivable(point, .55f))
                    throw new InvalidOperationException($"Route {route.id} leaves asphalt near {point} at sample {i}");
            }
        }
        route.samples = result.ToArray();
    }

    static Vector3[] TurnConnector(Vector3 from, Vector3 to, Vector3 entry, Vector3 exit, CityRoadNetwork network)
    {
        // Wider turns stay on the incoming heading until nearer the centre of the junction.
        Vector3[] widest = null;
        foreach (float fraction in new[] { .46f, .54f, .62f, .7f })
        {
            float handle = Vector3.Distance(from, to) * fraction;
            var control = new[] { from, from + entry * handle, to - exit * handle, to };
            var samples = Enumerable.Range(0, 25).Select(i => Bezier(control, i / 24f)).ToArray();
            var checks = samples.Concat(samples.Zip(samples.Skip(1), (a, b) => (a + b) / 2)).ToArray();
            if (checks.All(p => network.IsDrivable(p, 1.07f))) return samples;
            if (checks.All(p => network.IsDrivable(p, .55f))) widest = samples;
        }
        return widest;
    }

    static Vector3 Bezier(Vector3[] points, float t)
    {
        Vector3 a = Vector3.Lerp(points[0], points[1], t);
        Vector3 b = Vector3.Lerp(points[1], points[2], t);
        return points.Length == 3 ? Vector3.Lerp(a, b, t) :
            Vector3.Lerp(Vector3.Lerp(a, b, t), Vector3.Lerp(b, Vector3.Lerp(points[2], points[3], t), t), t);
    }

    static CityRoadArea Area(Vector3[] points) => new CityRoadArea
    {
        boundary = points,
        bounds = Rect.MinMaxRect(points.Min(p => p.x), points.Min(p => p.z), points.Max(p => p.x), points.Max(p => p.z))
    };

    sealed class Building
    {
        public readonly GameObject prefab;
        public readonly Bounds bounds;
        public readonly int triangles;
        public Building(string name)
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KoboPrefabs + name + ".prefab");
            if (!prefab) throw new FileNotFoundException("Missing Kobo prefab: " + name);
            var scratch = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            var renderers = scratch.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled).ToArray();
            bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                for (int i = 0; i < mesh.subMeshCount; i++) triangles += (int)mesh.GetIndexCount(i) / 3;
            }
            Object.DestroyImmediate(scratch);
        }
    }

    string BuildFrontages(Transform parent, Street[] streets)
    {
        var shopHouses = new[]
        {
            new Building("tb_shop_house_3f_01"), new Building("tb_kanban_shop_2f_01"),
            new Building("tb_kanban_shop_2f_01_b")
        };
        var houses = new[]
        {
            new Building("tb_house_tile_2f_01"), new Building("tb_house_modern_2f_01"),
            new Building("tb_wooden_apartment_2f_01")
        };
        var occupied = roadAreas.Concat(frontageBuffers).ToList();
        var scenery = new GameObject("Kobo street frontages");
        scenery.transform.SetParent(parent, false);
        int count = 0, triangleCount = 0;
        foreach (var street in streets)
        foreach (int side in new[] {-1, 1})
        {
            bool arterial = street.lanesPerDirection == 2;
            var choices = arterial ? shopHouses : houses;
            float end = Vector3.Distance(street.a.Centre, street.b.Centre);
            // Leave both corners readable. The exact footprint is checked against every neighboring street.
            float startMargin = street.a.arms.Count > 1 ? 15.5f : 6f;
            float endMargin = street.b.arms.Count > 1 ? 15.5f : 6f;
            int pick = side > 0 ? 1 : 0;
            float cursor = startMargin;
            while (cursor < end - endMargin - 3)
            {
                var building = choices[pick++ % choices.Length];
                var bounds = building.bounds;
                float width = bounds.size.x;
                float centre = cursor + width / 2;
                cursor += width + (arterial ? 2 : 3);
                if (centre + width / 2 > end - endMargin) continue;
                var section = street.Sample(centre / end);
                if (arterial && side > 0 && section.centre.x > -266 && section.centre.x < -185) continue;
                var rotation = Quaternion.LookRotation(-side * section.right);
                float setback = street.raisedSidewalk ? .85f : .4f;
                var front = section.At(side * (street.HalfWidth + street.sidewalk + setback), street.SidewalkHeight);
                var position = front - rotation * new Vector3(bounds.center.x, 0, bounds.max.z);
                var footprint = BuildingFootprint(bounds, position, rotation);
                if (occupied.Any(other => Overlaps(footprint, other))) continue;
                occupied.Add(footprint);
                var copy = (GameObject)PrefabUtility.InstantiatePrefab(building.prefab, scenery.transform);
                copy.transform.SetPositionAndRotation(position, rotation);
                count++;
                triangleCount += building.triangles;
            }
        }
        Box(parent, "Open space", World(114, -29), new Vector3(70, .03f, 29), Material("Leaves"));
        if (count < 20) throw new InvalidOperationException("Too few Kobo frontages fitted to the district");
        return $"{count} Kobo buildings at imported scale; {triangleCount} building triangles before culling.";
    }

    static CityRoadArea BuildingFootprint(Bounds bounds, Vector3 position, Quaternion rotation)
    {
        const float gap = .22f;
        var min = bounds.min;
        var max = bounds.max;
        Vector3 Corner(float x, float z)
        {
            var point = position + rotation * new Vector3(x, 0, z);
            point.y = 0;
            return point;
        }
        return Area(new[]
        {
            Corner(min.x - gap, min.z - gap), Corner(max.x + gap, min.z - gap),
            Corner(max.x + gap, max.z + gap), Corner(min.x - gap, max.z + gap)
        });
    }

    static bool Overlaps(CityRoadArea a, CityRoadArea b)
    {
        if (!a.bounds.Overlaps(b.bounds)) return false;
        if (a.boundary.Any(point => CityRoadNetwork.Inside(point, b.boundary)) ||
            b.boundary.Any(point => CityRoadNetwork.Inside(point, a.boundary))) return true;
        for (int i = 0; i < a.boundary.Length; i++)
        for (int j = 0; j < b.boundary.Length; j++)
        {
            var p = a.boundary[i];
            var q = a.boundary[(i + 1) % a.boundary.Length];
            var r = b.boundary[j];
            var s = b.boundary[(j + 1) % b.boundary.Length];
            float Side(Vector3 start, Vector3 end, Vector3 point) => Vector3.Cross(end - start, point - start).y;
            if (Side(p, q, r) * Side(p, q, s) < 0 && Side(r, s, p) * Side(r, s, q) < 0) return true;
        }
        return false;
    }

    static Transform Box(Transform parent, string name, Vector3 position, Vector3 size, Material material)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = name;
        obj.transform.SetParent(parent, false);
        obj.transform.position = position;
        obj.transform.localScale = size;
        obj.GetComponent<MeshRenderer>().sharedMaterial = material;
        Object.DestroyImmediate(obj.GetComponent<Collider>());
        return obj.transform;
    }

    static Material Material(string name) => AssetDatabase.LoadAssetAtPath<Material>(GridMaterials + name + ".mat");

    static Mesh SaveSurface(string name, GridRoadGeometry.Surface surface)
    {
        string path = Root + "/" + name + ".asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!mesh)
        {
            mesh = surface.Mesh(name);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        // Rebuild the live vertex buffer too, so this editor can render the changed roads before reopening.
        mesh.Clear(false);
        mesh.SetVertices(surface.vertices);
        mesh.SetTriangles(surface.triangles, 0);
        mesh.SetUVs(0, surface.uv);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    static T Save<T>(T asset, string path) where T : Object
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (!existing) { AssetDatabase.CreateAsset(asset, path); return asset; }
        EditorUtility.CopySerialized(asset, existing);
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(asset);
        return existing;
    }

    static void Capture(RideTestSession session)
    {
        foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) camera.enabled = false;
        foreach (var mirror in session.GetComponentsInChildren<RideMirror>(true)) mirror.enabled = false;
        RenderSettings.fog = false;
        UrbanVehiclePreviewBuilder.Capture("../NavigationDistrict/overview.png", World(349, 105) + new Vector3(0, 520, -30), World(349, 105), 55, 900);
        RenderSettings.fog = true;
        UrbanVehiclePreviewBuilder.Capture("../NavigationDistrict/two-turns.png", World(128, -2.35f) + Vector3.up * 1.38f,
            World(202, 4) + Vector3.up * 2, 64);
        UrbanVehiclePreviewBuilder.Capture("../NavigationDistrict/right-turns.png", World(210.4f, 54) + Vector3.up * 1.38f,
            World(214, 134) + Vector3.up * 2, 68);
    }
}
