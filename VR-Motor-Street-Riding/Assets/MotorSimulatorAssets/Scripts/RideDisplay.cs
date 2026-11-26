using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.Rendering.Universal;

namespace AIT.Riding
{
    // Filled by the editor launch hook before scene Awake; normal Players retain VR.
    public static class RideLaunchRequest
    {
        public static RideDisplayMode Mode = RideDisplayMode.VR;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Mode = RideDisplayMode.VR; }
    }

    [DefaultExecutionOrder(-100)]
    public sealed class RideDisplay : MonoBehaviour
    {
        public RideDisplayMode Mode { get; private set; }
        public bool Ready { get; private set; }
        public string Status { get; private set; }
        RideTestSession session;
        XRManagerSettings manager;
        bool ownsLoader;
        const float MouseLookDegreesPerPixel = .12f;
        float desktopPitch, desktopYaw;
        bool desktopPointerCaptured, desktopHasFocus = true;
        readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

        public void Configure(RideTestSession owner, RideDisplayMode mode)
        {
            session = owner; Mode = mode;
            manager = XRGeneralSettings.Instance ? XRGeneralSettings.Instance.Manager : null;
            if (mode == RideDisplayMode.Desktop)
            {
                if (manager && manager.activeLoader) manager.StopSubsystems();
                var driver = session.headCamera.GetComponent<TrackedPoseDriver>();
                if (driver) driver.enabled = false;
                session.headCamera.GetUniversalAdditionalCameraData().allowXRRendering = false;
                session.headCamera.ResetProjectionMatrix(); session.headCamera.ResetWorldToCameraMatrix();
                session.headCamera.fieldOfView = 85;
                ResetDesktopView();
                ReleaseDesktopPointer();
                if (session.Settings.inputSource == RideInputSource.ViveControllers)
                    session.Settings.inputSource = RideInputSource.Keyboard;
                Ready = true; Status = "Desktop (XR off)";
            }
            else Status = "Initializing VR; wait for headset tracking";
        }

        IEnumerator Start()
        {
            if (Mode == RideDisplayMode.Desktop) yield break;
            if (!manager) { Status = "XR manager is not configured. Stop Play and choose Desktop."; yield break; }
            var driver = session.headCamera.GetComponent<TrackedPoseDriver>();
            if (driver) driver.enabled = true;
            session.headCamera.GetUniversalAdditionalCameraData().allowXRRendering = true;
            ownsLoader = manager.activeLoader == null;
            if (ownsLoader) yield return manager.InitializeLoader();
            if (!manager.activeLoader) { Status = "VR initialization failed. Stop Play and choose Desktop."; yield break; }
            manager.StartSubsystems();
            SubsystemManager.GetSubsystems(displays);
            RefreshAvailability(displays.Exists(d => d.running));
            session.Emit("display_mode", Status);
        }

        void Update()
        {
            if (Mode == RideDisplayMode.Desktop) { UpdateDesktopLook(); return; }
            if (Mode != RideDisplayMode.VR || !manager || !manager.activeLoader) return;
            SubsystemManager.GetSubsystems(displays);
            RefreshAvailability(displays.Exists(d => d.running));
        }

        void UpdateDesktopLook()
        {
            var keyboard = Keyboard.current;
            if (session.Running && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                session.Pause("Desktop view released");
            if (!desktopHasFocus || !session.Running || session.Inspecting || session.SetupUI.IsOpen || Mouse.current == null)
            { ReleaseDesktopPointer(); return; }
            // The Editor can release a locked cursor itself (for example with Esc).
            // Pause rather than immediately taking the pointer back from the user.
            if (desktopPointerCaptured && Cursor.lockState != CursorLockMode.Locked)
            { session.Pause("Desktop pointer released"); ReleaseDesktopPointer(); return; }
            if (!desktopPointerCaptured)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                desktopPointerCaptured = true;
                return; // Ignore the capture/click frame so entering the view cannot turn the head.
            }
            if (keyboard != null && keyboard.cKey.wasPressedThisFrame)
            { ResetDesktopView(); return; }
            // Mouse delta is already motion per input update; do not multiply by frame time.
            Vector2 delta = Mouse.current.delta.ReadValue() * MouseLookDegreesPerPixel;
            desktopYaw = Mathf.Repeat(desktopYaw + delta.x + 180, 360) - 180;
            desktopPitch = Mathf.Clamp(desktopPitch - delta.y, -80, 80);
            session.headCamera.transform.localRotation = Quaternion.Euler(desktopPitch, desktopYaw, 0);
        }

        public void ReleaseDesktopPointer()
        {
            if (Mode != RideDisplayMode.Desktop) return;
            desktopPointerCaptured = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void OnApplicationFocus(bool focused)
        {
            desktopHasFocus = focused;
            if (!focused) ReleaseDesktopPointer();
        }
        void OnApplicationPause(bool paused) { if (paused) ReleaseDesktopPointer(); }
        void OnDisable() { ReleaseDesktopPointer(); }

        void RefreshAvailability(bool running)
        {
            // Starting XR is asynchronous: an early false must never latch forever.
            Ready = running;
            Status = running ? "VR connected" : "Waiting for XR display to become active";
        }

        public void ResetDesktopView()
        {
            if (Mode != RideDisplayMode.Desktop) return;
            session.trackingOrigin.localPosition = Vector3.zero;
            session.trackingOrigin.localRotation = Quaternion.identity;
            session.headCamera.transform.localPosition = session.Settings.seatedEyePosition;
            desktopPitch = session.Settings.desktopLookPitch; desktopYaw = session.Settings.desktopLookYaw;
            session.headCamera.transform.localRotation = Quaternion.Euler(desktopPitch, desktopYaw, 0);
        }
        void OnDestroy()
        {
            ReleaseDesktopPointer();
            if (ownsLoader && manager && manager.activeLoader) manager.DeinitializeLoader();
        }
    }
}
