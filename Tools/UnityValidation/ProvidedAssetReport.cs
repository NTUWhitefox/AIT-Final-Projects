using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class ProvidedAssetReport
{
    [Serializable] sealed class Part
    {
        public string path, mesh; public Vector3 centre, size, pivot;
        public int triangles, submeshes; public string[] materials;
        public List<Island> islands;
    }
    [Serializable] sealed class Island { public int firstTriangle, triangles; public Vector3 centre, size; }
    static List<Island> Islands(Mesh mesh, Transform transform)
    {
        var v = mesh.vertices; var t = mesh.triangles;
        var parent = Enumerable.Range(0, v.Length).ToArray();
        int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        void Join(int a, int b) { parent[Find(a)] = Find(b); }
        var positions = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < v.Length; i++)
        {
            var key = Vector3Int.RoundToInt(v[i] * 10000);
            if (positions.TryGetValue(key, out int j)) Join(i, j); else positions[key] = i;
        }
        for (int i = 0; i < t.Length; i += 3) { Join(t[i], t[i+1]); Join(t[i], t[i+2]); }
        var groups = new Dictionary<int, List<int>>();
        for (int i = 0; i < t.Length; i += 3)
        { int key = Find(t[i]); if (!groups.ContainsKey(key)) groups[key] = new List<int>(); groups[key].Add(i); }
        var result = new List<Island>();
        foreach (var group in groups.Values)
        {
            var bounds = new Bounds(transform.TransformPoint(v[t[group[0]]]), Vector3.zero);
            foreach (int triangle in group) for (int j = 0; j < 3; j++) bounds.Encapsulate(transform.TransformPoint(v[t[triangle+j]]));
            result.Add(new Island { firstTriangle = group[0] / 3, triangles = group.Count, centre = bounds.center, size = bounds.size });
        }
        return result.OrderByDescending(i => i.triangles).ToList();
    }
    [Serializable] sealed class Model
    {
        public string path; public Vector3 size, centre; public List<Part> parts = new List<Part>();
    }
    [Serializable] sealed class Report { public List<Model> models = new List<Model>(); }
    public static void Run()
    {
        var report = new Report();
        foreach (string path in new[] { "Assets/MotorSimulatorAssets/Models/Test/scooter_d.FBX", "Assets/MotorSimulatorAssets/Models/Test/TraffixLight.FBX" })
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.isReadable = true; importer.SaveAndReimport(); // inspection copy only
            var obj = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            try
            {
                var model = new Model { path = path };
                var renderers = obj.GetComponentsInChildren<MeshRenderer>();
                var bounds = renderers[0].bounds;
                foreach (var r in renderers)
                {
                    var mesh = r.GetComponent<MeshFilter>().sharedMesh; bounds.Encapsulate(r.bounds);
                    model.parts.Add(new Part { path = AnimationUtility.CalculateTransformPath(r.transform, obj.transform), mesh = mesh.name,
                        centre = r.bounds.center, size = r.bounds.size, pivot = r.transform.position,
                        triangles = mesh.triangles.Length / 3, submeshes = mesh.subMeshCount,
                        materials = r.sharedMaterials.Select(m => m ? m.name : "<null>").ToArray(),
                        islands = Islands(mesh, r.transform) });
                }
                model.size = bounds.size; model.centre = bounds.center; report.models.Add(model);
            }
            finally { UnityEngine.Object.DestroyImmediate(obj); }
        }
        Directory.CreateDirectory("Personal");
        File.WriteAllText("Personal/provided-assets.json", JsonUtility.ToJson(report, true));
    }
}
