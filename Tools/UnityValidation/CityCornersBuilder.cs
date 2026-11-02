// Offline authoring only. Imported into the ignored validation clone, never main Assets.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using AIT.Riding;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CityCornersBuilder
{
    public const string ScenePath="Assets/Scenes/CityCorners.unity";
    const string Root="Assets/MotorSimulatorAssets/CityCornersGenerated";
    const string Existing="Assets/MotorSimulatorAssets/CityPreviewGenerated/";
    static Material asphalt,white,yellow,curb,glass;
    static readonly List<string> report=new List<string>();
    static readonly Dictionary<Vector2Int,CityJunction> junctions=new Dictionary<Vector2Int,CityJunction>();
    public static void Run()
    {
        Directory.CreateDirectory(Root);Directory.CreateDirectory("Personal/VehiclePreview");AssetDatabase.Refresh();
        BuildHandMeshes();
        foreach(var path in new[]{"Assets/Scenes/FirstRideTest.unity","Assets/Scenes/UrbanVehiclePreview.unity"})
        {
            EditorSceneManager.OpenScene(path);
            foreach(var text in UnityEngine.Object.FindObjectsByType<TextMesh>(FindObjectsSortMode.None))
                if(text.name=="Sign text" && text.text.Contains("km/h")) UnityEngine.Object.DestroyImmediate(text.transform.parent.gameObject);
            AddHands(UnityEngine.Object.FindFirstObjectByType<RidePresentation>());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        }
        EditorSceneManager.OpenScene("Assets/Scenes/FirstRideTest.unity");
        var session=UnityEngine.Object.FindFirstObjectByType<RideTestSession>();
        foreach(var obj in SceneManager.GetActiveScene().GetRootGameObjects())
            if(obj!=session.gameObject)UnityEngine.Object.DestroyImmediate(obj);
        UrbanVehiclePreviewBuilder.Lighting();RenderSettings.fog=false;
        var map=new GameObject("City streets - right angle junctions");
        var course=map.AddComponent<RideTestCourse>();session.course=course;
        course.stages=new[]{
            new RideTestStage{name="南側進入 / 自由騎行",freeRoam=true,spawnPosition=new Vector3(2,0,-42),centreline=Array.Empty<Vector3>()},
            new RideTestStage{name="西側進入 / 自由騎行",freeRoam=true,spawnPosition=new Vector3(-42,0,-2),spawnYaw=90,centreline=Array.Empty<Vector3>()},
            new RideTestStage{name="北側進入 / 自由騎行",freeRoam=true,spawnPosition=new Vector3(-2,0,42),spawnYaw=180,centreline=Array.Empty<Vector3>()}};
        course.streetSegmentEndpoints=new[]{
            new Vector3(-64,0,-92),new Vector3(-64,0,92),new Vector3(0,0,-92),new Vector3(0,0,92),new Vector3(64,0,-92),new Vector3(64,0,92),
            new Vector3(-92,0,-64),new Vector3(92,0,-64),new Vector3(-92,0,0),new Vector3(92,0,0),new Vector3(-92,0,64),new Vector3(92,0,64)};
        session.vehicleRoot.SetPositionAndRotation(course.stages[0].spawnPosition,Quaternion.identity);
        asphalt=Mat("Asphalt",new Color(.115f,.125f,.13f));white=Mat("White markings",new Color(.86f,.86f,.79f));
        yellow=Mat("Yellow markings",new Color(.95f,.66f,.08f));curb=Mat("Sidewalk",new Color(.43f,.42f,.39f));glass=Mat("Windows",new Color(.1f,.17f,.2f));
        Box("Ground",map.transform,new Vector3(0,-.16f,0),new Vector3(220,.25f,220),Mat("Ground",new Color(.28f,.29f,.26f)),false);
        foreach(int c in new[]{-64,0,64})
        {
            Box("North south road",map.transform,new Vector3(c,.003f,0),new Vector3(8,.02f,184),asphalt,false);
            Box("East west road",map.transform,new Vector3(0,.003f,c),new Vector3(184,.02f,8),asphalt,false);
            for(int p=-88;p<=88;p+=4)
            {
                if(new[]{-64,0,64}.Any(j=>Mathf.Abs(p-j)<14))continue;
                foreach(float offset in new[]{-.09f,.09f})
                {
                    Box("Centre line",map.transform,new Vector3(c+offset,.02f,p),new Vector3(.055f,.012f,3.8f),yellow,false);
                    Box("Centre line",map.transform,new Vector3(p,.02f,c+offset),new Vector3(3.8f,.012f,.055f),yellow,false);
                }
            }
        }
        junctions.Clear();
        foreach(int x in new[]{-64,0,64})foreach(int z in new[]{-64,0,64})
        {
            var point=new Vector3(x,0,z);
            Box("Junction asphalt",map.transform,point+Vector3.up*.006f,new Vector3(16,.018f,16),asphalt,false);
            var obj=new GameObject($"Junction {x},{z}");obj.transform.SetParent(map.transform);obj.transform.position=point;
            var j=obj.AddComponent<CityJunction>();j.session=session;j.signalControlled=x==0&&z==0;junctions[new Vector2Int(x,z)]=j;
            Crossings(obj.transform,point);
        }
        foreach(int x in new[]{-64,0})foreach(int z in new[]{-64,0})Block(map.transform,x,z);
        // Buildings beyond the grid keep the road edges readable from inside the headset.
        foreach(int side in new[]{-1,1})for(int k=-3;k<=3;k++)
        {
            Building(map.transform,new Vector3(side*77,0,k*22),new Vector3(15,9+(k+3)%3*3,18),k+3);
            Building(map.transform,new Vector3(k*22,0,side*77),new Vector3(18,9+(k+3)%3*3,15),k+4);
        }
        BakeStaticGeometry(map.transform);
        var central=junctions[Vector2Int.zero];
        central.northSouthSignal=Signal(map.transform,new Vector3(6,0,5),0,session);
        central.eastWestSignal=Signal(map.transform,new Vector3(-5,0,6),-90,session);
        central.eastWestSignal.colour=PreviewSignalColour.Red;central.eastWestSignal.Apply();
        var right=Car(map.transform,session,"Right turning traffic",1,new[]{new Vector2Int(0,0),new Vector2Int(64,0),new Vector2Int(64,-64),new Vector2Int(0,-64)},33.75f);
        var left=Car(map.transform,session,"Left turning traffic",-1,new[]{new Vector2Int(0,0),new Vector2Int(-64,0),new Vector2Int(-64,-64),new Vector2Int(0,-64)},0);
        right.Initialise();left.Initialise();ValidatePath(right);ValidatePath(left);
        foreach(var p in new[]{new Vector3(-5.9f,0,-33),new Vector3(7,0,27),new Vector3(-30,0,6.2f)})
        {var scooter=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Existing+"TDAL_Scooter_D.prefab"));scooter.transform.SetParent(map.transform);scooter.transform.SetPositionAndRotation(p,Quaternion.Euler(0,-15,0));}
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),ScenePath);AssetDatabase.SaveAssets();
        UrbanVehiclePreviewBuilder.Capture("city-grid.png",new Vector3(103,117,-126),new Vector3(0,0,0),62);
        session.GetComponent<RidePresentation>().pausedPanel.SetActive(false);
        UrbanVehiclePreviewBuilder.Capture("city-rider-hands.png",session.vehicleRoot.TransformPoint(new Vector3(0,1.35f,-.28f)),session.vehicleRoot.TransformPoint(new Vector3(0,.7f,.8f)),78);
        PoseForRender(right,right.turns[0].start+(right.turns[0].end-right.turns[0].start)*.5f);
        UrbanVehiclePreviewBuilder.Capture("city-right-turn.png",new Vector3(0,10,-17),new Vector3(3,1,-3),55);
        PoseForRender(left,left.turns[0].start+(left.turns[0].end-left.turns[0].start)*.5f);
        UrbanVehiclePreviewBuilder.Capture("city-left-turn.png",new Vector3(0,10,-17),new Vector3(-2,1,-1),55);
        CaptureSpeedMenu(session);
        File.WriteAllLines("Personal/VehiclePreview/city-authoring.txt",report);
    }
    static void CaptureSpeedMenu(RideTestSession session)
    {
        RideLaunchRequest.Mode=RideDisplayMode.Desktop;
        typeof(RideTestSession).GetMethod("Awake",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(session,null);
        session.SetupUI.Buttons.Single(b=>b.name=="Tab5").onClick.Invoke();
        var canvas=session.SetupUI.MenuCanvas;canvas.GetComponent<UnityEngine.UI.CanvasScaler>().enabled=false;
        canvas.renderMode=RenderMode.WorldSpace;canvas.transform.SetParent(null,false);
        canvas.transform.SetPositionAndRotation(new Vector3(25,2,27),Quaternion.identity);canvas.transform.localScale=Vector3.one*.001f;
        ((RectTransform)canvas.transform).sizeDelta=new Vector2(940,780);Canvas.ForceUpdateCanvases();
        UrbanVehiclePreviewBuilder.Capture("city-speed-menu.png",new Vector3(25,2,26),new Vector3(25,2,27),50);
    }
    static void BakeStaticGeometry(Transform root)
    {
        var renderers=root.GetComponentsInChildren<MeshRenderer>();
        foreach(var group in renderers.GroupBy(r=>r.sharedMaterial))
        {
            var instances=group.Select(r=>new CombineInstance{mesh=r.GetComponent<MeshFilter>().sharedMesh,transform=root.worldToLocalMatrix*r.transform.localToWorldMatrix}).ToArray();
            var mesh=new Mesh{name="Static "+group.Key.name,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.CombineMeshes(instances,true,true);mesh.RecalculateBounds();
            mesh=SaveMesh(mesh,"Static_"+group.Key.name);
            var obj=new GameObject("Street geometry - "+group.Key.name);obj.transform.SetParent(root,false);obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=group.Key;
            foreach(var r in group)
            {
                var source=r.gameObject;UnityEngine.Object.DestroyImmediate(source.GetComponent<MeshFilter>());UnityEngine.Object.DestroyImmediate(r);
                if(source.GetComponents<Component>().Length==1&&source.transform.childCount==0)UnityEngine.Object.DestroyImmediate(source);
            }
        }
        report.Add("Static street/marking/facade/window geometry combined by material; collision shapes retained.");
    }
    static void PoseForRender(CityTrafficVehicle car,float progress)
    {
        car.SetPose(progress);car.Sample(progress,out _,out _,out float curve);
        foreach(var wheel in car.wheels.Where(w=>w.name.Contains("Front")))
        {
            float x=car.transform.InverseTransformPoint(wheel.position).x;
            wheel.localRotation*=Quaternion.Euler(0,Mathf.Atan(car.wheelbase*curve/(1-curve*x))*Mathf.Rad2Deg,0);
        }
    }
    static Material Mat(string name,Color colour)
    {
        string path=Root+"/"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
        mat.SetColor("_BaseColor",colour);mat.SetFloat("_Smoothness",.25f);mat.enableInstancing=true;EditorUtility.SetDirty(mat);return mat;
    }
    static GameObject Box(string name,Transform parent,Vector3 pos,Vector3 size,Material mat,bool collision)
    {
        var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.name=name;o.transform.SetParent(parent,false);o.transform.position=pos;o.transform.localScale=size;o.GetComponent<Renderer>().sharedMaterial=mat;
        if(!collision)UnityEngine.Object.DestroyImmediate(o.GetComponent<Collider>());return o;
    }
    static void Crossings(Transform parent,Vector3 centre)
    {
        for(int heading=0;heading<360;heading+=90)
        {
            var q=Quaternion.Euler(0,heading,0);
            for(int i=0;i<8;i++)
            {var stripe=Box("Crosswalk",parent,centre+q*new Vector3(-3.5f+i,.03f,-9.5f),new Vector3(.55f,.012f,2),white,false);stripe.transform.rotation=q;}
            var line=Box("Stop line",parent,centre+q*new Vector3(2,.03f,-12),new Vector3(3.5f,.012f,.3f),white,false);line.transform.rotation=q;
        }
    }
    static void Block(Transform parent,int x,int z)
    {
        float lo=4.2f,hi=59.8f,c=3.5f;
        var polygon=new[]{new Vector3(x+lo+c,.14f,z+lo),new Vector3(x+hi-c,.14f,z+lo),new Vector3(x+hi,.14f,z+lo+c),new Vector3(x+hi,.14f,z+hi-c),
            new Vector3(x+hi-c,.14f,z+hi),new Vector3(x+lo+c,.14f,z+hi),new Vector3(x+lo,.14f,z+hi-c),new Vector3(x+lo,.14f,z+lo+c)};
        var mesh=new Mesh{name="Chamfered sidewalk"};mesh.vertices=polygon;var tris=new List<int>();for(int i=1;i<polygon.Length-1;i++)tris.AddRange(new[]{0,i+1,i});mesh.triangles=tris.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
        mesh=SaveMesh(mesh,$"Sidewalk_{x}_{z}");
        var walk=new GameObject("Sidewalk with cut corner");walk.transform.SetParent(parent);walk.AddComponent<MeshFilter>().sharedMesh=mesh;walk.AddComponent<MeshRenderer>().sharedMaterial=curb;walk.AddComponent<MeshCollider>().sharedMesh=mesh;
        for(int i=0;i<3;i++)
        {
            float p=15+i*17;
            Building(parent,new Vector3(x+11,0,z+p),new Vector3(9,9+i*3,14),i);
            Building(parent,new Vector3(x+53,0,z+p),new Vector3(9,12+i*3,14),i+1);
            if(i==1)continue;
            Building(parent,new Vector3(x+p,0,z+11),new Vector3(14,10+i*3,9),i+2);
            Building(parent,new Vector3(x+p,0,z+53),new Vector3(14,9+i*3,9),i+3);
        }
    }
    static void Building(Transform parent,Vector3 p,Vector3 size,int variant)
    {
        var colour=new[]{new Color(.64f,.62f,.55f),new Color(.58f,.63f,.64f),new Color(.69f,.59f,.49f),new Color(.55f,.58f,.52f)}[Mathf.Abs(variant)%4];
        Box("Street building",parent,p+Vector3.up*size.y/2,size,Mat("Facade "+Mathf.Abs(variant)%4,colour),true);
        for(int floor=0;floor<(size.y-2)/3;floor++)for(int side=-1;side<=1;side+=2)
        {
            for(int col=0;col<3;col++)
            {
                Box("Window",parent,p+new Vector3(side*(size.x/2+.02f),2+floor*3,(col-1)*size.z*.25f),new Vector3(.03f,1.3f,1.4f),glass,false);
                Box("Window",parent,p+new Vector3((col-1)*size.x*.25f,2+floor*3,side*(size.z/2+.02f)),new Vector3(1.4f,1.3f,.03f),glass,false);
            }
        }
    }
    static PreviewTrafficSignal Signal(Transform parent,Vector3 p,float yaw,RideTestSession session)
    {
        var o=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Existing+"TDAL_TrafficLight_C.prefab"));
        o.transform.SetParent(parent);o.transform.SetPositionAndRotation(p,Quaternion.Euler(0,yaw,0));
        var signal=o.GetComponent<PreviewTrafficSignal>();signal.cycleInPlay=false;signal.session=session;signal.colour=PreviewSignalColour.Green;signal.Apply();return signal;
    }
    static CityTrafficVehicle Car(Transform parent,RideTestSession session,string name,int direction,Vector2Int[] corners,float initial)
    {
        var obj=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Existing+"Car26_URP.prefab"));obj.name=name;obj.transform.SetParent(parent);
        var old=obj.GetComponent<PreviewRoadVehicle>();var wheels=old.wheels;UnityEngine.Object.DestroyImmediate(old);
        var car=obj.AddComponent<CityTrafficVehicle>();car.session=session;car.wheels=wheels;car.initialProgress=initial;
        car.rearAxleZ=wheels.Where(w=>w.name.Contains("Rear")).Average(w=>obj.transform.InverseTransformPoint(w.position).z);
        car.wheelbase=wheels.Where(w=>w.name.Contains("Front")).Average(w=>obj.transform.InverseTransformPoint(w.position).z)-car.rearAxleZ;
        car.wheelRadius=wheels.SelectMany(w=>w.GetComponentsInChildren<MeshRenderer>()).Where(r=>r.enabled).Average(r=>r.bounds.extents.y);
        var positions=new List<Vector3>();var yaws=new List<float>();var curves=new List<float>();var turns=new List<CityTrafficTurn>();
        float length=0;Vector3 previous=new Vector3(2,0,-55.75f);
        void Add(Vector3 p,float yaw,float curvature)
        {if(positions.Count>0)length+=Vector3.Distance(positions[positions.Count-1],p);positions.Add(p);yaws.Add(yaw);curves.Add(curvature);}
        Add(previous,0,0);
        float heading=0,radius=direction>0?6.25f:10.25f;
        foreach(var corner in corners)
        {
            var q=Quaternion.Euler(0,heading,0);var forward=q*Vector3.forward;var right=q*Vector3.right;
            var start=new Vector3(corner.x,0,corner.y)+right*2-forward*8.25f;
            int steps=Mathf.CeilToInt(Vector3.Distance(previous,start)/.25f);
            for(int i=1;i<=steps;i++)Add(Vector3.Lerp(previous,start,(float)i/steps),heading,0);
            float begin=length;
            var centre=start+right*(direction*radius);var radial=start-centre;
            int arcs=Mathf.CeilToInt(radius*Mathf.PI*.5f/.2f);
            for(int i=1;i<=arcs;i++)Add(centre+Quaternion.Euler(0,direction*90f*i/arcs,0)*radial,heading+direction*90f*i/arcs,direction/radius);
            turns.Add(new CityTrafficTurn{junction=junctions[corner],start=begin,end=length,stop=begin-(3.75f+(2.56f-car.rearAxleZ)+.5f),approachYaw=heading,direction=direction});
            heading+=direction*90;previous=positions[positions.Count-1];
        }
        car.pathPoints=positions.ToArray();car.pathYaw=yaws.ToArray();car.pathCurvature=curves.ToArray();car.turns=turns.ToArray();
        report.Add(name+$": rear axle Z={car.rearAxleZ:F3}, wheelbase={car.wheelbase:F3}, radius={radius:F2}, {positions.Count} path samples; closed gap="+Vector3.Distance(positions[0],positions[positions.Count-1]));
        return car;
    }
    static void ValidatePath(CityTrafficVehicle car)
    {
        float largest=0;
        for(float s=0;s<car.Length;s+=.15f)
        {
            car.Sample(s,out var rear,out float yaw,out float curve);var q=Quaternion.Euler(0,yaw,0);var centre=rear-q*Vector3.forward*car.rearAxleZ;
            largest=Mathf.Max(largest,Mathf.Abs(Mathf.Atan(car.wheelbase*curve)*Mathf.Rad2Deg));
            foreach(float x in new[]{-1.06f,1.06f})foreach(float z in new[]{-2.56f,2.56f})
            {var p=centre+q*new Vector3(x,0,z);if(!OnRoad(p))throw new Exception(car.name+" footprint crosses sidewalk at "+p+" progress="+s);}
        }
        if(largest>35)throw new Exception("Traffic steering exceeds preview envelope");
        report.Add(car.name+$": full body footprint remains on asphalt; maximum centre steering {largest:F2} degrees");
    }
    public static bool OnRoad(Vector3 p)
    {
        foreach(int c in new[]{-64,0,64})if(Mathf.Abs(p.x-c)<4.22f||Mathf.Abs(p.z-c)<4.22f)return true;
        foreach(int x in new[]{-64,0,64})foreach(int z in new[]{-64,0,64})
        {float dx=Mathf.Abs(p.x-x),dz=Mathf.Abs(p.z-z);if(dx<8 && dz<8 && dx+dz<11.95f)return true;}
        return false;
    }
    static Mesh SaveMesh(Mesh mesh,string name)
    {
        string path=Root+"/"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(existing);return existing;}
        AssetDatabase.CreateAsset(mesh,path);return mesh;
    }
    static void BuildHandMeshes()
    {
        foreach(string side in new[]{"Left","Right"})
        {
            var obj=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Samples/XR Hands/1.8.1/HandVisualizer/Models/"+side+"Hand.fbx"));
            foreach(var t in obj.GetComponentsInChildren<Transform>())
            {
                if(t.name.Contains("Thumb"))continue;
                var e=t.localEulerAngles;
                if(t.name.EndsWith("Proximal"))e.x=55;
                if(t.name.EndsWith("Intermediate"))e.x=75;
                if(t.name.EndsWith("Distal"))e.x=45;
                t.localEulerAngles=e;
            }
            foreach(var t in obj.GetComponentsInChildren<Transform>().Where(t=>t.name.Contains("Thumb")&&t.name.EndsWith("Distal")))
                t.localRotation*=Quaternion.Euler(25,0,0);
            var renderer=obj.GetComponentInChildren<SkinnedMeshRenderer>();var mesh=new Mesh{name=side+" fixed grip hand"};renderer.BakeMesh(mesh);
            SaveMesh(mesh,side+"GripHand");UnityEngine.Object.DestroyImmediate(obj);
        }
    }
    static void AddHands(RidePresentation presentation)
    {
        if(presentation.handlebars.Find("Static rider hands"))return;
        var pair=new GameObject("Static rider hands");pair.transform.SetParent(presentation.handlebars,false);
        var skin=Mat("Hand skin",new Color(.59f,.35f,.23f));
        foreach(int side in new[]{-1,1})
        {
            string hand=side<0?"Left":"Right";
            var grip=presentation.handlebars.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="Scooter_grip_rib_"+side+"_7");
            var target=presentation.handlebars.InverseTransformPoint(grip.bounds.center);
            var o=new GameObject(hand+" hand - fixed grip");o.layer=2;o.transform.SetParent(pair.transform,false);
            var q=Quaternion.Euler(0,-side*10,0);
            o.transform.localRotation=q;o.transform.localPosition=target-q*new Vector3(0,-.025f,.015f);
            o.AddComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/"+hand+"GripHand.asset");o.AddComponent<MeshRenderer>().sharedMaterial=skin;
            report.Add(hand+" hand grip target "+target.ToString("F4"));
        }
    }
}
