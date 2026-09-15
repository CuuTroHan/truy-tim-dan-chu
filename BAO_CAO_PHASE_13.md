# BÁO CÁO NGHIỆM THU PHASE 13: DI CHUYỂN ĐƯỢC SERVER XÁC NHẬN

---

## 1. MỤC TIÊU VÀ PHẠM VI NGHIỆM THU
- Triển khai cơ chế di chuyển do máy chủ toàn quyền kiểm soát và xác nhận (**Server-Authoritative Movement**).
- Máy chủ nắm giữ vị trí thực tế của người chơi trong suốt trận đấu, máy khách không gửi tọa độ tùy ý mà chỉ gửi tín hiệu điều khiển (`Keys`) và số thứ tự (`Sequence`).
- Máy chủ tính toán vị trí mới dựa trên vật lý chuyển động và dữ liệu va chạm dùng chung (`TownCollision`):
  - Tốc độ di chuyển chuẩn 73 px/s.
  - Chuẩn hóa tốc độ khi đi chéo nhân hệ số `0.7071068f` để đảm bảo tổng tốc độ không vượt quá 73 px/s.
  - Chặn va chạm hộp giới hạn AABB với mọi vật thể (tường thư viện, tường xưởng, cụm nhà ven sông, công viên, sạp báo, sông) và giới hạn biên bản đồ.
- Chống gian lận và lỗi mạng:
  - Máy chủ tự tính `dt` theo đồng hồ server (kẹp tối đa 50 ms/bước), gửi spam gói tin gấp 10 lần cũng không thể tăng tốc.
  - Kiểm tra thứ tự gói tin (`Sequence`): từ chối các gói tin cũ hoặc trùng lặp (`OUTDATED_SEQUENCE`), không làm giật lùi vị trí người chơi.
  - Cơ chế hết hạn điều khiển (TTL = 400 ms): khi người chơi ngừng gửi input hoặc ẩn tab, trạng thái bước đi tự động chuyển về `Walking = 0`.
- Đồng bộ và cách ly mạng:
  - Khi người chơi di chuyển, máy chủ trả lời xác nhận (`MovementAck`) cho người gửi và chỉ broadcast sự kiện `PlayerMoved` tới các thành viên cùng đội trong nhóm `team_{teamId}`.
  - Tuyệt đối không rò rỉ tọa độ người chơi sang đội khác.
- Bảo toàn tuyệt đối chế độ chơi đơn (Single-Player), `GameSmoke` đạt 100% PASS.

---

## 2. KIẾN TRÚC VÀ CÁC THAY ĐỔI TRIỂN KHAI

### 2.1. Shared Layer (`Shared/MovementModels.cs`, `Shared/Game/TownCollision.cs`, `Shared/Game/MultiplayerSession.cs`)
- **`Shared/MovementModels.cs`**:
  - `PlayerMovementInput(string RoomId, string PlayerId, int Keys, int Sequence, long ClientTimestampMs)`: Gói tin input bàn phím gửi từ client lên máy chủ.
  - `MovementAck(bool Success, string? ErrorCode, int Sequence, float X, float Y, int Facing, int Walking, long ServerTimestampMs)`: Gói tin xác nhận từ máy chủ với tọa độ thực tế được duyệt.
  - `PlayerMovedBroadcast(string PlayerId, float X, float Y, int Facing, int Walking, int Sequence, long ServerTimestampMs)`: Gói tin thông báo vị trí đồng đội gửi cho những người cùng đội.
- **`Shared/Game/TownCollision.cs`**:
  - Module va chạm AABB dùng chung giữa client và server. Cung cấp hàm `SimulateStep` chuẩn hóa đường chéo và kiểm tra va chạm.
- **`Shared/Game/MultiplayerSession.cs`**:
  - Tích hợp vòng lặp gửi input mạng với tần số 10–15 Hz (`OnSendMovement`), đồng bộ vị trí khi nhận `MovementAck` và cập nhật tọa độ đồng đội (`UpdateTeammatePosition`).

### 2.2. Server Layer (`Server/TeamGameInstance.cs`, `Server/RoomHub.cs`)
- **`TeamMemberSession`**:
  - Lưu trữ trạng thái di chuyển: `X`, `Y`, `Facing`, `Walking`, `LastProcessedSequence`, `LastInputTimestamp`, `LastSimulatedTime`.
  - Phương thức `ProcessMovement`:
    - Khử bitmask phím hợp lệ (chỉ chấp nhận 0..15).
    - So sánh `sequence > LastProcessedSequence`, từ chối gói tin cũ.
    - Kẹp delta-time tối đa 50 ms.
    - Chạy mô phỏng vật lý `TownCollision.SimulateStep`.
    - Cập nhật tọa độ máy chủ và trả về `MovementAck`.
  - Phương thức `CheckTtl`: Tự động dừng hoạt ảnh bước đi nếu quá 400 ms không nhận input.
- **`TeamGameInstance`**:
  - Phương thức đồng bộ `TryProcessMovement`: Khóa luồng an toàn theo từng người chơi, sinh `PlayerMovedBroadcast`.
- **`RoomHub`**:
  - Hub method `SendMovement`: Xác thực phòng và người chơi, gọi `TryProcessMovement` và phát sự kiện `PlayerMoved` tới `Clients.OthersInGroup($"team_{teamId}")`.

### 2.3. Client & UI Layer (`Services/MultiplayerConnection.cs`, `Pages/MultiplayerGameView.razor`)
- **`MultiplayerConnection`**:
  - Bổ sung sự kiện `PlayerMoved` và phương thức gọi Hub `SendMovementAsync`.
- **`Pages/MultiplayerGameView.razor`**:
  - Lắng nghe `Connection.PlayerMoved` để cập nhật vị trí đồng đội theo thời gian thực trên Canvas.
  - Kết nối `_session.OnSendMovement` với máy chủ SignalR.

---

## 3. KẾT QUẢ KIỂM THỬ TỰ ĐỘNG

### 3.1. Unit Tests (`Tests/Multiplayer.UnitTests/MovementTests.cs`)
- 7 bài kiểm tra quy chuẩn máy chủ:
  1. `OneSecondStraightMovement_TravelsExactly73Pixels_WithinTolerance`: Di chuyển 1 giây thẳng đạt đúng 73 px sai số cho phép.
  2. `DiagonalMovement_IsNormalized_TravelsExpectedSpeed`: Di chuyển chéo được chuẩn hóa, tổng quãng đường 73 px, mỗi trục ~51.6 px.
  3. `WallAndRiverCollisions_BlockPlayerFromPassing`: Thử nghiệm va chạm sông và tường thư viện, người chơi không thể xuyên tường hay nhảy xuống nước.
  4. `MapBorders_ClampMovementWithinBounds`: Kiểm tra biên thế giới giữ người chơi trong khoảng [16, 1008] và [16, 624].
  5. `SpammingPackets10x_DoesNotIncreaseSpeed_ServerClampsDt`: Gửi spam 100 gói tin trong 1 giây không làm người chơi bay nhanh hơn 73 px/s.
  6. `OutdatedSequence_IsRejected_DoesNotRollbackPosition`: Gói tin sequence cũ bị từ chối với lỗi `OUTDATED_SEQUENCE`, không làm giật lùi vị trí.
  7. `Inactivity_ExceedingTTL_StopsWalkingAnimation`: Sau 400 ms không có input, hoạt ảnh `Walking` được đặt về 0.
- **Kết quả Unit Tests:** **124/124 PASSED (100%)**.

### 3.2. Integration Tests (`Tests/Multiplayer.IntegrationTests/MovementIntegrationTests.cs`)
- Kiểm thử luồng tích hợp SignalR toàn diện với 3 người chơi thật (A & B thuộc Đội Đỏ, C thuộc Đội Xanh):
  - Player A gửi `SendMovement` -> nhận `MovementAck` hợp lệ.
  - Player B nhận `PlayerMoved` của Player A với đúng tọa độ máy chủ xác nhận.
  - Player C không nhận bất kỳ gói tin di chuyển nào của Đội Đỏ.
  - Player A gửi sequence cũ -> bị từ chối với mã lỗi `OUTDATED_SEQUENCE`.
- **Kết quả Integration Tests:** **18/18 PASSED (100%)**.

### 3.3. Hồi quy Single-Player (`GameSmoke.csproj`)
- Chế độ chơi đơn giữ nguyên vẹn 100%: **PASS**.

---

## 4. BẰNG CHỨNG KIỂM THỬ GIAO DIỆN VÀ MẠNG (CHROME CDP)

Kịch bản kiểm thử đa phiên thực tế trên Chrome CDP (`scratch/verify_phase13.mjs`):
1. **Admin H:** Tạo phòng `DC-CDVK`, cấu hình Đội Đỏ và Đội Xanh.
2. **Player A (Đội Đỏ, avatar Bảo) & Player B (Đội Đỏ, avatar Dũng):** Vào Đội Đỏ, sẵn sàng.
3. **Player C (Đội Xanh, avatar Phương):** Vào Đội Xanh, sẵn sàng.
4. **Admin H:** Khởi động trận đấu.
5. **Player A di chuyển sang phải (D / ArrowRight):**
   - Chụp ảnh `p13_01_player_a_moved_right.png`: Player A chuyển hướng sang phải và bước sang bên phải vị trí ban đầu.
   - Chụp ảnh `p13_02_player_b_sees_a_moved.png`: Màn hình Player B hiển thị Player A đã di chuyển sang phải.
6. **Player A di chuyển hướng lên phía Bắc đâm vào tường thư viện:**
   - Chụp ảnh `p13_03_player_a_wall_collision.png`: Nhân vật dừng lại sát mép tường dưới của thư viện, không xuyên qua tường.
7. **Kiểm tra cách ly mạng của Player C:**
   - Không nhận bất kỳ gói tin `PlayerMoved` nào của Đội Đỏ (0 packets).

---

## 5. KẾT LUẬN VÀ NGHIỆM THU
- Phase 13 đã hoàn thành trọn vẹn, đáp ứng 100% các tiêu chí nghiệm thu:
  - [x] Tách dữ liệu va chạm dùng chung (`TownCollision`), logic đồng nhất client & server.
  - [x] Di chuyển được server xác nhận qua `SendMovement` và `MovementAck`.
  - [x] Tốc độ chéo được chuẩn hóa, quãng đường 1 giây đạt 73 px/s.
  - [x] Server kẹp delta-time, không tin delta-time hay tọa độ tự xưng của client.
  - [x] Input hết TTL (400 ms) tự động dừng bước.
  - [x] Chặn sequence cũ, chống giật lùi vị trí.
  - [x] Đồng đội nhận vị trí hợp lệ, cách ly hoàn toàn với các đội khác.
  - [x] Unit test, Integration test và Chrome CDP test đạt 100%.
  - [x] Single-Player và các Phase 01–12 được bảo toàn nguyên vẹn.

