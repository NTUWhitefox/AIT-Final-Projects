using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
public static class HandPoseReport
{
    public static void Run()
    {
        var s=new StringBuilder();
        foreach(string side in new[]{"Left","Right"})
        {
            var obj=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Samples/XR Hands/1.8.1/HandVisualizer/Models/"+side+"Hand.fbx"));
            s.AppendLine(side);
            foreach(var t in obj.GetComponentsInChildren<Transform>())s.AppendLine(t.name+" parent="+t.parent?.name+" p="+t.localPosition.ToString("F4")+" r="+t.localEulerAngles+" world="+t.position.ToString("F4"));
            foreach(var r in obj.GetComponentsInChildren<SkinnedMeshRenderer>())s.AppendLine("BOUNDS "+r.bounds+" size="+r.bounds.size+" mesh="+r.sharedMesh.bounds);
            Object.DestroyImmediate(obj);
        }
        File.WriteAllText("Personal/hand-pose.txt",s.ToString());
    }
}
