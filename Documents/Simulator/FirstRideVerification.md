# 第一版驗證紀錄

日期：2026-11-01。環境：Unity 6000.3.21f1、Windows、URP 17.3、Input System 1.20、OpenXR 1.17.1。未變更專案套件版本。

## 已完成

- Unity 編譯成功。
- 29 項車輛/場景/預覽檢查通過，包括速度上限、煞車優先、不倒退、靜止不轉向、左右對稱、橫向加速度與角速度限制、90/180 Hz 積分比較、deadzone、無效輸入拒收、單位比例、shader、把手/輪子實際分件與中心線投影。
- 19 項桌面 Play Mode 檢查通過：校正、明確開始、鍵盤加速、速度上限、位置前進、煞車、釋放油門後重新開始、轉向、車身與鏡頭傾角、兩鏡面實際畫面內容、vignette、停止、未接外部 adapter 時無法開始、紀錄輸出。
- 回讀最新的 `telemetry.csv`、`events.csv`、`frames.csv` 與 `session.json`：數值有限、CSV 欄數正常、時間非負且單調、設定與校正快照可解析、評分與正常結束事件存在。
- 視覺檢查了第三人稱模型、騎乘視角、道路全景、Play 畫面與左右鏡面。車輛原始尺寸約 1.896 × 0.814 × 1.168 m。
- 原先開啟的 Testing 場景在 Play 檢查結束後已還原；試驗資料與擷取圖皆位於 git 排除的 `Personal/`。

完整自動報告與圖片：`VR-Motor-Street-Riding/Personal/FirstRideValidation/`。最新桌面試驗：`20261101-111619-900-ecb28a`。這些資料是虛擬鍵盤軟體驗證，不能當成受試者資料；較早的同日資料夾也包含開發除錯紀錄。

## 桌面效能觀察

在 818 × 328 Game View、XR 子系統停止的自動測試中，共記錄 6,935 個 frame；略去前 10 frame 後，CPU frame interval 中位數 1.44 ms、95 百分位 1.96 ms。測試包含截圖/鏡面 readback 與編輯器干擾，最大值有明顯尖峰；這不是正式 benchmark，更不等於頭盔內幀率或 motion-to-photon 延遲。

先前串流 runtime 在沒有有效頭部追蹤時仍回報 XR display active，接管相機投影並出現約 50 ms 的 frame interval。桌面檢查現在暫停 XR 子系統後才測試，未修改序列化 XR 設定。正常手動 Play 仍走原有 XR 初始化流程；鏡面使用一般 URP 相機排程。

## 實機待驗收

- Focus 3 內左右眼顯示、頭部六自由度與每個按鍵對應。
- VBS/SteamVR 實際呈現率、reprojection、串流延遲與斷線/移除頭盔的停止行為。
- 座高、眼位、把手與鏡面位置，及鏡面左右方向。
- 各段路線的操作性、速度感與不適；用 [FirstRideTest.md](FirstRideTest.md) 的逐段流程記錄。
- 實體 PS4、未來專用控制系統與 Quest 3 尚未測試。

目前可進行現場初次試騎；尚未驗證車輛物理、實車操作等效性或暈眩改善效果。

## 第一輪回饋修訂（2026-11-01）

本輪遵照使用者偏好，不使用 computer use、不操作 Unity 視窗。以既有 Unity 編譯器 response files 完成 Runtime 與 Editor 編譯，純 managed C# 執行 8 個零件分類回歸案例並讀取 171 個 OBJ 分件；另檢查已儲存場景全部分件的父節點。18 個父節點修正且保留世界座標，後續檢查為零待修正。

新設定 UI、JSON profile 的 Unity 序列化/互動、VR/Desktop 初始化與重連、外觀檢查相機，尚未經本輪實際 Play/視覺驗收。上方 29/19 項與效能數字屬上一版的驗證紀錄，不能視為這次新功能已通過。新的手動驗收步驟與 Native JSON 檢查入口見 [SetupAndProfiles.md](SetupAndProfiles.md)。


## 遊戲內選單修復（2026-11-01，頭部選取版本，已被取代）

上一輪設定介面未在頭盔內呈現；Desktop 又因沒有明確校正而不能開始。實際 VR 紀錄顯示稍後已有頭部追蹤與控制輸入，顯示子系統的早期未就緒狀態卻被永久保留。這些是此次修復的觸發原因。

本輪移除 Editor／IMGUI 設定表單與 draft 流程，改為同一個 UGUI 選單：Desktop overlay、VR world-space。首次開始自動置中，XR display 可延遲恢復就緒，VR 不要求桌面視窗焦點；選單顯示開始失敗原因。舊暫停面板停用，避免遮住新選單。具名 JSON 新增刪除。

以 `python Tools/check_riding_recovery.py` 在本機忽略的獨立 Unity 副本執行 native engine 檢查，沒有控制使用者視窗或注入 OS 按鍵：

- 選單預設顯示；實際按鈕事件可以開始並自動置中；開選單會暫停。
- 眼高 +2 cm 立即套用，實際相機到達目標眼位。
- 設定檔另存、載入、覆寫與刪除；第一次刪除選取保留檔案，第二次移除；路徑越界被拒收。只在隔離副本建立／刪除測試檔。
- 世界座標選單的實際 Start 按鈕可被視線射線命中，反向射線不命中；以虛擬 Gamepad South 驗證選取與按住不連發。
- 模擬 XR display 從 unavailable 轉為 running，無須重啟即可恢復；模擬有效頭部狀態下，桌面失焦不擋 VR 開始。
- 實際 Desktop Play：虛擬滑鼠透過 InputSystemUIInputModule／EventSystem 點擊開始，虛擬鍵盤油門使 Update／FixedUpdate 加速並移動，STOP 清速並重開選單，釋放後可重新開始。
- Runtime／Editor 編譯、8 個分件分類案例與 171 個分件的場景父節點檢查通過。

報告：`VR-Motor-Street-Riding/Personal/RepairValidationProject/Personal/recovery-results.txt` 與 `recovery-play-results.txt`。無繪圖 Play 測試在隔離副本停用 render pipeline／相機／鏡面繪圖，並允許虛擬輸入在無 Game View 焦點下送入；未更動工作專案的輸入設定。Unity Editor Search 啟動時曾輸出其 SearchDatabase 的 ArgumentOutOfRangeException；上述檢查仍完成，未將它算成 simulator 驗證通過項目。

這次沒有驗證實體頭盔畫面、字體可讀性、A/X 實鍵、真實串流初始化／重連或暈眩。VR 狀態轉換檢查使用模擬狀態，不等於完成 Focus 3 驗收。此段記錄的頭部選取版本已被取代；目前控制器 UI 的驗證見下段。最新操作見 [SetupAndProfiles.md](SetupAndProfiles.md)。


## 控制器射線／扳機修正（2026-11-01，目前版本）

依使用者指出的 Sample Scene 操作修正上一版自行加入的頭部指向與 A 鍵選取。讀取 SampleScene 的 EventSystem、XR Origin 相依 prefab 與 Starter Assets 的 XRI Default Input Actions，改用 `XRUIInputModule`、`TrackedDeviceGraphicRaycaster`、標準 `XRRayInteractor`／`XRInteractorLineVisual`。沿用 pointer pose 優先、device pose 備援與 TriggerButton／Trigger 的綁定。沒有加入 locomotion、傳送或手部互動功能。

新的隔離 Unity native 檢查涵蓋：

- VR 世界座標 Canvas 使用 tracked-device raycaster；左右手射線位於同一校正 tracking origin，使用扳機綁定。
- 實際 Play 中，以虛擬 XRController 的位姿驅動 TrackedPoseDriver，經 XRRayInteractor 命中設定檔按鈕，再經 XRUIInputModule 的按下／放開事件建立恰好一個設定檔；按住不點擊或連發。測試不直接呼叫該按鈕的 onClick。
- UI 射線只在選單開啟時啟用，騎行時關閉。
- 開始按鈕先排程，等待 Session.Update 取到放開油門的樣本，再開始。VR 扳機尚未完全放開時短暫等待，兩秒未放開則取消；操作其他按鈕也取消等待，避免稍後意外開始。
- Desktop 滑鼠選單、開始／加速移動／STOP／重新開始，以及設定檔存取刪除的檢查保留。

頭部方向與 A 鍵不再選取 UI。上段頭部指向版本的測試僅為歷史紀錄，不代表目前的控制器路徑驗證。

虛擬控制器以極小的位置變化持續送樣，讓 XRI 在動態建立 Canvas 之後更新 hover；沒有 OS 輸入注入、實體 XR 初始化或使用者桌面操作。實體頭盔的射線位置、線條可見性、字體、扳機實鍵與串流仍待現場核對。


## Editor 工具整理（2026-11-02）

新增 Manage Profiles，重新命名保留 JSON 原始內容、拒絕覆寫同名檔案，支援 Windows 大小寫變更；在隔離 Unity 檢查通過。Runtime／Editor 編譯及既有開始／停車／控制器 UI 路徑仍通過。尚未視覺操作使用者 Editor 視窗。

FirstRideValidation 與 FirstRideSceneBuilder 移至 Tools/UnityValidation，FirstRidePlaySmoke 已移除。主專案 Editor 僅保留使用者用的場景開啟、Play 模式選擇及 profile 管理；上方歷史測試選單入口不再存在。命令列檢查只把必要 helper 複製到忽略的測試副本。

城市素材幾何／動畫清單是素材檢視結果，不是城市情境已完成；見 CityAssetsAndScenarios.md。
