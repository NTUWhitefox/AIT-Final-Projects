// Offline authoring and acceptance. Never copied to the working project's Assets.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using AIT.Riding;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RidingManeuverUpdate
{
    const string Car="Assets/MotorSimulatorAssets/CityPreviewGenerated/Car26_URP.prefab";
    static readonly List<string> report=new List<string>();
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);report.Add("PASS "+message);}
    public static void Run()
    {
        Directory.CreateDirectory("Personal/VehiclePreview");
        var source=PrefabUtility.LoadPrefabContents(Car);
        try
        {
            var groups=source.GetComponentsInChildren<LODGroup>(true);
            foreach(var group in groups) report.Add(group.name+" source thresholds: "+string.Join(", ",group.GetLODs().Select(l=>l.screenRelativeTransitionHeight.ToString("0.####"))));
            UrbanVehiclePreviewBuilder.UseFinestGeometry(source);
            foreach(var group in groups)
            {
                Check(!group.enabled,group.name+" automatic LOD disabled for preview");
                foreach(var r in group.GetLODs()[0].renderers.Where(r=>r))Check(r.enabled,r.name+" finest geometry enabled");
            }
            PrefabUtility.SaveAsPrefabAsset(source,Car);
        }
        finally{PrefabUtility.UnloadPrefabContents(source);}
        var s=AssetDatabase.LoadAssetAtPath<RideTestSettings>("Assets/MotorSimulatorAssets/FirstRideGenerated/FirstRideSettings.asset");
        RideSettingsProfiles.Validate(s);
        foreach(float kph in new[]{5f,8f,10f}) UTurn(s,kph);
        var m=new PlanarScooterModel();m.Reset(Vector3.zero,0);
        for(int i=0;i<450;i++)m.Step(new RideInputFrame{reverse=1,steering=1},s,20,1f/90);
        Check(m.speed<0 && m.speed>=-s.maximumReverseSpeedKph/3.6f-.001f,"reverse speed is signed and capped");
        Check(m.yawRate<0,"reverse steering follows the backward bicycle path");
        for(int i=0;i<90;i++)m.Step(new RideInputFrame(),s,20,1f/90);
        Check(m.speed==0,"releasing reverse stops backward travel");
        m.speed=2;
        for(int i=0;i<450;i++)m.Step(new RideInputFrame{brake=1},s,20,1f/90);
        Check(m.speed==0,"holding brake never reverses");
        m.speed=-.7f;
        m.Step(new RideInputFrame{brake=1,reverse=1,throttle=1},s,20,1f/90);
        Check(m.speed>-.7f && m.speed<=0,"brake overrides reverse and throttle");
        Check(!RideInput.Validate(new RideInputFrame{reverse=float.NaN,connected=true}).connected,"invalid reverse samples rejected");
        var old=RideSettingsProfiles.FromJson("{\"profileVersion\":1,\"lowSpeedSteeringDegrees\":28}");
        Check(old.lowSpeedSteeringDegrees==28 && old.maximumReverseSpeedKph==3,"old profiles retain custom lock and get reverse defaults");
        UnityEngine.Object.DestroyImmediate(old);
        // High-speed stability is still limited by the existing lateral envelope.
        m.Reset(Vector3.zero,0);m.speed=40/3.6f;
        for(int i=0;i<900;i++)
        {
            m.Step(new RideInputFrame{steering=1,throttle=1},s,40,1f/90);
            if(Mathf.Abs(m.lateralAcceleration)>s.maximumLateralAcceleration+.001f)throw new Exception("High-speed lateral limit exceeded");
        }
        report.Add("PASS high-speed lateral envelope retained");
        File.WriteAllLines("Personal/VehiclePreview/maneuver-check.txt",report);
        Render();AssetDatabase.SaveAssets();
    }
    static void UTurn(RideTestSettings s,float kph)
    {
        var m=new PlanarScooterModel();m.Reset(new Vector3(1.75f,0,0),0);m.speed=kph/3.6f;
        float heading=0,extent=0;
        for(int i=0;i<9000 && heading<180;i++)
        {
            float yaw=m.yaw;
            float maintain=(s.coastingDeceleration+s.quadraticDrag*m.speed*m.speed)/s.acceleration;
            m.Step(new RideInputFrame{steering=-1,throttle=maintain},s,20,1f/90);
            heading+=Mathf.Abs(Mathf.DeltaAngle(yaw,m.yaw));
            float a=m.yaw*Mathf.Deg2Rad;
            float halfWidth=.385f*Mathf.Abs(Mathf.Cos(a))+.952f*Mathf.Abs(Mathf.Sin(a));
            extent=Mathf.Max(extent,Mathf.Abs(m.position.x)+halfWidth);
        }
        report.Add($"U-turn {kph} km/h: end x={m.position.x:F3}, swept |x|={extent:F3} m, heading={heading:F2}");
        if(kph<=8)Check(heading>=180 && extent<4,$"{kph} km/h U-turn, including scooter footprint, fits an 8 m road");
        else report.Add("INFO 10 km/h is a wider maneuver; slow to 5-8 km/h for the lane-centre U-turn.");
    }
    public static void Render()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        UrbanVehiclePreviewBuilder.Lighting();
        var obj=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Car));
        obj.GetComponent<PreviewRoadVehicle>().enabled=false;
        var groups=obj.GetComponentsInChildren<LODGroup>();
        // Explicit reference images compare the vendor LOD2 with the new fixed LOD0.
        foreach(var g in groups)
        {
            foreach(var r in g.GetLODs().SelectMany(l=>l.renderers).Where(r=>r))r.enabled=true;
            g.enabled=true;g.ForceLOD(Mathf.Min(2,g.lodCount-1));
        }
        UrbanVehiclePreviewBuilder.Capture("car26-vendor-lod2.png",new Vector3(3.3f,1.2f,4.2f),new Vector3(0,.65f,0),46);
        UrbanVehiclePreviewBuilder.UseFinestGeometry(obj);
        UrbanVehiclePreviewBuilder.Capture("car26-fixed-finest.png",new Vector3(3.3f,1.2f,4.2f),new Vector3(0,.65f,0),46);
        foreach(var w in obj.GetComponent<PreviewRoadVehicle>().wheels)w.localRotation*=Quaternion.Euler(45,0,0);
        UrbanVehiclePreviewBuilder.Capture("car26-fixed-wheel45.png",new Vector3(3.3f,1.2f,4.2f),new Vector3(0,.65f,0),46);
        UnityEngine.Object.DestroyImmediate(obj);
        EditorSceneManager.OpenScene("Assets/Scenes/FirstRideTest.unity");
        RideLaunchRequest.Mode=RideDisplayMode.Desktop;
        var session=UnityEngine.Object.FindFirstObjectByType<RideTestSession>();
        typeof(RideTestSession).GetMethod("Awake",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(session,null);
        session.SetupUI.Buttons.Single(b=>b.name=="Tab4").onClick.Invoke();
        var canvas=session.SetupUI.MenuCanvas;
        canvas.GetComponent<UnityEngine.UI.CanvasScaler>().enabled=false;
        canvas.renderMode=RenderMode.WorldSpace;
        canvas.transform.SetParent(null,false);
        canvas.transform.SetPositionAndRotation(new Vector3(0,2,2),Quaternion.identity);
        canvas.transform.localScale=Vector3.one*.001f;
        ((RectTransform)canvas.transform).sizeDelta=new Vector2(940,780);
        Canvas.ForceUpdateCanvases();
        UrbanVehiclePreviewBuilder.Capture("maneuver-menu.png",new Vector3(0,2,1),new Vector3(0,2,2),50);
    }
}
