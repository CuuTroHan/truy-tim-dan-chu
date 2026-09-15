# Báo cáo Phase 07 — Tự chọn hoặc rời đội

Ngày: 15/09/2026.

**Trạng thái: Hoàn thành nghiệm thu toàn diện** (Unit test 66/66 pass, Integration test 12/12 pass, GameSmoke pass, Chrome DevTools Protocol multi-client automation pass 100%). Sẵn sàng chuyển sang Phase 08.

## Chức năng đã triển khai

- **Mô hình DTO và thông điệp giao tiếp (`Shared/RoomModels.cs`):**
  - Cập nhật DTO cho hành động tự chọn và rời đội:
    - Vào đội: `JoinTeamRequest(RoomId, PlayerId, TeamId)`, `JoinTeamResponse(Success, ErrorCode, TeamId, RoomVersion)`.
    - Rời đội: `LeaveTeamRequest(RoomId, PlayerId)`, `LeaveTeamResponse(Success, ErrorCode, RoomVersion)`.
    - Sự kiện broadcast thời gian thực: `PlayerTeamChangedEvent(PlayerId, OldTeamId, NewTeamId)`.
  - Bổ sung mã lỗi nghiệp vụ:
    - `SELF_SELECTION_DISABLED`: Từ chối khi phòng đã cấu hình cấm tự chọn đội.
    - `TEAM_FULL`: Từ chối khi đội đã đủ số lượng thành viên tối đa theo `Capacity`.
    - `PLAYER_NOT_FOUND`: Người chơi không tồn tại trong phòng.
- **Xử lý mutation nguyên tử tại máy chủ (`Server/RoomInstance.cs`, `RoomManager.cs`):**
  - Phương thức `TryJoinTeam`:
    - Kiểm tra trạng thái phòng (`Lobby`).
    - Kiểm tra cờ `AllowSelfTeamSelection`.
    - Kiểm tra người chơi có tồn tại trong phòng.
    - Kiểm tra đội mục tiêu tồn tại.
    - Tính toán nguyên tử số lượng thành viên hiện tại trong đội: nếu `currentOccupancy >= targetTeam.Capacity`, từ chối ngay lập tức và giữ nguyên trạng thái cũ của người chơi.
    - Rời khỏi đội cũ (nếu có) và gia nhập đội mới một cách an toàn (atomic switch).
    - Cập nhật `Version++` và mốc hoạt động `LastActivity`.
  - Phương thức `TryLeaveTeam`:
    - Rời khỏi đội hiện tại (nếu đang ở trong đội), gán `player.TeamId = null`.
    - Cập nhật `Version++`.
- **Giao thức SignalR Hub (`Server/RoomHub.cs`):**
  - Triển khai 2 API: `JoinTeam`, `LeaveTeam`.
  - Quản lý SignalR Group cho từng đội: tự động rời nhóm `team_{oldTeamId}` và tham gia nhóm `team_{newTeamId}`.
  - Broadcast sự kiện `PlayerTeamChanged` tới toàn bộ người trong phòng `room_{RoomId}`.
- **Giao diện Client (`Pages/Lobby.razor`, `wwwroot/css/multiplayer.css`):**
  - Hiển thị danh sách thành viên trực tiếp trong từng thẻ đội dạng thẻ chip (`member-chip`).
  - Nút hành động tương tác:
    - Hiển thị nút **[VÀO ĐỘI]** khi chưa ở trong đội đó.
    - Tự động khóa và đổi nhãn thành **[ĐÃ ĐẦY]** khi đội đủ quân số.
    - Hiển thị nút **[RỜI ĐỘI]** khi người chơi đang thuộc đội đó.
  - Cập nhật nhãn đội trên danh sách người chơi phòng chờ (`player-team-pill`) theo màu đội thực tế hoặc `Chưa vào đội`.
  - Khắc phục triệt để lỗi CSS isolation bằng việc tạo `wwwroot/css/multiplayer.css` toàn cục, đảm bảo giao diện sắc nét, đồng bộ và chuyên nghiệp.

## File thay đổi chính

| Phần | File |
|---|---|
| Giao thức DTO | `Shared/RoomModels.cs` (`JoinTeamRequest/Response`, `LeaveTeamRequest/Response`, `PlayerTeamChangedEvent`, mã lỗi mới) |
| Máy chủ | `Server/RoomInstance.cs`, `Server/RoomManager.cs`, `Server/RoomHub.cs` |
| Kết nối client | `Services/MultiplayerConnection.cs` (`JoinTeamAsync`, `LeaveTeamAsync`, event `PlayerTeamChanged`) |
| Giao diện & CSS | `Pages/Lobby.razor`, `wwwroot/css/multiplayer.css`, `wwwroot/index.html` |
| Unit Test | `Tests/Multiplayer.UnitTests/TeamSelectionTests.cs` (9 test cases: tự chọn, kiểm tra đầy đội, atomic team switch, race barrier) |
| Integration Test | `Tests/Multiplayer.IntegrationTests/TeamSelectionIntegrationTests.cs` |
| Tiến độ | `TIEN_DO_MULTIPLAYER.md`, báo cáo này |

## Kết quả kiểm thử tự động

| Lệnh/kiểm tra | Kết quả |
|---|---|
| `dotnet build Server/TruyTimDanChu.Server.csproj` | PASS, 0 warning, 0 error |
| `dotnet build TruyTimDanChu.csproj` | PASS, 0 warning, 0 error |
| `dotnet run --project Tests/GameSmoke.csproj` | PASS: mở đầu, 4 nhiệm vụ, lựa chọn sai, phản hồi cuối và lưu/tiếp tục |
| `dotnet test Tests/Multiplayer.UnitTests/Multiplayer.UnitTests.csproj` | 66 pass (thêm 9 test `TeamSelectionTests`), 0 fail |
| `dotnet test Tests/Multiplayer.IntegrationTests/Multiplayer.IntegrationTests.csproj` | 12 pass (thêm test SignalR broadcast chọn/rời đội và kiểm tra capacity), 0 fail |

## Nghiệm thu giao diện thực tế (Chrome CDP Multi-Client Automation)

Kịch bản tự động hóa trên Chrome Headless 3 phiên độc lập: `Admin H`, `Minh Quân (Player A)`, `Hải Yến (Player B)`.
- **Bước 1:** Admin H tạo phòng `DC-TCV2`, thêm 2 đội: Đội Đỏ (sức chứa 1 người) và Đội Xanh (sức chứa 3 người).
- **Bước 2:** Player A và Player B vào phòng bằng mã `DC-TCV2`.
- **Bước 3:** Player A bấm **[VÀO ĐỘI]** cho Đội Đỏ (1/1):
  - Phía Player A: nút chuyển thành **[RỜI ĐỘI]**, hiện chip `Minh Quân (Bạn)`.
  - Phía Player B: Đội Đỏ hiển thị `1 / 1 người`, nút bấm bị vô hiệu hóa với chữ **[ĐÃ ĐẦY]**.
- **Bước 4:** Player B bấm **[VÀO ĐỘI]** cho Đội Xanh:
  - Phía Player B: vào thành công Đội Xanh, hiện chip `Hải Yến (Bạn)`.
- **Bước 5:** Player A bấm **[RỜI ĐỘI]** khỏi Đội Đỏ:
  - Đội Đỏ lập tức giải phóng quân số về `0 / 1 người`.
  - Nút Đội Đỏ phía Player B lập tức mở lại thành **[VÀO ĐỘI]**.
- **Bước 6:** Player B đổi đội: bấm **[VÀO ĐỘI]** Đội Đỏ:
  - Phía Player B: chuyển sang Đội Đỏ (`1 / 1 người`), đồng thời Đội Xanh tự động giảm về `0 / 3 người`.
- **Bằng chứng ảnh chụp:**
  - `p07_01_player_a_joined_red_full.png`: Player A vào Đội Đỏ đạt 1/1, hiển thị nút RỜI ĐỘI.
  - `p07_02_player_b_sees_full_and_joins_blue.png`: Player B thấy Đội Đỏ đầy và gia nhập Đội Xanh.
  - `p07_03_player_b_switched_to_red_after_a_leaves.png`: Player B đổi thành công sang Đội Đỏ sau khi Player A rời.
  - `p07_04_admin_view_teams_and_players.png`: Góc nhìn của Admin đồng bộ hoàn toàn danh sách đội và người chơi theo thời gian thực.

