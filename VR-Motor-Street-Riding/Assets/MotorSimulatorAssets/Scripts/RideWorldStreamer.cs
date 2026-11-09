using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AIT.Riding
{
    [DefaultExecutionOrder(-120)]
    public sealed class RideWorldStreamer : MonoBehaviour
    {
        enum ChunkState { Unloaded, Loading, Ready, Unloading, Failed }
        sealed class Entry
        {
            public StreamingChunkDefinition definition;
            public ChunkState state;
            public Scene scene;
            public WorldChunkMarker marker;
            public GameObject proxy;
            public double lastWanted, retryAt;
            public bool wanted;
            public int failures;
            public string error;
        }
        public RideTestSession session;
        public StreamingWorldDefinition world;
        public Transform proxyRoot;
        [Min(0)] public float unloadDelaySeconds = 6;
        [Min(0)] public float predictionSeconds = 5;
        public int LoadsCompleted { get; private set; }
        public int UnloadsCompleted { get; private set; }
        public int Failures { get; private set; }
        public int ReadyCount { get { int n=0;foreach(var e in entries)if(e.state==ChunkState.Ready)n++;return n; } }
        public string LastError { get; private set; }
        public string PreparationStatus => LastError == null ? "正在準備附近街區，請稍候再開始。" : "街區載入失敗；請稍候，或再選開始重試。";
        readonly List<Entry> entries = new List<Entry>();
        float nextWindow;
        bool stopping;

        void Awake()
        {
            if (!world || !session || world.chunks==null || world.chunks.Length==0 || world.chunkSize<=0)
            { LastError = "Streaming world/session/chunk manifest is missing or invalid"; enabled=false; return; }
            var ids=new HashSet<string>();var paths=new HashSet<string>();
            foreach(var definition in world.chunks)
            {
                if(string.IsNullOrEmpty(definition.id)||!ids.Add(definition.id)||!paths.Add(definition.scenePath))
                {LastError="Duplicate/missing chunk ID or scene path";enabled=false;return;}
                var proxy=proxyRoot ? proxyRoot.Find(definition.id) : null;
                entries.Add(new Entry{definition=definition,proxy=proxy?proxy.gameObject:null});
            }
            RefreshWindow();
        }
        void Start() { if(enabled) StartCoroutine(Work()); }
        void Update() { if(Time.unscaledTime>=nextWindow){nextWindow=Time.unscaledTime+.15f;RefreshWindow();} }
        void RefreshWindow()
        {
            if(!session||!world)return;
            Vector3 position=session.Model.position;
            // Session.Awake has not necessarily initialized the model yet.
            if(!session.Settings)position=session.vehicleRoot.position;
            var cell=world.CellAt(position);
            var forward=Quaternion.Euler(0,session.Model.yaw,0)*Vector3.forward;
            var predicted=world.CellAt(position+forward*session.Model.speed*predictionSeconds);
            double now=Time.realtimeSinceStartupAsDouble;
            foreach(var e in entries)
            {
                var c=e.definition.cell;
                e.wanted=Mathf.Abs(c.x-cell.x)<=1&&Mathf.Abs(c.y-cell.y)<=1;
                // A fast approach adds the predicted neighbourhood; all four sides remain covered.
                e.wanted|=Mathf.Abs(c.x-predicted.x)<=1&&Mathf.Abs(c.y-predicted.y)<=1;
                if(e.wanted)e.lastWanted=now;
            }
            RefreshError();
        }
        void RefreshError()
        {
            LastError=null;
            foreach(var e in entries)
                if(e.wanted&&e.state==ChunkState.Failed){LastError=e.error;break;}
        }
        IEnumerator Work()
        {
            yield return null; // telemetry and input setup finish before the first request
            while(!stopping)
            {
                RefreshWindow();
                Entry next=null;float best=float.MaxValue;
                var position=session.Model.position;double now=Time.realtimeSinceStartupAsDouble;
                foreach(var e in entries)
                {
                    bool canLoad=e.state==ChunkState.Unloaded || e.state==ChunkState.Failed&&e.failures<3&&now>=e.retryAt;
                    if(!e.wanted||!canLoad)continue;
                    float distance=e.definition.bounds.SqrDistance(position);
                    if(distance<best){best=distance;next=e;}
                }
                if(next!=null) {yield return Load(next);continue;}
                foreach(var e in entries)
                    if(e.state==ChunkState.Ready&&!e.wanted&&now-e.lastWanted>=unloadDelaySeconds){next=e;break;}
                if(next!=null){yield return Unload(next);continue;}
                yield return null;
            }
        }
        IEnumerator Load(Entry e)
        {
            e.state=ChunkState.Loading;
            double began=Time.realtimeSinceStartupAsDouble;
            session.Emit("chunk_load_requested",e.definition.id);
            AsyncOperation operation=null;Exception error=null;
            try {operation=SceneManager.LoadSceneAsync(e.definition.scenePath,LoadSceneMode.Additive);}
            catch(Exception ex){error=ex;}
            if(operation==null){Failed(e,error?.Message??"No scene load operation");yield break;}
            while(!operation.isDone)yield return null;
            e.scene=SceneManager.GetSceneByPath(e.definition.scenePath);
            if(e.scene.IsValid()&&e.scene.isLoaded)
                foreach(var root in e.scene.GetRootGameObjects())
                {
                    var marker=root.GetComponent<WorldChunkMarker>();
                    if(marker&&marker.worldId==world.worldId&&marker.chunkId==e.definition.id){e.marker=marker;break;}
                }
            if(!e.marker||!e.marker.contentRoot||!e.marker.contentRoot.GetComponentInChildren<Collider>())
            {
                if(e.scene.IsValid()&&e.scene.isLoaded)yield return SceneManager.UnloadSceneAsync(e.scene);
                Failed(e,"Loaded chunk has no matching identity/content/collider");yield break;
            }
            Physics.SyncTransforms();
            e.state=ChunkState.Ready;e.failures=0;e.error=null;LoadsCompleted++;
            if(e.proxy)e.proxy.SetActive(false);
            RefreshError();
            session.Emit("chunk_ready",e.definition.id+";seconds="+(Time.realtimeSinceStartupAsDouble-began).ToString("F4",System.Globalization.CultureInfo.InvariantCulture));
        }
        void Failed(Entry e,string error)
        {
            e.state=ChunkState.Failed;e.failures++;Failures++;e.retryAt=Time.realtimeSinceStartupAsDouble+3;
            e.error=e.definition.id+": "+error;RefreshError();
            session.Emit("chunk_failed",e.error);
        }
        IEnumerator Unload(Entry e)
        {
            e.state=ChunkState.Unloading;
            // The low-cost proxy replaces distant visuals before destruction.
            if(e.marker&&e.marker.contentRoot)e.marker.contentRoot.SetActive(false);
            if(e.proxy)e.proxy.SetActive(true);
            session.Emit("chunk_unload_requested",e.definition.id);
            if(e.scene.IsValid()&&e.scene.isLoaded)yield return SceneManager.UnloadSceneAsync(e.scene);
            e.marker=null;e.scene=default;e.state=ChunkState.Unloaded;UnloadsCompleted++;
            session.Emit("chunk_unloaded",e.definition.id);
        }
        public void RetryFailed()
        {if(!enabled)return;foreach(var e in entries)if(e.state==ChunkState.Failed){e.failures=0;e.retryAt=0;}RefreshWindow();}
        public bool Contains(Vector3 point) => world&&world.worldBounds.Contains(point);
        public bool ReadyToStart(Vector3 point)
        {
            if(!enabled||!Contains(point))return false;
            RefreshWindow();
            foreach(var e in entries)if(e.wanted&&e.state!=ChunkState.Ready)return false;
            return TravelReady(point,point);
        }
        public bool TravelReady(Vector3 from,Vector3 to)
        {
            if(!enabled||!Contains(from)||!Contains(to)||!ReadyPoint(from)||!ReadyPoint(to))return false;
            var sweep=new Bounds((from+to)*.5f,new Vector3(Mathf.Abs(to.x-from.x)+1,1,Mathf.Abs(to.z-from.z)+1));
            bool found=false;
            foreach(var e in entries)if(e.definition.bounds.Intersects(sweep))
            {found=true;if(e.state!=ChunkState.Ready)return false;}
            return found;
        }
        bool ReadyPoint(Vector3 point)
        {
            bool found=false;
            foreach(var e in entries)if(e.definition.bounds.Contains(point))
            {found=true;if(e.state!=ChunkState.Ready)return false;}
            return found;
        }
        void OnDestroy(){stopping=true;}
    }
}
