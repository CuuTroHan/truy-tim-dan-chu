# HƯỚNG DẪN TRẢI NGHIỆM BẢN DEMO MULTIPLAYER (PHASE 11–16)

Tài liệu này hướng dẫn chi tiết cách khởi động và trực tiếp chơi bản demo multiplayer của trò chơi **"Truy tìm Dân chủ"** sau khi hoàn thành Phase 16 (lát cắt hoàn chỉnh: bắt đầu trận, countdown, bản đồ riêng theo đội, di chuyển đồng bộ mượt mà, gặp NPC và thu thập vật phẩm dùng chung).

---

## 1. Khởi động hệ thống máy chủ và máy khách

Nếu các tiến trình chưa chạy, mở 2 cửa sổ Terminal độc lập:

### Cửa sổ 1: Khởi động Game Server (SignalR Authoritative)
```bash
cd "/Users/dungng/FPT/Ki 9/MLN131/truy-tim-dan-chu"
dotnet run --project Server/TruyTimDanChu.Server.csproj --urls http://localhost:5080
```

### Cửa sổ 2: Khởi động Game Client (Blazor WebAssembly / Server)
```bash
cd "/Users/dungng/FPT/Ki 9/MLN131/truy-tim-dan-chu"
dotnet run --project TruyTimDanChu.csproj --urls http://localhost:5267
```

Truy cập địa chỉ trong trình duyệt: **`http://localhost:5267/online`**

---

## 2. Kịch bản trải nghiệm nhiều người chơi (2–3 người)

Mở 3 tab hoặc cửa sổ trình duyệt (khuyến nghị dùng các cửa sổ ẩn danh để có session người chơi riêng biệt):

### Tab 1: Quản trị viên (Admin H)
1. Vào tab **TẠO PHÒNG**.
2. Nhập tên phòng (ví dụ: `Phòng Thi Đấu 1`) và thời gian trận đấu.
3. Bấm **TẠO PHÒNG**. Ghi nhận **Mã phòng** hiển thị trên góc (ví dụ: `DC-ABCD`).
4. Ở phần quản lý đội, thêm 2 đội:
   - **Đội Đỏ** (Sức chứa: 2..4, Màu đỏ `#E53935`).
   - **Đội Xanh** (Sức chứa: 2..4, Màu xanh `#1E88E5`).

### Tab 2: Người chơi A (Player A - Đội Đỏ)
1. Vào tab **VÀO PHÒNG**.
2. Nhập **Mã phòng** và Tên `Player A` -> bấm **VÀO PHÒNG**.
3. Bấm **VÀO ĐỘI** tại thẻ **Đội Đỏ**.
4. Chọn Avatar (ví dụ: **Bảo**).
5. Bấm **SẴN SÀNG**.

### Tab 3: Người chơi B (Player B - Đội Đỏ, Đồng đội của A)
1. Vào tab **VÀO PHÒNG**, nhập mã phòng và Tên `Player B`.
2. Bấm **VÀO ĐỘI** tại thẻ **Đội Đỏ**.
3. Chọn Avatar (ví dụ: **Dũng**).
4. Bấm **SẴN SÀNG**.

*(Tùy chọn) Tab 4: Người chơi C (Player C - Đội Xanh)*:
- Vào Đội Xanh, chọn Avatar và bấm Sẵn sàng để quan sát tính năng cách ly đội.

---

## 3. Bắt đầu trận đấu & Trải nghiệm tính năng đã hoàn thiện

### Bước 1: Countdown đồng bộ (Phase 11)
- Trên **Tab 1 (Admin)**, bấm **BẮT ĐẦU TRẬN ĐẤU**.
- Màn hình tất cả người chơi hiển thị bộ đếm ngược đồng bộ (3, 2, 1) và tự động chuyển vào bản đồ thị trấn.

### Bước 2: Bản đồ riêng theo đội & Đồng đội (Phase 12)
- Người chơi A và B xuất hiện tại quảng trường trung tâm.
- Trên bản đồ của A thấy nhân vật của B với chip tên và màu huy hiệu của Đội Đỏ.
- Người chơi C (Đội Xanh) xuất hiện ở bản đồ riêng, không thấy A và B.

### Bước 3: Di chuyển đồng bộ & Chuyển động mượt (Phase 13 & 14)
- **Điều khiển:** Sử dụng cụm phím `W`, `A`, `S`, `D` (hoặc phím mũi tên).
- **Trải nghiệm:**
  - Nhân vật của bạn phản hồi ngay lập tức (Client Prediction) với tốc độ chuẩn 73 px/s, không có độ trễ phím.
  - Khi quan sát đồng đội di chuyển, nhân vật đồng đội lướt đi êm ái ở 60 FPS (Teammate Interpolation), không hề bị giật cục hay teleport.
  - Thử đâm vào tường thư viện (phía Bắc): máy chủ authoritative chặn di chuyển, nhân vật dừng lại chính xác không thể xuyên tường.

### Bước 4: Tương tác NPC dùng chung & Đọc hội thoại riêng (Phase 15)
- Di chuyển Player A tiếp cận chú **Trọng** tại tọa độ `(526, 300)` (gần Hòm Dân chủ) và bấm **`E`**.
- Player A mở hội thoại mở đầu cá nhân và bấm **TIẾP TỤC**.
- Cả Đội Đỏ tự động chuyển sang chương **Bốn ngọn đèn** (`Chapter.Lights`), thẻ nhiệm vụ hiển thị "Trò chuyện với Phương, Dũng, Bảo và Nam".
- Player B nhận được cập nhật nhiệm vụ mới nhưng **không bị khóa giao diện**, vẫn tự do đi lại.
- Player A đi sang phía Tây gặp **Phương** (`(170, 280)`) bấm `E` -> Đội Đỏ nhận cờ `Spoken[0]`.
- Player B đi gặp **Dũng** (`(205, 338)`) bấm `E` -> Đội Đỏ nhận cờ `Spoken[1]`.

### Bước 5: Thu thập ngọn đèn cho cả đội (Phase 16)
- Sau khi đã trò chuyện với Phương, Player A di chuyển sang bệ đèn của Phương (`lamp0` tại `(125, 260)`):
  - Bấm phím **`E`**.
  - Bệ đèn được thắp sáng thành công!
  - Cả Player A và Player B lập tức thấy ngọn đèn đã được đặt trên bản đồ chung của đội.
- Thử nghiệm kiểm tra quy tắc:
  - Nếu đến bệ đèn của Nam khi chưa nói chuyện với Nam, hệ thống hiển thị nhắc nhở và chưa cho phép đặt đèn.
  - Người chơi B thử tương tác lại với ngọn đèn đã đặt: hệ thống báo "Ngọn đèn đã ở đúng bệ", không gây lỗi và không tính trùng.

---

## 4. Chạy kiểm thử tự động (Automated Verification)

Bất kỳ lúc nào, bạn có thể kiểm chứng lại toàn bộ các kiểm thử tự động bằng các lệnh sau:

```bash
# 1. Chạy toàn bộ 151 Unit Tests (Phase 01 - 16)
dotnet test Tests/Multiplayer.UnitTests/Multiplayer.UnitTests.csproj

# 2. Chạy toàn bộ 19 Integration Tests SignalR Hub
dotnet test Tests/Multiplayer.IntegrationTests/Multiplayer.IntegrationTests.csproj

# 3. Kiểm tra hồi quy chế độ Single-Player
dotnet run --project Tests/GameSmoke.csproj

# 4. Chạy kịch bản đa phiên Chrome CDP tự động
node scratch/verify_phase16_demo.mjs
```

