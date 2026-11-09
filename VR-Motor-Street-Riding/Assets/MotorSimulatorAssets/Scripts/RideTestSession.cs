using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace AIT.Riding
{
    [DefaultExecutionOrder(-50)]
    public sealed class RideTestSession : MonoBehaviour
    {
        public RideTestSettings settingsTemplate;
        public RideInput input;
        public RideTestCourse course;
        public Transform vehicleRoot, vehicleVisual, riderLean, trackingOrigin;
        public Camera headCamera;
        public RideTelemetry telemetry;
        public RideWorldStreamer worldStreamer;
        public CityRoadNetwork roadNetwork;
        public RideTestSettings Settings { get; private set; }
        public RideDisplay Display { get; private set; }
        public RideSetupUI SetupUI { get; private set; }
        public bool Inspecting => GetComponent<RidePresentation>().Inspecting;
        public float StageSpeedCapKph => Settings.maximumSpeedKph;
        public PlanarScooterModel Model { get; } = new PlanarScooterModel();
        public RideInputFrame CurrentInput { get; private set; }
        public bool Running { get; private set; }
        public bool Calibrated { get; private set; }
        public int StageIndex { get; private set; }
        public string Status { get; private set; } = "Open the ride menu to start";
        public float LateralOffset { get; private set; }
        public float CourseProgress { get; private set; }
        public float BodyLean { get; private set; }
        public float CameraLean { get; private set; }
        public bool HeadTracked { get; private set; }
        public string StageName => course.stages[StageIndex].name;
        public event Action<string, string> SessionEvent;
        public Vector3 CalibrationOffset => trackingOrigin.localPosition;
        public Quaternion CalibrationRotation => trackingOrigin.localRotation;
        Vector3 previousPosition;
        float previousYaw, inputSampleTime, oldFixedStep;
        bool throttleReleased, focus = true, waitingForWorld;
        RideInputSource previousSource;

        void Awake()
        {
            Settings = Instantiate(settingsTemplate); // runtime controls never dirty the shared asset
            input.settings = Settings;
            Display = gameObject.AddComponent<RideDisplay>();
            Display.Configure(this, RideLaunchRequest.Mode);
            SetupUI = gameObject.AddComponent<RideSetupUI>();
            SetupUI.Configure(this);
            previousSource = Settings.inputSource;
            oldFixedStep = Time.fixedDeltaTime;
            Time.fixedDeltaTime = 1f / 90;
            ResetToStage(0);
        }

        void Update()
        {
            CurrentInput = input.ReadInput();
            if(waitingForWorld&&worldStreamer&&worldStreamer.ReadyToStart(Model.position))
            {waitingForWorld=false;Status="附近街區已就緒；請放開油門與倒退鍵，再選開始。";}
            inputSampleTime = CurrentInput.sampleTime;
            var hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            HeadTracked = hmd.isValid && hmd.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
            bool presenceOk = !hmd.TryGetFeatureValue(CommonUsages.userPresence, out bool present) || present;
            if (Settings.inputSource != previousSource)
            {
                Pause("Input source changed");
                throttleReleased = false;
                previousSource = Settings.inputSource;
                Calibrated = false;
                Emit("input_source", Settings.inputSource.ToString());
            }
            if (CurrentInput.connected && CurrentInput.throttle < 0.05f && CurrentInput.reverse < .05f) throttleReleased = true;
            if (Running && (!CurrentInput.connected || (Display.Mode == RideDisplayMode.Desktop && !focus)))
                Pause("Input disconnected or desktop application lost focus");
            if (Running && Display.Mode == RideDisplayMode.VR && (!HeadTracked || !presenceOk))
                Pause("Headset tracking lost or headset removed");
            if (CurrentInput.stopHeld) Pause("Emergency stop");
            else if (CurrentInput.pausePressed)
            {
                if (Running) Pause("Rider paused");
                else if (Display.Mode == RideDisplayMode.VR) SetupUI.Open();
                else TryStart();
            }
            course.Measure(StageIndex, Model.position, out float offset, out float progress);
            LateralOffset = offset; CourseProgress = progress;
            if (Running && !course.stages[StageIndex].freeRoam && Mathf.Abs(offset) > course.stages[StageIndex].halfWidth + 4)
                Pause("Left the test corridor; pause and reset");
            var stage = course.stages[StageIndex];
            if (Running && stage.stopAtEnd && stage.centreline.Length > 1)
            {
                Vector3 end = stage.centreline[stage.centreline.Length - 1];
                Vector3 tangent = (end - stage.centreline[stage.centreline.Length - 2]).normalized;
                if (Vector3.Dot(Model.position - end, tangent) >= -2) Pause("Section completed");
            }
            if (Running && (worldStreamer ? !worldStreamer.Contains(Model.position) :
                Mathf.Abs(Model.position.x) > 500 || Mathf.Abs(Model.position.z) > 500))
                Pause("Test area boundary");
        }

        void FixedUpdate()
        {
            previousPosition = Model.position; previousYaw = Model.yaw;
            if (!Running) return;
            if (Time.realtimeSinceStartup - inputSampleTime > Settings.inputTimeoutSeconds ||
                inputSampleTime > Time.realtimeSinceStartup + 0.05f)
            { Pause("Input sample timed out"); return; }
            Vector3 before = Model.position;
            Model.Step(CurrentInput, Settings, StageSpeedCapKph, Time.fixedDeltaTime);
            if(worldStreamer&&!worldStreamer.TravelReady(before,Model.position))
            {
                Model.position=before;Model.yaw=previousYaw;
                Pause("前方街區尚未準備好；請等待後再選開始。");
                Emit("streaming_wait","Movement held at the last ready position");return;
            }
            Vector3 delta = Model.position - before;
            if (roadNetwork && !roadNetwork.IsDrivable(Model.position, .3f))
            {
                Model.position = before; Model.yaw = previousYaw;
                Pause("已碰到路緣；可重新開始後低速倒退移車。");
                Emit("contact", "City road boundary; planar curb contact, not crash dynamics"); return;
            }
            if (delta.sqrMagnitude > 0 && Physics.SphereCast(before + Vector3.up * 0.65f, 0.3f,
                delta.normalized, out _, delta.magnitude + 0.03f, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            {
                Model.position = before;
                Pause("Contact with test-area obstacle");
                Emit("contact", "Sphere proxy; not a crash dynamics measurement");
            }
        }

        void LateUpdate()
        {
            float t = Running ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime) : 1;
            vehicleRoot.SetPositionAndRotation(Vector3.Lerp(previousPosition, Model.position, t),
                Quaternion.Euler(0, Mathf.LerpAngle(previousYaw, Model.yaw, t), 0));
            float requestedLean = -Mathf.Atan2(Model.lateralAcceleration, 9.81f) * Mathf.Rad2Deg;
            float targetBody = Settings.comfortMode == RideComfortMode.UprightVehicle ? 0 :
                Mathf.Clamp(requestedLean, -Settings.maximumBodyLeanDegrees, Settings.maximumBodyLeanDegrees);
            BodyLean = Mathf.Lerp(BodyLean, targetBody, 1 - Mathf.Exp(-6 * Time.deltaTime));
            CameraLean = Settings.comfortMode == RideComfortMode.ReducedCameraLean ?
                Mathf.Clamp(BodyLean * Settings.cameraLeanGain, -Settings.maximumCameraLeanDegrees,
                    Settings.maximumCameraLeanDegrees) : 0;
            vehicleVisual.localRotation = Quaternion.Euler(0, 0, BodyLean);
            // Roll about the calibrated eye, avoiding a sideways orbit of the head.
            Vector3 pivot = Settings.seatedEyePosition;
            riderLean.localRotation = Quaternion.Euler(0, 0, CameraLean);
            riderLean.localPosition = pivot - riderLean.localRotation * pivot;
        }

        public bool TryStart()
        {
            if (Running) return true;
            if (Inspecting) return RejectStart("Leave exterior inspection before riding");
            if (!Display.Ready) return RejectStart(Display.Status);
            if(worldStreamer&&!worldStreamer.ReadyToStart(Model.position))
            {waitingForWorld=true;worldStreamer.RetryFailed();return RejectStart(worldStreamer.PreparationStatus);}
            if (Display.Mode == RideDisplayMode.Desktop && !focus) return RejectStart("Click the Game view to give desktop input focus");
            if (!CurrentInput.connected) return RejectStart("Selected input is not connected: " + Settings.inputSource);
            if (CurrentInput.stopHeld) return RejectStart("Release the STOP button (left Grip / L1 / Space)");
            if (Time.realtimeSinceStartup - inputSampleTime > Settings.inputTimeoutSeconds || inputSampleTime > Time.realtimeSinceStartup + 0.05f)
                return RejectStart("Waiting for a fresh input sample");
            if (!throttleReleased || CurrentInput.throttle > 0.05f || CurrentInput.reverse > .05f)
                return RejectStart("Release the throttle and reverse control before starting");
            if (Display.Mode == RideDisplayMode.VR && !HeadTracked)
                return RejectStart("Waiting for headset tracking");
            if (!Calibrated && !Calibrate()) return RejectStart(Status);
            SetupUI.Close();
            Running = true; Status = "Riding";
            Emit("start", StageName);
            return true;
        }

        bool RejectStart(string reason)
        {
            Status = reason;
            Emit("start_rejected", reason);
            return false;
        }

        public void Pause(string reason)
        {
            bool wasRunning = Running;
            Running = false; Model.Stop();
            previousPosition = Model.position; previousYaw = Model.yaw;
            throttleReleased = false;
            Status = reason;
            if (wasRunning)
            {
                Emit("pause", reason);
                if (SetupUI) SetupUI.Open();
            }
        }

        public void ResetToStage(int index)
        {
            if (Running || course.stages == null || course.stages.Length == 0) return;
            StageIndex = Mathf.Clamp(index, 0, course.stages.Length - 1);
            var stage = course.stages[StageIndex];
            Model.Reset(stage.spawnPosition, stage.spawnYaw);
            previousPosition = Model.position; previousYaw = Model.yaw;
            BodyLean = CameraLean = 0;
            vehicleRoot.SetPositionAndRotation(Model.position, Quaternion.Euler(0, Model.yaw, 0));
            Status = $"{stage.name}: paused; calibrate if needed, then start";
            waitingForWorld=worldStreamer&&!worldStreamer.ReadyToStart(Model.position);
            throttleReleased = false;
            Emit("stage_reset", stage.name);
        }

        public bool Calibrate()
        {
            if (Running) return false;
            if (Inspecting) { Status = "Leave exterior inspection before calibrating"; return false; }
            if (!Display.Ready) { Status = Display.Status; return false; }
            if (Display.Mode == RideDisplayMode.VR && !HeadTracked)
            { Status = "Cannot calibrate without headset tracking"; return false; }
            riderLean.localRotation = Quaternion.identity; riderLean.localPosition = Vector3.zero;
            BodyLean = CameraLean = 0; vehicleVisual.localRotation = Quaternion.identity;
            if (Display.Mode == RideDisplayMode.Desktop) Display.ResetDesktopView();
            else
            {
                trackingOrigin.localRotation = Quaternion.Euler(0, -headCamera.transform.localEulerAngles.y, 0);
                trackingOrigin.localPosition = Settings.seatedEyePosition -
                    trackingOrigin.localRotation * headCamera.transform.localPosition;
            }
            Calibrated = true;
            Status = "Seated view calibrated; release throttle, then start";
            Emit("calibrate", $"{trackingOrigin.localPosition} / {trackingOrigin.localEulerAngles}");
            return true;
        }

        public void ApplySettings(RideTestSettings settings)
        {
            if (Running) throw new InvalidOperationException("Pause before changing settings.");
            RideSettingsProfiles.Validate(settings);
            if (settings.recordFrameTimes != Settings.recordFrameTimes)
                throw new InvalidOperationException("Change frame-file recording before Play; it cannot be toggled mid-session.");
            GetComponent<RidePresentation>().StopInspection();
            bool recenter = Settings.seatedEyePosition != settings.seatedEyePosition || Settings.inputSource != settings.inputSource;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(settings), Settings);
            if (Display.Mode == RideDisplayMode.Desktop)
            {
                if (Settings.inputSource == RideInputSource.ViveControllers) Settings.inputSource = RideInputSource.Keyboard;
                Display.ResetDesktopView();
            }
            if (recenter) Calibrated = false;
            Status = recenter ? "Settings loaded; view will center on start" : "Settings applied";
            Emit("settings", "Applied settings from in-game menu");
        }

        public void CycleComfort()
        {
            if (Running) return;
            Settings.comfortMode = (RideComfortMode)(((int)Settings.comfortMode + 1) % 3);
            Emit("comfort", Settings.comfortMode.ToString());
        }

        public void Emit(string name, string detail) => SessionEvent?.Invoke(name, detail);
        void OnApplicationFocus(bool hasFocus)
        {
            focus = hasFocus;
            if (!hasFocus && Settings && Display && Display.Mode == RideDisplayMode.Desktop) Pause("Desktop application lost focus");
        }
        void OnApplicationPause(bool paused) { if (paused && Settings) Pause("Application suspended"); }
        void OnDestroy()
        {
            Time.fixedDeltaTime = oldFixedStep;
            if (Settings) Destroy(Settings);
        }
    }
}
