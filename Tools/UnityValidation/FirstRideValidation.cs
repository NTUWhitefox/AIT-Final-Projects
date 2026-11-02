using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace AIT.Riding.Editor
{
    public static class FirstRideValidation
    {
        static readonly List<string> results = new List<string>();
        static string Output => Path.GetFullPath("Personal/FirstRideValidation");

        public static void RebuildAndValidate()
        {
            FirstRideSceneBuilder.Build();
            ValidateAndCapture();
        }

        public static void ValidateAndCapture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first");
            Directory.CreateDirectory(Output); results.Clear();
            RunModelChecks();
            if (!File.Exists(FirstRideSceneBuilder.ScenePath)) FirstRideSceneBuilder.Build();
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(FirstRideSceneBuilder.ScenePath);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(FirstRideSceneBuilder.ScenePath, OpenSceneMode.Additive);
            GameObject panel = null; bool panelWasActive = false;
            SceneManager.SetActiveScene(scene);
            try
            {
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                        Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0,
                            "No missing scripts: " + t.name, false);
                var session = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<RideTestSession>()).Single();
                var visualBounds = new Bounds(session.vehicleRoot.position, Vector3.zero);
                foreach (var renderer in session.vehicleVisual.GetComponentsInChildren<MeshRenderer>())
                    if (renderer.enabled && !renderer.GetComponent<TextMesh>()) visualBounds.Encapsulate(renderer.bounds);
                Check(visualBounds.size.z > 1.7f && visualBounds.size.z < 2.2f, "Vehicle length in metres: " + visualBounds.size.z);
                Check(visualBounds.size.x > 0.7f && visualBounds.size.x < 1f, "Vehicle width in metres: " + visualBounds.size.x);
                Check(visualBounds.size.y > 1.1f && visualBounds.size.y < 1.4f, "Vehicle height in metres: " + visualBounds.size.y);
                Check(session.trackingOrigin.lossyScale == Vector3.one, "Tracking origin has unit scale");
                Check(session.course.stages.Length == 3, "Three test stages");
                Check(session.GetComponentsInChildren<RideMirror>().Length == 2, "Two rear-view cameras");
                Check(Shader.Find("AIT/RideVignette") != null, "Comfort shader resolves");
                Check(!ShaderUtil.ShaderHasError(Shader.Find("AIT/RideVignette")), "Comfort shader compiles");
                var shaders = session.GetComponentsInChildren<Renderer>(true).Select(r => r.sharedMaterial)
                    .Where(m => m).Select(m => m.shader).Distinct().ToArray();
                foreach (var shader in shaders) Check(!ShaderUtil.ShaderHasError(shader), "Shader compiles: " + shader.name);
                var presentation = session.GetComponent<RidePresentation>();
                foreach (var part in session.vehicleVisual.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!part.name.StartsWith("Scooter_", StringComparison.OrdinalIgnoreCase)) continue;
                    string expected = "Innerscene CC0 Urban Scooter";
                    switch (RideVehicleParts.Classify(part.name))
                    {
                        case RidePartGroup.Steering: expected = presentation.handlebars.name; break;
                        case RidePartGroup.FrontWheel: expected = presentation.frontWheel.name; break;
                        case RidePartGroup.RearWheel: expected = presentation.rearWheel.name; break;
                    }
                    Check(part.transform.parent.name == expected, "Part parent: " + part.name, false);
                }
                Check(true, "All imported parts use the explicit body / steering / wheel classification");
                Check(presentation.handlebars.GetComponentsInChildren<MeshRenderer>().Length>20,
                    "Imported handlebar parts are attached to the steering pivot");
                Check(presentation.frontWheel.GetComponentsInChildren<MeshRenderer>().Length>2&&
                    presentation.rearWheel.GetComponentsInChildren<MeshRenderer>().Length>2,
                    "Both wheel pivots contain imported wheel geometry");
                panel = presentation.pausedPanel; panelWasActive = panel.activeSelf;
                panel.SetActive(false);
                session.course.Measure(0, session.course.stages[0].spawnPosition, out float offset, out float progress);
                Check(Mathf.Abs(offset - 1.75f) < 0.02f && progress > 5, "Right-side spawn and course projection");
                var position = session.vehicleRoot.position;
                Capture("cockpit.png", position + session.settingsTemplate.seatedEyePosition,
                    Quaternion.Euler(18, 0, 0), 85);
                Capture("scooter.png", position + new Vector3(2, 1.45f, 2.3f),
                    Quaternion.LookRotation(position + Vector3.up * 0.6f - (position + new Vector3(2, 1.45f, 2.3f))), 50);
                Capture("course.png", new Vector3(160, 170, -210),
                    Quaternion.LookRotation(new Vector3(0, 0, 30) - new Vector3(160, 170, -210)), 58);
            }
            finally
            {
                if (panel) panel.SetActive(panelWasActive);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
                File.WriteAllLines(Path.Combine(Output, "validation.txt"), results);
            }
            Debug.Log($"First Ride validation passed. Previews and report: {Output}");
        }

        static void Check(bool condition, string message, bool report = true)
        {
            if (!condition) { results.Add("FAIL " + message); throw new InvalidOperationException(message); }
            if (report) results.Add("PASS " + message);
        }

        public static void RunModelChecks()
        {
            var s = ScriptableObject.CreateInstance<RideTestSettings>();
            try
            {
                Check(!s.enableSetupShortcuts, "Calibration and setup shortcuts default to off");
                Check(RideVehicleParts.Classify("Scooter_shaped_leg_shield_with_front_wheel_clearance") == RidePartGroup.Body,
                    "Leg shield description does not classify as a rotating wheel");
                Check(RideVehicleParts.Classify("Scooter_front_curved_mudguard") == RidePartGroup.Steering,
                    "Mudguard steers but does not spin with the wheel");
                RunProfileChecks(s);
                var m = new PlanarScooterModel();
                var full = new RideInputFrame { connected = true, throttle = 1 };
                m.Reset(Vector3.zero, 0);
                for (int i = 0; i < 2700; ++i) m.Step(full, s, 20, 1f / 90);
                Check(Mathf.Abs(m.speed - 20 / 3.6f) < 0.001f, "Stage speed cap respected under sustained throttle");
                Check(Mathf.Abs(m.position.x) < 0.001f && m.position.z > 100, "Straight input does not drift sideways");
                var brake = new RideInputFrame { brake = 1, throttle = 1 };
                for (int i = 0; i < 900; ++i) m.Step(brake, s, 20, 1f / 90);
                Check(m.speed == 0, "Brake overrides throttle and stops without reversing");
                float startYaw = m.yaw;
                for (int i = 0; i < 180; ++i) m.Step(new RideInputFrame { steering = 1 }, s, 40, 1f / 90);
                Check(m.yaw == startYaw, "No steering rotation while stationary");
                float left = Turn(-1, s), right = Turn(1, s);
                Check(Mathf.Abs(left + right) < 0.01f && right > 0, "Left/right steering is symmetric and correctly signed");
                var a = Integrate(s, 90);
                var b = Integrate(s, 180);
                Check(Vector3.Distance(a.position, b.position) < 0.2f && Mathf.Abs(a.speed - b.speed) < 0.03f,
                    "90/180 Hz integration agrees within pilot tolerance");
                Check(RideInput.ApplyDeadzone(0.1f, 0.15f) == 0 &&
                    RideInput.ApplyDeadzone(1, 0.15f) == 1 && RideInput.ApplyDeadzone(-1, 0.15f) == -1,
                    "Steering deadzone removes centre noise and preserves endpoints");
                Check(RideInput.ApplyDeadzone(-0.6f, 0.15f) == -RideInput.ApplyDeadzone(0.6f, 0.15f),
                    "Deadzone is symmetric");
                Check(!RideInput.Validate(new RideInputFrame { connected=true,steering=float.NaN }).connected,
                    "Invalid sensor values fail closed");
                var valid=RideInput.Validate(new RideInputFrame {connected=true,steering=2,throttle=-1,brake=4});
                Check(valid.steering==1&&valid.throttle==0&&valid.brake==1,"External inputs are bounded");
                m.Stop();Check(m.speed == 0 && m.yawRate == 0, "Emergency stop clears motion");
            }
            finally { UnityEngine.Object.DestroyImmediate(s); }
        }

        static void RunProfileChecks(RideTestSettings settings)
        {
            string original = JsonUtility.ToJson(settings);
            var copy = RideSettingsProfiles.FromJson(original);
            try { Check(JsonUtility.ToJson(copy) == original, "Settings JSON round-trip preserves every serialized parameter"); }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
            bool rejected = false;
            try { RideSettingsProfiles.FromJson("{\"profileVersion\":2}"); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Unknown profile schema is rejected");
            rejected = false;
            try { RideSettingsProfiles.ProfilePath("../escape"); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "Profile name cannot escape its local directory");
            settings.maximumSpeedKph = float.NaN; rejected = false;
            try { RideSettingsProfiles.Validate(settings); }
            catch (InvalidDataException) { rejected = true; }
            finally { JsonUtility.FromJsonOverwrite(original, settings); }
            Check(rejected, "Non-finite profile values are rejected before applying");
            string name = "__validation_" + Guid.NewGuid().ToString("N");
            string path = RideSettingsProfiles.ProfilePath(name);
            try
            {
                RideSettingsProfiles.Save(name, settings);
                var loaded = RideSettingsProfiles.Load(name);
                try { Check(JsonUtility.ToJson(loaded) == original, "Named JSON profile saves and reloads from disk"); }
                finally { UnityEngine.Object.DestroyImmediate(loaded); }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
            }
        }

        static float Turn(float steering, RideTestSettings s)
        {
            var m = new PlanarScooterModel();m.Reset(Vector3.zero,0);
            for (int i = 0; i < 450; ++i)
            {
                m.Step(new RideInputFrame { steering=steering, throttle=1 },s,40,1f/90);
                Check(Mathf.Abs(m.lateralAcceleration) <= s.maximumLateralAcceleration+0.001f, "Lateral acceleration limit",false);
                Check(Mathf.Abs(m.yawRate) <= Mathf.Max(s.lowSpeedYawDegreesPerSecond,s.maximumYawDegreesPerSecond)*Mathf.Deg2Rad+0.001f,"Speed-dependent yaw rate limit",false);
            }
            results.Add("PASS Steering/lateral/yaw constraints throughout full-lock run");
            return Mathf.DeltaAngle(0,m.yaw);
        }
        static PlanarScooterModel Integrate(RideTestSettings s,int hz)
        {
            var m=new PlanarScooterModel();m.Reset(Vector3.zero,0);
            for(int i=0;i<hz*10;++i)m.Step(new RideInputFrame{throttle=0.6f,steering=0.2f},s,40,1f/hz);
            return m;
        }

        static void Capture(string name,Vector3 position,Quaternion rotation,float fov)
        {
            var obj=new GameObject("Editor preview camera");var cam=obj.AddComponent<Camera>();cam.enabled=false;
            cam.scene=SceneManager.GetActiveScene();
            obj.transform.SetPositionAndRotation(position,rotation);cam.fieldOfView=fov;cam.nearClipPlane=0.04f;cam.farClipPlane=600;
            cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(0.64f,0.76f,0.82f);
            cam.stereoTargetEye=StereoTargetEyeMask.None;cam.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            var target=new RenderTexture(1600,1000,24);var previous=RenderTexture.active;
            try
            {
                target.Create();
                RenderPipeline.SubmitRenderRequest(cam,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;
                var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                File.WriteAllBytes(Path.Combine(Output,name),image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
                results.Add("PASS Preview captured: "+name);
            }
            finally
            {
                RenderTexture.active=previous;target.Release();UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }
    }
}
