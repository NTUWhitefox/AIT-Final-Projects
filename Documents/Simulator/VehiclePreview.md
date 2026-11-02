# 車輛與簡單街道試騎

2026-11-02。這一版先判斷汽車與現有素材在騎乘視角中的比例、外觀、遮擋與移動感。尚未完成台北街景的建築立面、行人、轉彎車流或完整號誌控制。

## 開啟與試騎

1. Unity 選 `AIT > Riding > Open Urban Vehicle Preview`。
2. 按 Play，選 Desktop 或 VR。遊戲內沿用原本選單與騎行控制；VR 用控制器射線與扳機選取，左 X 開選單。Desktop 用滑鼠，F2 開選單。
3. 選第一個直線路段並開始。前方銀色 Car #26 以 12 km/h 直行，在紅燈前停車、綠燈起步；路旁有另一輛 Car #26、一輛紅色比較車、三輛 TDAL 機車。新內容只在第一個直線路段。
4. 開選單或停止騎乘時，前車與燈號時鐘一起暫停。重新進入 Play 可重播整段；目前遊戲內「重設路段」只重設騎乘者，尚未重播前車事件。

原本 `FirstRideTest.unity` 保留；新場景是 `Assets/Scenes/UrbanVehiclePreview.unity`。騎乘車仍是原本已可轉向的機車，未換成 TDAL 掃描車。

請先看：靠近汽車時輪胎、車門、玻璃是否可信；前車停等時距離感是否合理；經過停車與建築時的速度感、暈眩及頭盔中是否穩定。尚未量測實機 PCVR 幀率，離線畫面不能代替頭盔試騎。

## 素材實際結果

尺寸為 Unity 匯入後 renderer bounds，含後照鏡等外凸部分；面數為 triangles，不能直接與網站未說明的「面數」比較。

| 素材 | 實際結果 | 目前可用範圍 |
|---|---|---|
| Realistic Mobile Car #26 | 約長 5.11、寬 2.12、高 1.47 m；保留四輪、四門、方向盤與原 LOD 結構；5 種 URP 材質 | 近距離外觀、直線前車、路側遮擋；已另渲染確認左前門可從鉸鏈開啟，但場景尚未安排開門事件 |
| TDAL `scooter_d.FBX` | 約長 1.90、寬 0.77、高 1.25 m；69,680 triangles；7 個依材質分組的 mesh | 已配對 diffuse/normal、先放路旁。前後輪合併，車殼與機械部件跨全車，需拆件及設 pivot 才能接騎行轉向 |
| `TraffixLight.FBX` 的 C 款 | 原檔是四款橫向排開，不是一支巨大號誌；C 款高約 5.5 m、15,976 triangles | 衍生 mesh 分成桿／燈殼／紅／黃／綠五槽；兩面共六個燈面，可切燈色 |
| CC0 紅色四門轎車 | 約長 4.84、寬 2.18、高 1.55 m；132,082 triangles、10 材質，無現成 LOD | 靜態比較車。造型、近看細節及面數效率不如 Car #26，不作目前主要車款 |
| 建築與道路 | 簡單建物量體、窗面、雨棚、人行道、交叉道路、斑馬線與停止線 | 先供車輛比例與移動觀看；尚無台灣立面貼圖、招牌、完整騎樓及四向路口配置 |

Car #26 按各 LODGroup 相同層級加總約為 56,687／35,662／15,269／3,748／328 triangles。試騎回報原切換過近、車體跳形與輪胎明顯多角化，因此目前預覽適配 prefab **固定使用 LOD0**：停用自動 LOD，僅啟用各組最高細節 renderer。原始廠商 prefab 與 LOD mesh 不改。這是少量車輛的視覺基準，尚未驗收大量車流的 PCVR 效能；後續需選出形狀可接受的低階模型與切換距離，不能只把跳形改成淡入淡出便視為完成。

騎乘車已加入低速倒退與新的低速轉向限制；操作見 [FirstRideTest.md](FirstRideTest.md)。遊戲內「操控」可直接調整並儲存 profile；迴轉先降至 5–8 km/h。

TDAL 原 ZIP 保留。只取出七組 diffuse／normal，設定為 2D 貼圖、正確 normal 類型、色彩空間與最大 2048；原檔仍保留 4K。此次沒有重建 glossiness／IOR／reflection 的完整來源材質。騎乘視角近看可見掃描貼圖模糊、儀表固定、鏡面烘焙，不能直接當成動態儀表或後視鏡。

## 沒貼圖的紅綠燈如何改色

已做好 `Assets/MotorSimulatorAssets/CityPreviewGenerated/TDAL_TrafficLight_C.prefab`，可拖入場景使用。展開其 `Signal`，Mesh Renderer 的材質槽依序是：

1. `Signal Pole`：金屬桿，改材質的 Base Map 顏色；Metallic 調金屬感，Smoothness 調反光。
2. `Signal Housing`：黑色燈殼，同樣調 Base Map。
3. `Signal Red`、`Signal Amber`、`Signal Green`：燈面，Emission 已啟用。

選 prefab 根物件的 `Preview Traffic Signal`，關掉 `Cycle In Play`，改 `Colour` 即可看固定紅／黃／綠。燈面的亮暗由此元件覆寫材質屬性，所以只改燈面材質顏色未必能看到預期結果；桿與燈殼可直接改材質。要保留不同配色，先複製材質再指派到該物件。

原 FBX 每款只有一個材質槽，直接拖一個材質會把整支都染色。這次利用可辨識的獨立幾何島建立衍生 submesh，原 FBX 未改；其他模型若部件完全黏合，仍可能要進 Blender 選面、分配材質，不能保證都能自動分色。

## 行為與調整範圍

移動車根物件 `Preview Road Vehicle` 可調速度、加速／減速、停止位置及終點。現在只沿世界 +Z 直行，輪子依路程旋轉，紅／黃燈停車、綠燈放行；到終點後停止，沒有瞬移循環。碰撞外形是近似方盒，供既有騎乘碰觸停止使用，沒有汽車動力學、車流決策或車體撞擊變形。車燈保留素材 emissive 外觀，尚未連動煞車燈與方向燈。

號誌時制先設紅 20、綠 12、黃 3 秒，只服務這條預覽路徑，未做全紅、對向／橫向互鎖、人行號誌及偵測。這些不是台北任何路口的實測時制。建物與號誌亦未組成可供正式研究的完整路口。

## 來源與驗收紀錄

- Car #26：使用者匯入 `Models/RealisticMobileCars - Pro3DModels`，沿用 Unity Asset Store 授權；URP 適配另放 `CityPreviewGenerated`，未覆寫廠商原材質與 prefab。[商店來源](https://assetstore.unity.com/packages/3d/vehicles/land/realistic-mobile-car-26-demo-305319)。
- TDAL 機車、ZIP 與號誌：使用者提供於 `Models/Test`；確切下載條目及作者需補記，不能僅靠檔名認定為先前候選頁面或沿用其授權標示。
- 紅色比較車：[Innerscene Red four-door sedan](https://www.innerscene.com/tools/library/3d-parts/red-four-door-sedan-fbx-a39faa80)，來源頁標示 CC0；來源、下載雜湊與修改見素材旁 `LICENSE.md`。
- 離線渲染、原生 Unity Play 驗收與匯入記錄放本機 `Personal/RepairValidationProject/Personal/VehiclePreview/`，不加入 Editor 選單。未操作使用者 Unity 視窗，未執行實體頭盔試騎。
