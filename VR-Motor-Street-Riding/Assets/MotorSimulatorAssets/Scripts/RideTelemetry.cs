using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.XR;

namespace AIT.Riding
{
    public sealed class RideTelemetry : MonoBehaviour
    {
        public RideTestSession session;
        public string OutputDirectory { get; private set; }
        public string Error { get; private set; }
        StreamWriter samples, events, frames;
        double started, nextSample, nextFlush;
        int frameCount;
        float worstFrame;
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        static double Clock => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

        [Serializable] sealed class Metadata
        {
            public string schema = "ait-first-ride-v2", startedUtc, unity, platform, gpu, inputDevices, headset;
            public string speedNote = "speed_mps is signed; negative means walking reverse; reverse_input is appended to telemetry.csv";
            public string displayMode;
            public string timingNote = "CPU frame interval; not end-to-end HMD latency or streaming latency";
            public string vehicleModel = "Planar kinematic approximation; no balance, tire slip or torque steering";
            public float fixedStepSeconds, xrRefreshRate;
            public string settingsJson;
            public RideTestStage[] stages;
            public Vector3[] streetSegmentEndpoints;
            public string streamingWorldJson;
            public string cityRoadNetworkJson;
        }
        [Serializable] sealed class SettingsSnapshot
        {
            public string settingsJson, inputDevices;
            public Vector3 calibrationPosition;
            public Quaternion calibrationRotation;
        }

        void Start()
        {
            try
            {
                string root;
#if UNITY_EDITOR
                root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Personal", "RideTests"));
#else
                root = Path.Combine(Application.persistentDataPath, "RideTests");
#endif
                OutputDirectory = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", Invariant)
                    + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                Directory.CreateDirectory(OutputDirectory);
                started = Clock;
                nextSample = started; nextFlush = started + 1;
                samples = Open("telemetry.csv"); events = Open("events.csv");
                samples.WriteLine("t_s,frame,stage,running,speed_mps,acceleration_mps2,steering_input,throttle,brake,steering_deg,yaw_rate_rads,lateral_accel_mps2,x_m,y_m,z_m,yaw_deg,body_lean_deg,camera_lean_deg,head_x_m,head_y_m,head_z_m,head_qx,head_qy,head_qz,head_qw,lateral_from_centre_m,course_progress_m,input_connected,head_tracked,input_sample_age_s,speed_limit_kph,reverse_input");
                events.WriteLine("t_s,event,detail");
                if (session.Settings.recordFrameTimes)
                { frames = Open("frames.csv"); frames.WriteLine("t_s,frame,cpu_frame_interval_ms"); }
                var hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
                float refresh = 0;
                var displays = new System.Collections.Generic.List<XRDisplaySubsystem>();
                SubsystemManager.GetSubsystems(displays);
                foreach (var display in displays) if (display.running) display.TryGetDisplayRefreshRate(out refresh);
                var meta = new Metadata { startedUtc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
                    platform = Application.platform.ToString(), gpu = SystemInfo.graphicsDeviceName,
                    displayMode = session.Display.Mode.ToString(),
                    inputDevices = session.input.DeviceDescription, headset=hmd.name, fixedStepSeconds = Time.fixedDeltaTime,
                    xrRefreshRate = refresh, settingsJson = JsonUtility.ToJson(session.Settings), stages = session.course.stages,
                    streetSegmentEndpoints = session.course.streetSegmentEndpoints,
                    streamingWorldJson = session.worldStreamer ? JsonUtility.ToJson(session.worldStreamer.world) : null,
                    cityRoadNetworkJson = session.roadNetwork ? JsonUtility.ToJson(session.roadNetwork) : null };
                File.WriteAllText(Path.Combine(OutputDirectory, "session.json"), JsonUtility.ToJson(meta, true));
                session.SessionEvent += OnEvent;
                OnEvent("session_open", "Initial metadata in session.json; later settings snapshots in events.csv");
                Debug.Log($"Ride test recordings: {OutputDirectory}", this);
            }
            catch (Exception e) { Fail(e); }
        }

        StreamWriter Open(string name) => new StreamWriter(Path.Combine(OutputDirectory, name), false, new UTF8Encoding(false), 65536);
        static string F(float v) => v.ToString("0.######", Invariant);
        static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

        void LateUpdate()
        {
            if (samples == null) return;
            try
            {
                double now = Clock;
                string time = (now - started).ToString("0.000000", Invariant);
                float interval = Time.unscaledDeltaTime * 1000;
                frames?.WriteLine($"{time},{Time.frameCount},{F(interval)}");
                ++frameCount; worstFrame = Mathf.Max(worstFrame, interval);
                if (now >= nextSample)
                {
                    nextSample = now + 1d / Mathf.Max(1, session.Settings.telemetryHz);
                    var m = session.Model; var i = session.CurrentInput;
                    Vector3 p = session.vehicleRoot.InverseTransformPoint(session.headCamera.transform.position);
                    Quaternion q = Quaternion.Inverse(session.vehicleRoot.rotation) * session.headCamera.transform.rotation;
                    samples.WriteLine(string.Join(",", time, Time.frameCount, session.StageIndex, session.Running ? 1 : 0,
                        F(m.speed), F(m.acceleration), F(i.steering), F(i.throttle), F(i.brake), F(m.steeringDegrees),
                        F(m.yawRate), F(m.lateralAcceleration), F(m.position.x), F(m.position.y), F(m.position.z), F(m.yaw),
                        F(session.BodyLean), F(session.CameraLean), F(p.x), F(p.y), F(p.z), F(q.x), F(q.y), F(q.z), F(q.w),
                        F(session.LateralOffset), F(session.CourseProgress), i.connected ? 1 : 0, session.HeadTracked ? 1 : 0,
                        F(Time.realtimeSinceStartup-i.sampleTime), F(session.StageSpeedCapKph), F(i.reverse)));
                }
                if (now >= nextFlush)
                { samples.Flush(); events.Flush(); frames?.Flush(); nextFlush = now + 1; }
            }
            catch (Exception e) { Fail(e); }
        }

        void OnEvent(string name, string detail)
        {
            if (events == null) return;
            try
            {
                events.WriteLine($"{(Clock - started).ToString("0.000000", Invariant)},{Csv(name)},{Csv(detail)}");
                if (name == "start" || name == "calibrate" || name == "comfort" || name == "settings")
                {
                    string snapshot = JsonUtility.ToJson(new SettingsSnapshot { settingsJson=JsonUtility.ToJson(session.Settings),
                        calibrationPosition=session.CalibrationOffset, calibrationRotation=session.CalibrationRotation,
                        inputDevices=session.input.DeviceDescription });
                    events.WriteLine($"{(Clock - started).ToString("0.000000", Invariant)},settings_snapshot,{Csv(snapshot)}");
                }
                events.Flush();
            }
            catch (Exception e) { Fail(e); }
        }

        public void RecordDiscomfort(int score)
        {
            // Custom 0-10 pilot rating, deliberately not labelled SSQ or FMS.
            OnEvent("discomfort_0_to_10", Mathf.Clamp(score, 0, 10).ToString(Invariant));
        }
        void Fail(Exception e)
        {
            Error = e.Message; Debug.LogError("Ride telemetry stopped: " + e.Message, this);
            Close();
        }
        void Close()
        {
            samples?.Dispose(); samples = null;
            events?.Dispose(); events = null;
            frames?.Dispose(); frames = null;
        }
        void OnDestroy()
        {
            if (session) session.SessionEvent -= OnEvent;
            OnEvent("session_close", $"frames={frameCount};worst_cpu_frame_ms={F(worstFrame)}");
            Close();
        }
    }
}
