// Compiled only in the ignored validation clone, outside its Editor folder.
// The working project never imports this controller or its player-only probe flag.
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using AIT.Riding;

[DefaultExecutionOrder(400)] public sealed class StreamingPlayerProbe : RideInputProvider
{
    RideTestSession session;
    RideWorldStreamer streamer;
    BlockoutRouteProgress route;
    int phase,sessionId,headId,trackingId,maxReady;
    float began,since,maximumSpeed;
    bool beyond500,movementHeld,startRejected;
    StreamingChunkDefinition fault;
    string correctPath;
    Vector3 faultPosition;
    string reportPath;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        if(!Environment.GetCommandLineArgs().Contains("-ait-stream-probe"))return;
        RideLaunchRequest.Mode=RideDisplayMode.Desktop;
        GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;
        var obj=new GameObject("Streaming player acceptance - clone only");DontDestroyOnLoad(obj);obj.AddComponent<StreamingPlayerProbe>();
    }
    void Awake()
    {
        var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-ait-probe-report");reportPath=i>=0?args[i+1]:"stream-probe.txt";
        began=since=Time.realtimeSinceStartup;
        SceneManager.sceneLoaded+=SceneLoaded;
    }
    void SceneLoaded(Scene scene,LoadSceneMode mode)
    {
        if(scene.path!="Assets/Scenes/StreamingBlockout.unity")return;
        session=FindFirstObjectByType<RideTestSession>();streamer=session.worldStreamer;route=FindFirstObjectByType<BlockoutRouteProgress>();
        session.Settings.inputSource=RideInputSource.External;session.input.externalProvider=this;
        session.Settings.mirrorsEnabled=false;session.Settings.engineSoundEnabled=false;session.Settings.maximumSpeedKph=40;
        foreach(var m in FindObjectsByType<RideMirror>(FindObjectsSortMode.None))m.enabled=false;
        foreach(var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))camera.enabled=false;
        sessionId=session.GetInstanceID();headId=session.headCamera.GetInstanceID();trackingId=session.trackingOrigin.GetInstanceID();
        session.SessionEvent+=(name,detail)=>{if(name=="streaming_wait")movementHeld=true;};
    }
    void Require(bool value,string message){if(!value)throw new Exception(message);}
    void Next(){phase++;since=Time.realtimeSinceStartup;}
    void Update()
    {
        if(!session)return;
        try
        {
            if(Time.realtimeSinceStartup-began>160)throw new TimeoutException("Streaming player probe timed out, phase="+phase+" status="+session.Status+" pos="+session.Model.position);
            maxReady=Mathf.Max(maxReady,streamer.ReadyCount);
            Require(session.GetInstanceID()==sessionId&&session.headCamera.GetInstanceID()==headId&&session.trackingOrigin.GetInstanceID()==trackingId,"Persistent rider/XR objects were replaced");
            if(phase==0)
            {
                Require(!session.Running,"World started without Start");
                if(!streamer.ReadyToStart(session.Model.position)||Time.realtimeSinceStartup-since<.4f)return;
                Require(streamer.ReadyCount>0&&streamer.ReadyCount<streamer.world.chunks.Length,"Prototype loaded the whole map at startup");
                session.SetupUI.Buttons.Single(b=>b.name=="Start").onClick.Invoke();Next();
            }
            else if(phase==1)
            {
                if(!session.Running)return;
                Time.timeScale=6;Next();
            }
            else if(phase==2)
            {
                Require(session.Running,"Drive interrupted: "+session.Status+" pos="+session.Model.position+" counts="+streamer.ReadyCount);
                maximumSpeed=Mathf.Max(maximumSpeed,session.Model.speed*3.6f);beyond500|=session.Model.position.x>500;
                Require(streamer.TravelReady(session.Model.position,session.Model.position),"Rider entered an unready area");
                if(route.CompletedLaps<1)return;
                Require(streamer.LoadsCompleted>9&&streamer.UnloadsCompleted>0,"Loop did not cause real additive scene load/unload");
                Require(beyond500&&maximumSpeed>28,"Old 500 m boundary or input prevented full riding");
                Require(maxReady<16,"All 16 chunks stayed resident");
                Time.timeScale=1;
                fault=streamer.world.chunks.OrderByDescending(c=>Vector3.Distance(c.bounds.center,session.Model.position))
                    .First(c=>!SceneManager.GetSceneByPath(c.scenePath).isLoaded);
                correctPath=fault.scenePath;fault.scenePath="Assets/Scenes/StreamingBlockoutChunks/Missing_probe.unity";
                faultPosition=fault.bounds.center;session.Model.Reset(faultPosition,0);session.Model.speed=2;
                session.vehicleRoot.SetPositionAndRotation(faultPosition,Quaternion.identity);
                Require(!streamer.TravelReady(faultPosition,faultPosition+Vector3.forward),"Unloaded area was treated as rideable");
                Next();
            }
            else if(phase==3)
            {
                if(session.Running)return; // wait for the real FixedUpdate movement gate
                Require(movementHeld&&Vector3.Distance(session.Model.position,faultPosition)<.001f&&session.Model.speed==0,"Movement gate did not hold the rider pose and stop");
                if(!startRejected){Require(!session.TryStart(),"Start allowed riding into an unloaded area");startRejected=true;}
                if(streamer.Failures==0)return;
                fault.scenePath=correctPath;streamer.RetryFailed();Next();
            }
            else if(phase==4)
            {
                if(!streamer.ReadyToStart(faultPosition))return;
                Require(Physics.Raycast(faultPosition+Vector3.up*2,Vector3.down,out var hit,5,Physics.DefaultRaycastLayers),"Reloaded floor collider is missing");
                Require(hit.collider.gameObject.scene.path==correctPath,"Collider came from a proxy or wrong scene");
                session.SetupUI.Buttons.Single(b=>b.name=="Start").onClick.Invoke();Next();
            }
            else if(phase==5&&session.Running)
            {
                File.WriteAllText(reportPath,"PASS built Windows Player: full 2.2 km loop using actual RideInputProvider/Update/FixedUpdate; additive scene loads="+streamer.LoadsCompleted+
                    ", unloads="+streamer.UnloadsCompleted+", maximum ready="+maxReady+"/16; passed x=500; persistent rider/head/tracking IDs; unloaded-area Start/movement guard; missing-scene failure and retry; reloaded real floor collider; recovered Start. Peak speed="+maximumSpeed.ToString("F1")+" km/h.\n");
                Application.Quit(0);
            }
        }
        catch(Exception e){File.WriteAllText(reportPath,"FAIL "+e);Debug.LogException(e);Application.Quit(1);}
    }
    public override RideInputFrame ReadInput()
    {
        var input=new RideInputFrame{connected=true,sampleTime=Time.realtimeSinceStartup};
        if(phase!=2||!session.Running)return input;
        var points=streamer.world.routePoints;var p=session.Model.position;
        int nearest=0;float best=float.MaxValue;
        for(int i=0;i<points.Length;i++){float d=(points[i]-p).sqrMagnitude;if(d<best){best=d;nearest=i;}}
        float lookahead=10+session.Model.speed*.5f;int target=nearest;float distance=0;
        while(distance<lookahead){int next=(target+1)%points.Length;distance+=Vector3.Distance(points[target],points[next]);target=next;}
        var local=Quaternion.Inverse(Quaternion.Euler(0,session.Model.yaw,0))*(points[target]-p);
        float curvature=2*local.x/Mathf.Max(4,local.sqrMagnitude);
        float angle=Mathf.Atan(session.Settings.wheelbase*curvature)*Mathf.Rad2Deg;
        float blend=Mathf.SmoothStep(0,1,Mathf.Abs(session.Model.speed)/(session.Settings.steeringTransitionSpeedKph/3.6f));
        float maxSteer=Mathf.Lerp(session.Settings.lowSpeedSteeringDegrees,session.Settings.highSpeedSteeringDegrees,blend);
        input.steering=Mathf.Clamp(angle/maxSteer,-1,1);
        float desired=(Mathf.Abs(angle)>.7f?25:35)/3.6f;
        float error=desired-session.Model.speed;
        input.throttle=Mathf.Clamp01((session.Settings.coastingDeceleration+session.Settings.quadraticDrag*session.Model.speed*session.Model.speed+error*1.6f)/session.Settings.acceleration);
        input.brake=error<-.5f?Mathf.Clamp01(-error*.5f):0;
        return input;
    }
    void OnDestroy(){SceneManager.sceneLoaded-=SceneLoaded;}
}
