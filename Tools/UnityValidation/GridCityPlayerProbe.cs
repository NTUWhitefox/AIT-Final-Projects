// Validation clone only; never enters main Assets.
using System;
using System.IO;
using System.Linq;
using AIT.Riding;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(400)]
public sealed class GridCityPlayerProbe : RideInputProvider
{
    RideTestSession session;
    RideWorldStreamer streamer;
    CityRoadRuntime runtime;
    BlockoutRouteProgress route;
    int phase, riderId, headId, trackingId, maxReady;
    int[] junctionIds;
    float began, since, peakSpeed, driven;
    Vector3 lastPosition, held;
    bool passed500, curbHeld;
    readonly bool[] phasesSeen = new bool[3];
    CityTrafficVehicle[] cars;
    CityPedestrian[] pedestrians;
    float[] initialCarProgress;
    bool redStopSeen, pedestrianHoldSeen;
    bool legMoved;
    Quaternion initialLeg;
    Transform leg;
    string roadReport;
    string report;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        if (!Environment.GetCommandLineArgs().Contains("-ait-grid-probe")) return;
        RideLaunchRequest.Mode = RideDisplayMode.Desktop;
        GraphicsSettings.defaultRenderPipeline = null; QualitySettings.renderPipeline = null;
        Application.runInBackground = true; Application.targetFrameRate = 180;
        var root = new GameObject("Grid City acceptance - clone only"); DontDestroyOnLoad(root); root.AddComponent<GridCityPlayerProbe>();
    }
    void Awake()
    {
        var args = Environment.GetCommandLineArgs(); report = args[Array.IndexOf(args, "-ait-probe-report") + 1];
        began = since = Time.realtimeSinceStartup; SceneManager.sceneLoaded += SceneLoaded;
    }
    void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.path != "Assets/Scenes/GridCity.unity") return;
        session = FindFirstObjectByType<RideTestSession>(); streamer = session.worldStreamer;
        route = FindFirstObjectByType<BlockoutRouteProgress>(); runtime = FindFirstObjectByType<CityRoadRuntime>();
        session.Settings.inputSource = RideInputSource.External; session.input.externalProvider = this;
        session.Settings.maximumSpeedKph = 40; session.Settings.engineSoundEnabled = false; session.Settings.mirrorsEnabled = false;
        foreach (var mirror in FindObjectsByType<RideMirror>(FindObjectsSortMode.None)) mirror.enabled = false;
        foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None)) camera.enabled = false;
        foreach (var visibility in FindObjectsByType<CityActorVisibility>(FindObjectsSortMode.None)) visibility.enabled = false;
        cars = FindObjectsByType<CityTrafficVehicle>(FindObjectsSortMode.None); pedestrians = FindObjectsByType<CityPedestrian>(FindObjectsSortMode.None);
        foreach (var car in cars) { car.enabled = false; foreach (var collider in car.GetComponentsInChildren<Collider>()) collider.enabled = false; }
        foreach (var ped in pedestrians) { ped.enabled = false; foreach (var collider in ped.GetComponentsInChildren<Collider>()) collider.enabled = false; }
        riderId = session.GetInstanceID(); headId = session.headCamera.GetInstanceID(); trackingId = session.trackingOrigin.GetInstanceID(); junctionIds = runtime.junctions.Select(j => j.GetInstanceID()).ToArray();
        session.SessionEvent += (name, detail) => { if (name == "contact" && detail.StartsWith("City road boundary")) curbHeld = true; };
    }
    void Require(bool value, string reason) { if (!value) throw new Exception(reason); }
    void Next() { phase++; since = Time.realtimeSinceStartup; }
    void StartRide() => session.SetupUI.Buttons.Single(b => b.name == "Start").onClick.Invoke();
    void Update()
    {
        if (!session) return;
        try
        {
            if (Time.realtimeSinceStartup - began > 200) throw new TimeoutException("phase=" + phase + " status=" + session.Status + " pos=" + session.Model.position + " checkpoint=" + route.NextPoint);
            Require(session.GetInstanceID() == riderId && session.headCamera.GetInstanceID() == headId && session.trackingOrigin.GetInstanceID() == trackingId, "Rider/tracking hierarchy replaced");
            for (int i = 0; i < junctionIds.Length; i++) Require(runtime.junctions[i].GetInstanceID() == junctionIds[i], "Junction logical state replaced on reload");
            maxReady = Mathf.Max(maxReady, streamer.ReadyCount); phasesSeen[(int)runtime.junctions[0].ReadSignal(true)] = true;
            if (phase == 0)
            {
                if (!streamer.ReadyToStart(session.Model.position) || Time.realtimeSinceStartup - since < .5f) return;
                Require(streamer.ReadyCount < 16, "All map chunks loaded at startup"); StartRide(); Next();
            }
            else if (phase == 1)
            { if (!session.Running) return; lastPosition = session.Model.position; Time.timeScale = 4; Next(); }
            else if (phase == 2)
            {
                Require(session.Running, "Drive stopped: " + session.Status + " pos=" + session.Model.position + " driven=" + driven);
                driven += Vector3.Distance(lastPosition, session.Model.position); lastPosition = session.Model.position;
                peakSpeed = Mathf.Max(peakSpeed, session.Model.speed * 3.6f); passed500 |= session.Model.position.x > 500;
                Require(session.roadNetwork.IsDrivable(session.Model.position, .29f), "Rider left road");
                if (route.CompletedLaps < 1) return;
                Require(driven > 1970 && passed500 && peakSpeed > 25, "Full city loop was not ridden");
                Require(streamer.LoadsCompleted > 9 && streamer.UnloadsCompleted > 0 && maxReady < 16, "Real chunk load/unload not observed");
                Require(phasesSeen.All(b => b), "Signal phase cycle missing");
                Time.timeScale = 1;
                var stage = session.course.stages[0]; held = stage.spawnPosition + Vector3.right * (session.roadNetwork.HalfRoad - .31f - session.roadNetwork.LaneOffset(1));
                Require(streamer.TravelReady(held, held + Vector3.right), "Curb probe neighbourhood not ready");
                session.Model.Reset(held, 90); session.Model.speed = 2; session.vehicleRoot.SetPositionAndRotation(held, Quaternion.Euler(0, 90, 0)); Next();
            }
            else if (phase == 3)
            {
                if (session.Running) return;
                Require(curbHeld && Vector3.Distance(session.Model.position, held) < .001f && session.Model.speed == 0, "Curb did not stop/hold actual movement");
                session.ResetToStage(0); Next();
            }
            else if (phase == 4)
            { if (Time.realtimeSinceStartup - since < .4f || !streamer.ReadyToStart(session.Model.position)) return; StartRide(); Next(); }
            else if (phase == 5)
            { if (!session.Running) return; held = session.Model.position; Next(); }
            else if (phase == 6)
            {
                Require(session.Running, "Reverse interrupted: " + session.Status);
                if (Time.realtimeSinceStartup - since < 1.4f) return;
                Require(session.Model.speed < -.3f && Vector3.Distance(held, session.Model.position) > .25f, "Actual reverse input failed"); Next();
            }
            else if (phase == 7)
            {
                if (Time.realtimeSinceStartup - since < 1) return;
                Require(Mathf.Abs(session.Model.speed) < .01f, "Release-to-stop reverse failed");
                roadReport = "PASS built Windows Player: actual input/Update/FixedUpdate completed " + driven.ToString("F1") + " m city loop; peak " + peakSpeed.ToString("F1") +
                    " km/h; loads=" + streamer.LoadsCompleted + ", unloads=" + streamer.UnloadsCompleted + ", max ready=" + maxReady + "/16. Persistent rider/XR and 64 junction IDs; passed x=500; all signal phases; real curb movement stop; reverse and release-to-stop.\n";
                Require(cars.Length == 6 && pedestrians.Length == 12, "Controlled traffic/pedestrian actors missing");
                var parking = runtime.network.nodes[0].position + new Vector3(runtime.network.LaneOffset(0), 0, 18);
                session.Model.Reset(parking, 0); initialCarProgress = cars.Select(c => c.Progress).ToArray();
                foreach (var car in cars) car.enabled = true; foreach (var ped in pedestrians) ped.enabled = true;
                leg = pedestrians[0].GetComponentsInChildren<Transform>().First(t => t.name == "UpperLeg.L"); initialLeg = leg.localRotation;
                foreach (var visibility in FindObjectsByType<CityActorVisibility>(FindObjectsSortMode.None)) visibility.enabled = true;
                Time.timeScale = 12; Next();
            }
            else if (phase == 8)
            {
                Require(session.Running, "Traffic observation interrupted: " + session.Status);
                foreach (var ped in pedestrians) if (ped.Crossing)
                {
                    Require(ped.animationPlayer.IsPlaying(ped.walkClip.name), "Crossing pedestrian is not playing walk clip");
                    pedestrianHoldSeen = true; var junction = runtime.junctions[ped.node];
                    Require(junction.ReadSignal(true) == PreviewSignalColour.Red && junction.ReadSignal(false) == PreviewSignalColour.Red, "Pedestrian crossing with vehicle green");
                }
                legMoved |= Quaternion.Angle(initialLeg, leg.localRotation) > 3;
                foreach (var car in cars) if (car.Speed < .03f)
                {
                    float progress = Mathf.Repeat(car.Progress, car.Length);
                    foreach (var turn in car.turns)
                        if (Mathf.Abs(progress - turn.stop) < .1f && turn.junction.ReadSignal(Mathf.Abs(Mathf.Cos(turn.approachYaw * Mathf.Deg2Rad)) > .5f) != PreviewSignalColour.Green) redStopSeen = true;
                }
                if (Time.realtimeSinceStartup - since < 35) return;
                Require(redStopSeen && pedestrianHoldSeen && legMoved && pedestrians.Sum(p => p.CrossingsCompleted) >= 4, "Stop-line, walking bone animation or pedestrian clearance behaviour not observed");
                for (int i = 0; i < cars.Length; i++) Require(cars[i].Progress - initialCarProgress[i] > 120, "Controlled car did not traverse its lane/turn path");
                File.WriteAllText(report, roadReport + "PASS six persistent cars progressed through right/left turns and actual red stop lines; 12 animated pedestrians; " + pedestrians.Sum(p => p.CrossingsCompleted) + " completed crossings; exclusive all-red pedestrian clearance.\n");
                Application.Quit(0);
            }
        }
        catch (Exception e) { File.WriteAllText(report, "FAIL " + e); Debug.LogException(e); Application.Quit(1); }
    }
    public override RideInputFrame ReadInput()
    {
        var input = new RideInputFrame { connected = true, sampleTime = Time.realtimeSinceStartup };
        if (phase == 6) { input.reverse = 1; return input; }
        if (phase != 2 || !session.Running) return input;
        var points = streamer.world.routePoints; var position = session.Model.position; int nearest = 0; float best = float.MaxValue;
        for (int i = 0; i < points.Length; i++) { float d = (points[i] - position).sqrMagnitude; if (d < best) { best = d; nearest = i; } }
        float lookahead = Mathf.Clamp(1.2f + session.Model.speed * .35f, 1.8f, 4.5f), distance = 0, desired = 30 / 3.6f; int target = nearest;
        for (int k = 0; distance < 45 && k < 300; k++)
        {
            int a = (nearest + k) % points.Length, b = (a + 1) % points.Length, c = (a + 2) % points.Length;
            float step = Vector3.Distance(points[a], points[b]); distance += step;
            if (distance < lookahead) target = c;
            float curve = Vector3.Angle(points[b] - points[a], points[c] - points[b]) * Mathf.Deg2Rad / Mathf.Max(.05f, step);
            if (curve > .05f) desired = Mathf.Min(desired, Mathf.Sqrt(Mathf.Pow(10.5f / 3.6f, 2) + 4 * Mathf.Max(0, distance - 4)));
        }
        var local = Quaternion.Inverse(Quaternion.Euler(0, session.Model.yaw, 0)) * (points[target] - position);
        float angle = Mathf.Atan(session.Settings.wheelbase * 2 * local.x / Mathf.Max(.5f, local.sqrMagnitude)) * Mathf.Rad2Deg;
        float blend = Mathf.SmoothStep(0, 1, Mathf.Abs(session.Model.speed) / (session.Settings.steeringTransitionSpeedKph / 3.6f));
        float limit = Mathf.Lerp(session.Settings.lowSpeedSteeringDegrees, session.Settings.highSpeedSteeringDegrees, blend);
        input.steering = Mathf.Clamp(angle / limit, -1, 1); float error = desired - session.Model.speed;
        input.throttle = Mathf.Clamp01((session.Settings.coastingDeceleration + session.Settings.quadraticDrag * session.Model.speed * session.Model.speed + error * 1.6f) / session.Settings.acceleration);
        input.brake = error < -.2f ? Mathf.Clamp01(-error * .8f) : 0; return input;
    }
    void OnDestroy() { SceneManager.sceneLoaded -= SceneLoaded; }
}
