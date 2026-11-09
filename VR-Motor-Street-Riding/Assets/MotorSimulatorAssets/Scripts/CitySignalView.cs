using UnityEngine;

namespace AIT.Riding
{
    public sealed class CitySignalView : MonoBehaviour
    {
        public int node;
        public bool northSouth;
        public Renderer lamps;
        CityRoadRuntime runtime;
        MaterialPropertyBlock properties;
        int previous = -1;
        void OnEnable() { runtime = FindFirstObjectByType<CityRoadRuntime>(); }
        void Update()
        {
            if (!runtime || !lamps || node < 0 || node >= runtime.junctions.Length) return;
            var colour = runtime.junctions[node].ReadSignal(northSouth);
            if (previous == (int)colour) return;
            previous = (int)colour; properties ??= new MaterialPropertyBlock();
            properties.SetFloat("_Phase", previous); lamps.SetPropertyBlock(properties);
        }
    }
}
