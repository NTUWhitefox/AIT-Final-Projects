using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AIT.Riding;
using AIT.Riding.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Actual Play acceptance in the isolated clone. No OS input or working editor control.
[InitializeOnLoad] public static class CityCornersPlayCheck
{
    const string Active="AIT.CityCorners.Check";
    static RideTestSession session;
    static CityTrafficVehicle[] cars;
    static CityJunction centre;
    static Keyboard keyboard;
    static int phase;
    static double began,since;
    static bool sawLeft,sawRight,sawBlink;
    static float heading,lastYaw;
    static Vector3 snapshot;
    static Quaternion handRest;
    static Transform hands;
    static CityCornersPlayCheck()
    {
        EditorApplication.playModeStateChanged+=s=>
        {if(s==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(Active,false))
            {began=since=EditorApplication.timeSinceStartup;phase=0;EditorApplication.update+=Tick;}};
    }
    public static void Run()
    {
        UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;
        EditorSceneManager.OpenScene(CityCornersBuilder.ScenePath);FirstRideLaunch.Prepare(RideDisplayMode.Desktop);
        SessionState.SetBool(Active,true);EditorApplication.isPlaying=true;
    }
    static void Require(bool ok,string text){if(!ok)throw new Exception(text);}
    static void Next(){phase++;since=EditorApplication.timeSinceStartup;}
    static void Click(string name)=>session.SetupUI.Buttons.Single(b=>b.name==name).onClick.Invoke();
    static void Field(object o,string name,object value)=>o.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,value);
    static void Feed()
    {
        if(keyboard==null)return;
        InputSystem.QueueStateEvent(keyboard,phase==3?new KeyboardState(Key.W):phase==4?new KeyboardState(Key.Space):phase==7?new KeyboardState(Key.W,Key.D):new KeyboardState());
    }
    static void Tick()
    {
        if(!EditorApplication.isPlaying)return;
        try
        {
            if(EditorApplication.timeSinceStartup-began>70)throw new TimeoutException("City Play check timed out, phase "+phase);
            double elapsed=EditorApplication.timeSinceStartup-since;
            if(session)Field(session,"focus",true);
            if(phase==0)
            {
                session=UnityEngine.Object.FindFirstObjectByType<RideTestSession>();if(!session||!session.Settings||!session.Display.Ready)return;
                Application.runInBackground=true;
                InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
                keyboard=InputSystem.AddDevice<Keyboard>("CityCornersKeyboard");InputSystem.EnableDevice(keyboard);InputSystem.onBeforeUpdate+=Feed;
                session.Settings.inputSource=RideInputSource.Keyboard;session.Settings.mirrorsEnabled=false;
                foreach(var cam in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))cam.enabled=false;
                cars=UnityEngine.Object.FindObjectsByType<CityTrafficVehicle>(FindObjectsSortMode.None).OrderBy(c=>c.name).ToArray();
                Require(cars.Length==2,"Moving traffic missing");foreach(var c in cars)c.enabled=false;
                centre=UnityEngine.Object.FindObjectsByType<CityJunction>(FindObjectsSortMode.None).Single(j=>j.signalControlled);
                Require(UnityEngine.Object.FindObjectsByType<CityJunction>(FindObjectsSortMode.None).Length==9,"Grid junctions missing");
                Require(!UnityEngine.Object.FindObjectsByType<TextMesh>(FindObjectsSortMode.None).Any(t=>t.name=="Sign text"&&t.text.Contains("km/h")),"Map speed boards still present");
                hands=session.GetComponent<RidePresentation>().handlebars.Find("Static rider hands");
                Require(hands&&hands.GetComponentsInChildren<MeshFilter>().Length==2,"Fixed hands missing");handRest=hands.localRotation;
                session.ResetToStage(1);session.Settings.maximumSpeedKph=40;Click("Tab5");Click("SpeedLimitPlus");
                Require(session.Settings.maximumSpeedKph==45&&session.StageSpeedCapKph==45,"Speed UI still limited by scene");
                RideSettingsProfiles.Save("city_check_speed",session.Settings);
                var loaded=RideSettingsProfiles.Load("city_check_speed");Require(loaded.maximumSpeedKph==45,"Speed does not persist in profiles");
                UnityEngine.Object.Destroy(loaded);RideSettingsProfiles.Delete("city_check_speed");Click("Tab0");Next();
            }
            else if(phase==1&&elapsed>.4){Click("Start");Next();}
            else if(phase==2&&elapsed>.4)
            {Require(session.Running,"City scene cannot start: "+session.Status);Time.timeScale=3;InputSystem.EnableDevice(keyboard);InputSystem.Update();Next();}
            else if(phase==3&&elapsed>4)
            {
                Require(session.Running&&session.Model.speed*3.6f>25,"Rider cannot exceed old map caps: "+session.Status+" speed="+session.Model.speed*3.6f);
                Next();
            }
            else if(phase==4&&elapsed>.5)
            {
                Require(!session.Running,"STOP failed");session.Model.Reset(new Vector3(2,0,-6),0);session.Settings.maximumSpeedKph=8;
                Next();
            }
            else if(phase==5&&elapsed>.3){Click("Start");Next();}
            else if(phase==6&&elapsed>.4)
            {Require(session.Running,"Cannot start corner ride");session.Model.speed=8/3.6f;heading=lastYaw=0;Next();}
            else if(phase==7)
            {
                Require(session.Running,"Corner ride stopped: "+session.Status);
                heading+=Mathf.Abs(Mathf.DeltaAngle(lastYaw,session.Model.yaw));lastYaw=session.Model.yaw;
                Require(CityCornersBuilder.OnRoad(session.Model.position),"Rider right turn left the road");
                Require(hands.localRotation==handRest&&hands.parent==session.GetComponent<RidePresentation>().handlebars,"Hands do not remain attached to steering");
                if(heading>=90){session.Pause("Traffic acceptance");session.ResetToStage(0);session.Model.Reset(new Vector3(25,0,25),0);foreach(var c in cars){c.enabled=true;c.Initialise();}Next();}
            }
            else if(phase==8&&elapsed>.3){Click("Start");Next();}
            else if(phase==9)
            {
                Require(session.Running,"Traffic session stopped: "+session.Status);
                foreach(var c in cars)
                {
                    sawLeft|=c.SteeringDegrees<-2;sawRight|=c.SteeringDegrees>2;
                    foreach(var r in c.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled&&r.name.Contains("TurnSignal")))
                    {var b=new MaterialPropertyBlock();r.GetPropertyBlock(b);sawBlink|=b.GetColor("_EmissionColor").maxColorComponent>1;}
                }
                Require(!Overlap(cars[0],cars[1]),"Traffic cars intersect while turning/queuing");
                Require(!(centre.northSouthSignal.colour==PreviewSignalColour.Green&&centre.eastWestSignal.colour==PreviewSignalColour.Green),"Conflicting green signals");
                if(elapsed>23)
                {
                    Require(sawLeft&&sawRight&&sawBlink,"Did not see both turns: left="+sawLeft+" right="+sawRight+" blink="+sawBlink+" "+string.Join(";",cars.Select(c=>c.name+" progress="+c.Progress+" speed="+c.Speed)));
                    Require(cars.All(c=>c.Progress>c.Length*.75f),"Traffic stalled before covering its junctions");
                    var right=cars.Single(c=>c.name.Contains("Right"));var left=cars.Single(c=>c.name.Contains("Left"));left.enabled=false;
                    foreach(var j in UnityEngine.Object.FindObjectsByType<CityJunction>(FindObjectsSortMode.None))j.Release(right);
                    right.Initialise();float s=right.turns[0].stop-2;Field(right,"<Progress>k__BackingField",s);right.SetPose(s);
                    Field(centre,"elapsed",23f);Next();
                }
            }
            else if(phase==10&&elapsed>2.5)
            {
                var right=cars.Single(c=>c.name.Contains("Right"));
                Require(Mathf.Abs(right.Progress-right.turns[0].stop)<.12f&&right.Speed<.1f,"Red-light traffic did not stop before crossing: "+right.Progress+" stop="+right.turns[0].stop);
                snapshot=right.transform.position;Field(centre,"elapsed",0f);Next();
            }
            else if(phase==11&&elapsed>1.7)
            {
                var right=cars.Single(c=>c.name.Contains("Right"));
                Require(Vector3.Distance(snapshot,right.transform.position)>3,"Traffic did not restart on green");
                right.Sample(right.Progress+12,out var rear,out float yaw,out _);
                session.Model.Reset(rear-Quaternion.Euler(0,yaw,0)*Vector3.forward*right.rearAxleZ,yaw);
                Next();
            }
            else if(phase==12&&elapsed>2)
            {
                var right=cars.Single(c=>c.name.Contains("Right"));
                Require(right.Speed<.05f&&Vector3.Distance(right.transform.position,session.Model.position)>3.5f,"Traffic failed to yield to a stopped rider");
                session.SetupUI.Open();snapshot=right.transform.position;Next();
            }
            else if(phase==13&&elapsed>.4)
            {
                Require(!session.Running&&Vector3.Distance(snapshot,cars.Single(c=>c.name.Contains("Right")).transform.position)<.001f,"Traffic does not pause with menu");Finish(null);
            }
        }
        catch(Exception e){Finish(e);}
    }
    static bool Overlap(CityTrafficVehicle a,CityTrafficVehicle b)
    {
        var delta=b.transform.position-a.transform.position;
        foreach(var axis in new[]{a.transform.forward,a.transform.right,b.transform.forward,b.transform.right})
        {
            float ra=Mathf.Abs(Vector3.Dot(a.transform.forward,axis))*2.56f+Mathf.Abs(Vector3.Dot(a.transform.right,axis))*1.06f;
            float rb=Mathf.Abs(Vector3.Dot(b.transform.forward,axis))*2.56f+Mathf.Abs(Vector3.Dot(b.transform.right,axis))*1.06f;
            if(Mathf.Abs(Vector3.Dot(delta,axis))>ra+rb)return false;
        }
        return true;
    }
    static void Finish(Exception error)
    {
        SessionState.SetBool(Active,false);EditorApplication.update-=Tick;InputSystem.onBeforeUpdate-=Feed;Time.timeScale=1;
        if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
        File.WriteAllText("Personal/VehiclePreview/city-play.txt",error==null?
            "PASS: nine real junctions; in-game speed/profile controls and actual riding above old limits; 90-degree right turn; two fixed hands follow steering; left/right traffic steering, indicators, no car overlap across the routes; red stop/green restart, yield to stopped rider, and menu pause.":error.ToString());
        if(error!=null)Debug.LogException(error);EditorApplication.Exit(error==null?0:1);
    }
}
