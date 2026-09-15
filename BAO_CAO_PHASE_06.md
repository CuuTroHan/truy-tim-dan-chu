# Báo cáo Phase 06 — Chỉnh sửa danh sách đội

Ngày: 15/09/2026.

**Trạng thái: Hoàn thành nghiệm thu toàn diện** (Unit test 57/57 pass, Integration test 11/11 pass, GameSmoke pass, Chrome DevTools Protocol multi-client automation pass 100%). Sẵn sàng chuyển sang Phase 07.

## Chức năng đã triển khai

- **Mô hình DTO và thông điệp giao tiếp (`Shared/RoomModels.cs`):**
  - Cập nhật các DTO cho 3 thao tác chỉnh sửa:
    - Sửa đội: `AdminUpdateTeamRequest`, `AdminUpdateTeamResponse`.
    - Sắp xếp thứ tự: `AdminReorderTeamsRequest`, `AdminReorderTeamsResponse`.
    - Xóa đội rỗng: `AdminRemoveTeamRequest`, `AdminRemoveTeamResponse`.
  - Bổ sung các mã lỗi nghiệp vụ:
    - `CAPACITY_BELOW_OCCUPANCY`: Ngăn giảm sức chứa xuống dưới số thành viên hiện có trong đội.
    - `TEAM_NOT_EMPTY`: Ngăn xóa đội đang có người tham gia (chống tạo thành viên mồ côi).
    - `INVALID_TEAM_ORDER`: Ngăn reorder thiếu, thừa hoặc trùng lặp ID đội.
    - `TEAM_NOT_FOUND`: Đội cần thao tác không tồn tại.
- **Xử lý mutation nguyên tử tại máy chủ (`Server/RoomInstance.cs`, `RoomManager.cs`):**
  - Phương thức `TryUpdateTeam`:
    - Chỉ cho phép sửa khi phòng ở trạng thái `Lobby`.
    - Kiểm tra tên hợp lệ (2–30 ký tự), kiểm tra trùng tên với đội khác trong phòng.
    - Kiểm tra mã màu hex hợp lệ.
    - Kiểm tra sức chứa mới không nhỏ hơn số người đang ở trong đội (`occupancy`).
    - Kiểm tra tổng sức chứa mới của toàn phòng không vượt quá `MaxPlayersPerRoom` (40 người).
    - Tăng `Version++` và cập nhật mốc thời gian hoạt động.
  - Phương thức `TryReorderTeams`:
    - Kiểm tra tập hợp ID gửi lên khớp chính xác 1-1 với tập ID hiện có của phòng (không trùng, không thiếu, không có ID lạ).
    - Cập nhật trường `DisplayOrder` cho từng đội theo thứ tự mới.
    - Tăng `Version++`.
  - Phương thức `TryRemoveTeam`:
    - Chỉ xóa khi `currentOccupancy == 0`. Nếu có thành viên trong đội thì từ chối ngay.
    - Xóa khỏi từ điển và tự động đánh lại số thứ tự `DisplayOrder` cho các đội còn lại (0, 1, 2, ...).
    - Tăng `Version++`.
- **Giao thức SignalR Hub (`Server/RoomHub.cs`):**
  - Triển khai 3 API quản trị: `AdminUpdateTeam`, `AdminReorderTeams`, `AdminRemoveTeam`.
  - Mọi API đều kiểm tra quyền `VerifyAdmin(AdminToken)`. Nếu không đúng quyền, trả về `UNAUTHORIZED_ADMIN`.
  - Tự động phát broadcast các sự kiện SignalR tương ứng tới nhóm `room_{RoomId}`:
    - `TeamUpdated` (kèm `TeamSnapshot`).
    - `TeamsReordered` (kèm danh sách `List<TeamSnapshot>`).
    - `TeamRemoved` (kèm mã `RemovedTeamId`).
- **Giao diện Client (`Pages/Lobby.razor`, `Pages/Online.razor.css`):**
  - Mở rộng thẻ đội `team-card` cho Admin với các nút hành động trực quan:
    - Nút di chuyển thứ tự `▲` (lên) và `▼` (xuống): tự động disable ở biên danh sách.
    - Nút chỉnh sửa `✏️`: Mở form chỉnh sửa tại chỗ (inline edit) cho phép đổi tên, sức chứa và màu sắc đội từ bảng màu.
    - Nút xóa đội `🗑️`: Tự động disable nếu đội đang có người tham gia.
  - Xử lý các sự kiện thời gian thực trên client:
    - Khi Admin sửa, sắp xếp hoặc xóa đội, toàn bộ client của người chơi trong phòng đều tự động cập nhật danh sách đội và thứ tự hiển thị tức thì mà không bị lệch dữ liệu hay reload trang.

## File thay đổi chính

| Phần | File |
|---|---|
| Giao thức DTO | `Shared/RoomModels.cs` (`AdminUpdateTeamRequest/Response`, `AdminReorderTeamsRequest/Response`, `AdminRemoveTeamRequest/Response`, mã lỗi mới) |
| Máy chủ | `Server/RoomInstance.cs`, `Server/RoomManager.cs`, `Server/RoomHub.cs` |
| Kết nối client | `Services/MultiplayerConnection.cs` (`AdminUpdateTeamAsync`, `AdminReorderTeamsAsync`, `AdminRemoveTeamAsync`, các event tương ứng) |
| Giao diện | `Pages/Lobby.razor`, `Pages/Online.razor.css` |
| Unit Test | `Tests/Multiplayer.UnitTests/EditTeamTests.cs` (9 test cases chuyên biệt) |
| Integration Test | `Tests/Multiplayer.IntegrationTests/EditTeamIntegrationTests.cs` |
| Tiến độ | `TIEN_DO_MULTIPLAYER.md`, báo cáo này |

## Kết quả kiểm thử tự động

| Lệnh/kiểm tra | Kết quả |
|---|---|
| `dotnet build Server/TruyTimDanChu.Server.csproj -c Release` | PASS, 0 warning, 0 error |
| `dotnet build TruyTimDanChu.csproj -c Release` | PASS, 0 warning, 0 error |
| `dotnet run --project Tests/GameSmoke.csproj -c Release` | PASS: mở đầu, 4 nhiệm vụ, lựa chọn sai, phản hồi cuối và lưu/tiếp tục |
| `dotnet test Tests/Multiplayer.UnitTests/Multiplayer.UnitTests.csproj -c Release` | 57 pass (thêm 9 test `EditTeamTests` kiểm tra sửa/reorder/xóa rỗng/chặn giảm capacity/chặn xóa đội có người/chặn sai quyền), 0 fail |
| `dotnet test Tests/Multiplayer.IntegrationTests/Multiplayer.IntegrationTests.csproj -c Release` | 11 pass (thêm test SignalR broadcast sửa, đảo thứ tự, xóa đội và từ chối non-admin), 0 fail |

## Kết quả kiểm chứng giao diện Chrome (CDP Multi-Client Automation)

Kịch bản kiểm thử đa client tự động trên Chrome headless (`scratch/verify_phase06.mjs`) với 3 phiên đồng thời (Admin H, Player A "Thành Nam", Player B "Hải Yến"):

1. **Khởi tạo 3 đội ban đầu:** Admin H tạo 3 đội: `Đội Đỏ` (3 người), `Đội Xanh` (5 người), `Đội Vàng` (2 người). Player A và Player B thấy đủ 3 đội.
2. **Sửa Đội Đỏ:**
   - Admin H click `✏️` trên Đội Đỏ, đổi tên thành `Đội Sao Đỏ`, đổi màu sang xanh lá `#43A047`, tăng sức chứa lên `4`.
   - Click `LƯU` -> Màn hình Admin cập nhật ngay lập tức.
   - Player A và Player B ngay lập tức nhận cập nhật `Đội Sao Đỏ (0 / 4 người)` với mã màu mới theo thời gian thực.
3. **Đảo thứ tự (Reorder):**
   - Admin H click `▲` trên `Đội Xanh` để đưa Đội Xanh lên vị trí đầu tiên.
   - Danh sách chuyển thành: `[Đội Xanh, Đội Sao Đỏ, Đội Vàng]`.
   - Cả Player A và Player B đều tự động cập nhật danh sách với đúng thứ tự `[Đội Xanh, Đội Sao Đỏ, Đội Vàng]` mà không bị lệch thứ tự.
4. **Xóa đội rỗng:**
   - Admin H click `🗑️` trên `Đội Vàng`.
   - Đội Vàng biến mất khỏi danh sách phòng trên cả 3 màn hình (Admin H, Player A, Player B).
   - Danh sách cuối cùng đồng bộ 100% gồm 2 đội: `Đội Xanh (0 / 5 người)` và `Đội Sao Đỏ (0 / 4 người)`.
5. **Bằng chứng hình ảnh:**
   - `scratch/evidence/p06_01_admin_after_edit_reorder_delete.png`: Màn hình Admin H sau khi hoàn tất chỉnh sửa, đảo thứ tự và xóa đội rỗng.
   - `scratch/evidence/p06_02_player_a_after_updates.png`: Màn hình Player A cập nhật danh sách và thứ tự thời gian thực.
   - `scratch/evidence/p06_03_player_b_after_updates.png`: Màn hình Player B cập nhật danh sách và thứ tự thời gian thực.
6. **Console:** 0 lỗi console, 0 unhandled exception.

**Kết luận Phase 06:** Đạt 100% tiêu chí nghiệm thu. Admin chỉnh sửa tên/màu/capacity, sắp xếp thứ tự và xóa đội rỗng thành công; các ràng buộc bảo vệ occupancy được thực thi nghiêm ngặt; trạng thái client đồng bộ nhất quán. Sẵn sàng chuyển sang Phase 07.

