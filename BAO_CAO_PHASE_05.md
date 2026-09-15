# Báo cáo Phase 05 — Thêm đội với sức chứa riêng

Ngày: 15/09/2026.

**Trạng thái: Hoàn thành nghiệm thu toàn diện** (Unit test 48/48 pass, Integration test 10/10 pass, GameSmoke pass, Chrome DevTools Protocol multi-client automation pass 100%). Sẵn sàng chuyển sang Phase 06.

## Chức năng đã triển khai

- **Mô hình đội và sức chứa riêng (`Shared/RoomModels.cs`, `Server/TeamInstance.cs`):**
  - Thực thể `TeamInstance` quản lý: `TeamId`, `RoomId`, tên đội `Name`, mã màu `Color`, sức chứa tối đa `Capacity`, và thứ tự hiển thị `DisplayOrder`.
  - DTOs giao tiếp: `TeamSnapshot` (kèm `MemberCount`), `AdminAddTeamRequest`, `AdminAddTeamResponse`.
  - Cập nhật `JoinRoomResponse` tự động trả về danh sách các đội hiện có (`Teams`) khi người chơi mới tham gia phòng.
- **Bảo vệ giới hạn và thao tác nguyên tử tại máy chủ (`Server/RoomInstance.cs`, `RoomManager.cs`):**
  - Kiểm tra điều kiện chỉ được thêm đội khi phòng ở trạng thái `Lobby`.
  - Kiểm tra độ dài tên đội (2–30 ký tự) và chuẩn hóa chống trùng tên không phân biệt hoa thường (`DUPLICATE_TEAM_NAME`).
  - Kiểm tra định dạng mã màu hex hợp lệ (`#RGB` hoặc `#RRGGBB`, `INVALID_TEAM_COLOR`).
  - Kiểm tra sức chứa từng đội: từ 1 đến `MaxPlayersPerTeam` (8 người).
  - Kiểm tra số lượng đội tối đa trong phòng: không vượt quá `MaxTeamsPerRoom` (10 đội, `MAX_TEAMS_EXCEEDED`).
  - Kiểm tra tổng sức chứa tích lũy của toàn bộ các đội trong phòng: không vượt quá `MaxPlayersPerRoom` (40 người, `TOTAL_CAPACITY_EXCEEDED`).
  - Cơ chế khóa nguyên tử (`lock (_gate)`): bảo vệ chống Race Condition. Hai yêu cầu thêm đội đồng thời vượt tổng sức chứa chỉ có đúng một yêu cầu thành công, yêu cầu còn lại bị từ chối nguyên tử.
- **Quyền quản trị và SignalR Hub (`Server/RoomHub.cs`):**
  - Phương thức Hub `AdminAddTeam`: Xác thực `AdminToken` với `room.VerifyAdmin(token)`. Người chơi thông thường hoặc token giả mạo bị từ chối ngay với mã lỗi `UNAUTHORIZED_ADMIN`.
  - Khi thêm đội thành công, tự động broadcast sự kiện `TeamAdded` tới toàn bộ nhóm `room_{RoomId}`.
  - Đảm bảo tính cách ly phòng: Client ở các phòng khác không nhận được sự kiện thêm đội của phòng này.
- **Giao diện Client (`Pages/TeamEditor.razor`, `Pages/Lobby.razor`, `Pages/Online.razor`, `Pages/Online.razor.css`):**
  - Component `TeamEditor.razor` dành riêng cho Quản trị viên (Admin):
    - Nhập tên đội và sức chứa tùy ý (1–8 người).
    - Bảng màu mẫu 8 màu nhận diện trực quan: Đỏ, Xanh dương, Xanh lá, Cam, Tím, Lam ngọc, Vàng, Nâu.
    - Hiển thị thông báo lỗi chi tiết khi vi phạm bất kỳ giới hạn hoặc chính sách nào từ máy chủ.
  - Cập nhật `Lobby.razor`:
    - Khu vực "Các đội thi đấu" hiển thị danh sách đội dạng thẻ (thanh màu nhận diện, tên đội, chỉ số sức chứa `MemberCount / Capacity người`).
    - Lắng nghe sự kiện SignalR `TeamAdded`: Tự động cập nhật danh sách đội tức thì theo thời gian thực trên mọi máy khách (Admin và các Người chơi) mà không cần tải lại trang.

## File thay đổi chính

| Phần | File |
|---|---|
| Giao thức DTO | `Shared/RoomModels.cs` (`TeamSnapshot`, `AdminAddTeamRequest`, `AdminAddTeamResponse`, error codes) |
| Máy chủ | `Server/TeamInstance.cs`, `Server/RoomInstance.cs`, `Server/RoomManager.cs`, `Server/RoomHub.cs` |
| Kết nối client | `Services/MultiplayerConnection.cs` (`AdminAddTeamAsync`, event `TeamAdded`) |
| Giao diện | `Pages/TeamEditor.razor`, `Pages/Lobby.razor`, `Pages/Online.razor`, `Pages/Online.razor.css` |
| Unit Test | `Tests/Multiplayer.UnitTests/AddTeamTests.cs` (19 test cases) |
| Integration Test | `Tests/Multiplayer.IntegrationTests/AddTeamIntegrationTests.cs` |
| Tiến độ | `TIEN_DO_MULTIPLAYER.md`, báo cáo này |

## Kết quả kiểm thử tự động

| Lệnh/kiểm tra | Kết quả |
|---|---|
| `dotnet build Server/TruyTimDanChu.Server.csproj -c Release` | PASS, 0 warning, 0 error |
| `dotnet build TruyTimDanChu.csproj -c Release` | PASS, 0 warning, 0 error |
| `dotnet run --project Tests/GameSmoke.csproj -c Release` | PASS: mở đầu, 4 nhiệm vụ, lựa chọn sai, phản hồi cuối và lưu/tiếp tục |
| `dotnet test Tests/Multiplayer.UnitTests/Multiplayer.UnitTests.csproj -c Release` | 48 pass (thêm 19 test `AddTeamTests` gồm giới hạn tên/màu/capacity/tổng số đội/tổng sức chứa và đa luồng barrier), 0 fail |
| `dotnet test Tests/Multiplayer.IntegrationTests/Multiplayer.IntegrationTests.csproj -c Release` | 10 pass (thêm test SignalR broadcast đa client, cách ly phòng và từ chối non-admin), 0 fail |

## Kết quả kiểm chứng giao diện Chrome (CDP Multi-Client Automation)

Đã chạy kịch bản tự động hóa trên Chrome headless mô phỏng 3 phiên làm việc đồng thời (`scratch/verify_phase05.mjs`):

1. **Admin H tạo phòng:** Khởi tạo phòng `Đại Hội Dân Chủ Phase 05` -> nhận mã phòng -> vào Lobby hiển thị badge `✦ QUẢN TRỊ VIÊN` cùng form cấu hình `✦ Thêm đội thi đấu`.
2. **Player A & Player B vào phòng:** Tham gia bằng mã phòng -> vào phòng chờ hiển thị danh sách người chơi (Thành Nam, Hải Yến). Khu vực đội ban đầu báo "Chưa có đội thi đấu nào. Đang chờ Admin thiết lập đội."
3. **Admin H thêm Đội Đỏ (sức chứa 3, màu #E53935):**
   - Form gửi yêu cầu và thành công.
   - Thẻ `Đội Đỏ (0 / 3 người)` lập tức hiển thị trên giao diện của Admin H.
   - Đồng thời, cả Player A và Player B ngay lập tức xuất hiện thẻ `Đội Đỏ (0 / 3 người)` theo thời gian thực mà không cần reload trang.
4. **Admin H thêm Đội Xanh (sức chứa 5, màu #1E88E5):**
   - Thẻ `Đội Xanh (0 / 5 người)` xuất hiện trên cả 3 màn hình (Admin H, Player A, Player B). Tổng sức chứa phòng cập nhật thành `8 người`.
5. **Kiểm tra Validation & Giới hạn tại UI Admin:**
   - Admin H thử thêm lại đội trùng tên "Đội Đỏ" -> Máy chủ từ chối và UI hiển thị ngay thông báo lỗi thân thiện: `Tên đội này đã tồn tại trong phòng.`
6. **Bằng chứng hình ảnh đã thu thập:**
   - `scratch/evidence/p05_01_admin_teams_and_error.png`: Màn hình Admin H với 2 đội (Đỏ 3, Xanh 5) và thông báo lỗi trùng tên.
   - `scratch/evidence/p05_02_player_a_teams.png`: Màn hình Player A (Thành Nam) cập nhật real-time 2 đội Đỏ và Xanh.
   - `scratch/evidence/p05_03_player_b_teams.png`: Màn hình Player B (Hải Yến) cập nhật real-time 2 đội Đỏ và Xanh.
7. **Console:** 0 lỗi console, 0 unhandled exception.

**Kết luận Phase 05:** Đạt 100% tiêu chí nghiệm thu. Admin thêm được các đội với tên, màu và sức chứa riêng; các giới hạn được bảo vệ nguyên tử tại máy chủ; danh sách đội được đồng bộ tức thì tới tất cả người chơi trong phòng. Sẵn sàng chuyển sang Phase 06.

