using UnityEngine;

namespace AIT.Riding
{
    // Only appearance/collision is culled; actor identity and movement keep running.
    public sealed class CityActorVisibility : MonoBehaviour
    {
        public RideTestSession session;
        public float visibleDistance = 240;
        Renderer[] renderers;
        Collider[] colliders;
        bool[] original;
        float next;
        void Start()
        {
            renderers = GetComponentsInChildren<Renderer>(true); colliders = GetComponentsInChildren<Collider>(true);
            original = new bool[renderers.Length]; for (int i = 0; i < original.Length; i++) original[i] = renderers[i].enabled;
        }
        void Update()
        {
            if (!session || Time.unscaledTime < next) return; next = Time.unscaledTime + .15f;
            float distance = Vector3.Distance(transform.position, session.Model.position);
            bool ready = !session.worldStreamer || session.worldStreamer.TravelReady(transform.position, transform.position);
            bool visible = ready && distance < visibleDistance;
            for (int i = 0; i < renderers.Length; i++) if (renderers[i]) renderers[i].enabled = visible && original[i];
            foreach (var collider in colliders) if (collider) collider.enabled = ready && distance < 100;
        }
    }
}
