using System;
using UnityEngine;

namespace AIT.Riding
{
    [Serializable] public sealed class CityTrafficTurn
    {
        public CityJunction junction;
        public float start, end, stop, approachYaw;
        public int direction; // +1 right, -1 left
    }

    public sealed class CityTrafficVehicle : MonoBehaviour
    {
        public RideTestSession session;
        public Transform[] wheels;
        public Vector3[] pathPoints;
        public float[] pathYaw, pathCurvature;
        public CityTrafficTurn[] turns;
        public float initialProgress, cruiseKph = 20, acceleration = 1.5f, braking = 3;
        public float wheelbase = 2.95f, rearAxleZ = -1.45f, wheelRadius = .32f;
        public float Speed { get; private set; }
        public float Progress { get; private set; }
        public float ReservedApproachYaw { get; private set; }
        public float SteeringDegrees { get; private set; }
        public float Length => distances == null ? 0 : distances[distances.Length - 1];
        float[] distances, wheelRoll, wheelX;
        Quaternion[] wheelRest;
        MeshRenderer[] lamps;
        CityTrafficVehicle[] fleet;
        CityJunction reserved;
        float releaseAt;
        MaterialPropertyBlock lampProperties;

        void Awake() => Initialise();
        void OnEnable() { if (session) session.SessionEvent += OnSessionEvent; }
        void OnDisable() { if (session) session.SessionEvent -= OnSessionEvent; if (reserved) reserved.Release(this); }
        void OnSessionEvent(string name, string detail) { if (name == "stage_reset") ResetRoute(); }
        public void Initialise()
        {
            if (pathPoints == null || pathPoints.Length < 2) return;
            distances = new float[pathPoints.Length];
            for (int i = 1; i < distances.Length; i++) distances[i] = distances[i - 1] + Vector3.Distance(pathPoints[i - 1], pathPoints[i]);
            wheelRest = new Quaternion[wheels.Length]; wheelRoll = new float[wheels.Length]; wheelX = new float[wheels.Length];
            for (int i = 0; i < wheels.Length; i++)
            { wheelRest[i] = wheels[i].localRotation; wheelX[i] = transform.InverseTransformPoint(wheels[i].position).x; }
            lamps = GetComponentsInChildren<MeshRenderer>();
            fleet = FindObjectsByType<CityTrafficVehicle>(FindObjectsSortMode.None);
            lampProperties = new MaterialPropertyBlock(); ResetRoute();
        }
        void ResetRoute()
        {
            if (reserved) reserved.Release(this); reserved = null;
            Progress = initialProgress; Speed = SteeringDegrees = 0;
            if (distances != null) SetPose(Progress);
        }
        public void Sample(float progress, out Vector3 rearAxle, out float yaw, out float curvature)
        {
            float s = Mathf.Repeat(progress, Length);
            int index = Array.BinarySearch(distances, s);
            if (index < 0) index = ~index - 1;
            index = Mathf.Clamp(index, 0, distances.Length - 2);
            float t = Mathf.InverseLerp(distances[index], distances[index + 1], s);
            rearAxle = Vector3.Lerp(pathPoints[index], pathPoints[index + 1], t);
            yaw = Mathf.LerpAngle(pathYaw[index], pathYaw[index + 1], t);
            curvature = Mathf.Lerp(pathCurvature[index], pathCurvature[index + 1], t);
        }
        public void SetPose(float progress)
        {
            Sample(progress, out var rear, out float yaw, out _);
            var rotation = Quaternion.Euler(0, yaw, 0);
            transform.SetPositionAndRotation(rear - rotation * Vector3.forward * rearAxleZ, rotation);
        }
        CityTrafficTurn NextTurn(out float lap)
        {
            float currentLap = Mathf.Floor(Progress / Length) * Length;
            foreach (var turn in turns)
                if (currentLap + turn.end + 2 >= Progress) { lap = currentLap; return turn; }
            lap = currentLap + Length; return turns[0];
        }
        void FixedUpdate()
        {
            if (distances == null || (session && !session.Running)) return;
            if (reserved && Progress > releaseAt) { reserved.Release(this); reserved = null; }
            var turn = NextTurn(out float lap);
            float stop = lap + turn.stop;
            float usable = 1000;
            if (lap + turn.start - Progress < Mathf.Max(18, Speed * Speed / (2 * braking) + 9))
            {
                ReservedApproachYaw = turn.approachYaw;
                bool committed = reserved == turn.junction && Progress > stop + .05f;
                if (!turn.junction.TryReserve(this, turn.approachYaw, committed)) usable = Mathf.Max(0, stop - Progress);
                else { reserved = turn.junction; releaseAt = lap + turn.end + 3.5f; }
            }
            usable = Mathf.Min(usable, ObstacleDistance());
            float target = cruiseKph / 3.6f;
            // Brake before the bend, using a 1.8 m/s² preview lateral envelope.
            for (float d = 0; d <= 22; d += 1)
            {
                Sample(Progress + d, out _, out _, out float c);
                if (Mathf.Abs(c) > .001f) target = Mathf.Min(target, Mathf.Sqrt(1.8f / Mathf.Abs(c) + 2 * braking * d));
            }
            target = Mathf.Min(target, Mathf.Sqrt(2 * braking * Mathf.Max(0, usable - .05f)));
            float previousSpeed = Speed;
            Speed = Mathf.MoveTowards(Speed, target, (target < Speed ? braking : acceleration) * Time.fixedDeltaTime);
            float step = Mathf.Min(Speed * Time.fixedDeltaTime, usable);
            Progress += step; SetPose(Progress);
            Sample(Progress + .7f, out _, out _, out float curve);
            float requestedSteer = Mathf.Atan(wheelbase * curve) * Mathf.Rad2Deg;
            SteeringDegrees = Mathf.MoveTowards(SteeringDegrees, requestedSteer, 75 * Time.fixedDeltaTime);
            float steeringCurve = Mathf.Tan(SteeringDegrees * Mathf.Deg2Rad) / wheelbase;
            for (int i = 0; i < wheels.Length; i++)
            {
                float angle = wheels[i].name.Contains("Front") ? Mathf.Atan(wheelbase * steeringCurve / (1 - steeringCurve * wheelX[i])) * Mathf.Rad2Deg : 0;
                float radiusRatio = Mathf.Sqrt(Mathf.Pow(1 - curve * wheelX[i], 2) + (wheels[i].name.Contains("Front") ? Mathf.Pow(curve * wheelbase, 2) : 0));
                wheelRoll[i] = Mathf.Repeat(wheelRoll[i] + step * radiusRatio / wheelRadius * Mathf.Rad2Deg, 360);
                wheels[i].localRotation = wheelRest[i] * Quaternion.Euler(0, angle, 0) * Quaternion.Euler(wheelRoll[i], 0, 0);
            }
            int indicator = lap + turn.start - Progress < 22 && Progress < lap + turn.end + 1 ? turn.direction : 0;
            Lamps(Speed < previousSpeed - .001f || Speed < .05f, indicator);
        }
        float ObstacleDistance()
        {
            float available = 1000;
            foreach (var car in fleet)
            {
                if (!car || car == this) continue;
                var delta = car.transform.position - transform.position;
                float along = Vector3.Dot(delta, transform.forward);
                if (along > 0 && Mathf.Abs(Vector3.Dot(delta, transform.right)) < 2.2f)
                    available = Mathf.Min(available, Mathf.Max(0, along - 7.5f));
            }
            if (session && session.vehicleRoot)
                for (float d = 0; d <= 22; d += .75f)
                {
                    Sample(Progress + d, out var rear, out float yaw, out _);
                    var q = Quaternion.Euler(0, yaw, 0);
                    var centre = rear - q * Vector3.forward * rearAxleZ;
                    var local = Quaternion.Inverse(q) * (session.vehicleRoot.position - centre);
                    if (Mathf.Abs(local.x) < 1.65f && Mathf.Abs(local.z) < 3.9f)
                    { available = Mathf.Min(available, Mathf.Max(0, d - 1)); break; }
                }
            return available;
        }
        void Lamps(bool brakingNow, int indicator)
        {
            bool blink = Mathf.Repeat(Time.time, .8f) < .4f;
            foreach (var r in lamps)
            {
                bool brake = r.name.Contains("BrakeLight");
                bool left = r.name.Contains("TurnSignal") && r.name.Contains("Left");
                bool right = r.name.Contains("TurnSignal") && r.name.Contains("Right");
                if (!brake && !left && !right) continue;
                bool on = brake ? brakingNow : blink && (left && indicator < 0 || right && indicator > 0);
                lampProperties.Clear(); lampProperties.SetColor("_EmissionColor", on ? Color.white * 3 : Color.black);
                r.SetPropertyBlock(lampProperties);
            }
        }
    }
}
