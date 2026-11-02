using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace AIT.Riding
{
    // Rear-view camera display, centred on the mirror location. Monoscopic and
    // fixed-view: it is NOT a calibrated stereo planar reflection or blind-spot sensor.
    [DefaultExecutionOrder(150)]
    public sealed class RideMirror : MonoBehaviour
    {
        public RideTestSession session;
        public Camera mirrorCamera;
        public Renderer screen;
        public Shader screenShader;
        RenderTexture texture;
        Material material;
        float nextRender;

        void Start()
        {
            int size = session.Settings.mirrorResolution;
            texture = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32)
                { name = name + " rear view", antiAliasing = 1 };
            texture.Create();
            material = new Material(screenShader);
            material.SetTexture("_BaseMap", texture);
            // Conventional rear-view presentation; validate orientation on site.
            material.SetTextureScale("_BaseMap", new Vector2(-1, 1));
            material.SetTextureOffset("_BaseMap", Vector2.right);
            screen.sharedMaterial = material;
            mirrorCamera.enabled = false;
            mirrorCamera.depth = -10;
            mirrorCamera.GetUniversalAdditionalCameraData().allowXRRendering = false;
            mirrorCamera.aspect = 1.45f;
            mirrorCamera.targetTexture = texture;
        }

        void LateUpdate()
        {
            if (!texture || !session.Settings) return;
            if (texture.width != session.Settings.mirrorResolution)
            {
                mirrorCamera.targetTexture = null;
                texture.Release(); Destroy(texture);
                int size = session.Settings.mirrorResolution;
                texture = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32)
                    { name = name + " rear view", antiAliasing = 1 };
                texture.Create(); mirrorCamera.targetTexture = texture;
                material.SetTexture("_BaseMap", texture);
            }
            screen.enabled = session.Settings.mirrorsEnabled;
            // Schedule normal pipeline cameras instead of nested manual render
            // requests. This keeps the main camera/XR render context intact and
            // lets URP submit all cameras within one frame.
            mirrorCamera.enabled = session.Settings.mirrorsEnabled && Time.unscaledTime >= nextRender;
            if (!mirrorCamera.enabled) return;
            nextRender = Time.unscaledTime + 1f / session.Settings.mirrorUpdatesPerSecond;
        }

        void OnDestroy()
        {
            if (mirrorCamera) mirrorCamera.targetTexture = null;
            if (texture) { texture.Release(); Destroy(texture); }
            if (material) Destroy(material);
        }
    }
}
