using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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
            if (Mode != RideDisplayMode.VR || !manager || !manager.activeLoader) return;
            SubsystemManager.GetSubsystems(displays);
            RefreshAvailability(displays.Exists(d => d.running));
        }

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
            session.headCamera.transform.localRotation = Quaternion.Euler(session.Settings.desktopLookPitch, session.Settings.desktopLookYaw, 0);
        }
        void OnDestroy()
        {
            if (ownsLoader && manager && manager.activeLoader) manager.DeinitializeLoader();
        }
    }
}
