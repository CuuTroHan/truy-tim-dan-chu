# Kế hoạch code multiplayer theo từng chức năng nhỏ

Nguồn: `DAC_TA_MULTIPLAYER.md`, `KE_HOACH_MULTIPLAYER.md` và code hiện tại. Tài liệu này chia kế hoạch tổng quan thành **40 phase**. Mỗi phase hoàn thành một chức năng kiểm chứng được trước khi chuyển tiếp.

**Trạng thái cập nhật 15/09/2026:** Phase 01 đã triển khai, 5 unit test và 7 integration test pass; Chrome đã kiểm chứng kết nối/mất kết nối/thử lại và sửa lỗi thiếu stylesheet; còn vài bước hồi quy browser trong báo cáo trước khi đóng nghiệm thu. Xem [báo cáo Phase 01](BAO_CAO_PHASE_01.md). Phase 02–40 chưa triển khai; tên file/API/test của các phase đó vẫn là đề xuất.

## 1. Cách code và nghiệm thu mỗi phase

1. Đọc phase và code liên quan; xác nhận các phase phụ thuộc đã hoàn thành.
2. Code cả server/client/UI cần thiết để đầu ra chạy được, không chỉ tạo class rỗng.
3. Chạy test tự động của chức năng và test hồi quy phần bị ảnh hưởng.
4. Demo theo kịch bản thủ công; đối chiếu kết quả mong đợi.
5. Ghi file thay đổi, lệnh test, kết quả và lỗi còn lại. Chỉ đóng phase khi đủ điều kiện “Xong khi”.

Các phase nền tảng 01–02 có đầu ra kỹ thuật chạy được. Những phase còn lại đều có chức năng người dùng có thể thao tác. Một phase có thể gồm nhiều commit nhỏ nhưng không kéo theo code phase kế tiếp khi chưa nghiệm thu.

### Môi trường và cách test chung

- **H:** trình duyệt admin. **A/B:** người chơi đội Đỏ. **C:** người chơi đội Xanh. Dùng profile riêng để không dùng chung token ngoài ý muốn.
- **Unit:** kiểm tra luật bằng state cô lập, dùng clock giả tăng thời gian được; không phải chờ thật 90 giây để test TTL.
- **Integration:** server test chạy trên cổng local ngẫu nhiên và client SignalR thật. Dùng barrier để hai lệnh tới đồng thời; gọi tuần tự không chứng minh được xử lý tranh chấp.
- **Browser:** nhiều profile hoặc nhiều máy để kiểm tra UI/Canvas. Kiểm tra cách ly đội bằng payload thực nhận, không chỉ nhìn nhân vật bị ẩn.
- **Database:** SQLite tạm riêng cho mỗi test; đọc lại bằng kết nối mới để chứng minh đã ghi bền vững.
- Test sai quyền phải gọi API trực tiếp. Nút bị disable chưa chứng minh server an toàn.
- Mỗi phase build project bị tác động và chạy test liên quan. Nếu sửa engine, Home hoặc Canvas, chạy smoke single-player và mở thử game. Chạy toàn bộ suite ở các mốc tích hợp và trước phát hành.

### Lệnh chạy từ thư mục `truy-tim-dan-chu`

```bash
# Có sẵn: chạy trước khi sửa để ghi nhận đường cơ sở
dotnet build TruyTimDanChu.csproj -c Release
dotnet run --project Tests/GameSmoke.csproj -c Release

# Dự kiến tạo từ phase 01
dotnet build Server/TruyTimDanChu.Server.csproj -c Release
dotnet test Tests/Multiplayer.UnitTests/Multiplayer.UnitTests.csproj -c Release
dotnet test Tests/Multiplayer.IntegrationTests/Multiplayer.IntegrationTests.csproj -c Release

# Ví dụ lọc test một phase; đặt tên class tương ứng khi code
dotnet test Tests/Multiplayer.UnitTests/Multiplayer.UnitTests.csproj -c Release --filter FullyQualifiedName~CreateRoomTests

# Demo local: hai terminal, URL lấy từ output/config thực tế
dotnet run --project Server/TruyTimDanChu.Server.csproj
dotnet run --project TruyTimDanChu.csproj
```

Nếu tên project/test được thay đổi lúc triển khai, cập nhật lệnh thực tế vào báo cáo phase. Không coi test chưa tồn tại là đã pass.

## 2. Quy tắc làm căn cứ cho implementation/test

Các lựa chọn này là đề xuất kế thừa plan tổng quan cho chỗ đặc tả còn thiếu; ghi nhận lựa chọn cuối trước khi code chức năng liên quan.

- Thêm `Paused`; thời gian thi không tính khoảng pause. Menu cá nhân/ẩn tab không pause cả trận.
- Start cần ít nhất một đội, mỗi đội có người, mọi người tham gia thuộc đội, online và ready. Admin không chơi không tính vào điều kiện này.
- Chỉ xóa đội rỗng; capacity không thấp hơn số slot đang giữ, kể cả người offline trong TTL.
- Đổi cấu hình thi đấu/roster reset ready. Countdown khóa đội và avatar.
- Reconnect giữ chỗ 90 giây mặc định, cấu hình được. Sau TTL token cũ không khôi phục phiên; tham gia mới theo luật late join.
- Submit hợp lệ nhưng sai tính một lỗi. Gói trùng, thiếu điều kiện, thiếu quyền và payload hỏng không tính lỗi đáp án.
- Server chuyển chương sau khi xác nhận, không chờ mọi người đọc xong hội thoại. UI cũ không được ghi đè state đội mới.
- Đội chưa xong không có thời gian hoàn thành/hạng chiến thắng. Phân biệt hết giờ, bỏ cuộc, kết thúc sớm, hủy và gián đoạn.
- Trạng thái trận đang chơi ở bộ nhớ; restart server không khôi phục trận dở. Kết quả đã lưu phải còn.
- Mặc định tắt late join, AdminCanPlay và kết thúc khi có đội thắng đầu tiên đến khi triển khai phase tương ứng.

## 3. Các phase code

### Phase 01 — Kết nối được client với server

**Chức năng hoàn thành:** màn online báo kết nối thành công hoặc lỗi có thể thử lại.

- **Code:** tạo `Shared/TruyTimDanChu.Shared.csproj`, `Server/TruyTimDanChu.Server.csproj`, `RoomHub`, `Services/MultiplayerConnection.cs`; handshake protocol/content version. Tạo project unit/integration test. Loại Server/Shared/test con khỏi glob biên dịch của WASM và smoke test.
- **Phải kiểm tra:** client không tham chiếu server; URL Hub lấy từ config; handler được dispose, không đăng ký nhiều lần; CORS local chỉ cho origin cấu hình.
- **Test tự động — `ConnectionTests`:** handshake đúng trả version; version sai bị từ chối; Hub thực sự kết nối bằng client SignalR; build riêng server/client.
- **Test tay:** mở online → connected; tắt server → báo mất kết nối; bật lại và thử lại → connected; ra/vào màn ba lần không nhân đôi event.
- **Xong khi:** trạng thái dựa trên kết nối thật; game một người vẫn mở được khi server tắt.

### Phase 02 — Single-player chạy qua lớp phiên chơi

**Chức năng hoàn thành:** hành trình cũ chạy qua `SinglePlayerSession`, sẵn sàng thay nguồn state cho online.

- **Code:** thêm `IGameSession`, `SinglePlayerSession`; cập nhật `Pages/Home.razor`; tách `PlayerState`, `TeamProgress`, `LocalUiState` từ `GameEngine`. Giữ định dạng save hoặc bộ đọc tương thích; sửa tham chiếu smoke test nếu engine có thêm file.
- **Phải kiểm tra:** panel/hội thoại/tùy chọn cá nhân không nằm trong tiến độ đội; presenter/local save chỉ cho single-player; không chuyển luật nghiệp vụ sang Canvas.
- **Test tự động — `SinglePlayerSessionTests`:** chạy GameSmoke; kiểm tra save/load, tiến độ chương và đầy đủ chuỗi finale qua session.
- **Test tay:** chơi một đoạn → reload → tiếp tục đúng; mở menu, đọc hội thoại, dùng presenter; tắt server vẫn chạy.
- **Xong khi:** hành vi cũ giữ nguyên qua lớp session, không cần kết nối mạng cho single-player.

### Phase 03 — Admin tạo phòng

**Chức năng hoàn thành:** form tạo phòng trả mã và mở trang quản trị.

- **Code:** `Room`, `RoomOptions`, `RoomService.Create`, `CreateRoom.razor`; mã không trùng, admin session/token riêng; giới hạn đọc từ config server.
- **Phải kiểm tra:** tên/thời hạn/cấu hình hợp lệ; ConnectionId không dùng làm danh tính admin lâu dài; token quản trị không nằm trong snapshot công khai.
- **Test tự động — `CreateRoomTests`:** tạo thành công ở Lobby; ép trùng mã để kiểm tra sinh lại; input lỗi không tạo phòng; snapshot không chứa secret.
- **Test tay:** H tạo hai phòng → hai mã khác nhau; gửi cấu hình âm/quá giới hạn trực tiếp → server trả lỗi rõ.
- **Xong khi:** phòng tồn tại trong store thật và quyền quản trị gắn đúng người tạo.

### Phase 04 — Vào phòng bằng mã

**Chức năng hoàn thành:** người chơi xuất hiện trong danh sách chờ phân đội.

- **Code:** `JoinRoom`, `PlayerSession`, `JoinRoom.razor`, `Lobby.razor`; chuẩn hóa tên/mã; snapshot có version; cấp reconnect token riêng, server chỉ giữ hash.
- **Phải kiểm tra:** tên trùng sau chuẩn hóa, mã sai, phòng không cho join, vượt tổng người; không tin PlayerId do client tự khai.
- **Test tự động — `JoinRoomTests`:** join hợp lệ; hai yêu cầu cùng tên đồng thời chỉ một thành công; sai mã/đầy phòng bị từ chối; không lộ token người khác.
- **Test tay:** A nhập mã H → H thấy A; B nhập cùng tên → báo trùng; đổi tên → tất cả thấy danh sách thống nhất.
- **Xong khi:** join hoạt động bằng server thật và event không gửi sang phòng khác.

### Phase 05 — Thêm đội với sức chứa riêng

**Chức năng hoàn thành:** admin thêm đội, mọi lobby cập nhật ngay.

- **Code:** `Team`, `AdminAddTeam`, `TeamEditor.razor`; kiểm tra MaxTeamsPerRoom/MaxPlayersPerTeam/tổng capacity trong cùng thao tác nguyên tử.
- **Phải kiểm tra:** không hardcode số đội; chỉ admin đúng phòng được thêm khi Lobby; tên/màu/capacity hợp lệ.
- **Test tự động — `AddTeamTests`:** capacity khác nhau hợp lệ; vượt từng giới hạn bị từ chối; hai lệnh thêm đồng thời không vượt tổng capacity.
- **Test tay:** H thêm Đỏ 3, Xanh 5 → A/B thấy đúng; A gọi API thêm đội → forbidden.
- **Xong khi:** thêm đội từ UI được, các giới hạn được bảo vệ tại server.

### Phase 06 — Chỉnh sửa danh sách đội

**Chức năng hoàn thành:** admin đổi tên/màu/capacity, sắp xếp hoặc xóa đội rỗng.

- **Code:** `AdminUpdateTeam`, `AdminReorderTeams`, `AdminRemoveTeam`; tăng version sau mutation, cập nhật UI theo state server.
- **Phải kiểm tra:** capacity không dưới occupancy; chỉ xóa đội rỗng; reorder đúng tập ID thuộc phòng, không trùng/thiếu; mutation nguyên tử.
- **Test tự động — `EditTeamTests`:** sửa/reorder/xóa hợp lệ; fixture có người để test giảm capacity/xóa bị từ chối; ID phòng khác và vượt tổng capacity thất bại.
- **Test tay:** H đổi màu, đưa Xanh lên đầu, xóa đội rỗng → A/B giống H; sau phase 07 kiểm tra lại xóa đội có người bị chặn.
- **Xong khi:** không lệch thứ tự client và không tạo thành viên mồ côi.

### Phase 07 — Tự chọn hoặc rời đội

**Chức năng hoàn thành:** người chơi vào đội còn chỗ, đổi đội hoặc về trạng thái chưa phân đội.

- **Code:** `JoinTeam`, `LeaveTeam`, `TeamList.razor`; cập nhật Player.TeamId/occupancy/SignalR group trong cùng luồng; kiểm tra đích trước khi rời đội cũ.
- **Phải kiểm tra:** self-selection bật; một người chỉ ở một đội; đổi sang đội đầy không làm mất đội cũ; gỡ group cũ.
- **Test tự động — `TeamSelectionTests`:** A/B tranh slot cuối bằng barrier chỉ một thành công; join lặp không tăng occupancy; team ngoài phòng bị từ chối; leave trả đúng slot.
- **Test tay:** Đỏ capacity=1, A vào → B không vào được; A rời → B vào được; H thấy đúng số lượng.
- **Xong khi:** state người/đội và group membership nhất quán.

### Phase 08 — Admin phân đội và loại người chơi

**Chức năng hoàn thành:** admin quản lý roster khi người chơi không được tự chọn đội.

- **Code:** `AdminMovePlayer`, `AdminKickPlayer`, tùy chọn self-selection và UI chờ phân đội; kick thu hồi phiên/connection.
- **Phải kiểm tra:** chuyển vào đội đầy thất bại nguyên tử; không sửa người phòng khác; token người bị kick không resume được phiên cũ.
- **Test tự động — `RosterAdminTests`:** move đúng, đội đầy, giả quyền, kick rồi gửi lệnh/resume; mọi kết quả đúng quyền và slot.
- **Test tay:** tắt tự chọn → A chờ H; H đưa A vào Đỏ → A thấy đội; kick A → A về màn join, không còn nhận event phòng.
- **Xong khi:** quyền bị thu hồi ở server, không chỉ ẩn người khỏi UI.

### Phase 09 — Chọn ngoại hình

**Chức năng hoàn thành:** avatar có preview bốn hướng và được lưu trên player.

- **Code:** `AvatarCatalog`, `SelectAvatar`, `AvatarPicker.razor`, helper vẽ Pipoya; màu đội/dấu phân biệt người trùng sprite.
- **Phải kiểm tra:** chỉ ID whitelist, không nhận URL asset tùy ý; ngoại hình không đổi tốc độ/va chạm; lựa chọn nằm trong snapshot.
- **Test tự động — `AvatarTests`:** ID/màu hợp lệ được lưu; ID lạ bị từ chối; hai người cùng sprite có ký hiệu riêng ổn định.
- **Test tay:** A/B chọn cùng mẫu → có dấu khác; preview đủ bốn hướng, không cắt sprite; kiểm tra khóa avatar trong Countdown ở phase 11.
- **Xong khi:** avatar được đồng bộ thật và preview dùng asset hiện có.

### Phase 10 — Ready và khóa lobby

**Chức năng hoàn thành:** lobby thể hiện ai sẵn sàng, admin khóa tham gia/đội hình được.

- **Code:** `SetReady`, `AdminSetJoinLock`, `AdminSetRosterLock`, validator lý do chưa thể start; cảnh báo đội lệch số người.
- **Phải kiểm tra:** chưa có đội không ready; sửa cấu hình thi đấu/roster reset ready; khóa join không loại người đang có; quyền khóa là admin.
- **Test tự động — `ReadinessTests`:** toggle/reset ready, join khi khóa bị từ chối, mutation khi khóa roster bị từ chối, giả quyền admin thất bại.
- **Test tay:** A/B ready → H đổi capacity → cả hai chưa ready; khóa phòng → C mới không vào được; mở lại → vào được.
- **Xong khi:** UI và server thống nhất ready/khóa và lý do chưa thể bắt đầu.

### Phase 11 — Countdown đồng bộ

**Chức năng hoàn thành:** mọi client chuyển vào trận tại cùng mốc server; admin hủy được countdown.

- **Code:** `Match`, `AdminStartMatch`, `AdminCancelCountdown`, scheduler dùng clock thay được trong test; MatchId/StartedAt; chặn sửa đội/avatar khi Countdown.
- **Phải kiểm tra:** đội rỗng, người offline/chưa ready/chưa phân đội chặn start; double click không tạo hai trận; hủy vô hiệu callback cũ.
- **Test tự động — `CountdownTests`:** clock trước/đúng mốc → Playing một lần; hủy rồi tăng clock không start; tất cả đội có cùng StartedAt; lệnh sửa bị chặn.
- **Test tay:** H start → A/B/C cùng đếm; hủy → về Lobby; start lại → cùng vào game; gọi API đổi avatar lúc đếm bị từ chối.
- **Xong khi:** client chỉ hiển thị timer, server quyết định start.

### Phase 12 — Bản đồ riêng theo đội

**Chức năng hoàn thành:** A/B nhìn thấy nhau đứng tại spawn, C không thấy A/B.

- **Code:** `TeamGameInstance`, `MatchSnapshot`, `MultiplayerSession`; Canvas vẽ danh sách player thay cho một Quang; dùng chung content version.
- **Phải kiểm tra:** camera theo người mình; player không bị nhầm NPC; room public snapshot không chứa vị trí/quest chi tiết.
- **Test tự động — `TeamIsolationTests`:** client thật A/B/C thu toàn bộ snapshot/event; C không có tọa độ A/B; các instance có cùng dữ liệu bản đồ nhưng state khác nhau.
- **Test tay:** start hai đội → A/B thấy đúng tên/avatar nhau, C chỉ thấy đội mình; đọc payload để xác minh cách ly.
- **Xong khi:** server không gửi dữ liệu đội khác, không chỉ Canvas ẩn dữ liệu đó.

### Phase 13 — Di chuyển được server xác nhận

**Chức năng hoàn thành:** người chơi di chuyển, đồng đội nhận vị trí hợp lệ.

- **Code:** tách dữ liệu va chạm dùng chung; `SendMovement(input, sequence)`, server simulation, gửi mạng 10–15 Hz, trả ACK sequence; phase này có thể vẽ trực tiếp snapshot.
- **Phải kiểm tra:** tốc độ chéo được chuẩn hóa; server không tin tọa độ/delta-time client; input hết TTL phải dừng; chặn sequence cũ, dữ liệu NaN/vô hạn và spam.
- **Test tự động — `MovementTests`:** một giây cho quãng đường đúng sai số số học; tường/biên chặn; gửi gấp 10 lần không tăng tốc; gói cũ không kéo lùi.
- **Test tay:** A đi bốn hướng, B thấy; A đâm tường không xuyên; ẩn tab/ngừng input → dừng sau TTL cấu hình.
- **Xong khi:** server nắm vị trí thật, không có API cho client tự teleport.

### Phase 14 — Chuyển động mượt

**Chức năng hoàn thành:** người mình phản hồi ngay, đồng đội được nội suy giữa snapshot.

- **Code:** local prediction, input buffer chưa ACK, reconciliation, interpolation theo server timestamp; giới hạn buffer, clear khi đổi match/resume; cấu hình ngưỡng sửa mềm/snap.
- **Phải kiểm tra:** render không gắn tần suất mạng; không replay input đã ACK; packet đảo thứ tự không kéo lùi; buffer không tăng vô hạn.
- **Test tự động — `MovementSyncTests`:** snapshot trễ/đảo/lặp, ACK một phần; vị trí hội tụ đúng server, input được loại đúng và buffer có giới hạn.
- **Test tay:** giả lập RTT 100 ms và jitter; A chạy quanh vật cản 2 phút, B quan sát/ghi video; trả mạng bình thường → hội tụ, không rung kéo dài.
- **Xong khi:** di chuyển mượt và mọi sai lệch prediction được sửa, không ảnh hưởng cách ly đội.

### Phase 15 — Gặp NPC tính chung cho đội

**Chức năng hoàn thành:** A gặp NPC, B nhận cờ tiến độ nhưng đọc hội thoại riêng.

- **Code:** `Interact(objectId)` kiểm tra vị trí/chương; Opening → Lights một lần; Spoken flags nguyên tử, TeamStateVersion; CommandId chống xử lý lặp cho lệnh nghiệp vụ từ đây.
- **Phải kiểm tra:** không tương tác từ xa/ID lạ/chương sai; callback hội thoại cũ không ghi đè state mới; bốn người có thể gặp bốn NPC độc lập.
- **Test tự động — `NpcProgressTests`:** A/B gặp cùng NPC chỉ một mutation; gặp hai NPC đồng thời giữ cả hai cờ; C không đổi; client bỏ snapshot cũ.
- **Test tay:** A gặp Phương, B gặp Dũng → cả hai có hai cờ; B đọc hội thoại, A vẫn di chuyển; gọi tương tác từ xa bị chặn.
- **Xong khi:** mở đầu và thông tin NPC được cộng tác mà không khóa UI cả đội.

### Phase 16 — Thu thập vật phẩm cho cả đội

**Chức năng hoàn thành:** một người kích hoạt đèn, cả đội nhận tiến độ đúng một lần.

- **Code:** handler thu thập đèn, tiền đề theo engine hiện tại; cập nhật nguyên tử và Done/version cho render, tái sử dụng hạ tầng phase 15.
- **Phải kiểm tra:** hai người không ghi đè cờ nhau; lấy lại vật đã xong trả kết quả nhất quán; mutation thật mới tăng version/phát tiến độ.
- **Test tự động — `CollectionTests`:** dùng barrier cùng lấy một đèn → một mutation; lấy hai đèn khác nhau → giữ cả hai; team khác không đổi; replay không nhân đôi.
- **Test tay:** A lấy đèn → B thấy Done; B lấy lại không tăng tiến độ; C vẫn còn đèn riêng.
- **Xong khi:** có lát cắt hoàn chỉnh di chuyển → tương tác → tiến độ chung độc lập giữa đội.

### Phase 17 — Giữ quyền giải câu đố

**Chức năng hoàn thành:** một câu đố trong đội chỉ có một người giữ quyền submit.

- **Code:** `PuzzleReservation`, `ReservationService`, `ReleasePuzzle`, token khóa/TTL; UI báo người đang giải; giải phóng khi đóng, disconnect hoặc hết hạn. Đề xuất reset bản nháp khi chủ khóa rời.
- **Phải kiểm tra:** khóa theo MatchId+TeamId+PuzzleId; token chủ cũ vô hiệu; hai đội mở cùng câu đố không chặn nhau.
- **Test tự động — `ReservationTests`:** mở đồng thời một owner; clock vượt TTL để B lấy khóa, A submit token cũ bị từ chối; không release khóa người khác.
- **Test tay:** A mở Gương → B thấy A đang giải; A đóng → B mở được; A mất mạng → khóa được trả khi server nhận disconnect hoặc hết TTL.
- **Xong khi:** quyền submit do server kiểm tra, không dựa vào panel client.

### Phase 18 — Giải Gương, nhận mảnh 1

**Chức năng hoàn thành:** Gương đúng mở Draft cho cả đội.

- **Code:** validator Gương server, schema đáp án; đủ NPC/đèn, đúng chương/chủ khóa; đếm lỗi hợp lệ, chuyển chương/timing/mảnh một lần, giải phóng khóa.
- **Phải kiểm tra:** không tin Shards/Chapter của client; xoay bản nháp không tự đổi progress; hội thoại không trì hoãn state đội.
- **Test tự động — `MirrorsTests`:** sai +1 lỗi, replay cùng CommandId không +2; đúng thiếu tiền đề bị chặn; đúng đủ điều kiện nhận một mảnh và một timing.
- **Test tay:** A gửi sai rồi đúng → A/B sang Draft dù B chưa đóng hội thoại; C giữ state riêng.
- **Xong khi:** trọn chương Lights hoàn thành bằng luật server.

### Phase 19 — Giải Dự thảo, nhận mảnh 2

**Chức năng hoàn thành:** sắp xếp quy trình đúng mở River.

- **Code:** validator Draft; UI submit danh sách ID bước qua session/reservation; kiểm tra đủ phần tử, không trùng/ngoài danh sách.
- **Phải kiểm tra:** đúng thứ tự nhưng sai chương/thiếu khóa không được nhận; phân biệt payload hỏng với đáp án sai hợp lệ.
- **Test tự động — `DraftTests`:** đúng/sai, thiếu/trùng ID, thiếu khóa, replay; sai hợp lệ +1 lỗi; malformed không +1; chuyển chương một lần.
- **Test tay:** B giữ khóa, xếp sai submit → vẫn Draft; sửa đúng → A/B có hai mảnh và mục tiêu River.
- **Xong khi:** chương Draft chạy online từ đầu tới cuối, giữ nội dung cũ.

### Phase 20 — Giải Dòng sông, nhận mảnh 3

**Chức năng hoàn thành:** chỉnh đúng bốn trạm mở News.

- **Code:** validator River, submit bốn lựa chọn/range, gợi ý theo nội dung cũ; kiểm tra chương/chủ khóa và timing.
- **Phải kiểm tra:** thiếu/thừa trạm không được chấm như đáp án hợp lệ; submit cạnh tranh không tạo hai timing; client RiverDone không có quyền quyết định.
- **Test tự động — `RiverTests`:** sai một trạm không qua, đúng chuyển News; dữ liệu biên bị chặn, replay không tăng lỗi/mảnh; hoàn thành rồi không làm lại.
- **Test tay:** A chọn sai một biển → gợi ý; sửa đúng → A/B cùng ba mảnh; B submit River đã xong bị từ chối.
- **Xong khi:** chương River hoàn chỉnh và không thể nhận thưởng lặp.

### Phase 21 — Thu thập manh mối cộng tác

**Chức năng hoàn thành:** các thành viên lấy những manh mối khác nhau đồng thời; sổ đội cập nhật.

- **Code:** clue object, NPC cung cấp clue, LoreFlags và sổ tài liệu theo luật hiện tại; chia sẻ tiến độ nhưng hội thoại/panel riêng.
- **Phải kiểm tra:** thêm clue không mất cờ trước; điều kiện xuất hiện đúng; đọc Lore không tự trao mảnh hoặc chuyển chương.
- **Test tự động — `ClueTests`:** ba command đồng thời giữ đủ ba cờ; cùng clue idempotent; Lore không thay completion; team khác không nhận chi tiết.
- **Test tay:** A lấy biên lai, B lấy bảng và gặp Bảo → cả hai đủ ba manh mối; A đọc tài liệu, B vẫn đi được.
- **Xong khi:** đội đủ dữ liệu giải News nhưng chưa tự chuyển chương.

### Phase 22 — Xác minh phản ánh, nhận mảnh 4

**Chức năng hoàn thành:** đúng lựa chọn News và đủ chứng cứ mở Finale.

- **Code:** validator News, reservation, submit UI; giữ thông điệp lựa chọn hiện tại; đồng bộ lỗi, mảnh và mục tiêu mới.
- **Phải kiểm tra:** thiếu clue không qua; lựa chọn sai hợp lệ tính lỗi theo quy tắc đã chốt; đủ bốn mảnh chưa đồng nghĩa thắng.
- **Test tự động — `NewsTests`:** đúng nhưng thiếu clue bị chặn; từng lựa chọn sai không mở Finale; đủ clue + đúng chuyển một lần và có bốn mảnh.
- **Test tay:** submit sớm → báo thiếu chứng cứ; thu đủ rồi chọn đúng → A/B quay về Hòm Dân chủ, chưa Finished.
- **Xong khi:** chương News hoàn chỉnh và không xác nhận thắng sớm.

### Phase 23 — Hoàn thành thử thách cuối

**Chức năng hoàn thành:** đội được server xác nhận xong sau cả xếp mảnh lẫn chuỗi trả thư.

- **Code:** finale state, thứ tự mảnh, ReturnStep, command từng bước có idempotency/chủ khóa; ghi Team.FinishedAt và chặn mutation đội đã xong.
- **Phải kiểm tra:** không có lệnh client tự đặt Complete; đủ mảnh chưa đủ thắng; mất chủ khóa xử lý bản nháp nhất quán, không bỏ bước.
- **Test tự động — `FinaleTests`:** xếp sai, bỏ/lặp bước, thiếu chương, submit đồng thời; chỉ chuỗi hợp lệ ghi FinishedAt, đúng một lần.
- **Test tay:** A làm xếp mảnh và từng bước trả thư → A/B thấy hoàn thành; C tiếp tục; lệnh quest mới của đội Đỏ bị từ chối.
- **Xong khi:** chơi hết hành trình online bằng thao tác thật, không presenter.

### Phase 24 — Đồng hồ và hết giờ

**Chức năng hoàn thành:** timer server hiển thị trên client; hết hạn tự chặn tiến độ mới.

- **Code:** `MatchClock`, deadline, timer HUD, scheduler timeout; elapsed dùng thời gian đơn điệu, UTC lưu mốc sự kiện; quy tắc phân xử finish/timeout.
- **Phải kiểm tra:** đề xuất command được chấp nhận trước deadline mới tính, tại/sau deadline bị chặn; ẩn tab/menu không pause; không tin FinishedAt client.
- **Test tự động — `MatchClockTests`:** clock trước/đúng/sau deadline với finale; không có input vẫn timeout; event kết thúc một lần; đội đã xong giữ thời gian.
- **Test tay:** phòng thời hạn ngắn, A mở menu B chơi → hết hạn cả hai dừng tương tác; không hiển thị đội chưa xong thành 00:00 hoàn thành.
- **Xong khi:** trận tự hết giờ dù không còn trình duyệt chủ động cập nhật.

### Phase 25 — Bảng xếp hạng cuối trận

**Chức năng hoàn thành:** kết quả xếp đúng thời gian, lỗi, chặng áp chót và đồng hạng.

- **Code:** `RankingService`, `Results.razor`, chapter timings/roster snapshot; kết thúc khi tất cả xong hoặc hết giờ.
- **Phải kiểm tra:** so sánh giá trị chính xác trước làm tròn; đội chưa xong không có hạng thắng; đề xuất đánh số đồng hạng 1,1,3; tên/thành viên lấy tại thời điểm thi.
- **Test tự động — `RankingTests`:** từng tie-break, đồng hạng hoàn toàn, thời gian hiển thị giống nhưng số thật khác, đội DNF; đảo thứ tự input vẫn cùng kết quả.
- **Test tay:** hai đội xong khác thời điểm → thứ tự đúng; một đội hết giờ → trạng thái rõ; đối chiếu thành viên và từng chặng.
- **Xong khi:** kết quả độc lập với thứ tự client nhận event và đồng hồ client.

### Phase 26 — Bảng tiến độ trực tiếp

**Chức năng hoàn thành:** người chơi thấy tiến độ công khai theo cấu hình admin.

- **Code:** public progress DTO riêng, `ShowLiveLeaderboard`, UI thu gọn; server lọc trường/quyền khi phát snapshot/event.
- **Phải kiểm tra:** public data chỉ gồm tên/màu/mảnh/trạng thái và trường được phép; không lộ tọa độ, objectId, câu đố đang mở của đối thủ.
- **Test tự động — `PublicProgressTests`:** bật/tắt quyền ở snapshot đầu và update; client C thu mọi event không có dữ liệu chi tiết A/B; bổ sung reconnect case sau phase 27.
- **Test tay:** thử phòng bật/tắt → dữ liệu hiển thị đúng; kiểm tra payload C không thấy quest cụ thể của Đỏ.
- **Xong khi:** tính năng hoạt động mà không phá cách ly đội bằng payload dư.

### Phase 27 — Người chơi reconnect đúng phiên

**Chức năng hoàn thành:** mất mạng ngắn rồi trở lại đúng người, đội, avatar, vị trí và tiến độ mới nhất.

- **Code:** resume token, rebind connection, vô hiệu connection cũ, rejoin group, full snapshot; clear input/interpolation cũ; không replay mù command đang chờ.
- **Phải kiểm tra:** token/TTL/thu hồi; không tạo thêm player; không khôi phục khóa đã mất; snapshot có MatchId/version/clock/state/reservation hiện tại.
- **Test tự động — `ReconnectTests`:** trước TTL đúng, sau TTL/sai token/kicked bị chặn; connection cũ mất quyền; B đổi chương lúc A offline → A nhận state mới.
- **Test tay:** A offline 10 giây, B làm nhiệm vụ, A online → vị trí cũ và progress mới; tab connection cũ không điều khiển song song.
- **Xong khi:** khôi phục nghiệp vụ đầy đủ, không chỉ socket connected.

### Phase 28 — Dọn phiên mất kết nối và phòng hết hạn

**Chức năng hoàn thành:** hết TTL trả slot; phòng bỏ không được dọn mà không mất tiến độ hợp lệ.

- **Code:** heartbeat/LastSeenAt, session cleanup, room inactivity TTL, trạng thái toàn đội offline; ghi rõ luật bỏ cuộc, không tự coi mất mạng ngắn là bỏ cuộc.
- **Phải kiểm tra:** không dọn phòng Playing chỉ vì admin offline; cleanup/reconnect cùng lúc xử lý nguyên tử; tiến độ đội còn dù nhân vật đã loại.
- **Test tự động — `CleanupTests`:** clock trước/sau TTL, slot giữ/trả đúng; race cleanup/resume không sinh hai player; phòng đang hoạt động không bị dọn nhầm.
- **Test tay:** TTL test ngắn, đóng A → tạm vắng rồi biến mất; B còn vật phẩm chung; Lobby bỏ không đóng đúng cấu hình.
- **Xong khi:** tài nguyên được dọn đúng và không phá phiên đang dùng.

### Phase 29 — Lưu kết quả và sự kiện vào SQLite

**Chức năng hoàn thành:** kết quả đã xong còn sau restart server.

- **Code:** schema/migration cho room/match/result/timing/roster/MatchEventLog; event đáp án/hoàn thành/quản trị có CommandId/timestamp; transaction và unique constraint chống trùng.
- **Phải kiểm tra:** không log token thô, không lưu mỗi frame; result và event hoàn thành nhất quán; ghi DB lỗi không được báo đã lưu giả; room dở được đánh dấu Interrupted sau restart.
- **Test tự động — `PersistenceTests`:** ghi/đọc kết nối mới; complete lặp chỉ một result; ép lỗi transaction không lưu nửa kết quả; restart giữ trận xong và đánh dấu trận dở.
- **Test tay:** hoàn thành → restart → đọc DB vẫn có kết quả; kiểm tra log không chứa token; trận dở không hồi sinh bằng state sai.
- **Xong khi:** dữ liệu bền vững được xác minh; không hứa resume trận dở sau restart.

### Phase 30 — Chơi lượt mới trong cùng phòng

**Chức năng hoàn thành:** Finished về Lobby, chơi lại từ đầu và giữ lịch sử cũ.

- **Code:** `AdminNewMatch`, reset ready/position/progress/reservation/clock; giữ config/roster hợp lệ; MatchId mới; client bỏ buffer và event lượt cũ.
- **Phải kiểm tra:** command MatchId cũ bị chặn; double click không tạo hai lượt; không reset kết quả đã lưu.
- **Test tự động — `RematchTests`:** ID mới/state sạch/config giữ nguyên/history còn; lệnh và snapshot lượt cũ không tác động lượt mới.
- **Test tay:** xong trận → lượt mới → mọi người ở lobby chưa ready; start → không có mảnh cũ; DB còn kết quả trận trước.
- **Xong khi:** chơi hai lượt liên tiếp không cần tạo phòng mới, không nhiễm state.

### Phase 31 — Admin pause/resume trận

**Chức năng hoàn thành:** pause dừng toàn trận và thời gian thi, resume đúng trạng thái.

- **Code:** `Paused`, `AdminPauseMatch`, `AdminResumeMatch`, paused duration và deadline mới; đề xuất release khóa/đóng puzzle khi pause, mở lại sau resume.
- **Phải kiểm tra:** chặn movement/quest khi pause; reconnect nhận Paused; menu cá nhân không gọi pause admin; thứ tự race pause/complete xác định tại server.
- **Test tự động — `PauseTests`:** chơi 10 giây, pause 30, chơi 5 → elapsed 15; lệnh khi pause bị chặn; pause/resume lặp không cộng sai, deadline dời đúng.
- **Test tay:** H pause lúc A giải → A/B/C dừng, timer đứng; resume → chơi lại, mở puzzle được; A mở menu riêng không dừng B.
- **Xong khi:** timer trận/chặng/kết quả đều loại trừ thời gian pause.

### Phase 32 — Kết thúc, hủy hoặc đóng phòng

**Chức năng hoàn thành:** admin chấm dứt phiên với lý do rõ, không để client mắc kẹt.

- **Code:** `AdminEndMatch`, `AdminCancelMatch`, `AdminCloseRoom`; transition hợp lệ từ các trạng thái; event kết thúc, hủy scheduler/thu hồi membership phù hợp.
- **Phải kiểm tra:** end giữ thành tích đã có, đội chưa xong ghi kết thúc sớm; cancel không công bố như trận thi hợp lệ; Closed chặn join/resume/mutation.
- **Test tự động — `EndRoomTests`:** transition đúng/sai, giả quyền, end lặp; callback countdown sau close không start; command queued không làm sống lại phòng.
- **Test tay:** H end khi C chưa xong → kết quả ghi rõ; lượt khác cancel → trận hủy; close → mọi client rời game, mã cũ bị từ chối.
- **Xong khi:** lý do kết thúc lưu/hiển thị đúng và không còn tác vụ nền thay đổi phòng đã đóng.

### Phase 33 — Admin reconnect quyền điều hành

**Chức năng hoàn thành:** admin reload/mất mạng rồi trở về dashboard đúng phòng.

- **Code:** resume admin session/token riêng player; rebind connection và snapshot quản trị; không tạo slot player nếu AdminCanPlay chưa bật.
- **Phải kiểm tra:** token admin không nằm trong link chia sẻ room code/public snapshot; token player không nâng quyền; mất admin không tự pause trận.
- **Test tự động — `AdminReconnectTests`:** token đúng phục hồi, connection cũ mất quyền; token sai/player bị chặn; Closed không phục hồi điều khiển.
- **Test tay:** H reload khi Playing → dashboard đúng; A/B vẫn chơi liên tục; profile chỉ biết room code không trở thành admin.
- **Xong khi:** quyền admin dựa vào phiên ổn định, không phụ thuộc ConnectionId cũ.

### Phase 34 — Tham gia muộn vào chỗ trống

**Chức năng hoàn thành:** người mới vào đội đang chơi khi cấu hình cho phép.

- **Code:** join flow Playing, chọn avatar trước khi vào, spawn hợp lệ theo chương server; slot thay thế chỉ có sau TTL/loại phiên; nhận full progress.
- **Phải kiểm tra:** mặc định tắt; không join đội xong/tạo đội giữa trận/chiếm slot còn TTL; avatar khóa sau vào; không đổi nội dung hoặc độ khó.
- **Test tự động — `LateJoinTests`:** bật/tắt, slot giữ/hết hạn, đội Finished, hai người tranh slot; spawn không nằm trong vật cản, snapshot đủ state.
- **Test tay:** phòng bật từ trước, A rời quá TTL → D vào slot, thấy progress hiện tại; thử phòng tắt → bị chặn.
- **Xong khi:** người thay thế chơi tiếp được, occupancy và tiến độ không reset.

### Phase 35 — Admin tham gia như người chơi

**Chức năng hoàn thành:** bật AdminCanPlay trước trận, admin vào đội và chơi theo cùng luật.

- **Code:** liên kết admin/player identity riêng; chuyển dashboard/game; dùng cùng capacity/ready/avatar và validator gameplay.
- **Phải kiểm tra:** không tính admin hai lần; không bật giữa trận; quyền điều hành không bỏ qua luật nhiệm vụ; tắt thì không chiếm slot.
- **Test tự động — `AdminCanPlayTests`:** bật/tắt Lobby, đội đầy bị chặn, cần ready khi có chơi; reconnect không tạo hai player/slot.
- **Test tay:** H bật, chọn Đỏ/ready/start → A thấy H; H mở dashboard rồi quay lại vẫn đúng nhân vật/vị trí.
- **Xong khi:** admin vừa điều hành vừa chơi được nhưng tuân thủ cùng luật.

### Phase 36 — Kết thúc khi có đội thắng đầu tiên

**Chức năng hoàn thành:** tùy chọn riêng tự kết thúc phòng sau đội thắng đầu tiên.

- **Code:** `EndOnFirstFinish` cấu hình trước trận; dùng chung finish/ranking và lý do kết thúc sớm; xử lý command theo thời điểm server chấp nhận.
- **Phải kiểm tra:** mặc định false; gần đồng thời có quy tắc xác định; đội còn lại không bị ghi tự bỏ cuộc; kết thúc một lần.
- **Test tự động — `FirstFinishTests`:** tắt C tiếp tục, bật C dừng; replay không phát kết thúc lại; thành tích đã được chấp nhận trước lúc đóng được giữ.
- **Test tay:** hai trận bật/tắt, Đỏ thắng → Xanh lần lượt dừng với lý do kết thúc sớm hoặc tiếp tục chơi.
- **Xong khi:** tùy chọn không thay đổi hành vi mặc định của game.

### Phase 37 — Xem lịch sử trận

**Chức năng hoàn thành:** admin xem danh sách và chi tiết các lượt từ SQLite.

- **Code:** API lịch sử có quyền, `MatchHistory.razor`, phân trang ổn định; chi tiết roster/timing/lỗi/trạng thái.
- **Phải kiểm tra:** ID trận phòng khác không đọc được; tên lấy snapshot lúc thi, không lấy đội mới đổi tên; phân biệt Completed/Cancelled/Interrupted.
- **Test tự động — `HistoryTests`:** nhiều trang không mất/lặp khi dữ liệu ổn định; sai quyền bị chặn; đổi tên không sửa lịch sử; restart đọc lại được.
- **Test tay:** hai lượt, đổi tên đội giữa lượt → mỗi kết quả có đúng tên thời điểm thi; mở chi tiết đối chiếu bảng cuối trận.
- **Xong khi:** lịch sử dùng dữ liệu thật đã lưu, không dựa vào bộ nhớ trình duyệt.

### Phase 38 — Xuất bảng kết quả CSV

**Chức năng hoàn thành:** admin tải CSV đọc được tên tiếng Việt và đủ cột kết quả.

- **Code:** endpoint có quyền; encoding/escaping dấu phẩy, quote, newline; cột rank/team/members/time/errors/chapter timings/status; xử lý ô do người dùng nhập có thể bị hiểu thành công thức.
- **Phải kiểm tra:** đội chưa xong để trống completion time, không 0; không xuất token/log riêng; nội dung khớp UI.
- **Test tự động — `CsvExportTests`:** tiếng Việt/phẩy/quote/newline/ký tự mở đầu công thức; parse lại đủ hàng/cột; sai quyền không tải được.
- **Test tay:** xuất lượt có “Đội Ánh Sáng”, mở ứng dụng bảng tính → dấu tiếng Việt đúng, số hàng/thời gian/đồng hạng khớp bảng kết quả.
- **Xong khi:** file dùng được mà không sửa thủ công dữ liệu hoặc định dạng CSV.

### Phase 39 — Lobby/HUD dùng được khi đông người

**Chức năng hoàn thành:** nhiều đội/thành viên vẫn thao tác được và không che vùng chơi.

- **Code:** cuộn/phân trang danh sách, giới hạn HUD, tên dài/thu gọn bảng; tránh re-render toàn Razor theo từng packet movement; dọn handler/buffer.
- **Phải kiểm tra:** không hardcode số đội; keyboard/focus dùng được; nút/game không bị overlay chắn; bộ nhớ không tăng mãi.
- **Test tự động:** fixture nhiều đội xác minh không mất người/chọn nhầm ID; test logic mới nếu có, không viết test chỉ lặp CSS. Load bằng nhiều client SignalR thật với tải movement/quest.
- **Test tay:** 1280×720, thử 4×5 rồi 10×2 trong giới hạn cấu hình, đội lệch người/tên dài; cuộn tới đội cuối và chơi. Đo p95 input → cập nhật đồng đội trong LAN, tách RTT/server/render, đối chiếu mục tiêu <150 ms; ghi CPU/bộ nhớ/băng thông và video.
- **Xong khi:** UI dùng được với danh sách dài, có số đo dưới tải mục tiêu; chưa đạt latency phải sửa hoặc ghi rõ chưa đạt trước phát hành.

### Phase 40 — Chạy buổi thi trên máy chủ mục tiêu

**Chức năng hoàn thành:** máy khác mở URL và chơi hết trận trên bản publish thật.

- **Code:** host client+SignalR cùng origin, HTTPS/WebSocket, SQLite volume, giới hạn/TTL config; README chạy/publish/vận hành; xử lý service worker/cache và version handshake cho client cũ.
- **Phải kiểm tra:** Hub không hardcode localhost; static-only chỉ đủ single-player; DB tồn tại qua restart/deploy; không đưa secret vào client publish; version lệch phải yêu cầu tải lại.
- **Test tự động:** toàn bộ unit/integration/GameSmoke; build/publish client/server; kiểm tra endpoint và version mismatch trên bản publish.
- **Test tay:** ít nhất hai máy qua mạng mục tiêu, H+A/B/C từ tạo phòng tới kết quả; reconnect/rematch và các chức năng admin đã triển khai; restart kiểm tra lịch sử; client cache cũ phải cập nhật đúng. Chạy single-player khi server online tắt.
- **Xong khi:** URL vận hành được, có biên bản 14 tiêu chí nghiệm thu đặc tả; không còn lỗi chặn hành trình. Phần chưa kiểm chứng không được ghi pass.

## 4. Thứ tự và các mốc demo

Mặc định thực hiện **01 → 40**. Với MVP, thực hiện **01–30 + 39–40**; các chức năng mở rộng 31–38 chưa có phải tắt/ẩn đúng phạm vi, không hiện nút giả. Khi test MVP chỉ chạy các thao tác mở rộng đã triển khai, nhưng vẫn phải nghiệm thu đủ 14 tiêu chí MVP trong đặc tả. Bản đầy đủ cần toàn bộ 40 phase.

| Mốc | Demo |
|---|---|
| Sau 04 | Admin tạo phòng, người khác vào bằng mã |
| Sau 10 | Đội động, avatar, ready và khóa lobby |
| Sau 14 | Thành viên cùng đội thấy nhau di chuyển mượt |
| Sau 16 | Một người lấy vật phẩm, cả đội nhận; đội khác độc lập |
| Sau 18 | Hoàn thành chương đầu tiên online |
| Sau 23 | Chơi online hết toàn bộ nội dung |
| Sau 30 | Đồng hồ, kết quả, reconnect, persistence và chơi lại |
| Sau 38 | Đủ điều hành mở rộng, lịch sử UI và CSV |
| Sau 40 | Nghiệm thu trên môi trường sử dụng |

Không coi 40 phase là 40 ngày. Các phase 02, 13, 14, 27 và 40 có rủi ro cao hơn; cập nhật ước lượng sau phase 02 và 16 theo thời gian thực tế. Kiểm thử nằm trong từng phase, không dồn tới cuối.

## 5. Mẫu giao việc và báo cáo

**Mẫu yêu cầu code:**

> Thực hiện Phase XX trong PHASE_CODE_MULTIPLAYER.md. Đọc code và kiểm tra phase phụ thuộc trước khi sửa. Hoàn thành chức năng, chạy test phase và hồi quy phần bị ảnh hưởng. Chưa code phase tiếp theo. Báo cáo file thay đổi, lệnh test/kết quả, cách demo và phần chưa đạt; không đánh dấu hoàn thành nếu còn tiêu chí chưa kiểm chứng.

**Mẫu ghi nhận sau phase:**

```text
Phase:
Trạng thái: Chưa làm / Đang làm / Chưa đạt / Hoàn thành
Chức năng đã chạy được:
File đã thay đổi:
Test tự động: lệnh thực tế + số pass/fail
Test thủ công: bước đã thực hiện + kết quả thực tế
Hồi quy đã kiểm tra:
Lỗi còn lại / phần chưa kiểm chứng:
Bằng chứng: log, ảnh/video hoặc dữ liệu đối chiếu
```

Các mô tả phase là kế hoạch và điều kiện nghiệm thu; kết quả thực tế được ghi riêng trong báo cáo từng phase, không suy luận toàn bộ tiêu chí đã pass chỉ từ mô tả.
