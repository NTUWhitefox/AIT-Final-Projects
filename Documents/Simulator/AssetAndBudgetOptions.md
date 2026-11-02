# 速克達素材與預算選項

查詢日期：2026-11-01。標價可能隨商店變動；付費項目皆未購買。總預算暫按 NT$3,000 分配，先保留 sensor、固定零件與線材的空間。

2026-11-02 新增城市建築、交通物件、汽車、行人與手部候選，見 [CityAssetsAndScenarios.md](CityAssetsAndScenarios.md)。包含台灣在地 TDAL 素材與實際匯入數據；以下保留原速克達／硬體選項。

## 車輛候選

| 候選 | 網頁標價 / 授權標示 | 優點 | 代價與判斷 |
|---|---|---|---|
| [Innerscene Urban motor scooter](https://www.innerscene.com/tools/library/3d-parts/urban-motor-scooter-9dd945f9) | 免費，CC0 | 本次已下載；實際檔案 136,504 triangles、13 材質，有把手、儀表、鏡面、輪胎與分件，約 1.896 × 0.814 × 1.168 m | 復古外觀，與台灣常見現代 125/150 有差距；多 draw calls，須實測 PCVR 效能。初版採用，不能當特定車款尺寸驗證 |
| [Honda PCX / DevanirGrau](https://sketchfab.com/3d-models/honda-pcx-cb4b2c153e204b88bd3848af1157eb47) | 免費下載，頁面標示 CC Attribution | 外觀較接近目標現代速克達 | 網頁列 463.9k triangles，需登入取得下載檔並確認來源、貼圖與分件；未下載、未驗證 cockpit 品質。需要減面、LOD 與清楚署名 |
| [Low poly Scooter / pasquill3d](https://www.cgtrader.com/3d-models/vehicle/motorcycle/low-poly-scooter-e24184a9-3f17-4383-9388-be2d6c086a00) | 一次購買 US$30；Royalty Free License | 網頁列 5,103 polygons，PBR、FBX/OBJ/Blend、4K 貼圖，較有利於即時渲染 | 仍需確認儀表背面、把手近看、各零件 pivot 與授權條款；不是 Unity URP 現成 prefab。值得放在付費優先比較名單 |
| [Retro Vespa / annaandrei](https://sketchfab.com/3d-models/retro-vespa-scooter-d2841db11a87440b990a595c3a3f46d1) | 免費下載，CC Attribution | 網頁列 125.8k triangles，有細節 | 外觀偏可愛復古，與目標車型不符；不因免費就優先替換目前模型 |
| [Electric scooter / artgeorg](https://www.turbosquid.com/3d-models/3d-model-electric-scooter-2331667) | 免費，Standard License | 網頁列 PBR、FBX/OBJ/Blend，貼圖最高 4K | 網頁列 303,332 polygons，不是 triangles；偏高，近距離與授權需下載後確認。未下載，先列為候選 |
| [Honda PCX / Erkut Yurtlu](https://www.turbosquid.com/3d-models/scooter-moped-polygonal-model-1307258) | US$139，頁面標示 Editorial Uses Only | 具體車型、細節多 | 網頁列逾百萬 polygons，成本及授權用途皆不理想；列作超預算參考，現階段不優先 |

不建議把一般卡通 vehicle pack 當作已完成真實性需求。最值得花錢的是騎乘者近距離看得到的把手、儀表、煞車拉桿與鏡面，不只是第三人稱外觀。任何候選都須看實際檔案，不能由展示圖判定 VR cockpit 合格。

## 已匯入素材揭露

原作者 Innerscene Corp；下載自上述官方頁面與其 `/api/library/urban-motor-scooter-9dd945f9/download`。原始 GLB 內嵌 copyright 也標示 CC0 1.0 Universal。原件保存為 `urban-motor-scooter.glb.bytes`，SHA-256：

`0c8fe3b8fd30f3418d0406bc40a2f871fe12fdf2f8a41208460259e5455e9a40`

本次改動：格式轉為 OBJ、轉換座標/UV、貼圖對應 URP Lit、隱藏側柱與靜態儀表指針/刻度、加數位速度顯示、建立輪子與把手動畫 pivot、加後視攝影畫面。未自行重建機車外形。URP 未重現 GLB 所有光學材質 extension，尤其燈殼透射；不宣稱與原渲染完全一致。

## NT$3,000 的分配草案

以下是工程預留額，不是已逐件報價的 BOM；機械固定費用依現場用實車或獨立把手決定。已有 PC、HMD、controller、可借座位/車架不列入新增成本。

| 項目 | 初步預留 NT$ | 備註 |
|---|---:|---|
| 機車模型 | 0 | 先用已取得 CC0；付費模型另比較，避免先壓縮硬體預算 |
| 微控制器與 USB 通訊 | 250 | ESP32 等級；也可依現有系統改用具 USB HID 的板子 |
| 轉向角度感測 | 150 | AS5600 模組＋磁鐵的估額，含固定誤差與備品考量 |
| 油門 / 煞車感測與連接件 | 500 | 視既有把手採電位計、Hall 或其他機構；尚未選型 |
| 左右兩路觸覺、驅動與供電 | 500 | 驅動元件、馬達、接頭一起預留；振動與回正力矩是不同需求 |
| 夾具、固定、轉接與保護 | 700 | 依實體車/把手調整，通常比 sensor 單價更容易超支 |
| 線材與實作耗材 | 300 | USB、接頭、線材、板件 |
| 備品與調整空間 | 600 | 運費、重做支架或感測器替換 |
| 合計 | 3,000 | 付費模型或額外控制件可由此重新分配，沒有先行採購 |

查到的價格錨點：[iCShop ESP32 分類](https://www.icshop.com.tw/categories/esp32)有相容 NodeMCU-32S NT$169、DevKit V1 約 NT$199–207；[DigiKey 的 Grove AS5600](https://www.digikey.tw/zh/products/detail/seeed-technology-co-ltd/101020692/10451869)單件 NT$235；[露天一個通用 AS5600 模組賣場](https://www.ruten.com.tw/item/22548596378762/)列 NT$69；[iCShop 震動馬達分類](https://www.icshop.com.tw/categories/vibration-motors)列 M5Stack N20 模組 NT$165。它們是不同板件/供應來源，不能只比裸 sensor 名稱視為同品；未確認庫存、磁鐵、驅動與總運費。

[AS5600 原廠資料](https://look.ams-osram.com/m/7059eac7531a86fd/original/AS5600-DS000365.pdf)說明它量測軸上徑向磁化磁鐵的絕對角度。12-bit 是輸出解析度，不能直接當成整套把手感測精度。轉向角量測也不等於轉向力矩量測；如果研究要的是反向操舵與回正力，需要另選機械與控制方案。

## 選材驗收

1. 坐姿視角下把手、儀表和鏡子是否完整，比例能否與現場眼位匹配。
2. 需要動畫的把手、輪子、拉桿、油門是否可分離，pivot 是否可校正。
3. Unity URP 是否正常，材質數、透明表面、貼圖記憶體與鏡面額外渲染是否能維持目標更新率。
4. 署名、修改、專案分享與發布的權利是否清楚；付費原件不要直接放進公開 repository。
5. 先做同場景、同路線、同設定的 cockpit 比較，再決定是否值得換素材。
