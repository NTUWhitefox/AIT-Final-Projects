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

        [MenuItem("AIT/Riding/Open First Ride Scene")]
        public static void OpenFirstRide()
            => Open(FirstRidePath);

        [MenuItem("AIT/Riding/Open Urban Vehicle Preview")]
        public static void OpenUrbanPreview()
            => Open(UrbanPreviewPath);

        [MenuItem("AIT/Riding/Open City Corners")]
        public static void OpenCityCorners()
            => Open(CityCornersPath);

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
