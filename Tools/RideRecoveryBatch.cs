// Copied into the isolated validation project by check_riding_recovery.py.
// Never imported into the user's working Unity project.
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
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;

[InitializeOnLoad]
public static class RideRecoveryBatch
{
    const string Active = "AIT.Recovery.PlayCheck";
    static readonly List<string> results = new List<string>();
    static RideTestSession session;
    static Keyboard keyboard;
    static Mouse mouse;
    static Vector2 pointer;
    static InputDevice xrController;
    static Vector3 vrPointerPosition;
    static string[] vrProfilesBefore;
    static int phase;
    static double since, began;
    static Vector3 start;
    static RideRecoveryBatch()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (!SessionState.GetBool(Active, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) Begin();
        };
    }
    static void Check(bool ok, string name)
    { if (!ok) throw new Exception(name); results.Add("PASS " + name); }
    static void Invoke(object obj, string method, params object[] args)
    { obj.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(obj, args); }
    static void Field(object obj, string name, object value)
    { obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(obj, value); }
    static Button Button(RideTestSession s, string name) => s.SetupUI.Buttons.Single(b => b.name == name);
    static void Click(RideTestSession s, string name)
    { Button(s, name).onClick.Invoke(); Invoke(s.SetupUI, "LateUpdate"); }
    static void Report(string file, Exception error = null)
    {
        if (error != null) results.Add("FAIL " + error);
        Directory.CreateDirectory("Personal"); File.WriteAllLines("Personal/" + file, results);
        if (error != null) Debug.LogException(error);
    }
    public static void Run()
    {
        try
        {
            FirstRideValidation.RunModelChecks(); results.Add("PASS native Unity model/profile checks");
            EditorSceneManager.OpenScene(FirstRideSceneBuilder.ScenePath);
            var s = UnityEngine.Object.FindFirstObjectByType<RideTestSession>();
            RideLaunchRequest.Mode = RideDisplayMode.Desktop; Invoke(s, "Awake");
            keyboard = InputSystem.AddDevice<Keyboard>("RecoveryTestKeyboard"); Invoke(s, "Update");
            Check(s.SetupUI.IsOpen && s.SetupUI.MenuCanvas.renderMode == RenderMode.ScreenSpaceOverlay, "Desktop starts with the in-game menu");
            Check(!s.Calibrated, "initially not calibrated");
            Click(s, "Start");
            Check(s.Running && s.Calibrated, "actual Start button starts Desktop and centers automatically: " + s.Status);
            Check(!s.SetupUI.IsOpen, "successful Start closes menu");
            s.SetupUI.Open(); Check(!s.Running && s.SetupUI.IsOpen, "opening the menu pauses riding and shows controls"); Invoke(s, "Update");
            Click(s, "Tab1"); float eye = s.Settings.seatedEyePosition.y; Click(s, "EyeHeightPlus");
            Check(Mathf.Abs(s.Settings.seatedEyePosition.y - eye - .02f) < .001f && s.Calibrated, "eye-height button applies and centers immediately");
            Check(Vector3.Distance(s.vehicleRoot.InverseTransformPoint(s.headCamera.transform.position), s.Settings.seatedEyePosition) < .001f,
                "camera reaches the adjusted eye position");
            Click(s, "Tab3"); var before = RideSettingsProfiles.Names(); Click(s, "NewProfile");
            string name = RideSettingsProfiles.Names().Except(before).Single(); string path = RideSettingsProfiles.ProfilePath(name);
            float saved = s.Settings.seatedEyePosition.y;
            s.Settings.seatedEyePosition += Vector3.up * .02f; Click(s, "LoadProfile");
            Check(Mathf.Abs(s.Settings.seatedEyePosition.y - saved) < .001f, "Load immediately restores saved settings");
            s.Settings.seatedEyePosition += Vector3.up * .02f; Click(s, "SaveProfile");
            var loaded = RideSettingsProfiles.Load(name);
            Check(Mathf.Abs(loaded.seatedEyePosition.y - s.Settings.seatedEyePosition.y) < .001f, "Save persists current settings");
            UnityEngine.Object.DestroyImmediate(loaded);
            byte[] savedBytes = File.ReadAllBytes(path);
            string renamed = name + "_renamed";
            RideSettingsProfiles.Rename(name, renamed);
            Check(!File.Exists(path) && File.ReadAllBytes(RideSettingsProfiles.ProfilePath(renamed)).SequenceEqual(savedBytes), "profile rename preserves exact contents");
            RideSettingsProfiles.Save(name, s.Settings);
            bool collision = false;
            try { RideSettingsProfiles.Rename(renamed, name); } catch (IOException) { collision = true; }
            Check(collision && File.Exists(RideSettingsProfiles.ProfilePath(renamed)) && File.Exists(path), "rename never overwrites another profile");
            RideSettingsProfiles.Delete(name);
            RideSettingsProfiles.Rename(renamed, renamed.ToUpperInvariant());
            RideSettingsProfiles.Rename(renamed.ToUpperInvariant(), name);
            Check(File.ReadAllBytes(path).SequenceEqual(savedBytes), "case-only Windows rename and rename-back preserve the profile");
            Click(s, "DeleteProfile"); Check(File.Exists(path), "delete requires the second deliberate selection");
            Click(s, "DeleteProfile"); Check(!File.Exists(path), "profile deletion removes the selected file");
            bool rejected = false; try { RideSettingsProfiles.Delete("../escape"); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "delete rejects a path outside the profiles directory");
            s.Display.Configure(s, RideDisplayMode.VR); Invoke(s.Display, "RefreshAvailability", false);
            Check(!s.Display.Ready, "XR initially unavailable"); Invoke(s.Display, "RefreshAvailability", true);
            Check(s.Display.Ready, "late XR availability recovers without restarting");
            Invoke(s, "Update"); Field(s, "<HeadTracked>k__BackingField", true); Field(s, "focus", false);
            UnityEngine.Object.DestroyImmediate(s.SetupUI);
            var vrUI = s.gameObject.AddComponent<RideSetupUI>(); Field(s, "<SetupUI>k__BackingField", vrUI); vrUI.Configure(s);
            Check(vrUI.MenuCanvas.renderMode == RenderMode.WorldSpace && vrUI.MenuCanvas.GetComponent<TrackedDeviceGraphicRaycaster>(), "VR canvas uses the SampleScene tracked-device graphic raycaster");
            Check(vrUI.ControllerUI.GetComponent<XRUIInputModule>() && vrUI.ControllerUI.Rays.Count == 2, "VR has XRI UI input module and two controller rays");
            foreach (var ray in vrUI.ControllerUI.Rays)
            {
                Check(ray.transform.IsChildOf(s.trackingOrigin), "controller ray follows the calibrated tracking origin");
                Check(ray.uiPressInput.inputActionPerformed.bindings.Any(b => b.path.EndsWith("/{TriggerButton}")), "UI Press binds the Sample trigger-button usage");
            }
            // Emulate an EventSystem click while the previous session sample still
            // has throttle down. The UI must defer until the released sample arrives.
            var sample = s.CurrentInput; sample.throttle = 1;
            Field(s, "<CurrentInput>k__BackingField", sample);
            Button(s, "Start").onClick.Invoke();
            Check(!s.Running, "Start waits for Session.Update instead of using the previous trigger-down sample");
            Invoke(vrUI, "LateUpdate");
            Check(!s.Running, "partially held throttle keeps Start pending until fully released");
            sample.throttle = 0; Field(s, "<CurrentInput>k__BackingField", sample);
            Invoke(vrUI, "LateUpdate");
            Check(s.Running, "released-trigger sample starts VR without desktop-window focus: " + s.Status);
            Check(vrUI.ControllerUI.Rays.All(r => !r.gameObject.activeSelf), "UI rays turn off during riding");
            s.Pause("done");
            InputSystem.RemoveDevice(keyboard);
            Report("recovery-results.txt"); Debug.Log("RECOVERY MENU CHECKS PASSED");
        }
        catch (Exception e) { Report("recovery-results.txt", e); EditorApplication.Exit(1); }
    }
    public static void RunPlay()
    {
        // Null graphics device: test logic, not render-texture allocation.
        UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = null;
        QualitySettings.renderPipeline = null;
        SessionState.SetBool(Active, true);
        EditorSceneManager.OpenScene(FirstRideSceneBuilder.ScenePath);
        FirstRideLaunch.Prepare(RideDisplayMode.Desktop);
        EditorApplication.isPlaying = true;
    }
    static void Begin()
    {
        results.Clear(); phase = 0; began = since = EditorApplication.timeSinceStartup;
        Application.runInBackground = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        EditorApplication.update += Tick;
    }
    static void Next() { phase++; since = EditorApplication.timeSinceStartup; }
    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - since;
            // Real tracked controllers keep publishing poses. A tiny virtual movement
            // also makes XRI recompute hover after the dynamically built canvas layout.
            if (xrController != null)
            {
                InputSystem.QueueDeltaStateEvent((InputControl<Vector3>)xrController["devicePosition"],
                    vrPointerPosition + Vector3.right * (.0001f * Mathf.Sin(Time.realtimeSinceStartup * 10)));
                InputSystem.QueueDeltaStateEvent((InputControl<int>)xrController["trackingState"], 3);
            }
            if (EditorApplication.timeSinceStartup - began > 35) throw new TimeoutException("Play recovery timed out");
            if (phase == 0)
            {
                session = UnityEngine.Object.FindFirstObjectByType<RideTestSession>();
                if (!session || !session.Settings) return;
                Check(session.Display.Mode == RideDisplayMode.Desktop && session.Display.Ready, "Desktop launches in actual Play Mode with XR off");
                keyboard = InputSystem.AddDevice<Keyboard>("RecoveryPlayKeyboard");
                mouse = InputSystem.AddDevice<Mouse>("RecoveryPlayMouse");
                session.Settings.inputSource = RideInputSource.Keyboard; Field(session, "focus", true);
                session.Settings.mirrorsEnabled = false;
                foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) camera.enabled = false;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); Next();
            }
            else if (phase == 1 && elapsed > .5)
            {
                Check(session.SetupUI.IsOpen, "Play starts with the menu visible");
                Canvas.ForceUpdateCanvases();
                pointer = RectTransformUtility.WorldToScreenPoint(null, Button(session, "Start").transform.position);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer }.WithButton(MouseButton.Left)); Next();
            }
            else if (phase == 2 && elapsed > .15)
            {
                InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer }); Next();
            }
            else if (phase == 3 && elapsed > .15)
            {
                Check(session.Running && session.Calibrated && !session.SetupUI.IsOpen,
                    "virtual mouse through EventSystem starts and auto-centers without external setup: " + session.Status);
                start = session.Model.position;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); Next();
            }
            else if (phase == 4 && elapsed > 1.8)
            {
                Check(session.Running && session.Model.speed > 1 && Vector3.Distance(start, session.Model.position) > .5f,
                    "actual Update/FixedUpdate accelerates and moves the vehicle after menu Start: " + session.Status + $" speed={session.Model.speed} throttle={session.CurrentInput.throttle}");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); Next();
            }
            else if (phase == 5 && elapsed > .4)
            {
                Check(!session.Running && session.Model.speed == 0 && session.SetupUI.IsOpen, "STOP clears speed and reopens the in-game menu");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); Next();
            }
            else if (phase == 6 && elapsed > .3)
            {
                Click(session, "Start"); Check(session.Running, "releasing STOP allows restarting from the same menu");
                session.Pause("VR UI check");
                UnityEngine.Object.DestroyImmediate(session.SetupUI);
                session.Display.Configure(session, RideDisplayMode.VR);
                var vrUI = session.gameObject.AddComponent<RideSetupUI>();
                Field(session, "<SetupUI>k__BackingField", vrUI); vrUI.Configure(session);
                Click(session, "Tab3"); vrProfilesBefore = RideSettingsProfiles.Names();
                InputSystem.RegisterLayout(@"{
                    ""name"":""RideUITestController"", ""extend"":""XRController"",
                    ""controls"":[
                        {""name"":""triggerPressed"",""layout"":""Button"",""format"":""FLT"",""usages"":[""TriggerButton""]},
                        {""name"":""trigger"",""layout"":""Axis"",""format"":""FLT"",""usages"":[""Trigger""]}
                    ]}");
                xrController = InputSystem.AddDevice("RideUITestController");
                InputSystem.SetDeviceUsage(xrController, UnityEngine.InputSystem.CommonUsages.RightHand);
                var target = Button(session, "NewProfile").transform.position;
                var origin = session.headCamera.transform.position;
                var tracking = session.trackingOrigin;
                vrPointerPosition = tracking.InverseTransformPoint(origin);
                InputSystem.QueueDeltaStateEvent((InputControl<Vector3>)xrController["devicePosition"], vrPointerPosition);
                InputSystem.QueueDeltaStateEvent((InputControl<Quaternion>)xrController["deviceRotation"], Quaternion.Inverse(tracking.rotation) * Quaternion.LookRotation(target - origin));
                InputSystem.QueueDeltaStateEvent((InputControl<int>)xrController["trackingState"], 3);
                Next();
            }
            else if (phase == 7 && elapsed > .5)
            {
                Canvas.ForceUpdateCanvases();
                var ray = session.SetupUI.ControllerUI.Rays[1];
                var target = Button(session, "NewProfile");
                var driver = ray.GetComponent<TrackedPoseDriver>();
                results.Add($"DIAG ray={ray.transform.position} dir={ray.transform.forward} target={target.transform.position} depth={target.image.depth} poseAction={driver.positionInput.action.enabled} pose={driver.positionInput.action.ReadValue<Vector3>()} tracking={xrController["trackingState"].ReadValueAsObject()} module={UnityEngine.EventSystems.EventSystem.current?.currentInputModule}");
                bool hasModel = ray.TryGetUIModel(out var model);
                results.Add($"DIAG registered={hasModel} modelmask={model.raycastLayerMask.value} points={model.raycastPoints.Count} hit={model.currentRaycast.gameObject} cull={target.image.canvasRenderer.cull} camera={session.SetupUI.MenuCanvas.worldCamera}");
                var data = new TrackedDeviceEventData(UnityEngine.EventSystems.EventSystem.current)
                { rayPoints = new List<Vector3> { ray.transform.position, ray.transform.position + ray.transform.forward * 5 }, layerMask = 1 << 2 };
                var directHits = new List<UnityEngine.EventSystems.RaycastResult>();
                session.SetupUI.MenuCanvas.GetComponent<TrackedDeviceGraphicRaycaster>().Raycast(data, directHits);
                results.Add("DIAG direct=" + string.Join(",", directHits.Select(h => h.gameObject.name)));
                Check(ray.TryGetCurrentUIRaycastResult(out var hit) && hit.gameObject == Button(session, "NewProfile").gameObject,
                    "tracked controller pose hits the profile button through XRI raycasting");
                InputSystem.QueueDeltaStateEvent((InputControl<float>)xrController["triggerPressed"], 1f);
                InputSystem.QueueDeltaStateEvent((InputControl<float>)xrController["trigger"], 1f); Next();
            }
            else if (phase == 8 && elapsed > .3)
            {
                Check(RideSettingsProfiles.Names().Length == vrProfilesBefore.Length, "holding the controller trigger does not click or repeat");
                InputSystem.QueueDeltaStateEvent((InputControl<float>)xrController["triggerPressed"], 0f);
                InputSystem.QueueDeltaStateEvent((InputControl<float>)xrController["trigger"], 0f); Next();
            }
            else if (phase == 9 && elapsed > .4)
            {
                var created = RideSettingsProfiles.Names().Except(vrProfilesBefore).ToArray();
                Check(created.Length == 1, "controller trigger release clicks through XRUIInputModule and creates exactly one profile");
                RideSettingsProfiles.Delete(created.Single()); Finish(null);
            }
        }
        catch (Exception e) { Finish(e); }
    }
    static void Finish(Exception error)
    {
        EditorApplication.update -= Tick; SessionState.SetBool(Active, false);
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        if (xrController != null && xrController.added) InputSystem.RemoveDevice(xrController);
        Report("recovery-play-results.txt", error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
