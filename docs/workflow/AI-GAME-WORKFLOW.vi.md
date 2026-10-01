# Quy trình làm game bằng AI

> **Phiên bản 2.0 · 28-09-2026** · Bản tiếng Anh: [AI-GAME-WORKFLOW.en.md](AI-GAME-WORKFLOW.en.md) (cùng nội dung; sửa thì sửa cả hai).
> Bộ rule không phụ thuộc project, dùng để làm game có AI hỗ trợ. Tài liệu viết cho hai người đọc:
> **AI**, phải làm theo từng bước; và **team người làm**, là người ra quyết định.
> Nếu project dùng framework có rule riêng, các rule đó cũng áp dụng và được ưu tiên ở phần kỹ thuật chi tiết.

## Mục lục

1. [Cách dùng tài liệu này](#1-cách-dùng-tài-liệu-này)
2. [Quy trình 7 bước đã đủ chưa? (đánh giá)](#2-quy-trình-7-bước-đã-đủ-chưa-đánh-giá)
3. [Vai trò và quyền quyết định](#3-vai-trò-và-quyền-quyết-định)
4. [Cách làm việc: AI đề xuất, hỏi, người làm chốt](#4-cách-làm-việc-ai-đề-xuất-hỏi-người-làm-chốt)
5. [Rule chung](#5-rule-chung)
6. [Tổng quan quy trình](#6-tổng-quan-quy-trình)
7. [Chi tiết từng bước (Bước 0 → Bước 8)](#7-chi-tiết-từng-bước)
8. [Chọn tool animation (Spine, DragonBones, có sẵn của engine)](#8-chọn-tool-animation)
9. [Kinh nghiệm tích lũy](#9-kinh-nghiệm-tích-lũy)
10. [Mẫu tài liệu](#10-mẫu-tài-liệu)
11. [Lịch sử thay đổi](#11-lịch-sử-thay-đổi)

---

## 1. Cách dùng tài liệu này

**Với team người làm**

1. Copy file này (cả hai ngôn ngữ) vào `docs/workflow/` của project game.
2. Thêm một dòng vào hướng dẫn cho AI của project (`AGENTS.md`, `CLAUDE.md` hoặc tương đương):
   *"Làm theo `docs/workflow/AI-GAME-WORKFLOW.vi.md`. Đọc `docs/workflow/STATE.md` và `docs/workflow/DECISIONS.md` trước khi làm bất cứ việc gì."*
3. Tạo `STATE.md` và `DECISIONS.md` theo mẫu ở [§10](#10-mẫu-tài-liệu).
4. Chốt ai duyệt từng cổng (§3) và ghi tên vào `STATE.md`.

**Với AI — đầu mỗi phiên làm việc**

1. Đọc `STATE.md` (đang ở bước nào, câu hỏi còn mở, việc tiếp theo) và `DECISIONS.md`.
2. Báo ngắn gọn trong một tin nhắn: đang ở bước nào, đã xong gì, đang chờ người làm chốt gì.
3. Không bắt đầu bước sau khi cổng của bước hiện tại chưa được duyệt, trừ phần được làm song song ở §6.
4. Làm theo quy trình ở §4 cho mọi đầu việc.
5. Cuối phiên, cập nhật `STATE.md`.

---

## 2. Quy trình 7 bước đã đủ chưa? (đánh giá)

7 bước ban đầu gồm:

1. Tài liệu thiết kế game.
2. Mock-up.
3. Tách art vào file thiết kế và kiểm tra lỗi.
4. Spine animation.
5. Logic game.
6. Ráp vào game.
7. AI kiểm thử so với mock-up.

Bảy bước này bao được **dây chuyền sản xuất chính**. Khi đối chiếu với những gì một game puzzle casual thật sự cần (engine luật, 60 level, kinh tế, thưởng hằng ngày, shop, FTUE, âm thanh, localization, build Android và hiệu năng), vẫn còn nhiều việc chưa có chỗ trong 7 bước.

| Thực tế cần | 7 bước đã có chưa? | Cách xử lý trong tài liệu này |
|---|---|---|
| Ý tưởng, phân tích game tham khảo, phạm vi, nền tảng, chọn công nghệ | Chưa (chỉ ngầm hiểu) | Thêm **Bước 0 — Khởi động** |
| Nội dung level (generator, solver, đường cong độ khó) | Một phần (bước 1 có nhắc level) | **Nhánh Level** trong Bước 1 và 5 |
| Kinh tế và các con số cân bằng | Một phần | **Bảng kinh tế** ở Bước 1, dữ liệu config ở Bước 5 |
| Âm thanh (SFX, nhạc) | Chưa | **Nhánh Âm thanh** trong Bước 1, 4, 6 |
| Text và localization | Chưa | **Nhánh Text** trong Bước 1 và 6 |
| Dựng project và kiến trúc | Chưa | Một phần Bước 0 (quyết định) và Bước 5 |
| Ngân sách hiệu năng, test trên máy thật | Chưa | Ngân sách ở Bước 0, kiểm tra ở Bước 7 và 8 |
| Người chơi thử (độ vui, độ khó) | Chưa (chỉ có AI test) | **Bước 8** |
| Build, store, SDK (quảng cáo/IAP/analytics), phát hành | Chưa | **Bước 8** |
| Quay lại khi bước sau phát hiện lỗi thiết kế | Chưa | **Rule yêu cầu thay đổi** ở §4.4 |

**Quyết định trong phiên bản này:** giữ nguyên 7 bước lõi, cùng số thứ tự và ý nghĩa. Thêm **Bước 0** (khởi động) ở đầu và **Bước 8** (máy thật, người chơi thử, phát hành) ở cuối. Âm thanh, text, level và kinh tế chạy dưới dạng **nhánh** xuyên qua các bước có sẵn, không tách thành bước riêng, để quy trình vẫn dễ theo.

---

## 3. Vai trò và quyền quyết định

| Quyết định | Người làm chốt | AI làm gì |
|---|---|---|
| Ý tưởng game, phạm vi, danh sách tính năng MVP | ✅ | đưa phương án, ước lượng công sức, nêu rủi ro |
| Luật chơi, đường cong level, mục tiêu kinh tế | ✅ | soạn nháp, mô phỏng, chỉ ra chỗ mơ hồ |
| Hướng hình ảnh, duyệt mock-up | ✅ | tạo nhiều phương án, kiểm tra tính nhất quán |
| Duyệt art, lỗi nào chấp nhận được | ✅ | làm/sửa art, chạy QA tự động |
| Tool, engine, framework, license trả phí, SDK | ✅ | so sánh, thử trên mẫu nhỏ, khuyến nghị |
| Chi tiết kiến trúc trong framework đã duyệt | được báo | tự quyết và ghi lại (hỏi nếu làm đổi cấu trúc) |
| Tiêu chí pass, khác biệt chấp nhận, phát hành | ✅ | test, báo bằng chứng, khuyến nghị có nên phát hành không |
| Mọi việc phá hủy, công khai ra ngoài hoặc tốn tiền | ✅ luôn luôn | không bao giờ làm khi chưa được đồng ý rõ ràng |

Mỗi bước có một **người duyệt** được chỉ định. Khi người duyệt vắng, AI chỉ được làm tiếp những phần không phụ thuộc vào quyết định đang chờ, và phải nói rõ điều đó.

---

## 4. Cách làm việc: AI đề xuất, hỏi, người làm chốt

### 4.1 Vòng lặp cho mọi bước

```
 ┌─ 1 BẮT ĐẦU  đọc STATE/DECISIONS, liệt kê đầu vào, xác nhận mục tiêu của bước
 │  2 ĐỀ XUẤT  kế hoạch ngắn + các phương án, trong đó có một khuyến nghị (kèm lý do)
 │  3 HỎI      các câu hỏi cần chốt của bước (§7), gom theo nhóm, có mặc định
 │  4 CHỐT     chờ câu trả lời; ghi vào DECISIONS.md
 │  5 LÀM      tạo các đầu ra
 │  6 KIỂM TRA chạy các kiểm tra của bước; thu bằng chứng
 │  7 TRÌNH    đưa đầu ra + bằng chứng + vấn đề còn mở cho người duyệt
 └─ 8 ĐÓNG     người duyệt đồng ý → cập nhật STATE (qua cổng) → bước tiếp
               người duyệt không đồng ý → quay về bước 2 hoặc 5, ghi lại góp ý
```

### 4.2 AI hỏi thế nào

- Chỉ hỏi **những gì làm thay đổi kết quả**. Không hỏi điều đã có trong tài liệu đã duyệt hoặc có mặc định hợp lý.
- Tối đa **5 câu mỗi lượt**, gom theo chủ đề, câu quan trọng nhất đặt trước.
- Mỗi câu có **2–4 phương án cụ thể**. Đánh dấu một phương án **(khuyến nghị)** và giải thích một dòng. Cho phép trả lời tự do.
- Nói rõ AI sẽ giả định gì nếu không có câu trả lời, **trừ các mục "bắt buộc chốt"** (đánh dấu 🔒 ở §7). Với các mục này AI phải chờ, không được tự giả định.
- Dùng ngôn ngữ của team, tránh thuật ngữ khó. Có ví dụ hoặc phác thảo nhỏ nếu giúp người làm dễ chọn.
- Sau khi có câu trả lời, nhắc lại quyết định cuối trong một dòng và ghi vào `DECISIONS.md`.

Ví dụ:

> **C1 🔒 Đăng nhập.** Người chơi được nhận diện thế nào?
> a) Chỉ khách, lưu trên máy **(khuyến nghị cho MVP: không cần backend)** · b) Khách + đăng nhập Google/Apple, có cloud save · c) Tài khoản email.
> *Nếu chưa có câu trả lời, tôi sẽ không thiết kế phần tài khoản.*

### 4.3 AI báo cáo thế nào

Mọi báo cáo cuối bước (hoặc cuối phiên) có cùng một khung:

1. **Đã xong:** các đầu ra, kèm link.
2. **Bằng chứng:** đã kiểm tra gì, bằng cách nào, kết quả ra sao. Ghi rõ kiểm tra nào *chưa* chạy.
3. **Vấn đề còn mở:** chặn và không chặn.
4. **Cần bạn chốt:** câu hỏi theo khung §4.2.
5. **Tiếp theo:** sau khi được duyệt thì làm gì.

### 4.4 Yêu cầu thay đổi (quay lại bước trước)

Khi bước sau phát hiện vấn đề ở một đầu ra đã duyệt của bước trước (luật không thể làm, mock-up không vừa màn hình, level quá khó):

1. AI dừng phần việc đó và viết một **yêu cầu thay đổi**: vấn đề gì, vì sao, ảnh hưởng mục đã duyệt nào, các phương án và một khuyến nghị.
2. Người duyệt của *bước trước* ra quyết định.
3. Tài liệu đã duyệt được cập nhật thành phiên bản mới, kèm một dòng trong lịch sử thay đổi. Các phần phía sau phụ thuộc vào nó được liệt kê và kiểm tra lại.

AI không bao giờ âm thầm sửa thiết kế, mock-up hay art đã duyệt để bước hiện tại dễ làm hơn.

---

## 5. Rule chung

**Quyết định và nguồn**
- **G1. Mỗi bước có một nguồn đã duyệt.** Chỉ đầu ra đã duyệt và có phiên bản mới được chuyển sang bước sau (`lobby-mockup-v03`). Mọi thứ khác là nháp.
- **G2. Quyết định phải được ghi lại.** Quyết định chưa có trong `DECISIONS.md` coi như chưa có.
- **G3. Người làm duyệt thiết kế, hình ảnh, art, tool tốn tiền và phát hành.** AI không bao giờ tự duyệt việc mình làm.

**Sự thật và bằng chứng**
- **G4. Có bằng chứng rồi mới được nói.** "Xong", "chạy được", "nhìn đúng" phải có bằng chứng thấy trong phiên hiện tại: output test, ảnh chụp đã thực sự xem, log đã đọc. Báo cáo cũ không chứng minh gì cho hôm nay.
- **G5. Nói rõ loại bằng chứng.** Editor, bản build, máy thật và người chơi thử là các loại khác nhau. Tỉ lệ thắng của bot không phải tỉ lệ thắng của người chơi.
- **G6. Báo cả những gì chưa làm.** Kiểm tra bị bỏ qua, nền tảng chưa test và các giả định phải được liệt kê rõ.

**Asset và pháp lý**
- **G7. Lưu nguồn gốc.** Mỗi asset AI sinh (ảnh, âm thanh, text, level) phải lưu prompt, tool/model, ngày và đường dẫn output. Ảnh AI vẽ phải ghi là ảnh thiết kế, không phải ảnh chụp game.
- **G8. Game tham khảo để học, không để copy.** Được nghiên cứu nhịp độ, cấu trúc và khoảng giá trị. Không ship art, âm thanh, text hay dữ liệu level của họ. Ghi lại đã tham khảo những gì.
- **G9. Kiểm tra license** của tool, runtime, font, âm thanh và SDK trước khi đưa vào project.
- **G10. Không để lộ bí mật** (token, key, thông tin tài khoản, chủ license) trong tài liệu, log hay commit.

**Tay nghề**
- **G11. Chỉnh qua tool.** Scene, prefab, file thiết kế nhiều layer, project animation được chỉnh qua editor hoặc API tự động hóa của nó, không viết lại file thô.
- **G12. File sinh tự động thì sinh lại, không sửa tay.**
- **G13. Giữ nguồn chỉnh sửa được.** Giữ PSD, project animation và generator level, không chỉ giữ file xuất.
- **G14. Đặt ngân sách cho vòng lặp tốn kém.** Thống nhất giới hạn số lần sinh ảnh, phiên chơi thử và bản build cho mỗi bước. Tool timeout không có nghĩa là thao tác đã dừng: kiểm tra trạng thái rồi mới thử lại.
- **G15. Bảo vệ dữ liệu người dùng.** Backup và khôi phục file save khi test. Không test trên tiến trình thật của ai đó khi chưa backup.

**Code game (áp dụng trừ khi framework quy định khác)**
- **G16.** Luật game nằm trong model thuần, không phụ thuộc engine; random và thời gian được truyền vào; seed được log lại.
- **G17.** View chỉ hiển thị dữ liệu và chuyển input đi. Việc quyết định thuộc về controller.
- **G18.** Mọi thay đổi bền vững (nước đi, phần thưởng, mua hàng) là một commit nguyên tử: đổi dữ liệu, rồi lưu, và khôi phục toàn bộ nếu lưu lỗi.
- **G19.** Phần thưởng được lưu trước khi hiển thị và không bao giờ bị trao hai lần. Đóng hoặc hủy dialog không bao giờ trao thưởng.
- **G20.** Con số cân bằng và nội dung level là dữ liệu (file config/level có tên, mặc định, khoảng hợp lệ và revision nội dung), không phải hằng số trong code.
- **G21.** Mọi text hiển thị đi qua key localization. Màu, kích thước và font lấy từ một file design-token duy nhất.

---

## 6. Tổng quan quy trình

```
Bước 0 Khởi động ─► Bước 1 GDD ─► Bước 2 Mock-up ─► Bước 3 Art ─► Bước 4 Animation ─► Bước 6 Ráp ─► Bước 7 AI test ─► Bước 8 Máy thật/Chơi thử/Phát hành
                        │                                                              ▲
                        └──────────────► Bước 5 Logic (bắt đầu sau cổng Bước 1) ────────┘
Các nhánh chạy xuyên các bước:  Level (1,5,7,8) · Kinh tế (1,5,8) · Âm thanh (1,4,6,7) · Text/Localization (1,2,6,7)
```

**Việc được làm song song.** Sau cổng Bước 1, Bước 5 (engine luật, test, tool level) có thể bắt đầu với hình grey-box. Bước 3 và 4 có thể chạy song song dựa trên mock-up đã duyệt. Tất cả gặp nhau ở Bước 6.

**Nơi để sản phẩm (gợi ý):**

```
docs/workflow/   STATE.md, DECISIONS.md, tài liệu này
docs/design/     GDD.md, features/*.md, bảng kinh tế, level-design.md, glossary.md, screen-inventory.md
docs/mockups/    <screen>-vNN.png, prompt, biên bản duyệt
art/source/      file gốc nhiều layer (PSD)      art/export/   file xuất + báo cáo xuất
art/anim/        project animation, script build, cấu hình export
audio/           nguồn, file xuất, danh sách âm thanh
<engine>/        code, nội dung (level, config, localization), scene
docs/qa/         kế hoạch test, test case, ảnh chụp, báo cáo judge, danh sách bug, checklist phát hành
```

---

## 7. Chi tiết từng bước

Mỗi bước ghi: **Mục đích · Đầu vào · AI đề xuất · AI phải hỏi (🔒 = bắt buộc chốt, không được giả định) · Đầu ra · Rule · Cổng (khi nào xong)**.

### Bước 0 — Khởi động: ý tưởng, phạm vi và thiết lập *(bổ sung)*

- **Mục đích:** thống nhất làm gì và làm bằng gì, trước khi thiết kế.
- **Đầu vào:** ý tưởng hoặc brief, game tham khảo, ràng buộc của team.
- **AI đề xuất:**
  - ý tưởng một trang (thể loại, vòng chơi chính, người chơi mục tiêu, độ dài phiên chơi);
  - phân tích game tham khảo (vòng chơi, tính năng, nhịp độ, cách kiếm tiền);
  - danh sách tính năng MVP và tính năng để sau;
  - các phương án nền tảng, engine, framework;
  - danh sách rủi ro và ước lượng công sức sơ bộ.
- **AI phải hỏi:**
  - 🔒 Nền tảng, hướng màn hình, độ phân giải chuẩn?
  - 🔒 Engine và framework, gồm cả phiên bản?
  - 🔒 Phạm vi MVP: bao nhiêu level, tính năng nào có, tính năng nào không?
  - 🔒 Kiếm tiền: không, quảng cáo, IAP hay cả hai? MVP làm thật hay giả lập?
  - Ngôn ngữ lúc ra mắt? Hướng art (2–3 phương án)?
  - Ai duyệt từng bước? Hạn chót và ngân sách (AI/sinh ảnh, tool, license)?
  - Mục tiêu hiệu năng (máy thấp nhất, FPS, dung lượng build)?
- **Đầu ra:** `docs/design/concept.md`, bản phân tích game tham khảo, phạm vi MVP, thẻ project đã điền trong `STATE.md`, các mục đầu tiên trong `DECISIONS.md`.
- **Cổng:** đã chốt ý tưởng, phạm vi, nền tảng, công nghệ và người duyệt.

### Bước 1 — Tài liệu thiết kế game (GDD)

- **Mục đích:** mô tả game đầy đủ và test được. Mọi thứ phía sau được làm và kiểm tra dựa trên nó.
- **Đầu vào:** các quyết định ở Bước 0.
- **AI đề xuất:** bản nháp GDD đầy đủ, gồm các phần:
  1. **Luật lõi:** bàn/không gian, quân/vật thể, hành động, thứ tự xử lý, cách xử lý khi hòa, điều kiện thắng/thua, điểm, sao, yếu tố ngẫu nhiên.
  2. **Thiết kế level (nhánh Level):** số level và chapter, các yếu tố tạo độ khó, mục tiêu đường cong, level nghỉ, cơ chế nào xuất hiện khi nào, level làm thế nào (làm tay hay generator + solver).
  3. **Tính năng:** tiến trình meta và mở khóa, booster, shop, thưởng và nhiệm vụ hằng ngày, hồ sơ, bảng xếp hạng, bộ sưu tập, cài đặt và tạm dừng.
  4. **Đăng nhập, tài khoản và lưu game:** kiểu nhận diện, lần chạy đầu, cài lại, đổi máy, offline, phiên bản save và migration.
  5. **FTUE:** từng bước, điều kiện chuyển bước, có cho skip không, kết hợp với popup mở khóa ra sao.
  6. **Bảng kinh tế (nhánh Kinh tế):** mọi loại tiền và vật phẩm, nguồn vào và nguồn tiêu, mọi con số có tên, mặc định và khoảng hợp lệ, thu nhập dự kiến mỗi level.
  7. **Luồng UI và danh sách màn hình:** mọi màn hình và popup, đi đến bằng cách nào, các trạng thái (bình thường/rỗng/khóa/đã sở hữu/lỗi).
  8. **Danh sách âm thanh (nhánh Âm thanh):** sự kiện SFX, nhạc, mức ưu tiên.
  9. **Text (nhánh Text):** giọng văn, cách đặt tên key, ngôn ngữ, độ dài tối đa.
  10. **Kiếm tiền và quảng cáo** (nếu có), **sự kiện analytics** và **trường hợp biên** (offline, đổi giờ máy, app bị tắt giữa lúc nhận thưởng hoặc mua hàng, lưu lỗi).
- **AI phải hỏi:**
  - 🔒 Mọi luật mà AI thấy còn mơ hồ (AI liệt kê kèm phương án).
  - 🔒 Kiểu đăng nhập/tài khoản, có cloud save hay không?
  - 🔒 Điều kiện thắng/thua và các giới hạn (số nước, thời gian, mạng)?
  - 🔒 Mục tiêu kinh tế: thu nhập mỗi level, giá cả, cái gì mua bằng tiền thật?
  - FTUE: mấy bước, có skip được không, chạm có tính hay chỉ kéo thả mới tính?
  - Số level, số chapter và hình dạng đường cong độ khó (kèm biểu đồ)?
  - Tính năng nào thuộc MVP, tính năng nào để sau?
- **Đầu ra:** `GDD.md`, mỗi tính năng một spec (`features/<name>.md`), `economy-sheet`, `level-design.md`, `glossary.md` (ID chuẩn như `flavor_0` kèm tên hiển thị), `screen-inventory.md`.
- **Rule:**
  - Mỗi luật phải đủ chính xác để viết test.
  - Mỗi spec tính năng có: mục đích, luồng người chơi, trạng thái, dữ liệu lưu, trường hợp biên, key text, **Visual Contract** (người nhìn phải thấy được gì) và **Acceptance** (kiểm tra đo được).
  - Gọi các vật thể trong game bằng ID chuẩn, không chỉ bằng tên hiển thị.
- **Cổng:** người duyệt ký duyệt; câu hỏi mở đã hết, hoặc được ghi rõ là để sau và có người phụ trách.

### Bước 2 — Mock-up (giao diện giả)

- **Mục đích:** có ảnh đã duyệt của mọi màn hình để art, code và test cùng nhắm vào một đích.
- **Đầu vào:** GDD, danh sách màn hình, hướng phong cách.
- **AI đề xuất:** 2–3 hướng phong cách cho các màn chính (home, gameplay, một popup); sau khi chọn được hướng, làm đủ mọi màn và trạng thái trong danh sách.
- **AI phải hỏi:**
  - 🔒 Chọn hướng phong cách nào? Duyệt từng màn.
  - Cần thể hiện những tỉ lệ màn hình nào (điện thoại 9:16, máy dài 9:20, tablet)?
  - Có chừa vùng banner/quảng cáo không? Safe area chặt đến mức nào?
  - Làm màn nào trước?
- **Đầu ra:** `docs/mockups/<screen>-vNN.png`, prompt, bản nháp **design token** (bảng màu, thang chữ, khoảng cách, bo góc), biên bản duyệt.
- **Rule:**
  - Mock-up làm đúng độ phân giải chuẩn, dùng độ dài text thật và con số lớn nhất có thể gặp.
  - Có đủ trạng thái khóa, rỗng và lỗi.
  - Chỉ một ngôn ngữ hình ảnh: bộ design token sẽ thành file token duy nhất trong code.
- **Cổng:** mọi màn trong danh sách đều có phiên bản đã duyệt. Chỉ phiên bản đã duyệt mới được dùng ở Bước 7.

### Bước 3 — Art: file thiết kế nhiều layer, chuẩn xuất, kiểm tra asset

- **Mục đích:** art sẵn sàng đưa vào game, đúng kích thước, lỗi được phát hiện trước khi ráp.
- **Đầu vào:** mock-up và token đã duyệt, yêu cầu của engine (pixel-per-unit, kích thước atlas, ngân sách texture).
- **AI đề xuất:** cấu trúc file và layer, quy ước đặt tên, bảng xuất (asset, kích thước, pivot, viền 9-slice, atlas), danh sách asset cần vẽ lại hoặc làm sạch.
- **AI phải hỏi:**
  - 🔒 Tool nguồn và ai giữ file gốc?
  - 🔒 Quy tắc xuất: kích thước/tỉ lệ, giới hạn atlas, ngân sách texture, nén?
  - Quy ước đặt tên layer: đồng ý đề xuất hay đổi?
  - Lỗi nào AI được tự sửa, lỗi nào phải trả về cho artist?
  - Lỗi nhỏ nào chấp nhận được cho MVP?
- **Đầu ra:** `art/source/*.psd` có layer và group đặt tên chuẩn; `art/export/*`; **báo cáo xuất** liệt kê từng asset với các kiểm tra và lỗi.
- **Rule:**
  - Mỗi group là một phần tử xuất. Chữ để dạng text sống trong file nguồn, nhưng không bao giờ nướng vào ảnh xuất.
  - Kiểm tra tự động mọi file xuất:
    - kích thước đúng spec;
    - lũy thừa 2 hoặc kích thước chẵn nếu atlas cần;
    - không có quầng alpha hay viền tối;
    - không bị cắt mép canvas;
    - phần tử đối xứng qua được kiểm tra soi gương;
    - không có đường nối hay vết nứt ở mảnh ghép hoặc 9-slice;
    - không có pixel bán trong suốt lạc chỗ;
    - dung lượng trong ngân sách.
  - Mọi ảnh AI sinh hoặc AI sửa phải lưu prompt và được người xem bằng mắt.
- **Cổng:** báo cáo xuất không còn lỗi chặn; người duyệt art đồng ý.

### Bước 4 — Animation và hiệu ứng (Spine hoặc tương đương)

- **Mục đích:** animation và hiệu ứng có nguồn chỉnh sửa được và được xuất đúng runtime của game.
- **Đầu vào:** art đã duyệt, danh sách animation trong GDD, quyết định chọn tool (§8).
- **AI đề xuất:**
  - danh sách animation: tên, thời lượng, lặp hay chạy một lần, điều kiện kích hoạt, pose cuối khi "giảm chuyển động";
  - đường đi của tool (§8);
  - hiệu ứng nào làm được bằng script (hiệu ứng đơn giản), hiệu ứng nào cần animator (nhân vật có rig).
- **AI phải hỏi:**
  - 🔒 Tool animation và phiên bản runtime? Đã có license chưa?
  - 🔒 Danh sách animation cuối cùng và mức ưu tiên.
  - Giới hạn hiệu năng (số bone, draw call, kích thước texture)?
  - Tham khảo phong cách về nhịp và cảm giác?
- **Đầu ra:** project animation (nguồn), script build nếu có, cấu hình export, README ghi rõ các bước xuất, file xuất cho runtime (dữ liệu + atlas).
- **Rule:**
  - Phiên bản editor và runtime phải khớp (cùng major.minor).
  - Tên animation là API: liệt kê trong spec, không âm thầm đổi tên.
  - Gameplay không chờ hay phụ thuộc vào event của animation.
  - Animation nào cũng có pose cuối cho cài đặt "tắt animation".
  - Không bao giờ sinh lại đè lên nguồn mà artist đã chỉnh tay.
- **Nhánh Âm thanh:** làm hoặc tìm SFX và nhạc ở bước này, kèm ghi chép license. Chuẩn hóa âm lượng.
- **Cổng:** file xuất load được trong runtime; mọi animation có tên chạy đúng trong ảnh chụp; người duyệt đồng ý.

### Bước 5 — Logic game

- **Mục đích:** luật, meta, kinh tế và lưu game được implement, chạy tất định và có test.
- **Đầu vào:** GDD, bảng kinh tế, thiết kế level, rule framework.
- **AI đề xuất:** kiến trúc (module, luồng dữ liệu, model save); kế hoạch test; cách sinh level (generator + solver + probe độ khó); danh sách chỗ mơ hồ của GDD phát hiện khi code.
- **AI phải hỏi:**
  - 🔒 Mọi câu hỏi về luật mà GDD chưa trả lời (không bao giờ đoán luật game).
  - 🔒 Định dạng save và chính sách migration, gồm cả save cũ sẽ ra sao khi nội dung thay đổi.
  - Nội dung level: AI có được sinh level theo đường cong không? Ai review?
  - Độ sâu test cần có: chỉ luật, hay cả kinh tế và các trường hợp lưu lỗi?
- **Đầu ra:** code, test headless/unit, file level, dữ liệu config, ghi chú kiến trúc ngắn.
- **Rule:**
  - Tuân thủ G16–G21 và rule của framework.
  - Viết test trước hoặc cùng lúc với code: luật, trường hợp biên trong GDD, lưu/khôi phục, chạy lại lời giải đã kiểm chứng của mọi level, giao dịch kinh tế có giả lập lưu lỗi.
  - Level chỉ được phát hành khi solver chứng minh thắng được và báo cáo độ khó đã được review.
- **Cổng:** test pass (có output); code đã được review; designer đã review báo cáo level.

### Bước 6 — Ráp art, animation, âm thanh và text vào game

- **Mục đích:** game thật, nhìn giống mock-up đã duyệt.
- **Đầu vào:** art, animation, âm thanh, logic đã duyệt, danh sách màn hình.
- **AI đề xuất:** thứ tự ráp (màn gameplay chính trước), mỗi màn làm theo mock-up phiên bản nào, danh sách khác biệt dự kiến kèm lý do.
- **AI phải hỏi:**
  - 🔒 Mọi khác biệt so với mock-up đã duyệt (bố cục không vừa, thiếu asset).
  - Thứ tự ưu tiên các màn?
  - Placeholder: được dùng tạm hay hoàn toàn không?
- **Đầu ra:** các màn đã làm, nội dung gắn dữ liệu thật, file localization, danh sách màn hình cập nhật trạng thái từng màn.
- **Rule:**
  - View chỉ hiển thị (G17). Màu và kích thước từ token, text từ key (G21).
  - Scene và prefab chỉnh qua tool của editor (G11).
  - Màn và dialog mới tạo bằng scaffold của framework, nếu framework có.
  - Không để lại placeholder nào trừ khi người duyệt cho phép.
- **Cổng:** mọi màn trong danh sách đều có, dùng dữ liệu thật và không còn placeholder; test editor/tích hợp pass.

### Bước 7 — AI kiểm thử, so sánh với mock-up đã duyệt

- **Mục đích:** chứng minh hành vi đúng GDD và hình ảnh đúng mock-up đã duyệt.
- **Đầu vào:** bản build hoặc phiên chạy trong editor, test case, mock-up đã duyệt, Visual Contract.
- **AI đề xuất:** kế hoạch test (test case theo từng tính năng, lấy từ spec), danh sách tỉ lệ màn hình, các mức độ nghiêm trọng của bug.
- **AI phải hỏi:**
  - 🔒 Tiêu chí pass: mức độ nào là chặn?
  - 🔒 Khác biệt nào so với mock-up được chấp nhận?
  - Kích thước và tỉ lệ màn hình nào phải pass?
- **Cách làm: từ sàn lên trần.**
  1. **Sàn chức năng, tất định:** test tự động, và input có kịch bản đi qua hệ thống input thật, kèm kiểm tra trạng thái (điểm, ví, status). Kiểm tra pixel xác nhận mỗi bề mặt thực sự được render.
  2. **Trần hình ảnh:** chụp mọi màn và trạng thái ở mọi tỉ lệ yêu cầu. Một model thị giác chấm từng ảnh theo Visual Contract, **có đính kèm mock-up đã duyệt làm chuẩn**. Những gì judge "không xác định được" thì chuyển thành kiểm tra số liệu.
  3. **Kiểm tra hồi quy:** chạy lại sau mỗi lần sửa, so với ảnh chụp lần trước.
- **Đầu ra:** báo cáo test, ảnh chụp, kết quả so sánh, danh sách bug (các bước, mong đợi, thực tế, ảnh chụp, mức độ).
- **Rule:**
  - Test với bản backup save (G15).
  - Liệt kê các khác biệt cố ý để không bị báo lại.
  - Ảnh chụp chưa ai xem không phải là bằng chứng.
- **Cổng:** không còn bug chặn; các khác biệt còn lại được người duyệt chấp nhận bằng văn bản.

### Bước 8 — Test máy thật, người chơi thử và phát hành *(bổ sung)*

- **Mục đích:** xác nhận game chạy tốt trên máy thật, vui và công bằng với người chơi thật, rồi phát hành.
- **AI đề xuất:**
  - danh sách máy (máy yếu, máy trung bình, tablet, máy tai thỏ);
  - các kiểm tra hiệu năng (FPS, bộ nhớ, thời gian load, dung lượng build);
  - kế hoạch chơi thử (người chơi, nhiệm vụ, câu hỏi, chỉ số như tỉ lệ thắng từng level và điểm bỏ cuộc);
  - checklist phát hành (thông tin store, quyền riêng tư, cấu hình SDK, đánh phiên bản).
- **AI phải hỏi:**
  - 🔒 Test trên những máy nào, bao nhiêu người chơi thử?
  - 🔒 SDK nào được bật thật (quảng cáo, IAP, analytics), ở store nào, dùng tài khoản nào?
  - 🔒 Duyệt phát hành (phát hành hay chưa).
  - Được phép chỉnh độ khó thế nào dựa trên dữ liệu chơi thử?
- **Đầu ra:** báo cáo máy thật, báo cáo chơi thử kèm đề xuất chỉnh, checklist phát hành, release notes.
- **Rule:**
  - Thay đổi cân bằng từ chơi thử phải quay lại Bước 1 (tài liệu kinh tế/level) và Bước 5 (dữ liệu), thông qua yêu cầu thay đổi.
  - Không thanh toán thật, không bật quảng cáo thật, không nộp store khi chưa được đồng ý rõ ràng.
- **Cổng:** đạt mục tiêu hiệu năng trên máy mục tiêu; các vấn đề từ chơi thử đã được phân loại; người duyệt cho phát hành.

---

## 8. Chọn tool animation

AI trình bày các phương án này ở Bước 4 và người làm chốt.

| Phương án | Hợp với | Lưu ý |
|---|---|---|
| **Spine** (editor + runtime chính thức) | Nhân vật 2D và hiệu ứng; mesh, IK, skin; runtime cho engine rất tốt | License trả phí; license runtime Spine yêu cầu có license Spine editor hợp lệ thì sản phẩm mới được dùng. Phiên bản editor và runtime phải khớp major.minor. |
| **Dữ liệu Spine sinh bằng script** (AI viết JSON skeleton + atlas, sau đó import vào Spine editor) | Hiệu ứng đơn giản: glow, tia sáng, confetti, coin, scale/xoay/màu | Tốt cho hiệu ứng, yếu với nhân vật có rig. Vẫn cần license Spine để ship runtime. Giữ project editor làm nguồn để artist còn chỉnh được. |
| **DragonBones → Spine** | Team có animator quen DragonBones | DragonBones xuất ra định dạng Spine 3.x cũ. Spine editor bản mới có thể từ chối import thẳng. Đường đã chạy được: import bằng Spine 3.8 → mở/xuất bằng phiên bản Spine mục tiêu → pack atlas. Sau khi chuyển phải kiểm tra lại mesh, IK, easing, event và frame rate. Việc xuất có thể cần dịch vụ online. Làm thử một asset thật trước. |
| **Dùng thẳng runtime DragonBones** | Project cũ đang dùng sẵn | Runtime đã cũ, ít được bảo trì; phải kiểm tra có hỗ trợ phiên bản engine không; tránh dùng hai runtime animation trong một game. |
| **Animation 2D có sẵn của engine** (ví dụ Unity 2D Animation + PSD Importer) | Rig thẳng từ PSD nhiều layer, không cần license thêm | Quy trình và tool khác. Chọn theo từng project, không chọn theo từng asset. |
| **Tween UI bằng code** | Nút, popup, bộ đếm, highlight đơn giản | Để thời lượng trong token/config; tôn trọng cài đặt "giảm chuyển động". |

**Câu hỏi để người làm chốt:**

- Team đã có (hoặc sẽ mua) license Spine chưa?
- Animator sẽ dùng tool nào?
- Có nhân vật cần rig không, hay chỉ có hiệu ứng?
- Project engine đang chốt phiên bản runtime nào?

---

## 9. Kinh nghiệm tích lũy

Các kinh nghiệm dưới đây có được khi làm trọn một game puzzle casual bằng AI, và được viết lại thành rule.

### Thiết kế

1. **Ghi rõ cách xử lý khi hòa và thứ tự xử lý.** Khi không biết chính xác luật của game tham khảo, hãy chọn một luật, ghi lại và test nó. Không bao giờ tuyên bố giống hệt game gốc.
2. **Có ID chuẩn ngay từ đầu.** Tên trong code, tên hiển thị và tên trong art đã bị lệch nhau (một vị tên "Strawberry" trong code lại hiển thị là "Watermelon"). Spec và test dùng ID.
3. **Nội dung tăng làm text bị cũ.** Game tăng từ 10 lên 60 level nhưng vẫn còn text ghi "ten puzzles". Sau mỗi lần đổi nội dung, tìm text và tài liệu cũ.
4. **Kiểm tra nội dung sinh tự động với chính dữ liệu của nó.** Có mô tả level nhắc đến cơ chế mà level đó không có.
5. **Chốt input nào được tính.** Tutorial chỉ chuyển bước khi kéo thả, chạm thì không. Ghi lựa chọn này vào spec.
6. **Bot chỉ để chẩn đoán.** Một bot đơn giản cho thấy các level cuối rất khó, nhưng chỉ người chơi thật mới cho tỉ lệ thắng.
7. **Đánh phiên bản cho nội dung.** Ván đã lưu từ revision level cũ phải được bắt đầu lại an toàn, không được load vào bàn chơi đã thay đổi.

### Code và kiến trúc

8. **Engine luật thuần có test headless** giúp đổi luật rất nhanh: hơn một triệu assertion chạy trong vài giây, gồm cả chạy lại lời giải của mọi level.
9. **Commit nguyên tử có rollback** (và bù lại ví) giúp không mất hay nhân đôi phần thưởng khi lưu lỗi.
10. **Lưu thưởng trước, hiển thị sau.** Vòng quay hằng ngày chọn kết quả và lưu trước khi quay. Thưởng chưa xác nhận sẽ hiện lại, không bao giờ bị trao lại.
11. **Chống mua trùng** bằng transaction ID. Để gateway mua hàng sau một interface, để store giả lập thay được bằng IAP thật.
12. **Một nơi quản lý popup.** Một coordinator duy nhất xếp hàng dialog. Dialog đã hủy hoặc đã cũ không bao giờ chạy action.
13. **Khi framework cấm một thứ mình cần,** viết ra các phương án và xin quyết định. Không giấu cách lách khỏi validator.
14. **Đóng gói asset có thể nhân đôi asset dùng chung.** Để renderer và asset toàn cục ở scope gốc/boot, và có test kiểm tra trùng.
15. **Ghi lại mọi ngoại lệ về toolchain** (ví dụ phiên bản engine cũ hơn chuẩn của framework) trong một ghi chú quyết định, và thêm một kiểm tra sẽ fail khi gặp phiên bản không mong đợi.

### Hiệu năng

16. **Cài đặt quality của editor không phải của máy thật.** Kiểm tra mức mặc định của nền tảng.
17. **Đo các tính năng tốn kém trước:** MSAA, bóng đổ real-time, HDR/post, và texture offscreen full độ phân giải. Cách xử lý là một mức quality thấp cho mobile và giới hạn kích thước render texture.
18. **Shader tự viết có thể chỉ lỗi trên một nền tảng.** Ưu tiên shader đơn giản, dễ port, và kiểm tra trên máy thật.

### Quy trình và tool

19. **Tool timeout không hủy công việc.** Build và test vẫn chạy tiếp sau khi lệnh báo timeout. Kiểm tra trạng thái trước khi thử lại, và chặn chạy trùng các thao tác chỉ được chạy một lần như build.
20. **Xem mọi ảnh chụp.** Ảnh đã lưu mà chưa mở ra xem thì không chứng minh gì.
21. **Mở mọi file bằng chứng trước khi trích dẫn.** Có hai báo cáo bị serialize thành object rỗng `{}`.
22. **Backup đúng file save thật.** Việc test đã làm thay đổi hồ sơ người chơi thật. Tìm đúng vị trí file save, rồi backup và khôi phục nó.
23. **Đọc kết quả test thật.** Lần chạy test bị "timeout" hoặc trả về kết quả rỗng là chưa pass.
24. **Giữ checkpoint.** Phạm vi đã tăng nhiều lần trong một project. Một `STATE.md` có đủ danh sách việc và đánh dấu đã/ chưa kiểm chứng cho từng mục giúp làm tiếp được qua nhiều phiên.
25. **Tách bằng chứng theo loại.** Pass trong editor, build thành công và kiểm tra trên máy thật được báo riêng. Nhờ vậy không tuyên bố nhiều hơn những gì đã chứng minh.

---

## 10. Mẫu tài liệu

### 10.1 `docs/workflow/STATE.md`

```markdown
# Trạng thái project
Game: … | Nền tảng: … | Engine/framework: … | Độ phân giải chuẩn: …
Người duyệt: Thiết kế … · Hình ảnh … · Art … · Kỹ thuật … · Phát hành …

| Bước | Trạng thái (chưa bắt đầu / đang làm / chờ chốt / đã duyệt) | Đầu ra đã duyệt (phiên bản) | Người duyệt, ngày |
|---|---|---|---|
| 0 Khởi động | | | |
| 1 GDD | | | |
| 2 Mock-up | | | |
| 3 Art | | | |
| 4 Animation | | | |
| 5 Logic | | | |
| 6 Ráp | | | |
| 7 AI test | | | |
| 8 Máy thật/Chơi thử/Phát hành | | | |

## Câu hỏi đang chờ người làm
- [ ] C… (bước, hỏi ngày …)

## Việc tiếp theo (theo thứ tự)
1. …

## Phiên gần nhất
Ngày · đã đổi gì · bằng chứng · cái gì fail · cái gì chưa kiểm tra
```

### 10.2 Một mục trong `docs/workflow/DECISIONS.md`

```markdown
### D-012 · 01-10-2026 · Bước 1 · Kiểu đăng nhập
Câu hỏi: Người chơi được nhận diện thế nào?
Phương án: a) khách/lưu trên máy · b) khách + đăng nhập nền tảng · c) tài khoản email
Quyết định: a) — người chốt: <tên>
Lý do: MVP chưa có backend; xem lại ở Bước 8.
Ảnh hưởng: GDD §4, model save, màn cài đặt.
```

### 10.3 Tin nhắn mở đầu một bước (AI → người làm)

```markdown
**Bước 2 — Mock-up: bắt đầu.**
Đầu vào: GDD v1.2 (đã duyệt), danh sách màn hình (24 màn).
Kế hoạch: 3 hướng phong cách cho Home / Gameplay / Win → bạn chọn → làm đủ bộ.
Câu hỏi:
1. 🔒 Cần những tỉ lệ màn hình nào: a) chỉ 9:16 · b) 9:16 + 9:20 (khuyến nghị) · c) thêm tablet
2. Chừa vùng banner 160 px ở dưới không? a) có (khuyến nghị nếu có kế hoạch quảng cáo) · b) không
Nếu câu 2 chưa có trả lời, tôi sẽ chừa vùng banner.
```

### 10.4 Yêu cầu thay đổi

```markdown
### CR-004 · phát sinh ở Bước 6 · ảnh hưởng Mock-up "gameplay-v03" (đã duyệt)
Vấn đề: thanh booster đè lên khay trên màn hình 9:20.
Phương án: a) dời booster sang mép phải (khuyến nghị) · b) thu nhỏ khay 10% · c) giữ nguyên, chấp nhận chồng
Cần người chốt: người duyệt hình ảnh
Phần phía sau cần kiểm tra lại: ảnh chụp gameplay, vị trí highlight của tutorial.
```

---

## 11. Lịch sử thay đổi

| Ngày | Phiên bản | Thay đổi |
|---|---|---|
| 28-09-2026 | 1.0 | Bản đầu tiên, rút ra từ việc review project CakeSort |
| 28-09-2026 | 2.0 | Bỏ phụ thuộc project/máy. Thêm Bước 0 và Bước 8, các nhánh (level, kinh tế, âm thanh, text), vai trò, cách làm việc đề xuất-hỏi-chốt, câu hỏi cần chốt cho từng bước, yêu cầu thay đổi, mẫu tài liệu và hướng dẫn chọn tool animation. Kinh nghiệm được viết lại dạng tổng quát. |
