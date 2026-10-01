# Cap Chaos — Game Design Document (MVP)

| | |
|---|---|
| **SKU** | `CapsChaos` (`framework.config.json`) |
| **Framework** | ClassicSpins PrototypeFramework 0.4.0 |
| **Nguồn** | Video 1 `Screen Recording 2026-09-30 at 12.46.35.mov` (Level 12, 96 s) · Video 2 `… 12.54.20.mov` (Level 13, chai ẩn, 20 s) · ảnh popup Win/Lose |
| **Tài liệu liên quan** | Art: [`docs/art-direction.md`](../art-direction.md) · Schema level: [`level.schema.json`](level.schema.json) |
| **Trạng thái** | v0.2 — 2026-09-30 |

> **Quy ước trong tài liệu:** **[QS]** là điều quan sát được trong video/ảnh. **[GĐ]** là giả định hoặc
> đề xuất của designer. **[CHỐT]** là quyết định của product owner.

## 0. Quyết định đã chốt

| # | Quyết định | Hệ quả |
|---|---|---|
| D1 | **Camera 3D perspective** [CHỐT] | Theo **ADR-001 phương án B**: camera `GamePlay` chuyển sang perspective, không thêm layer. Xem §10.1 |
| D2 | **Không dùng nhãn hiệu**. Chai, nắp, khay chỉ phân biệt bằng **màu** [CHỐT] | Không có logo hay chữ trên vật thể gameplay |
| D3 | Chai ẩn hiển thị bằng **texture cầu vồng**, không dùng màu tối như video [CHỐT] | Art §4.2 |
| D4 | **Toàn bộ art của MVP do team dev/agent tự tạo** [CHỐT] | Pipeline ở Art §9 |
| D5 | **Level được cấu hình hoàn toàn bằng JSON** [CHỐT] | §6; scene và prefab không chứa dữ liệu level |

---

## 1. Tóm tắt

**Cap Chaos** là game puzzle 3D casual, màn hình dọc, chơi bằng tap. Trên đầu màn hình là một **khối chai
không nắp xếp chồng nhiều tầng**. Người chơi tap vào **khay nắp** (mỗi khay 4 nắp cùng màu) ở đầu 3
**băng chuyền**. Khay bay lên một trong 3 **slot**. Những chai cùng màu đang lấy được sẽ tự bay xuống,
được đóng nắp và cắm vào khay. Khay đủ 4 chai thì được đóng thùng và chuyển đi.

**Fantasy:** làm công nhân dây chuyền đóng chai, dọn sạch một núi chai hỗn loạn.
**Thể loại:** sort và match có hàng đợi, cùng họ với *Bus Jam* và *Screw Sort*. Cái khó nằm ở việc
**chọn thứ tự tap** để 3 slot không bị kẹt.

| Thuộc tính | Giá trị |
|---|---|
| Nền tảng | Mobile, portrait [QS] |
| Input | Chỉ tap [QS] |
| Độ dài một level | 60–120 s [QS] |
| Camera | 3D perspective, cố định, nhìn chếch từ trước-trên [QS][CHỐT] |

---

## 2. Core loop

```
 1. Đọc khối chai: chai nào ĐANG LẤY ĐƯỢC (tầng đất + hàng trước)? màu gì?
 2. Đọc 3 khay đầu băng chuyền.
 3. TAP một khay → khay bay vào slot trống; băng chuyền chạy, đưa khay kế lên đầu.
 4. Chai cùng màu lấy được bay xuống → đóng nắp → cắm vào khay.
 5. Lấy chai ở tầng đất → chồng phía trên RƠI xuống 1 tầng → chai chạm đất LỘ MÀU (nếu đang ẩn).
 6. Khay đủ 4 → đóng thùng → thùng bay đi → slot trống.
 Thắng: hết chai (popup GOOD JOB)   Thua: 3 slot kẹt (popup YOU CAN DO IT)
```

Nhịp một lượt, đo từ video 1 với mốc tap = 0 [QS]:

| t (s) | Sự kiện |
|---|---|
| 0,00 | Tap khay đầu làn |
| 0,20 | Khay đã vào slot; băng chuyền dồn lên một ô |
| 0,50–1,00 | 4 chai bay vào, mỗi chai cách nhau ~0,12 s; nắp bắn từ khay lên chụp vào cổ chai |
| ~2,00 | Thùng carton úp xuống, 4 nắp thùng gập lại |
| 2,50 | Dán băng keo |
| 2,80 | Thùng bay ra góc trên phải; slot trống |

---

## 3. Bố cục màn hình

| Vùng | Vị trí (theo chiều cao màn hình) | Nội dung |
|---|---|---|
| **HUD** | 0–8 % | Restart (trái) · pill "Level N" (giữa) · Home (phải) [QS] |
| **Bottle Stack** | 5–40 % | Khối chai 3D nhiều tầng [QS] |
| **Slot Bar** | 45–57 % | 3 slot nằm ngang, luôn thấy ô trống [QS] |
| **Conveyor Lanes** | 60–100 % | 3 băng chuyền dọc, chạy ra ngoài mép dưới màn hình [QS] |

Ảnh tham chiếu:
- [`refs/02_gameplay_start.jpg`](refs/02_gameplay_start.jpg): màn chơi lúc bắt đầu.
- [`refs/07_hidden_stack.jpg`](refs/07_hidden_stack.jpg): khối chai có chai ẩn.
- [`refs/05_deadlock.jpg`](refs/05_deadlock.jpg): lúc bế tắc.

---

## 4. Thực thể

| Thực thể | Mô tả | Thuộc tính |
|---|---|---|
| **Bottle** | Chai PET không nắp, đứng trong khối | `color`, `pos (x, z, layer)`, `hidden: bool` |
| **CapTray** | Khay 2×2 = 4 nắp cùng màu [QS] | `color`, `capacity` (mặc định 4), `filled` |
| **Lane (Conveyor)** | Băng chuyền, là hàng đợi FIFO các khay [QS] | `queue<CapTray>` |
| **Slot** | Chỗ đặt khay đang nạp chai [QS] | `tray?` |
| **Box** | Thùng carton cùng màu với khay; chỉ để trình diễn [QS] | — |

**Màu** [CHỐT D2]: chỉ dùng màu, không nhãn. Bảng MVP có 8 màu. Mỗi level chọn một tập con (video dùng 4).
Mã hex ở Art §3.

| Mã JSON | Màu | Có trong video |
|---|---|---|
| `R` | Hồng-đỏ | ✔ |
| `O` | Cam | ✔ |
| `B` | Xanh dương | ✔ |
| `G` | Xanh lá | ✔ |
| `P` | Tím | — |
| `Y` | Vàng | — |
| `C` | Xanh ngọc (cyan) | — |
| `N` | Nâu | — |

---

## 5. Luật chơi

### 5.1 Khối chai: lưới 3D có trọng lực
- **R1** [QS] Khối chai là lưới 3D `(x = cột, z = hàng sâu, layer = tầng)`. Chai ở tầng `k ≥ 1` **đứng
  trên** chai ở tầng `k−1` cùng ô. Video 2, frame 5,5 s: một chai tối đứng trên chai cam ở hàng trước.
- **R2 — Lấy được (exposed)** [QS] Một chai lấy được khi thoả **cả hai** điều kiện:
  1. nằm ở **tầng đất** (`layer = 0`);
  2. là chai **trước nhất** ở tầng đất trong cột `x` của nó, tức là không có chai tầng đất nào ở `z`
     nhỏ hơn (gần người chơi hơn).

  Bằng chứng:
  - Video 1, 76,5 s: người chơi bế tắc dù chai cam/hồng vẫn còn, vì chúng nằm ở tầng trên hoặc phía sau.
  - Video 2, 10–13 s: chai cam ở tầng đất nhưng nằm sau hàng chai xanh thì không bị kéo, cho tới khi hàng
    xanh được dọn đi.
- **R3 — Trọng lực** [QS] Khi chai ở `(x, z, 0)` bị lấy đi, **toàn bộ chồng** tại `(x, z)` rơi xuống một
  tầng (video 2, 7,8 s → 8,0 s). Cột không trượt về phía trước.
- **R4 — Chai ẩn** [QS][CHỐT D3] Chai có cờ `hidden` hiển thị bằng **vật liệu cầu vồng** và người chơi
  **không biết màu** của nó. Nó **lộ màu đúng lúc chạm đất** (`layer = 0`). Khi lộ màu có VFX "reveal"
  (Art §6).
  - Chai ẩn ở tầng đất **không tồn tại**: bộ validate level bắt lỗi này, hoặc engine lộ màu ngay khi load.
  - Chai ẩn **không bao giờ** được khay nào kéo, vì nó luôn nằm ở tầng ≥ 1.

### 5.2 Tap và băng chuyền
- **R5** [QS][CHỐT] **Tap vào khay, không tap vào băng chuyền.** Mỗi khay trên băng chuyền có vùng chạm riêng;
  phần băng chuyền trống không phản hồi gì.
  - Chạm **khay đầu** làn: khay bay lên slot, băng chuyền chạy.
  - Chạm **khay phía sau**: khay **rung ngang theo mặt sàn** (tween LitMotion, 0,35 s, 3 chu kỳ, biên độ
    0,07, tắt dần), luật chơi không đổi.
  - Việc quyết định thuộc về controller (`GameplayScreen.OnTrayTapped`); View chỉ báo `(lane, index)`.
- **R6** [QS] Khay được tap bay vào **slot trống ngoài cùng bên trái**.
- **R7 — Băng chuyền** [QS][CHỐT] Khi khay đầu rời làn, **băng chuyền chạy** và đưa **mọi khay còn lại
  lên một ô**. Mặt băng cuộn theo (sọc chạy), và khay mới trượt vào từ ngoài mép dưới màn hình. Người chơi
  chỉ thấy khoảng 4–5 khay đầu mỗi làn; phần còn lại của hàng đợi nằm ngoài màn hình.
- **R8** [GĐ] Tap khay đầu làn khi không còn slot trống: khay rung như R5 (SFX "denied" sẽ thêm sau). Không có gì thay đổi.
- **R9** [GĐ] Có thể tap tiếp trong lúc animation còn chạy; màn hình không bị khoá. Luật (Domain) chạy tức
  thì; animation chỉ phát lại kết quả.
  - **Phản hồi tap là tức thì** (sửa 2026-09-30): khay được tap bay vào slot và băng chuyền dồn lên *ngay
    khung hình đó*. Chỉ các fact còn lại (chai bay, rơi, lộ màu, đóng thùng) mới phát lại theo thứ tự.
  - Khi vừa đầy khay, luật trả slot ngay, nên có thể dùng lại chính slot đó cho khay tiếp theo, trong khi
    thùng cũ còn đang bay trên màn hình. Vì vậy View đặt khay mới vào slot **trống trên màn hình** (ưu tiên
    slot luật chọn, không thì slot trống ngoài cùng bên trái). Mọi animation bám theo **id khay**, không
    bám theo chỉ số slot.

### 5.3 Nạp chai
- **R10** [QS] Mỗi khay trong slot **tự động** kéo các chai lấy được **cùng màu** cho tới khi đủ
  `capacity`. Người chơi không cần tap vào chai.
- **R11 — Tất định** [GĐ] Engine lặp tới khi không còn gì thay đổi:
  1. Xét slot theo thứ tự trái → phải.
  2. Mỗi slot chọn chai hợp lệ có `z` nhỏ nhất; hoà thì lấy `x` gần slot nhất; vẫn hoà thì lấy `x` nhỏ.
  3. Mỗi lần lấy một chai thì áp R3 và R4 ngay, rồi lặp lại từ bước 1.

  Cùng một chuỗi tap luôn cho **cùng** kết quả, nhờ đó test headless tái lập được.
- **R12** [QS] Khay chưa đầy mà không còn chai hợp lệ thì **đứng chờ** trong slot.
- **R13** [QS] Khay đầy thì phát sự kiện đóng thùng; slot được giải phóng khi thùng bay đi.

### 5.4 Thắng / Thua
- **R14 — Thắng** [QS ảnh] Khối chai trống và thùng cuối cùng đã bay đi thì hiện popup **"GOOD JOB" +
  NEXT** ([`refs/09_win_popup.png`](refs/09_win_popup.png)). Bấm NEXT sang Level N+1.
- **R15 — Thua** [QS] Game xử thua khi:
  - mọi slot đều có khay,
  - không còn animation nạp nào đang chạy,
  - và không chai lấy được nào trùng màu với bất kỳ khay nào trong slot.

  Khi đó hiện popup **"YOU CAN DO IT" + RESTART** ([`refs/10_lose_popup.png`](refs/10_lose_popup.png)).
  Bấm RESTART chơi lại đúng level đó.
- **R16 — Bất biến** Với mọi màu c: `số chai màu c = capacity × số khay màu c`. Level vi phạm bất biến
  **không được load** (§6.4).

### 5.5 HUD
- Restart: chơi lại level ngay, không hỏi xác nhận [GĐ].
- Home: về màn Title [QS].

---

## 6. Level = JSON (bắt buộc)

[CHỐT D5] Một level được mô tả **trọn vẹn** trong **một file JSON**. Scene, prefab và ScriptableObject
**không chứa** dữ liệu level nào: chúng chỉ là "máy chạy" level. Thêm hoặc sửa một level **không cần mở
Unity**. Đây là machine zone: diff được, review được, test headless được.

### 6.1 Vị trí file

| File | Vai trò |
|---|---|
| `Assets/CapsChaos/Content/LevelConfig/level_0001.json` … | Một file cho mỗi level (TextAsset). Cả folder là **một** entry Addressables, address `LevelConfig`, nên mỗi file có address `LevelConfig/<id>.json` và level mới không cần đăng ký thêm |
| `Assets/CapsChaos/Content/LevelConfig/levels.index.json` | Thứ tự chơi: `{ "order": ["level_0001", "level_0002", …] }` |
| `Game.Domain.CapColor` | Enum màu, dùng chung cho chai, tray, nắp và hộp. Trong JSON màu vẫn viết bằng mã 1 chữ (`R O B G P Y C N`, chữ thường = chai ẩn); chỉ codec/LevelTool dùng mã |
| [`docs/design/level.schema.json`](level.schema.json) | JSON Schema (draft 2020-12), là nguồn chân lý của định dạng |

### 6.2 Định dạng

```json
{
  "$schema": "../../../../docs/design/level.schema.json",
  "formatVersion": 1,
  "id": "level_0013",
  "slots": 3,
  "trayCapacity": 4,
  "colors": ["R", "O", "B", "G"],
  "stack": {
    "cols": 7,
    "rows": 4,
    "layers": [
      ["GOOOOOG",
       "GOOOOOG",
       "GOOOOOG",
       "GBBOBBG"],
      ["rrbbggo",
       "oobbggr",
       "rgbbogr",
       "ggr.brr"]
    ]
  },
  "lanes": [
    ["B", "O", "B", "G", "O"],
    ["R", "G", "O", "R", "G"],
    ["R", "O", "R", "R", "B"]
  ],
  "view": { "cameraPreset": "default", "stackScale": 1.0 },
  "meta": { "name": "Mystery Tower", "difficulty": "hard", "notes": "Tái dựng từ video 2" }
}
```

Mảng `lanes` phải có số khay đúng theo R16. Validator sẽ kiểm, và ví dụ trên chỉ để minh hoạ định dạng.

| Trường | Ý nghĩa |
|---|---|
| `formatVersion` | Tăng khi đổi định dạng; loader có migrator cho từng version |
| `slots` | Số slot, 1–5 (mặc định 3) |
| `trayCapacity` | Số nắp mỗi khay (mặc định 4) |
| `colors` | Tập màu dùng trong level; mọi ký tự trong `stack` và `lanes` phải thuộc tập này |
| `stack.layers[k]` | Lưới của tầng `k`; `layers[0]` là **tầng đất** |
| `stack.layers[k][i]` | Một chuỗi dài `cols` ký tự; **`i = 0` là hàng xa nhất**, **`i = rows−1` là hàng trước** (gần người chơi) |
| Ký tự | `.` = trống · **chữ HOA** = chai lộ màu · **chữ thường** = chai **ẩn** (cầu vồng), lộ màu khi chạm đất |
| `lanes[j]` | Hàng đợi của băng chuyền `j` (trái → phải). Phần tử `[0]` là khay đầu làn |
| `view` | (tuỳ chọn) preset camera và scale khối chai, dùng khi khối chai quá to |
| `meta` | (tuỳ chọn) tên, độ khó, ghi chú. Engine bỏ qua |
| `meta.solution` | (tuỳ chọn) một chuỗi tap thắng (chỉ số làn), do LevelTool ghi; V6 chạy lại nó để chứng minh level giải được. Có thể dùng làm gợi ý (hint) sau này |

### 6.3 Tham số chung (không nằm trong level)
Timing animation, easing, màu hex và SFX là **config key / design token**, dùng chung cho mọi level (§9,
Art §10). Một level chỉ mô tả **nội dung** của nó.

### 6.4 Validator (chặn khi load và chặn trong CI)
Validator là C# thuần trong `Game.Domain`. Nó chạy ở ba nơi: khi load level, trong
`pf-build.sh validate`, và trong `dotnet test SkuHeadlessTests`.

| # | Luật | Lỗi mẫu |
|---|---|---|
| V1 | JSON khớp schema | `stack.layers[1][2]: length 6 ≠ cols 7` |
| V2 | Không có chai lơ lửng: chai ở tầng `k ≥ 1` phải có chai ở tầng `k−1` cùng ô | `floating bottle at (3,1,2)` |
| V3 | Không có chai ẩn ở tầng 0 | `hidden bottle on ground at (0,3)` |
| V4 | R16 cân bằng từng màu | `color O: 18 bottles vs 4 trays×4=16` |
| V5 | Ký tự nằm trong `colors` | `unknown color 'X' in lanes[1][3]` |
| V6 | **Có lời giải**. Nếu level có `meta.solution` thì **chạy lại** chuỗi tap đó (nhanh, chắc chắn). Nếu không thì solver DFS có memo, kèm budget node; vượt budget ⇒ `Unknown`, không bao giờ đoán | `V6 Unsolvable` / `V6 Unknown` |

V6 là cửa CI: level không giải được thì không ship.

### 6.5 Đòn bẩy độ khó

| Đòn bẩy | Dễ | Khó |
|---|---|---|
| Số màu | 2–3 | 6–8 |
| Số tầng | 1 | 3–4 |
| Tỷ lệ chai ẩn | 0 % | 60 %+ tầng trên [QS video 2: gần như toàn bộ tầng trên ẩn] |
| Độ trộn trong cột | Cột cùng màu | Xen kẽ từng chai |
| Thứ tự khay | Khớp với tầng đất | Lệch pha, người chơi phải nhìn trước 2–3 nước |
| Số slot (`"slots"`, 1–5, cấu hình theo từng level) | 4–5 (level tutorial) | 3 |

**Đường cong đề xuất** [GĐ]:
- L1–3: 1 tầng, 2–3 màu, dạy tap, băng chuyền và đóng thùng.
- L4–8: 2 tầng, lộ màu hết.
- L9–12: deadlock thật (giống video 1).
- L13+: chai ẩn (giống video 2).

**Seed levels của MVP:** 15 level, trong đó `level_0012` và `level_0013` tái dựng gần đúng từ hai video.

**Số slot là config của level** (`"slots"`, mặc định 3, cho phép 1–5):
- Domain: `CapChaosGame.SlotCount`, dùng cho R6 (chọn slot) và R15 (xử thua).
- LevelTool: spec có thể khai `"slots"`.
- View: `BoardView` co giãn cả hàng slot. Tối đa 4 slot giữ nguyên cỡ (`SlotRowMaxWidth` 4.48); với 5 slot,
  khoảng cách, ô slot, khay và thùng cùng co theo một tỉ lệ (0.8). Ảnh:
  [`refs/14_slots5_S6.jpg`](refs/14_slots5_S6.jpg), [`14_slots5_S7.jpg`](refs/14_slots5_S7.jpg).
- Seed levels: L1–L2 có 5 slot, L3–L5 và L10 có 4 slot, còn lại 3 slot.

### 6.6 Công cụ: `Tools/LevelTool` (CLI .NET, không cần Unity)

```sh
dotnet run --project Tools/LevelTool -- generate          # spec → level JSON + index (giải được theo cách dựng)
dotnet run --project Tools/LevelTool -- generate --check  # exit 1 nếu output khác file đã commit
dotnet run --project Tools/LevelTool -- validate          # V1–V6 + index cho mọi level
dotnet run --project Tools/LevelTool -- stats             # độ khó: tỉ lệ thắng khi tap ngẫu nhiên + effort của solver
```

- Spec nằm ở `Tools/LevelTool/seed-levels.json`, gồm: hình dạng khối chai (`#` chai lộ màu, `?` chai ẩn),
  bộ màu, `greed` (1 = dễ), `clustering`, và `seed`. Tool là nơi giữ seed (luật #14).
- **⚠ `generate` ghi đè** các level có trong spec. Level nào designer đã sửa tay thì **xoá khỏi spec**
  (hoặc đổi id) trước khi chạy lại. Level viết tay không cần `meta.solution`; V6 sẽ dùng solver.

**Độ khó của seed levels** (`stats`, 500 ván tap ngẫu nhiên, seed 20260930). Đây là proxy, **cần playtest**:

| Level | 1–4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13 | 14 | 15 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Nhãn | tutorial/easy | easy | easy | medium | medium | medium | breather | hard | hard | hard | medium | hard |
| Thắng ngẫu nhiên | 100 % | 100 % | 80 % | 37 % | 62 % | 18 % | 100 % | 3 % | 2 % | 3 % | 17 % | 0.6 % |

---

## 7. Luồng màn hình & UI

```
Title ──PLAY──▶ Gameplay ──(R15)──▶ LoseDialog ──RESTART──▶ Gameplay (cùng level)
  ▲                │ └─(R14)──▶ WinDialog ──NEXT──▶ Gameplay (level+1)
  └─────Home───────┘
```

| Màn | Thành phần | Nguồn |
|---|---|---|
| **Title** | Logo "CAP CHAOS", 5 chai trên sân khấu dưới spotlight, text "Level N", nút **PLAY** | [QS] `refs/01_title.jpg` |
| **Gameplay HUD** | Restart · pill "Level N" · Home | [QS] |
| **WinDialog** | Dim **xanh lá**; panel 3 dải xanh; tiêu đề **"GOOD JOB"**; nút xanh **NEXT** | [QS] `refs/09_win_popup.png` |
| **LoseDialog** | Dim **tím**; panel 3 dải tím; tiêu đề **"YOU CAN DO IT"**; nút tím **RESTART** | [QS] `refs/10_lose_popup.png` |

Win và Lose dùng **chung một prefab popup**, chỉ khác theme màu (Art §5). Dải dưới của panel đang trống
trong cả hai ảnh [QS]. [GĐ] Dải này hiển thị subtitle: Win "Level N cleared!", Lose "Out of space!".

Loc key khởi đầu: `title.play`, `hud.level` (arg `{0}`), `win.title`, `win.subtitle`, `win.next`,
`lose.title`, `lose.subtitle`, `lose.restart`.

---

## 8. Feel / juice

1. Chai rời khối có **vòng pop** tại chỗ nó đứng [QS].
2. Nắp **bắn từ khay lên** chụp vào cổ chai giữa không trung. Đây là khoảnh khắc "cap" của tên game [QS].
3. Chồng chai **rơi** xuống có nảy nhẹ và bụi ở chân chai [QS].
4. **Reveal**: cầu vồng tan ra thành màu thật, kèm flash và vài hạt lấp lánh [CHỐT D3].
5. Băng chuyền chạy mượt; khay có quán tính nhẹ khi dừng [QS].
6. Đóng thùng có 3 nhịp (úp, gập, dán), sau đó thùng bay đi [QS].
7. [GĐ] Haptic nhẹ khi đóng nắp, vừa khi đóng thùng, mạnh khi thắng.

---

## 9. Tham số tuning (config keys, chung cho mọi level)

| Key | Mặc định | Nguồn |
|---|---|---|
| `anim.trayToSlotSec` | 0.20 | [QS] |
| `anim.laneAdvanceSec` | 0.20 | [QS≈] |
| `anim.bottleFlightSec` | 0.30 | [QS≈] |
| `anim.bottleStaggerSec` | 0.12 | [QS≈] |
| `anim.stackDropSec` | 0.18 | [QS≈] |
| `anim.revealSec` | 0.25 | [GĐ] |
| `anim.boxHoldBeforeSec` | 0.60 | [QS≈] |
| `anim.boxCloseSec` | 0.50 | [QS≈] |
| `anim.boxExitSec` | 0.40 | [QS≈] |

Mọi key đi qua `ConfigDefaults` (skill `pf-add-key`). Không hardcode trong code.

---

## 10. Ánh xạ sang PrototypeFramework

### 10.1 Camera 3D: đã chốt ADR-001 phương án B

> **Hiện hành:** [`ADR-001-camera-3d.md`](ADR-001-camera-3d.md) §5. `GamePlayCamera` đã là perspective
> (FOV 30). Board tự đặt mình trước camera và nghiêng −60°. Tap đi qua hit-catcher uGUI + raycast 3D
> trong View. Nội dung 2D không được đặt ở layer GamePlay. Phần dưới đây là phân tích ban đầu, giữ lại
> để tham khảo.

#### Phân tích ban đầu
Camera của rig PF đều orthographic, tỷ lệ 1 px = 1 world unit (skill `pf-world-space`). Một camera
perspective cho khối chai, slot và băng chuyền **cần cấu hình camera riêng**. Theo framework, đó là
điều kiện để thêm một **RenderLayer mới**, tức là **spine change**, và phải **route qua
`bmad-architecture`** trước khi code View.

Đề xuất đưa vào review kiến trúc:
- Một layer `Board3D`:
  - camera perspective (FOV khoảng 35°), đặt **dưới** các layer UI;
  - `physicsRaycast: "3d"` để tap trúng khay (manifest layer đã hỗ trợ giá trị này);
  - HUD và dialog vẫn nằm trên các layer uGUI có sẵn.
- Ruler: world 3D dùng mét (1 unit = chiều cao một chai). Không áp ruler 1px = 1unit cho layer này. Cần
  ghi rõ điều này trong quyết định kiến trúc.

### 10.2 Phân tầng

| Tầng | Thành phần |
|---|---|
| **Domain** (engine-free) | `LevelDefinition`, `LevelJsonParser`, `LevelValidator` (V1–V6), `BottleStack` (lưới 3D, R1–R4), `ConveyorLane`, `SlotBar`, `CapChaosRules` (R5–R16), `LevelSolver` |
| **Application** | `LevelSession`: nhận `TapLane(j)`, trả về chuỗi **sự thật** đã xảy ra, để animation phát lại |
| **Events** (thì quá khứ, luật #1) | `TrayPlaced`, `LaneAdvanced`, `BottlePicked`, `StackDropped`, `BottleRevealed`, `BottleCapped`, `TrayPacked`, `LevelFailed`, `LevelCompleted` |
| **Infrastructure** | `LevelRepository`: đọc `levels.index.json` và từng level qua Addressables TextAsset |
| **Presentation** | `GameplayController` xếp hàng animation theo event; `TitleController`; controller cho WinDialog/LoseDialog |
| **Views** | `BottleView`, `CapTrayView`, `SlotView`, `ConveyorView`, `BoxView`, `HudView`, `ResultPopupView` (theme Win/Lose) |
| **Screens / Dialogs** (manifest + `Scaffold.Sync`) | Screens `Title`, `Gameplay` · Dialogs `Win`, `Lose` |

**Trạng thái màn Gameplay (2026-09-30):**

| Phần | File | Ghi chú |
|---|---|---|
| Screen (manifest + `Scaffold.Sync`) | `Scenes/Gameplay.unity`, `SceneKeys.Gameplay`, Addressables `Scenes/Gameplay` | **Scene riêng, load ADDITIVE** chồng lên Master qua `ISceneService`; `Replace` gỡ scene cũ (đã thử cả `Gameplay → Gameplay`) |
| Boot | `Composition/CapsChaosFrameworkSettings.cs` | `FirstSceneConfig` → `Main(ColdBoot)`: danh sách level. Màn Title (§7) sẽ thay chỗ này sau |
| Danh sách level (Main) | `Features/LevelSelectWidget/…/LevelSelectWidget.cs`, `Views/LevelSelectWidget/{LevelSelectView, LevelButtonView}.cs`, prefab `Content/UI/LevelSelect/Prefabs/` | Panel đặt sẵn trong `Main.unity` (instance prefab dưới canvas preview world-space trên layer `UI`, chỉ để nhìn và sửa trong Scene view); khi Main load, widget chuyển panel vào host `Ui` và xoá canvas preview. Lưới 4 cột, mỗi ô ghi số và độ khó (`loc.csv`). Bấm ô ⇒ `Gameplay(LevelIndex)`; Back/Escape trong Gameplay ⇒ về `Main`. Các ô chưa dùng pool vì `PoolInstaller` của framework chưa nối loader với `IAssetService` |
| Param / catalog | `Features/Gameplay/Application/{GameplayParam, LevelCatalog}.cs` | Singleton ở Root scope (engine-free). `Populate` parse + chạy V1–V5 cho **mọi** level một lần; V6 thuộc CI |
| Boot load | `Features/Boot/Infrastructure/LevelConfigNode.cs` | Boot node (Required) chạy ở giai đoạn Loading: `AssetReady → LevelConfig → LevelsLoaded`. Đọc index rồi mọi level qua `IAssetService`. `FirstScene` bị gate thêm cạnh `LevelsLoaded`. Một level hỏng ⇒ boot dừng, log nêu tên từng level hỏng |
| HUD | `Features/GameplayHudWidget/…/GameplayHudWidget.cs`, `Views/GameplayHudWidget/GameplayHudView.cs`, prefab `Content/UI/GameplayHud/Prefabs/` | Nút tròn Restart (trái) và Home (phải), đặt sẵn trong `Gameplay.unity` dưới canvas preview, lúc chạy gắn vào host `Ui`. Restart chơi lại level ngay; Home và Back về `Main`. Icon ↻ ⌂ và đĩa tròn sinh bằng `CapsChaos/Art/Generate UI Sprites`. Pill "Level N" chưa làm |
| Controller | `Features/Gameplay/Presentation/GameplayScreen.cs` | Tap → `CapChaosGame.Tap` → phát lại fact theo thứ tự trên `BoardView` |
| Scope | `Features/Gameplay/Composition/GameplayScreenScope.cs` | WorldRoot, entry screen (catalog kế thừa từ Root) |
| Views | `Views/Board/{BoardView, BoardInputView}.cs`, `Views/DesignTokens.cs` (`Board`, `Motion`, `TintFlavor`) | `TintFlavor` là bản song sinh của `CapColor` ở tầng View (Game.Views không được tham chiếu Game.Domain); `Presentation/CapColorTint.cs` map giữa hai enum. Animation dùng LitMotion lõi + UniTask. Input = hit-catcher uGUI → raycast 3D → chỉ số làn |

**Visual check** (play mode, 1080×1920, 2 phiên; ảnh `refs/13_gameplay_S*.jpg`):
- **S1** `level_0001` lúc bắt đầu;
- **S2** đang đóng thùng;
- **S3** tự sang `level_0002` sau khi thắng;
- **S4** `level_0013` có tầng cầu vồng;
- **S5** chồng chai rơi và lộ màu, khay chờ trong slot.

Không có Error/Exception nào.

**Checklist luật 17:**

| # | Mục | Kết quả |
|---|---|---|
| 1 | Ground | ✔ Sàn do board vẽ |
| 2 | Containment | ✔ Slot trống luôn hiện |
| 3 | Rhythm | ✔ Mọi số nằm trong `DesignTokens.Board` |
| 5 | Empty state | ✔ Có trạng thái trống |
| 7 | Edge | ⚠ Đỉnh khối chai cao sát mép trên. **HUD chưa có** — cần chừa chỗ cho HUD |
| 8 | Affordance | ⚠ Khay đầu làn chưa có viền sáng "tap được" (art §4.6) |
| 4, 6 | Hierarchy, Contrast | Chưa áp dụng: màn chưa có text |

⇒ **Màn chưa hoàn thiện theo luật 17** cho tới khi có HUD và popup Win/Lose.

**Chưa làm:**
- HUD: Restart, Level pill, Home.
- Dialog Win/Lose. Hiện tạm thời: thắng thì tự sang level sau, thua thì tự chơi lại, sau 1 s.
- Viền sáng cho khay đầu làn.
- Cuộn UV cho cầu vồng; dissolve khi lộ màu (hiện chỉ đổi màu và nảy scale).
- VFX, SFX.
- Test EditMode cho View.

**Trạng thái Domain (2026-09-30), đã xong và nằm trong gate headless (88 test):**

| File (`Assets/CapsChaos/Domain/CapChaos/`, namespace `Game.Domain`) | Nội dung |
|---|---|
| `JsonReader.cs` | JSON reader chặt, engine-free (báo dòng/cột, chặn key trùng) |
| `LevelDefinition.cs`, `LevelJson.cs` | Mô hình level; parse = V1 (khớp schema), writer ổn định |
| `LevelValidator.cs` | V2–V5; `LevelSolver` (V6: DFS + memo + budget), `Prove` (replay `meta.solution`) |
| `BottleStack.cs`, `CapChaosGame.cs`, `GameFacts.cs` | Luật R1–R16. `Tap(lane)` trả về chuỗi fact theo thứ tự xảy ra |
| `LevelGenerator.cs` | Sinh level giải được theo cách dựng, nhận `IRandom` (luật #14) |

Test ở `SkuHeadlessTests/CapChaos/`: mỗi luật R1–R16 và V1–V6 có ít nhất một test; `ContentLevelsTests`
chạy với mọi level trong `Content/LevelConfig/`; `Gate/LevelConfigAddressablesGateTests` giữ entry Addressables của folder.

> **Lệch so với kế hoạch ban đầu:** không tái dựng được *chính xác* chuỗi tap trong video, vì video không
> cho biết đủ trạng thái. Thay vào đó, R15 được test bằng một tình huống kẹt dựng theo frame 76,5 s
> (`R15_three_jammed_slots_lose_like_video_1_at_76s`), còn R3/R4 test bằng lưới nhỏ có chai ẩn.
> `level_0012`/`0013` chỉ **mô phỏng** hình dạng khối chai trong video.

---

## 11. Câu hỏi mở

1. Khay cam in chữ "PEPSI" trong video 1 (76,5 s): lỗi art của bản gốc. Không ảnh hưởng MVP vì D2.
2. Booster (undo, thêm slot, xáo trộn), tiền tệ, continue bằng quảng cáo: ngoài phạm vi MVP.
3. Progression và map level: ngoài phạm vi MVP. Hiện chỉ có `levels.index.json` tuyến tính.
4. Nên hé lộ màu chai ẩn một phần (ví dụ viền màu mờ) ở level khó không? Chờ playtest.
