// Local preview authoring; imported only by the offline Unity build.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIT.Riding;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class KoboPedestrianBuilder
{
    [Serializable] sealed class HumanFit { public float height, pace; }
    [Serializable] sealed class Fits { public HumanFit male, female; }
    const string Model = "Street";
    static string root;
    static Fits fits;
    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    public static CityPedestrian Install(string previewRoot, CityRoadRuntime road, List<string> report)
    {
        root = previewRoot;
        fits = JsonUtility.FromJson<Fits>(File.ReadAllText(root + "/Pedestrians/PedestrianFit.json"));
        foreach (string sex in new[] { "Male", "Female" }) Import(sex);
        foreach (var ped in road.GetComponentsInChildren<CityPedestrian>())
        {
            // This scene replaces the prefab's visual and its internal animation references.
            if (PrefabUtility.IsOutermostPrefabInstanceRoot(ped.gameObject))
                PrefabUtility.UnpackPrefabInstance(ped.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var old = ped.animationPlayer.transform.parent;
            if (old.parent != ped.transform) throw new InvalidOperationException("Unexpected original pedestrian hierarchy");
            Object.DestroyImmediate(old.gameObject);
            bool female = ped.name.EndsWith("1");
            AddVisual(ped, female, female ? "#A35D4E" : "#51807C", report);
            ped.speed = female ? 1.16f : 1.27f;
        }

        float x = road.network.HalfRoad + road.network.sidewalkWidth * .48f;
        float y = road.network.curbHeight;
        var walker = AddSidewalk(road, "Sidewalk east", new Vector3(x, y, -78), new Vector3(x, y, -30), true, "#A35D4E", report);
        AddSidewalk(road, "Sidewalk west", new Vector3(-x, y, -64), new Vector3(-x, y, -106), false, "#706748", report);
        return walker;
    }

    static void Import(string sex)
    {
        string path = root + "/Pedestrians/" + Model + sex + ".fbx";
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.useFileScale = true;
        importer.importAnimation = true;
        importer.animationType = ModelImporterAnimationType.Legacy;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        importer.SaveAndReimport();
    }

    static CityPedestrian AddSidewalk(CityRoadRuntime road, string name, Vector3 start, Vector3 end, bool female, string shirt, List<string> report)
    {
        var gameObject = new GameObject(name);
        gameObject.transform.SetParent(road.transform, false);
        gameObject.transform.SetPositionAndRotation(start, Quaternion.LookRotation(end - start));
        var ped = gameObject.AddComponent<CityPedestrian>();
        ped.actorId = female ? "KB_Sidewalk_E" : "KB_Sidewalk_W";
        ped.runtime = road;
        ped.useSidewalkRoute = true;
        ped.sidewalkStart = start;
        ped.sidewalkEnd = end;
        ped.speed = female ? 1.16f : 1.27f;
        ped.initialWait = female ? .3f : 1.0f;
        gameObject.AddComponent<CapsuleCollider>();
        AddVisual(ped, female, shirt, report);
        return ped;
    }

    static void AddVisual(CityPedestrian ped, bool female, string shirt, List<string> report)
    {
        string sex = female ? "Female" : "Male";
        string path = root + "/Pedestrians/" + Model + sex + ".fbx";
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var human = Object.Instantiate(source, ped.transform, false);
        human.name = Model + sex;
        if (human.TryGetComponent<Animator>(out var animator)) Object.DestroyImmediate(animator);
        var animation = human.GetComponent<Animation>() ?? human.AddComponent<Animation>();
        var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(a => !a.name.StartsWith("__preview__")).ToArray();
        var idle = PrepareClip(sex, "Idle", clips);
        var walk = PrepareClip(sex, "Walk", clips);
        animation.AddClip(idle, idle.name);
        animation.AddClip(walk, walk.name);
        animation.clip = idle;
        animation.playAutomatically = false;
        animation.cullingType = AnimationCullingType.AlwaysAnimate;
        foreach (var r in human.GetComponentsInChildren<SkinnedMeshRenderer>())
            r.sharedMaterials = r.sharedMaterials.Select(m => Clothes(m.name, female, shirt)).ToArray();

        idle.SampleAnimation(human, .35f);
        var fit = female ? fits.female : fits.male;
        float localSize = BodyBounds(human).size.y;
        if (localSize < 1.4f || localSize > 2.1f) throw new InvalidOperationException($"Unexpected {sex} source height {localSize:F2} m");
        float scale = fit.height / localSize;
        human.transform.localScale = Vector3.one * scale;
        var body = BodyBounds(human);
        human.transform.position -= Vector3.up * (body.min.y - ped.transform.position.y);
        var head = human.GetComponentsInChildren<Transform>().Single(t => t.name == "Head");
        float heads = body.size.y / (body.max.y - head.position.y);
        if (heads < 6.6f || heads > 8.6f) throw new InvalidOperationException($"Unexpected {sex} head proportion: {heads:F1}");
        ped.animationPlayer = animation;
        ped.idleClip = idle;
        ped.walkClip = walk;
        ped.walkReferenceSpeed = fit.pace * scale;
        var capsule = ped.GetComponent<CapsuleCollider>();
        capsule.height = fit.height;
        capsule.center = Vector3.up * (fit.height * .5f);
        capsule.radius = female ? .21f : .24f;

        float floorOffset = 0;
        for (int i = 0; i < 12; ++i)
        {
            walk.SampleAnimation(human, walk.length * i / 12);
            floorOffset = Mathf.Max(floorOffset, Mathf.Abs(BodyBounds(human).min.y - ped.transform.position.y));
        }
        idle.SampleAnimation(human, .35f);
        if (floorOffset > .04f) throw new InvalidOperationException($"{sex} walk shoe contact off by {floorOffset:F3} m");
        report.Add($"{ped.name}: {fit.height:F2} m, {heads:F1} heads, walk {walk.length:F2} s, reference pace {ped.walkReferenceSpeed:F2} m/s, stance floor deviation <= {floorOffset:F3} m");
    }

    static AnimationClip PrepareClip(string sex, string label, AnimationClip[] clips)
    {
        var original = clips.SingleOrDefault(a => a.name == label || a.name.EndsWith("|" + label));
        if (!original) throw new InvalidOperationException($"Missing {label} in {sex} FBX: {string.Join(", ", clips.Select(a => a.name))}");
        var destination = root + "/Pedestrians/" + sex + label + ".anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(destination);
        if (clip) EditorUtility.CopySerialized(original, clip);
        else { clip = Object.Instantiate(original); AssetDatabase.CreateAsset(clip, destination); }
        clip.name = label;
        clip.legacy = true;
        clip.wrapMode = WrapMode.Loop;
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static Material Clothes(string slot, bool female, string shirt)
    {
        switch (slot.Split('.')[0])
        {
            case "Shirt": return Surface("Tee " + shirt.Substring(1), shirt);
            case "Trousers": return Surface(female ? "Dark denim" : "Slate trousers", female ? "#454D60" : "#4A5456");
            case "Shoes": return Surface("Canvas shoes", "#2D302F");
            case "Soles": return Surface("Sole rubber", "#8E8981");
            case "MI_Eyes": return Surface("Subtle eyes", "#EEE8DC", "PedestrianEye.png");
            case "MI_Hair_1": return Surface("Parted hair", "#514139", "PedestrianHair1.png", true);
            case "MI_Hair_2": return Surface("Tied hair", "#413D38", "PedestrianHair2.png", true);
            case "Skin": return Surface(female ? "Warm skin" : "Tan skin", female ? "#CA9B7E" : "#B78667");
            default: throw new InvalidOperationException("Unmapped pedestrian material: " + slot);
        }
    }

    static Material Surface(string name, string hex, string texture = null, bool cutout = false)
    {
        if (materials.TryGetValue(name, out var cached)) return cached;
        string path = root + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        ColorUtility.TryParseHtmlString(hex, out var colour);
        material.SetColor("_BaseColor", colour);
        material.SetFloat("_Smoothness", .10f);
        if (texture != null) material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(root + "/Pedestrians/" + texture));
        if (cutout)
        {
            material.SetFloat("_AlphaClip", 1);
            material.SetFloat("_Cutoff", .48f);
            material.SetFloat("_Cull", (int)CullMode.Off);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;
        }
        EditorUtility.SetDirty(material);
        materials.Add(name, material);
        return material;
    }

    public static Bounds BodyBounds(GameObject human)
    {
        var mesh = new Mesh();
        var points = new List<Vector3>();
        foreach (var r in human.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            r.BakeMesh(mesh, true);
            points.AddRange(mesh.vertices.Select(r.transform.TransformPoint));
        }
        Object.DestroyImmediate(mesh);
        if (points.Count == 0) throw new InvalidOperationException("Missing skinned pedestrian");
        var result = new Bounds(points[0], Vector3.zero);
        foreach (var point in points) result.Encapsulate(point);
        return result;
    }
}
