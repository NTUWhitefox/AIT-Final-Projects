using UnityEngine;

namespace AIT.Riding
{
    public enum RideInputSource { ViveControllers, Gamepad, Keyboard, External }
    public enum RideComfortMode { StableHorizon, UprightVehicle, ReducedCameraLean }
    public enum RideDisplayMode { Desktop, VR }

    [CreateAssetMenu(menuName = "AIT/Ride Test Settings")]
    public sealed class RideTestSettings : ScriptableObject
    {
        [HideInInspector] public int profileVersion = 1;
        [Header("Input (explicit selection; never mixes two sources)")]
        public RideInputSource inputSource = RideInputSource.ViveControllers;
        public bool enableSetupShortcuts;
        [Range(0, 0.5f)] public float steeringDeadzone = 0.15f;
        [Min(0.05f)] public float inputTimeoutSeconds = 0.25f;

        [Header("Planar scooter approximation (not validated vehicle dynamics)")]
        [Min(0.5f)] public float wheelbase = 1.33f;
        [Min(1)] public float maximumSpeedKph = 40;
        [HideInInspector] public bool overrideSectionSpeedCaps; // legacy profile fields
        [HideInInspector] public Vector3 sectionSpeedCapsKph = new Vector3(20, 30, 40);
        [Min(0.1f)] public float acceleration = 1.8f;
        [Min(0.1f)] public float brakingDeceleration = 4;
        [Min(0)] public float coastingDeceleration = 0.25f;
        [Min(0)] public float quadraticDrag = 0.003f;
        [Range(1, 45)] public float lowSpeedSteeringDegrees = 38;
        [Range(1, 20)] public float highSpeedSteeringDegrees = 5;
        [Min(1)] public float steeringRateDegreesPerSecond = 55;
        [Min(1)] public float maximumYawDegreesPerSecond = 28;
        [Min(1)] public float lowSpeedYawDegreesPerSecond = 75;
        [Min(1)] public float steeringTransitionSpeedKph = 25;
        [Range(.5f, 6)] public float maximumReverseSpeedKph = 3;
        [Min(.1f)] public float reverseAcceleration = .8f;
        [Min(0.1f)] public float maximumLateralAcceleration = 3;

        [Header("Rider fit, in metres relative to the vehicle ground origin")]
        public Vector3 seatedEyePosition = new Vector3(0, 1.35f, -0.28f);
        [Range(-60, 60)] public float desktopLookPitch = 18;
        [Range(-180, 180)] public float desktopLookYaw;
        [Header("Visual condition; change only while paused")]
        public RideComfortMode comfortMode = RideComfortMode.StableHorizon;
        [Range(0, 30)] public float maximumBodyLeanDegrees = 18;
        [Range(0, 0.5f)] public float cameraLeanGain = 0.2f;
        [Range(0, 8)] public float maximumCameraLeanDegrees = 3;
        public bool vignetteEnabled;
        public bool mirrorsEnabled = true;
        [Range(5, 60)] public int mirrorUpdatesPerSecond = 30;
        [Range(128, 1024)] public int mirrorResolution = 256;
        public bool engineSoundEnabled = true;

        [Header("Logging (local; no network transmission)")]
        [Range(1, 90)] public int telemetryHz = 20;
        public bool recordFrameTimes = true;
        public bool showOperatorPanel = true;
    }
}
