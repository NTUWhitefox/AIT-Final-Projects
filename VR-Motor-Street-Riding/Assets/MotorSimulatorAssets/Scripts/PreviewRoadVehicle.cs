using UnityEngine;

namespace AIT.Riding
{
    // One straight preview route; not an autonomous traffic or crash model.
    public sealed class PreviewRoadVehicle : MonoBehaviour
    {
        public Transform[] wheels;
        public PreviewTrafficSignal signal;
        public RideTestSession session;
        public float speedKph = 12, acceleration = 1.8f, braking = 3;
        public float stopZ = -13, endZ = 135, wheelRadius = .32f;
        public float Speed { get; private set; }
        float wheelAngle;
        Quaternion[] initialWheelRotations;
        void Awake()
        {
            initialWheelRotations = new Quaternion[wheels.Length];
            for (int i = 0; i < wheels.Length; i++) initialWheelRotations[i] = wheels[i].localRotation;
        }
        void FixedUpdate()
        {
            if (session && (!session.Running || session.StageIndex != 0)) return;
            float distance = endZ - transform.position.z;
            if (signal && !signal.AllowsTraffic && transform.position.z <= stopZ + .02f)
                distance = Mathf.Min(distance, stopZ - transform.position.z);
            float target = Mathf.Min(speedKph / 3.6f, Mathf.Sqrt(2 * braking * Mathf.Max(0, distance - .03f)));
            Speed = Mathf.MoveTowards(Speed, target, (target < Speed ? braking : acceleration) * Time.fixedDeltaTime);
            float step = Mathf.Min(Speed * Time.fixedDeltaTime, Mathf.Max(0, distance));
            transform.position += Vector3.forward * step;
            wheelAngle = Mathf.Repeat(wheelAngle + step / wheelRadius * Mathf.Rad2Deg, 360);
            for (int i = 0; i < wheels.Length; i++)
                if (wheels[i]) wheels[i].localRotation = initialWheelRotations[i] * Quaternion.Euler(wheelAngle, 0, 0);
        }
    }
}
