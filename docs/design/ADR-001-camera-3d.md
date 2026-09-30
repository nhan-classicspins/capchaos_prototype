# ADR-001 — Camera 3D perspective cho board Cap Chaos

| | |
|---|---|
| **Trạng thái** | **Đề xuất — chờ chọn phương án** (2026-09-30) |
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

## 5. Việc cần làm sau khi chốt

- A: mở story trong repo framework ("SKU game-layer rig bindings") → `bmad-architecture` → dev → gate →
  bump → cập nhật `packages-lock` bên SKU → thêm `game.layers.json` + rig Board3D.
- B: ghi Design Note vào GDD §10.1; sửa rig qua editor sống (luật #12); làm vùng chạm uGUI cho 3 làn.
