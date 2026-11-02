// Offline scene acceptance check, never imported into the working project's Editor.
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

[InitializeOnLoad]
public static class VehiclePreviewPlayCheck
{
    const string Active="AIT.VehiclePreview.PlayCheck";
    static RideTestSession session;
    static PreviewRoadVehicle car;
    static PreviewTrafficSignal signal;
    static Keyboard keyboard;
    static int phase;
    static double since, began;
    static Vector3 position;
    static Quaternion wheel;
    static float turnHeading, previousYaw, maxExtent;
    static VehiclePreviewPlayCheck()
    {
        EditorApplication.playModeStateChanged+=s=>
        {
            if(s==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Active,false))
            {
                began=since=EditorApplication.timeSinceStartup;phase=0;
                EditorApplication.update+=Tick;
            }
        };
    }
    public static void Run()
    {
        UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline=null;
        QualitySettings.renderPipeline=null;
        EditorSceneManager.OpenScene(UrbanVehiclePreviewBuilder.ScenePath);
        FirstRideLaunch.Prepare(RideDisplayMode.Desktop);
        SessionState.SetBool(Active,true);EditorApplication.isPlaying=true;
    }
    static void Require(bool ok,string message) { if(!ok)throw new Exception(message); }
    static void Next() {phase++;since=EditorApplication.timeSinceStartup;}
    static void Tick()
    {
        if(!EditorApplication.isPlaying)return;
        try
        {
            if(EditorApplication.timeSinceStartup-began>50)throw new TimeoutException("Scene Play acceptance timed out");
            if(session)typeof(RideTestSession).GetField("focus",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(session,true);
            double elapsed=EditorApplication.timeSinceStartup-since;
            if(phase==0)
            {
                session=UnityEngine.Object.FindFirstObjectByType<RideTestSession>();
                if(!session || !session.Settings || !session.Display.Ready)return;
                car=UnityEngine.Object.FindObjectsByType<PreviewRoadVehicle>(FindObjectsSortMode.None).Single(c=>c.enabled);
                signal=car.signal;signal.cycleInPlay=false;signal.colour=PreviewSignalColour.Red;signal.Apply();
                Require(car.session==session && signal.session==session,"Traffic is not connected to the riding session");
                Require(car.wheels.Length==4,"Car26 wheel pivots missing");
                foreach(var renderer in car.GetComponentsInChildren<MeshRenderer>(true))
                    Require(renderer.sharedMaterials.All(m=>m && m.shader.name=="Universal Render Pipeline/Lit"),"Car material missing or not URP");
                keyboard=InputSystem.AddDevice<Keyboard>("VehiclePreviewKeyboard");session.Settings.inputSource=RideInputSource.Keyboard;
                Application.runInBackground=true;
                InputSystem.onBeforeUpdate+=FeedInput;
                InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
                InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                session.Settings.mirrorsEnabled=false;
                foreach(var c in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))c.enabled=false;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());
                position=car.transform.position;Next();
            }
            else if(phase==1 && elapsed>.5f)
            {
                Require(!session.Running && car.transform.position==position,"Car moved before the rider pressed Start");
                session.SetupUI.Buttons.Single(b=>b.name=="Start").onClick.Invoke();Next();
            }
            else if(phase==2 && elapsed>.4f)
            {
                Require(session.Running,"Preview cannot start from its in-game menu: "+session.Status);
                wheel=car.wheels[0].localRotation;Next();
            }
            else if(phase==3 && elapsed>1)
            {
                Require(car.transform.position.z>position.z+.2f && Quaternion.Angle(wheel,car.wheels[0].localRotation)>1,"Car did not drive with rotating wheels");
                InputSystem.EnableDevice(keyboard);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));InputSystem.Update();
                Debug.Log("Vehicle check injected W="+keyboard.wKey.isPressed+" enabled="+keyboard.enabled+" phase="+phase);Next();
            }
            else if(phase==4 && elapsed>1.8f)
            {
                Require(session.Model.speed>0 && session.Model.position.z>-90,"Rider cannot drive in the new scene: "+session.Status+" speed="+session.Model.speed+" throttle="+session.CurrentInput.throttle+" running="+session.Running+" current="+(Keyboard.current==keyboard)+" pressed="+keyboard.wKey.isPressed);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Space));Next();
            }
            else if(phase==5 && elapsed>.5f)
            {
                Require(!session.Running,"STOP did not pause the session");position=car.transform.position;Next();
            }
            else if(phase==6 && elapsed>1)
            {
                Require(car.transform.position==position,"Traffic did not pause with the riding menu");
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());Next();
            }
            else if(phase==7 && elapsed>.3f)
            { session.SetupUI.Buttons.Single(b=>b.name=="Start").onClick.Invoke();Time.timeScale=4;Next(); }
            else if(phase==8 && elapsed>7)
            {
                Require(session.Running,"Riding session stopped unexpectedly: "+session.Status);
                Require(Mathf.Abs(car.transform.position.z-car.stopZ)<.1f && car.Speed<.05f,"Car failed to stop for red before the crosswalk: "+car.transform.position);
                var box=car.GetComponent<BoxCollider>();
                Require(box.bounds.max.z < -10,"Car nose crossed the stop line");
                position=car.transform.position;signal.colour=PreviewSignalColour.Green;signal.Apply();Next();
            }
            else if(phase==9 && elapsed>2)
            {
                Require(car.transform.position.z>position.z+1,"Car failed to resume on green");
                session.Pause("Maneuver controls check");Next();
            }
            else if(phase==10 && elapsed>.3f)
            {
                session.SetupUI.Buttons.Single(b=>b.name=="Tab4").onClick.Invoke();
                session.SetupUI.Buttons.Single(b=>b.name=="ManeuverDefaults").onClick.Invoke();
                session.SetupUI.Buttons.Single(b=>b.name=="SteeringLockPlus").onClick.Invoke();
                Require(session.Settings.lowSpeedSteeringDegrees==40,"In-game maneuver control did not apply");
                session.SetupUI.Buttons.Single(b=>b.name=="ManeuverDefaults").onClick.Invoke();
                session.SetupUI.Buttons.Single(b=>b.name=="Tab0").onClick.Invoke();
                session.SetupUI.Buttons.Single(b=>b.name=="Start").onClick.Invoke();Next();
            }
            else if(phase==11 && elapsed>.4f)
            {
                Require(session.Running,"Cannot resume for reverse check");position=session.Model.position;
                InputSystem.EnableDevice(keyboard);InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.R));InputSystem.Update();Next();
            }
            else if(phase==12 && elapsed>1)
            {
                Require(session.Model.speed<-.1f && session.Model.position.z<position.z-.1f,"R key did not drive backwards in Play: "+session.Status);
                Next();
            }
            else if(phase==13 && elapsed>.6f)
            {
                Require(session.Model.speed==0,"Releasing reverse did not stop");
                session.Pause("U-turn check");session.Model.Reset(new Vector3(-83.25f,0,-84),0);
                session.Settings.maximumSpeedKph=8;Next();
            }
            else if(phase==14 && elapsed>.3f)
            { session.SetupUI.Buttons.Single(b=>b.name=="Start").onClick.Invoke();Next(); }
            else if(phase==15 && elapsed>.4f)
            {
                Require(session.Running,"Cannot resume for U-turn");session.Model.speed=8/3.6f;
                turnHeading=previousYaw=maxExtent=0;Next();
            }
            else if(phase==16)
            {
                turnHeading+=Mathf.Abs(Mathf.DeltaAngle(previousYaw,session.Model.yaw));previousYaw=session.Model.yaw;
                float a=previousYaw*Mathf.Deg2Rad;
                maxExtent=Mathf.Max(maxExtent,Mathf.Abs(session.Model.position.x+85)+.385f*Mathf.Abs(Mathf.Cos(a))+.952f*Mathf.Abs(Mathf.Sin(a)));
                Require(session.Running,"U-turn stopped on a scene obstacle: "+session.Status);
                if(turnHeading>=180)
                {
                    Require(maxExtent<4,"Actual U-turn left the 8 m road: "+maxExtent);
                    Finish(null);
                }
            }
        }
        catch(Exception e){Finish(e);}
    }
    static void FeedInput()
    {
        if(keyboard==null)return;
        InputSystem.QueueStateEvent(keyboard,phase==4?new KeyboardState(Key.W):
            phase==5 || phase==6?new KeyboardState(Key.Space):phase==12?new KeyboardState(Key.R):
            phase==16?new KeyboardState(Key.W,Key.A):new KeyboardState());
    }
    static void Finish(Exception error)
    {
        EditorApplication.update-=Tick;SessionState.SetBool(Active,false);Time.timeScale=1;
        InputSystem.onBeforeUpdate-=FeedInput;
        if(keyboard!=null && keyboard.added)InputSystem.RemoveDevice(keyboard);
        File.WriteAllText("Personal/VehiclePreview/play-check.txt",error==null?
            "PASS: actual Desktop Play, menu Start, rider acceleration/STOP, traffic pause/red/green, four wheels rotate, in-game maneuver controls, R reverses/release stops, 8 km/h U-turn fits the actual 8 m road with scooter footprint; all Car26 materials are URP.":error.ToString());
        if(error!=null)Debug.LogException(error);
        EditorApplication.Exit(error==null?0:1);
    }
}
