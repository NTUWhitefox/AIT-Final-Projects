// Offline Play check using a synthetic gamepad. No Windows input or user editor interaction.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using AIT.Riding;
using AIT.Riding.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class NavigationDistrictPlayCheck
{
    const string Active = "AIT.NavigationDistrict.PlayCheck";
    const string Output = "Personal/NavigationDistrict";
    static readonly FieldInfo Focus = typeof(RideTestSession).GetField("focus", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly List<string> results = new List<string>();
    static RideTestSession session;
    static Gamepad pad;
    static Layout layout;
    static Route current;
    static int routeIndex, segment;
    static bool driving, startRequested;
    enum BarrierPhase { None, Approach, Slide, Reverse }
    static BarrierPhase barrierPhase;
    static Vector3 contactPoint, reversePoint;
    static bool headOn;
    static double barrierDeadline, slideCheckTime;
    static double begun, restart;
    static float distance, maximumError;

    [Serializable] sealed class Layout { public Route[] routes; }
    [Serializable] sealed class Route { public string id; public Vector3[] samples; }

    static NavigationDistrictPlayCheck()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Active, false)) return;
            begun = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        };
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene(RideScenes.NavigationDistrictPath);
        FirstRideLaunch.Prepare(RideDisplayMode.Desktop);
        SessionState.SetBool(Active, true);
        EditorApplication.isPlaying = true;
    }

    static void Tick()
    {
        try
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - begun > 105)
                throw new TimeoutException($"Route {current?.id}, segment {segment}: {session?.Status} at {session?.Model.position}");
            if (!session)
            {
                session = Object.FindFirstObjectByType<RideTestSession>();
                if (!session) return;
                layout = JsonUtility.FromJson<Layout>(File.ReadAllText(Output + "/paths.json"));
                VerifyAsphaltMesh();
                GraphicsSettingsForBatch();
                pad = InputSystem.AddDevice<Gamepad>("NavigationCheckPad");
                InputSystem.EnableDevice(pad);
                InputSystem.onBeforeUpdate += Feed;
                session.Settings.inputSource = RideInputSource.Gamepad;
                session.Settings.mirrorsEnabled = false;
                restart = now + .6;
            }
            Focus.SetValue(session, true);
            if (barrierPhase != BarrierPhase.None)
            {
                CheckBarrier(now);
                return;
            }
            if (!driving)
            {
                if (!session.Display.Ready || now < restart) return;
                current = layout.routes[routeIndex];
                segment = 0;
                distance = maximumError = 0;
                if (!startRequested)
                {
                    session.SetupUI.Buttons.Single(button => button.name == "Start").onClick.Invoke();
                    startRequested = true;
                }
                if (!session.Running)
                {
                    Require(now - restart < 3, "Cannot start: " + session.Status);
                    return;
                }
                startRequested = false;
                Time.timeScale = 7;
                driving = true;
                return;
            }
            Require(session.Running, $"{current.id} paused at segment {segment}, {session.Model.position} ({session.Status})");
            if (Vector3.Distance(session.Model.position, current.samples[current.samples.Length - 1]) > 2) return;
            Require(segment >= current.samples.Length - 3, $"{current.id} reached the finish without following the route");
            Require(distance > 570 && maximumError < 2.5f, $"{current.id} out of tolerance: {distance:F1} m, error {maximumError:F2} m");
            results.Add($"{current.id}: rode {distance:F1} m using Desktop Gamepad input; maximum lane-path deviation {maximumError:F2} m.");
            if (++routeIndex == layout.routes.Length)
            {
                BeginBarrierCheck(now);
                return;
            }
            driving = false;
            session.Pause("Route complete");
            Time.timeScale = 1;
            session.ResetToStage(0);
            restart = now + .6;
        }
        catch (Exception error) { Finish(error); }
    }

    static void GraphicsSettingsForBatch()
    {
        UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = null;
        QualitySettings.renderPipeline = null;
        Application.runInBackground = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) camera.enabled = false;
        foreach (var mouse in InputSystem.devices.OfType<Mouse>().ToArray()) InputSystem.RemoveDevice(mouse);
    }

    static void Feed()
    {
        if (!pad.added || InputState.currentUpdateType != InputUpdateType.Dynamic) return;
        if (!driving || !session.Running)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState());
            return;
        }
        if (barrierPhase != BarrierPhase.None)
        {
            var state = barrierPhase == BarrierPhase.Reverse ? new GamepadState().WithButton(GamepadButton.East) :
                new GamepadState { rightTrigger = .7f };
            InputSystem.QueueStateEvent(pad, state);
            return;
        }
        var points = current.samples;
        var model = session.Model;
        var (nearest, error) = NearestPoint(points, model.position);
        maximumError = Mathf.Max(maximumError, error);
        distance += Mathf.Max(0, model.speed) * Time.deltaTime;
        float lookahead = Mathf.Clamp(model.speed * .7f, 3.1f, 4.5f);
        Vector3 target = Advance(points, nearest, segment, lookahead);
        Vector3 future = Advance(points, nearest, segment, 15);
        Vector3 forward = Quaternion.Euler(0, model.yaw, 0) * Vector3.forward;
        float lookaheadAngle = Vector3.SignedAngle(forward, target - model.position, Vector3.up);
        float previewAngle = Vector3.Angle(target - model.position, future - model.position);
        float steering = Mathf.Clamp(lookaheadAngle / 23, -1, 1);
        // Compensate for device and simulator dead zones in the synthetic test.
        if (Mathf.Abs(steering) > .004f) steering = Mathf.Sign(steering) * (.22f + .78f * Mathf.Abs(steering));
        float speed = Mathf.Lerp(7f, 2.6f, Mathf.Clamp01(previewAngle / 23));
        float difference = speed - model.speed;
        InputSystem.QueueStateEvent(pad, new GamepadState
        {
            leftStick = Vector2.right * steering,
            rightTrigger = Mathf.Clamp01(.18f + difference * .72f),
            leftTrigger = Mathf.Clamp01(-difference * .8f - .1f)
        });
    }

    static void BeginBarrierCheck(double now)
    {
        Require(session.roadNetwork.HasRoadBarriers, "No invisible road barriers after opening the scene");
        session.SessionEvent += ObserveBarrier;
        StartBarrierApproach(now);
    }

    static void StartBarrierApproach(double now)
    {
        Vector3 start = layout.routes[0].samples[0] + new Vector3(29, 0, -2.5f);
        session.Model.Reset(start, headOn ? 180 : 135);
        barrierPhase = BarrierPhase.Approach;
        barrierDeadline = now + 8;
        Time.timeScale = 2;
    }

    static void ObserveBarrier(string name, string detail)
    {
        if (barrierPhase != BarrierPhase.Approach || name != "contact" || detail != "City road barrier") return;
        contactPoint = session.Model.position;
        barrierPhase = BarrierPhase.Slide;
        slideCheckTime = EditorApplication.timeSinceStartup + .4;
        barrierDeadline = EditorApplication.timeSinceStartup + 5;
    }

    static void CheckBarrier(double now)
    {
        Require(now < barrierDeadline, "Barrier maneuver timed out: " + barrierPhase);
        Require(session.Running && !session.SetupUI.IsOpen, "Road edge paused or opened the ride menu");
        Vector3 position = session.Model.position;
        Require(session.roadNetwork.IsDrivable(position, .3f), "Rider crossed the invisible barrier");
        if (barrierPhase == BarrierPhase.Slide && (headOn ? now >= slideCheckTime : position.x > contactPoint.x + 1))
        {
            Require(Mathf.Abs(position.z - contactPoint.z) < .1f, "Rider did not follow the road edge");
            if (headOn)
                Require(Vector3.Distance(position, contactPoint) < .08f && Mathf.Abs(session.Model.speed) < .15f,
                    "Head-on contact did not stop the scooter");
            reversePoint = position;
            barrierPhase = BarrierPhase.Reverse;
            barrierDeadline = now + 5;
        }
        if (barrierPhase != BarrierPhase.Reverse || position.z < reversePoint.z + .35f || session.Model.speed >= 0) return;
        if (!headOn)
        {
            results.Add("Slid along an invisible road edge and reversed away with the ride still active.");
            headOn = true;
            StartBarrierApproach(now);
            return;
        }
        results.Add("A head-on contact stopped at the same boundary and reversed without opening the menu.");
        Finish(null);
    }

    static (Vector3 point, float error) NearestPoint(Vector3[] points, Vector3 position)
    {
        float best = float.PositiveInfinity;
        Vector3 nearest = points[segment];
        int selected = segment;
        for (int i = segment; i < Mathf.Min(segment + 12, points.Length - 1); i++)
        {
            var line = points[i + 1] - points[i];
            float t = Mathf.Clamp01(Vector3.Dot(position - points[i], line) / line.sqrMagnitude);
            var point = points[i] + line * t;
            float error = (point - position).sqrMagnitude;
            if (error >= best) continue;
            best = error;
            nearest = point;
            selected = i;
        }
        segment = selected;
        return (nearest, Mathf.Sqrt(best));
    }

    static void VerifyAsphaltMesh()
    {
        GameObject asphalt = GameObject.Find("Navigation district/Asphalt");
        Require(asphalt, "Missing saved road mesh");
        var collider = asphalt.AddComponent<MeshCollider>();
        collider.sharedMesh = asphalt.GetComponent<MeshFilter>().sharedMesh;
        Physics.SyncTransforms();
        foreach (var route in layout.routes)
        for (int i = 1; i < route.samples.Length; i++)
        {
            Vector3 a = route.samples[i - 1], b = route.samples[i];
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 2));
            for (int j = 0; j <= steps; j++)
            {
                var p = Vector3.Lerp(a, b, (float)j / steps);
                Require(collider.Raycast(new Ray(p + Vector3.up * 2, Vector3.down), out _, 3),
                    $"The saved asphalt mesh misses route {route.id} at {p}");
            }
        }
        Object.Destroy(collider);
        results.Add("All three paths align with the saved asphalt mesh after reopening the scene.");

        GameObject pavement = GameObject.Find("Navigation district/Pavement");
        Require(pavement, "Missing the arterial sidewalk");
        var sidewalk = pavement.AddComponent<MeshCollider>();
        sidewalk.sharedMesh = pavement.GetComponent<MeshFilter>().sharedMesh;
        Physics.SyncTransforms();
        var arterial = layout.routes[0].samples[0] + new Vector3(0, 2, -7);
        var alley = new Vector3(213.4f - 340, 2, 54 - 75);
        Require(sidewalk.Raycast(new Ray(arterial, Vector3.down), out _, 3), "Arterial sidewalk was removed");
        Require(!sidewalk.Raycast(new Ray(alley, Vector3.down), out _, 3), "Residential alley still has a raised sidewalk");
        Object.Destroy(sidewalk);
        results.Add("Arterial sidewalk remains and the alley beside the paired right turns has a flat edge.");
    }

    static Vector3 Advance(Vector3[] points, Vector3 start, int segmentIndex, float distance)
    {
        for (int i = segmentIndex + 1; i < points.Length; i++)
        {
            var end = points[i];
            float length = Vector3.Distance(start, end);
            if (length > distance) return Vector3.Lerp(start, end, distance / length);
            distance -= length;
            start = end;
        }
        return points[points.Length - 1];
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static void Finish(Exception error)
    {
        SessionState.SetBool(Active, false);
        EditorApplication.update -= Tick;
        InputSystem.onBeforeUpdate -= Feed;
        if (session) session.SessionEvent -= ObserveBarrier;
        Time.timeScale = 1;
        if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
        File.WriteAllText(Output + "/play-check.txt", error == null ? "PASS\n" + string.Join("\n", results) + "\n" : "FAIL " + error);
        if (error != null) Debug.LogException(error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
