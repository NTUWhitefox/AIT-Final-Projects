using System;
using System.IO;
using System.Linq;
using AIT.Riding;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.XR.Management;

public static class GridCityAcceptanceBuild
{
    public static void Run()
    {
        var world = AssetDatabase.LoadAssetAtPath<StreamingWorldDefinition>(GridCityBuilder.WorldPath);
        var xr = XRGeneralSettings.Instance; bool original = xr.InitManagerOnStart;
        xr.InitManagerOnStart = false; EditorUtility.SetDirty(xr); AssetDatabase.SaveAssets();
        try
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            Directory.CreateDirectory("Personal/GridCity/Player");
            var build = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { GridCityBuilder.Bootstrap }.Concat(world.chunks.Select(c => c.scenePath)).ToArray(),
                locationPathName = "Personal/GridCity/Player/GridCity.exe", target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
            File.WriteAllText("Personal/GridCity/build.txt", build.summary.result + "; errors=" + build.summary.totalErrors + "; size=" + build.summary.totalSize);
            if (build.summary.result != BuildResult.Succeeded) throw new Exception("Grid City Player build failed");
        }
        finally { xr.InitManagerOnStart = original; EditorUtility.SetDirty(xr); AssetDatabase.SaveAssets(); }
    }
}
