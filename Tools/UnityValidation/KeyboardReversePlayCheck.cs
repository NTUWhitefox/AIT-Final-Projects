// Isolated Editor only: real Play loop, synthetic devices, no OS input.
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
public static class KeyboardReversePlayCheck
{
    const string Active = "AIT.KeyboardReverse.Check";
    static RideTestSession session;
    static Keyboard keyboard;
    static Gamepad pad;
    static RideInput padProbe;
    static RideInputFrame padFrame;
    static bool padSampled;
    static Key[] keys = Array.Empty<Key>();
    static int phase;
    static double began, since;
    static bool sawBrake;
    static Vector3 start;
    static readonly System.Collections.Generic.List<string> results = new System.Collections.Generic.List<string>();

    static KeyboardReversePlayCheck()
    {
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Active, false))
            { began = since = EditorApplication.timeSinceStartup; phase = 0; EditorApplication.update += Tick; }
        };
    }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/MotorSimulatorAssets/BuildingComparisonLocal/Scenes/StreetscapeKobo.unity");
        FirstRideLaunch.Prepare(RideDisplayMode.Desktop);
        Directory.CreateDirectory("Personal/KeyboardReverse");
        SessionState.SetBool(Active, true); EditorApplication.isPlaying = true;
    }
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Next(params Key[] held) { keys = held; phase++; since = EditorApplication.timeSinceStartup; }
    static void Feed()
    {
        if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
        if (keyboard != null && keyboard.added) InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        if (pad != null && pad.added) InputSystem.QueueStateEvent(pad, new GamepadState { leftTrigger = 1 });
    }
    static void ObservePad()
    {
        // InputSystem maintains separate Editor and player state. Sample in
        // the same Dynamic context as the runtime session, not Editor Tick.
        if (!padProbe || InputState.currentUpdateType != InputUpdateType.Dynamic) return;
        pad.MakeCurrent(); padFrame = padProbe.ReadInput(); padSampled = true;
    }
    static void StartRide() => session.SetupUI.Buttons.Single(b => b.name == "Start").onClick.Invoke();
    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        try
        {
            if (EditorApplication.timeSinceStartup - began > 65) throw new TimeoutException("phase=" + phase + " " + session?.Status);
            double elapsed = EditorApplication.timeSinceStartup - since;
            if (session) typeof(RideTestSession).GetField("focus", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(session, true);
            if (phase == 0)
            {
                session = UnityEngine.Object.FindFirstObjectByType<RideTestSession>();
                if (!session || !session.Settings || !session.Display.Ready) return;
                Application.runInBackground = true;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
                foreach (var device in InputSystem.devices.OfType<Mouse>().ToArray()) InputSystem.RemoveDevice(device);
                keyboard = InputSystem.AddDevice<Keyboard>("ReverseCheckKeyboard"); InputSystem.onBeforeUpdate += Feed;
                session.Settings.inputSource = RideInputSource.Keyboard; session.Settings.mirrorsEnabled = false;
                foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) camera.enabled = false;
                foreach (var car in UnityEngine.Object.FindObjectsByType<CityTrafficVehicle>(FindObjectsSortMode.None))
                { car.enabled = false; foreach (var c in car.GetComponentsInChildren<Collider>()) c.enabled = false; }
                foreach (var ped in UnityEngine.Object.FindObjectsByType<CityPedestrian>(FindObjectsSortMode.None))
                { ped.enabled = false; foreach (var c in ped.GetComponentsInChildren<Collider>()) c.enabled = false; }
                Require(session.SetupUI.IsOpen, "Initial menu missing"); Next(Key.S);
            }
            else if (phase == 1 && elapsed > .4)
            {
                Require(session.CurrentInput.reverse == 1, "S from rest did not request reverse");
                StartRide(); Require(!session.Running, "Start must reject a held S");
                results.Add("Holding S in the menu cannot start riding."); Next();
            }
            else if (phase == 2 && elapsed > .3) { StartRide(); Require(session.Running, "Start failed after release"); Next(Key.W); }
            else if (phase == 3 && session.Model.speed > 4) { Next(Key.S); }
            else if (phase == 4)
            {
                Require(session.Running, "Brake/reverse interrupted: " + session.Status);
                if (session.Model.speed > .3f && elapsed > .05)
                { Require(session.CurrentInput.brake == 1 && session.CurrentInput.reverse == 0, "S must brake while moving forward"); sawBrake = true; }
                Require(session.Model.speed >= -session.Settings.maximumReverseSpeedKph / 3.6f - .001f, "Reverse cap exceeded");
                if (elapsed < 3 || session.Model.speed > -.3f) return;
                Require(sawBrake && session.CurrentInput.reverse == 1 && session.CurrentInput.brake == 0, "Held S did not change from brake to reverse");
                results.Add("W drives forward; held S uses the brake channel first, then reverses within the configured cap."); Next();
            }
            else if (phase == 5 && elapsed > .6)
            { Require(Mathf.Abs(session.Model.speed) < .001f, "Releasing S did not stop"); start = session.Model.position; Next(Key.S); }
            else if (phase == 6 && elapsed > 1.5)
            {
                Require(session.Model.speed < -.3f && session.Model.position.z < start.z - .2f, "S did not reverse from rest");
                results.Add("S also reverses from rest; releasing it stops the scooter."); Next(Key.W);
            }
            else if (phase == 7 && session.Model.speed > 2) { Next(Key.W, Key.S); }
            else if (phase == 8 && elapsed > 1)
            {
                Require(session.Running && Mathf.Abs(session.Model.speed) < .001f, "W+S did not stop");
                Require(session.CurrentInput.throttle == 1 && session.CurrentInput.brake == 1 && session.CurrentInput.reverse == 0, "W+S priority incorrect");
                results.Add("W while backing stops before going forward; W+S gives braking priority and cannot reverse."); Next(Key.R);
            }
            else if (phase == 9 && elapsed > 1.5)
            { Require(session.Model.speed < -.3f, "Legacy R reverse failed"); Next(); }
            else if (phase == 10 && elapsed > .6)
            {
                Require(Mathf.Abs(session.Model.speed) < .001f, "R release-to-stop failed");
                session.Pause("Gamepad brake probe"); pad = InputSystem.AddDevice<Gamepad>("ReverseCheckGamepad"); InputSystem.EnableDevice(pad);
                var settings = ScriptableObject.CreateInstance<RideTestSettings>(); settings.inputSource = RideInputSource.Gamepad;
                padProbe = new GameObject("GamepadBrakeProbe").AddComponent<RideInput>(); padProbe.settings = settings; padProbe.SignedSpeed = -1;
                InputSystem.onAfterUpdate += ObservePad; Next();
            }
            else if (phase == 11 && elapsed > .3 && padSampled)
            {
                var settings = padProbe.settings;
                var frame = padFrame; Require(frame.connected && frame.brake > .99f && frame.reverse == 0,
                    $"Gamepad probe: connected={frame.connected}, brake={frame.brake}, reverse={frame.reverse}");
                var model = new PlanarScooterModel(); model.Reset(Vector3.zero, 0); model.speed = 4;
                for (int i = 0; i < 180; i++) { model.Step(frame, settings, settings.maximumSpeedKph, 1f / 90); Require(model.speed >= 0, "Physical brake crossed zero"); }
                Require(model.speed == 0, "Gamepad brake failed to stop");
                results.Add("R and release-to-stop remain valid. Gamepad left trigger remains stop-only."); Finish(null);
            }
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        SessionState.SetBool(Active, false); EditorApplication.update -= Tick; InputSystem.onBeforeUpdate -= Feed;
        InputSystem.onAfterUpdate -= ObservePad;
        if (padProbe) { UnityEngine.Object.Destroy(padProbe.settings); UnityEngine.Object.Destroy(padProbe.gameObject); }
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
        File.WriteAllText("Personal/KeyboardReverse/play-check.txt", error == null ? "PASS\n" + string.Join("\n", results) + "\n" : "FAIL " + error);
        if (error != null) Debug.LogException(error); EditorApplication.Exit(error == null ? 0 : 1);
    }
}
