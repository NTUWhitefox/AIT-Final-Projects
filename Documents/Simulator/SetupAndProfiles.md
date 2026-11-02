# 啟動與遊戲內選單

更新：2026-11-02。上一輪把設定放在桌面 IMGUI／Editor 視窗，無法在頭盔操作，且啟動條件阻擋騎行。此次縮小為一個遊戲內選單；移除 draft、Apply、Play with draft 與 Editor 設定表單。

## 開始騎行

1. 停止 Play，等待 Unity 編譯完成。開啟 `AIT > Riding > Setup and Play Mode`。
2. 選 **Play Desktop** 或 **Play VR**。這個 Editor 視窗只負責選擇顯示模式。
3. 遊戲會先顯示騎行選單。坐好、看向車頭、放開油門，選 **開始／繼續騎行**；第一次會自動以當前坐姿置中。
4. VR：用任一控制器的射線指向按鈕，按下並放開**扳機**選取；左 **X** 暫停並開啟選單。選單放在面前的世界座標，不跟著每次轉頭移動。控制器射線依位置與旋轉追蹤移動。
5. Desktop：以滑鼠點選；**P** 開始／暫停，**F2** 開啟選單。鍵盤油門／煞車／轉向是 **W/S/A/D**，**Space** 停車。

Vive 的油門／煞車／轉向仍為右扳機／左扳機／左搖桿，左 Grip 停車。Gamepad 為 R2／L2／左搖桿，Options 暫停，L1 停車。停車或失去追蹤等停止事件會重新顯示選單及原因，不會自行恢復行進。

VR 仍須有效的頭部追蹤、已連接的騎行輸入、釋放油門與 STOP 才能開始。失敗原因顯示於選單並記錄為 `start_rejected`。不再要求先到別處按校正，也不會因桌面視窗失去焦點擋住 VR 開始。XR display 延遲啟動時會持續更新就緒狀態。

## 本輪保留的調整

打開選單會暫停騎行。調整立即生效，沒有草稿或 Apply。

| 頁籤 | 操作與效果 |
|---|---|
| 騎行 | 開始／繼續、以目前坐姿置中、下一路段、輸入來源 |
| 視角 | 眼高、前後、左右各以 2 cm 調整，立即重新置中。+X 右、+Y 上、+Z 前，數值為相對車體的目標眼位 |
| 舒適 | 車身／鏡頭傾斜模式、移動時周邊遮罩、後照鏡。傾斜和遮罩須回到騎行中觀察 |
| 設定檔 | 載入、儲存目前設定、另存新設定檔、刪除 |
| 操控 | 低速最大轉角、低速轉向上限、倒退速度與「套用低速操控預設」，調整立即生效，使用同一套 profile 儲存 |
| 速度 | 所有地圖共用的騎行速度上限，每次 ±5 km/h，可隨 profile 儲存；舊地圖分段限制不再套用 |

先保留這些操作；其餘參數仍在 `FirstRideSettings.asset` 與 JSON，沒有另做遊戲內編輯介面。舊校正／換段／舒適模式快捷鍵不再執行，避免選單操作又觸發其他設定。

設定檔以 `profile_001` 等名稱另存；用左右箭頭選取。**載入**立即套用並置中，**儲存目前設定**覆寫選中檔案。**刪除設定檔**需再選一次同一按鈕確認，只刪選中 JSON，不改變目前騎行參數。切換頁籤或選中其他檔案會取消刪除確認。

Editor 檔案位於 `VR-Motor-Street-Riding/Personal/RideProfiles/`，Player 位於 `Application.persistentDataPath/RideProfiles/`。沒有儲存的調整只保留在本次 Play，不修改專案預設。設定檔不保存房間追蹤原點；新一次 Play 會重新置中。Desktop 載入 Vive 設定時使用 Keyboard。`profileVersion: 1` 與既有 JSON 相容；錯誤版本、越界或非有限值會拒收。紀錄檔開關不能在錄製途中變更。

## Editor 設定檔管理

`AIT > Riding > Manage Profiles` 開啟專用視窗，選取檔案後可輸入新名稱、重新命名、刪除、重新整理或開啟資料夾。文字改名只在 Editor 做；VR 仍用另存新檔、載入與儲存。

重新命名只移動檔案，保留原始 JSON 位元內容；同名碰撞會拒絕，Windows 僅大小寫變更也支援。刪除有確認提示。這些動作不把內容套用到目前騎行設定。Play 中整理後，重開遊戲選單或切回設定檔頁會更新列表。

AIT/Riding 提供 `Open First Ride Scene`、`Open Urban Vehicle Preview`、`Open City Corners`、`Setup and Play Mode`、`Manage Profiles`。開發測試、驗證與重建產生器留在 `Tools/`，沒有使用者測試工具選單。

倒退按住右 B／手把 ○／鍵盤 R，放開減速停止，不需加油門。開始騎行前須放開油門與倒退鍵。迴轉先降至 5–8 km/h；舊 profile 會保留原轉角，若仍過大，可在「操控」套用低速預設再儲存。新增欄位使用預設值，因此原 `profileVersion: 1` 檔案仍可載入。

## Desktop／VR 選擇的範圍

騎行場景直接按工具列 Play，預設先取消啟動並開啟模式視窗。取消視窗中的每次詢問選項後沿用上次選擇。Sample Scene 等沒有 `RideTestSession` 的場景不經此流程。

這是 Unity Editor Play 的選擇：進 Play 前暫停 XR 自動初始化，由場景依選擇啟動；停止後還原原設定。Desktop 可讓 VBS／SteamVR 繼續開著。Player Build 目前預設 VR，尚無啟動選擇頁。本次不更換套件、runtime 或 loader 清單。

## 輪組修正

`Scooter_shaped_leg_shield_with_front_wheel_clearance` 原本因名稱含 `front_wheel` 而被誤歸為前輪。現在按明確前綴分類；leg shield 固定在車體，前土除／前叉只轉向，輪胎／輪框／碟盤滾動，固定軸排除。

已儲存場景修正 18 個父節點並保留世界座標；啟動時也整理記憶體中舊場景的歸屬。不必為此重建場景。

## 實機確認

開發檢查留在命令列 `Tools/`，不放進 Editor 的使用流程。上一輪與後續檢查紀錄見 [FirstRideVerification.md](FirstRideVerification.md)。本次 profile 管理的編譯、原始內容保留、同名拒絕與 Windows 大小寫改名已在隔離副本確認；沒有操作使用者 Unity 視窗。

實機操作仍為：頭盔看得到選單 → 控制器射線指向開始 → 按下並放開扳機後可以騎行 → 左 X 停車並回到選單。

速度較快時主觀較不暈的回報保留於 [PilotObservations.md](PilotObservations.md)，未據此更動預設速度。


## 控制器 UI 接法

已讀取 `Assets/Scenes/SampleScene.unity` 的 EventSystem 與其 XR Origin／Starter Assets 設定。沿用 `XRUIInputModule`、`TrackedDeviceGraphicRaycaster` 的事件路徑，使用標準 `XRRayInteractor` 與線條視覺；不載入範例的步行／傳送功能。控制器位姿採 Starter Assets 的 pointerPosition／pointerRotation 優先、devicePosition／deviceRotation 備援；UI Press 使用 `{TriggerButton}`／`{Trigger}` 綁定。左右手皆可操作，頭部中央與右 A 不選取 UI。

選單開啟時車輛暫停，射線顯示；騎行時射線停用。XRI 在扳機放開時完成點擊，開始請求會先等 Session.Update 取得新油門樣本；VR 扳機尚未完全放開時最多等兩秒，逾時或操作其他按鈕即取消，避免 UI 的放開事件早於油門歸零而一直拒絕開始。原有放開油門與 STOP 的開始條件仍保留。
