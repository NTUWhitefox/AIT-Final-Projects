// Offline import/derivation, only in ignored authoring clone.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using AIT.Riding;
using UnityEditor;
using UnityEngine;

public static class GridPedestrianBuilder
{
    const string Models = "Assets/MotorSimulatorAssets/Models/QuaterniusPedestrians/";
    const string Root = GridCityBuilder.Root;
    public static void Build(CityRoadRuntime runtime, List<string> report)
    {
        var prefabs = new List<GameObject>();
        foreach (string name in new[] { "Casual_Male", "Casual_Female", "Casual2_Male" }) prefabs.Add(Derive(name, report));
        int[] nodes = { runtime.network.Index(2, 2), runtime.network.Index(2, 3), runtime.network.Index(3, 5), runtime.network.Index(5, 5), runtime.network.Index(6, 3), runtime.network.Index(4, 1) };
        for (int i = 0; i < 12; i++)
        {
            var obj = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i % prefabs.Count]); obj.name = "Pedestrian " + i; obj.transform.SetParent(runtime.transform);
            var ped = obj.GetComponent<CityPedestrian>(); ped.actorId = "P" + i.ToString("00"); ped.runtime = runtime; ped.node = nodes[i / 2]; ped.arm = i % 2 == 0 ? 0 : 1; ped.initialWait = (i % 3) * 2;
            var outward = CityRoadNetwork.Direction(ped.arm); var across = CityRoadNetwork.Right(-outward);
            obj.transform.SetPositionAndRotation(runtime.network.nodes[ped.node].position + outward * runtime.network.crossingDistance - across * (runtime.network.HalfRoad + runtime.network.sidewalkWidth / 2) + Vector3.up * runtime.network.curbHeight, Quaternion.LookRotation(across));
            var visibility = obj.AddComponent<CityActorVisibility>(); visibility.session = runtime.session; visibility.visibleDistance = 180;
        }
        report.Add("12 persistent, animated Quaternius CC0 pedestrians at six junctions. Crossing reservation holds all vehicle signals red until clear.");
    }
    static GameObject Derive(string name, List<string> report)
    {
        string path = Models + name + ".fbx"; var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        if (!importer) throw new Exception("Pedestrian source missing: " + path);
        if (importer.animationType != ModelImporterAnimationType.Generic || !importer.importAnimation || importer.clipAnimations.Length != 2 || importer.materialLocation != ModelImporterMaterialLocation.InPrefab)
        {
            importer.animationType = ModelImporterAnimationType.Generic; importer.importAnimation = true;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (var mapping in importer.GetExternalObjectMap().Where(p => p.Key.type == typeof(Material)).ToArray()) importer.RemoveRemap(mapping.Key);
            importer.clipAnimations = importer.defaultClipAnimations.Where(c => c.name.EndsWith("|Idle") || c.name.EndsWith("|Walk")).ToArray();
            importer.SaveAndReimport();
        }
        var assets = AssetDatabase.LoadAllAssetsAtPath(path); var clips = assets.OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
        report.Add(name + " clips: " + string.Join(",", clips.Select(c => c.name)));
        var idle = clips.FirstOrDefault(c => c.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0);
        var walk = clips.FirstOrDefault(c => c.name.IndexOf("walk", StringComparison.OrdinalIgnoreCase) >= 0);
        if (!idle || !walk) throw new Exception("Idle/walk animation not found for " + name);
        AnimationClip Copy(AnimationClip source, string suffix)
        {
            string target = Root + "/" + name + "_" + suffix + ".anim"; var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(target);
            if (!clip) { clip = UnityEngine.Object.Instantiate(source); AssetDatabase.CreateAsset(clip, target); } else EditorUtility.CopySerialized(source, clip);
            clip.name = name + "_" + suffix; clip.legacy = true; clip.wrapMode = WrapMode.Loop;
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = true; AnimationUtility.SetAnimationClipSettings(clip, settings); EditorUtility.SetDirty(clip); return clip;
        }
        var root = new GameObject(name + " pedestrian"); var scale = new GameObject("Height normalization"); scale.transform.SetParent(root.transform, false);
        var model = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path)); model.transform.SetParent(scale.transform, false);
        foreach (var animator in model.GetComponentsInChildren<Animator>()) UnityEngine.Object.DestroyImmediate(animator);
        idle.SampleAnimation(model, .35f);
        var bounds = Bounds(model); scale.transform.localScale = Vector3.one * (1.72f / bounds.size.y);
        bounds = Bounds(model); scale.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        if (Mathf.Abs(Bounds(model).size.y - 1.72f) > .02f) throw new Exception("Pedestrian normalization failed: " + name);
        var converted = new Dictionary<Material, Material>();
        foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            renderer.sharedMaterials = renderer.sharedMaterials.Select(source => {
                if (converted.TryGetValue(source, out var existing)) return existing;
                string safe = string.Concat(source.name.Select(c => char.IsLetterOrDigit(c) ? c : '_')); string target = Root + "/Human_" + name + "_" + safe + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(target); if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, target); }
                var colour = source.HasProperty("_Color") ? source.color : Color.white;
                if (source.name.IndexOf("Skin", StringComparison.OrdinalIgnoreCase) >= 0)
                    colour = name == "Casual_Female" ? new Color(.62f,.41f,.29f) : name == "Casual2_Male" ? new Color(.47f,.3f,.21f) : new Color(.74f,.52f,.37f);
                material.SetColor("_BaseColor", colour); material.SetColor("_Color", colour);
                if (source.mainTexture) { material.SetTexture("_BaseMap", source.mainTexture); material.SetTexture("_MainTex", source.mainTexture); }
                material.SetFloat("_Smoothness", .1f); material.enableInstancing = true; EditorUtility.SetDirty(material); converted[source] = material; return material;
            }).ToArray();
        var animation = model.AddComponent<Animation>(); var idleCopy = Copy(idle, "Idle"); var walkCopy = Copy(walk, "Walk");
        animation.AddClip(idleCopy, idleCopy.name); animation.AddClip(walkCopy, walkCopy.name); animation.clip = idleCopy; animation.playAutomatically = true; animation.cullingType = AnimationCullingType.AlwaysAnimate;
        idleCopy.SampleAnimation(model, .35f);
        var controller = root.AddComponent<CityPedestrian>(); controller.animationPlayer = animation; controller.idleClip = idleCopy; controller.walkClip = walkCopy;
        var capsule = root.AddComponent<CapsuleCollider>(); capsule.center = Vector3.up * .86f; capsule.height = 1.72f; capsule.radius = .26f;
        string prefabPath = Root + "/" + name + "_Pedestrian.prefab"; var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath); UnityEngine.Object.DestroyImmediate(root); return prefab;
    }
    static Bounds Bounds(GameObject model)
    {
        var points = new List<Vector3>();
        foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = new Mesh(); renderer.BakeMesh(mesh, true);
            points.AddRange(mesh.vertices.Select(v => renderer.transform.TransformPoint(v))); UnityEngine.Object.DestroyImmediate(mesh);
        }
        if (points.Count == 0) throw new Exception("Pedestrian has no skinned geometry");
        var b = new Bounds(points[0], Vector3.zero); foreach (var point in points) b.Encapsulate(point); return b;
    }
}
