# Báo cáo Phase 08 — Admin phân đội và loại người chơi

Ngày: 15/09/2026.

**Trạng thái: Hoàn thành nghiệm thu toàn diện** (Unit test 74/74 pass, Integration test 13/13 pass, GameSmoke pass, Chrome DevTools Protocol multi-client automation pass 100%). Sẵn sàng chuyển sang Phase 09.

## Chức năng đã triển khai

- **Mô hình DTO và thông điệp giao tiếp (`Shared/RoomModels.cs`):**
  - Cập nhật các DTO cho quyền điều hành roster của Admin:
    - Phân/chuyển đội: `AdminMovePlayerRequest(RoomId, AdminToken, PlayerId, TargetTeamId)`, `AdminMovePlayerResponse(Success, ErrorCode, PlayerId, OldTeamId, NewTeamId, RoomVersion)`.
    - Loại người chơi (Kick): `AdminKickPlayerRequest(RoomId, AdminToken, PlayerId, Reason)`, `AdminKickPlayerResponse(Success, ErrorCode, KickedPlayerId, RoomVersion)`.
    - Bật/tắt tự chọn đội: `AdminToggleSelfTeamSelectionRequest(RoomId, AdminToken, AllowSelfTeamSelection)`, `AdminToggleSelfTeamSelectionResponse(...)`.
    - Sự kiện thông báo bị loại: `PlayerKickedEvent(PlayerId, Reason)`.
- **Xử lý mutation nguyên tử tại máy chủ (`Server/RoomInstance.cs`, `RoomManager.cs`):**
  - Phương thức `TryAdminMovePlayer`:
    - Cho phép Admin chuyển người chơi vào đội chỉ định (bỏ qua cờ cấm tự chọn đội).
    - Kiểm tra sức chứa đội mục tiêu (`Capacity`): nếu đội đích đã đầy, từ chối nguyên tử với mã lỗi `TEAM_FULL`, giữ nguyên đội cũ của người chơi.
    - Hỗ trợ gỡ người chơi khỏi đội về danh sách chờ (`TargetTeamId = null`).
  - Phương thức `TryKickPlayer`:
    - Xóa người chơi khỏi từ điển `_players`, tự động thu hồi `ReconnectToken` khiến người chơi không thể dùng token cũ để kết nối lại.
    - Giải phóng vị trí trong đội nếu người chơi đang thuộc đội nào đó.
    - Tăng `Version++` và ghi nhận mốc hoạt động.
  - Phương thức `TryToggleSelfTeamSelection`:
    - Cho phép Admin bật/tắt chính sách tự chọn đội của phòng trong thời gian ở phòng chờ.
- **Giao thức SignalR Hub (`Server/RoomHub.cs`):**
  - Triển khai 3 API: `AdminMovePlayer`, `AdminKickPlayer`, `AdminToggleSelfTeamSelection`.
  - Quản lý SignalR Groups:
    - Khi Admin chuyển đội người chơi: tự động gỡ ConnectionId của người chơi đó khỏi nhóm `team_{OldTeamId}` và thêm vào nhóm `team_{NewTeamId}`.
    - Khi Admin kick: gửi sự kiện `KickedFromRoom` trực tiếp cho ConnectionId bị kick, gỡ khỏi `room_{RoomId}` và nhóm đội, sau đó broadcast `PlayerLeft` và cập nhật quân số cho cả phòng.
- **Giao diện Client (`Pages/Lobby.razor`, `Pages/Online.razor`, `wwwroot/css/multiplayer.css`):**
  - Admin Toolbar: Bổ sung checkbox `[☑ Tự chọn đội]` ngay trên tiêu đề danh sách đội để bật/tắt quyền tự do chọn đội cho người chơi.
  - Danh sách người chơi (`player-roster`): Với tài khoản Admin, hiển thị dropdown chọn đội (`admin-team-select`) cho từng người chơi và nút đỏ `[❌]` để đuổi người chơi vi phạm.
  - Trải nghiệm người chơi bị kick: Nhận sự kiện `KickedFromRoom`, lập tức tự động thoát khỏi Lobby về màn hình vào phòng và hiển thị thông báo rõ ràng kèm lý do.

## File thay đổi chính

| Phần | File |
|---|---|
| Giao thức DTO | `Shared/RoomModels.cs` (`AdminMovePlayerRequest/Response`, `AdminKickPlayerRequest/Response`, `AdminToggleSelfTeamSelectionRequest/Response`, `PlayerKickedEvent`) |
| Máy chủ | `Server/RoomInstance.cs`, `Server/RoomManager.cs`, `Server/RoomHub.cs` |
| Kết nối client | `Services/MultiplayerConnection.cs` (`AdminMovePlayerAsync`, `AdminKickPlayerAsync`, `AdminToggleSelfTeamSelectionAsync`, các event tương ứng) |
| Giao diện & CSS | `Pages/Lobby.razor`, `Pages/Online.razor`, `wwwroot/css/multiplayer.css` |
| Unit Test | `Tests/Multiplayer.UnitTests/RosterAdminTests.cs` (8 test cases: move thành công, move đội đầy, unauthorized, kick giải phóng slot, chặn reconnect, toggle policy) |
| Integration Test | `Tests/Multiplayer.IntegrationTests/RosterAdminIntegrationTests.cs` (test SignalR broadcast move, kick, evict group) |
| Tiến độ | `TIEN_DO_MULTIPLAYER.md`, báo cáo này |

## Kết quả kiểm thử tự động

| Lệnh/kiểm tra | Kết quả |
|---|---|
| `dotnet build Server/TruyTimDanChu.Server.csproj` | PASS, 0 warning, 0 error |
| `dotnet build TruyTimDanChu.csproj` | PASS, 0 warning, 0 error |
| `dotnet run --project Tests/GameSmoke.csproj` | PASS: mở đầu, 4 nhiệm vụ, lựa chọn sai, phản hồi cuối và lưu/tiếp tục |
| `dotnet test Tests/Multiplayer.UnitTests/Multiplayer.UnitTests.csproj` | 74 pass (thêm 8 test `RosterAdminTests`), 0 fail |
| `dotnet test Tests/Multiplayer.IntegrationTests/Multiplayer.IntegrationTests.csproj` | 13 pass (thêm test SignalR broadcast move, kick, evict), 0 fail |

## Nghiệm thu giao diện thực tế (Chrome CDP Multi-Client Automation)

Kịch bản tự động hóa trên Chrome Headless 3 phiên độc lập: `Admin H`, `Minh Quân (Player A)`, `Hải Yến (Player B)`.
- **Bước 1:** Admin H tạo phòng `DC-6N2G` với tùy chọn tự chọn đội bị TẮT. Thêm Đội Đỏ (sức chứa 1) và Đội Xanh (sức chứa 2).
- **Bước 2:** Player A và Player B vào phòng. Xác nhận Player A không có nút tự chọn đội.
- **Bước 3:** Admin H dùng dropdown phân Minh Quân vào Đội Đỏ ➔ Phía Player A cập nhật tức thời thành Đội Đỏ.
- **Bước 4:** Admin H thử phân Hải Yến vào Đội Đỏ (đã đủ 1/1) ➔ Hệ thống chặn nguyên tử, Admin nhận thông báo lỗi "Đội này đã đủ số lượng thành viên tối đa", Hải Yến vẫn an toàn ở danh sách chờ.
- **Bước 5:** Admin H phân Hải Yến vào Đội Xanh ➔ Phía Player B nhận đội thành công.
- **Bước 6:** Admin H bấm nút kick Minh Quân:
  - Phía Player A: lập tức bị điều hướng ra màn hình ngoài và hiển thị khung cảnh báo màu đỏ: "⚠️ Bạn đã bị Quản trị viên mời ra khỏi phòng".
  - Phía Admin H & Player B: Minh Quân biến mất khỏi danh sách người chơi, Đội Đỏ được giải phóng slot trở về `0 / 1 người`.
- **Bước 7:** Admin H chuyển Hải Yến sang Đội Đỏ (vừa được giải phóng) ➔ Thành công, Đội Đỏ thành 1/1, Đội Xanh thành 0/2.
- **Bằng chứng ảnh chụp:**
  - `p08_01_admin_moved_players.png`: Admin điều phối Minh Quân vào Đội Đỏ, Hải Yến vào Đội Xanh.
  - `p08_02_admin_full_team_error.png`: Thông báo lỗi khi Admin cố tình chuyển người vào đội đã đầy.
  - `p08_03_player_a_kicked_notification.png`: Màn hình Player A nhận thông báo khi bị kick ra ngoài.
  - `p08_04_admin_and_b_after_kick.png`: Giao diện Admin sau khi kick Player A và chuyển Player B vào Đội Đỏ.

