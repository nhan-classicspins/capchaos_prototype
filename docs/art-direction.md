# Cap Chaos — Art Direction & Art Element Document (MVP)

| | |
|---|---|
| **SKU** | `CapsChaos` |
| **Vai trò** | Artist spec, rung 1 của skill `pf-visual-design`. File này **cao hơn** `DesignTokens.cs`: khi hai nơi lệch nhau thì sửa token theo spec, trong cùng một change |
| **Nguồn** | Video 1 (Level 12) và video 2 (Level 13, chai ẩn), ảnh popup Win/Lose. Frame trích ở [`design/refs/`](design/refs/). GDD: [`design/GDD.md`](design/GDD.md) |
| **Trạng thái** | v0.2 — 2026-09-30. Mã hex là **mẫu đo** từ video/ảnh; trường hợp nội suy có ghi chú riêng |

## Các quyết định đã chốt ảnh hưởng tới art

| # | Quyết định |
|---|---|
| D1 | **Camera 3D perspective** cho khối chai, slot và băng chuyền (GDD §10.1) |
| D2 | **Không nhãn hiệu, không logo, không chữ** trên chai, nắp, khay và thùng. Chỉ phân biệt bằng **màu** |
| D3 | Chai **ẩn** dùng **texture cầu vồng**, không dùng màu tối như video |
| D4 | **Toàn bộ art của MVP do team dev/agent tự tạo** (§9). Không chờ artist ngoài |

---

## 1. Art pillars

1. **Đồ chơi bóng bẩy.** Nhựa đúc, bo tròn, bóng, màu bão hoà cao. Không texture bẩn, không photoreal.
2. **Màu chính là gameplay.** Vì D2 không có nhãn phụ trợ, 8 màu phải phân biệt được **trong 0,2 s**:
   khác cả **hue lẫn độ sáng** (§3.2).
3. **Cầu vồng = bí ẩn.** Chỉ chai ẩn được dùng nhiều màu trên một vật thể. Mọi vật thể khác đều đơn sắc.
4. **Nền lùi, vật nổi.** Nền navy-lavender bão hoà thấp; không vật gameplay nào dùng tông nền.
5. **Mọi hành động đều có "pop".** Mọi thứ rời chỗ đều có vòng sáng, nảy scale và âm thanh (§6–8).

---

## 2. Camera & bố cục 3D (D1)

| Thông số | Giá trị đề xuất | Căn cứ |
|---|---|---|
| Projection | Perspective, FOV dọc ~35° | [QS] có thu nhỏ theo chiều sâu |
| Góc nhìn | Pitch ~50° xuống, yaw 0° | [QS] thấy cả đỉnh lẫn thân chai |
| Đơn vị | **1 unit = chiều cao một chai** | Ruler 3D riêng (GDD §10.1) |
| Framing | Khối chai chiếm 5–40 % chiều cao màn hình, tự fit theo `stack.cols/rows/layers` × `view.stackScale` | Level to hay nhỏ đều vừa khung |
| Ánh sáng | 1 directional light (từ trên-trái, ấm nhẹ) + ambient gradient (trời lavender, đất navy) | [QS] bóng đổ về trái-sau |
| Bóng | Soft shadow cho chai, khay trong slot và thùng. Băng chuyền không đổ bóng | [QS] |
| Hậu kỳ | Bloom nhẹ (threshold 1.1, cường độ 0.3) cho highlight nhựa và VFX. Không dùng DOF | |

HUD và popup là uGUI trên các layer có sẵn của rig, không bị ảnh hưởng bởi camera 3D.

---

## 3. Bảng màu

### 3.1 Môi trường (đo từ video)

| Token | Hex | Dùng cho |
|---|---|---|
| `Ground` | `#4D5680` → `#5A6090` (lên trên) | Nền phía sau khối chai |
| `Floor` | `#525781` | Sàn hai bên băng chuyền |
| `SlotBand` | `#474F7A` | Dải chứa slot |
| `SlotEmpty` | `#3E4E78` | Ô slot trống (lõm, bo góc) |
| `Divider` | `#3C4162` | Gờ giữa slot bar và băng chuyền |
| `LaneRail` | `#25272E` | Khung và viền băng chuyền |
| `LaneBeltA` / `LaneBeltB` | `#D5E0EE` / `#CAD2DF` | Sọc ngang xen kẽ trên mặt băng |

### 3.2 Màu gameplay (8 màu, D2)

| Mã | Tên | `Body` | `Shade` | `Cap` (highlight) | Nguồn |
|---|---|---|---|---|---|
| `R` | Hồng-đỏ | `#EF2B86` | `#B81F65` | `#F692C2` | đo |
| `O` | Cam | `#E3761B` | `#A65100` | `#F4A15A` | đo · cap nội suy |
| `B` | Xanh dương | `#0098FB` | `#076DBD` | `#5CC0FF` | đo · cap nội suy |
| `G` | Xanh lá | `#2FA36B` | `#006636` | `#81CBA4` | đo |
| `P` | Tím | `#8E4BF0` | `#5E2BB0` | `#C29BFA` | mới |
| `Y` | Vàng | `#F7C520` | `#B98A00` | `#FFE27A` | mới |
| `C` | Xanh ngọc | `#18C3C8` | `#0D8589` | `#8EE9EC` | mới |
| `N` | Nâu | `#8B5A3A` | `#5A3620` | `#C08A66` | mới |

- Thùng carton dùng `Body` cho mặt và `Shade` cho cạnh.
- Khay dùng `Body` cho thân và `Shade` cho viền.
- **Luật phân biệt** (kiểm bằng `Tools/pf/pixel-probe.py`, skill `pf-uigate`): mọi cặp `Body` cách nhau
  **ΔE76 ≥ 25**. Những cặp dễ lẫn nhất là `R`–`P`, `B`–`C` và `O`–`Y`, nên các cặp này cần **lệch độ sáng
  L\* ≥ 10**.
- **Không** màu gameplay nào được trùng tông nền: navy `#3E4E78`–`#5A6090`.

### 3.3 UI

| Token | Hex | Dùng cho |
|---|---|---|
| `HudPill` | `#2EBCFB` (gờ dưới `#1E8FD0`) | Pill "Level N" |
| `HudButton` | `#0C345B` | Nút tròn Restart / Home |
| `TextOnFill` | `#FFFFFF` + viền `#000000` | Chữ display |
| **Theme Win** | dim `#57935B` @85 % · header `#4B9B8B` · body `#5DB987` · band `#89D851` · nút `#86D64F`→`#75C34C`, gờ `#428C66` | [`refs/09_win_popup.png`](design/refs/09_win_popup.png) |
| **Theme Lose** | dim `#5733A2` @85 % · header `#984BF5` · body `#B34DFF` · band `#D972FE` · nút `#B34FE6`→`#8434AB` | [`refs/10_lose_popup.png`](design/refs/10_lose_popup.png) |
| `TitleGlow` / `TitleEdge` | `#FBC3DE` / `#422B66` | Spotlight và vignette màn Title |
| `LogoFill` / `LogoOutline` | `#46A5FC`→`#66DEFD` / `#1F2A55` | Logo "CAP CHAOS" |

---

## 4. Asset 3D

Quy ước chung:
- Low-poly smooth-shaded, bo góc lớn.
- **Mỗi loại vật thể có một mesh và một material.** Màu được tint bằng `MaterialPropertyBlock` theo mã
  màu. Không làm prefab riêng cho từng màu.
- Dùng GPU instancing hoặc SRP Batcher, vì một level có thể có 200+ chai.

### 4.1 Bottle — chai PET không nắp
- **Dáng:** chai 500 ml, eo thắt, 2 gờ ngang, đáy 5 múi. **Cổ hở, thấy vành ren** [QS]: chai "trần" chờ
  đóng nắp.
- **Tỷ lệ:** cao 1,0 · rộng 0,38.
- **Không nhãn** (D2). Thay nhãn bằng một **băng mờ** (frosted band) quanh thân ở 40–60 % chiều cao,
  cùng hue, sáng hơn khoảng 15 %. Băng này chỉ để thân chai có chi tiết, không phải nhãn.
- **Shader `M_Plastic`:**
  - tint theo `Body`/`Shade`;
  - fresnel rim;
  - `Translucency` 0.15–0.35;
  - specular sắc.
- **Poly:** ≤ 600 tris.
- **Trạng thái:** `Idle` · `Exposed` (rim sáng hơn 15 %, [GĐ]) · `Flying` · `Capped` · `Hidden` (§4.2).

### 4.2 Hidden Bottle — chai ẩn, cầu vồng (D3)
- **Cùng mesh** với Bottle, chỉ khác material: `M_Rainbow`.
- **Texture `T_Rainbow`** (512×512, tạo bằng code):
  - các **dải chéo 30°**, 7 hue: đỏ, cam, vàng, lục, lam, chàm, tím;
  - saturation 0.8, value 0.95;
  - mỗi dải rộng khoảng 1/6 chiều cao chai;
  - rắc vài chấm lấp lánh trắng.
- **Shader:**
  - UV cuộn chậm theo trục dọc (0,15 vòng/s);
  - mỗi chai lệch pha ngẫu nhiên nhưng **tất định**, seed theo toạ độ ô, để cả khối không cuộn đồng bộ;
  - fresnel rim trắng.
- **Đọc được:** người chơi phải thấy ngay "chai này chưa biết màu". Không chai lộ màu nào dùng quá một hue.
- **Reveal** khi chai chạm đất: cầu vồng tan trong 0,25 s, dissolve theo chiều từ dưới lên, sang màu thật,
  kèm VFX `vfx.reveal` (§6).
- Video gốc dùng màu tối `#2F364E`–`#3D4765` ([`refs/07_hidden_stack.jpg`](design/refs/07_hidden_stack.jpg)).
  **Không dùng** tông tối đó (D3).

### 4.3 Cap — nắp
- Nắp vặn có răng cưa ở vành; mặt trên trơn và **không in chữ** (D2); có một vòng gờ đồng tâm để bắt sáng.
- Màu theo `Cap`.
- **Poly:** ≤ 150 tris.

### 4.4 CapTray — khay 4 nắp
- Khay nhựa bo góc lớn, **2×2 hốc**, tỷ lệ 1 : 0,95 [QS].
- **Trạng thái:**
  - trên băng: 4 nắp nằm trong hốc;
  - trong slot: nắp đã bắn đi, các hốc giữ đáy chai ([`refs/05_deadlock.jpg`](design/refs/05_deadlock.jpg)).
- **Poly:** ≤ 400 tris (không tính nắp).

### 4.5 Box — thùng carton
- Thùng cùng màu với khay [QS], có 4 nắp gập riêng, đặt pivot ở bản lề để animation gập được.
- Mặt bên in một **icon trung tính** (ví dụ mũi tên "this side up"), không dùng "⚠" như video. Băng keo trắng.
- **Poly:** ≤ 500 tris.

### 4.6 Conveyor Lane — băng chuyền
- 3 làn song song. Mỗi làn có:
  - **khung `LaneRail`**: hai thanh ray bên và bậc tối ở đầu làn [QS];
  - **mặt băng**: sọc `LaneBeltA/B`, material `M_Belt` với **UV scroll**.
- Khi làn dồn (R7), mặt băng cuộn **đúng một bước khay** cùng lúc với khay trượt lên. Vận tốc UV phải
  khớp vận tốc khay, không để khay trượt trên băng đứng yên.
- Làn **kéo dài ra ngoài mép dưới màn hình** [QS]. Khay mới trượt vào từ ngoài khung hình.
- Khay đầu làn (tap được) có viền sáng mảnh để báo "chạm vào đây".

### 4.7 Môi trường
- **Sàn và nền:** mặt phẳng lớn, gradient dọc `Ground`, không hoạ tiết. Bóng mềm dưới khối chai.
- **Slot bar:** một dải lõm và 3 ô bo góc chìm (`SlotEmpty`). Ô trống **luôn nhìn thấy** (checklist #2).
- **Không** dùng clear colour của camera làm nền. Nền phải là vật thể do SKU vẽ (skill `pf-visual-design`).

---

## 5. UI

- **Font:** display dày, bo tròn, in hoa, **viền đen 8–10 % + bóng cứng phía dưới** [QS]. Đề xuất
  **Lilita One** (tiêu đề, nút) và **Nunito Black** (số), cả hai là OFL. Chỉ 3 cỡ chữ mỗi màn; đo
  `fontSize` trên rig theo skill `pf-visual-design`.

| Phần tử | Mô tả |
|---|---|
| **Nút tròn HUD** | Tròn `HudButton`, icon trắng (↻ Restart, ⌂ Home); đường kính ≈ 9 % chiều rộng màn; vùng chạm ≥ 120 px |
| **Pill Level** | Chữ nhật bo góc `HudPill`, có gờ dưới; text "Level {0}". **Nằm trong safe rect** (video đang dính sát mép trên) |
| **Nút chính** | Bo góc lớn, gradient hai tông, gờ dưới dày 12 %, có một vệt sáng ở nửa trên; khi nhấn thì lún xuống. **Một prefab, đổi theme** |
| **Result popup** (Win / Lose) | **Một prefab** gồm dim toàn màn + panel 3 dải (header / body / band) + nút chính. Theme Win xanh lá, theme Lose tím (§3.3). Tiêu đề "GOOD JOB" / "YOU CAN DO IT"; band hiển thị subtitle (GDD §7) |
| **Màn Title** | Logo "CAP CHAOS" chữ bong bóng xanh cyan, viền navy, bọt trắng; 5 chai **không nhãn** trên sân khấu; spotlight tia hồng-tím; vignette tím ([`refs/01_title.jpg`](design/refs/01_title.jpg)) |

Popup vào bằng cách scale 0,8 → 1 (`OutBack`, 0,25 s) + dim fade 0,15 s; ra bằng cách ngược lại. Dùng
`ScaleTransition` của framework (skill `pf-add-dialog`).

---

## 6. VFX

| Key | Khi nào | Mô tả | Thời lượng |
|---|---|---|---|
| `vfx.bottle_pop` | Chai rời khối | Vòng ring trắng xanh trên sàn + 4–6 hạt [QS] | 0,35 s |
| `vfx.stack_drop` | Chồng chai rơi xuống một tầng | Bụi nhỏ ở chân chai + squash 0,9 rồi nảy lại | 0,18 s |
| `vfx.reveal` | Chai ẩn chạm đất | Cầu vồng tan (dissolve từ dưới lên) + flash trắng + 6 hạt sao mang màu thật | 0,25 s |
| `vfx.cap_fly` | Nắp bắn lên | Đường cong, xoay 360°, vệt mờ [QS] | 0,25 s |
| `vfx.cap_snap` | Nắp chụp vào cổ chai | Chớp trắng + 3 hạt sao | 0,15 s |
| `vfx.box_seal` | Dán băng keo | Băng quét dọc + bụi giấy [QS] | 0,3 s |
| `vfx.box_exit` | Thùng bay đi | Vòng sáng phía sau + tia cyan [QS] | 0,4 s |
| `vfx.win_confetti` | Thắng | Confetti theo màu của level | 1,5 s |
| `vfx.deadlock_shake` | Trước popup Lose | 3 khay trong slot rung + tint đỏ nhẹ | 0,5 s |

---

## 7. Animation

| Chuyển động | Ease | Thời lượng |
|---|---|---|
| Khay: làn → slot | `OutBack` (vọt 8 %) | 0,20 s |
| Băng chuyền dồn một bước (khay và UV đồng bộ) | `OutCubic` | 0,20 s |
| Chai: khối → khay | Bezier, đỉnh cao hơn điểm đầu 1,2 lần chiều cao chai | 0,30 s |
| Khoảng cách giữa các chai | — | 0,12 s |
| Chồng chai rơi một tầng | `InQuad` rơi rồi `OutBack` nảy | 0,18 s |
| Reveal | `Linear` dissolve | 0,25 s |
| Thùng úp / gập 4 nắp / bay đi | `OutBounce` / `InOutQuad` / `InBack` | 0,25 / 0,25 / 0,40 s |
| Khay bị từ chối | Lắc ngang 3 lần | 0,25 s |
| Nút UI khi nhấn | Scale 1 → 0,92 → 1 | 0,12 s |

Chạy bằng **LitMotion**. Mọi thời lượng đọc từ config key (GDD §9), không literal trong View.

---

## 8. Âm thanh

| Key | Mô tả |
|---|---|
| `sfx.tray_place` | "Tách" nhựa |
| `sfx.belt_step` | Tiếng băng chuyền chạy ngắn, cơ khí nhẹ |
| `sfx.bottle_whoosh` | Gió ngắn, pitch tăng dần theo từng chai trong một khay |
| `sfx.cap_click` | "Cạch" vặn nắp. Đây là **âm chữ ký**: giòn, thoả mãn |
| `sfx.stack_drop` | "Cộp" nhựa rỗng |
| `sfx.reveal` | Chuông "ting" lấp lánh |
| `sfx.box_close` / `sfx.box_ship` | Carton gập + băng keo "rẹt" / "vút" |
| `sfx.win` / `sfx.lose` / `sfx.denied` | Stinger ngắn |
| `music.gameplay` | Loop lo-fi vui, 100–110 BPM |

---

## 9. ⭐ Pipeline tự tạo art cho MVP (D4)

**Nguyên tắc:**
- Mọi asset MVP do team dev hoặc agent tạo, **không chờ artist**.
- Ưu tiên **sinh bằng code, tất định** (machine zone): chạy lại ra đúng kết quả, diff được, đổi token là
  toàn bộ asset cập nhật theo.
- Chỉ dùng sinh ảnh bằng AI cho những thứ **mang tính minh hoạ**.

| Loại | Cách tạo | Công cụ | Output |
|---|---|---|---|
| **Mesh 3D gameplay** (Bottle, Cap, CapTray, Box + flaps, Lane, Slot, Floor) | **Editor generator C# dựng mesh thủ tục**. Bottle và Cap: tiện tròn (lathe) từ profile spline, 24 segment. Tray: rounded box trừ 4 hốc. Box: 6 mặt + 4 nắp là child có pivot bản lề | `Assets/CapsChaos/Editor/ArtGen/*MeshGenerator.cs`, chạy qua MCP `script-execute` | `Content/Art/Meshes/*.asset` |
| **Prefab** lắp từ mesh + material | Editor script chạy trên **editor sống** qua MCP (luật #12: không sửa YAML tay) | Unity-MCP | `Content/Art/Prefabs/` |
| **Texture thủ tục** (`T_Rainbow`, `T_BeltStripes`, `T_Cardboard`, `T_Tape`) | Sinh bằng code: gradient, dải, noise | `Editor/ArtGen/TextureGenerator.cs` | `Content/Art/Textures/` |
| **Material / shader** (`M_Plastic`, `M_Rainbow` + dissolve, `M_Belt` UV scroll, `M_Cardboard`) | Shader Graph URP; tham số lấy từ token | Unity | `Content/Art/Materials/` |
| **UI chrome** (panel 3 dải, pill, nút tròn, nút chính, icon ↻ ⌂) | **Editor ArtGenerator từ `DesignTokens`**: vẽ hình bo góc, gờ và bóng bằng code | Skill `pf-asset-gen`, bảng đầu, và `pf-visual-design` | `Content/UI/` |
| **Minh hoạ** (logo "CAP CHAOS", nền sân khấu + spotlight của màn Title) | Sinh ảnh AI, dùng ảnh tham chiếu từ `refs/`, variants → chọn → **user duyệt** | `Tools/pf/asset-gen.py` (skill `pf-asset-gen`); file `.gen.json` ở trạng thái `pending-user-review` | `Tools/AssetGen/assets/` (**ngoài** `Assets/`) |
| **VFX** | Particle System dựng bằng Editor script, màu từ token | Unity | `Content/Art/VFX/` |
| **SFX / music** | Sinh bằng AI (Coplay MCP `generate_sfx` / `generate_music`), hoặc thư viện CC0 | Coplay MCP | `Content/Audio/` |
| **Font** | **Không sinh được.** Dùng font OFL (Lilita One, Nunito) tải từ Google Fonts, cần user đồng ý tải | TMP Font Asset Creator | `Content/Fonts/` |

**Phương án dự phòng:** nếu mesh thủ tục của chai trông quá thô, dùng Coplay `generate_3d_model_from_text`
để lấy mesh tham khảo, rồi **retopo và đặt lại pivot** trước khi dùng. Cách này chỉ dành cho prop trang trí.
Mesh gameplay cần kiểm soát poly và pivot, nên vẫn giữ generator.

**Cổng chất lượng trước khi coi asset là xong:**
1. Checklist 8 điểm của luật 17 (skill `pf-visual-design`).
2. **Visual check trong play mode**: capture 1080×1920 ở các trạng thái đã đặt tên trước: bắt đầu level,
   đang nạp chai, chai ẩn, lúc reveal, popup Win, popup Lose.
3. `pixel-probe.py` kiểm ΔE của 8 màu (§3.2).
4. Asset do AI sinh phải được user duyệt, chuyển `.gen.json` sang trạng thái approved.

---

## 10. Danh sách asset & ưu tiên

| P | Asset | Cách tạo (§9) |
|---|---|---|
| P0 | Bottle mesh + `M_Plastic` | Generator |
| P0 | `M_Rainbow` + `T_Rainbow` + dissolve reveal | Generator + Shader Graph |
| P0 | Cap, CapTray, Box (rig nắp), Lane + `M_Belt`, Slot bar, Floor | Generator |
| P0 | Nút HUD, pill, nút chính, Result popup (2 theme) | UI ArtGenerator |
| P0 | Font TMP SDF (Lilita One, Nunito Black) | OFL |
| P1 | VFX `bottle_pop`, `reveal`, `cap_fly`, `box_exit`, `stack_drop` | Particle + script |
| P1 | Logo "CAP CHAOS", nền màn Title | asset-gen, user duyệt |
| P1 | SFX P0: `cap_click`, `tray_place`, `box_close`, `reveal` | Coplay / CC0 |
| P2 | VFX còn lại, SFX còn lại, music | |

**Đường dẫn:** `Assets/CapsChaos/Content/Art/{Meshes,Prefabs,Materials,Textures,VFX}/`,
`Content/UI/`, `Content/Audio/`, `Content/Fonts/`. Generator nằm ở `Assets/CapsChaos/Editor/ArtGen/`.
**Đặt tên:** `PascalCase`; mesh không có hậu tố màu, vì màu là tint.

---

## 11. Design tokens

Mọi hex, spacing và cỡ chữ ở trên nằm trong **một** file `Assets/CapsChaos/Views/DesignTokens.cs`
(static class). Không literal nào nằm trong View (luật #17). Các nhóm token:

- **Môi trường:** `Ground`, `Floor`, `SlotBand`, `SlotEmpty`, `Divider`, `LaneRail`, `LaneBelt{A,B}`
- **Màu gameplay:** hàm `Flavor(code).{Body, Shade, Cap}`, tra theo mã màu, cho 8 mã
- **Rainbow:** `Rainbow.{Hues[7], Saturation, Value, ScrollSpeed, BandAngle}`
- **Theme popup:** `ResultTheme.Win` / `ResultTheme.Lose` `{Dim, Header, Body, Band, ButtonTop, ButtonBottom, ButtonLip}`
- **HUD:** `HudPill`, `HudButton`, `TextOnFill`
- **Spacing và chữ:** `Unit` + các biểu thức spacing; `FontLabel`, `FontValue`, `FontTitle` (đo trên rig)

Các generator ở §9 **đọc cùng token này**, nên đổi một hex là cả mesh tint, texture và UI chrome cập nhật
theo.

---

## Design Notes (các điểm lệch so với video, cố ý)

1. Bỏ toàn bộ nhãn, logo và chữ trên vật thể gameplay; chỉ dùng màu (D2).
2. Chai ẩn dùng cầu vồng thay cho màu tối (D3).
3. Icon "⚠" trên thùng đổi sang icon trung tính.
4. Pill Level dời vào trong safe rect.
5. Dải band của popup Win/Lose hiển thị subtitle thay vì để trống.
6. Thêm 4 màu `P Y C N` ngoài 4 màu trong video, để có đủ độ khó cho các level sau.
7. [GĐ] Highlight trạng thái `Exposed`; chờ playtest xác nhận có cần hay không.
