# BÁO CÁO NGHIỆM THU PHASE 11: COUNTDOWN ĐỒNG BỘ

---

## 1. MỤC TIÊU VÀ PHẠM VI NGHIỆM THU
- Triển khai cơ chế đếm ngược (Countdown) đồng bộ bằng mốc thời gian máy chủ chuẩn (`MatchStartTimeUtc`), đảm bảo tất cả client chuyển vào trận cùng một thời điểm.
- Hỗ trợ quyền điều hành của Quản trị viên (Admin):
  - Admin có nút bắt đầu trận đấu khi phòng thỏa mãn đầy đủ điều kiện (`ReadinessValidator.CanStart == true`).
  - Admin có quyền hủy đếm ngược (`AdminCancelCountdown`) bất kỳ lúc nào trong thời gian đếm ngược để đưa phòng quay lại trạng thái `Lobby`.
- Quy tắc bảo đảm an toàn trận đấu:
  - Khóa toàn bộ các thao tác sửa đổi cấu hình đội (thêm, sửa, xóa, đổi thứ tự), tự chọn/rời đội, đổi ngoại hình (avatar), sẵn sàng, khóa phòng trong suốt giai đoạn Countdown (`RoomErrorCodes.InvalidRoomStatus`).
  - Chống double start: ngăn chặn click đúp hoặc gửi yêu cầu bắt đầu đồng thời tạo ra hai trận đấu.
  - Hủy đếm ngược vô hiệu hóa callback hẹn giờ cũ, không cho phép kích hoạt `Playing` trái ý muốn.
- Đồng bộ nguyên tử qua SignalR (`CountdownStarted`, `CountdownCanceled`, `MatchStarted`), cập nhật `MatchId`, `MatchStartTimeUtc`, `RoomVersion` và trạng thái `RoomStatus`.
- Bảo toàn tuyệt đối chế độ chơi đơn (Single-Player), `GameSmoke` đạt 100% PASS.

---

## 2. KIẾN TRÚC VÀ CÁC THAY ĐỔI TRIỂN KHAI

### 2.1. Shared Layer (`Shared/RoomModels.cs`)
- **`RoomSnapshot`**:
  - Thêm các thuộc tính mới: `string? MatchId = null`, `DateTimeOffset? MatchStartTimeUtc = null`.
- **DTOs & SignalR Events**:
  - `AdminStartMatchRequest(string RoomId, string AdminToken, int CountdownSeconds = 3)`
  - `AdminStartMatchResponse(bool Success, string? ErrorCode, string? MatchId, DateTimeOffset? MatchStartTimeUtc, int RoomVersion)`
  - `CountdownStartedEvent(string MatchId, int CountdownSeconds, DateTimeOffset MatchStartTimeUtc)`
  - `AdminCancelCountdownRequest(string RoomId, string AdminToken)`
  - `AdminCancelCountdownResponse(bool Success, string? ErrorCode, int RoomVersion)`
  - `CountdownCanceledEvent()`
  - `MatchStartedEvent(string MatchId, DateTimeOffset StartedAtUtc)`
- **Error Codes**:
  - `RoomErrorCodes.NotReadyToStart`: Chưa đủ điều kiện bắt đầu trận.
  - `RoomErrorCodes.CountdownAlreadyActive`: Trận đấu đang trong quá trình đếm ngược.
  - `RoomErrorCodes.MatchAlreadyStarted`: Trận đấu đã bắt đầu.
  - `RoomErrorCodes.CountdownNotActive`: Không có đếm ngược nào đang diễn ra để hủy.

### 2.2. Server Layer (`Server/RoomInstance.cs`, `Server/RoomManager.cs`, `Server/RoomHub.cs`, `Server/ServerBootstrap.cs`)
- **`RoomInstance`**:
  - Quản lý `MatchId`, `MatchStartTimeUtc`, `_countdownCts`, và `UtcNowProvider` (cho phép mock clock trong unit test).
  - `TryStartMatch`: Kiểm tra quyền admin, kiểm tra điều kiện `ReadinessValidator.Validate`, khởi tạo `MatchId`, chuyển `Status = RoomStatus.Countdown`, kích hoạt background scheduler tự động chuyển sang `RoomStatus.Playing` khi đến hạn.
  - `TryCancelCountdown`: Hủy `_countdownCts`, đặt lại `Status = RoomStatus.Lobby`, xóa `MatchId` và `MatchStartTimeUtc`.
  - `TryForceAdvanceToPlaying`: Hỗ trợ kiểm thử thời gian thực nhanh và tất định.
  - Chặn toàn bộ các thao tác chỉnh sửa đội hình, đổi avatar khi phòng không ở trạng thái `Lobby`.
- **`RoomManager`**:
  - Cung cấp phương thức `AdminStartMatch` và `AdminCancelCountdown`.
  - Expose sự kiện `event Action<string, string, DateTimeOffset>? OnMatchStarted`.
- **`ServerBootstrap` & `RoomHub`**:
  - `ServerBootstrap`: Đăng ký lắng nghe sự kiện `OnMatchStarted` từ `RoomManager`, tự động broadcast SignalR event `MatchStarted` tới nhóm phòng `room_{roomId}`.
  - `RoomHub`: Cung cấp Hub method `AdminStartMatch` (broadcast `CountdownStarted`) và `AdminCancelCountdown` (broadcast `CountdownCanceled`).

### 2.3. Client & UI Layer (`Services/MultiplayerConnection.cs`, `Pages/Lobby.razor`, `wwwroot/css/multiplayer.css`)
- **`MultiplayerConnection`**:
  - Đăng ký các sự kiện `CountdownStarted`, `CountdownCanceled`, `MatchStarted`.
  - Bổ sung phương thức `AdminStartMatchAsync`, `AdminCancelCountdownAsync`.
- **`Pages/Lobby.razor`**:
  - Khi Admin kiểm tra phòng đủ điều kiện (`validation.CanStart`), hiển thị nút to màu xanh: `🚀 BẮT ĐẦU TRẬN ĐẤU (3S)`.
  - Khi phòng chuyển sang `Countdown`, hiển thị modal đếm ngược toàn màn hình (`countdown-overlay`) với số đếm ngược thời gian thực `3... 2... 1...` và nút `✕ HỦY ĐẾM NGƯỢC` cho Admin.
  - Khi nhận sự kiện `CountdownCanceled`, overlay tự động đóng và trở về giao diện chuẩn bị.
  - Khi nhận sự kiện `MatchStarted`, trạng thái phòng chuyển sang `Playing`.
- **`wwwroot/css/multiplayer.css`**: Thiết kế overlay đếm ngược phong cách pixel art cổ điển với backdrop blur, hiệu ứng phát sáng typography và nút bấm nổi bật.

---

## 3. KẾT QUẢ KIỂM THỬ TỰ ĐỘNG

### 3.1. Unit Tests (`Tests/Multiplayer.UnitTests/CountdownTests.cs`)
- **113/113 passed (100%)** (Thời gian chạy: 25 ms):
  - `AdminStartMatch_ValidLobby_TransitionsToCountdownAndSetsMatchIdAndStartTime`: Bắt đầu countdown thành công, lưu đúng MatchId và thời gian server.
  - `AdminStartMatch_UnreadyOrInvalidLobby_FailsWithAppropriateError`: Kiểm tra chặn bắt đầu khi có người chơi chưa ready hoặc token admin không hợp lệ.
  - `AdminStartMatch_WhenAlreadyCountdownOrPlaying_RejectsDuplicateStart`: Chống bắt đầu lặp khi đang Countdown hoặc Playing.
  - `Mutations_BlockedDuringCountdown`: Kiểm tra 11 thao tác chỉnh sửa đội, đổi avatar, ready, khóa phòng đều bị từ chối khi đang Countdown.
  - `AdminCancelCountdown_RevertsToLobbyAndInvalidatesCallbacks`: Hủy countdown đưa phòng về Lobby và vô hiệu hóa chuyển tiếp trận.
  - `ClockAdvance_TransitionsToPlayingExactlyOnce`: Chuyển sang Playing đúng một lần duy nhất khi hết giờ đếm ngược.
  - `ConcurrentStartAttempts_OnlyOneSucceeds`: Kiểm tra 20 luồng bắt đầu trận đồng thời, duy nhất 1 luồng thành công.

### 3.2. Integration Tests (`Tests/Multiplayer.IntegrationTests/CountdownIntegrationTests.cs`)
- **16/16 passed (100%)** (Thời gian chạy: 1 s):
  - `SignalR_CountdownAndMatchStart_FullFlow`: Kiểm thử tích hợp đầy đủ quy trình SignalR thật giữa Admin và 2 Player:
    - Admin kích hoạt countdown -> Cả 2 Player nhận `CountdownStartedEvent` cùng MatchId và MatchStartTimeUtc.
    - Player A thử đổi avatar trong lúc đếm ngược -> Bị từ chối.
    - Admin hủy đếm ngược -> Cả 2 Player nhận `CountdownCanceledEvent`.
    - Admin bắt đầu lại với 1 giây -> Hết hạn tự động broadcast `MatchStartedEvent` tới toàn bộ người chơi trong phòng.

### 3.3. GameSmoke Test (`Tests/GameSmoke.csproj`)
- **PASS**:
  - Chế độ chơi đơn (Single-Player), 4 nhiệm vụ, hội thoại, phản hồi và lưu/tiếp tục hoàn toàn nguyên vẹn, độc lập tuyệt đối với hạ tầng multiplayer.

---

## 4. KẾT QUẢ KIỂM TRA GIAO DIỆN CHROME CDP (HEADLESS CHROME)

Chạy kịch bản tự động đa tab trên Chrome CDP (`scratch/verify_phase11.mjs`):
1. **Admin tạo phòng và cấu hình đội thi đấu**:
   - Phòng "Giải Đấu Toàn Dân Minh Đăng", 2 đội Sao Vàng và Búa Liềm.
   - Minh Đức (Đội Sao Vàng) và Thanh Hằng (Đội Búa Liềm) vào phòng và bấm Sẵn sàng.
   - Banner chuyển sang xanh: "Đủ điều kiện bắt đầu!". Nút `🚀 BẮT ĐẦU TRẬN ĐẤU (3S)` hiển thị trên giao diện Admin.
2. **Admin bấm Bắt đầu trận đấu**:
   - Overlay đếm ngược hiển thị trên tất cả các tab với con số đếm ngược `3`.
   - Ảnh minh chứng Admin: `scratch/evidence/p11_01_countdown_overlay_admin.png`.
   - Ảnh minh chứng Player: `scratch/evidence/p11_02_countdown_overlay_player.png`.
3. **Admin bấm Hủy đếm ngược**:
   - Overlay đếm ngược đóng lập tức, phòng quay về trạng thái Lobby ban đầu.
   - Ảnh minh chứng: `scratch/evidence/p11_03_countdown_canceled_revert_lobby.png`.
4. **Admin bấm Bắt đầu trận đấu lần 2 và để đếm ngược hoàn tất**:
   - Sau 3 giây, server tự động chuyển trạng thái phòng sang `Playing`.
   - Huy hiệu phòng cập nhật sang `● Playing`.
   - Ảnh minh chứng: `scratch/evidence/p11_04_match_started_playing.png`.

---

## 5. KẾT LUẬN NGHIỆM THU
- **Đạt điều kiện nghiệm thu Phase 11**: Toàn bộ chức năng Countdown đồng bộ, khóa phòng/đội khi đếm ngược, hủy countdown và chuyển sang Playing đã hoàn thành, vượt qua 100% bài kiểm tra tự động và kiểm tra giao diện thực tế.
- Sẵn sàng chuyển tiếp sang **Phase 12: Bản đồ riêng theo đội**.

