using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.XR.Management;
using AIT.Riding;

public static class StreamingAcceptanceBuild
{
    public static void Run()
    {
        var world=AssetDatabase.LoadAssetAtPath<StreamingWorldDefinition>(StreamingBlockoutBuilder.WorldPath);
        var xr=XRGeneralSettings.Instance;
        bool init=xr.InitManagerOnStart;xr.InitManagerOnStart=false;EditorUtility.SetDirty(xr);AssetDatabase.SaveAssets();
        try
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
            Directory.CreateDirectory("Personal/StreamingBlockout/Player");
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{
                scenes=new[]{StreamingBlockoutBuilder.Bootstrap}.Concat(world.chunks.Select(c=>c.scenePath)).ToArray(),
                locationPathName="Personal/StreamingBlockout/Player/StreamingBlockout.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
            File.WriteAllText("Personal/StreamingBlockout/build.txt",result.summary.result+"; errors="+result.summary.totalErrors+"; size="+result.summary.totalSize);
            if(result.summary.result!=BuildResult.Succeeded)throw new System.Exception("Streaming acceptance Player build failed");
        }
        finally{xr.InitManagerOnStart=init;EditorUtility.SetDirty(xr);AssetDatabase.SaveAssets();}
    }
}
