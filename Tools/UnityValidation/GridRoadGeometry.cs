// Offline road kit authoring. Never imported into the working project's Assets.
using System;
using System.Collections.Generic;
using System.Linq;
using AIT.Riding;
using UnityEngine;
using UnityEngine.Rendering;

public static class GridRoadGeometry
{
    public const float AsphaltY = .008f, PaintY = .017f;
    public sealed class Surface
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<int> triangles = new List<int>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public void Polygon(List<Vector3> polygon, float uvScale)
        {
            if (polygon.Count < 3) return;
            var remaining = Enumerable.Range(0, polygon.Count).ToList();
            // Remove redundant collinear points before ear clipping.
            for (int i = remaining.Count - 1; i >= 0 && remaining.Count > 3; i--)
            {
                var a = polygon[remaining[(i + remaining.Count - 1) % remaining.Count]];
                var b = polygon[remaining[i]]; var c = polygon[remaining[(i + 1) % remaining.Count]];
                if (Vector3.Cross(b - a, c - b).sqrMagnitude < .00000001f) remaining.RemoveAt(i);
            }
            int first = vertices.Count; vertices.AddRange(polygon); uv.AddRange(polygon.Select(p => new Vector2(p.x, p.z) * uvScale));
            double area = 0; for (int i = 0; i < polygon.Count; i++) { var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count]; area += (double)a.x * b.z - (double)b.x * a.z; }
            if (Math.Abs(area) < .00001)
            {
                // Vertical curb/wall quads have zero projected area.
                for (int i = 1; i < polygon.Count - 1; i++) triangles.AddRange(new[] { first, first + i, first + i + 1 });
                return;
            }
            if (area > 0) remaining.Reverse();
            int guard = 0;
            while (remaining.Count > 2 && guard++ < polygon.Count * polygon.Count)
            {
                bool cut = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int a = remaining[(i + remaining.Count - 1) % remaining.Count], b = remaining[i], c = remaining[(i + 1) % remaining.Count];
                    if (Cross(polygon[b] - polygon[a], polygon[c] - polygon[b]) >= -.000001f) continue;
                    bool occupied = remaining.Any(k => k != a && k != b && k != c && InTriangle(polygon[k], polygon[a], polygon[b], polygon[c]));
                    if (occupied) continue;
                    triangles.AddRange(new[] { first + a, first + b, first + c }); remaining.RemoveAt(i); cut = true; break;
                }
                if (!cut) throw new Exception("Road polygon could not be triangulated: " + string.Join(" / ", polygon.Select(p => p.ToString("F4"))));
            }
        }
        static float Cross(Vector3 a, Vector3 b) => a.x * b.z - a.z * b.x;
        static bool InTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
            => Cross(b - a, p - a) < -.000001f && Cross(c - b, p - b) < -.000001f && Cross(a - c, p - c) < -.000001f;
        public Mesh Mesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
    public sealed class Map
    {
        public readonly Dictionary<string, Surface>[] cells;
        public readonly Vector3 origin;
        public readonly int count;
        public readonly float size;
        public Map(Vector3 origin, int count = 4, float size = 256)
        { this.origin = origin; this.count = count; this.size = size; cells = Enumerable.Range(0, count * count).Select(_ => new Dictionary<string, Surface>()).ToArray(); }
        public void Polygon(string material, params Vector3[] points)
        {
            float minX = points.Min(p => p.x), maxX = points.Max(p => p.x), minZ = points.Min(p => p.z), maxZ = points.Max(p => p.z);
            int x0 = Mathf.Clamp(Mathf.FloorToInt((minX - origin.x) / size), 0, count - 1), x1 = Mathf.Clamp(Mathf.FloorToInt((maxX - origin.x - .0001f) / size), 0, count - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((minZ - origin.z) / size), 0, count - 1), z1 = Mathf.Clamp(Mathf.FloorToInt((maxZ - origin.z - .0001f) / size), 0, count - 1);
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
            {
                var polygon = points.ToList();
                polygon = Clip(polygon, 0, origin.x + x * size, true); polygon = Clip(polygon, 0, origin.x + (x + 1) * size, false);
                polygon = Clip(polygon, 2, origin.z + z * size, true); polygon = Clip(polygon, 2, origin.z + (z + 1) * size, false);
                if (polygon.Count < 3) continue;
                var cell = cells[z * count + x]; if (!cell.ContainsKey(material)) cell[material] = new Surface();
                cell[material].Polygon(polygon, material == "Asphalt" ? .125f : .5f);
            }
        }
        static List<Vector3> Clip(List<Vector3> input, int axis, float edge, bool greater)
        {
            var result = new List<Vector3>(); if (input.Count == 0) return result;
            var before = input[input.Count - 1]; bool insideBefore = greater ? before[axis] >= edge : before[axis] <= edge;
            foreach (var current in input)
            {
                bool inside = greater ? current[axis] >= edge : current[axis] <= edge;
                if (inside != insideBefore) result.Add(Vector3.Lerp(before, current, (edge - before[axis]) / (current[axis] - before[axis])));
                if (inside) result.Add(current); before = current; insideBefore = inside;
            }
            return result;
        }
        public void Rect(string material, Vector3 centre, Vector3 right, Vector3 forward, float width, float length)
        { Polygon(material, centre - right * width / 2 - forward * length / 2, centre - right * width / 2 + forward * length / 2, centre + right * width / 2 + forward * length / 2, centre + right * width / 2 - forward * length / 2); }
        public void Stroke(string material, Vector3 a, Vector3 b, float width)
        { var forward = (b - a).normalized; Rect(material, (a + b) / 2, CityRoadNetwork.Right(forward), forward, width, Vector3.Distance(a, b)); }
    }
    public static Vector3[] Boundary(CityRoadNetwork n, int arms)
    {
        float h = n.HalfRoad, k = h + n.cornerRadius, r = n.cornerRadius; var points = new List<Vector3>();
        void Add(Vector3 p) { if (points.Count == 0 || Vector3.Distance(points[points.Count - 1], p) > .0001f) points.Add(p); }
        for (int arm = 0; arm < 4; arm++)
        {
            var q = Quaternion.Euler(0, arm * 90, 0); bool a = (arms & (1 << arm)) != 0, b = (arms & (1 << ((arm + 1) % 4))) != 0;
            if (a) Add(q * new Vector3(h, 0, n.halfJunction));
            if (a && b)
            {
                for (int i = 0; i <= 8; i++) { float angle = (180 + 90f * i / 8) * Mathf.Deg2Rad; Add(q * new Vector3(k + Mathf.Cos(angle) * r, 0, k + Mathf.Sin(angle) * r)); }
            }
            else Add(q * new Vector3(h, 0, h));
            if (b) Add(q * new Vector3(n.halfJunction, 0, h));
        }
        if (Vector3.Distance(points[0], points[points.Count - 1]) < .001f) points.RemoveAt(points.Count - 1);
        return points.ToArray();
    }
    static bool PortEdge(CityRoadNetwork n, Vector3 a, Vector3 b)
        => Mathf.Abs(a.x - b.x) < .001f && Mathf.Abs(Mathf.Abs(a.x) - n.halfJunction) < .001f || Mathf.Abs(a.z - b.z) < .001f && Mathf.Abs(Mathf.Abs(a.z) - n.halfJunction) < .001f;
    public static void Junction(Map map, CityRoadNetwork n, CityRoadNode node)
    {
        var boundary = node.boundary; var offset = new Vector3[boundary.Length];
        // Every finite junction is star-shaped about its centre. Use that
        // contract directly; no arbitrary concave polygon tessellation needed.
        var centre = node.position + Vector3.up * AsphaltY;
        for (int i = 0; i < boundary.Length; i++)
            map.Polygon("Asphalt", centre, boundary[i] + centre, boundary[(i + 1) % boundary.Length] + centre);
        for (int i = 0; i < boundary.Length; i++)
        {
            var previous = boundary[(i + boundary.Length - 1) % boundary.Length]; var p = boundary[i]; var next = boundary[(i + 1) % boundary.Length];
            var normalA = -CityRoadNetwork.Right((p - previous).normalized); var normalB = -CityRoadNetwork.Right((next - p).normalized);
            if (PortEdge(n, previous, p)) offset[i] = p + normalB * n.sidewalkWidth;
            else if (PortEdge(n, p, next)) offset[i] = p + normalA * n.sidewalkWidth;
            else { var bisector = (normalA + normalB).normalized; offset[i] = p + bisector * (n.sidewalkWidth / Mathf.Max(.15f, Vector3.Dot(bisector, normalA))); }
        }
        for (int i = 0; i < boundary.Length; i++)
        {
            int j = (i + 1) % boundary.Length; if (PortEdge(n, boundary[i], boundary[j])) continue;
            var a = boundary[i] + node.position; var b = boundary[j] + node.position; var c = offset[j] + node.position; var d = offset[i] + node.position;
            map.Polygon("Pavement", a + Vector3.up * n.curbHeight, b + Vector3.up * n.curbHeight, c + Vector3.up * n.curbHeight, d + Vector3.up * n.curbHeight);
            map.Polygon("Curb", b + Vector3.up * AsphaltY, a + Vector3.up * AsphaltY, a + Vector3.up * n.curbHeight, b + Vector3.up * n.curbHeight);
        }
        for (int arm = 0; arm < 4; arm++) if ((node.arms & (1 << arm)) != 0) Approach(map, n, node, arm);
    }
    public static void Straight(Map map, CityRoadNetwork n, Vector3 a, Vector3 b)
    {
        var forward = (b - a).normalized; var right = CityRoadNetwork.Right(forward); float length = Vector3.Distance(a, b), h = n.HalfRoad;
        map.Rect("Asphalt", (a + b) / 2 + Vector3.up * AsphaltY, right, forward, h * 2, length);
        foreach (int side in new[] { -1, 1 })
        {
            map.Rect("Pavement", (a + b) / 2 + right * side * (h + n.sidewalkWidth / 2) + Vector3.up * n.curbHeight, right, forward, n.sidewalkWidth, length);
            map.Polygon("Curb", a + right * side * h + Vector3.up * AsphaltY, b + right * side * h + Vector3.up * AsphaltY,
                b + right * side * h + Vector3.up * n.curbHeight, a + right * side * h + Vector3.up * n.curbHeight);
            map.Stroke("Yellow", a + right * side * .13f + Vector3.up * PaintY, b + right * side * .13f + Vector3.up * PaintY, .12f);
            map.Stroke("White", a + right * side * (h - n.edgeWidth) + Vector3.up * PaintY, b + right * side * (h - n.edgeWidth) + Vector3.up * PaintY, .12f);
            for (float s = 1; s < length; s += 8)
            {
                float end = Mathf.Min(length, s + 3);
                map.Stroke("White", a + forward * s + right * side * n.laneWidth + Vector3.up * PaintY,
                    a + forward * end + right * side * n.laneWidth + Vector3.up * PaintY, .15f);
            }
        }
    }
    static void Approach(Map map, CityRoadNetwork n, CityRoadNode node, int arm)
    {
        var outward = CityRoadNetwork.Direction(arm); var incoming = -outward; var right = CityRoadNetwork.Right(incoming);
        Vector3 At(float x, float distance) => node.position + right * x + outward * distance + Vector3.up * PaintY;
        // Zebra stripes run across the full road and stop before the curb.
        for (float x = -6.25f; x < 6.4f; x += 1)
            map.Rect("White", At(x, n.crossingDistance), right, outward, .5f, 3);
        map.Rect("White", At(n.laneWidth, n.stopDistance), right, outward, n.laneWidth * 2, .4f);
        foreach (int side in new[] { -1, 1 })
        {
            map.Stroke("Yellow", At(side * .13f, 16.5f), At(side * .13f, n.halfJunction), .12f);
            map.Stroke("White", At(side * (n.HalfRoad - n.edgeWidth), 16.5f), At(side * (n.HalfRoad - n.edgeWidth), n.halfJunction), .12f);
        }
        float bikeX = n.LaneOffset(1);
        map.Rect("Green", At(bikeX, 18.75f), right, outward, 3, 4.8f);
        map.Rect("White", At(bikeX, 16.35f), right, outward, 3, .13f);
        foreach (float x in new[] { bikeX - 1.5f, bikeX + 1.5f }) map.Stroke("White", At(x, 16.35f), At(x, 21.15f), .13f);
        MotorcycleText(map, At(bikeX, 18.75f) + Vector3.up * .002f, right, incoming);
        for (int lane = 0; lane < 2; lane++)
        {
            var options = n.movements.Where(m => m.node == Array.IndexOf(n.nodes, node) && m.incomingArm == arm && m.lane == lane).Select(m => m.turn).Distinct().ToArray();
            Arrow(map, At(n.LaneOffset(lane), n.halfJunction + 10), right, incoming, options);
        }
    }
    public static void Arrow(Map map, Vector3 origin, Vector3 right, Vector3 forward, int[] turns)
    {
        void Line(Vector3 a, Vector3 b) => map.Stroke("White", origin + right * a.x + forward * a.z, origin + right * b.x + forward * b.z, .24f);
        Line(new Vector3(0, 0, -1.7f), new Vector3(0, 0, .35f));
        foreach (int turn in turns)
        {
            if (turn == 0)
            {
                Line(new Vector3(0, 0, 0), new Vector3(0, 0, 1.2f));
                map.Polygon("White", origin + forward * 2, origin + right * .62f + forward * .95f, origin - right * .62f + forward * .95f);
            }
            else
            {
                Line(new Vector3(0, 0, .1f), new Vector3(0, 0, .65f));
                Line(new Vector3(0, 0, .65f), new Vector3(turn * .9f, 0, .65f));
                map.Polygon("White", origin + right * turn * 1.65f + forward * .65f,
                    origin + right * turn * .62f + forward * 1.25f, origin + right * turn * .62f + forward * .05f);
            }
        }
    }
    static void MotorcycleText(Map map, Vector3 centre, Vector3 right, Vector3 forward)
    {
        int x = Mathf.Clamp(Mathf.FloorToInt((centre.x - map.origin.x) / map.size), 0, map.count - 1), z = Mathf.Clamp(Mathf.FloorToInt((centre.z - map.origin.z) / map.size), 0, map.count - 1);
        var cell = map.cells[z * map.count + x]; if (!cell.ContainsKey("Stencil")) cell["Stencil"] = new Surface(); var mesh = cell["Stencil"]; int first = mesh.vertices.Count;
        mesh.vertices.AddRange(new[] { centre - right * 1.1f - forward * .65f, centre - right * 1.1f + forward * .65f, centre + right * 1.1f + forward * .65f, centre + right * 1.1f - forward * .65f });
        mesh.uv.AddRange(new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right }); mesh.triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
    }
}
