# Kế hoạch triển khai multiplayer — Truy tìm Dân chủ

Ngày lập: 14/09/2026. Nguồn: `DAC_TA_MULTIPLAYER.md` và mã nguồn hiện tại.
Đây là kế hoạch đề xuất; chưa triển khai hoặc chạy kiểm thử multiplayer.

Kế hoạch code chi tiết theo 40 chức năng nhỏ, kèm kiểm tra và cách test từng phase: [PHASE_CODE_MULTIPLAYER.md](PHASE_CODE_MULTIPLAYER.md). Dùng tài liệu đó khi giao việc triển khai; tài liệu này giữ vai trò tổng quan kiến trúc và phạm vi.

## 1. Mục tiêu và hướng triển khai

Giữ game một người chạy độc lập, bổ sung chế độ online dùng ASP.NET Core + SignalR, với máy chủ quyết định di chuyển hợp lệ, tiến độ, thời gian và kết quả. Tái sử dụng Canvas, bản đồ, nội dung và sprite hiện có.

Triển khai theo thứ tự: tách logic → phòng chờ → một lát cắt chơi chung → toàn bộ nhiệm vụ → kết quả và khôi phục kết nối → kiểm thử triển khai. Mốc thử nghiệm đầu tiên là hai trình duyệt cùng đội nhìn thấy nhau và cùng nhận một vật phẩm, trong khi đội khác không bị ảnh hưởng.

Giả định vận hành ban đầu: một tiến trình máy chủ, trạng thái trận đang chơi trong bộ nhớ, SQLite lưu cấu hình/kết quả/lịch sử. Chưa triển khai nhiều máy chủ hay phục hồi trận đang chơi sau khi máy chủ khởi động lại.

## 2. Những điểm cần xử lý từ code hiện tại

| Thành phần | Hiện trạng | Thay đổi dự kiến |
|---|---|---|
| `Game/GameEngine.cs` | Gộp vị trí, panel, hội thoại, câu đố, tiến độ và save | Tách trạng thái cá nhân khỏi tiến độ đội; đưa xác nhận nhiệm vụ lên server |
| `Pages/Home.razor` | Gọi trực tiếp engine, tự lưu và khôi phục game | Dùng lớp phiên chơi để hỗ trợ single-player và multiplayer |
| `wwwroot/js/game.js` | Vẽ Quang là người chơi duy nhất; gọi C# mỗi khung hình | Vẽ danh sách đồng đội, ngoại hình, tên và nội suy; gửi mạng theo nhịp riêng |
| Chuyển chương | Một số bước chờ callback kết thúc hội thoại | Online chuyển tiến độ tại server sau lệnh hợp lệ; hội thoại là trải nghiệm riêng |
| Thử thách cuối | Gồm xếp mảnh và các bước trả thư | Server phải kiểm tra cả chuỗi, không chỉ đủ bốn mảnh |
| Pause/ẩn tab | Client gọi `PauseClock()` | Online chỉ dừng input cá nhân; đồng hồ trận vẫn chạy trừ khi admin tạm dừng |
| Presenter/save | Có nhảy chương và tiến độ trong trình duyệt | Chỉ áp dụng single-player; server online không nhận các quyền này từ client |
| `Tests/GameSmoke.csproj` | Liên kết trực tiếp `GameEngine.cs` | Điều chỉnh tham chiếu nếu tách project; giữ kiểm thử hành trình một người |
| Project WASM | Tự quét file C# bên dưới thư mục project | Loại `Server/**`, `Shared/**` và project test mới khỏi glob khi thêm project con |

## 3. Quy tắc cần chốt trước khi lập trình

Các lựa chọn dưới đây là đề xuất bổ sung cho những chỗ đặc tả chưa đủ rõ, không phải yêu cầu đã được xác nhận.

| Vấn đề | Phương án đề xuất |
|---|---|
| Pause có API nhưng thiếu trạng thái | Bổ sung `Paused`, cho phép `Playing → Paused → Playing`; chặn di chuyển và nhiệm vụ khi pause |
| Thời gian khi pause | Thời gian thi thực tế = thời gian từ lúc bắt đầu trừ tổng thời gian pause; thời hạn trận và thời gian chặng dùng cùng cách tính |
| Điều kiện bắt đầu | Có ít nhất một đội, mỗi đội có người, mọi người thuộc đội và sẵn sàng; người giữ chỗ nhưng offline chặn bắt đầu cho tới khi quay lại hoặc bị loại |
| Sửa cấu hình sau ready | Reset ready khi đổi cấu hình thi đấu/đội hình; chỉ bắt đầu sau khi xác nhận lại |
| Xóa đội/giảm sức chứa | Chỉ xóa đội rỗng; không giảm sức chứa dưới số thành viên đang giữ chỗ |
| Tham gia muộn | Mặc định tắt; khi bật chỉ vào đội hiện có đang chơi và còn chỗ, không tạo đội giữa trận; khóa ngoại hình sau khi vào |
| Spawn người vào muộn | Dùng điểm spawn hợp lệ theo chương do server chọn, nhận snapshot tiến độ hiện tại |
| Reconnect | Giữ chỗ theo TTL cấu hình, ban đầu 90 giây; trong TTL không tính là slot trống; reconnect nhận lại đúng người và vị trí |
| Admin mất mạng | Trận tiếp tục; admin dùng thông tin quản trị riêng để kết nối lại, không dùng ConnectionId làm định danh/quyền lâu dài |
| Toàn đội mất mạng | Giữ tiến độ và để đồng hồ tiếp tục; chỉ đánh dấu bỏ cuộc theo chính sách rõ ràng hoặc hành động admin, không tự coi một lần mất mạng là bỏ cuộc |
| Hết giờ/admin kết thúc | Đội chưa xong không có thời gian hoàn thành; lưu lý do kết thúc, phân biệt hết giờ, bỏ cuộc và trận bị hủy |
| Đồng hạng | So sánh thời gian chính xác lưu trên server, số đáp án sai rồi thời gian chặng áp chót; hiển thị có làm tròn không đổi cách xếp hạng |
| Đội chưa hoàn thành | Hiển thị sau nhóm hoàn thành, không gán thứ hạng chiến thắng dựa trên tiến độ khi đặc tả chưa quy định |
| Đếm đáp án sai | Đếm mỗi lần gửi đáp án hợp lệ nhưng sai; không đếm gói lặp, thiếu quyền hoặc lệnh sai định dạng; áp dụng nhất quán cho mọi câu đố |
| Lượt chơi mới | Sinh MatchId mới; giữ cấu hình đội, reset ready/vị trí/tiến độ/khóa; không ghi đè kết quả lượt trước |
| Server khởi động lại | Giữ lịch sử đã lưu; đánh dấu lượt dở dang bị gián đoạn, không hứa khôi phục trận từ bộ nhớ |

## 4. Kiến trúc dự kiến

```text
Blazor + Canvas hiện tại
  ├─ SinglePlayerSession → GameEngine cục bộ + save cũ
  └─ MultiplayerSession → SignalR → ASP.NET Core Server
                                      ├─ RoomService / MatchService
                                      ├─ TeamGameInstance
                                      ├─ MovementSimulation / PuzzleValidator
                                      ├─ ReservationService / SessionService
                                      └─ SQLite: cấu hình, kết quả, event log

Shared: DTO, command/event, ID, trạng thái và dữ liệu bản đồ công khai
```

- `Shared/`: giao thức và dữ liệu công khai; không đưa đáp án/xác nhận chiến thắng online vào DTO gửi client. Single-player vẫn có logic cục bộ để chạy độc lập.
- `Server/`: Hub mỏng chỉ xác thực và chuyển lệnh; nghiệp vụ đặt trong service có thể kiểm thử không cần UI.
- `Services/`: `IGameSession`, hai implementation cho single-player/online, client SignalR và đồng bộ đồng hồ.
- `Pages/`: màn vào phòng, lobby, admin, kết quả; tái sử dụng màn game khi khả thi.
- `Components/Multiplayer/`: danh sách đội, thành viên, avatar picker, trạng thái kết nối và tiến độ.
- `Tests/`: giữ smoke test hiện có, bổ sung project test luật server và tích hợp SignalR.

Tách kênh phát `room:{id}` cho dữ liệu lobby/tiến độ công khai, `team:{id}` cho vị trí và nhiệm vụ, kênh admin cho điều hành. Server quyết định membership; lọc trên giao diện không đủ để bảo vệ dữ liệu đội.

## 5. Các giai đoạn thực hiện

### Giai đoạn 0 — Chốt hợp đồng và đường cơ sở (1–2 ngày)

- Chốt các quy tắc ở mục 3 và phạm vi MVP so với bản đầy đủ.
- Chạy build và smoke test hiện tại để ghi nhận đường cơ sở trước sửa code.
- Lập danh sách object/NPC, va chạm, điều kiện mở từng nhiệm vụ, toàn bộ bước finale và điểm chuyển chương.
- Chốt DTO, state machine, mã lỗi, giới hạn cấu hình và phiên bản nội dung.
- Bổ sung model `Match`, `PuzzleReservation`, `ChapterTiming`; thêm `RoomVersion`, `MatchId`, `ContentVersion`, trạng thái pause và danh tính admin ổn định.

**Hoàn thành khi:** có bảng luật nhiệm vụ và giao thức đủ để viết server/client độc lập; biết những test đang pass hoặc lỗi sẵn có.

### Giai đoạn 1 — Tách nền tảng, giữ single-player (2–3 ngày)

- Thêm project Server/Shared, cấu hình tham chiếu và loại trừ glob để WASM không biên dịch nhầm server.
- Tách `PlayerState`, `TeamProgress`, `LocalUiState`; panel/hội thoại không được khóa hành động của toàn đội.
- Tách dữ liệu va chạm/bản đồ khỏi render; dùng cùng phiên bản ở client và server.
- Đặt lớp phiên chơi giữa Razor và engine; giữ save hiện tại cùng chế độ presenter cho single-player.

**Hoàn thành khi:** single-player vẫn chơi hết hành trình, load save được và build độc lập không cần server.

### Giai đoạn 2 — Phòng chờ, đội động và avatar (3–4 ngày)

- Tạo/tham gia/đóng phòng, mã phòng duy nhất và khóa quản trị riêng; kiểm tra tên, token và quyền tại server.
- Thêm/sửa/xóa/sắp xếp đội; sức chứa riêng; kiểm tra tổng sức chứa trong cùng thao tác nguyên tử.
- Chọn đội hoặc admin phân đội, chuyển/loại người, khóa tham gia/đội hình, ready và reset ready.
- Avatar whitelist từ Pipoya, preview bốn hướng, màu đội và dấu phân biệt người trùng sprite.
- Đồng bộ lobby theo version; bổ sung lệnh còn thiếu trong đặc tả như sắp xếp đội, loại người, khóa phòng và hủy countdown.
- Countdown dùng cùng mốc server; hủy về Lobby theo state machine.

**Hoàn thành khi:** nhiều trình duyệt thấy cùng lobby; hai yêu cầu cùng tranh chỗ cuối chỉ một yêu cầu thành công; client thường không gọi được lệnh admin.

### Giai đoạn 3 — Lát cắt chơi chung đầu tiên (3–4 ngày)

- Tạo `TeamGameInstance` cho từng đội khi bắt đầu; mọi đội dùng cùng cấu hình nội dung.
- Client gửi input + sequence khoảng 10–15 lần/giây; server mô phỏng bằng thời gian server, kiểm tra tốc độ, va chạm và biên bản đồ.
- Độc lập nhịp render với nhịp mạng; dự đoán nhân vật mình, nội suy đồng đội, điều chỉnh theo snapshot server và sequence đã xử lý.
- Thêm thời hạn input: ngừng di chuyển nếu client không gửi tiếp; chặn phát lại input cũ và lượng input vượt mức.
- Vẽ nhiều nhân vật, tên, màu và dấu nhận diện; chỉ gửi vị trí trong đúng đội.
- Triển khai một tương tác thu thập dùng chung, kiểm tra khoảng cách và đảm bảo chỉ tính một lần.

**Hoàn thành khi:** hai người cùng đội thấy nhau, cùng nhận tiến độ vật phẩm; đội thứ hai độc lập; không đi xuyên vật cản bằng lệnh giả.

### Giai đoạn 4 — Toàn bộ nhiệm vụ và tranh chấp (4–5 ngày)

- Chuyển luật Opening → Lights → Draft → River → News → Finale → Complete sang xử lý server cho online.
- Đồng bộ NPC, đèn, manh mối, tài liệu, bốn mảnh và thời gian chặng; hỗ trợ nhiều người thu thập đồng thời.
- Server xác nhận tiến độ ngay theo luật; client tự đọc/đóng hội thoại và nhận mục tiêu mới mà không ghi đè state đội bằng state cũ.
- Khóa câu đố theo `(MatchId, TeamId, PuzzleId)` và TTL, có reservation token để từ chối đáp án của chủ khóa cũ.
- Bổ sung `ReleasePuzzle`; giải phóng khi đóng panel, hết hạn hoặc mất kết nối; xác định hành vi TTL khi pause.
- Kiểm tra chủ khóa, chương, khoảng cách, điều kiện tiền đề và cấu trúc đáp án trước chấm.
- Finale phải hoàn thành xếp mảnh và toàn bộ bước trả thư; không có lệnh cho client tự đặt `Complete`.
- Mỗi lệnh nghiệp vụ có CommandId để chống xử lý lặp; xử lý tuần tự trong phạm vi phòng hoặc cơ chế tương đương cho các thay đổi cạnh tranh.

**Hoàn thành khi:** hai đội có thể chơi hết hành trình độc lập; hai người tương tác đồng thời không nhân đôi vật phẩm, chuyển chương hay số lần trả lời sai.

### Giai đoạn 5 — Đồng hồ, kết quả, reconnect và lưu trữ (3–4 ngày)

- Server quản lý bắt đầu/kết thúc, hết giờ, hoàn thành từng đội và tie-break; dùng đồng hồ đơn điệu để đo khoảng thời gian, UTC để lưu mốc sự kiện.
- Duy trì đội khác chơi khi đội đầu hoàn thành; khóa tiến độ đội đã xong.
- UI tiến độ công khai không lộ câu đố/vị trí đội khác; kết quả lưu thành viên và tên đội tại thời điểm thi.
- Reconnect bằng token lưu dạng hash ở server; bind lại connection, loại quyền connection cũ, nhận snapshot đầy đủ và version mới.
- Snapshot sau reconnect gồm vị trí, tiến độ, trạng thái trận, thời gian và khóa đang có; không chỉ bật lại socket.
- Dọn phiên/phòng hết hạn, heartbeat; lưu kết quả và event log nhất quán, không log token thô.
- Chơi lại sinh MatchId mới; lệnh từ lượt cũ bị từ chối.

**Hoàn thành khi:** ngắt mạng ngắn rồi quay lại đúng phiên, kết quả không đổi theo đồng hồ client, lượt mới không nhận sự kiện cũ.

### Giai đoạn 6 — Hoàn thiện điều hành theo bản đặc tả đầy đủ (2–3 ngày)

- Admin pause/resume/end/cancel, khôi phục quyền admin sau mất mạng.
- Tham gia muộn/thay thế slot, `AdminCanPlay`, kết thúc ngay khi có đội thắng theo tùy chọn.
- Xuất CSV kết quả, xem lịch sử, các trạng thái hết giờ/bỏ cuộc/gián đoạn và lý do kết thúc.
- Bổ sung event pause/resume/closed và API tạo lượt mới, truy vấn/xuất kết quả.

**Hoàn thành khi:** chức năng điều hành tuân thủ state machine và thay đổi đồng bộ cho mọi client.

### Giai đoạn 7 — Nghiệm thu và đưa lên môi trường chạy (2–3 ngày)

- Chạy kiểm thử tích hợp và toàn bộ tiêu chí nghiệm thu ở mục 6 dưới đây.
- Đo dưới tải mục tiêu; đề xuất cấu hình thử nghiệm đầu 4 đội × 5 người, thêm kịch bản lệch sức chứa và nhiều đội ít người. Đây là cấu hình đo, không phải giới hạn hardcode.
- Đo p95 từ input tới cập nhật hiển thị đồng đội trong LAN, đối chiếu mục tiêu dưới 150 ms; ghi riêng RTT và thời gian server xử lý.
- Kiểm tra UI tại 1280×720, danh sách dài, mất mạng, ẩn tab, countdown và reconnect lúc đổi chương.
- Host ASP.NET Core phục vụ API/SignalR và client, cấu hình HTTPS, WebSocket, volume SQLite, log và dọn phòng. Static hosting riêng chỉ đủ cho bản single-player.
- Rà service worker để client cũ không dùng giao thức/nội dung không khớp; handshake kiểm tra version và yêu cầu tải lại khi cần.
- Cập nhật README cho chạy local, cấu hình giới hạn, publish và vận hành một buổi thi.

**Hoàn thành khi:** có bản chạy được trên máy chủ mục tiêu và biên bản nghiệm thu; single-player vẫn chạy độc lập.

## 6. Ma trận kiểm thử bắt buộc

| Nhóm | Kịch bản cần chứng minh | Tiêu chí đặc tả |
|---|---|---|
| Lobby | Số đội/sức chứa khác nhau, tranh slot cuối, thêm/xóa đồng bộ, tổng sức chứa vượt mức bị từ chối | 1–4 |
| Đồng hồ | Cùng StartedAt, hết giờ, pause/resume, sửa giờ client không ảnh hưởng | 5, 12 |
| Di chuyển | Cùng đội thấy avatar/di chuyển; đọc payload đội khác không thấy tọa độ; chặn vượt tốc độ/va chạm | 6, 7, 11 |
| Nhiệm vụ | Thu thập trùng, submit đồng thời, khóa hết hạn, gói lặp, đáp án khi chưa đủ điều kiện | 8, 9, 11 |
| Khôi phục | Reconnect trước/sau TTL, connection cũ bị vô hiệu, trở lại sau khi đồng đội chuyển chương | 10 |
| Kết quả | Đủ chuỗi finale, tie-break/đồng hạng, đội chưa hoàn thành, lượt chơi mới | 12 |
| Giao diện | Đội đông, nhiều đội, tên dài, cuộn danh sách, HUD không che game | 13 |
| Hồi quy | Toàn bộ hành trình một người, save/load, presenter, không có server | 14 |
| Quyền và phiên | Giả quyền admin, token sai, room/team ID khác, lệnh lượt cũ, thao tác khi Closed/Paused | 7, 11 |

Ưu tiên kiểm thử luật server và cạnh tranh dữ liệu; kiểm thử tích hợp dùng client SignalR thật; kiểm thử trình duyệt xác minh render, UI và cảm giác chuyển động.

## 7. Phạm vi và ước lượng

- **MVP để chơi thử:** giai đoạn 0–5 và phần nghiệm thu/triển khai tương ứng của giai đoạn 7. Giữ tham gia muộn và AdminCanPlay tắt; không hiển thị điều khiển chưa hỗ trợ.
- **Bản đầy đủ theo đặc tả:** thực hiện thêm giai đoạn 6 và nghiệm thu toàn bộ.
- **Chưa làm:** tài khoản lâu dài, chat, matchmaking, danh sách phòng công cộng, ghép từng bộ phận nhân vật, cân bằng tự động và scale nhiều server.

Ước lượng cho một lập trình viên quen codebase: khoảng **20–28 ngày công cho MVP**, **22–31 ngày công cho bản đầy đủ**, chưa gồm dự phòng. Nên dự phòng 20–30% cho việc tách engine, lỗi đồng thời và chất lượng di chuyển khi mạng không ổn định. Đây là ước lượng lập kế hoạch, sẽ cập nhật sau giai đoạn 1 và lát cắt ở giai đoạn 3.

Thứ tự phụ thuộc chính: **0 → 1 → 2 → 3 → 4 → 5 → 6 → 7**. Kiểm thử đi cùng từng giai đoạn; giai đoạn 7 là nghiệm thu tổng thể.

Việc đầu tiên khi bắt đầu triển khai: chạy kiểm thử đường cơ sở, chốt state machine và tách trạng thái cá nhân/đội. Không cần viết lại toàn bộ Canvas hoặc di chuyển cấu trúc client ngay từ đầu.
