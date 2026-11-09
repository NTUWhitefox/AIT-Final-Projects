// Project-authored low-cost street context, after the road/traffic foundation.
using System;
using System.Linq;
using System.Collections.Generic;
using AIT.Riding;
using UnityEditor;
using UnityEngine;

public static class GridCityScenery
{
    public static void Materials(Dictionary<string, Material> materials)
    {
        var colours = new Dictionary<string, Color> {
            { "Facade0",new Color(.62f,.61f,.55f) }, { "Facade1",new Color(.58f,.64f,.65f) },
            { "Facade2",new Color(.7f,.61f,.52f) }, { "Facade3",new Color(.57f,.61f,.54f) },
            { "Windows",new Color(.12f,.2f,.24f) }, { "Roof",new Color(.43f,.46f,.46f) },
            { "Trunks",new Color(.29f,.23f,.17f) }, { "Leaves",new Color(.2f,.34f,.2f) }, { "Signs",Color.white } };
        foreach (var pair in colours)
        {
            string path = GridCityBuilder.Root + "/" + pair.Key + ".mat"; var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_BaseColor", pair.Value); material.SetFloat("_Smoothness", pair.Key == "Windows" ? .22f : .08f); material.enableInstancing = true;
            if (pair.Key == "Signs")
            {
                const string atlas = "Assets/MotorSimulatorAssets/Models/CitySignage/SignAtlas.png";
                var importer = AssetImporter.GetAtPath(atlas) as TextureImporter;
                if (!importer) throw new Exception("Run Tools/create_city_signage.py first");
                if (importer.anisoLevel != 8 || importer.textureShape != TextureImporterShape.Texture2D)
                { importer.textureShape = TextureImporterShape.Texture2D; importer.anisoLevel = 8; importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Clamp; importer.SaveAndReimport(); }
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(atlas); if (!texture) throw new Exception("Sign atlas texture not imported");
                material.SetTexture("_BaseMap", texture); material.SetTexture("_MainTex", texture);
            }
            EditorUtility.SetDirty(material); materials[pair.Key] = material;
        }
    }
    public static void Build(GridRoadGeometry.Map map, CityRoadNetwork network, List<string> report)
    {
        int buildings = 0;
        for (int z = 0; z < network.count - 1; z++) for (int x = 0; x < network.count - 1; x++)
        {
            var low = network.nodes[network.Index(x, z)].position;
            for (int side = 0; side < 4; side++) foreach (float along in new[] { 44f, 84f })
            {
                var normal = CityRoadNetwork.Direction(side); var right = Vector3.Cross(normal, Vector3.up);
                var centre = low + new Vector3(64, 0, 64); var front = centre + normal * 53.5f + right * (along - 64);
                int variant = (x + z + side + (along > 64 ? 1 : 0)) % 4; float height = 12.5f + ((x * 3 + z + side) % 4) * 3;
                Building(map, front, normal, right, height, variant, 16 + (x + z + side) % 8); buildings++;
            }
        }
        foreach (var link in network.links)
        {
            var a = network.nodes[link.from].position; var direction = (network.nodes[link.to].position - a).normalized; var right = CityRoadNetwork.Right(direction);
            foreach (int side in new[] { -1, 1 }) Tree(map, a + direction * (side < 0 ? 48 : 80) + right * side * (network.HalfRoad + network.sidewalkWidth / 2) + Vector3.up * network.curbHeight);
        }
        foreach (var node in network.nodes)
        {
            var basePoint = node.position + new Vector3(8.8f, network.curbHeight, 8.8f);
            Box(map, "SignalMetal", basePoint + Vector3.up * 1.35f, new Vector3(.09f, 2.7f, .09f), Quaternion.identity);
            Sign(map, basePoint + Vector3.up * 2.65f, Vector3.forward, 2.1f, .5f, node.cell.x);
            Sign(map, basePoint + Vector3.up * 2.65f + Vector3.back * .005f, Vector3.back, 2.1f, .5f, node.cell.x);
            Sign(map, basePoint + Vector3.up * 2.05f, Vector3.right, 2.1f, .5f, 8 + node.cell.y);
            Sign(map, basePoint + Vector3.up * 2.05f + Vector3.left * .005f, Vector3.left, 2.1f, .5f, 8 + node.cell.y);
        }
        report.Add(buildings + " low-cost mid-rise buildings, recessed shopfronts/arcade columns, Chinese street/shop atlas and " + network.links.Length * 2 + " street trees. Mesh groups remain per chunk.");
    }
    static void Building(GridRoadGeometry.Map map, Vector3 front, Vector3 outward, Vector3 right, float height, int variant, int sign)
    {
        var rotation = Quaternion.LookRotation(outward); string facade = "Facade" + variant;
        Box(map, facade, front - outward * 9 + Vector3.up * ((height + 3.5f) / 2), new Vector3(28, height - 3.5f, 18), rotation);
        Box(map, facade, front - outward * 10.5f + Vector3.up * 1.75f, new Vector3(28, 3.5f, 15), rotation);
        Box(map, "Roof", front - outward * 9 + Vector3.up * (height + .2f), new Vector3(28.3f, .4f, 18.3f), rotation);
        Box(map, "Roof", front - outward * 8 + right * 7 + Vector3.up * (height + 1), new Vector3(3, 1.6f, 3), rotation);
        // The covered setback remains behind the public sidewalk, never on asphalt.
        foreach (float x in new[] { -13.5f, -4.5f, 4.5f, 13.5f })
            Box(map, facade, front + right * x - outward * .3f + Vector3.up * 1.7f, new Vector3(.45f, 3.4f, .45f), rotation);
        Box(map, "Roof", front - outward * 1.5f + Vector3.up * 3.35f, new Vector3(28.5f, .25f, 3.5f), rotation);
        for (int floor = 0; floor < (height - 4) / 3; floor++) for (int column = 0; column < 5; column++)
        {
            var centre = front + right * ((column - 2) * 5) + Vector3.up * (5.2f + floor * 3) + outward * .015f;
            Flat(map, "Windows", centre, outward, 2.2f, 1.55f);
        }
        for (int shop = 0; shop < 3; shop++)
        {
            var centre = front + right * (shop - 1) * 8 - outward * 2.98f;
            Flat(map, "Windows", centre + Vector3.up * 1.4f, outward, 6.4f, 2.6f);
            Sign(map, front + right * (shop - 1) * 8 + outward * .28f + Vector3.up * 3.05f, outward, 6.8f, .9f, 16 + (sign - 16 + shop) % 8);
        }
        // Side/rear windows prevent featureless boxes at corners and in mirrors.
        foreach (int side in new[] { -1, 1 }) for (int floor = 0; floor < (height - 4) / 3; floor++) for (int column = 0; column < 3; column++)
            Flat(map, "Windows", front - outward * (4 + column * 5) + right * side * 14.015f + Vector3.up * (5.2f + floor * 3), right * side, 1.8f, 1.55f);
    }
    static void Tree(GridRoadGeometry.Map map, Vector3 centre)
    {
        Box(map, "Trunks", centre + Vector3.up * 1.7f, new Vector3(.2f, 3.4f, .2f), Quaternion.identity);
        // Fixed low-poly canopy; opaque, no alpha-leaf overdraw or distant silhouette LOD changes.
        var top = centre + Vector3.up * 5.7f; var bottom = centre + Vector3.up * 2.9f;
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.PI / 6, b = (i + 1) * Mathf.PI / 6;
            var p = centre + new Vector3(Mathf.Cos(a) * 1.6f, 4.15f, Mathf.Sin(a) * 1.6f);
            var q = centre + new Vector3(Mathf.Cos(b) * 1.6f, 4.15f, Mathf.Sin(b) * 1.6f);
            Triangle(map, "Leaves", top, p, q); Triangle(map, "Leaves", bottom, q, p);
        }
    }
    public static void Box(GridRoadGeometry.Map map, string material, Vector3 centre, Vector3 size, Quaternion rotation)
    {
        var v = new Vector3[8]; for (int i = 0; i < 8; i++) v[i] = centre + rotation * new Vector3((i % 2 == 0 ? -1 : 1) * size.x / 2, (i / 2 % 2 == 0 ? -1 : 1) * size.y / 2, (i / 4 == 0 ? -1 : 1) * size.z / 2);
        int[][] faces = { new[] { 0, 1, 3, 2 }, new[] { 5, 4, 6, 7 }, new[] { 4, 0, 2, 6 }, new[] { 1, 5, 7, 3 }, new[] { 2, 3, 7, 6 }, new[] { 4, 5, 1, 0 } };
        foreach (var face in faces) Raw(map, material, face.Reverse().Select(i => v[i]).ToArray(), null);
    }
    static void Flat(GridRoadGeometry.Map map, string material, Vector3 centre, Vector3 normal, float width, float height)
    {
        var right = Vector3.Cross(normal, Vector3.up);
        Raw(map, material, new[] { centre - right * width / 2 - Vector3.up * height / 2, centre - right * width / 2 + Vector3.up * height / 2,
            centre + right * width / 2 + Vector3.up * height / 2, centre + right * width / 2 - Vector3.up * height / 2 }, null);
    }
    static void Sign(GridRoadGeometry.Map map, Vector3 centre, Vector3 normal, float width, float height, int label)
    {
        var right = Vector3.Cross(normal, Vector3.up); float x = label % 4 / 4f, y = 1 - (label / 4 + 1) / 8f;
        Raw(map, "Signs", new[] { centre - right * width / 2 - Vector3.up * height / 2, centre - right * width / 2 + Vector3.up * height / 2,
            centre + right * width / 2 + Vector3.up * height / 2, centre + right * width / 2 - Vector3.up * height / 2 },
            new[] { new Vector2(x, y), new Vector2(x, y + .125f), new Vector2(x + .25f, y + .125f), new Vector2(x + .25f, y) });
    }
    static void Triangle(GridRoadGeometry.Map map, string material, Vector3 a, Vector3 b, Vector3 c) => Raw(map, material, new[] { a, c, b }, null);
    static void Raw(GridRoadGeometry.Map map, string material, Vector3[] vertices, Vector2[] uv)
    {
        var centre = vertices.Aggregate(Vector3.zero, (a, b) => a + b) / vertices.Length;
        int x = Mathf.Clamp(Mathf.FloorToInt((centre.x - map.origin.x) / map.size), 0, map.count - 1), z = Mathf.Clamp(Mathf.FloorToInt((centre.z - map.origin.z) / map.size), 0, map.count - 1);
        var cell = map.cells[z * map.count + x]; if (!cell.ContainsKey(material)) cell[material] = new GridRoadGeometry.Surface(); var mesh = cell[material];
        int first = mesh.vertices.Count; mesh.vertices.AddRange(vertices); mesh.uv.AddRange(uv ?? vertices.Select(v => new Vector2(v.x, v.y)).ToArray());
        for (int i = 1; i < vertices.Length - 1; i++) mesh.triangles.AddRange(new[] { first, first + i, first + i + 1 });
    }
}
