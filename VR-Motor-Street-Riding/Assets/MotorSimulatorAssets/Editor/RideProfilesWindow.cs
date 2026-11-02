using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AIT.Riding.Editor
{
    public sealed class RideProfilesWindow : EditorWindow
    {
        string[] profiles = Array.Empty<string>();
        [SerializeField] string selected, newName;
        Vector2 scroll;
        string error, feedback;

        [MenuItem("AIT/Riding/Manage Profiles")]
        public static void ShowWindow()
        {
            var window = GetWindow<RideProfilesWindow>("Ride Profiles");
            window.minSize = new Vector2(460, 360); window.Show();
        }
        void OnEnable() => Refresh();
        void OnFocus() { Refresh(); Repaint(); }
        void Refresh()
        {
            try
            {
                profiles = RideSettingsProfiles.Names();
                if (Array.IndexOf(profiles, selected) < 0) Select(profiles.Length > 0 ? profiles[0] : null);
                error = null;
            }
            catch (Exception e) { error = e.Message; }
        }
        void Select(string name) { selected = newName = name; feedback = null; error = null; }
        void OnGUI()
        {
            EditorGUILayout.LabelField("騎行設定檔", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("在 VR 儲存設定後，可在這裡改名及整理。改名會保留檔案內容；不改動目前騎行設定。", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("重新整理")) Refresh();
                if (GUILayout.Button("開啟資料夾"))
                {
                    Directory.CreateDirectory(RideSettingsProfiles.DirectoryPath);
                    EditorUtility.RevealInFinder(RideSettingsProfiles.DirectoryPath);
                }
            }
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(110));
            if (profiles.Length == 0) EditorGUILayout.LabelField("尚無設定檔。請先在遊戲內另存設定。");
            foreach (string name in profiles)
                if (GUILayout.Toggle(selected == name, name, "Button") && selected != name) Select(name);
            EditorGUILayout.EndScrollView();
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(selected)))
            {
                newName = EditorGUILayout.TextField("名稱", newName ?? "");
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(newName) || newName == selected))
                        if (GUILayout.Button("重新命名")) Attempt(() =>
                        {
                            RideSettingsProfiles.Rename(selected, newName);
                            Select(newName.Trim()); Refresh(); feedback = "已重新命名。";
                        });
                    if (GUILayout.Button("刪除"))
                    {
                        if (EditorUtility.DisplayDialog("刪除設定檔", "確定刪除「" + selected + "」？", "刪除", "取消"))
                            Attempt(() => { RideSettingsProfiles.Delete(selected); selected = null; Refresh(); feedback = "已刪除。"; });
                    }
                }
            }
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Error);
            else if (feedback != null) EditorGUILayout.HelpBox(feedback, MessageType.Info);
            EditorGUILayout.LabelField("Play 中整理檔案後，重新開啟遊戲選單或切回設定檔頁即可更新列表。", EditorStyles.wordWrappedMiniLabel);
        }
        void Attempt(Action action)
        {
            try { action(); error = null; }
            catch (Exception e) { error = e.Message; }
        }
    }
}
