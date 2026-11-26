// Isolated batch Editor only. Synthetic input never sends mouse/keyboard events to Windows.
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
using Cursor = AIT.Validation.DesktopLookPointer;

[InitializeOnLoad]
public static class DesktopLookPlayCheck
{
    const string Active = "AIT.DesktopLook.Check";
    static RideTestSession session;
    static Keyboard keyboard;
    static Mouse mouse;
    static int phase;
    static double began, since;
    static Quaternion initial, looked;
    static Vector3 position;
    static float yaw;
    static DesktopLookPlayCheck()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Active, false)) return;
            began = since = EditorApplication.timeSinceStartup; phase = 0; EditorApplication.update += Tick;
        };
    }
    public static void Run()
    {
        Directory.CreateDirectory("Personal/DesktopLook");
        EditorSceneManager.OpenScene("Assets/Scenes/CityCorners.unity");
        FirstRideLaunch.Prepare(RideDisplayMode.Desktop);
        SessionState.SetBool(Active, true); EditorApplication.isPlaying = true;
    }
    static void Require(bool ok, string text) { if (!ok) throw new Exception(text); }
    static void Next() { phase++; since = EditorApplication.timeSinceStartup; }
    static void StartRide() => session.SetupUI.Buttons.Single(b => b.name == "Start").onClick.Invoke();
    static void Field(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
    static void Feed()
    {
        if (InputState.currentUpdateType != InputUpdateType.Dynamic || keyboard == null) return;
        var key = phase == 5 ? Key.C : phase == 7 ? Key.Escape : phase == 10 ? Key.F2 : Key.None;
        InputSystem.QueueStateEvent(keyboard, key == Key.None ? new KeyboardState() : new KeyboardState(key));
        var delta = phase == 3 || phase == 8 ? new Vector2(30, 10) : phase == 6 ? new Vector2(20000, 20000) : Vector2.zero;
        InputSystem.QueueStateEvent(mouse, new MouseState { delta = delta });
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        try
        {
            if (EditorApplication.timeSinceStartup - began > 45) throw new TimeoutException("phase=" + phase + "; " + session?.Status);
            double elapsed = EditorApplication.timeSinceStartup - since;
            // A batch Editor has no focused Game window; simulate focus without OS input.
            if (session && phase < 13) { Field(session, "focus", true); Field(session.Display, "desktopHasFocus", true); }
            if (phase == 0)
            {
                session = UnityEngine.Object.FindFirstObjectByType<RideTestSession>();
                if (!session || !session.Settings || !session.Display.Ready) return;
                UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = null; QualitySettings.renderPipeline = null;
                Application.runInBackground = true;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
                keyboard = InputSystem.AddDevice<Keyboard>("DesktopLookKeyboard");
                mouse = InputSystem.AddDevice<Mouse>("DesktopLookMouse");
                InputSystem.EnableDevice(keyboard); InputSystem.EnableDevice(mouse); InputSystem.onBeforeUpdate += Feed;
                foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) camera.enabled = false;
                foreach (var car in UnityEngine.Object.FindObjectsByType<CityTrafficVehicle>(FindObjectsSortMode.None)) car.enabled = false;
                session.Settings.inputSource = RideInputSource.Keyboard; session.Settings.mirrorsEnabled = false;
                Field(session, "focus", true); Field(session.Display, "desktopHasFocus", true);
                initial = session.headCamera.transform.localRotation; position = session.Model.position; yaw = session.Model.yaw;
                Require(Cursor.lockState == CursorLockMode.None && Cursor.visible, "Initial menu captured the cursor"); Next();
            }
            else if (phase == 1 && elapsed > .3) { StartRide(); Next(); }
            else if (phase == 2 && elapsed > .3)
            {
                Require(session.Running && Cursor.lockState == CursorLockMode.Locked && !Cursor.visible, "Start failed to capture: " + session.Status + "; cursor=" + Cursor.lockState + "; visible=" + Cursor.visible + "; mouse=" + Mouse.current);
                Next();
            }
            else if (phase == 3 && elapsed > .3)
            {
                Require(session.Running && Quaternion.Angle(initial, session.headCamera.transform.localRotation) > 5, "Mouse did not turn the head");
                Require(Vector3.Distance(position, session.Model.position) < .001f && Mathf.Abs(Mathf.DeltaAngle(yaw, session.Model.yaw)) < .001f, "Looking changed vehicle motion");
                looked = session.headCamera.transform.localRotation; Next();
            }
            else if (phase == 4 && elapsed > .2)
            { Require(Quaternion.Angle(looked, session.headCamera.transform.localRotation) < .01f, "View drifted without mouse motion"); Next(); }
            else if (phase == 5 && elapsed > .2)
            { Require(Quaternion.Angle(initial, session.headCamera.transform.localRotation) < .01f, "C did not reset to the profile view"); Next(); }
            else if (phase == 6 && elapsed > .2)
            {
                Require(Mathf.Abs(Mathf.DeltaAngle(0, session.headCamera.transform.localEulerAngles.x) + 80) < .01f, "Pitch limit failed");
                looked = session.headCamera.transform.localRotation; Next();
            }
            else if (phase == 7 && elapsed > .2)
            { Require(!session.Running && session.SetupUI.IsOpen && Cursor.lockState == CursorLockMode.None && Cursor.visible, "Esc did not pause/release"); Next(); }
            else if (phase == 8 && elapsed > .2)
            { Require(Quaternion.Angle(looked, session.headCamera.transform.localRotation) < .01f, "Menu mouse movement changed the view"); StartRide(); Next(); }
            else if (phase == 9 && elapsed > .2)
            { Require(session.Running && Cursor.lockState == CursorLockMode.Locked && Quaternion.Angle(looked, session.headCamera.transform.localRotation) < .01f, "Resume lost head pose/capture"); Next(); }
            else if (phase == 10 && elapsed > .2)
            { Require(!session.Running && session.SetupUI.IsOpen && Cursor.lockState == CursorLockMode.None && Cursor.visible, "F2 did not pause/release"); Next(); }
            else if (phase == 11 && elapsed > .2) { StartRide(); Next(); }
            else if (phase == 12 && elapsed > .2)
            { Require(session.Running, "Could not resume before focus check"); session.gameObject.SendMessage("OnApplicationFocus", false); Next(); }
            else if (phase == 13 && elapsed > .2)
            { Require(!session.Running && Cursor.lockState == CursorLockMode.None && Cursor.visible, "Focus loss failed to release/pause"); session.gameObject.SendMessage("OnApplicationFocus", true); Next(); }
            else if (phase == 14 && elapsed > .2)
            { Require(!session.Running && Cursor.lockState == CursorLockMode.None, "Focus return resumed without user action"); StartRide(); Next(); }
            else if (phase == 15 && elapsed > .2)
            {
                Require(session.Running && Cursor.lockState == CursorLockMode.Locked, "Could not recapture after focus return");
                Cursor.lockState = CursorLockMode.None; Next();
            }
            else if (phase == 16 && elapsed > .2)
            {
                Require(!session.Running && session.SetupUI.IsOpen && Cursor.visible, "External pointer release was immediately recaptured"); StartRide(); Next();
            }
            else if (phase == 17 && elapsed > .2)
            {
                Require(session.Running && Cursor.lockState == CursorLockMode.Locked, "Could not resume after external pointer release");
                session.Display.enabled = false;
                Require(Cursor.lockState == CursorLockMode.None && Cursor.visible, "Disabling display trapped cursor"); Finish(null);
            }
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        SessionState.SetBool(Active, false); EditorApplication.update -= Tick; InputSystem.onBeforeUpdate -= Feed;
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        File.WriteAllText("Personal/DesktopLook/play-check.txt", error == null
            ? "PASS actual Play loop: synthetic mouse turns head independently of vehicle; no idle drift; C reset; pitch limit; Esc/menu/resume; F2; simulated focus loss/return and external release; disable releases pointer. Native Cursor API substituted in clone only; OS pointer capture requires a manual Game-view check.\n"
            : "FAIL phase=" + phase + " " + error);
        if (error != null) Debug.LogException(error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
