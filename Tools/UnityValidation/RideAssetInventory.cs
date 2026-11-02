// CLI only: inspect source models in the ignored Unity project, never an AIT menu.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class RideAssetInventory
{
    [Serializable] sealed class ClipInfo { public string name; public float seconds, framesPerSecond; }
    [Serializable] sealed class ModelInfo
    {
        public string path; public int triangles, meshes, materials, skinRenderers, bones;
        public Vector3 boundsMetres;
        public ClipInfo[] clips;
    }
    [Serializable] sealed class Report { public List<ModelInfo> models = new List<ModelInfo>(); }
    public static void Run()
    {
        var report = new Report();
        string[] paths = {
            "Assets/AssetInspection/rp_nathan_animated_003_walking.fbx",
            "Assets/AssetInspection/rp_sophia_animated_003_idling.fbx",
            "Assets/Samples/XR Hands/1.8.1/HandVisualizer/Models/LeftHand.fbx",
            "Assets/Samples/XR Hands/1.8.1/HandVisualizer/Models/RightHand.fbx",
        };
        foreach (string path in paths)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!source) throw new FileNotFoundException(path);
            var obj = UnityEngine.Object.Instantiate(source);
            try
            {
                var skins = obj.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var renderers = obj.GetComponentsInChildren<Renderer>(true);
                var meshes = obj.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh)
                    .Concat(skins.Select(s => s.sharedMesh)).Where(m => m).Distinct().ToArray();
                Bounds bounds = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                report.models.Add(new ModelInfo {
                    path = path, meshes = meshes.Length,
                    triangles = meshes.Sum(m => Enumerable.Range(0, m.subMeshCount).Sum(i => (int)m.GetIndexCount(i) / 3)),
                    skinRenderers = skins.Length, bones = skins.SelectMany(s => s.bones).Distinct().Count(),
                    materials = renderers.SelectMany(r => r.sharedMaterials).Distinct().Count(), boundsMetres = bounds.size,
                    clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__"))
                        .Select(c => new ClipInfo { name = c.name, seconds = c.length, framesPerSecond = c.frameRate }).ToArray(),
                });
            }
            finally { UnityEngine.Object.DestroyImmediate(obj); }
        }
        Directory.CreateDirectory("Personal");
        File.WriteAllText("Personal/city-asset-inventory.json", JsonUtility.ToJson(report, true));
    }
}
