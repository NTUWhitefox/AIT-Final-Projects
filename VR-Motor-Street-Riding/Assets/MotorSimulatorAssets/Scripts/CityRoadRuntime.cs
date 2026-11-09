using UnityEngine;

namespace AIT.Riding
{
    // Logical junctions live with the rider; visual signal heads may unload.
    public sealed class CityRoadRuntime : MonoBehaviour
    {
        public RideTestSession session;
        public CityRoadNetwork network;
        public CityJunction[] junctions;
    }
}
