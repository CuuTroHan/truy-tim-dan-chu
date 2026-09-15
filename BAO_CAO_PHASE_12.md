# BÁO CÁO NGHIỆM THU PHASE 12: BẢN ĐỒ RIÊNG THEO ĐỘI

---

## 1. MỤC TIÊU VÀ PHẠM VI NGHIỆM THU
- Triển khai kiến trúc phiên bản đồ riêng biệt theo từng đội thi đấu (`TeamGameInstance`, `TeamGameStateSnapshot`), bảo đảm tính cách ly dữ liệu thời gian thực.
- Đồng đội cùng đội (A và B thuộc Đội Đỏ) nhìn thấy nhau đứng tại điểm xuất phát (spawn), camera bám theo người chơi cá nhân của từng client.
- Người chơi khác đội (Player C thuộc Đội Xanh) hoàn toàn không nhìn thấy vị trí hay sự hiện diện của Đội Đỏ trên bản đồ của mình và ngược lại.
- Cách ly mạng toàn diện:
  - Máy chủ chỉ gửi dữ liệu vị trí, thành viên và trạng thái qua SignalR group riêng `team_{teamId}`.
  - Tuyệt đối không rò rỉ tọa độ, danh sách người chơi hay tiến độ nhiệm vụ chi tiết sang SignalR group chung của phòng `room_{roomId}`.
- Canvas render nhân vật:
  - Vẽ nhân vật người chơi và đồng đội bằng sprite Pipoya tương ứng với `AvatarId` đã chọn.
  - Phân biệt người chơi, đồng đội và NPC: đồng đội mang huy hiệu tên kèm viền màu của đội (`accentColor`), không bị nhầm lẫn với NPC (không hiện dấu chấm than tương tác `drawBang`).
- Bảo toàn tuyệt đối chế độ chơi đơn (Single-Player), `GameSmoke` đạt 100% PASS.

---

## 2. KIẾN TRÚC VÀ CÁC THAY ĐỔI TRIỂN KHAI

### 2.1. Shared Layer (`Shared/MatchModels.cs`, `Shared/Game/TownCollision.cs`, `Shared/Game/MultiplayerSession.cs`)
- **`Shared/MatchModels.cs`**:
  - `TeamMemberState`: DTO đại diện cho một thành viên trong trận (`PlayerId`, `DisplayName`, `AvatarId`, `AccentColor`, `X`, `Y`, `Facing`, `Walking`, `IsConnected`).
  - `TeamProgressSnapshot`: DTO tiến độ nhiệm vụ độc lập của từng đội (`Chapter`, `Spoken`, `Lamps`, `Clues`, `Version`, `Shards`).
  - `TeamGameStateSnapshot` & `MatchSnapshot`: Snapshot tổng thể của một đội, bao gồm danh sách thành viên và tiến độ nhiệm vụ cùng `ServerTimeUtc`.
  - `GetTeamStateRequest` & `GetTeamStateResponse`: Yêu cầu lấy snapshot trạng thái đội cho một người chơi.
- **`Shared/Game/TownCollision.cs`**:
  - Tách logic va chạm hộp giới hạn (AABB) và vật lý di chuyển của thị trấn Minh Đăng thành module dùng chung giữa máy chủ và máy khách.
  - Tốc độ di chuyển chuẩn 73 px/s, chuẩn hóa đường chéo `0.7071068f`, giới hạn biên `(16, 16)` đến `(1008, 624)`.
- **`Shared/Game/MultiplayerSession.cs`**:
  - Thực thi giao diện `IGameSession`, đại diện cho phiên chơi nhiều người ở máy khách.
  - Tự động đồng bộ vị trí spawn, camera cá nhân theo người chơi (`X - 240`, `Y - 135`), và danh sách diễn viên gồm các NPC thị trấn + đồng đội trong đội (`kind = "teammate"`).

### 2.2. Server Layer (`Server/TeamGameInstance.cs`, `Server/RoomInstance.cs`, `Server/RoomHub.cs`, `Server/ServerBootstrap.cs`)
- **`TeamGameInstance`**:
  - Quản lý trạng thái thi đấu riêng của một đội trong một trận đấu (`MatchId`, `TeamId`, `TeamName`, `TeamColor`, `Progress`, danh sách `TeamMemberSession`).
  - Cung cấp phương thức nguyên tử `GetSnapshot()` trả về `TeamGameStateSnapshot`.
- **`RoomInstance`**:
  - Khi trận đấu chuyển sang `RoomStatus.Playing`, gọi `InitializeTeamGames(matchId)` tạo instance riêng cho mỗi đội và phân bổ tọa độ xuất phát quanh `(500, 366)`.
  - Cung cấp `GetTeamGame(teamId)` và `GetTeamGameForPlayer(playerId)`.
  - Giữ `RoomSnapshot` công khai ở mức phòng (chỉ chứa metadata, không chứa tọa độ người chơi hay chi tiết nhiệm vụ).
- **`RoomHub` & `ServerBootstrap`**:
  - Hub method `GetMyTeamState`: Xác thực caller, trả về snapshot đúng của đội người chơi, tự động đưa connection vào SignalR group `team_{teamId}`.
  - `ServerBootstrap`: Khi `OnMatchStarted` kích hoạt, gửi `MatchStarted` tới `room_{roomId}` và gửi snapshot riêng `TeamStateUpdated` trực tiếp vào từng `team_{teamId}`.

### 2.3. Client & UI Layer (`Services/MultiplayerConnection.cs`, `Pages/Online.razor`, `Pages/MultiplayerGameView.razor`, `wwwroot/js/game.js`)
- **`MultiplayerConnection`**:
  - Lắng nghe sự kiện `TeamStateUpdated`.
  - Cung cấp phương thức `GetMyTeamStateAsync`.
- **`Pages/Online.razor`**:
  - Khi phòng chuyển sang trạng thái `Playing`, chuyển đổi giao diện từ `Lobby` sang component `MultiplayerGameView`.
- **`Pages/MultiplayerGameView.razor`**:
  - Kết nối Canvas engine và Blazor UI qua JSInterop.
  - Hiển thị HUD thời gian thực: Brand mark, Huy hiệu Đội (tên đội, màu sắc, danh sách thành viên), Bảng nhiệm vụ (Quest Card), Sổ Tiếng Nói, Hộp thoại và Bảng tương tác.
- **`wwwroot/js/game.js`**:
  - Cập nhật hàm vẽ `draw`: Hỗ trợ vẽ sprite Pipoya và bảng tên riêng cho đồng đội (`isTeammate = a.kind === "teammate"`) kèm viền màu đội.
  - Ngăn chặn hiển thị dấu chấm than (`drawBang`) trên đầu người chơi/đồng đội để tránh nhầm lẫn với NPC.

---

## 3. KẾT QUẢ KIỂM THỬ TỰ ĐỘNG

### 3.1. Unit Tests (`Tests/Multiplayer.UnitTests/TeamIsolationTests.cs`)
- Đã bổ sung 4 test cases kiểm tra cách ly:
  1. `TeamGameInstance_Isolation_MembersContainedOnlyInTheirOwnTeam`: Kiểm tra snapshot của Đội Đỏ chỉ chứa thành viên Đội Đỏ, Đội Xanh chỉ chứa thành viên Đội Xanh.
  2. `RoomSnapshot_DoesNotLeakPlayerPositionsOrQuestProgress`: Xác nhận snapshot công khai của phòng không rò rỉ tọa độ người chơi hay tiến độ nhiệm vụ.
  3. `MultiplayerSession_CameraFollowsLocalPlayer_AndTeammatesAreNotNpcs`: Kiểm tra camera bám theo đúng tọa độ người chơi địa phương và đồng đội mang loại `teammate`, không nằm trong danh sách `Objects`.
  4. `IndependentTeamInstances_HaveSameMapBounds_ButIndependentStates`: Xác nhận hai đội có cùng dữ liệu bản đồ nhưng đột biến tiến độ của đội này không ảnh hưởng đến đội kia.
- **Kết quả Unit Tests:** **117/117 PASSED (100%)**.

### 3.2. Integration Tests (`Tests/Multiplayer.IntegrationTests/TeamIsolationIntegrationTests.cs`)
- Đã bổ sung kịch bản kiểm thử tích hợp SignalR toàn diện:
  - Máy chủ thật trên port ngẫu nhiên.
  - Admin tạo phòng với Đội Đỏ và Đội Xanh.
  - Player A và Player B vào Đội Đỏ; Player C vào Đội Xanh.
  - Khởi động trận đấu và thu thập gói tin SignalR của cả 3 client.
  - **Kết quả:**
    - Player A & B nhận `TeamStateUpdated` chỉ chứa thành viên Đội Đỏ.
    - Player C nhận `TeamStateUpdated` chỉ chứa thành viên Đội Xanh.
    - `GetMyTeamState` trả về dữ liệu cách ly tuyệt đối cho từng người chơi.
- **Kết quả Integration Tests:** **17/17 PASSED (100%)**.

### 3.3. Hồi quy Single-Player (`GameSmoke.csproj`)
- Chế độ chơi đơn giữ nguyên vẹn 100%: **PASS**.

---

## 4. BẰNG CHỨNG KIỂM THỬ GIAO DIỆN VÀ MẠNG (CHROME CDP)

Kịch bản kiểm thử đa phiên thực tế trên Chrome CDP (`scratch/verify_phase12.mjs`) với 4 client độc lập:
1. **Admin H:** Tạo phòng `DC-JCJ5`, cấu hình Đội Đỏ và Đội Xanh.
2. **Player A (Đội Đỏ, avatar Bảo):** Vào Đội Đỏ, sẵn sàng.
3. **Player B (Đội Đỏ, avatar Dũng):** Vào Đội Đỏ, sẵn sàng.
4. **Player C (Đội Xanh, avatar Phương):** Vào Đội Xanh, sẵn sàng.
5. **Admin H:** Bắt đầu trận đấu (3s countdown) -> Tất cả chuyển sang `MultiplayerGameView`.

### Bằng chứng hình ảnh được ghi nhận:
1. `p12_01_team_red_player_a.png`:
   - Giao diện Player A trong trận đấu.
   - Camera căn giữa Player A.
   - Player A và đồng đội Player B đứng tại điểm xuất phát spawn quanh `(500, 366)` với avatar Pipoya chính xác và viền tên màu Đội Đỏ.
   - HUD trên cùng hiển thị: `ĐỘI ĐỎ`, `👤 Player A (Bạn)`, `👥 Player B`.
2. `p12_02_team_red_player_b.png`:
   - Giao diện Player B trong trận đấu.
   - Camera căn giữa Player B, nhìn thấy đồng đội Player A.
   - HUD trên cùng hiển thị: `ĐỘI ĐỎ`, `👤 Player B (Bạn)`, `👥 Player A`.
3. `p12_03_team_blue_player_c.png`:
   - Giao diện Player C trong trận đấu.
   - Player C đứng một mình tại điểm xuất phát của Đội Xanh.
   - **Tuyệt đối không có sự xuất hiện của Player A và Player B.**
   - HUD trên cùng hiển thị: `ĐỘI XANH`, `👤 Player C (Bạn)`.

### Xác minh cách ly mạng:
- Phân tích toàn bộ payload WebSocket của Player C trong suốt quá trình chơi.
- **Kết quả:** Không có bất kỳ frame `TeamStateUpdated` hay tọa độ nào của Đội Đỏ được truyền tới kết nối của Player C.

---

## 5. KẾT LUẬN VÀ NGHIỆM THU
- Phase 12 đã hoàn thành trọn vẹn, đáp ứng 100% các tiêu chí nghiệm thu trong đặc tả kỹ thuật:
  - [x] A/B nhìn thấy nhau đứng tại spawn, C chỉ thấy đội mình.
  - [x] Camera đi theo người chơi cục bộ.
  - [x] Player và đồng đội không bị nhầm lẫn với NPC.
  - [x] Snapshot phòng công khai không chứa vị trí hoặc tiến độ nhiệm vụ chi tiết.
  - [x] Máy chủ không gửi dữ liệu đội khác qua mạng (cách ly từ tầng server, không chỉ ẩn trên canvas).
  - [x] Unit test, Integration test và Chrome CDP test đa phiên đạt 100%.
  - [x] Single-Player và các Phase 01–11 được bảo toàn nguyên vẹn.

