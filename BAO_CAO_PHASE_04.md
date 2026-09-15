# Báo cáo Phase 04 — Vào phòng bằng mã

Ngày: 15/09/2026.

**Trạng thái: Hoàn thành nghiệm thu toàn diện** (Unit test 29/29 pass, Integration test 9/9 pass, GameSmoke pass, Chrome DevTools Protocol multi-client automation pass 100%). Sẵn sàng chuyển sang Phase 05.

## Chức năng đã triển khai

- **Mô hình phiên người chơi và bảo mật token (`Server/PlayerSession.cs`):**
  - Quản lý định danh người chơi `PlayerId`, tên hiển thị `DisplayName` và tên chuẩn hóa `NormalizedName` (viết hoa, cắt khoảng trắng).
  - Bảo mật tuyệt đối reconnect token: Token ngẫu nhiên chỉ trả về cho client tương ứng; máy chủ chỉ băm lưu trữ SHA256 (`ReconnectTokenHash`), không lưu token thô.
  - Snapshot công khai `PlayerSnapshot` không chứa bất kỳ secret hay token nhạy cảm nào.
- **Xử lý gia nhập phòng tại máy chủ (`Server/RoomInstance.cs`, `RoomManager.cs`):**
  - Kiểm tra điều kiện gia nhập: Phòng phải đang ở trạng thái `Lobby` (trừ khi bật tham gia muộn), chưa vượt quá `MaxPlayersPerRoom`.
  - Cơ chế đồng bộ chống trùng tên (Race Condition): Khóa nguyên tử kiểm tra `NormalizedName`. Hai yêu cầu cùng tên gửi đến đồng thời chỉ có đúng một yêu cầu thành công, yêu cầu còn lại bị từ chối với mã lỗi `DUPLICATE_DISPLAY_NAME`.
- **Giao thức SignalR Hub (`Server/RoomHub.cs`):**
  - Triển khai API `JoinRoom`: Đưa connection người chơi vào group SignalR `room_{RoomId}`.
  - Tự động broadcast sự kiện `PlayerJoined` qua `Clients.OthersInGroup` để cập nhật tức thì danh sách cho Admin và toàn bộ người chơi khác trong phòng.
  - Đảm bảo tính cách ly: Client ở phòng khác không nhận được bất kỳ event nào của phòng này.
- **Giao diện Client (`Pages/JoinRoom.razor`, `Pages/Lobby.razor`, `Pages/Online.razor`):**
  - Bổ sung tab chuyển đổi "TẠO PHÒNG MỚI" và "VÀO PHÒNG BẰNG MÃ" trên màn `/online`.
  - Form tham gia phòng: Nhập mã phòng và tên hiển thị (2–25 ký tự) với cơ chế tự động viết hoa mã phòng và hiển thị lỗi thân thiện.
  - Giao diện phòng chờ `Lobby.razor`: Hiển thị mã phòng, tên phòng, trạng thái phòng, vai trò Admin/Người chơi và danh sách người chơi đang chờ phân đội cập nhật real-time.

## File thay đổi chính

| Phần | File |
|---|---|
| Giao thức DTO | `Shared/RoomModels.cs` (`PlayerSnapshot`, `JoinRoomRequest`, `JoinRoomResponse`) |
| Máy chủ | `Server/PlayerSession.cs`, `Server/RoomInstance.cs`, `Server/RoomManager.cs`, `Server/RoomHub.cs` |
| Kết nối client | `Services/MultiplayerConnection.cs` (`JoinRoomAsync`, event `PlayerJoined`, `PlayerLeft`) |
| Giao diện | `Pages/JoinRoom.razor`, `Pages/Lobby.razor`, `Pages/Online.razor`, `Pages/Online.razor.css` |
| Unit Test | `Tests/Multiplayer.UnitTests/JoinRoomTests.cs` |
| Integration Test | `Tests/Multiplayer.IntegrationTests/JoinRoomIntegrationTests.cs` |
| Tiến độ | `TIEN_DO_MULTIPLAYER.md`, báo cáo này |

## Kết quả kiểm thử tự động

| Lệnh/kiểm tra | Kết quả |
|---|---|
| `dotnet build Server/TruyTimDanChu.Server.csproj -c Release` | PASS, 0 warning, 0 error |
| `dotnet build TruyTimDanChu.csproj -c Release` | PASS, 0 warning, 0 error |
| `dotnet run --project Tests/GameSmoke.csproj -c Release` | PASS: mở đầu, 4 nhiệm vụ, lựa chọn sai, phản hồi cuối và lưu/tiếp tục |
| `dotnet test Tests/Multiplayer.UnitTests/Multiplayer.UnitTests.csproj -c Release` | 29 pass (thêm 9 test cho `JoinRoomTests` bao gồm đa luồng barrier tranh tên), 0 fail |
| `dotnet test Tests/Multiplayer.IntegrationTests/Multiplayer.IntegrationTests.csproj -c Release` | 9 pass (thêm test đa client SignalR thật và cách ly phòng), 0 fail |

## Kết quả kiểm chứng giao diện Chrome (CDP Multi-Client Automation)

Đã chạy kịch bản tự động hóa trên Chrome headless mô phỏng 3 phiên làm việc đồng thời (`scratch/verify_phase04.mjs`):

1. **Admin H tạo phòng:** Khởi tạo phòng `Hành Trình Dân Chủ Phase 4` -> nhận mã `DC-G59W` -> vào Lobby.
2. **Player A vào phòng:** Nhập mã `DC-G59W` và tên "Thành Nam" -> tham gia thành công.
3. **Admin H cập nhật real-time:** Màn hình Admin H ngay lập tức xuất hiện "Thành Nam" trong danh sách chờ phân đội mà không cần reload (ảnh `scratch/evidence/p04_01_admin_sees_player_a.png`).
4. **Player B thử trùng tên:** Nhập cùng mã phòng và tên "thành nam" (viết thường) -> nhận ngay thông báo lỗi `Tên người chơi này đã có người sử dụng trong phòng.`
5. **Player B đổi tên:** Đổi thành "Hải Yến" -> tham gia thành công.
6. **Thống nhất danh sách trên 3 màn hình:** Cả Admin H, Player A và Player B đều hiển thị đầy đủ 2 thành viên "Thành Nam" và "Hải Yến", có tag nhận diện `(Bạn)` chính xác cho từng người (ảnh `scratch/evidence/p04_02_admin_unified_roster.png`, `p04_03_player_a_roster.png`, `p04_04_player_b_roster.png`).
7. **Console:** 0 lỗi console, 0 unhandled exception.

**Kết luận Phase 04:** Đạt 100% tiêu chí nghiệm thu. Chức năng vào phòng bằng mã và đồng bộ danh sách phòng chờ thời gian thực đã hoàn thiện, sẵn sàng cho Phase 05.

