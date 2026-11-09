using UnityEngine;

namespace AIT.Riding
{
    // One vehicle reserves the conflict area. This is controlled preview traffic,
    // not a general priority/negotiation model for Taipei intersections.
    public sealed class CityJunction : MonoBehaviour
    {
        public RideTestSession session;
        public bool signalControlled;
        public PreviewTrafficSignal northSouthSignal, eastWestSignal;
        CityTrafficVehicle occupant;
        float elapsed;
        int pedestrianUsers;
        void OnEnable() { if (session) session.SessionEvent += OnSessionEvent; ApplySignals(); }
        void OnDisable() { if (session) session.SessionEvent -= OnSessionEvent; }
        void OnSessionEvent(string name, string detail)
        { if (name == "stage_reset") { occupant = null; elapsed = 0; pedestrianUsers = 0; ApplySignals(); } }
        void Update()
        { if (session && session.Running) { if (pedestrianUsers == 0) elapsed += Time.deltaTime; ApplySignals(); } }
        PreviewSignalColour Phase(bool northSouth)
        {
            float t = Mathf.Repeat(elapsed, 46);
            if (northSouth) return t < 18 ? PreviewSignalColour.Green : t < 21 ? PreviewSignalColour.Amber : PreviewSignalColour.Red;
            return t >= 23 && t < 41 ? PreviewSignalColour.Green : t >= 41 && t < 44 ? PreviewSignalColour.Amber : PreviewSignalColour.Red;
        }
        public bool TryReserve(CityTrafficVehicle car, float approachYaw, bool committed)
        {
            if (pedestrianUsers > 0 && !committed) return false;
            if (occupant && occupant != car) return false;
            bool ns = Mathf.Abs(Mathf.Cos(approachYaw * Mathf.Deg2Rad)) > .5f;
            if (signalControlled && Phase(ns) != PreviewSignalColour.Green && !committed)
            { if (occupant == car) occupant = null; return false; }
            occupant = car; return true;
        }
        public void Release(CityTrafficVehicle car) { if (occupant == car) occupant = null; }
        public bool TryBeginPedestrian()
        {
            if (occupant || !signalControlled || Phase(true) != PreviewSignalColour.Red || Phase(false) != PreviewSignalColour.Red) return false;
            pedestrianUsers++; return true;
        }
        public void EndPedestrian() { pedestrianUsers = Mathf.Max(0, pedestrianUsers - 1); }
        public bool PedestrianClearance => pedestrianUsers > 0;
        void ApplySignals()
        {
            if (!signalControlled) return;
            Set(northSouthSignal, ReadSignal(true));
            Set(eastWestSignal, ReadSignal(false));
        }
        public PreviewSignalColour ReadSignal(bool northSouth)
        {
            if (!signalControlled) return PreviewSignalColour.Green;
            if (pedestrianUsers > 0) return PreviewSignalColour.Red;
            bool occupiedNS = occupant && Mathf.Abs(Mathf.Cos(occupant.ReservedApproachYaw * Mathf.Deg2Rad)) > .5f;
            return occupant && occupiedNS != northSouth ? PreviewSignalColour.Red : Phase(northSouth);
        }
        static void Set(PreviewTrafficSignal signal, PreviewSignalColour colour)
        { if (signal && signal.colour != colour) { signal.colour = colour; signal.Apply(); } }
    }
}
