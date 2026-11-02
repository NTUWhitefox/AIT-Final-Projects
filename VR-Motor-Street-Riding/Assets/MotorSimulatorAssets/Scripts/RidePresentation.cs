using UnityEngine;

namespace AIT.Riding
{
    [DefaultExecutionOrder(100)]
    public sealed class RidePresentation : MonoBehaviour
    {
        public RideTestSession session;
        public TextMesh speedDisplay, pausedDisplay;
        public GameObject pausedPanel;
        public Transform handlebars, frontWheel, rearWheel;
        public Renderer vignetteRenderer;
        public Shader vignetteShader;
        Material vignetteMaterial;
        float wheelAngle, nextTextUpdate;
        int rating;
        public bool Inspecting { get; private set; }
        public bool inspectSpinWheels;
        public float inspectSteering, inspectAzimuth = 40, inspectElevation = 20, inspectDistance = 3;
        Vector3 savedCameraPosition;
        Quaternion savedCameraRotation;

        void Start()
        {
            RepairVehicleAssembly();
            if (vignetteRenderer && vignetteShader)
            {
                vignetteMaterial = new Material(vignetteShader);
                vignetteRenderer.sharedMaterial = vignetteMaterial;
            }
        }

        void LateUpdate()
        {
            if (!session.Settings) return;
            if (pausedPanel) pausedPanel.SetActive(false);
            if (Time.unscaledTime >= nextTextUpdate)
            {
                nextTextUpdate = Time.unscaledTime + 0.1f;
                speedDisplay.text = $"{(session.Model.speed < -.01f ? "R " : "")}{Mathf.Abs(session.Model.speed) * 3.6f:00}\n<size=14>km/h</size>";

            }
            float steer = Inspecting ? inspectSteering : session.Model.steeringDegrees;
            if (handlebars) handlebars.localRotation = Quaternion.Euler(0, steer, 0);
            float rotationSpeed = Inspecting ? (inspectSpinWheels ? 120 : 0) : session.Model.speed / 0.245f * Mathf.Rad2Deg;
            wheelAngle = Mathf.Repeat(wheelAngle + rotationSpeed * Time.deltaTime, 360);
            if (frontWheel) frontWheel.localRotation = Quaternion.Euler(wheelAngle, 0, 0);
            if (rearWheel) rearWheel.localRotation = Quaternion.Euler(wheelAngle, 0, 0);
            if (Inspecting)
            {
                Quaternion orbit = Quaternion.Euler(inspectElevation, inspectAzimuth, 0);
                Vector3 target = session.vehicleRoot.TransformPoint(new Vector3(0, .65f, 0));
                session.headCamera.transform.SetPositionAndRotation(target + orbit * Vector3.back * inspectDistance, orbit);
            }
            bool showVignette = session.Settings.vignetteEnabled && session.Running;
            if (vignetteRenderer)
            {
                vignetteRenderer.enabled = showVignette;
                if (vignetteMaterial)
                {
                    float intensity = Mathf.Clamp01(Mathf.Abs(session.Model.speed) / 8 + Mathf.Abs(session.Model.yawRate));
                    vignetteMaterial.SetFloat("_Strength", intensity * 0.85f);
                }
            }
        }

        void OnGUI()
        {
            if (!session.Settings || session.Display.Mode != RideDisplayMode.Desktop || !session.Settings.showOperatorPanel || session.SetupUI.IsOpen) return;
            GUILayout.BeginArea(new Rect(12, 12, 340, Mathf.Min(Screen.height - 24, 310)), GUI.skin.box);
            GUILayout.Label("FIRST RIDE / operator");
            GUILayout.Label(session.Status);
            GUILayout.Label(session.Display.Status);
            GUILayout.Label($"{session.StageName} | {session.Model.speed * 3.6f:0.0} km/h");
            if (session.Running)
            {
                if (GUILayout.Button("STOP / pause")) session.Pause("Operator stopped");
            }
            else
            {
                if (GUILayout.Button("Start")) session.TryStart();
            }
            if (GUILayout.Button("Setup / calibration / profiles (F2)")) session.SetupUI.Open();
            GUILayout.Space(8);
            GUILayout.Label($"Pilot discomfort: {rating}/10 (custom rating)");
            rating = Mathf.RoundToInt(GUILayout.HorizontalSlider(rating, 0, 10));
            if (GUILayout.Button("Record rating")) session.telemetry.RecordDiscomfort(rating);
            if (session.telemetry.Error != null) GUILayout.Label("RECORDING ERROR: " + session.telemetry.Error);
            GUILayout.EndArea();
        }

        public int RepairVehicleAssembly()
        {
            Transform body = session.vehicleVisual.Find("Innerscene CC0 Urban Scooter");
            if (!body || !handlebars || !frontWheel || !rearWheel) return 0;
            int repaired = 0;
            foreach (var renderer in session.vehicleVisual.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.name.StartsWith("Scooter_", System.StringComparison.OrdinalIgnoreCase)) continue;
                Transform parent = body;
                switch (RideVehicleParts.Classify(renderer.name))
                {
                    case RidePartGroup.FrontWheel: parent = frontWheel; break;
                    case RidePartGroup.RearWheel: parent = rearWheel; break;
                    case RidePartGroup.Steering: parent = handlebars; break;
                }
                if (renderer.transform.parent == parent) continue;
                renderer.transform.SetParent(parent, true); ++repaired;
            }
            if (repaired > 0) session.Emit("assembly_repair", $"Reassigned {repaired} parts using explicit prefixes");
            return repaired;
        }

        public void StartInspection()
        {
            if (Inspecting || session.Running || session.Display.Mode != RideDisplayMode.Desktop) return;
            savedCameraPosition = session.headCamera.transform.localPosition;
            savedCameraRotation = session.headCamera.transform.localRotation;
            Inspecting = true;
            session.Emit("inspection", "Exterior view started; wheel spin is a visual test only");
        }
        public void StopInspection()
        {
            if (!Inspecting) return;
            Inspecting = false; inspectSpinWheels = false; inspectSteering = wheelAngle = 0;
            if (session.headCamera)
            {
                session.headCamera.transform.localPosition = savedCameraPosition;
                session.headCamera.transform.localRotation = savedCameraRotation;
            }
            session.Emit("inspection", "Returned to rider view");
        }
        void OnDestroy() { if (vignetteMaterial) Destroy(vignetteMaterial); }
    }
}
