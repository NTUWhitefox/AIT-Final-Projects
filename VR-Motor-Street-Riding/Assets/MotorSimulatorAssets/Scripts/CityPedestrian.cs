using UnityEngine;

namespace AIT.Riding
{
    // Controlled crossing preview, not a crowd/priority-negotiation model.
    public sealed class CityPedestrian : MonoBehaviour
    {
        public string actorId;
        public CityRoadRuntime runtime;
        public int node, arm;
        public float speed = 1.25f, initialWait;
        [Min(0)] public float walkReferenceSpeed;
        public bool useSidewalkRoute;
        public Vector3 sidewalkStart, sidewalkEnd;
        public Animation animationPlayer;
        public AnimationClip idleClip, walkClip;
        public bool Crossing { get; private set; }
        public int CrossingsCompleted { get; private set; }
        float wait, progress;
        int side = -1;
        bool returning;
        void OnEnable() { if (runtime && runtime.session) runtime.session.SessionEvent += Event; }
        void OnDisable() { Release(); if (runtime && runtime.session) runtime.session.SessionEvent -= Event; }
        void Start() { ResetActor(); }
        void Event(string name, string detail) { if (name == "stage_reset") ResetActor(); }
        void ResetActor()
        {
            Release(); side = -1; progress = 0; wait = initialWait; CrossingsCompleted = 0;
            returning = false;
            if (useSidewalkRoute)
            {
                Vector3 direction = sidewalkEnd - sidewalkStart;
                if (direction.sqrMagnitude > .001f)
                    transform.SetPositionAndRotation(sidewalkStart, Quaternion.LookRotation(direction));
            }
            else Pose();
            Play(false);
        }
        void Release() { if (Crossing && runtime) runtime.junctions[node].EndPedestrian(); Crossing = false; }
        void Play(bool walk)
        {
            if (!animationPlayer) return; var clip = walk ? walkClip : idleClip;
            if (clip && !animationPlayer.IsPlaying(clip.name)) animationPlayer.CrossFade(clip.name, .15f);
        }
        void Update()
        {
            bool running = runtime && runtime.session.Running;
            if (animationPlayer)
                foreach (AnimationState state in animationPlayer)
                {
                    float animSpeed = state.clip == walkClip && walkReferenceSpeed > .01f ? speed / walkReferenceSpeed : 1;
                    state.speed = running ? animSpeed : 0;
                }
            if (!running) return;
            if (useSidewalkRoute) { UpdateSidewalk(); return; }
            if (!Crossing)
            {
                wait -= Time.deltaTime;
                if (wait > 0 || !runtime.junctions[node].TryBeginPedestrian()) return;
                Crossing = true; progress = 0; Play(true);
                runtime.session.Emit("pedestrian_crossing_start", actorId + ";junction=" + runtime.network.nodes[node].id);
            }
            float length = 2 * (runtime.network.HalfRoad + runtime.network.sidewalkWidth / 2);
            progress = Mathf.Min(1, progress + speed * Time.deltaTime / length); Pose();
            if (progress < 1) return;
            side = -side; progress = 0; Release(); CrossingsCompleted++; wait = 5 + initialWait; Play(false); Pose();
            runtime.session.Emit("pedestrian_crossing_end", actorId);
        }
        void UpdateSidewalk()
        {
            Vector3 target = returning ? sidewalkStart : sidewalkEnd;
            Vector3 forward = returning ? sidewalkStart - sidewalkEnd : sidewalkEnd - sidewalkStart;
            if (forward.sqrMagnitude < .001f) return;
            Quaternion heading = Quaternion.LookRotation(forward);
            if (wait > 0)
            {
                wait -= Time.deltaTime;
                if (wait < .55f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, heading, 400f * Time.deltaTime);
                return;
            }
            transform.rotation = heading;
            Play(true);
            transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
            if ((transform.position - target).sqrMagnitude > .000001f) return;
            returning = !returning;
            wait = .92f;
            Play(false);
        }
        void Pose()
        {
            if (!runtime) return;
            var n = runtime.network; var outward = CityRoadNetwork.Direction(arm); var across = CityRoadNetwork.Right(-outward);
            float extent = n.HalfRoad + n.sidewalkWidth / 2;
            float x = Mathf.Lerp(side * extent, -side * extent, progress);
            float height = n.curbHeight * Mathf.Clamp01((Mathf.Abs(x) - n.HalfRoad) / .6f);
            transform.SetPositionAndRotation(n.nodes[node].position + outward * n.crossingDistance + across * x + Vector3.up * height,
                Quaternion.LookRotation(across * -side));
        }
    }
}
