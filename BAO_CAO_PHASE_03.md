# Báo cáo Phase 03 — Admin tạo phòng

Ngày: 15/09/2026.

**Trạng thái: Hoàn thành nghiệm thu toàn diện** (Unit test 20/20 pass, Integration test 8/8 pass, GameSmoke pass, Chrome DevTools Protocol automation pass 100%). Sẵn sàng chuyển sang Phase 04.

## Chức năng đã triển khai

- **Mô hình dữ liệu phòng (`Shared/RoomModels.cs`):**
  - Enum `RoomStatus` (`Lobby`, `Countdown`, `Playing`, `Paused`, `Finished`, `Closed`).
  - DTO `CreateRoomRequest`, `CreateRoomResponse`, `RoomSnapshot` và mã lỗi `RoomErrorCodes`.
  - Bảo mật tuyệt đối: `AdminToken` chỉ trả về riêng cho người tạo phòng trong response, tuyệt đối không xuất hiện trong `RoomSnapshot` công khai.
- **Dịch vụ quản lý phòng Authoritative tại máy chủ (`Server`):**
  - `RoomServerOptions`: Đọc cấu hình giới hạn số phòng, số đội, sức chứa và thời hạn từ cấu hình máy chủ.
  - `RoomInstance`: Đối tượng phòng lưu trữ bộ nhớ an toàn đa luồng, theo dõi phiên bản state (`Version`) và xác thực quyền admin qua `VerifyAdmin`.
  - `RoomManager`: Quản lý danh bạ phòng theo ID và theo mã phòng ngắn `DC-XXXX`. Cơ chế sinh mã phòng ngẫu nhiên có vòng lặp retry tránh xung đột trùng mã.
  - Đăng ký `RoomManager` Singleton và cấu hình trong `ServerBootstrap.cs`.
- **Giao thức SignalR Hub (`RoomHub.cs`):**
  - Cung cấp API `CreateRoom` nhận cấu hình từ client, tạo phòng và tự động đưa connection của admin vào SignalR Group `room_{RoomId}` và `admin_{RoomId}`.
- **Dịch vụ phía client (`MultiplayerConnection.cs`):**
  - Bổ sung `CreateRoomAsync` gọi Hub với timeout và xử lý ngoại lệ an toàn.
- **Giao diện người dùng (`Pages/CreateRoom.razor`, `Pages/Online.razor`):**
  - Khi đã kết nối máy chủ, hiển thị form cấu hình phòng: Tên phòng (3–50 ký tự), Thời gian thi đấu (1–120 phút), Tự chọn đội, Hiển thị tiến độ trực tiếp.
  - Validation dữ liệu đầu vào trực quan trước khi gửi máy chủ.
  - Màn hình kết quả sau khi tạo: Hiển thị mã phòng to rõ `DC-XXXX` để chia sẻ cho người chơi, tóm tắt thông số phòng và vai trò Admin.

## File thay đổi chính

| Phần | File |
|---|---|
| Giao thức & DTO | `Shared/RoomModels.cs` |
| Máy chủ | `Server/RoomOptions.cs`, `Server/RoomInstance.cs`, `Server/RoomManager.cs`, `Server/RoomHub.cs`, `Server/ServerBootstrap.cs` |
| Kết nối client | `Services/MultiplayerConnection.cs` |
| Giao diện | `Pages/CreateRoom.razor`, `Pages/Online.razor`, `Pages/Online.razor.css` |
| Unit Test | `Tests/Multiplayer.UnitTests/CreateRoomTests.cs`, `Multiplayer.UnitTests.csproj` |
| Integration Test | `Tests/Multiplayer.IntegrationTests/CreateRoomIntegrationTests.cs` |
| Tiến độ | `TIEN_DO_MULTIPLAYER.md`, báo cáo này |

## Kết quả kiểm thử tự động

| Lệnh/kiểm tra | Kết quả |
|---|---|
| `dotnet build Server/TruyTimDanChu.Server.csproj -c Release` | PASS, 0 warning, 0 error |
| `dotnet build TruyTimDanChu.csproj -c Release` | PASS, 0 warning, 0 error |
| `dotnet run --project Tests/GameSmoke.csproj -c Release` | PASS: mở đầu, 4 nhiệm vụ, lựa chọn sai, phản hồi cuối và lưu/tiếp tục |
| `dotnet test Tests/Multiplayer.UnitTests/Multiplayer.UnitTests.csproj -c Release` | 20 pass (thêm 5 test cho `CreateRoomTests`), 0 fail |
| `dotnet test Tests/Multiplayer.IntegrationTests/Multiplayer.IntegrationTests.csproj -c Release` | 8 pass (thêm integration test tạo phòng SignalR thật), 0 fail |

## Kết quả kiểm chứng giao diện Chrome (CDP Automation)

Đã chạy kịch bản kiểm thử tự động trên Chrome headless (`scratch/verify_phase03.mjs`):

1. **Kết nối và hiển thị form:** Mở `/online` -> đạt `Connected` -> form tạo phòng hiển thị đầy đủ các trường (ảnh `scratch/evidence/p03_01_create_room_form.png`).
2. **Kiểm tra Validation:** Để trống tên phòng và bấm tạo -> hiển thị thông báo lỗi `Tên phòng phải từ 3 đến 50 ký tự.`
3. **Tạo phòng thành công:** Nhập tên "Đại hội Dân chủ 2026", 20 phút -> bấm xác nhận tạo phòng -> nhận mã phòng `DC-N5CD` từ máy chủ (ảnh `scratch/evidence/p03_02_room_created_success.png`).
4. **Bảo mật và trạng thái:** Phòng tạo ở trạng thái `Lobby`, vai trò người tạo là `Admin`, thông tin cấu hình khớp với lựa chọn.
5. **Console sạch:** 0 lỗi nghiêm trọng, 0 unhandled exception.

**Kết luận Phase 03:** Đạt 100% tiêu chí nghiệm thu. Đã hoàn thành chức năng Admin tạo phòng từ server tới UI, sẵn sàng tiếp tục Phase 04.

