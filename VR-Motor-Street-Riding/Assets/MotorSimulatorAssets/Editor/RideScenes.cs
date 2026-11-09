using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace AIT.Riding.Editor
{
    public static class RideScenes
    {
        public const string FirstRidePath = "Assets/Scenes/FirstRideTest.unity";
        public const string UrbanPreviewPath = "Assets/Scenes/UrbanVehiclePreview.unity";
        public const string CityCornersPath = "Assets/Scenes/CityCorners.unity";
        public const string StreamingBlockoutPath = "Assets/Scenes/StreamingBlockout.unity";
        public const string GridCityPath = "Assets/Scenes/GridCity.unity";

        [MenuItem("AIT/Riding/Open First Ride Scene")]
        public static void OpenFirstRide()
            => Open(FirstRidePath);

        [MenuItem("AIT/Riding/Open Urban Vehicle Preview")]
        public static void OpenUrbanPreview()
            => Open(UrbanPreviewPath);

        [MenuItem("AIT/Riding/Open City Corners")]
        public static void OpenCityCorners()
            => Open(CityCornersPath);

        [MenuItem("AIT/Riding/Open Streaming Blockout")]
        public static void OpenStreamingBlockout()
            => Open(StreamingBlockoutPath);

        [MenuItem("AIT/Riding/Open Grid City")]
        public static void OpenGridCity()
            => Open(GridCityPath);

        static void Open(string path)
        {
            if (!File.Exists(path))
            {
                EditorUtility.DisplayDialog("找不到騎行場景", "請確認專案包含 " + path, "確定");
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(path);
        }
    }
}
