# ĐẶC TẢ CƠ CHẾ MULTIPLAYER — TRUY TÌM DÂN CHỦ

## 1. Thông tin tài liệu

- Tên tính năng: Chơi online nhiều đội.
- Sản phẩm: Truy tìm Dân chủ — Bốn mảnh của Tiếng Nói.
- Trạng thái: Đặc tả đề xuất, chưa triển khai.
- Mục tiêu: Biến hành trình 10–15 phút hiện tại thành một cuộc thi cộng tác giữa nhiều đội.

## 2. Mô tả tổng quát

Admin tạo một phòng thi đấu và tự cấu hình danh sách đội. Người chơi nhập mã phòng, tham gia một đội và tùy chỉnh ngoại hình nhân vật. Các thành viên cùng đội được đưa vào chung một bản sao của bản đồ Minh Đăng, nhìn thấy nhau di chuyển và cùng chia sẻ tiến độ nhiệm vụ.

Mỗi đội có một bản đồ độc lập nhưng dùng cùng nội dung, vị trí vật phẩm, thứ tự câu đố và điều kiện chiến thắng. Đội hoàn thành toàn bộ hành trình trong thời gian ngắn nhất sẽ giành chiến thắng.

```text
Admin tạo phòng và cấu hình các đội
                 ↓
Người chơi nhập mã phòng, chọn đội và ngoại hình
                 ↓
Các thành viên xác nhận sẵn sàng
                 ↓
Admin khóa đội hình và bắt đầu đếm ngược
                 ↓
Mỗi đội chơi trên một bản đồ riêng, thành viên cùng đội cộng tác
                 ↓
Máy chủ xác nhận hoàn thành và lập bảng xếp hạng
```

## 3. Nguyên tắc số đội và số người linh hoạt

Số đội và số người trong đội không được viết cứng trong mã nguồn hoặc giao diện.

### 3.1. Cấu hình đội

Admin có thể:

- Thêm hoặc xóa đội khi phòng còn ở trạng thái `Lobby`.
- Đổi tên và màu nhận diện của từng đội.
- Sắp xếp lại thứ tự hiển thị các đội.
- Đặt số thành viên tối đa riêng cho từng đội.
- Cho phép các đội có sức chứa khác nhau.
- Chuyển người chơi giữa các đội trước khi bắt đầu.
- Cho phép người chơi tự chọn đội hoặc chỉ admin được phân đội.

Ví dụ hợp lệ:

| Đội | Sức chứa | Thành viên hiện tại |
|---|---:|---:|
| Ánh Sáng | 3 | 2 |
| Tiếng Nói | 5 | 5 |
| Đồng Thuận | 8 | 6 |
| Minh Bạch | 4 | 4 |

### 3.2. Giới hạn vận hành

- Giao diện không áp đặt một danh sách đội cố định.
- Máy chủ có cấu hình `MaxTeamsPerRoom`, `MaxPlayersPerTeam` và `MaxPlayersPerRoom` để bảo vệ tài nguyên.
- Các giá trị trên là giới hạn triển khai, không phải luật chơi cố định và có thể thay đổi mà không sửa logic game.
- Khi thêm đội hoặc tăng sức chứa, tổng số chỗ không được vượt quá `MaxPlayersPerRoom` của máy chủ.
- Mỗi đội phải có ít nhất một thành viên khi trận đấu bắt đầu.
- Có thể cho phép đội không đủ sức chứa bắt đầu; không bắt buộc các đội phải có số người bằng nhau.
- Giao diện cần cảnh báo admin nếu số người giữa các đội chênh lệch, vì điều này có thể ảnh hưởng tính công bằng.

## 4. Vai trò người dùng

### 4.1. Admin

Admin là người tạo và điều hành phòng. Admin có quyền:

- Tạo, sửa cấu hình và đóng phòng.
- Thêm, xóa, đổi tên hoặc đổi màu đội.
- Đặt sức chứa cho từng đội.
- Phân đội, chuyển đội hoặc loại người chơi.
- Khóa/mở quyền tham gia phòng.
- Khóa đội hình trước giờ bắt đầu.
- Bắt đầu, tạm dừng, tiếp tục hoặc hủy trận đấu.
- Theo dõi tiến độ và trạng thái kết nối.
- Xem kết quả, xuất bảng xếp hạng và tạo lượt chơi mới.

Admin mặc định không được tính là một người chơi. Hệ thống có thể cho phép admin tham gia một đội nếu bật tùy chọn `AdminCanPlay` trước khi trận đấu bắt đầu.

### 4.2. Người chơi

Người chơi có thể:

- Nhập tên hiển thị và mã phòng.
- Chọn đội nếu phòng bật chế độ tự chọn.
- Yêu cầu admin chuyển đội nếu đội hình do admin quản lý.
- Tùy chỉnh ngoại hình.
- Xác nhận hoặc hủy trạng thái sẵn sàng.
- Di chuyển, tương tác và giải nhiệm vụ cùng đồng đội.
- Xem tiến độ của đội và bảng xếp hạng theo quyền admin cấu hình.

## 5. Vòng đời phòng

| Trạng thái | Ý nghĩa | Hành động chính |
|---|---|---|
| `Lobby` | Đang tập hợp người chơi | Sửa đội, chọn ngoại hình, sẵn sàng |
| `Countdown` | Đếm ngược đồng bộ | Không được đổi đội hoặc ngoại hình |
| `Playing` | Trận đấu đang diễn ra | Di chuyển, giải nhiệm vụ, theo dõi tiến độ |
| `Finished` | Tất cả đội hoàn thành hoặc hết giờ | Xem kết quả, chơi lại |
| `Closed` | Phòng đã đóng | Không thể tham gia hoặc thay đổi dữ liệu |

Luồng chuyển trạng thái hợp lệ:

```text
Lobby → Countdown → Playing → Finished → Lobby
  └──────────────────────────────────────→ Closed
Countdown → Lobby       (admin hủy đếm ngược)
Playing   → Finished    (hết giờ hoặc admin kết thúc)
```

## 6. Tạo và tham gia phòng

### 6.1. Tạo phòng

Admin nhập:

- Tên phòng.
- Danh sách đội ban đầu hoặc thêm đội sau.
- Sức chứa của từng đội.
- Thời gian tối đa của trận đấu.
- Chế độ tự chọn đội hoặc admin phân đội.
- Cho phép hoặc cấm tham gia muộn.
- Chế độ hiển thị tiến độ trực tiếp.

Máy chủ sinh:

- `RoomId` dùng nội bộ.
- Mã phòng ngắn, ví dụ `DC-4821`.
- Khóa quản trị riêng cho admin.
- Thời điểm hết hạn của phòng nếu phòng không hoạt động.

### 6.2. Tham gia phòng

Người chơi nhập mã phòng và tên hiển thị. Máy chủ kiểm tra:

- Phòng có tồn tại và chưa đóng.
- Tên hiển thị hợp lệ và không trùng trong cùng phòng.
- Phòng có cho phép tham gia ở trạng thái hiện tại không.
- Đội được chọn còn chỗ không.
- Tổng số người chưa vượt giới hạn máy chủ.

Khi đội đã đầy, người chơi phải chọn đội khác hoặc chờ admin tăng sức chứa.

## 7. Tùy chỉnh ngoại hình

Trước khi bắt đầu, người chơi chọn một mẫu sprite từ danh sách được máy chủ cho phép.

Phiên bản đầu hỗ trợ:

- Mẫu nhân vật.
- Màu nhận diện hoặc biến thể trang phục.
- Phụ kiện nếu asset cung cấp.
- Xem trước hoạt ảnh đi bốn hướng.

Quy tắc:

- Ngoại hình chỉ mang tính trang trí, không thay đổi tốc độ hoặc khả năng tương tác.
- Tên hiển thị luôn nằm trên đầu nhân vật.
- Viền tên hoặc vòng chân sử dụng màu đội.
- Nhân vật do người chơi điều khiển có dấu nhận diện riêng.
- Có thể cho phép trùng mẫu giữa các đội.
- Trong cùng một đội, nếu hai người chọn cùng mẫu, hệ thống tự gán màu phụ hoặc ký hiệu khác nhau.
- Ngoại hình bị khóa khi phòng chuyển sang `Countdown`.

Sprite Pipoya đang có trong dự án được dùng làm danh sách ngoại hình ban đầu. Về sau có thể bổ sung bộ ghép tóc, trang phục và phụ kiện mà không thay đổi giao thức phòng.

## 8. Bản đồ theo đội

Mỗi đội sở hữu một `TeamGameInstance` riêng:

- Thành viên cùng đội ở chung một bản đồ.
- Người chơi thấy vị trí, hướng nhìn và hoạt ảnh của đồng đội.
- Người chơi không thấy nhân vật của đội khác.
- Các đội không thể lấy vật phẩm hoặc kích hoạt nhiệm vụ thay nhau.
- Mọi bản đồ dùng cùng phiên bản nội dung và cùng cấu hình nhiệm vụ.
- Thời điểm bắt đầu của tất cả đội được lấy từ cùng đồng hồ máy chủ.

Tách bản đồ theo đội giúp các đội có thể giải cùng một vật phẩm và cùng một câu đố mà không tranh chấp trực tiếp với đội khác.

## 9. Đồng bộ di chuyển thời gian thực

### 9.1. Dữ liệu đồng bộ

Mỗi cập nhật vị trí gồm:

- `PlayerId`.
- Tọa độ `X`, `Y`.
- Hướng nhìn.
- Trạng thái đứng hoặc đi.
- Số thứ tự gói tin.
- Mốc thời gian máy chủ.

### 9.2. Tần suất và hiển thị

- Client gửi ý định di chuyển hoặc trạng thái vị trí khoảng 10–15 lần/giây.
- Máy chủ kiểm tra tốc độ, va chạm và phạm vi hợp lệ.
- Client hiển thị ở tốc độ khung hình của trình duyệt, thường là 60 FPS.
- Vị trí của đồng đội được nội suy giữa hai gói tin gần nhất.
- Khi sai lệch nhỏ, client dịch chuyển mềm về vị trí máy chủ.
- Khi sai lệch lớn hoặc đổi bản đồ, client đặt lại vị trí ngay lập tức.

Cách này giảm lưu lượng mạng nhưng vẫn tránh hiện tượng nhân vật giật hoặc dịch chuyển từng đoạn.

## 10. Tiến độ nhiệm vụ dùng chung

Trạng thái sau được chia sẻ trong một đội:

- NPC đã trò chuyện.
- Vật phẩm và manh mối đã thu thập.
- Câu đố đã hoàn thành.
- Bốn mảnh Tiếng Nói.
- Chương hiện tại.
- Lịch sử đáp án sai.
- Thời gian hoàn thành từng chặng.

Quy tắc cập nhật:

- Một thành viên thu thập vật phẩm thì vật phẩm được tính cho toàn đội.
- Một thành viên hoàn thành câu đố thì tất cả thành viên nhận chương hoặc mục tiêu mới.
- Hội thoại có thể được đọc riêng nhưng cờ nhiệm vụ thuộc về đội.
- Máy chủ là nơi duy nhất được quyền xác nhận hoàn thành.
- Mọi thay đổi được phát tới tất cả thành viên cùng đội qua SignalR.

## 11. Xử lý nhiều người cùng tương tác

Mỗi vật thể hoặc câu đố có thể ở một trong các trạng thái:

- `Available`: có thể tương tác.
- `Reserved`: đang được một người mở.
- `Completed`: đội đã hoàn thành.

Khi một người mở câu đố:

- Máy chủ tạm giữ quyền tương tác cho người đó.
- Đồng đội thấy thông báo ai đang xử lý.
- Chỉ người giữ quyền được gửi đáp án.
- Khi đóng giao diện, hết thời gian hoặc mất kết nối, quyền được trả lại.
- Nếu hoàn thành, kết quả được áp dụng ngay cho cả đội.

Các nhiệm vụ thu thập độc lập không cần khóa và có thể được nhiều thành viên thực hiện song song.

## 12. Cơ chế cộng tác

Multiplayer cần khuyến khích phân công thay vì để mọi người chạy theo một người dẫn đầu:

- Bốn thành viên có thể đi gặp bốn NPC khác nhau.
- Các manh mối ở Phố Tin tức có thể được thu thập đồng thời.
- Một người đọc tài liệu trong thư viện trong khi người khác di chuyển tới mục tiêu tiếp theo.
- Kết quả thu thập được cập nhật ngay trên HUD của cả đội.
- Thử thách cuối chỉ mở khi trạng thái dùng chung đã đủ điều kiện.

Nếu đội có nhiều người hơn số nhánh nhiệm vụ, các thành viên còn lại vẫn có thể hỗ trợ định hướng, đọc nội dung hoặc đi trước đến vị trí kế tiếp. Số người đông không tạo thêm vật phẩm hoặc thay đổi độ khó tự động trong phiên bản đầu.

## 13. Thời gian và điều kiện chiến thắng

Máy chủ quản lý toàn bộ thời gian:

- `StartedAt`: thời điểm đếm ngược kết thúc.
- `FinishedAt`: thời điểm máy chủ xác nhận nhiệm vụ cuối.
- `Elapsed = FinishedAt - StartedAt`.

Một đội chỉ hoàn thành khi:

- Đã hoàn thành đủ các chương bắt buộc.
- Có đủ bốn mảnh Tiếng Nói.
- Đã gửi đúng đáp án thử thách cuối.
- Trạng thái được xác minh bởi máy chủ.

Xếp hạng theo thứ tự:

1. Đội có thời gian hoàn thành thấp hơn.
2. Nếu bằng nhau, đội có ít lần trả lời sai hơn.
3. Nếu vẫn bằng nhau, đội hoàn thành chặng áp chót sớm hơn.
4. Nếu mọi tiêu chí vẫn bằng nhau, hai đội đồng hạng.

Khi đội đầu tiên hoàn thành, các đội khác tiếp tục chơi để xác định đầy đủ bảng xếp hạng. Admin có thể cấu hình kết thúc trận ngay khi có đội thắng, nhưng đây không phải thiết lập mặc định.

## 14. Bảng tiến độ và kết quả

Trong trận, bảng tiến độ có thể hiển thị:

- Tên và màu đội.
- Số mảnh đã thu thập.
- Trạng thái đang chơi, đã hoàn thành hoặc mất kết nối toàn đội.
- Thứ hạng tạm thời nếu admin cho phép.

Không hiển thị vị trí chính xác hoặc câu đố cụ thể của đội khác.

Sau trận, bảng kết quả gồm:

- Thứ hạng.
- Tên đội.
- Danh sách thành viên.
- Thời gian hoàn thành.
- Số lần trả lời sai.
- Thời gian hoàn thành từng chương.
- Trạng thái `Hoàn thành`, `Hết giờ` hoặc `Bỏ cuộc`.

## 15. Mất kết nối và tham gia lại

- Khi mất kết nối, nhân vật chuyển sang trạng thái tạm vắng.
- Máy chủ giữ phiên người chơi trong khoảng thời gian cấu hình, ví dụ 90 giây.
- Người chơi dùng mã tái kết nối để trở lại đúng phòng, đội, ngoại hình và vị trí.
- Trong thời gian mất kết nối, đồng đội vẫn tiếp tục chơi.
- Nếu quá thời gian giữ phiên, nhân vật được loại khỏi bản đồ nhưng tiến độ đội không bị mất.
- Admin có thể cho phép người mới thay thế vị trí trống nếu phòng bật tham gia muộn.

## 16. Công bằng và chống gian lận

- Đồng hồ, va chạm, nhiệm vụ, đáp án và điều kiện thắng được xác nhận ở máy chủ.
- Client chỉ gửi ý định, không tự tuyên bố hoàn thành.
- Máy chủ từ chối vị trí vượt quá tốc độ cho phép.
- Mỗi lệnh có số thứ tự để tránh gửi lại hoặc đảo thứ tự.
- Mọi lần trả lời, hoàn thành và thao tác quản trị được ghi vào `MatchEventLog`.
- Mọi đội dùng cùng phiên bản bản đồ và nội dung.
- Khi admin thay đổi cấu hình sau khi có người sẵn sàng, trạng thái sẵn sàng của phòng được đặt lại.

Do số người mỗi đội có thể khác nhau, hệ thống phải cảnh báo nhưng không bắt buộc cân bằng. Admin chịu trách nhiệm lựa chọn luật phù hợp cho buổi thi đấu.

## 17. Mô hình dữ liệu đề xuất

### 17.1. Room

```text
RoomId
RoomCode
Name
Status
AdminConnectionId
AllowSelfTeamSelection
AllowLateJoin
ShowLiveLeaderboard
AdminCanPlay
TimeLimitSeconds
StartedAt
FinishedAt
CreatedAt
```

### 17.2. Team

```text
TeamId
RoomId
Name
Color
DisplayOrder
Capacity
Status
FinishedAt
WrongAnswerCount
```

### 17.3. Player

```text
PlayerId
RoomId
TeamId
DisplayName
AvatarId
AccentColor
ConnectionId
ReconnectTokenHash
IsReady
IsConnected
LastSeenAt
PositionX
PositionY
Facing
MovementSequence
```

### 17.4. TeamGameState

```text
TeamId
Chapter
SpokenNpcFlags
LampFlags
ClueFlags
LoreFlags
DraftCompleted
RiverCompleted
NewsCompleted
FinaleCompleted
Version
UpdatedAt
```

### 17.5. MatchEventLog

```text
EventId
RoomId
TeamId
PlayerId
EventType
Payload
ServerTimestamp
```

## 18. Sự kiện SignalR đề xuất

### Client gửi lên máy chủ

- `CreateRoom(settings)`
- `JoinRoom(roomCode, displayName, reconnectToken?)`
- `JoinTeam(teamId)`
- `LeaveTeam()`
- `SetReady(isReady)`
- `SelectAvatar(avatarId, accentColor)`
- `SendMovement(input, sequence)`
- `Interact(objectId)`
- `SubmitPuzzle(puzzleId, answer)`
- `AdminAddTeam(teamSettings)`
- `AdminUpdateTeam(teamId, teamSettings)`
- `AdminRemoveTeam(teamId)`
- `AdminMovePlayer(playerId, teamId)`
- `AdminStartMatch()`
- `AdminPauseMatch()`
- `AdminResumeMatch()`
- `AdminEndMatch()`

### Máy chủ phát xuống client

- `RoomSnapshot(room)`
- `TeamAdded(team)`
- `TeamUpdated(team)`
- `TeamRemoved(teamId)`
- `PlayerJoined(player)`
- `PlayerUpdated(player)`
- `PlayerLeft(playerId)`
- `PlayerMoved(movementSnapshot)`
- `CountdownStarted(serverStartTime)`
- `MatchStarted(matchSnapshot)`
- `TeamProgressUpdated(teamState)`
- `PuzzleReserved(puzzleId, playerId)`
- `PuzzleReleased(puzzleId)`
- `TeamFinished(teamResult)`
- `LeaderboardUpdated(results)`
- `MatchFinished(results)`
- `RoomError(code, message)`

## 19. Kiến trúc .NET đề xuất

```text
Blazor WebAssembly Client
 ├─ Canvas game
 ├─ Lobby và chọn đội
 ├─ Tùy chỉnh ngoại hình
 ├─ HUD tiến độ đội
 └─ Bảng xếp hạng
              │
           SignalR
              │
ASP.NET Core Server
 ├─ RoomHub
 ├─ RoomManager
 ├─ MatchManager
 ├─ TeamGameInstance
 ├─ MovementValidator
 ├─ GameStateValidator
 └─ Persistence
       ├─ Bộ nhớ cho trạng thái đang chạy
       └─ SQLite cho phòng/kết quả/lịch sử
```

Máy chủ là authoritative server. Client Blazor chịu trách nhiệm hiển thị, nhận input và dự đoán/nội suy chuyển động, nhưng không có quyền quyết định kết quả trận đấu.

## 20. Màn hình cần có

### Admin

1. Tạo phòng.
2. Cấu hình động danh sách đội.
3. Phòng chờ và phân đội.
4. Bảng điều khiển trận đấu.
5. Kết quả và lịch sử.

### Người chơi

1. Nhập mã phòng.
2. Chọn đội hoặc chờ admin phân đội.
3. Chọn ngoại hình.
4. Phòng chờ và nút sẵn sàng.
5. Màn chơi chung với đồng đội.
6. Bảng kết quả.

## 21. Yêu cầu phi chức năng

- Một người chơi không được nhận dữ liệu vị trí chi tiết của đội khác.
- Độ trễ mục tiêu trong mạng nội bộ: dưới 150 ms.
- Hoạt ảnh hiển thị độc lập với tần suất nhận gói mạng.
- Thao tác hoàn thành nhiệm vụ phải có tính nguyên tử, không hoàn thành hai lần.
- Room state phải có số phiên bản để client phát hiện dữ liệu cũ.
- Có cơ chế heartbeat và dọn phòng không hoạt động.
- Mọi tên hiển thị, tên đội và mã phòng phải được kiểm tra độ dài và ký tự.
- Không ghi reconnect token dạng nguyên bản vào cơ sở dữ liệu hoặc log.
- Giao diện danh sách đội phải cuộn hoặc chia trang được khi có nhiều đội.
- HUD trong game chỉ hiện đồng đội và dữ liệu cần thiết, không che vùng chơi khi đội đông.

## 22. Phạm vi MVP đề xuất

Phiên bản đầu triển khai:

- Admin tạo phòng và thêm số đội tùy ý trong giới hạn máy chủ.
- Sức chứa cấu hình riêng cho từng đội.
- Người chơi nhập mã phòng, chọn đội và chọn sprite.
- Các thành viên cùng đội nhìn thấy nhau di chuyển.
- Nội suy chuyển động và đồng bộ bốn hướng.
- Tiến độ nhiệm vụ dùng chung theo đội.
- Bộ đếm thời gian do máy chủ quản lý.
- Kết quả và bảng xếp hạng cuối trận.
- Tự kết nối lại sau mất mạng ngắn hạn.

Chưa cần trong MVP:

- Tài khoản và mật khẩu lâu dài.
- Chat thoại hoặc chat văn bản.
- Ghép đội tự động.
- Máy chủ công cộng có danh sách phòng.
- Trình ghép nhân vật theo từng bộ phận.
- Cân bằng độ khó tự động theo số người.

## 23. Tiêu chí nghiệm thu

1. Admin có thể tạo phòng với số đội khác nhau mà không sửa mã nguồn.
2. Admin có thể đặt sức chứa khác nhau cho từng đội.
3. Không thể vào một đội đã đầy.
4. Thêm hoặc xóa đội được đồng bộ ngay tới mọi người trong lobby.
5. Khi bắt đầu, tất cả đội nhận cùng một mốc thời gian máy chủ.
6. Thành viên cùng đội nhìn thấy chuyển động và ngoại hình của nhau.
7. Người chơi không nhận vị trí chi tiết của đội khác.
8. Một vật phẩm do một thành viên thu thập được tính cho cả đội đúng một lần.
9. Câu đố không thể bị hai người gửi đáp án xung đột.
10. Người mất kết nối có thể trở lại đúng đội và tiến độ cũ.
11. Máy chủ từ chối yêu cầu di chuyển hoặc hoàn thành không hợp lệ.
12. Đội hoàn thành sớm nhất được xếp hạng cao nhất theo thời gian máy chủ.
13. Khi có nhiều đội hoặc nhiều thành viên, giao diện vẫn sử dụng được và không che khu vực chơi chính.
14. Bản game một người hiện tại vẫn có thể chạy độc lập nếu chế độ multiplayer chưa được bật.

