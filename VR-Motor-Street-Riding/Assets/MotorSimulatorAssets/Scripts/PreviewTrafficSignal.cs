using UnityEngine;

namespace AIT.Riding
{
    public enum PreviewSignalColour { Red, Amber, Green }

    // Appearance preview only. Timings are editable, not a surveyed Taipei plan.
    [ExecuteAlways]
    public sealed class PreviewTrafficSignal : MonoBehaviour
    {
        public Renderer signalRenderer;
        public PreviewSignalColour colour = PreviewSignalColour.Red;
        public bool cycleInPlay = true;
        public RideTestSession session;
        [Min(1)] public float redSeconds = 20, greenSeconds = 12, amberSeconds = 3;
        public bool AllowsTraffic => colour == PreviewSignalColour.Green;
        MaterialPropertyBlock properties;
        float elapsed;
        void OnEnable() => Apply();
        void OnValidate() => Apply();
        void Update()
        {
            if (!Application.isPlaying || !cycleInPlay || (session && (!session.Running || session.StageIndex != 0))) return;
            elapsed += Time.deltaTime;
            float t = Mathf.Repeat(elapsed, redSeconds + greenSeconds + amberSeconds);
            var next = t < redSeconds ? PreviewSignalColour.Red :
                t < redSeconds + greenSeconds ? PreviewSignalColour.Green : PreviewSignalColour.Amber;
            if (next == colour) return;
            colour = next; Apply();
        }
        public void Apply()
        {
            if (!signalRenderer) return;
            properties ??= new MaterialPropertyBlock();
            var colours = new[] { new Color(1, .025f, .01f), new Color(1, .55f, .01f), new Color(.01f, 1, .22f) };
            for (int i = 0; i < 3; i++)
            {
                bool on = (int)colour == i;
                properties.Clear();
                properties.SetColor("_BaseColor", colours[i] * (on ? .65f : .09f));
                properties.SetColor("_EmissionColor", on ? colours[i] * 2 : Color.black);
                signalRenderer.SetPropertyBlock(properties, i + 2);
            }
        }
    }
}
