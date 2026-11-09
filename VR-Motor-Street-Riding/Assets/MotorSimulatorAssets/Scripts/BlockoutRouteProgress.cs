using UnityEngine;

namespace AIT.Riding
{
    // Ordered checkpoints for this loading prototype, not the final lane/route editor format.
    public sealed class BlockoutRouteProgress : MonoBehaviour
    {
        public RideTestSession session;
        public StreamingWorldDefinition world;
        public float checkpointRadius = 14;
        public int NextPoint { get; private set; } = 1;
        public int CompletedLaps { get; private set; }
        void OnEnable(){if(session)session.SessionEvent+=Event;}
        void OnDisable(){if(session)session.SessionEvent-=Event;}
        void Event(string name,string detail){if(name=="stage_reset"){NextPoint=1;CompletedLaps=0;}}
        void Update()
        {
            if(!session.Running||!world||world.routePoints==null||world.routePoints.Length<2)return;
            if(Vector3.Distance(session.Model.position,world.routePoints[NextPoint])>checkpointRadius)return;
            NextPoint++;
            if(NextPoint>=world.routePoints.Length)
            {NextPoint=1;CompletedLaps++;session.Emit("blockout_route_complete",CompletedLaps.ToString());}
        }
    }
}
