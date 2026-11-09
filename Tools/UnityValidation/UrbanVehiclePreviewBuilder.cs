// Offline authoring only. This file is not imported into the working Unity project.
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

public static class UrbanVehiclePreviewBuilder
{
    const string Root = "Assets/MotorSimulatorAssets/CityPreviewGenerated";
    const string Scooter = "Assets/MotorSimulatorAssets/Models/Test/scooter_d.FBX";
    const string Signal = "Assets/MotorSimulatorAssets/Models/Test/TraffixLight.FBX";
    const string Sedan = "Assets/MotorSimulatorAssets/Models/ComparisonSedan/red-four-door-sedan.fbx";
    const string Car26 = "Assets/MotorSimulatorAssets/Models/RealisticMobileCars - Pro3DModels/RMCar26/Prefabs/RMCar26.prefab";
    public const string ScenePath = "Assets/Scenes/UrbanVehiclePreview.unity";
    static readonly List<string> notes = new List<string>();

    public static void Run()
    {
        Directory.CreateDirectory(Root); Directory.CreateDirectory("Personal/VehiclePreview");
        foreach (string file in Directory.GetFiles("Assets/MotorSimulatorAssets/Models/Test/ScooterD_Textures", "*.png"))
        {
            string path = file.Replace('\\','/');
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            bool normal = path.EndsWith("_Normal.png", StringComparison.OrdinalIgnoreCase);
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal; importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Compressed; importer.compressionQuality = 90;
            importer.SaveAndReimport();
        }
        foreach (string path in new[] { Scooter, Signal, Sedan })
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.isReadable = true; importer.SaveAndReimport();
        }
        var scooter = MakeScooter();
        var signal = MakeSignal("TrafficLight_c_Mesh");
        var comparison = MakeCar();
        var car = MakeCar26();
        AssetDatabase.SaveAssets();
        BuildStreet(scooter, signal, car, comparison);
        File.WriteAllLines("Personal/VehiclePreview/asset-notes.txt", notes);
        AssetDatabase.SaveAssets();
        Capture("street.png", new Vector3(-85, 2.3f, -73), new Vector3(-84, 1.8f, -38), 75);
        // Asset close-ups use a separate empty preview scene and consistent lighting.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Lighting(); Ground();
        var obj = UnityEngine.Object.Instantiate(scooter); obj.transform.position = Vector3.zero;
        Capture("scooter.png", new Vector3(2.8f, 1.65f, 3.4f), new Vector3(0, .65f, 0), 38);
        Capture("scooter-cockpit.png", new Vector3(0, 1.35f, -.6f), new Vector3(0, .85f, .7f), 65);
        UnityEngine.Object.DestroyImmediate(obj);
        obj = UnityEngine.Object.Instantiate(car); obj.transform.position = Vector3.zero;
        obj.GetComponent<PreviewRoadVehicle>().enabled = false;
        foreach (var lod in obj.GetComponentsInChildren<LODGroup>()) lod.ForceLOD(0);
        Capture("sedan.png", new Vector3(5.6f, 2.9f, 6.7f), new Vector3(0, .7f, 0), 40);
        Capture("car26-rear.png", new Vector3(-5.6f, 2.1f, -6.7f), new Vector3(0, .7f, 0), 40);
        var frontDoor=obj.GetComponentsInChildren<Transform>().First(t=>t.name=="RMCar26_DoorFrontLeft");
        frontDoor.localRotation *= Quaternion.Euler(0,-55,0);
        Capture("car26-door.png", new Vector3(-4.7f, 2.2f, 2.9f), new Vector3(0, .7f, 0), 45);
        UnityEngine.Object.DestroyImmediate(obj);
        obj=UnityEngine.Object.Instantiate(comparison);obj.GetComponent<PreviewRoadVehicle>().enabled=false;
        Capture("comparison-sedan.png", new Vector3(5.6f, 2.9f, 6.7f), new Vector3(0, .7f, 0), 40);
        UnityEngine.Object.DestroyImmediate(obj);
        obj = UnityEngine.Object.Instantiate(signal); obj.transform.position = Vector3.zero;
        Capture("signal.png", new Vector3(-4, 4.7f, -7), new Vector3(-2, 4.6f, 0), 40);
        UnityEngine.Object.DestroyImmediate(obj);
        Debug.Log("Vehicle preview authored: " + ScenePath);
    }
    static GameObject Source(string path)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!source) throw new FileNotFoundException(path);
        return UnityEngine.Object.Instantiate(source);
    }
    static Material Mat(string name, Color colour, float metallic = 0, float smoothness = .3f, bool emission = false)
    {
        string path = Root + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", colour); m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smoothness);
        if (emission) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", colour * .1f); }
        m.enableInstancing = true; EditorUtility.SetDirty(m); return m;
    }
    static Bounds Bounds(GameObject obj)
    {
        var renderers = obj.GetComponentsInChildren<Renderer>(); var b = renderers[0].bounds;
        foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds); return b;
    }
    static GameObject Normalize(GameObject model, string name)
    {
        var bounds = Bounds(model); var root = new GameObject(name);
        model.transform.SetParent(root.transform, true);
        model.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        return root;
    }
    static GameObject SavePrefab(GameObject obj, string name)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(obj, Root + "/" + name + ".prefab");
        UnityEngine.Object.DestroyImmediate(obj); return prefab;
    }
    static GameObject MakeScooter()
    {
        var obj = Source(Scooter);
        foreach (var r in obj.GetComponentsInChildren<MeshRenderer>())
        {
            string name = r.sharedMaterial.name;
            var m = Mat(name, Color.white, 0, name.Contains("Tire") ? .18f : .5f);
            string prefix = "Assets/MotorSimulatorAssets/Models/Test/ScooterD_Textures/" + name;
            var diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "_diffuse.png");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "_Normal.png");
            if (!diffuse || !normal) throw new Exception("Texture pair missing: [" + name + "] diffuse=" + (diffuse ? diffuse.name : AssetDatabase.GetMainAssetTypeAtPath(prefix+"_diffuse.png")?.FullName) + " normal=" + (normal ? normal.name : AssetDatabase.GetMainAssetTypeAtPath(prefix+"_Normal.png")?.FullName));
            m.SetTexture("_BaseMap", diffuse); m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP");
            r.sharedMaterial = m;
        }
        var root = Normalize(obj, "TDAL Scooter D - parked appearance");
        notes.Add("Scooter D: " + Bounds(root).size + "; 7 material-group meshes; not a mechanically separated riding rig.");
        return SavePrefab(root, "TDAL_Scooter_D");
    }
    // Merge duplicated vertices by position, then identify disconnected surface islands.
    // Work on an independent mesh asset, preserving the supplied FBX.
    static List<List<int>> Islands(Mesh mesh)
    {
        var v = mesh.vertices; var t = mesh.triangles; var parent = Enumerable.Range(0, v.Length).ToArray();
        int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        void Join(int a, int b) { parent[Find(a)] = Find(b); }
        var positions = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < v.Length; i++)
        { var key = Vector3Int.RoundToInt(v[i] * 10000); if (positions.TryGetValue(key, out int j)) Join(i,j); else positions[key] = i; }
        for (int i = 0; i < t.Length; i += 3) { Join(t[i], t[i+1]); Join(t[i], t[i+2]); }
        var groups = new Dictionary<int, List<int>>();
        for (int i = 0; i < t.Length; i += 3)
        { int key = Find(t[i]); if (!groups.ContainsKey(key)) groups[key] = new List<int>(); groups[key].Add(i); }
        return groups.Values.ToList();
    }
    static GameObject MakeSignal(string part)
    {
        var original = Source(Signal);
        var renderer = original.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == part);
        var root = new GameObject("TDAL signal - coloured material slots");
        var obj = new GameObject("Signal"); obj.transform.SetParent(root.transform, false);
        var source = renderer.GetComponent<MeshFilter>().sharedMesh;
        var vertices = source.vertices; var triangles = source.triangles;
        var groups = Islands(source); var buckets = Enumerable.Range(0,5).Select(_ => new List<int>()).ToArray();
        var lens = new List<(float x, float z)>();
        foreach (var group in groups)
        {
            var b = new Bounds(renderer.transform.TransformPoint(vertices[triangles[group[0]]]), Vector3.zero);
            foreach (int i in group) for (int j = 0; j < 3; j++) b.Encapsulate(renderer.transform.TransformPoint(vertices[triangles[i+j]]));
            if (group.Count == 126 && b.size.x > .19f && b.size.x < .23f && b.size.y > .19f && b.size.z < .06f)
                lens.Add((b.center.x,b.center.z));
        }
        if (lens.Count < 3) throw new Exception("Signal lens islands could not be identified");
        foreach (var group in groups)
        {
            var b = new Bounds(renderer.transform.TransformPoint(vertices[triangles[group[0]]]), Vector3.zero);
            foreach (int i in group) for (int j = 0; j < 3; j++) b.Encapsulate(renderer.transform.TransformPoint(vertices[triangles[i+j]]));
            int slot = b.center.y > 4.7f && b.size.y < .6f && b.size.z > .08f ? 1 : 0;
            if (group.Count == 126 && b.size.x > .19f && b.size.x < .23f && b.size.y > .19f && b.size.z < .06f)
            {
                var bank = lens.Where(l => Mathf.Sign(l.z) == Mathf.Sign(b.center.z)).OrderBy(l => l.x).ToArray();
                int index = Array.FindIndex(bank, l => Mathf.Abs(l.x - b.center.x) < .02f);
                slot = 2 + (b.center.z < 0 ? index : 2 - index);
                if (slot < 2 || slot > 4) throw new Exception("Unexpected signal lamp arrangement");
            }
            foreach (int i in group) for (int j = 0; j < 3; j++) buckets[slot].Add(triangles[i+j]);
        }
        var mesh = UnityEngine.Object.Instantiate(source); mesh.name = "SignalC coloured submeshes";
        mesh.subMeshCount = 5; for (int i = 0; i < 5; i++) mesh.SetTriangles(buckets[i], i);
        string meshPath=Root+"/SignalC_Coloured.asset";
        var existing=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if(existing) { EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing; }
        else AssetDatabase.CreateAsset(mesh,meshPath);
        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        obj.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
        obj.transform.localScale = renderer.transform.lossyScale;
        var r = obj.AddComponent<MeshRenderer>();
        r.sharedMaterials = new[] { Mat("Signal Pole", new Color(.42f,.45f,.43f), .65f), Mat("Signal Housing",new Color(.025f,.03f,.028f)),
            Mat("Signal Red", Color.red, emission:true), Mat("Signal Amber", new Color(1,.55f,0), emission:true), Mat("Signal Green", Color.green, emission:true) };
        // The FBX contains four laid-out variants; reset the chosen variant to its mast pivot.
        obj.transform.position -= new Vector3(renderer.transform.position.x, 0, 0);
        var control = root.AddComponent<PreviewTrafficSignal>(); control.signalRenderer = r; control.Apply();
        notes.Add("TrafficLight C: " + source.triangles.Length / 3 + " triangles; " + lens.Count + " lens islands; 5 editable material slots.");
        UnityEngine.Object.DestroyImmediate(original);
        return SavePrefab(root, "TDAL_TrafficLight_C");
    }
    static GameObject MakeCar()
    {
        var obj = Source(Sedan); obj.transform.rotation = Quaternion.Euler(0,90,0);
        var converted = new Dictionary<Material, Material>();
        foreach (var r in obj.GetComponentsInChildren<MeshRenderer>())
            r.sharedMaterials = r.sharedMaterials.Select(source =>
            {
                if (converted.TryGetValue(source, out var found)) return found;
                Color colour = source.HasProperty("_Color") ? source.color : Color.gray;
                string name = source.name.ToLowerInvariant();
                var m = Mat("Sedan_" + source.name.Replace('/', '_'), colour, name.Contains("metal") || name.Contains("chrome") ? .8f : .05f,
                    name.Contains("tyre") || name.Contains("tire") ? .18f : .65f);
                if (name.Contains("glass") || colour.a < .9f)
                {
                    colour.a = .5f; m.SetColor("_BaseColor", colour); m.SetFloat("_Surface",1); m.SetFloat("_ZWrite",0);
                    m.SetFloat("_SrcBlend",5); m.SetFloat("_DstBlend",10); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000;
                    m.SetOverrideTag("RenderType", "Transparent");
                }
                converted[source] = m; return m;
            }).ToArray();
        var root = Normalize(obj,"CC0 Sedan - straight route preview");
        var wheelParts = root.GetComponentsInChildren<MeshRenderer>().Where(r =>
            new[] { "tyre", "tire", "wheel", "rim" }.Any(w => r.name.ToLowerInvariant().Contains(w)) &&
            !r.name.ToLowerInvariant().Contains("steering") && r.bounds.center.y < .7f).ToArray();
        var pivots = new List<Transform>();
        foreach (var group in wheelParts.GroupBy(r => (r.bounds.center.x > 0 ? "R" : "L") + (r.bounds.center.z > 0 ? "F" : "B")))
        {
            var b = group.First().bounds; foreach (var r in group.Skip(1)) b.Encapsulate(r.bounds);
            var pivot = new GameObject("Wheel " + group.Key).transform; pivot.SetParent(root.transform,false); pivot.position = b.center;
            foreach (var r in group) r.transform.SetParent(pivot,true); pivots.Add(pivot);
        }
        var bounds = Bounds(root);
        var collider = root.AddComponent<BoxCollider>(); collider.center = new Vector3(0,bounds.size.y * .5f,0);
        collider.size = new Vector3(Mathf.Min(1.95f,bounds.size.x),bounds.size.y,bounds.size.z);
        var motion = root.AddComponent<PreviewRoadVehicle>(); motion.wheels = pivots.ToArray();
        int triangles = root.GetComponentsInChildren<MeshFilter>().Sum(m => m.sharedMesh.triangles.Length / 3);
        notes.Add("CC0 Sedan: " + bounds.size + "; " + triangles + " triangles; " + converted.Count + " materials; wheel pivots=" + pivots.Count);
        if (pivots.Count != 4) notes.Add("WARNING: Wheel grouping needs manual review.");
        return SavePrefab(root,"CC0_RedSedan");
    }
    static GameObject MakeCar26()
    {
        var obj=Source(Car26);var converted=new Dictionary<Material,Material>();
        foreach(var r in obj.GetComponentsInChildren<MeshRenderer>(true))
            r.sharedMaterials=r.sharedMaterials.Select(source=>
            {
                if(converted.TryGetValue(source,out var cached))return cached;
                var colour=source.GetColor("_Color");
                var m=Mat(source.name+"_URP",colour,source.GetFloat("_Metallic"),source.GetFloat("_Glossiness"));
                foreach(var pair in new[] { ("_MainTex","_BaseMap"), ("_BumpMap","_BumpMap"), ("_MetallicGlossMap","_MetallicGlossMap"), ("_EmissionMap","_EmissionMap"), ("_OcclusionMap","_OcclusionMap") })
                {
                    var texture=source.GetTexture(pair.Item1);m.SetTexture(pair.Item2,texture);
                    m.SetTextureScale(pair.Item2,source.GetTextureScale(pair.Item1));m.SetTextureOffset(pair.Item2,source.GetTextureOffset(pair.Item1));
                }
                if(m.GetTexture("_BumpMap"))m.EnableKeyword("_NORMALMAP");
                if(m.GetTexture("_MetallicGlossMap")) { m.EnableKeyword("_METALLICSPECGLOSSMAP");m.SetFloat("_Smoothness",source.GetFloat("_GlossMapScale")); }
                if(source.IsKeywordEnabled("_EMISSION")) { m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",source.GetColor("_EmissionColor")); }
                float mode=source.GetFloat("_Mode");
                if(mode>=2)
                {
                    m.SetFloat("_Surface",1);m.SetFloat("_ZWrite",0);m.SetFloat("_SrcBlend",5);m.SetFloat("_DstBlend",10);
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.renderQueue=3000;m.SetOverrideTag("RenderType","Transparent");
                }
                converted[source]=m;return m;
            }).ToArray();
        foreach(var c in obj.GetComponentsInChildren<Collider>())UnityEngine.Object.DestroyImmediate(c);
        var root=Normalize(obj,"Car26 URP preview");
        var motion=root.AddComponent<PreviewRoadVehicle>();
        motion.wheels=root.GetComponentsInChildren<Transform>().Where(t=>new[] { "RMCar26_WheelFrontLeft","RMCar26_WheelFrontRight","RMCar26_WheelRearLeft","RMCar26_WheelRearRight" }.Contains(t.name)).ToArray();
        if(motion.wheels.Length!=4)throw new Exception("Car26 wheel pivots missing");
        var bounds=Bounds(root);var collision=root.AddComponent<BoxCollider>();collision.center=new Vector3(0,bounds.size.y*.5f,0);collision.size=new Vector3(Mathf.Min(1.9f,bounds.size.x),bounds.size.y,bounds.size.z);
        var lods=root.GetComponentsInChildren<LODGroup>();
        int[] levels=Enumerable.Range(0,lods.Max(l=>l.lodCount)).Select(i=>lods.Sum(l=>
            i<l.lodCount?l.GetLODs()[i].renderers.OfType<MeshRenderer>().Sum(r=>
            {var m=r.GetComponent<MeshFilter>().sharedMesh;return Enumerable.Range(0,m.subMeshCount).Sum(s=>(int)m.GetIndexCount(s)/3); }):0)).ToArray();
        notes.Add("Car26: "+bounds.size+"; "+converted.Count+" URP materials; "+lods.Length+" LOD groups; triangle counts by LOD="+string.Join(",",levels)+"; four wheel pivots and front/rear door pivots retained.");
        UseFinestGeometry(root);
        return SavePrefab(root,"Car26_URP");
    }
    public static void UseFinestGeometry(GameObject car)
    {
        var lods=car.GetComponentsInChildren<LODGroup>(true);
        var finest=lods.SelectMany(l=>l.GetLODs()[0].renderers).Where(r=>r).ToHashSet();
        foreach(var lod in lods)
        {
            lod.ForceLOD(-1); lod.enabled=false;
            foreach(var r in lod.GetLODs().SelectMany(l=>l.renderers).Where(r=>r)) r.enabled=finest.Contains(r);
        }
    }
    static void BuildStreet(GameObject scooter, GameObject signal, GameObject car,GameObject comparison)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/FirstRideTest.unity");
        var root = new GameObject("Urban vehicle preview - simple street");
        var concrete = Mat("Concrete",new Color(.42f,.43f,.4f));
        var windows = Mat("Window",new Color(.09f,.15f,.18f),.3f,.7f);
        var paint = Mat("Markings",new Color(.88f,.87f,.79f));
        var sidewalk = Mat("Sidewalk",new Color(.36f,.35f,.32f));
        var asphalt = Mat("Cross street",new Color(.11f,.12f,.13f));
        Box("Cross street",root.transform,new Vector3(-85,.035f,0),new Vector3(70,.01f,8),asphalt,false);
        for (int side = -1; side <= 1; side += 2)
        {
            Box("Sidewalk",root.transform,new Vector3(-85+side*6,-.04f,-52),new Vector3(3,.1f,80),sidewalk,false);
            Box("Sidewalk beyond junction",root.transform,new Vector3(-85+side*6,-.04f,36),new Vector3(3,.1f,60),sidewalk,false);
            for (int i = 0; i < 11; i++)
            {
                float z = -85+i*14, height=8+(i%3)*3;
                if(Mathf.Abs(z)<10)continue;
                var building = Box("Simple building",root.transform,new Vector3(-85+side*13,height/2,z),new Vector3(9,height,11),concrete,true);
                for (int floor = 0; floor < (height-2)/3; floor++)
                    for (int col = 0; col < 3; col++)
                        Box("Window",root.transform,new Vector3(-85+side*8.45f,2+floor*3,z-3+col*3),new Vector3(.04f,1.5f,1.3f),windows,false);
                Box("Shop awning",root.transform,new Vector3(-85+side*7.5f,2.9f,z),new Vector3(2, .18f,9),concrete,false);
            }
        }
        for (int i=0;i<8;i++) Box("Crosswalk stripe",root.transform,new Vector3(-88.5f+i,.022f,-7),new Vector3(.55f,.015f,3),paint,false);
        Box("Stop line",root.transform,new Vector3(-83.25f,.023f,-10),new Vector3(3.4f,.015f,.3f),paint,false);
        var mast = (GameObject)PrefabUtility.InstantiatePrefab(signal); mast.transform.SetParent(root.transform); mast.transform.position = new Vector3(-79.6f,0,-3);
        foreach (float z in new[] {-80f,-65f,-45f})
        {
            var parked=(GameObject)PrefabUtility.InstantiatePrefab(scooter);parked.transform.SetParent(root.transform);parked.transform.SetPositionAndRotation(new Vector3(-78.5f,0,z),Quaternion.Euler(0,-20,0));
        }
        var lead=(GameObject)PrefabUtility.InstantiatePrefab(car);lead.transform.SetParent(root.transform);lead.transform.position=new Vector3(-83.25f,0,-58);
        lead.GetComponent<PreviewRoadVehicle>().signal=mast.GetComponent<PreviewTrafficSignal>();
        var session=UnityEngine.Object.FindFirstObjectByType<RideTestSession>();
        lead.GetComponent<PreviewRoadVehicle>().session=session;
        mast.GetComponent<PreviewTrafficSignal>().session=session;
        var parkedCar=(GameObject)PrefabUtility.InstantiatePrefab(car);parkedCar.transform.SetParent(root.transform);parkedCar.transform.SetPositionAndRotation(new Vector3(-88.5f,0,-58),Quaternion.Euler(0,180,0));
        parkedCar.GetComponent<PreviewRoadVehicle>().enabled=false;
        var compareCar=(GameObject)PrefabUtility.InstantiatePrefab(comparison);compareCar.transform.SetParent(root.transform);compareCar.transform.SetPositionAndRotation(new Vector3(-88.5f,0,-38),Quaternion.Euler(0,180,0));compareCar.GetComponent<PreviewRoadVehicle>().enabled=false;
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),ScenePath);
    }
    static GameObject Box(string name,Transform parent,Vector3 position,Vector3 size,Material material,bool collider)
    {
        var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=name;obj.transform.SetParent(parent,false);obj.transform.position=position;obj.transform.localScale=size;
        obj.GetComponent<Renderer>().sharedMaterial=material;
        if (!collider) UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());
        return obj;
    }
    public static void Lighting()
    {
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.5f,.55f,.6f);
        var light=new GameObject("Preview sun").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;light.transform.rotation=Quaternion.Euler(40,-35,0);light.shadows=LightShadows.Soft;
    }
    static void Ground() => Box("Ground",null,new Vector3(0,-.06f,0),new Vector3(40,.1f,40),Mat("Preview Ground",new Color(.21f,.23f,.24f)),false);
    public static void Capture(string name,Vector3 position,Vector3 target,float fov,float farClip=600)
    {
        var obj=new GameObject("Offline camera");var cam=obj.AddComponent<Camera>();cam.enabled=false;
        obj.transform.position=position;obj.transform.LookAt(target);cam.fieldOfView=fov;cam.nearClipPlane=.04f;cam.farClipPlane=farClip;
        cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.56f,.65f,.72f);
        var data=cam.GetUniversalAdditionalCameraData();data.allowXRRendering=false;data.renderPostProcessing=false;
        var rt=new RenderTexture(1400,1000,24);var previous=RenderTexture.active;
        try
        {
            rt.Create();RenderPipeline.SubmitRenderRequest(cam,new UniversalRenderPipeline.SingleCameraRequest { destination=rt });
            RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();
            File.WriteAllBytes("Personal/VehiclePreview/"+name,image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
        }
        finally { RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(obj); }
    }
}
