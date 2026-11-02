using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;

namespace AIT.Riding.Editor
{
    [InitializeOnLoad]
    public static class FirstRideLaunch
    {
        const string Selected = "AIT.Ride.Launch.Selected";
        const string Managed = "AIT.Ride.Launch.Managed", OriginalInit = "AIT.Ride.Launch.OriginalInit";
        static string PreferenceKey => "AIT.Ride.AskBeforePlay." + Application.dataPath;
        static string LastModeKey => "AIT.Ride.LastMode." + Application.dataPath;
        public static bool AskBeforePlay
        {
            get => EditorPrefs.GetBool(PreferenceKey, true);
            set => EditorPrefs.SetBool(PreferenceKey, value);
        }

        static FirstRideLaunch()
        {
            EditorApplication.playModeStateChanged += Changed;
            if (!EditorApplication.isPlayingOrWillChangePlaymode) Restore();
        }

        public static void Prepare(RideDisplayMode mode)
        {
            SessionState.SetInt(Selected, (int)mode);
            EditorPrefs.SetInt(LastModeKey, (int)mode);
        }

        public static bool HasRideScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; ++i)
                foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects())
                    if (root.GetComponentInChildren<RideTestSession>(true)) return true;
            return false;
        }

        static void Changed(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode) { Restore(); return; }
            if (state != PlayModeStateChange.ExitingEditMode || !HasRideScene()) return;
            if (SessionState.GetInt(Selected, -1) < 0)
            {
                if (AskBeforePlay)
                {
                    EditorApplication.isPlaying = false;
                    EditorApplication.delayCall += () => FirstRideSetupWindow.ShowWindow();
                    return;
                }
                SessionState.SetInt(Selected, EditorPrefs.GetInt(LastModeKey, (int)RideDisplayMode.Desktop));
            }
            var xr = XRGeneralSettings.Instance;
            if (!xr) { Debug.LogError("No XR settings instance; launch choice could not be applied."); EditorApplication.isPlaying = false; return; }
            // The scene owns manual startup in BOTH modes. Suppress automatic
            // initialization before AfterAssembliesLoaded (not after XR starts).
            SessionState.SetBool(OriginalInit, xr.InitManagerOnStart);
            SessionState.SetBool(Managed, true);
            xr.InitManagerOnStart = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void TransferChoice()
        {
            if (!SessionState.GetBool(Managed, false)) return;
            RideLaunchRequest.Mode = (RideDisplayMode)SessionState.GetInt(Selected, (int)RideDisplayMode.Desktop);
        }

        static void Restore()
        {
            if (SessionState.GetBool(Managed, false))
            {
                var xr = XRGeneralSettings.Instance;
                if (xr) xr.InitManagerOnStart = SessionState.GetBool(OriginalInit, true);
            }
            SessionState.SetBool(Managed, false);
            SessionState.SetInt(Selected, -1);
        }
    }

    public sealed class FirstRideSetupWindow : EditorWindow
    {
        string message;
        [MenuItem("AIT/Riding/Setup and Play Mode")]
        public static FirstRideSetupWindow ShowWindow()
        {
            var window = GetWindow<FirstRideSetupWindow>("Ride Play Mode");
            window.minSize = new Vector2(420, 190); window.Show(); return window;
        }
        void OnGUI()
        {
            if (EditorApplication.isPlaying)
            {
                GUILayout.Label("Settings are in the in-game menu.", EditorStyles.boldLabel);
                GUILayout.Label("VR: left X opens the menu; point the controller and pull the trigger.");
                GUILayout.Label("Desktop: F2 opens the menu; use the mouse.");
                return;
            }
            GUILayout.Label("Choose the display for this Play session.", EditorStyles.boldLabel);
            FirstRideLaunch.AskBeforePlay = GUILayout.Toggle(FirstRideLaunch.AskBeforePlay,
                "Choose Desktop / VR when pressing toolbar Play");
            GUILayout.Space(15);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Play Desktop", GUILayout.Height(45))) Play(RideDisplayMode.Desktop);
            if (GUILayout.Button("Play VR", GUILayout.Height(45))) Play(RideDisplayMode.VR);
            GUILayout.EndHorizontal();
            GUILayout.Label(message ?? "Adjust and save settings in the in-game menu after starting.");
        }
        void Play(RideDisplayMode mode)
        {
            try
            {
                if (!FirstRideLaunch.HasRideScene()) RideScenes.OpenFirstRide();
                if (!FirstRideLaunch.HasRideScene()) return;
                FirstRideLaunch.Prepare(mode);
                EditorApplication.isPlaying = true;
            }
            catch (Exception e) { message = e.Message; }
        }
    }
}
