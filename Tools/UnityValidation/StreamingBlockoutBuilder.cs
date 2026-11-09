// Offline prototype authoring. This file never enters the working project's Assets.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using AIT.Riding;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

public static class StreamingBlockoutBuilder
{
    public const string Bootstrap="Assets/Scenes/StreamingBlockout.unity";
    const string SceneFolder="Assets/Scenes/StreamingBlockoutChunks";
    const string Root="Assets/MotorSimulatorAssets/StreamingBlockoutGenerated";
    public const string WorldPath=Root+"/BlockoutWorld.asset";
    static Material road,ground,paint,proxyBlocks,blocks;
    sealed class Recipe {public Vector3 p,size;}
    public static void Run()
    {
        Directory.CreateDirectory(Root);Directory.CreateDirectory(SceneFolder);Directory.CreateDirectory("Personal/StreamingBlockout");AssetDatabase.Refresh();
        road=Mat("Road",new Color(.12f,.14f,.16f));ground=Mat("Ground",new Color(.34f,.36f,.34f));
        paint=Mat("Route paint",new Color(.95f,.72f,.12f));proxyBlocks=Mat("Distant blocks",new Color(.43f,.46f,.48f));
        blocks=Mat("Near blocks",new Color(.61f,.67f,.69f));
        var world=AssetDatabase.LoadAssetAtPath<StreamingWorldDefinition>(WorldPath);
        if(!world){world=ScriptableObject.CreateInstance<StreamingWorldDefinition>();AssetDatabase.CreateAsset(world,WorldPath);}
        world.worldId="streaming-blockout-01";world.contentVersion="1";world.chunkSize=256;world.gridOrigin=new Vector3(-256,0,-512);
        world.worldBounds=new Bounds(new Vector3(256,0,0),new Vector3(1024,256,1024));
        var loop=Loop();world.routePoints=loop.Select((p,i)=>p+Vector3.Cross(Vector3.up,(loop[(i+1)%loop.Length]-loop[(i-1+loop.Length)%loop.Length]).normalized)*1.75f).ToArray();
        world.roads=new[]{new BlockoutRoadDefinition{id="loop",width=14,centreline=loop.Concat(new[]{loop[0]}).ToArray()},
            new BlockoutRoadDefinition{id="east-west",width=20,centreline=new[]{new Vector3(-256,0,0),new Vector3(768,0,0)}},
            new BlockoutRoadDefinition{id="north-south",width=14,centreline=new[]{new Vector3(256,0,-512),new Vector3(256,0,512)}}};
        var definitions=new List<StreamingChunkDefinition>();
        for(int z=0;z<4;z++)for(int x=0;x<4;x++)
        {
            string id=$"B{x:00}_{z:00}";
            definitions.Add(new StreamingChunkDefinition{id=id,cell=new Vector2Int(x,z),scenePath=SceneFolder+"/"+id+".unity",
                bounds=new Bounds(world.gridOrigin+new Vector3(x*256+128,0,z*256+128),new Vector3(256,256,256))});
        }
        world.chunks=definitions.ToArray();EditorUtility.SetDirty(world);AssetDatabase.SaveAssets();
        foreach(var chunk in world.chunks)BuildChunk(world,chunk);
        EditorSceneManager.OpenScene("Assets/Scenes/CityCorners.unity");
        var session=UnityEngine.Object.FindFirstObjectByType<RideTestSession>();
        foreach(var obj in SceneManager.GetActiveScene().GetRootGameObjects())if(obj!=session.gameObject)UnityEngine.Object.DestroyImmediate(obj);
        var runtime=new GameObject("Streaming world runtime");var course=runtime.AddComponent<RideTestCourse>();session.course=course;
        var start=world.routePoints[0];
        course.stages=new[]{new RideTestStage{name="區塊環線 / 約 2.2 km",freeRoam=true,spawnPosition=start,centreline=Array.Empty<Vector3>()}};
        course.streetSegmentEndpoints=world.roads.SelectMany(r=>Enumerable.Range(1,r.centreline.Length-1).SelectMany(i=>new[]{r.centreline[i-1],r.centreline[i]})).ToArray();
        session.vehicleRoot.SetPositionAndRotation(start,Quaternion.identity);
        var streamer=runtime.AddComponent<RideWorldStreamer>();streamer.session=session;streamer.world=world;session.worldStreamer=streamer;
        var tracker=runtime.AddComponent<BlockoutRouteProgress>();tracker.session=session;tracker.world=world;
        streamer.proxyRoot=new GameObject("Low detail neighbourhoods").transform;streamer.proxyRoot.SetParent(runtime.transform);
        foreach(var chunk in world.chunks)
        {
            var proxy=new GameObject(chunk.id).transform;proxy.SetParent(streamer.proxyRoot);
            MeshObject("Ground proxy",proxy,AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Ground_"+chunk.id+".asset"),ground);
            MeshObject("Road proxy",proxy,AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Road_"+chunk.id+".asset"),road);
            MeshObject("Block proxy",proxy,Save(BoxesMesh(Recipes(world,chunk)),"Proxy_"+chunk.id),proxyBlocks);
        }
        UrbanVehiclePreviewBuilder.Lighting();RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;
        RenderSettings.fogColor=new Color(.56f,.65f,.72f);RenderSettings.fogStartDistance=380;RenderSettings.fogEndDistance=750;
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),Bootstrap);AssetDatabase.SaveAssets();
        var sceneList=EditorBuildSettings.scenes.ToList();
        foreach(string path in new[]{Bootstrap}.Concat(world.chunks.Select(c=>c.scenePath)))
        {int i=sceneList.FindIndex(s=>s.path==path);if(i<0)sceneList.Add(new EditorBuildSettingsScene(path,true));else sceneList[i].enabled=true;}
        EditorBuildSettings.scenes=sceneList.ToArray();
        float length=0;for(int i=0;i<world.routePoints.Length;i++)length+=Vector3.Distance(world.routePoints[i],world.routePoints[(i+1)%world.routePoints.Length]);
        File.WriteAllText("Personal/StreamingBlockout/authoring.txt",$"16 additive chunks, 256 m; world 1024 x 1024 m; route={length:F1} m; 3D bounds and road coordinates; flat riding only.\n");
        session.GetComponent<RidePresentation>().pausedPanel.SetActive(false);
        RenderSettings.fog=false;
        UrbanVehiclePreviewBuilder.Capture("../StreamingBlockout/overview.png",new Vector3(1050,900,-950),new Vector3(256,0,0),65,2600);
        RenderSettings.fog=true;
        UrbanVehiclePreviewBuilder.Capture("../StreamingBlockout/rider.png",start+new Vector3(0,1.35f,-.28f),start+new Vector3(0,1,50),80);
    }
    static Vector3[] Loop()
    {
        var list=new List<Vector3>();Vector3 previous=new Vector3(-74,0,-200);list.Add(previous);float heading=0;
        foreach(var centre in new[]{new Vector3(-34,0,200),new Vector3(546,0,200),new Vector3(546,0,-200),new Vector3(-34,0,-200)})
        {
            var q=Quaternion.Euler(0,heading,0);var start=centre-q*Vector3.right*40;
            int n=Mathf.CeilToInt(Vector3.Distance(previous,start)/6);
            for(int i=1;i<=n;i++)list.Add(Vector3.Lerp(previous,start,(float)i/n));
            var radial=start-centre;
            for(int i=1;i<=20;i++)list.Add(centre+Quaternion.Euler(0,90f*i/20,0)*radial);
            previous=list[list.Count-1];heading+=90;
        }
        list.RemoveAt(list.Count-1); // the final arc ends at the start; keep one copy
        return list.ToArray();
    }
    static void BuildChunk(StreamingWorldDefinition world,StreamingChunkDefinition chunk)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var root=new GameObject(chunk.id);var marker=root.AddComponent<WorldChunkMarker>();marker.worldId=world.worldId;marker.chunkId=chunk.id;
        var content=new GameObject("Near detail and collision");content.transform.SetParent(root.transform);marker.contentRoot=content;
        var floor=new GameObject("Ground collision");floor.transform.SetParent(content.transform);
        var box=floor.AddComponent<BoxCollider>();box.center=chunk.bounds.center+Vector3.down*.05f;box.size=new Vector3(256,.1f,256);
        MeshObject("Ground",content.transform,Save(FloorMesh(chunk.bounds),"Ground_"+chunk.id),ground);
        var roadMesh=RoadMesh(world,chunk.bounds);
        MeshObject("Road",content.transform,Save(roadMesh,"Road_"+chunk.id),road);
        var recipes=Recipes(world,chunk);
        MeshObject("Blocks",content.transform,Save(BoxesMesh(recipes),"Blocks_"+chunk.id),blocks);
        foreach(var item in recipes)
        {var obstacle=new GameObject("Block collision");obstacle.transform.SetParent(content.transform);var c=obstacle.AddComponent<BoxCollider>();c.center=item.p;c.size=item.size;}
        // Sparse arrows supply a visible rideable loop, independent of chunk IDs.
        var vertices=new List<Vector3>();var tris=new List<int>();
        for(int i=0;i<world.routePoints.Length;i+=12)
        {
            var p=world.routePoints[i];if(!chunk.bounds.Contains(p))continue;
            var forward=(world.routePoints[(i+1)%world.routePoints.Length]-world.routePoints[i]).normalized;
            var right=Vector3.Cross(Vector3.up,forward);int index=vertices.Count;
            vertices.Add(p+Vector3.up*.025f+forward*2);vertices.Add(p+Vector3.up*.025f-forward*1.3f+right*.7f);vertices.Add(p+Vector3.up*.025f-forward*1.3f-right*.7f);
            tris.AddRange(new[]{index,index+1,index+2});
        }
        var arrows=new Mesh{name="Route arrows"};arrows.SetVertices(vertices);arrows.SetTriangles(tris,0);arrows.RecalculateNormals();
        MeshObject("Route arrows",content.transform,Save(arrows,"Arrows_"+chunk.id),paint);
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),chunk.scenePath);
    }
    static List<Recipe> Recipes(StreamingWorldDefinition world,StreamingChunkDefinition chunk)
    {
        var result=new List<Recipe>();var b=chunk.bounds;
        for(float x=b.min.x+26;x<b.max.x-22;x+=48)for(float z=b.min.z+26;z<b.max.z-22;z+=48)
        {
            var p=new Vector3(x,0,z);bool nearRoad=false;
            foreach(var r in world.roads)for(int i=1;i<r.centreline.Length;i++)
            {
                var a=r.centreline[i-1];var d=r.centreline[i]-a;
                float t=Mathf.Clamp01(Vector3.Dot(p-a,d)/Mathf.Max(.0001f,d.sqrMagnitude));
                if(Vector3.Distance(p,a+d*t)<r.width*.5f+18)nearRoad=true;
            }
            if(nearRoad)continue;
            float height=7+((int)(x+z)+2048)%4*4;
            result.Add(new Recipe{p=p+Vector3.up*height*.5f,size=new Vector3(22,height,24)});
        }
        return result;
    }
    static Mesh RoadMesh(StreamingWorldDefinition world,Bounds bounds)
    {
        var v=new List<Vector3>();var t=new List<int>();
        foreach(var roadDef in world.roads)for(int i=1;i<roadDef.centreline.Length;i++)
        {
            var a=roadDef.centreline[i-1];var b=roadDef.centreline[i];var right=Vector3.Cross(Vector3.up,(b-a).normalized)*roadDef.width*.5f;
            var polygon=new List<Vector3>{a-right+Vector3.up*.012f,b-right+Vector3.up*.012f,b+right+Vector3.up*.012f,a+right+Vector3.up*.012f};
            polygon=Clip(polygon,0,bounds.min.x,true);polygon=Clip(polygon,0,bounds.max.x,false);
            polygon=Clip(polygon,2,bounds.min.z,true);polygon=Clip(polygon,2,bounds.max.z,false);
            int first=v.Count;v.AddRange(polygon);for(int j=1;j<polygon.Count-1;j++)t.AddRange(new[]{first,first+j,first+j+1});
        }
        var mesh=new Mesh{name="Clipped roads",indexFormat=IndexFormat.UInt32};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    static List<Vector3> Clip(List<Vector3> input,int axis,float edge,bool greater)
    {
        var result=new List<Vector3>();if(input.Count==0)return result;
        var previous=input[input.Count-1];bool before=greater?previous[axis]>=edge:previous[axis]<=edge;
        foreach(var current in input)
        {
            bool inside=greater?current[axis]>=edge:current[axis]<=edge;
            if(inside!=before)result.Add(Vector3.Lerp(previous,current,(edge-previous[axis])/(current[axis]-previous[axis])));
            if(inside)result.Add(current);previous=current;before=inside;
        }
        return result;
    }
    static Mesh FloorMesh(Bounds b)
    {
        var mesh=new Mesh{name="Ground tile"};mesh.vertices=new[]{new Vector3(b.min.x,0,b.min.z),new Vector3(b.min.x,0,b.max.z),new Vector3(b.max.x,0,b.max.z),new Vector3(b.max.x,0,b.min.z)};
        mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    static Mesh BoxesMesh(List<Recipe> recipes)
    {
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);var source=cube.GetComponent<MeshFilter>().sharedMesh;
        var mesh=new Mesh{name="Block buildings",indexFormat=IndexFormat.UInt32};
        if(recipes.Count>0)mesh.CombineMeshes(recipes.Select(r=>new CombineInstance{mesh=source,transform=Matrix4x4.TRS(r.p,Quaternion.identity,r.size)}).ToArray());
        UnityEngine.Object.DestroyImmediate(cube);return mesh;
    }
    static void MeshObject(string name,Transform parent,Mesh mesh,Material material)
    {var o=new GameObject(name);o.transform.SetParent(parent);o.AddComponent<MeshFilter>().sharedMesh=mesh;o.AddComponent<MeshRenderer>().sharedMaterial=material;}
    static Mesh Save(Mesh mesh,string name)
    {
        string path=Root+"/"+name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(old){EditorUtility.CopySerialized(mesh,old);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(old);return old;}AssetDatabase.CreateAsset(mesh,path);return mesh;
    }
    static Material Mat(string name,Color c)
    {
        string path=Root+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
        m.SetColor("_BaseColor",c);m.SetFloat("_Smoothness",.15f);m.enableInstancing=true;EditorUtility.SetDirty(m);return m;
    }
}
