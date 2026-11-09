using System;
using System.Collections.Generic;
using UnityEngine;

namespace AIT.Riding
{
    [Serializable] public sealed class CityRoadNode
    {
        public string id;
        public Vector2Int cell;
        public Vector3 position;
        public int arms; // N=1, E=2, S=4, W=8
        public Vector3[] boundary;
    }
    [Serializable] public sealed class CityRoadLink
    {
        public string id;
        public int from, to;
    }
    [Serializable] public sealed class CityLaneDefinition
    {
        public string id;
        public int from, to, lane;
        public Vector3 start, end;
    }
    [Serializable] public sealed class CityMovementDefinition
    {
        public string id;
        public int node, incomingArm, outgoingArm, lane, turn;
        public Vector3 stopLine;
        public Vector3[] points;
        public float[] yaw, curvature;
    }
    // The authored source for road geometry, traffic, stop lines and routes.
    [CreateAssetMenu(menuName = "AIT/City Road Network")]
    public sealed class CityRoadNetwork : ScriptableObject
    {
        public string networkId = "grid-city-01", version = "1";
        public Vector3 origin;
        public int count = 8;
        public float spacing = 128, nodeOffset = 64, halfJunction = 24;
        public float laneWidth = 3.25f, edgeWidth = .5f, sidewalkWidth = 3, curbHeight = .15f, cornerRadius = 4;
        public float stopDistance = 21.8f, crossingDistance = 14.5f;
        public CityRoadNode[] nodes;
        public CityRoadLink[] links;
        public CityLaneDefinition[] lanes;
        public CityMovementDefinition[] movements;
        public int[] routeNodes;
        public int[] routeLanes;
        public float HalfRoad => laneWidth * 2 + edgeWidth;
        public float LaneOffset(int lane) => laneWidth * (lane + .5f);
        public static Vector3 Direction(int arm) => Quaternion.Euler(0, arm * 90, 0) * Vector3.forward;
        public static Vector3 Right(Vector3 direction) => Vector3.Cross(Vector3.up, direction);
        public int Index(int x, int z) => z * count + x;
        public bool HasArm(int node, int arm) => (nodes[node].arms & (1 << arm)) != 0;
        public Vector3 Port(int node, int arm, int lane, bool incoming)
        {
            var outward = Direction(arm);
            return nodes[node].position + outward * halfJunction + Right(incoming ? -outward : outward) * LaneOffset(lane);
        }
        public int ArmFromTo(int from, int to)
        {
            var d = nodes[to].position - nodes[from].position;
            if (Mathf.Abs(d.x) > Mathf.Abs(d.z)) return d.x > 0 ? 1 : 3;
            return d.z > 0 ? 0 : 2;
        }
        public CityMovementDefinition Movement(int node, int incoming, int outgoing, int lane)
        {
            foreach (var movement in movements)
                if (movement.node == node && movement.incomingArm == incoming && movement.outgoingArm == outgoing && movement.lane == lane) return movement;
            throw new InvalidOperationException($"Missing movement at {nodes[node].id}: {incoming}->{outgoing}, lane {lane}");
        }
        public bool IsDrivable(Vector3 position, float radius = 0)
        {
            if (!PointOnRoad(position)) return false;
            if (radius <= 0) return true;
            for (int i = 0; i < 8; i++)
                if (!PointOnRoad(position + Quaternion.Euler(0, i * 45, 0) * Vector3.forward * radius)) return false;
            return true;
        }
        bool PointOnRoad(Vector3 position)
        {
            float x = (position.x - origin.x - nodeOffset) / spacing;
            float z = (position.z - origin.z - nodeOffset) / spacing;
            int ix = Mathf.Clamp(Mathf.RoundToInt(x), 0, count - 1), iz = Mathf.Clamp(Mathf.RoundToInt(z), 0, count - 1);
            var node = nodes[Index(ix, iz)]; var local = position - node.position;
            if (Mathf.Abs(local.x) <= halfJunction + .001f && Mathf.Abs(local.z) <= halfJunction + .001f)
                return Inside(local, node.boundary);
            if (x >= 0 && x <= count - 1 && Mathf.Abs(local.z) <= HalfRoad) return true;
            if (z >= 0 && z <= count - 1 && Mathf.Abs(local.x) <= HalfRoad) return true;
            return false;
        }
        public static bool Inside(Vector3 p, Vector3[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                var a = polygon[j]; var b = polygon[i]; var d = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, d) / Mathf.Max(.00001f, d.sqrMagnitude));
                if ((p - a - d * t).sqrMagnitude < .000001f) return true;
                if ((a.z > p.z) != (b.z > p.z) && p.x < (b.x - a.x) * (p.z - a.z) / (b.z - a.z) + a.x) inside = !inside;
            }
            return inside;
        }
        public void Route(int[] nodeSequence, int[] laneSequence, out Vector3[] points, out float[] yaw, out float[] curvature,
            out float[] movementStarts, out float[] movementEnds)
        {
            var p = new List<Vector3>(); var y = new List<float>(); var c = new List<float>();
            movementStarts = new float[nodeSequence.Length]; movementEnds = new float[nodeSequence.Length]; float length = 0;
            void Add(Vector3 point, float heading, float curve)
            {
                if (p.Count > 0)
                {
                    float distance = Vector3.Distance(p[p.Count - 1], point);
                    if (distance < .0001f) return;
                    length += distance;
                }
                p.Add(point); y.Add(heading); c.Add(curve);
            }
            for (int i = 0; i < nodeSequence.Length; i++)
            {
                int previous = nodeSequence[(i + nodeSequence.Length - 1) % nodeSequence.Length], current = nodeSequence[i], next = nodeSequence[(i + 1) % nodeSequence.Length];
                int incoming = ArmFromTo(current, previous), outgoing = ArmFromTo(current, next);
                var movement = Movement(current, incoming, outgoing, laneSequence[i]);
                movementStarts[i] = length;
                if (p.Count > 0)
                {
                    var a = p[p.Count - 1]; var b = movement.points[0]; int steps = Mathf.CeilToInt(Vector3.Distance(a, b) / .5f);
                    for (int k = 1; k <= steps; k++)
                    {
                        float t = (float)k / steps; var q = Vector3.Lerp(a, b, t);
                        Add(q, movement.yaw[0], 0);
                    }
                    movementStarts[i] = length;
                }
                for (int k = 0; k < movement.points.Length; k++) Add(movement.points[k], movement.yaw[k], movement.curvature[k]);
                movementEnds[i] = length;
            }
            // The final straight closes the loop without teleporting the vehicle.
            var first = p[0]; var last = p[p.Count - 1]; int closing = Mathf.CeilToInt(Vector3.Distance(last, first) / .5f);
            for (int i = 1; i <= closing; i++) Add(Vector3.Lerp(last, first, (float)i / closing), y[0], 0);
            points = p.ToArray(); yaw = y.ToArray(); curvature = c.ToArray();
        }
    }
}
