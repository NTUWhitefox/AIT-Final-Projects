using System;
using UnityEngine;

namespace AIT.Riding
{
    [Serializable]
    public sealed class RideTestStage
    {
        public string name;
        [HideInInspector] public float speedCapKph; // legacy scene data; no longer applied
        public bool freeRoam;
        public Vector3 spawnPosition;
        public float spawnYaw;
        public Vector3[] centreline;
        public float halfWidth = 4;
        public bool stopAtEnd;
    }

    public sealed class RideTestCourse : MonoBehaviour
    {
        public RideTestStage[] stages;
        public Vector3[] streetSegmentEndpoints; // pairs, for free-roam nearest-street measurement

        public void Measure(int stage, Vector3 position, out float lateralOffset, out float progress)
        {
            lateralOffset = progress = 0;
            if (stages == null || stage < 0 || stage >= stages.Length) return;
            Vector3[] points = stages[stage].centreline;
            bool network = stages[stage].freeRoam;
            if (network) points = streetSegmentEndpoints;
            float best = float.MaxValue, distance = 0;
            if (points == null) return;
            for (int i = 1; i < points.Length; i += network ? 2 : 1)
            {
                Vector3 segment = points[i] - points[i - 1]; segment.y = 0;
                float length = segment.magnitude;
                if (length < 0.001f) continue;
                Vector3 delta = position - points[i - 1]; delta.y = 0;
                float t = Mathf.Clamp01(Vector3.Dot(delta, segment) / segment.sqrMagnitude);
                Vector3 error = delta - segment * t;
                if (error.sqrMagnitude < best)
                {
                    best = error.sqrMagnitude;
                    lateralOffset = Vector3.Dot(error, Vector3.Cross(Vector3.up, segment.normalized));
                    progress = network ? 0 : distance + length * t;
                }
                distance += length;
            }
        }
    }
}
