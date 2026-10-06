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
| Framing | Băng oval chiếm 5–40 % chiều cao màn hình, tự fit theo `loop.rows/pickRows` (`LoopMaxWidth × LoopMaxDepth`) | Level to hay nhỏ đều vừa khung |
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
| `R` | Đỏ | `#E8314D` | `#A22236` | `#F498A6` | texture `Items_01` |
| `O` | Cam | `#F97610` | `#AE530B` | `#FCBA88` | texture `Items_06` |
| `B` | Xanh dương | `#2977F7` | `#1D53AD` | `#94BBFB` | texture `Items_02` |
| `G` | Xanh lá | `#64E917` | `#46A310` | `#B2F48B` | texture `Items_03` |
| `P` | Tím | `#9141D8` | `#662E97` | `#C8A0EC` | texture `Items_07` |
| `Y` | Vàng | `#FBC40F` | `#B0890B` | `#FDE287` | texture `Items_04` |
| `C` | Xanh ngọc | `#1AD1ED` | `#1292A6` | `#8DE8F6` | texture `Items_08` |
| `N` | **Hồng** (dữ liệu vẫn gọi `Brown`) | `#FE79C0` | `#B25586` | `#FFBCE0` | texture `Items_05` |

- Sửa 2026-10-02: `Body` = màu trung bình của texture item tương ứng, `Shade` ≈ 70 %, `Cap` ≈ nửa đường tới trắng; để khay
  và thùng khớp với vật đang chạy trên băng.
- ~~Thùng carton~~ đã bỏ (2026-10-02): khay đầy tự đậy nắp rồi bay đi (§4.4).
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
| **Theme Lose** | dim `#5733A2` @85 % · header `#984BF5` · body `#B34DFF` · band `#D972FE` · nút `#B34FE6`→`#8434AB`, gờ `#5E2A86` (gờ là nội suy) | [`refs/10_lose_popup.png`](design/refs/10_lose_popup.png) |
| `TitleGlow` / `TitleEdge` | `#FBC3DE` / `#422B66` | Spotlight và vignette màn Title |
| `LogoFill` / `LogoOutline` | `#46A5FC`→`#66DEFD` / `#1F2A55` | Logo "CAP CHAOS" |

---

## 4. Asset 3D

Quy ước chung:
- Low-poly smooth-shaded, bo góc lớn.
- **Mỗi loại vật thể có một mesh và một material.** Màu được tint bằng `MaterialPropertyBlock` theo mã
  màu. Không làm prefab riêng cho từng màu.
- Dùng GPU instancing hoặc SRP Batcher, vì một level có thể có 200+ chai.

### 4.1 Item trên băng (thay chai, 2026-10-02)
- Băng chở **item** thay cho chai: mỗi màu một prefab `Content/Art/Prefabs/Items/Items_NN` (model + material + texture đã
  có màu sẵn, không qua `TokenTint`). Bảng ghép màu ↔ prefab nằm ở `GameplayScreen` (theo màu texture, xem §3.2).
- Mỗi item được bọc trong một gốc rỗng: model được scale để bề ngang lớn nhất = `ItemSize` (0,38), căn giữa, đáy ở y = 0,
  nên prefab có scale/pivot nào cũng đứng đúng chỗ trên băng và trong hốc khay.
- Chai cũ đã **xoá** (2026-10-02): ArtGenerator không còn sinh `Bottle.asset` / `Bottle.prefab` / `M_PlasticFrosted`
  và xoá chúng nếu còn sót. Phần dưới đây chỉ giữ để tham khảo.

### 4.1a Bottle — chai PET không nắp (đã xoá)
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

### 4.2 ~~Chai ẩn (cầu vồng)~~ — bỏ 2026-10-02
Chai ẩn đã bỏ cùng khối chai (GDD D3, D6). Prefab `BottleHidden`, `M_Rainbow`, `T_Rainbow` không còn được sinh.

### 4.2b Băng oval và hàng chờ (2026-10-02)
- Băng oval dựng lúc chạy (`LoopBeltView`) từ hai mặt cắt: **mặt băng** dùng `M_Belt` + token `LaneBelt` (sọc cuộn
  theo pha băng, 1 chu kỳ sọc = 1 hàng chai), **gờ hai bên** dùng `M_Matte` + token `LaneRail`. Cùng vật liệu với băng
  khay, nên cả bàn đọc như một dây chuyền.
- Chai đứng **hàng 4**, khoảng cách 0,40 ngang × 0,42 dọc (chai rộng 0,38): đông như đám đông trong video tham chiếu.
- **Hình vòng băng** (2026-10-02) là một **đa giác lồi bo góc** (`LoopPath`), khai trong level `view.loopShape`: preset
  `oval` (mặc định), `circle`, `triangle` (đỉnh hướng lên, như ảnh tham chiếu), hoặc tự khai các đỉnh + bán kính bo. Các
  hàng chai **cách đều** trên cả vòng: hình được scale để chu vi = `rows` × bước hàng; bước hàng không dưới `LoopRowPitch`,
  và đủ lớn để ở góc gắt nhất track trong vẫn cách `LoopInnerPitch` (0,30 < chai 0,38: chồng nhẹ ở mép trong là chấp nhận
  được — product owner) quanh một lỗ tối thiểu `LoopMinHole`. Vùng lấy nằm giữa cạnh trước. Cả vòng co cho vừa khung
  `LoopMaxWidth × LoopMaxDepth` và luôn **nằm giữa** theo chiều ngang.
- Hàng chờ là một **làn nhập**: `mergeAt` là lối vào, chỗ đầu hàng đứng; làn hạ xuống oval cách đó `FeederEntryRows` hàng
  (hạ nguồn; bên trái là ảnh gương nên ngược lại). Làn đi vào từ ngoài màn hình (thẳng), rồi một đường cong Bézier rẽ vào oval và hạ tiếp tuyến
  với băng qua một đoạn ngắn (`FeederMergeRun`), tạo thành một miệng nhập. Các hàng chờ xếp thành **cột đi xuống từ mép
  trên**, trái → phải đúng thứ tự trên băng: từ 2 hàng trở lên thì chia **đều và đối xứng** trên bề ngang safe rect (cách mép
  `FeederEdgeMargin`); chỉ 1 hàng thì đi xuống ngay phía trên điểm nhập (`FeederSwing`, `FeederTopLead`). Hàng nhập ở khúc
  cua trái được vẽ là **ảnh gương** của hàng nhập ở vị trí đối xứng bên khúc cua phải: đi dọc cạnh trái và hợp vào cạnh
  trái (băng ở đó đi lên, tức hợp ngược chiều; product owner chọn đối xứng, 2026-10-02). Gờ mở theo hình học thật: gờ trong của hàng
  chờ dừng ở chỗ chạm mặt oval; gờ ngoài của hàng chờ dừng trước đường trượt của chai từ đầu hàng xuống oval; gờ ngoài của
  oval mở trên đoạn mặt hàng chờ phủ lên, và (khi gờ hàng chờ bị cắt) tới quá điểm nhập `FeederLandRows` hàng, để không gờ
  nào chắn chỗ thả chai.
- Cây object: `Loop/Belt` (kèm các gờ của nó), `Loop/Bottles`, `Loop/FeederN/{Belt (kèm gờ), Queue}`. Mặt băng hàng chờ thấp hơn một chút (`FeederBeltSink`) để mặt oval nằm trên. Đầu hàng chờ đứng ở chỗ hai băng
  vừa chạm; mỗi chai của hàng vừa nhập **tự đi tới điểm đích riêng** của nó (ô của nó trên oval, đang chạy theo băng): lần lượt từng chai (chai gần ô đích nhất đi trước, cách nhau `GameFeel.MergeStagger`), mỗi chai trượt mượt trong `GameFeel.MergeSeconds` (ease `MergeEase`) và xoay dần theo hướng băng; hàng chờ phía sau tiến lên đều theo nhịp băng, không khựng.
- Camera orthographic (ADR-001 §7): khung hình do `ViewHeight` (11,6) quyết định, tính theo chiều cao safe rect.

### 4.3 Cap — nắp (đã bỏ 2026-10-02)
- Product owner bỏ nắp: không còn mesh `Cap.asset`, prefab `Cap.prefab`, nắp trên khay hay hiệu ứng nắp bay lên chụp cổ
  chai. Chai bay thẳng vào hốc khay. Prefab chai sẽ được thay sau. Token `FlavorCap` / màu `Cap` còn giữ (enum được
  serialize), nhưng không prefab nào dùng.

### 4.4 Container — khay (thay CapTray, 2026-10-02)
- Khay giờ là prefab có sẵn `Content/Art/Prefabs/Containers/Container_S` (`Box` + `BoxLid`, decal "?" `Mystery`,
  4 điểm neo `ItemAnchors` 01–04). `ContainerView` chỉ cache **phần của chính nó** (2 renderer, decal, 4 neo) và nhận
  material từ ngoài; **danh sách material nằm một chỗ** trong asset dùng chung `ContainerPalette` (Addressable
  `ContainerPalette`, group Shared, tải một lần bởi `ContainerPaletteProvider` ở Root): màu thường `M_Container_NN` (cùng
  số với `Items_NN`), khay **khoá** (R18) `M_Container_Locked`, khay **ẩn** (R17) `M_Container_Hidden` + bật decal "?"
  (khoá ưu tiên hơn ẩn). Ổ khoá có số đếm (`TrayLock`) vẫn hiện trên khay khoá.
- Mỗi khay là một gốc rỗng chứa model, scale cho bề ngang `ContainerSize` (0,9), đáy đặt trên băng; vùng bấm là hit box
  của gốc. Item được thu thập bay tới **điểm neo của ô nó** (01–04 = ô 0–3) và **ở lại làm con của neo đó**; kích thước
  `ItemInTray` (0,85) và độ cao vòng cung được bù theo tỉ lệ của neo.
- Gắn và điền tham chiếu bằng menu **CapsChaos → Art → Wire Containers** (tạo/cập nhật `ContainerPalette`, đánh dấu
  Addressable, gắn `ContainerView` vào prefab).
- Trên băng và khi đang nhận item, nắp được cất đi (khay mở); khay đầy thì đậy nắp rồi bay đi (§4.5).
- Khay cũ `CapTray` đã **xoá** (2026-10-02): ArtGenerator không còn sinh nó và xoá nó nếu còn sót. Mô tả cũ giữ để tham khảo:

### 4.4a CapTray — khay 4 nắp (đã xoá)
- Khay nhựa bo góc lớn, **2×2 hốc**, tỷ lệ 1 : 0,95 [QS].
- **Trạng thái:**
  - trên băng và trong slot: 4 hốc trống, khay tô màu của nó; chai bay vào thì các hốc giữ đáy chai
    ([`refs/05_deadlock.jpg`](design/refs/05_deadlock.jpg)).
- **Poly:** ≤ 400 tris.
- **Khay đặc biệt** (GDD R17–R19, chủ SKU quyết 2026-10-01; màu chọn theo rule 17, token trong `DesignTokens`):
  - **Ẩn:** khay tô flavor `Mystery` (slate `#5F6A8F`), cộng decal **"?"** trắng viền tối
    (`T_MysteryMark`, child `Mystery` trong `CapTray.prefab`) nằm phẳng trên khay. Lộ màu thì đổi tint, tắt decal, khay nảy.
  - **Khoá:** `TrayLock.prefab` có quai thép `#D4DBEA` + thân than `#2F3554` (2 quad cùng khung, nghiêng 60° về
    camera), số lượt màu trắng (`TextTint` key `White`) in trên thân. Đây là nội dung tạm, chủ SKU sẽ thay.
  - **Nối:** `TrayLink.prefab` là dây thừng `#EBD5A4`, có sọc xoắn (`T_Rope`) và viền nâu `#5A4630`, vắt vòng cung qua
    nắp của 2 khay. Dây bám theo khay mỗi frame và mảnh dần rồi biến mất khi khay bay.

### 4.5 Box — thùng carton (đã bỏ 2026-10-02)
- Không còn thùng carton: khay `Container_S` đầy thì **đậy nắp `BoxLid`** (nắp rơi từ trên xuống `LidDrop`, nghiêng
  `LidTilt`, nảy `OutBounce` trong `LidClose` 0,3 s) rồi **bay đi theo đúng motion cũ của thùng** (`InBack` 0,4 s tới
  `BoxExitX/Y`, thu còn 60 %, nghiêng −15°). ArtGenerator không còn sinh `Box.prefab` và xoá nó nếu còn sót. Mô tả cũ:
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
| Khay đầy: đậy nắp / bay đi | `OutBounce` / `InBack` | 0,30 / 0,40 s |
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

### 9.1 Trạng thái triển khai (2026-09-30): asset 3D P0 đã sinh

| Thành phần | File |
|---|---|
| Generator (menu **CapsChaos → Art → Generate 3D Assets**; headless: `Game.Editor.ArtGenerator.GenerateAll()`) | `Assets/CapsChaos/Editor/ArtGen/{ArtGenerator, ArtShapes, MeshBuilder, ProceduralTextures}.cs` |
| Preview (menu **CapsChaos → Art → Render Preview**, render trong preview scene, không đụng scene đang mở) | `Assets/CapsChaos/Editor/ArtGen/ArtPreview.cs` · ảnh [`design/refs/11_art_mvp_layout.png`](design/refs/11_art_mvp_layout.png), [`12_art_mvp_closeup.png`](design/refs/12_art_mvp_closeup.png) |
| Token + tint | `Assets/CapsChaos/Views/DesignTokens.cs`, `Assets/CapsChaos/Views/Art/TokenTint.cs` |
| Output | `Content/Art/Meshes` (11), `Textures` (7), `Materials` (12), `Prefabs` (10: Bottle, BottleHidden, Cap, CapTray, Box, Slot, Lane, Floor, TrayLock, TrayLink) |

**Quy ước kỹ thuật đã chốt khi làm:**
- **Đơn vị:** 1 unit = chiều cao một chai. Mọi kích thước là hằng số trong `ArtShapes`, gồm `CellPitch 0.42` và `LanePitch 0.95`.
- **Màu:** material luôn **trắng**. Màu do `TokenTint` (MaterialPropertyBlock, lấy từ `DesignTokens`) tô lên. Cạnh tham chiếu `Game.Editor → Game.Views` đang bị **ghim rỗng** (`SkuHeadlessTests/Gate/AssemblyReferenceTests.cs`), nên generator gắn `TokenTint` bằng **tên type**, và không chép lại mã màu nào.
- **Thùng:** trục local +Z của pivot nắp hướng vào trong thùng. Tư thế mở = `yaw · Euler(FlapOpenLean = −35°)`, tư thế đóng = `yaw · Euler(90°)`. Child `Tape` mặc định tắt.
- **GUID ổn định:** chạy lại generator sẽ ghi đè asset tại chỗ, nên mọi tham chiếu vẫn giữ nguyên.
- Prefab có entry Addressables. Registrar của framework tự thêm khi import prefab; `AssetKeys.gen.cs` do `Framework/Codegen/Generate` sinh ra.

**Chưa làm (việc của View, khi làm màn Gameplay):**
- Cuộn UV cho cầu vồng và băng chuyền.
- Dissolve khi reveal.
- VFX, logo, nền Title, SFX.

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
