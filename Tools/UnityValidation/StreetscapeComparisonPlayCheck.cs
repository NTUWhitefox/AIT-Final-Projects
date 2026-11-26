// Isolated batch Editor only; synthetic keyboard input, no Windows input or UI automation.
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
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class StreetscapeComparisonPlayCheck
{
    const string Active = "AIT.Streetscape.Check";
    const string TrafficOnly = "AIT.Streetscape.TrafficOnly";
    static RideTestSession session;
    static CityTrafficVehicle[] cars;
    static CityPedestrian[] pedestrians;
    static Keyboard keyboard;
    static int phase, variant;
    static double began, since;
    static Vector3 start;
    static float[] carStarts;
    static Quaternion[] wheelStarts;
    static Vector3[] pedestrianStarts;
    static WalkCheck[] gaitChecks;
    static bool sawAllRed;
    static string commonRoad;
    static readonly System.Collections.Generic.List<string> results = new System.Collections.Generic.List<string>();

    sealed class WalkCheck
    {
        readonly CityPedestrian pedestrian;
        readonly Transform foot, hand;
        Vector3 firstFoot, firstHand;
        bool sampled;
        float footTravel, handTravel;
        public bool Passed => footTravel > .08f && handTravel > .03f;
        public string Travel => $"foot {footTravel:F3} m, hand {handTravel:F3} m";

        public WalkCheck(CityPedestrian pedestrian)
        {
            this.pedestrian = pedestrian;
            Require(pedestrian.animationPlayer && pedestrian.walkClip, $"{pedestrian.name} has no walk player or clip after opening the scene");
            var bones = pedestrian.animationPlayer.GetComponentsInChildren<Transform>();
            foot = bones.Single(bone => bone.name == "foot_l");
            hand = bones.Single(bone => bone.name == "hand_l");
        }

        public void Sample()
        {
            var player = pedestrian.animationPlayer;
            var walk = player[pedestrian.walkClip.name];
            if (!player.IsPlaying(walk.name) || walk.time < .20f) return;
            Require(Mathf.Abs(walk.speed - pedestrian.speed / pedestrian.walkReferenceSpeed) < .02f, "Wrong walk playback speed");
            Vector3 relativeFoot = pedestrian.transform.InverseTransformPoint(foot.position);
            Vector3 relativeHand = pedestrian.transform.InverseTransformPoint(hand.position);
            if (!sampled)
            {
                firstFoot = relativeFoot;
                firstHand = relativeHand;
                sampled = true;
            }
            footTravel = Mathf.Max(footTravel, Vector3.Distance(relativeFoot, firstFoot));
            handTravel = Mathf.Max(handTravel, Vector3.Distance(relativeHand, firstHand));
        }
    }
    static StreetscapeComparisonPlayCheck()
    {
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Active, false))
            { began = since = EditorApplication.timeSinceStartup; phase = variant = 0; EditorApplication.update += Tick; }
        };
    }
    public static void Run()
    {
        SessionState.SetBool(TrafficOnly, false);
        EditorSceneManager.OpenScene(PathFor(0)); FirstRideLaunch.Prepare(RideDisplayMode.Desktop);
        Directory.CreateDirectory("Personal/BuildingComparison");
        SessionState.SetBool(Active, true); EditorApplication.isPlaying = true;
    }
    public static void RunKoboTraffic()
    {
        SessionState.SetBool(TrafficOnly, true);
        EditorSceneManager.OpenScene(RideScenes.KoboTrafficPath); FirstRideLaunch.Prepare(RideDisplayMode.Desktop);
        Directory.CreateDirectory("Personal/KoboTraffic");
        SessionState.SetBool(Active, true); EditorApplication.isPlaying = true;
    }
    static string PathFor(int index) => StreetscapeComparisonBuilder.Root + "/Scenes/Streetscape" + (index == 0 ? "Pasha" : "Kobo") + ".unity";
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Next() { phase++; since = EditorApplication.timeSinceStartup; }
    static void Feed()
    {
        if (keyboard == null || !keyboard.added || InputState.currentUpdateType != InputUpdateType.Dynamic) return;
        InputSystem.QueueStateEvent(keyboard, phase == 3 ? new KeyboardState(Key.W) : phase == 6 ? new KeyboardState(Key.R) : new KeyboardState());
    }
    static void StartRide() => session.SetupUI.Buttons.Single(b => b.name == "Start").onClick.Invoke();
    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        try
        {
            if (EditorApplication.timeSinceStartup - began > 95) throw new TimeoutException("variant=" + variant + " phase=" + phase + " " + session?.Status);
            double elapsed = EditorApplication.timeSinceStartup - since;
            if (session) typeof(RideTestSession).GetField("focus", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(session, true);
            if (phase == 0)
            {
                session = UnityEngine.Object.FindFirstObjectByType<RideTestSession>(); if (!session || !session.Settings || !session.Display.Ready) return;
                UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = null; QualitySettings.renderPipeline = null;
                Application.runInBackground = true;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
                // Batch mode has no Game window: don't request native cursor capture.
                foreach (var device in InputSystem.devices.OfType<Mouse>().ToArray()) InputSystem.RemoveDevice(device);
                if (keyboard == null) { keyboard = InputSystem.AddDevice<Keyboard>("StreetscapeKeyboard"); InputSystem.onBeforeUpdate += Feed; }
                InputSystem.EnableDevice(keyboard); session.Settings.inputSource = RideInputSource.Keyboard; session.Settings.mirrorsEnabled = false;
                foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) camera.enabled = false;
                cars = UnityEngine.Object.FindObjectsByType<CityTrafficVehicle>(FindObjectsSortMode.None); pedestrians = UnityEngine.Object.FindObjectsByType<CityPedestrian>(FindObjectsSortMode.None);
                bool trafficOnly = SessionState.GetBool(TrafficOnly, false);
                Require(cars.Length == (trafficOnly ? 3 : 2) && pedestrians.Length == (trafficOnly ? 4 : 2), "Expected traffic and crossing pedestrians");
                if (trafficOnly)
                {
                    var bike = cars.Single(c => c.steeringAssembly);
                    Require(bike.leaningVisual.Find("Helmeted rider") && bike.wheels.Length == 2, "Helmeted rider or scooter wheel missing");
                    Require(cars.Where(c => c != bike).All(c => c.wheels.Length == 3 && c.wheelbase > 2), "Car wheels missing");
                    float eye = session.headCamera.transform.position.y - session.vehicleRoot.position.y;
                    float roof = cars.Single(c => c.name == "Sedan").GetComponent<BoxCollider>().bounds.max.y;
                    Require(Mathf.Abs(eye - session.Settings.seatedEyePosition.y) < .01f &&
                            session.headCamera.transform.position.y < roof, "Runtime seat is not below the sedan roof");
                    Require(Mathf.Abs(Mathf.DeltaAngle(session.headCamera.transform.localEulerAngles.x, session.Settings.desktopLookPitch)) < .2f,
                            "Runtime camera did not use the street view pitch");
                    results.Add($"Street camera in Play: eye height {eye:F2} m; sedan roof {roof:F2} m; initial pitch {session.Settings.desktopLookPitch:F0} degrees down.");
                }
                Require(session.roadNetwork && !session.worldStreamer && session.Display.Mode == RideDisplayMode.Desktop, "Desktop/common road setup failed");
                string road = string.Join("|", UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Where(m => m.sharedMesh.name == "Asphalt" || m.sharedMesh.name == "White" || m.sharedMesh.name == "Pavement").Select(m => AssetDatabase.GetAssetPath(m.sharedMesh)).OrderBy(p => p));
                Require(road.Length > 0 && (variant == 0 || commonRoad == road), "Variants don't share the same road assets"); commonRoad = road;
                foreach (var car in cars) { car.enabled = false; foreach (var c in car.GetComponentsInChildren<Collider>()) c.enabled = false; }
                foreach (var ped in pedestrians) { ped.enabled = false; foreach (var c in ped.GetComponentsInChildren<Collider>()) c.enabled = false; }
                Require(session.SetupUI.IsOpen, "Start menu did not open"); start = session.Model.position; Next();
            }
            else if (phase == 1 && elapsed > .4) { StartRide(); Next(); }
            else if (phase == 2 && elapsed > .3)
            { Require(session.Running, "Scene could not start: " + session.Status); Time.timeScale = 4; Next(); }
            else if (phase == 3)
            {
                Require(session.Running, "Straight ride interrupted: " + session.Status);
                Require(session.roadNetwork.IsDrivable(session.Model.position, .3f), "Rider left common road");
                if (session.Model.position.z - start.z < 100) return;
                Require(session.Model.speed * 3.6f > 25, "Shared speed input not working");
                results.Add((SessionState.GetBool(TrafficOnly, false) ? "Kobo traffic" : variant == 0 ? "Pasha" : "Kobo") + ": rode 100 m through the normal Desktop menu/start and input loop.");
                Time.timeScale = 1; session.Pause("Reverse check"); Next();
            }
            else if (phase == 4 && elapsed > .4) { StartRide(); Next(); }
            else if (phase == 5 && elapsed > .2) { Require(session.Running, "Resume failed"); start = session.Model.position; Next(); }
            else if (phase == 6 && elapsed > 1.4)
            { Require(session.Running && session.Model.speed < -.2f && session.Model.position.z < start.z - .2f, "Reverse failed"); Next(); }
            else if (phase == 7 && elapsed > .8)
            {
                Require(Mathf.Abs(session.Model.speed) < .01f, "Release reverse failed to stop");
                session.Model.Reset(new Vector3(4.875f, 0, 70), 0);
                carStarts = cars.Select(c => c.Progress).ToArray(); foreach (var car in cars) car.enabled = true; foreach (var ped in pedestrians) ped.enabled = true;
                wheelStarts = cars.Select(c => c.wheels[0].localRotation).ToArray();
                pedestrianStarts = pedestrians.Select(p => p.transform.position).ToArray();
                if (SessionState.GetBool(TrafficOnly, false))
                    gaitChecks = pedestrians.Select(pedestrian => new WalkCheck(pedestrian)).ToArray();
                Time.timeScale = 8; Next();
            }
            else if (phase == 8)
            {
                bool trafficOnly = SessionState.GetBool(TrafficOnly, false);
                if (trafficOnly)
                {
                    for (int i = 0; i < pedestrians.Length; i++)
                    {
                        var ped = pedestrians[i];
                        if (ped.useSidewalkRoute)
                        {
                            Require(Mathf.Abs(ped.transform.position.x - ped.sidewalkStart.x) < .01f &&
                                    !session.roadNetwork.IsDrivable(ped.transform.position, .2f), "Walker left the sidewalk");
                            gaitChecks[i].Sample();
                        }
                        else if (ped.Crossing)
                        {
                            gaitChecks[i].Sample();
                            var j = ped.runtime.junctions[ped.node];
                            Require(j.PedestrianClearance && j.ReadSignal(true) == PreviewSignalColour.Red && j.ReadSignal(false) == PreviewSignalColour.Red,
                                "Crossing without all-red clearance");
                            sawAllRed = true;
                        }
                    }
                }
                if (elapsed <= 6 || (trafficOnly && !pedestrians.Any(p => p.CrossingsCompleted > 0) && elapsed < 14)) return;
                Require(session.Running && cars.Where((car, i) => car.Progress - carStarts[i] > 10).Count() == cars.Length, "Traffic did not progress");
                if (trafficOnly)
                {
                    Require(cars.Where((car, i) => Quaternion.Angle(car.wheels[0].localRotation, wheelStarts[i]) > .1f).Count() == cars.Length, "A traffic wheel did not rotate");
                    Require(pedestrians.Where((ped, i) => ped.useSidewalkRoute && Vector3.Distance(ped.transform.position, pedestrianStarts[i]) > 3).Count() == 2,
                        "Sidewalk walkers did not advance");
                    for (int i = 0; i < pedestrians.Length; i++)
                    {
                        var ped = pedestrians[i];
                        var gait = gaitChecks[i];
                        Require(gait.Passed, $"{ped.name} moved without changing its walk pose ({gait.Travel})");
                        results.Add($"{ped.name} walk pose varied relative to the body ({gait.Travel}).");
                    }
                    Require(sawAllRed && pedestrians.Any(p => p.CrossingsCompleted > 0), "No complete controlled pedestrian crossing");
                    results.Add("Both sidewalk walkers advanced within their paths with paced walk animation; controlled crossing completed with both signals red.");
                }
                var before = cars.Select(c => c.transform.position).ToArray(); session.SetupUI.Open();
                Require(!session.Running && session.SetupUI.IsOpen, "Menu failed to pause");
                results.Add("Reverse/release-to-stop passed; all " + cars.Length + " traffic vehicles progressed, crossing pedestrians present.");
                Time.timeScale = 1;
                if (variant == 0 && !SessionState.GetBool(TrafficOnly, false))
                {
                    variant = 1; phase = 0; since = EditorApplication.timeSinceStartup;
                    EditorSceneManager.LoadSceneInPlayMode(PathFor(1), new LoadSceneParameters(LoadSceneMode.Single)); session = null;
                }
                else Finish(null);
            }
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        SessionState.SetBool(Active, false); EditorApplication.update -= Tick; InputSystem.onBeforeUpdate -= Feed; Time.timeScale = 1;
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        string folder = SessionState.GetBool(TrafficOnly, false) ? "KoboTraffic" : "BuildingComparison";
        File.WriteAllText("Personal/" + folder + "/play-check.txt", error == null ? "PASS\n" + string.Join("\n", results) + "\n" : "FAIL " + error);
        if (error != null) Debug.LogException(error); EditorApplication.Exit(error == null ? 0 : 1);
    }
}
