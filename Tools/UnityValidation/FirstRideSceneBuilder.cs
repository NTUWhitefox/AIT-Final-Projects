using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIT.Riding;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace AIT.Riding.Editor
{
    public static class FirstRideSceneBuilder
    {
        const string Root = "Assets/MotorSimulatorAssets";
        const string Generated = Root + "/FirstRideGenerated";
        const string ModelPath = Root + "/Models/UrbanScooter";
        public const string ScenePath = "Assets/Scenes/FirstRideTest.unity";
        public const string SettingsPath = Generated + "/FirstRideSettings.asset";
        static Font font;
        static int meshCounter;
        static Material road, white, yellow, grass, dark, red, blue;

        [Serializable] sealed class MaterialList { public MaterialInfo[] materials; }
        [Serializable] sealed class MaterialInfo
        { public string name, label, texture; public float metallic, smoothness; public float[] color; }

        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            Directory.CreateDirectory(Generated);
            AssetDatabase.Refresh();
            var previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                meshCounter = 0;
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                road = Material("Asphalt", new Color(0.14f, 0.16f, 0.17f));
                white = Material("RoadWhite", new Color(0.87f, 0.88f, 0.82f));
                yellow = Material("RoadYellow", new Color(0.95f, 0.7f, 0.12f));
                grass = Material("Ground", new Color(0.33f, 0.43f, 0.27f));
                dark = Material("Rubber", new Color(0.035f, 0.045f, 0.05f));
                red = Material("MarkerRed", new Color(0.75f, 0.12f, 0.06f));
                blue = Material("SignBlue", new Color(0.04f, 0.19f, 0.29f));
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.6f, 0.7f, 0.8f);
                RenderSettings.ambientEquatorColor = new Color(0.5f, 0.55f, 0.55f);
                RenderSettings.ambientGroundColor = new Color(0.28f, 0.29f, 0.25f);
                RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogStartDistance = 180; RenderSettings.fogEndDistance = 450;
                RenderSettings.fogColor = new Color(0.64f, 0.76f, 0.82f);
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.25f;
                sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(45, -30, 0);
                RenderSettings.sun = sun;

                var environment = new GameObject("First ride test environment").transform;
                Box("Ground", environment, new Vector3(0, -0.12f, 20), new Vector3(420, 0.2f, 560), grass, false);
                var course = environment.gameObject.AddComponent<RideTestCourse>();
                course.stages = BuildCourse(environment);
                BuildLandmarks(environment);
                BuildRider(course);

                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
                Debug.Log("First Ride scene generated: " + ScenePath + ". Existing open scenes were preserved.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        static RideTestStage[] BuildCourse(Transform parent)
        {
            var straight = new List<Vector3>();
            for (int z = -100; z <= 180; z += 4) straight.Add(new Vector3(-85, 0, z));
            var oval = new List<Vector3>();
            for (int z = -80; z < 80; z += 4) oval.Add(new Vector3(32, 0, z));
            for (int i = 0; i < 48; ++i)
            { float a = i * Mathf.PI / 48; oval.Add(new Vector3(32 * Mathf.Cos(a), 0, 80 + 32 * Mathf.Sin(a))); }
            for (int z = 80; z > -80; z -= 4) oval.Add(new Vector3(-32, 0, z));
            for (int i = 0; i <= 48; ++i)
            { float a = Mathf.PI + i * Mathf.PI / 48; oval.Add(new Vector3(32 * Mathf.Cos(a), 0, -80 + 32 * Mathf.Sin(a))); }
            var slalom = new List<Vector3>();
            for (int z = -100; z <= 180; z += 2)
            {
                float ramp = Mathf.SmoothStep(0, 1, (z + 80) / 40f) * (1 - Mathf.SmoothStep(0, 1, (z - 130) / 40f));
                slalom.Add(new Vector3(90 + 7 * Mathf.Sin((z + 80) * 2 * Mathf.PI / 140) * ramp, 0, z));
            }
            Road("Straight", parent, straight.ToArray(), 8);
            Road("Wide bend loop", parent, oval.ToArray(), 8);
            Road("S bends", parent, slalom.ToArray(), 8);
            Box("Straight stopping line", parent, new Vector3(-85, 0.035f, 178), new Vector3(8, 0.015f, 0.4f), white, false);
            Box("S-bend stopping line", parent, new Vector3(90, 0.035f, 178), new Vector3(8, 0.015f, 0.4f), white, false);
            return new[]
            {
                new RideTestStage { name="01 Straight / accelerate and brake",
                    spawnPosition=new Vector3(-83.25f,0,-90), centreline=straight.ToArray(), stopAtEnd=true },
                new RideTestStage { name="02 Wide bends / look and turn",
                    spawnPosition=new Vector3(33.75f,0,-65), centreline=oval.ToArray() },
                new RideTestStage { name="03 S bends / control transitions",
                    spawnPosition=new Vector3(91.75f,0,-90), centreline=slalom.ToArray(), stopAtEnd=true }
            };
        }

        static void Road(string name, Transform parent, Vector3[] points, float width)
        {
            var root = new GameObject(name).transform; root.SetParent(parent, false);
            Ribbon(root, points, -width/2, width/2, 0.01f, road, name);
            Ribbon(root, points, -width/2+0.12f, -width/2+0.24f, 0.02f, white, name+" edge L");
            Ribbon(root, points, width/2-0.24f, width/2-0.12f, 0.02f, white, name+" edge R");
            // Each dash is ~4 m; posts every ~20 m supply repeatable optic-flow cues.
            float distance = 0, nextPost = 0;
            for (int i = 1; i < points.Length; ++i)
            {
                Vector3 delta = points[i] - points[i-1];
                float length = delta.magnitude;
                if (((int)(distance / 8)) % 2 == 0)
                    Ribbon(root, new[] { points[i-1], points[i] }, -0.06f, 0.06f, 0.025f, yellow, name+" dash");
                if (distance >= nextPost)
                {
                    nextPost += 20;
                    Vector3 right = Vector3.Cross(Vector3.up, delta.normalized);
                    foreach (float side in new[] { -1f, 1f })
                    {
                        Vector3 p = points[i] + right * side * (width/2 + 1.2f);
                        Box("Roadside post", root, p + Vector3.up*0.55f, new Vector3(0.12f,1.1f,0.12f), white, true);
                        Box("Red reflector", root, p + Vector3.up*0.82f, new Vector3(0.14f,0.15f,0.14f), red, false);
                    }
                }
                distance += length;
            }
        }

        static void Ribbon(Transform parent, Vector3[] points, float left, float right, float y, Material mat, string label)
        {
            var vertices = new Vector3[points.Length*2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[(points.Length-1)*6];
            for (int i = 0; i < points.Length; ++i)
            {
                Vector3 tangent = points[Mathf.Min(i+1,points.Length-1)] - points[Mathf.Max(i-1,0)];
                Vector3 side = Vector3.Cross(Vector3.up, tangent.normalized);
                vertices[i*2] = points[i] + side*left + Vector3.up*y;
                vertices[i*2+1] = points[i] + side*right + Vector3.up*y;
                uv[i*2] = new Vector2(0,i); uv[i*2+1] = new Vector2(1,i);
                if (i == points.Length-1) continue;
                int k=i*6, a=i*2;
                triangles[k]=a;triangles[k+1]=a+2;triangles[k+2]=a+1;
                triangles[k+3]=a+1;triangles[k+4]=a+2;triangles[k+5]=a+3;
            }
            var mesh = new Mesh { name=label, vertices=vertices, uv=uv, triangles=triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            mesh=Save(mesh, Generated+$"/RoadMesh{meshCounter++:000}.asset");
            var obj=new GameObject(label); obj.transform.SetParent(parent,false);
            obj.AddComponent<MeshFilter>().sharedMesh=mesh;
            obj.AddComponent<MeshRenderer>().sharedMaterial=mat;
        }

        static void BuildLandmarks(Transform parent)
        {
            var buildingA=Material("BuildingA",new Color(0.65f,0.58f,0.45f));
            var buildingB=Material("BuildingB",new Color(0.46f,0.55f,0.6f));
            for(int i=0;i<12;++i)
            {
                float z=-110+i*26;
                Box("Distant building",parent,new Vector3(-120,6+i%4*2,z),
                    new Vector3(12,12+i%4*4,16),i%2==0?buildingA:buildingB,true);
                Box("Distant building",parent,new Vector3(130,5+i%3*2,z),
                    new Vector3(12,10+i%3*4,16),i%2==0?buildingB:buildingA,true);
            }
            Sign(parent,new Vector3(-91,0,150),"BRAKE\nEND OF SECTION",0);
            Sign(parent,new Vector3(84,0,150),"BRAKE\nEND OF SECTION",0);
        }

        static void BuildRider(RideTestCourse course)
        {
            var settings=AssetDatabase.LoadAssetAtPath<RideTestSettings>(SettingsPath);
            if(!settings) { settings=ScriptableObject.CreateInstance<RideTestSettings>(); AssetDatabase.CreateAsset(settings,SettingsPath); }
            var host=new GameObject("First Ride Session");
            var session=host.AddComponent<RideTestSession>();
            session.settingsTemplate=settings;session.course=course;
            session.input=host.AddComponent<RideInput>();
            session.telemetry=host.AddComponent<RideTelemetry>();session.telemetry.session=session;
            var vehicle=new GameObject("Vehicle Ground Pose").transform;
            vehicle.SetParent(host.transform,false);vehicle.position=course.stages[0].spawnPosition;
            session.vehicleRoot=vehicle;
            session.vehicleVisual=new GameObject("Vehicle visual lean (independent of head)").transform;
            session.vehicleVisual.SetParent(vehicle,false);
            var rider=new GameObject("Rider visual lean (eye pivot)").transform;rider.SetParent(vehicle,false);
            session.riderLean=rider;
            var tracking=new GameObject("Seated Tracking Origin (unit scale)").transform;tracking.SetParent(rider,false);
            session.trackingOrigin=tracking;
            var cameraObj=new GameObject("Main Camera");cameraObj.tag="MainCamera";
            cameraObj.transform.SetParent(tracking,false);cameraObj.transform.localPosition=settings.seatedEyePosition;
            var camera=cameraObj.AddComponent<Camera>();camera.nearClipPlane=0.05f;camera.farClipPlane=500;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0.64f,0.76f,0.82f);
            camera.fieldOfView=85;cameraObj.AddComponent<AudioListener>();
            var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;
            var driver=cameraObj.AddComponent<TrackedPoseDriver>();
            driver.positionInput=new InputActionProperty(new InputAction("Head Position",InputActionType.Value,"<XRHMD>/centerEyePosition",expectedControlType:"Vector3"));
            driver.rotationInput=new InputActionProperty(new InputAction("Head Rotation",InputActionType.Value,"<XRHMD>/centerEyeRotation",expectedControlType:"Quaternion"));
            driver.trackingStateInput=new InputActionProperty(new InputAction("Head Tracking",InputActionType.Value,"<XRHMD>/trackingState",expectedControlType:"Integer"));
            session.headCamera=camera;
            var presentation=host.AddComponent<RidePresentation>();presentation.session=session;
            AddScooter(session,presentation);
            var panel=new GameObject("Paused rider instructions");panel.layer=2;
            panel.transform.SetParent(cameraObj.transform,false);panel.transform.localPosition=new Vector3(0,0,1.05f);
            Box("Pause panel",panel.transform,new Vector3(0,0,0.01f),new Vector3(1.12f,0.85f,0.008f),dark,false,2);
            presentation.pausedDisplay=Text("Instructions",panel.transform,Vector3.zero,"Calibrate then start",0.01f,25);
            presentation.pausedPanel=panel;
            var vignette=GameObject.CreatePrimitive(PrimitiveType.Quad);vignette.name="Optional comfort vignette";vignette.layer=2;
            UnityEngine.Object.DestroyImmediate(vignette.GetComponent<Collider>());
            vignette.transform.SetParent(cameraObj.transform,false);vignette.transform.localPosition=new Vector3(0,0,0.11f);
            vignette.transform.localScale=Vector3.one*0.8f;
            presentation.vignetteRenderer=vignette.GetComponent<Renderer>();
            presentation.vignetteShader=Shader.Find("AIT/RideVignette");vignette.SetActive(true);
            vignette.GetComponent<Renderer>().enabled=false;
            var audio=host.AddComponent<RideEngineAudio>();audio.session=session;
        }

        static void AddScooter(RideTestSession session,RidePresentation presentation)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath+"/UrbanScooter.obj");
            if(!source)throw new FileNotFoundException("Convert the documented CC0 scooter before building.");
            var scooter=UnityEngine.Object.Instantiate(source,session.vehicleVisual);scooter.name="Innerscene CC0 Urban Scooter";
            var materials=JsonUtility.FromJson<MaterialList>(File.ReadAllText(ModelPath+"/materials.json"));
            var lookup=new Dictionary<string,Material>();
            foreach(var info in materials.materials)
            {
                var color=info.color;var mat=Material(info.name,new Color(color[0],color[1],color[2],color[3]));
                mat.SetFloat("_Metallic",info.metallic);mat.SetFloat("_Smoothness",info.smoothness);
                mat.SetFloat("_Cull",0);
                mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(ModelPath+"/"+info.texture));
                EditorUtility.SetDirty(mat);lookup[info.name]=mat;
            }
            foreach(var renderer in scooter.GetComponentsInChildren<MeshRenderer>())
            {
                renderer.gameObject.layer=2;
                var slots=renderer.sharedMaterials;
                for(int i=0;i<slots.Length;++i)
                {
                    string name=slots[i]?slots[i].name:"";
                    var match=lookup.Keys.FirstOrDefault(k=>name.Contains(k));
                    if(match!=null)slots[i]=lookup[match];
                }
                renderer.sharedMaterials=slots;
                if(renderer.name.Contains("side_stand")||renderer.name.Contains("stand_ground_shoe"))renderer.enabled=false;
                if(renderer.name.Contains("instrument_parked_needle")||renderer.name.Contains("instrument_raised_scale_tick"))renderer.enabled=false;
            }
            // Unity's OBJ importer is verified by the generated previews before delivery.
            presentation.handlebars=Pivot("Steering",session.vehicleVisual,new Vector3(0,0.94f,0.38f));
            presentation.frontWheel=Pivot("Front wheel",presentation.handlebars,new Vector3(0,0.245f,0.6770266f));
            presentation.rearWheel=Pivot("Rear wheel",session.vehicleVisual,new Vector3(0,0.245f,-0.6529734f));
            foreach(var renderer in scooter.GetComponentsInChildren<MeshRenderer>())
            {
                switch(RideVehicleParts.Classify(renderer.name))
                {
                    case RidePartGroup.FrontWheel: renderer.transform.SetParent(presentation.frontWheel,true); break;
                    case RidePartGroup.RearWheel: renderer.transform.SetParent(presentation.rearWheel,true); break;
                    case RidePartGroup.Steering: renderer.transform.SetParent(presentation.handlebars,true); break;
                }
            }
            presentation.speedDisplay=Text("Digital speed",presentation.handlebars,new Vector3(0,0.98f,0.34f),"00\n<size=14>km/h</size>",0.007f,48);
            presentation.speedDisplay.transform.localRotation=Quaternion.Euler(90,0,0);
            // Text() uses local coordinates; steering pivot is already at handlebar position.
            presentation.speedDisplay.transform.position=session.vehicleVisual.TransformPoint(new Vector3(0,0.98f,0.34f));
            AddMirror(session,presentation.handlebars,-1);
            AddMirror(session,presentation.handlebars,1);
        }

        static Transform Pivot(string name,Transform parent,Vector3 vehiclePosition)
        {
            var pivot=new GameObject(name).transform;
            pivot.SetParent(parent,false);
            Transform vehicle=parent;
            while(vehicle.parent && vehicle.name!="Vehicle visual lean (independent of head)")vehicle=vehicle.parent;
            pivot.position=vehicle.TransformPoint(vehiclePosition);
            return pivot;
        }

        static void AddMirror(RideTestSession session,Transform steering,int side)
        {
            var screen=GameObject.CreatePrimitive(PrimitiveType.Quad);screen.name=side<0?"Left rear-view screen":"Right rear-view screen";
            screen.layer=2;UnityEngine.Object.DestroyImmediate(screen.GetComponent<Collider>());
            screen.transform.SetParent(steering,false);
            screen.transform.position=session.vehicleVisual.TransformPoint(new Vector3(side*0.345f,1.124f,0.325f));
            screen.transform.localScale=new Vector3(0.095f,0.057f,1);
            var cam=new GameObject(screen.name+" camera").AddComponent<Camera>();
            cam.transform.SetParent(steering,false);cam.transform.position=screen.transform.position;
            cam.transform.rotation=Quaternion.Euler(0,180+side*22,0);
            cam.enabled=false;cam.cullingMask=~(1<<2);cam.nearClipPlane=0.1f;cam.farClipPlane=220;
            cam.fieldOfView=65;cam.backgroundColor=new Color(0.64f,0.76f,0.82f);
            cam.clearFlags=CameraClearFlags.SolidColor;
            var data=cam.GetUniversalAdditionalCameraData();data.renderShadows=false;data.renderPostProcessing=false;
            var mirror=screen.AddComponent<RideMirror>();mirror.session=session;mirror.mirrorCamera=cam;
            mirror.screen=screen.GetComponent<Renderer>();mirror.screenShader=Shader.Find("Universal Render Pipeline/Unlit");
        }

        static void Sign(Transform parent,Vector3 p,string label,float yaw)
        {
            var root=new GameObject("Sign "+label.Replace('\n',' ')).transform;root.SetParent(parent,false);
            root.position=p;root.rotation=Quaternion.Euler(0,yaw,0);
            Box("Pole",root,new Vector3(0,1,0),new Vector3(0.1f,2,0.1f),dark,true);
            Box("Board",root,new Vector3(0,1.9f,0),new Vector3(3,1.3f,0.08f),blue,true);
            Text("Sign text",root,new Vector3(0,1.9f,-0.05f),label,0.045f,48);
        }
        static TextMesh Text(string name,Transform parent,Vector3 p,string text,float size,int fontSize)
        {
            var obj=new GameObject(name);obj.layer=2;obj.transform.SetParent(parent,false);obj.transform.localPosition=p;
            var mesh=obj.AddComponent<TextMesh>();mesh.text=text;mesh.font=font;mesh.fontSize=fontSize;mesh.characterSize=size;
            mesh.anchor=TextAnchor.MiddleCenter;mesh.alignment=TextAlignment.Center;mesh.color=Color.white;
            obj.GetComponent<MeshRenderer>().sharedMaterial=font.material;return mesh;
        }
        static GameObject Box(string name,Transform parent,Vector3 p,Vector3 scale,Material material,bool collider,int layer=0)
        {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=name;obj.layer=layer;
            obj.transform.SetParent(parent,false);obj.transform.localPosition=p;obj.transform.localScale=scale;
            obj.GetComponent<Renderer>().sharedMaterial=material;
            if(!collider)UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());
            return obj;
        }
        static Material Material(string name,Color color)
        {
            string path=Generated+"/"+name+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
            mat.SetColor("_BaseColor",color);mat.enableInstancing=true;EditorUtility.SetDirty(mat);return mat;
        }
        static T Save<T>(T value,string path) where T:UnityEngine.Object
        {
            var existing=AssetDatabase.LoadAssetAtPath<T>(path);
            if(existing){EditorUtility.CopySerialized(value,existing);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(existing);return existing;}
            AssetDatabase.CreateAsset(value,path);return value;
        }
        public static void Open()
        {
            if(!File.Exists(ScenePath))Build();
            if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
