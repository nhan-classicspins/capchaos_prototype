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
| D1 | **Camera 3D, orthographic** [CHỐT, sửa 2026-10-02] | ADR-001 phương án B, sửa §7: camera `GamePlay` orthographic, board nghiêng và tự scale theo viewport. Xem §10.1 |
| D2 | **Không dùng nhãn hiệu**. Chai, nắp, khay chỉ phân biệt bằng **màu** [CHỐT] | Không có logo hay chữ trên vật thể gameplay |
| D3 | ~~Chai ẩn hiển thị bằng texture cầu vồng~~ — **bỏ 2026-10-02** cùng khối chai: mọi chai trên băng oval và trong hàng chờ đều lộ màu | Thông tin "sắp tới" nằm ở đuôi hàng chờ, không cần chai ẩn |
| D6 | **Băng chuyền oval thay khối chai** (2026-10-02) [CHỐT]: chai đi thành hàng 4 trên một vòng oval chạy liên tục; hàng chờ (feeder) nhập vào oval khi có ô trống; nhiều điểm nhập cấu hình theo level | §5.1, §6.2. Model rời rạc (1 bước = oval tiến 1 hàng), controller bước theo tick cố định |
| D4 | **Toàn bộ art của MVP do team dev/agent tự tạo** [CHỐT] | Pipeline ở Art §9 |
| D5 | **Level được cấu hình hoàn toàn bằng JSON** [CHỐT] | §6; scene và prefab không chứa dữ liệu level |

---

## 1. Tóm tắt

**Cap Chaos** là game puzzle 3D casual, màn hình dọc, chơi bằng tap. Trên đầu màn hình là một **băng chuyền
oval** chở chai không nắp, xếp **hàng 4**, chạy vòng liên tục; một hoặc nhiều **hàng chờ** nhập thêm chai vào oval
mỗi khi có chỗ trống. Người chơi tap vào **khay nắp** (mỗi khay 4 nắp cùng màu) ở đầu 3 **băng chuyền khay**. Khay bay
lên một trong 3 **slot**. Chai cùng màu chạy ngang qua **đoạn trước slot** sẽ tự bay xuống, được đóng nắp và cắm vào
khay. Khay đủ 4 chai thì được đóng thùng và chuyển đi.

**Fantasy:** làm công nhân dây chuyền đóng chai, dọn sạch một dòng chai không ngừng chảy.
**Thể loại:** sort và match có hàng đợi, cùng họ với *Bus Jam* và *Screw Sort*. Cái khó nằm ở việc
**chọn thứ tự tap** để 3 slot không bị kẹt.

| Thuộc tính | Giá trị |
|---|---|
| Nền tảng | Mobile, portrait [QS] |
| Input | Chỉ tap [QS] |
| Độ dài một level | 60–120 s [QS] |
| Camera | 3D orthographic, cố định, board nghiêng nhìn chếch từ trước-trên [CHỐT, sửa 2026-10-02] |

---

## 2. Core loop

```
 1. Đọc băng oval: màu nào sắp chạy ngang qua đoạn trước slot? hàng chờ còn những màu gì?
 2. Đọc 3 khay đầu băng chuyền khay.
 3. TAP một khay → khay bay vào slot trống; băng khay chạy, đưa khay kế lên đầu.
 4. Chai cùng màu chạy qua đoạn trước slot bay xuống → đóng nắp → cắm vào khay.
 5. Chai bị lấy để lại ô trống chạy theo oval → tới điểm nhập thì hàng chờ đổ chai mới vào.
 6. Khay đủ 4 → đóng thùng → thùng bay đi → slot trống.
 Thắng: oval + hàng chờ hết chai (popup GOOD JOB)   Thua: slot đầy, không chai trên oval khớp khay nào và hàng chờ không nhập được (popup YOU CAN DO IT)
```

Nhịp một lượt, đo từ video 1 với mốc tap = 0 [QS]:

| t (s) | Sự kiện |
|---|---|
| 0,00 | Tap khay đầu làn |
| 0,20 | Khay đã vào slot; băng chuyền dồn lên một ô |
| 0,50–1,00 | 4 chai bay vào hốc khay, mỗi chai cách nhau ~0,12 s (không còn nắp — bỏ 2026-10-02) |
| ~2,00 | Khay đậy nắp (bỏ thùng carton — 2026-10-02) |
| 2,50 | Dán băng keo |
| 2,80 | Khay đã đậy nắp bay ra góc trên phải; slot trống |

---

## 3. Bố cục màn hình

| Vùng | Vị trí (theo chiều cao màn hình) | Nội dung |
|---|---|---|
| **HUD** | 0–8 % | Restart (trái) · pill "Level N" (giữa) · Home (phải) [QS] |
| **Băng oval** | 5–40 % | Oval chai hàng 4, hàng chờ đi vào từ mép trên/phải; cạnh trước oval là vùng lấy chai |
| **Slot Bar** | 45–57 % | 3 slot nằm ngang, luôn thấy ô trống [QS] |
| **Conveyor Lanes** | 60–100 % | 3 băng chuyền dọc, chạy ra ngoài mép dưới màn hình [QS] |

Ảnh tham chiếu:
- [`refs/02_gameplay_start.jpg`](refs/02_gameplay_start.jpg): màn chơi lúc bắt đầu.
- [`refs/05_deadlock.jpg`](refs/05_deadlock.jpg): lúc bế tắc.

---

## 4. Thực thể

| Thực thể | Mô tả | Thuộc tính |
|---|---|---|
| **Bottle** | Chai PET không nắp, đứng trên băng oval hoặc trong hàng chờ | `color`, vị trí `(row, track)` trên oval |
| **Loop** | Băng oval: `rows` hàng × `width` chai (mặc định 4), chạy vòng liên tục | `rows`, `width`, `pickRows` |
| **Feeder** | Hàng chờ chai, nhập vào oval tại một điểm cố định | `mergeAt`, `bottles` |
| **CapTray** | Khay 2×2 = 4 nắp cùng màu [QS] | `color`, `capacity` (mặc định 4), `filled` |
| **Lane (Conveyor)** | Băng chuyền, là hàng đợi FIFO các khay [QS] | `queue<CapTray>` |
| **Slot** | Chỗ đặt khay đang nạp chai [QS] | `tray?` |
| **Box** | ~~Thùng carton~~ — bỏ 2026-10-02: khay (container) tự đậy nắp rồi bay đi | — |

**Màu** [CHỐT D2]: chỉ dùng màu, không nhãn. Bảng MVP có 8 màu. Mỗi level chọn một tập con (video dùng 4).
Mã hex ở Art §3.

Trong level JSON (format v2), màu ghi bằng **số** = giá trị enum `Game.Domain.CapColor`. Số **không bao giờ đổi**
(tool của designer ghi số này), màu mới lấy số tiếp theo. `0` = ô trống. Mã chữ chỉ còn ở format v1 cũ, spec của
LevelTool và test.

| Số JSON | `CapColor` | Màu | Mã chữ (v1) | Có trong video |
|---|---|---|---|---|
| `0` | `None` | ô trống (chỉ trong `stack`) | `.` | — |
| `1` | `Red` | Hồng-đỏ | `R` | ✔ |
| `2` | `Orange` | Cam | `O` | ✔ |
| `3` | `Blue` | Xanh dương | `B` | ✔ |
| `4` | `Green` | Xanh lá | `G` | ✔ |
| `5` | `Purple` | Tím | `P` | — |
| `6` | `Yellow` | Vàng | `Y` | — |
| `7` | `Cyan` | Xanh ngọc (cyan) | `C` | — |
| `8` | `Brown` | Nâu | `N` | — |

---

## 5. Luật chơi

### 5.1 Băng oval và hàng chờ (thay khối chai, 2026-10-02)
Tham chiếu: video crowd-sort (người đi thành hàng trên vòng xoay, hàng chờ nhập vào ở cạnh phải). Phân tích ở
chat 2026-10-02; mô hình dưới đây là bản chuyển sang chai.

- **R1 — Oval** Băng gồm `rows` **hàng**, mỗi hàng `width` **ô** (track) — mặc định 4 chai một hàng. Băng **chạy
  liên tục** theo chiều kim đồng hồ nhìn từ trên (cạnh trước chạy phải → trái), mỗi bước tiến **đúng 1 hàng**.
  Một hàng là một miếng băng cố định: chai trên đó đi cùng nó. Vị trí trên đường chạy (track position) `0` là hàng
  đầu tiên của **vùng lấy chai**; vị trí tăng theo chiều chạy.
- **R2 — Vùng lấy chai** Vùng lấy là `pickRows` vị trí `0 … pickRows−1`, tức **cạnh thẳng phía trước, ngay trên dãy
  slot**. Chỉ chai đang nằm trong vùng này mới bay vào khay. Chai cùng màu ở chỗ khác phải đợi oval quay tới.
- **R3 — Ô trống chạy theo băng** Chai bị lấy để lại **ô trống**; không có chai nào dồn lên lấp. Ô trống chạy theo
  oval cho tới khi được hàng chờ lấp (R4).
- **R4 — Hàng chờ (feeder)** Mỗi level có **0–2** hàng chờ (sửa 2026-10-02). `mergeAt` là **lối vào** của hàng chờ (chỗ
  đầu hàng đứng), ngoài vùng lấy. Chai trong hàng chờ xếp theo hàng: chai thứ `i` đứng ở hàng `i / width`, track
  `i % width`. Hàng chờ **nhập nguyên hàng**: hàng đầu bước lên **hàng oval trống gần lối vào nhất** — xét lần lượt vị trí
  `mergeAt`, `−1`, `+1`, `−2`, `+2` (`FeedReach` = 2; ưu tiên hàng sắp tới trước hàng vừa qua; bỏ qua vị trí trong vùng
  lấy) — hàng đó phải **trống ở mọi track nó cần**; không bao giờ nhập lẻ từng chai. Mỗi bước mỗi hàng chờ nhập tối đa 1
  hàng; nhiều hàng chờ xét theo thứ tự khai báo. Generator tô **mỗi hàng một màu** (số chai mỗi hàng chờ là bội của
  `width`). Về hình ảnh, hàng chờ là một **làn nhập** đi xuống từ mép trên; làn hạ xuống oval cách lối vào
  `FeederEntryRows` (3) hàng về phía hạ nguồn. Vị trí nên dùng (B = số hàng mỗi khúc cua): **phải** `rows − B/2 − 3`,
  **trái** là ảnh gương `rows + pickRows − mergeAt(phải) − 2` (vẽ đối xứng gương với hàng phải). Không có hàng nào vừa thì
  hàng chờ đứng yên.
  - Không khai `initial` ⇒ oval **bắt đầu trống**: mọi chai nằm trong hàng chờ và nhập vào khi oval chạy (sửa
    2026-10-02, trước đây oval được đổ đầy trước khi vào ván).
  - Mọi chai đều lộ màu. **Không còn chai ẩn** (D3).

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
- **R10** Mỗi khay trong slot **tự động** nhận chai **cùng màu** chạy qua vùng lấy (R2) cho tới khi đủ `capacity`.
  Người chơi không cần tap vào chai.
- **R11 — Tất định** Sau mỗi tap, và sau mỗi bước oval:
  1. Xét vùng lấy từ hàng **sắp rời vùng** trước (vị trí `pickRows−1` → `0`), trong mỗi hàng track trái → phải.
  2. Mỗi chai trùng màu một khay thì bay vào **slot trái nhất** đang chờ màu đó. Khay đủ thì đóng thùng ngay (R13).
  3. Sau đó (chỉ khi là một bước oval) hàng chờ lấp ô trống tại điểm nhập (R4).

  Cùng một chuỗi tap ở cùng các bước luôn cho **cùng** kết quả, nhờ đó test headless tái lập được.
- **R12** Khay chưa đầy thì **đứng chờ** trong slot cho tới khi chai cùng màu chạy tới.
- **R13** [QS] Khay đầy thì phát sự kiện đóng thùng; slot được giải phóng khi thùng bay đi.

### 5.4 Thắng / Thua
- **R14 — Thắng** [QS ảnh] Oval và mọi hàng chờ hết chai, và thùng cuối cùng đã bay đi thì hiện popup **"GOOD JOB" +
  NEXT** ([`refs/09_win_popup.png`](refs/09_win_popup.png)). Bấm NEXT sang Level N+1.
- **R15 — Thua** Game xử thua khi bàn chơi **đứng yên vĩnh viễn** (quiescent) mà:
  - mọi slot đều có khay,
  - không chai nào **trên oval** trùng màu với khay nào trong slot,
  - và không hàng chờ nào nhập được nữa (oval không còn ô trống, hoặc hàng chờ của các track còn trống đã hết).

  Còn chai khớp màu trên oval thì **chưa thua**: chai sẽ chạy tới vùng lấy. Khi chưa đầy slot mà bàn đứng yên và không
  tap nào được chấp nhận (khoá, cặp nối thiếu slot…) thì cũng thua (`NoMovesLeft`).

  Khi đó hiện popup **"YOU CAN DO IT" + RESTART** ([`refs/10_lose_popup.png`](refs/10_lose_popup.png)).
  Bấm RESTART chơi lại đúng level đó.
- **R16 — Bất biến** Với mọi màu c: `số chai màu c (oval + hàng chờ) = capacity × số khay màu c`. Level vi phạm bất biến
  **không được load** (§6.4).

### 5.5 Khay đặc biệt (thêm 2026-10-01)
Ba loại khay đặc biệt, khai trong level JSON (§6.2). Domain: `CapChaosGame.Tap`; test: `SkuHeadlessTests/CapChaos/TrayModifierRulesTests.cs`.

- **R17 — Khay ẩn** [GĐ] Khay và nắp **không lộ màu**: tô màu slate `Mystery` và in dấu **"?"** lên trên nắp. Khi khay
  lên **đầu làn** thì lộ màu thật (fact `TrayRevealed`, khay nảy nhẹ). Khay ẩn nằm sẵn ở đầu làn lúc vào level thì
  lộ màu ngay. Khay sau của một cặp nối cùng làn lộ màu cùng lúc với khay trước của cặp.
- **R18 — Khay khoá** [GĐ] Có `n` lượt khoá, hiện bằng icon ổ khoá có số. **Chỉ đếm khi khay đang ở đầu làn**: mỗi
  khay bay lên slot (từ làn khác) trừ 1, một cặp nối trừ 2. Lượt đưa khay khoá lên đầu làn thì không tính. Về 0 thì
  ổ khoá bật mở và biến mất (fact `TrayLockTicked`, `Remaining = 0`). Tap khay đang khoá thì khay rung, luật không đổi.
  Prefab ổ khoá là `Content/Art/Prefabs/TrayLock.prefab`, nội dung tạm do ArtGenerator vẽ, chủ SKU sẽ thay. Giữ
  `TrayLockView` ở root khi thay nội dung.
- **R19 — Khay nối** [GĐ] Hai khay nối bằng dây. Chỉ nối được **2 khay liền nhau trong cùng làn**, hoặc **2 khay
  cùng vị trí ở 2 làn kề nhau**. Hai khay chỉ di chuyển **cùng nhau**:
  - chưa sẵn sàng (một khay chưa ở đầu làn) mà tap vào khay nào của cặp thì **cả 2 khay cùng rung**;
  - sẵn sàng thì tap vào khay nào của cặp cũng được: cả 2 bay lên slot, thứ tự theo file level (cùng làn: khay
    trước trước; 2 làn: làn trái trước). Dây được tháo khi khay bay. **Cần 2 slot trống**, thiếu thì cả cặp rung.
  - Cặp nối khác làn **luôn đứng ngang nhau** (sửa 2026-10-01 theo feedback chủ SKU). Băng chuyền chở một khay nối
    sang làn khác chỉ chạy khi **băng chuyền kia cũng chạy được**. Khay nối bị giữ **chỉ chặn chính nó và các khay phía
    sau nó** (sửa 2026-10-05 theo feedback chủ SKU): các khay **phía trước** nó vẫn tiến lên đầu làn bình thường và tap
    được, nên trước khay nối mở ra một khoảng trống (`LaneGap`). Chỉ khi khay nối là khay kế tiếp của làn thì ô đầu mới
    trống và tap vào làn bị từ chối (`RejectedBeltHeld`, khay rung). Vị trí từng khay trên băng: `TrayPosition(lane, tray)`.
    Khi khay đầu làn B bay đi thì **cả 2 băng cùng chạy** (`CapChaosGame.AdvanceBelts`). Ví dụ level 18: tap khay đầu
    làn 0 ⇒ băng 0 đứng yên; tap tiếp khay đầu làn 1 ⇒ 2 băng cùng chạy, cặp nối cùng lên đầu hàng.
  - Khay nối không được khoá (V7).
- **Thua khi hết nước** Nếu còn slot trống mà không lần tap nào được chấp nhận (mọi khay đầu làn đang khoá, đang chờ
  bạn nối, hoặc cặp nối thiếu slot) thì thua với lý do `NoMovesLeft`, vì chỉ có tap mới thay đổi được trạng thái.

### 5.6 Slot mở thêm (R20, thêm 2026-10-02)
- **R20** Mỗi level có `slots` slot mở sẵn (mặc định **4**) và `extraSlots` slot **khoá** (mặc định **2**) nằm bên phải,
  vẽ bằng ô tối có dấu **"+"** xanh. Slot khoá không nhận khay.
  - **Bấm vào slot khoá** → popup **Parking Slot**: FREE (xem quảng cáo thưởng, placement `slot_unlock_rewarded`) hoặc
    `slots.unlockPrice` coin (mặc định 300); ✕ để thôi. Mở slot trái nhất còn khoá.
  - **Hết slot**: bàn đứng yên, không còn gì di chuyển được nếu không có thêm slot, mà vẫn còn slot khoá → luật phát
    `SlotsRanOut` (**chưa thua**). Màn chơi hiện popup **Out of Slot**: FREE (`slot_rescue_rewarded`) hoặc
    `slots.rescuePrice` coin (mặc định 900), hoặc **Restart** (chơi lại level). Hết slot khoá mà kẹt → thua như R15.
  - Slot đã mở **chỉ có hiệu lực trong ván đó**: restart hoặc sang level khác lại về 4 slot.
  - Băng oval **dừng** khi popup đang mở.
  - V6 chứng minh level giải được **chỉ với slot mở sẵn**: slot trả phí là trợ giúp, không phải lời giải.
- **Coin**: ví của framework (`ResourceKeys.Coins`, lưu cùng save). Lần chạy đầu được `economy.startCoins` (1000); thắng
  một level được `economy.winReward` (50). HUD hiện số coin cạnh nút Home.

### 5.7 Cỡ container (R21, thêm 2026-10-02)
- **R21** Mỗi khay có một **cỡ**: field `size` trong level JSON là số **1 S · 2 M · 3 L · 4 XL** (mặc định 1, không
  bao giờ đổi số). Khay nhận `size × trayCapacity` item mới đầy; với `trayCapacity` 4 là **S 4 · M 8 · L 12 · XL 16**.
  V4 cân bằng màu theo tổng sức chứa đó.
- Prefab: `Content/Art/Prefabs/Containers/Container_S|M|L|XL`. Theo quyết định của chủ SKU (2026-10-02), M, L và XL là
  **bản sao của Container_S**: thêm 2/3/4 tầng anchor 2×2, hộp kéo cao lên, nắp, dấu "?" và khối băng nâng theo.
  Bản sao chỉ được tạo khi còn thiếu (menu `CapsChaos/Art/Wire Containers`); sau đó art có thể thay model, và menu này
  chỉ nối lại (re-wire) các tham chiếu.
- Trên băng chuyền, container luôn **đóng nắp**. Khi bay lên slot, nắp bật ra và text `Count` ở mặt trước hộp hiện số
  item **còn thiếu**. Mỗi item rơi vào thì số giảm 1; khi đủ thì text ẩn, nắp đóng và hộp bay đi.
- Level mẫu: `level_0021` "Big Boxes" (XL, L, M, S).

### 5.8 HUD
- Restart: chơi lại level ngay, không hỏi xác nhận [GĐ].
- Home: về màn Title [QS].

---

## 6. Level = JSON (bắt buộc)

[CHỐT D5] Một level được mô tả **trọn vẹn** bằng JSON. Scene, prefab và ScriptableObject **không chứa** dữ liệu level
nào: chúng chỉ là "máy chạy" level. Thêm hoặc sửa một level **không cần mở Unity**. Đây là machine zone: diff được,
review được, test headless được.

**Hai file cho mỗi level (2026-10-05).** Mỗi level gồm:
- **file level** (`level_NNNN.json`, phần *items*): chai trong từng hàng chờ, `initial`, các làn khay và khay đặc biệt,
  số slot, meta;
- **file conveyor** (`Conveyors/<id>.json`, phần *layout* của băng chuyền trên): số hàng, số chai mỗi hàng, vùng lấy,
  hình vòng băng, vị trí nhập của từng hàng chờ. **Không chứa chai.**

File conveyor là **thư viện dùng chung**: level ghi `"conveyor": "<id>"`, nhiều level có thể dùng cùng một conveyor
(22 seed level hiện dùng 9 conveyor). Đổi layout một conveyor thì mọi level dùng nó đổi theo, nên trước khi sửa hãy
xem level nào đang dùng nó (`LevelTool conveyors`).

### 6.1 Vị trí file

| File | Vai trò |
|---|---|
| `Assets/CapsChaos/Content/LevelConfig/level_0001.json` … | Một file cho mỗi level (TextAsset). Cả folder là **một** entry Addressables, address `LevelConfig`, nên mỗi file có address `LevelConfig/<id>.json` và level mới không cần đăng ký thêm |
| `Assets/CapsChaos/Content/LevelConfig/Conveyors/<id>.json` | Một file cho mỗi layout băng chuyền trên, dùng chung. Nằm trong cùng entry folder, address `LevelConfig/Conveyors/<id>.json`. Lúc Loading, `LevelConfigNode` chỉ tải các conveyor mà ít nhất một level có tên |
| `Assets/CapsChaos/Content/LevelConfig/levels.index.json` | Thứ tự chơi: `{ "order": ["level_0001", "level_0002", …] }` |
| `Game.Domain.CapColor` | Enum màu, dùng chung cho chai, tray, nắp và hộp. Trong JSON (v2) màu là **số** của enum (bảng §4) |
| [`docs/design/level.schema.json`](level.schema.json) | JSON Schema (draft 2020-12) của file level, là nguồn chân lý của định dạng |
| [`docs/design/conveyor.schema.json`](conveyor.schema.json) | JSON Schema của file conveyor |

### 6.2 Định dạng file level (format v4, đổi 2026-10-05)

Định dạng viết cho **designer** (họ có thể tự viết tool): màu là **số** (§4), mọi cờ là **field có tên**. Format v4
tách `loop` của v3 làm hai: layout chuyển sang file conveyor (§6.2b), level chỉ giữ chai. File v3 bị loader **từ chối**,
kèm lỗi chỉ về cách tách. Toàn bộ 22 level đã được tách 2026-10-05, nội dung giữ nguyên. File v1/v2 mô tả khối chai,
không có cách chuyển tương đương sang oval: loader **từ chối** và chỉ về `LevelTool generate`.

```json
{
  "$schema": "../../../../docs/design/level.schema.json",
  "formatVersion": 4,
  "id": "level_0019",
  "conveyor": "oval_20_2f",
  "slots": 4,
  "trayCapacity": 4,
  "colors": [1, 2, 3],
  "feeders": [
    { "bottles": [
      1, 1, 1, 1,
      2, 2, 2, 2,
      3, 3, 3, 3
    ] },
    { "bottles": [ 2, 2, 1, 1 ] }
  ],
  "lanes": [
    [{ "color": 3 }, { "color": 2, "hidden": true }],
    [{ "color": 1, "lockTurns": 2 }, { "color": 2 }],
    [{ "color": 1 }]
  ],
  "links": [
    { "a": { "lane": 0, "tray": 0 }, "b": { "lane": 0, "tray": 1 } }
  ],
  "view": { "cameraPreset": "default" },
  "meta": { "name": "Ví dụ", "difficulty": "easy", "notes": "Chỉ để minh hoạ định dạng" }
}
```

Mảng `lanes` phải có số khay đúng theo R16. Validator sẽ kiểm.

| Trường | Ý nghĩa |
|---|---|
| `formatVersion` | `4`. v3 (conveyor nằm trong level) bị từ chối, chỉ về cách tách; v1/v2 (khối chai) bị từ chối, chỉ về `LevelTool generate` |
| `conveyor` | Id của file conveyor dùng chung (`Conveyors/<id>.json`, §6.2b). Không có file đó ⇒ lỗi V1 `$.conveyor` |
| `slots` | Số slot mở sẵn, 1–6 (mặc định 4) |
| `extraSlots` | (tuỳ chọn, mặc định 2) số slot khoá mở được bằng coin / quảng cáo (R20); `slots + extraSlots ≤ 6` |
| `trayCapacity` | Số nắp mỗi khay (mặc định 4) |
| `colors` | Tập số màu dùng trong level; mọi màu trong `feeders`, `initial` và `lanes` phải thuộc tập này |
| `feeders[f].bottles` | Hàng chờ của feeder `f` **của conveyor** (đúng thứ tự trong file conveyor, đúng số lượng feeder), chai đầu trước, ghi theo hàng `width` chai (chai `i` ở track `i % width`). Bắt buộc nếu conveyor có feeder |
| `initial` | (tuỳ chọn) oval lúc bắt đầu, `rows` mảng × `width` số của conveyor (`0` = ô trống), hàng `r` bắt đầu ở vị trí `r`. Không có ⇒ oval bắt đầu trống, hàng chờ nhập dần (R4) |
| `lanes[j]` | Hàng đợi của băng khay `j` (trái → phải), mỗi phần tử là một khay. Phần tử `[0]` là khay đầu làn |
| `lanes[j][t].color` | Số màu của khay |
| `lanes[j][t].hidden` | (tuỳ chọn, mặc định `false`) khay **ẩn** (R17) |
| `lanes[j][t].lockTurns` | (tuỳ chọn) khay **khoá** `n` lượt, 1–99 (R18) |
| `links` | (tuỳ chọn) cặp khay **nối** (R19): `{ "a": { "lane": 0, "tray": 1 }, "b": { "lane": 1, "tray": 1 } }`, `tray` là chỉ số trong `lanes[lane]` |
| `view` | (tuỳ chọn) preset camera. Hình vòng băng nằm trong file conveyor |
| `meta` | (tuỳ chọn) tên, độ khó, ghi chú. Engine bỏ qua |
| `meta.solution` | (tuỳ chọn) chuỗi tap thắng (chỉ số làn), do LevelTool ghi. V6 chạy lại theo kiểu "tap rồi chờ bàn đứng yên" để chứng minh level giải được |

### 6.2b Định dạng file conveyor (format v1, thêm 2026-10-05)

Layout của băng chuyền trên (R1–R4), dùng chung giữa các level. Quy ước đặt id: `<hình>_<số hàng>_<số feeder>f`, ví dụ
`oval_16_1f`, `triangle_28_2f`. Id phải trùng tên file.

```json
{
  "$schema": "../../../../../docs/design/conveyor.schema.json",
  "formatVersion": 1,
  "id": "oval_20_2f",
  "rows": 20,
  "width": 4,
  "pickRows": 5,
  "shape": "oval",
  "feeders": [
    { "mergeAt": 15 },
    { "mergeAt": 8 }
  ],
  "meta": { "name": "Oval vừa, 2 hàng chờ" }
}
```

| Trường | Ý nghĩa |
|---|---|
| `formatVersion` | `1` |
| `id` | Chữ thường, số và `_`, bắt đầu bằng chữ, tối đa 48 ký tự; trùng tên file |
| `rows` | Số hàng quanh oval, 8–64 |
| `width` | (tuỳ chọn, mặc định 4) số chai mỗi hàng, 1–6 |
| `pickRows` | Số hàng của vùng lấy (giữa cạnh trước). V8: `rows ≥ 2 × pickRows + 6`. Với preset `oval`, cạnh thẳng dài đúng `pickRows` hàng; với hình khác, vùng lấy có thể tràn qua góc nếu cạnh trước ngắn hơn |
| `shape` | (tuỳ chọn, mặc định `"oval"`) hình vòng băng: `"oval"`, `"circle"`, `"triangle"`, hoặc `{ "points": [[x, z], …], "radius": r \| [r, …] }` (đa giác lồi bo góc, các đỉnh theo chiều kim đồng hồ nhìn từ trên, cạnh đỉnh 0 → 1 là cạnh trước). Chỉ để vẽ, luật không đọc. Writer luôn ghi rõ |
| `feeders[f].mergeAt` | Tối đa 2 hàng chờ. Vị trí hàng chờ `f` nhập vào oval. V8: ngoài vùng lấy, mỗi vị trí một hàng chờ |
| `meta` | (tuỳ chọn) `name`, `notes`. Engine bỏ qua |

### 6.3 Tham số chung (không nằm trong level)
Timing animation, easing, màu hex và SFX là **config key / design token**, dùng chung cho mọi level (§9,
Art §10). Một level chỉ mô tả **nội dung** của nó.

### 6.4 Validator (chặn khi load và chặn trong CI)
Validator là C# thuần trong `Game.Domain`. Nó chạy ở ba nơi: khi load level, trong
`pf-build.sh validate`, và trong `dotnet test SkuHeadlessTests`. V1 của conveyor là `ConveyorJson.Parse`; V8–V9 là
`ConveyorValidator`, và `LevelValidator` cũng gọi nó cho conveyor của level.

| # | Luật | Lỗi mẫu |
|---|---|---|
| V1 | JSON khớp schema | `$.stack.layers[1][2]: 6 cells ≠ cols 7` |
| V4 | R16 cân bằng từng màu | `color O: 18 bottles vs 4 trays×4=16` |
| V5 | Ký tự nằm trong `colors` | `unknown color 'X' in lanes[1][3]` |
| V7 | `locks`/`links` trỏ tới khay có thật; mỗi khay khoá tối đa 1 lần; cặp nối phải kề nhau (cùng làn liền nhau, hoặc 2 làn kề cùng vị trí); mỗi khay nằm trong tối đa 1 cặp; khay nối không được khoá | `V7 links[0]: lanes[0][0] and lanes[1][1] are not neighbours` |
| V8 | Conveyor: `rows ≥ 2 × pickRows + 6`; mỗi `mergeAt` nằm trên đường chạy, ngoài vùng lấy, không trùng nhau. Level: hàng chờ có ít nhất 1 chai | `V8 conveyor oval_20_2f.feeders[0].mergeAt: 2 is inside the pick zone 0..4` |
| V9 | `shape` tự khai của conveyor: ≥ 3 đỉnh, theo chiều kim đồng hồ, lồi; cạnh đầu là cạnh trước (nằm ngang, thấp nhất, phải → trái); bán kính > 0 và hai góc kề nhau không bo quá chiều dài cạnh giữa chúng | `$.shape.radius: corners 0 and 1 are rounded more than their edge (4) allows` |
| V6 | **Có lời giải**. Người chơi giả định "tap rồi chờ bàn đứng yên" (`CapChaosGame.Settle`) — mọi chuỗi thắng của người chơi đó cũng là chuỗi thắng thật. Nếu level có `meta.solution` thì **chạy lại** chuỗi tap đó (nhanh, chắc chắn). Nếu không thì solver DFS có memo, kèm budget node; vượt budget ⇒ `Unknown`, không bao giờ đoán | `V6 Unsolvable` / `V6 Unknown` |

V6 là cửa CI: level không giải được thì không ship.

### 6.5 Đòn bẩy độ khó

| Đòn bẩy | Dễ | Khó |
|---|---|---|
| Số màu | 2–3 | 6–8 |
| Kích thước oval (`rows`) | Nhỏ, chai quay lại nhanh | Lớn, phải chờ lâu mới tới màu cần |
| Số chai trong hàng chờ | 0 (mọi chai lên oval ngay) | Nhiều, màu cần nằm sâu trong hàng chờ |
| Số hàng chờ / điểm nhập | 1 | 3–4, nhập ở nhiều chỗ |
| Độ cụm màu (`clustering`) | Khối dài một màu | Xen kẽ từng chai |
| Thứ tự khay | Khớp với màu đang trên oval | Lệch pha, người chơi phải nhìn trước màu trong hàng chờ |
| Số slot (`"slots"`, 1–5, cấu hình theo từng level) | 4–5 (level tutorial) | 3 |

**Đường cong đề xuất** [GĐ]:
- L1–3: oval nhỏ, mọi chai đã lên oval, 2–3 màu; dạy tap, vùng lấy, đóng thùng.
- L4–6: có hàng chờ; dạy chai mới nhập vào chỗ trống, rồi 2 điểm nhập.
- L7–10: oval lớn hơn, hàng chờ dài, `greed` thấp dần — deadlock thật.
- L11+: 3 hàng chờ, 5 màu, oval 28–32 hàng (giống video tham chiếu).

**Seed levels của MVP:** 15 level sinh tự động (sinh lại 2026-10-02 cho oval); `level_0012` mô phỏng video tham chiếu
(một hàng chờ dài nuôi oval đông). `level_0016`–`level_0018` viết tay (không có trong spec của LevelTool), mỗi level dạy
một loại khay đặc biệt: ẩn, khoá, nối (R17–R19). `generate` giữ các level viết tay ở cuối index.

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
dotnet run --project Tools/LevelTool -- validate          # V1–V9 cho mọi conveyor và level + index; báo conveyor không ai dùng
dotnet run --project Tools/LevelTool -- conveyors         # liệt kê conveyor và các level đang dùng mỗi cái
dotnet run --project Tools/LevelTool -- stats             # độ khó: tỉ lệ thắng khi tap ngẫu nhiên + effort của solver
dotnet run --project Tools/LevelTool -- migrate           # ghi lại mọi conveyor và level theo layout hiện tại, nội dung giữ nguyên; --check: exit 1 nếu còn file khác
```

- Spec nằm ở `Tools/LevelTool/seed-levels.json`, gồm: `conveyor` (id trong `Conveyors/`), `feeders` (số chai cho
  từng feeder của conveyor đó, theo thứ tự), bộ màu, `greed` (1 = dễ), `clustering` (1 = khối dài một màu), và `seed`. Tool là nơi giữ
  seed (luật #14). Người chơi giả định của `generate`, `validate` và `stats` tap khi bàn đứng yên.
- `generate` **không bao giờ** ghi file conveyor; muốn layout mới thì tạo file trong `Conveyors/` trước.
- **⚠ `generate` ghi đè** các level có trong spec. Level nào designer đã sửa tay thì **xoá khỏi spec**
  (hoặc đổi id) trước khi chạy lại. Level viết tay không cần `meta.solution`; V6 sẽ dùng solver.

**Độ khó của seed levels** (`stats`, 200 ván tap ngẫu nhiên khi bàn đứng yên, seed 20260930, sinh lại 2026-10-02).
Với băng oval, mọi seed level hiện thắng 100 % khi tap ngẫu nhiên (trừ L17: 93 %). Độ khó do designer quyết định qua
thứ tự khay (`lanes`) — quyết định 2026-10-02.

| Level | 1–16 | 17 | 18 |
|---|---|---|---|
| Thắng ngẫu nhiên | 100 % | 93 % | 100 % |

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
2. ~~Nắp bắn từ khay lên chụp vào cổ chai~~ — **bỏ 2026-10-02** (product owner): chai bay thẳng vào hốc khay.
3. Chồng chai **rơi** xuống có nảy nhẹ và bụi ở chân chai [QS].
4. **Reveal**: cầu vồng tan ra thành màu thật, kèm flash và vài hạt lấp lánh [CHỐT D3].
5. Băng chuyền chạy mượt; khay có quán tính nhẹ khi dừng [QS].
6. Khay đầy **đậy nắp** rồi bay đi (sửa 2026-10-02: bỏ thùng carton 3 nhịp úp, gập, dán).
7. [GĐ] Haptic nhẹ khi đóng nắp, vừa khi đóng thùng, mạnh khi thắng.

---

## 9. Tham số tuning (config keys, chung cho mọi level)

| Key | Mặc định | Nguồn |
|---|---|---|
| `economy.startCoins` | 1000 | coin lần chạy đầu (R20) |
| `economy.winReward` | 50 | coin mỗi lần thắng |
| `slots.unlockPrice` | 300 | Parking Slot (R20) |
| `slots.rescuePrice` | 900 | Out of Slot (R20) |
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
| Màu UI | `Content/UI/UiPalette.asset` (`Views/Art/UiPalette.cs`) | Màu của mọi `UiTint` (nền, pill, nút HUD, icon) và mọi `TextTint` (chữ TMP, key là `TintFlavor`: `None` = trắng, các flavor mặc định lấy màu thân). Mỗi text TMP mới tự được gắn `TextTint` nhờ hook `Editor/TextTintAutoAdd.cs` chỉnh trong Inspector, thấy ngay kể cả lúc Play, không cần biên dịch. Lệch quy ước "token là code" của framework, do chủ SKU chọn (2026-10-01). `DesignTokens.cs` giữ giá trị mặc định; spacing, cỡ chữ, màu gameplay 3D vẫn ở code. Enum `UiToken` được lưu bằng số: chỉ thêm vào cuối. Palette là entry Addressables `UiPalette` (group `Shared`), được `UiPaletteProvider` (singleton Root scope) load một lần ở giai đoạn Loading qua `UiPaletteNode`. Controller widget đẩy palette xuống `UiTint`; không prefab nào tham chiếu trực tiếp tới asset |
| Popup Win / Lose | `Features/ResultDialog/…/ResultDialog.cs`, `Views/ResultDialog/ResultDialogView.cs`, prefab `Content/UI/Result/Prefabs/ResultDialog.prefab` | Một prefab, hai theme (token `Win*`/`Lose*` trong `UiPalette`). Dialog modal: nền dim trung tính và blocker của framework (chủ SKU chọn, 2026-10-01; không dim theo màu theme như ảnh). Panel 3 dải + nút chính hai tông, vào bằng scale 0,8→1 + fade. NEXT ⇒ level kế, RESTART ⇒ chơi lại, Back ⇒ Main. Chữ dùng `TextTint` key `White`, material `LilitaOne-Regular-outline-black` |
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
4. ~~Hé lộ màu chai ẩn~~ — chai ẩn đã bỏ (D3, 2026-10-02).
5. ~~Độ khó của băng oval~~ — chốt 2026-10-02: designer chỉnh qua thứ tự khay; tối đa 8 màu, 4 slot + 2 slot mở thêm.
