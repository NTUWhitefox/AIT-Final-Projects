using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace AIT.Riding
{
    // One menu for both displays. VR uses the SampleScene XRI UI event path.
    [DefaultExecutionOrder(200)]
    public sealed class RideSetupUI : MonoBehaviour
    {
        public bool IsOpen { get; private set; }
        public Canvas MenuCanvas { get; private set; }
        public IReadOnlyList<Button> Buttons => buttons;
        readonly List<Button> buttons = new List<Button>();
        RideTestSession session;
        RectTransform panel, pageRoot;
        Font font;
        Text status;
        GameObject eventSystem;
        public RideControllerUI ControllerUI { get; private set; }
        bool menuWasHeld, anchored, startRequested;
        int page, selected;
        string[] profiles = Array.Empty<string>();
        string message = "", pendingDelete;
        float nextStatus, startRequestedAt;

        public void Configure(RideTestSession owner)
        {
            session = owner;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft JhengHei", "Arial" }, 32);
            var root = new GameObject("Ride in-game menu", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.layer = 2;
            root.transform.SetParent(session.vehicleRoot, false);
            MenuCanvas = root.GetComponent<Canvas>();
            MenuCanvas.worldCamera = session.headCamera;
            MenuCanvas.sortingOrder = 50;
            var scaler = root.GetComponent<CanvasScaler>();
            if (session.Display.Mode == RideDisplayMode.VR)
            {
                MenuCanvas.renderMode = RenderMode.WorldSpace;
                ((RectTransform)root.transform).sizeDelta = new Vector2(940, 780);
                root.transform.localScale = Vector3.one * .0012f;
                scaler.dynamicPixelsPerUnit = 10;
                root.GetComponent<GraphicRaycaster>().enabled = false;
                root.AddComponent<TrackedDeviceGraphicRaycaster>();
                eventSystem = new GameObject("Ride XR menu input", typeof(EventSystem), typeof(XRUIInputModule));
                eventSystem.transform.SetParent(session.trackingOrigin, false);
                eventSystem.GetComponent<XRUIInputModule>().uiCamera = session.headCamera;
                ControllerUI = eventSystem.AddComponent<RideControllerUI>();
                ControllerUI.Configure();
            }
            else
            {
                MenuCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1000, 820);
                scaler.matchWidthOrHeight = 1;
                if (!FindFirstObjectByType<EventSystem>())
                {
                    eventSystem = new GameObject("Ride menu input", typeof(EventSystem), typeof(InputSystemUIInputModule));
                    eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
                }
            }
            panel = Rect("Panel", root.transform, Vector2.zero, new Vector2(940, 780));
            panel.gameObject.AddComponent<Image>().color = new Color(.035f, .05f, .07f, .98f);
            Label(panel, "騎行選單", new Vector2(0, 345), new Vector2(860, 60), 38);
            status = Label(panel, "", new Vector2(0, 260), new Vector2(860, 100), 25);
            pageRoot = Rect("Controls", panel, new Vector2(0, -40), new Vector2(900, 470));
            Label(panel, session.Display.Mode == RideDisplayMode.VR
                ? "控制器射線指向按鈕，按扳機選取。左 X 開啟／暫停。"
                : "滑鼠點選。P 開始／暫停，F2 開啟選單。", new Vector2(0, -345), new Vector2(880, 70), 26);
            RefreshProfiles();
            Open();
        }

        public void Open()
        {
            if (!session || !MenuCanvas) return;
            if (session.Running) { session.Pause("Opened ride menu"); return; }
            startRequested = false;
            RefreshProfiles();
            IsOpen = true; page = 0; pendingDelete = null; anchored = false;
            MenuCanvas.gameObject.SetActive(true);
            BuildPage(); PositionPanel();
            if (ControllerUI) ControllerUI.SetMenuOpen(true);
        }
        public void Close()
        {
            IsOpen = false; startRequested = false;
            if (ControllerUI) ControllerUI.SetMenuOpen(false);
            if (MenuCanvas) MenuCanvas.gameObject.SetActive(false);
        }
        void PositionPanel()
        {
            if (session.Display.Mode != RideDisplayMode.VR) return;
            var camera = session.headCamera.transform;
            MenuCanvas.transform.SetPositionAndRotation(camera.position + camera.forward * 1.3f, camera.rotation);
            anchored = session.HeadTracked;
        }
        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame) Open();
            if (session.Display.Mode == RideDisplayMode.VR)
            {
                var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
                bool menuHeld = left.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool pressed) && pressed;
                if (menuHeld && !menuWasHeld && !IsOpen) Open();
                menuWasHeld = menuHeld;
            }
            if (IsOpen)
            {
                if (!anchored && session.HeadTracked) PositionPanel();
                if (Time.unscaledTime >= nextStatus)
                {
                    nextStatus = Time.unscaledTime + .1f;
                    status.text = $"{session.Display.Status} | {session.Settings.inputSource}: {(session.CurrentInput.connected ? "已連接" : "未連接")}\n{session.Status}\n{message}";
                }
            }
        }
        void LateUpdate()
        {
            // XRI clicks on trigger RELEASE. EventSystem runs before Session.Update;
            // wait until the session has sampled that release before checking throttle.
            if (!startRequested) return;
            if ((session.CurrentInput.throttle > .05f || session.CurrentInput.reverse > .05f) && !session.CurrentInput.stopHeld)
            {
                message = "請完全放開油門／扳機與倒退鍵以開始騎行。";
                if (Time.realtimeSinceStartup - startRequestedAt < 2) return;
                message = "開始已取消；請放開油門與倒退鍵後再選開始。";
                startRequested = false; return;
            }
            startRequested = false; message = "";
            session.TryStart();
        }

        void BuildPage()
        {
            for (int i = pageRoot.childCount - 1; i >= 0; i--)
            { var child = pageRoot.GetChild(i); child.gameObject.SetActive(false); Release(child.gameObject); }
            buttons.Clear();
            string[] tabs = { "騎行", "視角", "舒適", "設定檔", "操控", "速度" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int target = i;
                AddButton("Tab" + i, tabs[i], new Vector2(-375 + i * 150, 200), new Vector2(138, 60),
                    () => { page = target; pendingDelete = null; message = ""; BuildPage(); });
            }
            if (page == 0)
            {
                AddButton("Start", "開始／繼續騎行", new Vector2(0, 100), new Vector2(650, 70), () => { startRequested = true; startRequestedAt = Time.realtimeSinceStartup; });
                AddButton("Recenter", "以目前坐姿置中", new Vector2(0, 10), new Vector2(650, 65), () => { session.Calibrate(); PositionPanel(); });
                AddButton("NextSection", session.course.stages.Length==1?"回到路線起點":"切換下一路段", new Vector2(0, -75), new Vector2(650, 65),
                    () => { session.ResetToStage((session.StageIndex + 1) % session.course.stages.Length); PositionPanel(); BuildPage(); });
                AddButton("Input", "輸入：" + session.Settings.inputSource, new Vector2(0, -160), new Vector2(650, 65), () =>
                {
                    int next = ((int)session.Settings.inputSource + 1) % 4;
                    if (session.Display.Mode == RideDisplayMode.Desktop && next == 0) next = 1;
                    session.Settings.inputSource = (RideInputSource)next;
                    session.Emit("settings", "Input: " + session.Settings.inputSource); BuildPage();
                });
                Label(pageRoot, "先放開油門與倒退鍵再開始。\n倒退按住：右 B／手把 ○／鍵盤 R；放開即停。", new Vector2(0, -250), new Vector2(850, 85), 25);
            }
            else if (page == 1)
            {
                EyeRow("EyeHeight", "眼睛高度", 1, 90);
                EyeRow("EyeForward", "眼睛前後", 2, -10);
                EyeRow("EyeSide", "眼睛左右", 0, -110);
                Label(pageRoot, "每次調整 2 cm，立即套用至目前坐姿。", new Vector2(0, -235), new Vector2(850, 70), 27);
            }
            else if (page == 2)
            {
                AddButton("Comfort", "傾斜：" + new[] { "車身傾斜，視角保持水平", "車身與視角皆直立", "車身傾斜，視角小幅傾斜" }[(int)session.Settings.comfortMode], new Vector2(0, 90), new Vector2(850, 65),
                    () => { session.CycleComfort(); BuildPage(); });
                AddButton("Vignette", "移動時縮小周邊視野：" + (session.Settings.vignetteEnabled ? "開" : "關"), new Vector2(0, -10), new Vector2(850, 65),
                    () => { session.Settings.vignetteEnabled = !session.Settings.vignetteEnabled; Changed("Vignette"); });
                AddButton("Mirrors", "後照鏡：" + (session.Settings.mirrorsEnabled ? "開" : "關"), new Vector2(0, -110), new Vector2(850, 65),
                    () => { session.Settings.mirrorsEnabled = !session.Settings.mirrorsEnabled; Changed("Mirrors"); });
                Label(pageRoot, "傾斜與周邊視野效果要在騎行中觀察。", new Vector2(0, -235), new Vector2(850, 70), 27);
            }
            else if (page == 4)
            {
                ControlRow("SteeringLock", "低速最大轉角", session.Settings.lowSpeedSteeringDegrees, "°", 100,
                    d => session.Settings.lowSpeedSteeringDegrees = Mathf.Clamp(session.Settings.lowSpeedSteeringDegrees + d * 2, 1, 45));
                ControlRow("LowYaw", "低速轉向上限", session.Settings.lowSpeedYawDegreesPerSecond, "°/s", 5,
                    d => session.Settings.lowSpeedYawDegreesPerSecond = Mathf.Clamp(session.Settings.lowSpeedYawDegreesPerSecond + d * 5, 1, 180));
                ControlRow("ReverseSpeed", "倒退速度上限", session.Settings.maximumReverseSpeedKph, "km/h", -90,
                    d => session.Settings.maximumReverseSpeedKph = Mathf.Clamp(session.Settings.maximumReverseSpeedKph + d * .5f, .5f, 6));
                AddButton("ManeuverDefaults", "套用低速操控預設", new Vector2(0, -195), new Vector2(650, 60), () =>
                {
                    session.Settings.lowSpeedSteeringDegrees = 38; session.Settings.lowSpeedYawDegreesPerSecond = 75;
                    session.Settings.steeringTransitionSpeedKph = 25; session.Settings.maximumReverseSpeedKph = 3;
                    Changed("Maneuver defaults");
                });
                Label(pageRoot, "迴轉先降至 5–8 km/h；調整可在設定檔分頁儲存。", new Vector2(0, -245), new Vector2(850, 45), 25);
            }
            else if (page == 5)
            {
                ControlRow("SpeedLimit", "騎行速度上限", session.Settings.maximumSpeedKph, "km/h", 90,
                    d => session.Settings.maximumSpeedKph = Mathf.Clamp(session.Settings.maximumSpeedKph + d * 5, 1, 120));
                Label(pageRoot, "此設定套用所有路段，不受地圖速限限制。\n每次調整 5 km/h；可在設定檔分頁儲存。\n提高上限後仍需依實際彎道減速。", new Vector2(0, -80), new Vector2(850, 180), 27);
            }
            else { RefreshProfiles(); BuildProfiles(); }
        }
        void ControlRow(string id, string title, float value, string unit, float y, Action<float> change)
        {
            Label(pageRoot, $"{title}：{value:0.#} {unit}", new Vector2(0, y), new Vector2(590, 65), 28);
            AddButton(id + "Minus", "−", new Vector2(-365, y), new Vector2(110, 65), () => { change(-1); Changed(id); });
            AddButton(id + "Plus", "+", new Vector2(365, y), new Vector2(110, 65), () => { change(1); Changed(id); });
        }
        void EyeRow(string id, string title, int axis, float y)
        {
            Label(pageRoot, $"{title}：{session.Settings.seatedEyePosition[axis]:0.00} m", new Vector2(0, y), new Vector2(540, 65), 30);
            AddButton(id + "Minus", "−", new Vector2(-355, y), new Vector2(130, 65), () => ChangeEye(axis, -.02f));
            AddButton(id + "Plus", "+", new Vector2(355, y), new Vector2(130, 65), () => ChangeEye(axis, .02f));
        }
        void ChangeEye(int axis, float amount)
        {
            var changed = Instantiate(session.Settings);
            try
            {
                var eye = changed.seatedEyePosition; eye[axis] += amount; changed.seatedEyePosition = eye;
                session.ApplySettings(changed); session.Calibrate(); PositionPanel();
            }
            finally { Release(changed); }
            BuildPage();
        }
        void Changed(string detail) { session.Emit("settings", detail); BuildPage(); }
        void RefreshProfiles() { profiles = RideSettingsProfiles.Names(); selected = Mathf.Clamp(selected, 0, Mathf.Max(0, profiles.Length - 1)); }
        void BuildProfiles()
        {
            string name = profiles.Length > 0 ? profiles[selected] : null;
            Label(pageRoot, name ?? "尚無設定檔", new Vector2(0, 105), new Vector2(600, 65), 30);
            AddButton("PreviousProfile", "〈", new Vector2(-365, 105), new Vector2(100, 65), () => SelectProfile(-1)).interactable = profiles.Length > 1;
            AddButton("NextProfile", "〉", new Vector2(365, 105), new Vector2(100, 65), () => SelectProfile(1)).interactable = profiles.Length > 1;
            AddButton("LoadProfile", "載入", new Vector2(-220, 10), new Vector2(380, 65), () =>
            {
                var loaded = RideSettingsProfiles.Load(name);
                try { session.ApplySettings(loaded); session.Calibrate(); PositionPanel(); message = "已載入 " + name; }
                finally { Release(loaded); }
                BuildPage();
            }).interactable = name != null;
            AddButton("SaveProfile", "儲存目前設定", new Vector2(220, 10), new Vector2(380, 65), () =>
            { RideSettingsProfiles.Save(name, session.Settings); message = "已儲存 " + name; }).interactable = name != null;
            AddButton("NewProfile", "另存新設定檔", new Vector2(-220, -90), new Vector2(380, 65), () =>
            {
                int index = 1; string fresh;
                do { fresh = "profile_" + (index++).ToString("000"); } while (Array.IndexOf(profiles, fresh) >= 0);
                RideSettingsProfiles.Save(fresh, session.Settings); RefreshProfiles(); selected = Array.IndexOf(profiles, fresh);
                pendingDelete = null; message = "已儲存 " + fresh; BuildPage();
            });
            AddButton("DeleteProfile", pendingDelete == name && name != null ? "再按一次確認刪除" : "刪除設定檔", new Vector2(220, -90), new Vector2(380, 65), () =>
            {
                if (pendingDelete != name) { pendingDelete = name; BuildPage(); return; }
                RideSettingsProfiles.Delete(name); pendingDelete = null; RefreshProfiles(); message = "已刪除 " + name; BuildPage();
            }).interactable = name != null;
            Label(pageRoot, "載入會立即套用；刪除檔案不會改變目前設定。\n選單內的調整，儲存後才會保留至下次測試。", new Vector2(0, -235), new Vector2(850, 100), 26);
        }
        void SelectProfile(int delta)
        { selected = (selected + delta + profiles.Length) % profiles.Length; pendingDelete = null; BuildPage(); }
        Button AddButton(string id, string title, Vector2 position, Vector2 size, Action action)
        {
            var rect = Rect(id, pageRoot, position, size);
            rect.gameObject.AddComponent<Image>().color = new Color(.14f, .21f, .29f);
            var button = rect.gameObject.AddComponent<Button>();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => { if (id != "Start") startRequested = false; try { action(); } catch (Exception e) { message = e.Message; Debug.LogWarning(e.Message); } });
            Label(rect, title, Vector2.zero, size - new Vector2(12, 4), 28);
            buttons.Add(button); return button;
        }
        Text Label(Transform parent, string value, Vector2 position, Vector2 size, int fontSize)
        {
            var text = Rect("Label", parent, position, size).gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = fontSize; text.text = value; text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
            return text;
        }
        static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 2; rect.SetParent(parent, false); rect.sizeDelta = size; rect.anchoredPosition = position;
            return rect;
        }
        static void Release(UnityEngine.Object value)
        { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        void OnDestroy()
        {
            if (MenuCanvas) Release(MenuCanvas.gameObject);
            if (eventSystem) Release(eventSystem);
            if (font) Release(font);
        }
    }
}
