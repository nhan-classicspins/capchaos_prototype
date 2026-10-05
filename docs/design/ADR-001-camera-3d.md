# ADR-001 — Camera 3D perspective cho board Cap Chaos

| | |
|---|---|
| **Trạng thái** | **Đã chốt: phương án B** (2026-09-30, product owner) · **sửa 2026-10-02: camera `GamePlay` về orthographic** (§7) |
| **Bối cảnh** | GDD D1: camera 3D perspective [CHỐT] · GDD §10.1 |
| **Loại** | Spine change (luật #7, `architecture-map.md` §5) |

## 1. Kiểm tra ba điều kiện (luật #7)

| Điều kiện | Board 3D có cần không |
|---|---|
| 1. Thứ tự tuyệt đối so với một layer chuẩn | Không bắt buộc |
| 2. **Cấu hình camera riêng** | **Có**: projection perspective, FOV, có thể thêm post-processing |
| 3. Bật/tắt culling theo nhóm | Không |

Thoả điều kiện 2, nên **được phép có RenderLayer riêng**. Brief framework §863 nêu đích danh trường hợp
"projection perspective cho board 3D".

## 2. Hiện trạng framework 0.4.0 (đã đọc code)

| Thành phần | Trạng thái | File |
|---|---|---|
| Codegen `game.layers.json` (band, budget, `physicsRaycast: "3d"`) | ✅ có | `Tools~/Codegen/Core/LayerManifestValidation.cs` |
| `WorldSpaceCanvasScaler` với camera perspective | ✅ có (nhánh FOV, `planeDistance`) | `Runtime/Views/WorldSpaceCanvasScaler.cs:303,401` |
| `WorldViewport` với camera perspective | ✅ có ("perspective fallback R-3") | `Runtime/Infrastructure/Rendering/WorldViewport.cs:99` |
| **`RootLifetimeScope` bind camera/host cho layer của SKU** | ❌ **thiếu**: chỉ có 6 field cố định, `RenderLayerRegistry` sẽ throw "unknown RenderLayerId" | `Runtime/Composition/RootLifetimeScope.cs:40–61,136–142` |
| **`RenderRigBuildCheck` chấp nhận camera của layer SKU** | ❌ **thiếu**: `ExpectedFromManifest()` chỉ đọc `framework.layers.json`, đòi *exactly 6 cameras*, nên thêm camera thứ 7 là **fail build** | `Editor/Rendering/RenderRigBuildCheck.cs:214,502` |

**Kết luận:** tài liệu cho phép layer SKU ("declare it in game.layers.json and wire it into the rig"),
nhưng chưa có đường nào để nối nó vào rig. Đây là **lỗ hổng của framework**, SKU không vá được: luật
cấm sửa framework từ repo SKU.

## 3. Phương án

### A. Layer SKU `Board3D` + hoàn thiện hỗ trợ game layer trong framework — **khuyên dùng**

- **SKU:** `game.layers.json` khai báo `{ "id": "Board3D", "order": -5, "physicsRaycast": "3d" }`, nằm dưới
  `GamePlay` và trên `UnderGamePlay`. Rig có thêm `Board3DCamera` (perspective, Overlay trong stack) và
  `Board3DHost`.
- **Framework** (sửa trong repo framework ở chế độ dev-link, chạy 4 gate, bump version):
  1. `RootLifetimeScope`: thêm danh sách serialize `_gameLayerBindings` gồm `{ id, camera, host }`, được
     đưa vào cùng `RenderLayerRegistry`.
  2. `RenderRigBuildCheck`: expected = framework **+** `game.layers.json`; stack order xét toàn bộ band.
  3. Doctor check 4 đếm cả camera của layer SKU.
  4. Test headless và EditMode cho cả 3 điểm trên.
- **Ưu:** đúng thiết kế của brief; 2D `GamePlay` và ruler 1px = 1unit giữ nguyên; mọi SKU sau cần board 3D
  dùng lại được.
- **Nhược:** là spine change của framework, phải qua `bmad-architecture` **trong repo framework**, và ảnh
  hưởng mọi SKU. Ước tính 1–2 ngày.

### B. Chuyển camera `GamePlay` có sẵn sang perspective (dùng nhánh R-3)

- Không thêm layer. Board 3D đặt dưới `WorldRoot` của `GamePlay`.
- **Nhược:**
  - Ruler 1px = 1unit chỉ còn đúng trên mặt phẳng canvas; mọi nội dung 2D của GamePlay phải tính lại.
  - `GamePlay` khai báo cứng `physicsRaycast: "2d"` (manifest framework, chỉ đọc), nên tap vào khay 3D phải
    đi đường vòng (vùng chạm uGUI trên layer `Ui`).
  - Phải sửa rig Master scene: việc Ask-First, human zone.
- **Ưu:** không sửa framework; làm được ngay.
- **Rủi ro:** lệch khỏi thiết kế framework; khi framework có phương án A thì phải migrate.

## 4. Đề xuất

Chọn **A**. Trong lúc framework đang làm A, SKU vẫn tiến độ được ở những phần **không phụ thuộc camera**:
Domain (luật chơi, lưới 3D, trọng lực, reveal), parser và validator JSON, solver, và các test headless.

## 5. Quyết định: B, và cách triển khai

Product owner chọn B vì ưu tiên tốc độ; nợ chuyển sang A được chấp nhận (§3). Những gì đã làm:

**Rig (Master scene, sửa qua editor sống; diff 2 dòng):** `GamePlayCamera`
`orthographic 1 → 0`, `field of view 60 → 30`. Vị trí và hướng camera **giữ nguyên**: tại `z = −1000`,
nhìn về `+z`, giống 5 camera còn lại. `GamePlayHost` vẫn cách camera `planeDistance = 1000`; scaler đi
nhánh perspective, `localScale ≈ 0.28` (chiều cao frustum 535.9 / 1920). Boot smoke tới
`[MainScreen] entered.` đạt.

**Quy ước bắt buộc cho các View của board** (đọc trước khi code màn Gameplay):

1. **Board tự đặt mình trước camera.** Camera không nghiêng; board nghiêng.
   - Root `BoardRig` nằm dưới `WorldRoot` của GamePlay và được `Stamp` vào layer GamePlay (luật #16).
   - Pose tương đối với camera: xoay **`Euler(−60, 0, 0)`**; điểm focus `(0, 0, 1.2)` của board
     (toạ độ local) nằm trên trục nhìn, cách camera **19.5 unit**. Tức là
     `root.position = cam.position + cam.forward·19.5 − root.rotation·(0, 0, 1.2)`.
   - Kết quả là góc nhìn giống hệt ảnh preview (FOV 30 / pitch 60 / khoảng cách 19.5 / focus z 1.2,
     trong `ArtPreview.cs`).
   - Đơn vị giữ nguyên: 1 unit = chiều cao một chai.
2. **Luật 1 px = 1 unit không còn đúng trên layer GamePlay**, ngoại trừ tại mặt phẳng canvas. Mọi nội
   dung 2D (điểm số nổi, coin bay…) đặt ở `OverlayGamePlay` hoặc `Ui`, **không** đặt ở GamePlay.
3. **Tap đi đường vòng.** GamePlay khai `physicsRaycast: "2d"`, nên framework gắn
   `Physics2DRaycaster`. Log boot đã xác nhận: *"attached Physics2DRaycaster to camera
   'GamePlayCamera'"*. Collider 3D **không** nhận được event. Cách làm:
   - một `Image` trong suốt full-screen (raycast target) trên `GamePlayHost` làm hit-catcher;
   - View nhận `IPointerClickHandler`, tự `Physics.Raycast(cam.ScreenPointToRay(pos))` vào collider của
     khay đầu làn, rồi báo **chỉ số làn** cho controller. Controller không bao giờ thấy toạ độ màn hình
     (luật #10), và không có input polling (luật #9).
4. **Ánh sáng:** directional light của board nằm trong scene Gameplay, tức là trong scope của scene,
   không đặt trong Master.
5. **Không `Stamp` nội dung 3D** (phát hiện khi làm màn Gameplay). `IRenderLayerRegistry.Stamp` đưa
   **`localPosition.z` về 0 cho cả cây object** (hợp đồng của rig 2D; xem
   `Runtime/Infrastructure/Rendering/RenderLayerRegistry.cs` `StampRecursive`), nên sẽ ép phẳng board.
   - Board 3D chỉ gán **culling layer của host GamePlay** (`GetHost(GamePlay).gameObject.layer` = `PFGamePlay`)
     cho cả cây object, không đụng vị trí. Không cần SortingLayer vì mesh 3D đã sắp theo depth.
   - Riêng hit-catcher (uGUI trên `GamePlayHost`) vẫn `Stamp` bình thường.
   - Khi chuyển sang phương án A, layer `Board3D` nên có quy ước riêng cho việc này.

**Chưa kiểm được ở máy local:** `RenderRigBuildCheck` (chạy lúc build) và suite PlayMode chỉ chạy trên
CI. Đã đọc code: build check không kiểm projection, nhưng **CI mới là bằng chứng**.

## 6. Việc cần làm sau khi chốt (tham khảo)

- A: mở story trong repo framework ("SKU game-layer rig bindings") → `bmad-architecture` → dev → gate →
  bump → cập nhật `packages-lock` bên SKU → thêm `game.layers.json` + rig Board3D.
- B: ghi Design Note vào GDD §10.1; sửa rig qua editor sống (luật #12); làm vùng chạm uGUI cho 3 làn.

## 7. Sửa đổi 2026-10-02: camera `GamePlay` về orthographic

Product owner yêu cầu chuyển camera `GamePlay` về **orthographic** (cùng lúc với băng oval, GDD D6). Các quy ước của §5
vẫn giữ nguyên (board tự đặt mình và nghiêng −60° trước camera, không `Stamp` nội dung 3D, tap đi qua hit-catcher uGUI);
chỉ khác cách lấy kích thước:

- **Rig (Master scene, sửa qua editor sống; diff 2 dòng):** `GamePlayCamera` `orthographic 0 → 1`, `field of view 30 → 60`
  (giá trị mặc định, không dùng khi ortho). `orthographicSize` vẫn do `WorldSpaceCanvasScaler` quản lý (960).
- **Board scale theo viewport.** Với ortho, khoảng cách không làm vật nhỏ đi, nên board phải tự scale:
  `scale = IWorldViewport.SafeRect.height / ViewHeight`, với `ViewHeight = 11.6` đơn vị board (ban đầu 10.45 = khung
  hình rig perspective cũ; nới ra 2026-10-02 để thấy hàng chờ rõ hơn). Tính theo **safe rect** chứ không theo `HalfHeight`:
  màn hình cao hơn 9:16 chỉ thấy thêm board ở trên/dưới, không bao giờ bị cắt hai bên. Board đặt cách camera `ViewDistance = 1000` world unit (mặt phẳng canvas), điểm
  focus nằm trên trục nhìn. Controller đọc `IWorldViewport` mỗi tick và gọi lại `BoardView.PlaceInFrontOf`, không bao giờ
  dùng `orthographicSize` như hằng số.
- **Bóng đổ.** Board giờ cách camera khoảng 1000 world unit, xa hơn `m_ShadowDistance = 50` mặc định của URP nên mất bóng.
  `PC_RPAsset` / `Mobile_RPAsset` nâng `m_ShadowDistance` lên **2200**. Các layer 2D không dùng bóng nên không bị ảnh hưởng.
- **Hệ quả:** không còn phối cảnh (vật ở xa không nhỏ đi). Khung hình được giữ bằng `ViewHeight`; nếu cần to/nhỏ hơn, chỉnh
  token đó.

## 8. Sửa đổi 2026-10-05: board nằm trên mặt phẳng XZ, camera cúi xuống

Product owner yêu cầu gameplay render trên **mặt phẳng XZ của world**, bố cục trên màn hình giữ nguyên. Thay quy ước §5.1
("camera không nghiêng; board nghiêng"):

- **Board nằm phẳng.** Root board dưới `WorldRoot` có rotation world = identity: x sang phải, z ra xa người chơi, y hướng lên.
  Vị trí và scale tính như cũ (`scale = SafeRect.height / ViewHeight`; điểm focus `(0, 0, FocusZ)` nằm trên trục nhìn, cách
  camera `ViewDistance`).
- **Camera cúi xuống.** `BoardView.Frame` giữ nguyên **vị trí** camera `GamePlay` trong rig, chỉ đặt rotation
  `Euler(CameraPitchDegrees = 60, 0, 0)`. Tư thế tương đối giữa camera và board y hệt trước (`cam⁻¹·board = Euler(−60, 0, 0)`),
  và camera orthographic, nên ảnh không đổi. Ánh sáng (`Sun` là con của board), đường bay, rung, nảy đều tính theo toạ độ
  local của board, nên cũng không đổi.
- **Camera là của chung, nên phải trả lại.** `GameplayScreen` nhớ rotation của rig khi dựng board lần đầu, và đặt lại khi
  rời màn (`OnExit`, Home, `OnUnloadAsync`). Rig trong Master scene **không sửa**: 6 camera vẫn nhìn `+z` khi không ở
  Gameplay.
- **UI và tap.** `WorldSpaceCanvasScaler` đặt lại `GamePlayHost` theo vị trí và hướng camera mỗi `LateUpdate`
  (`PositionInFrustum`), nên hit-catcher vẫn phủ kín màn. Tap vẫn là `cam.ScreenPointToRay` + `Physics.Raycast`.
- **Hệ quả:** `IWorldViewport.SafeRect` chỉ được dùng để lấy **kích thước** (chiều cao), không dùng để lấy vị trí: rect đó nằm
  trên mặt phẳng canvas, giờ đã nghiêng so với world. Không đặt nội dung 2D theo world-rect lên layer `GamePlay` (§5.2 vẫn giữ).
