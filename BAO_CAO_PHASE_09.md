# BÁO CÁO NGHIỆM THU PHASE 09: CHỌN NGOẠI HÌNH (AVATAR)

---

## 1. MỤC TIÊU VÀ PHẠM VI NGHIỆM THU
- Triển khai chức năng chọn ngoại hình (avatar) nhân vật bằng tài nguyên Pipoya spritesheet có sẵn trong dự án (`wwwroot/assets/pipoya/`).
- Hỗ trợ danh mục 9 nhân vật whitelisted: **Quang, Trọng, Kiều Anh, Ninh, Phương, Dũng, Bảo, Hân, Nam**.
- Cung cấp component xem trước 4 hướng (Xuống, Trái, Phải, Lên) không bị cắt sprite, hiển thị chuyển động idle/stand chân thực.
- Kiểm tra danh sách trắng (whitelist): từ chối tuyệt đối các ID lạ hoặc URL asset tùy ý để đảm bảo an toàn bảo mật.
- Hỗ trợ ký hiệu phân biệt ổn định (`#1`, `#2`,...) khi nhiều người chơi trong phòng chọn cùng ngoại hình, và tự động biến mất khi người chơi chuyển sang ngoại hình độc nhất.
- Đồng bộ nguyên tử qua SignalR (`PlayerAvatarChanged`), cập nhật trực tiếp snapshot người chơi và tăng `RoomVersion`.
- Bảo toàn tuyệt đối chế độ chơi đơn (Single-Player), tính tương thích lưu trữ và nội dung nguyên bản (`GameSmoke` đạt 100%).

---

## 2. KIẾN TRÚC VÀ CÁC THAY ĐỔI TRIỂN KHAI

### 2.1. Shared Layer (`Shared/AvatarCatalog.cs`, `Shared/RoomModels.cs`)
- **`AvatarCatalog`**:
  - `IReadOnlyList<AvatarInfo> All`: Danh sách 9 nhân vật Pipoya (`quang`, `trong`, `kieu_anh`, `ninh`, `phuong`, `dung`, `bao`, `han`, `nam`).
  - `bool IsValid(string? avatarId)`: Kiểm tra whitelist không phân biệt hoa thường.
  - `AvatarInfo? Get(string? avatarId)`: Lấy thông tin avatar hoặc `null` nếu không hợp lệ.
  - `string DefaultAvatarId`: Mặc định là `"quang"`.
  - `string GetDiscriminator(string playerId, IEnumerable<PlayerSnapshot> roomPlayers)`: Tạo ký hiệu `#1`, `#2` ổn định, tất định theo thứ tự `PlayerId` của các người chơi trùng sprite.
- **DTOs & Error Codes**:
  - `SelectAvatarRequest(string RoomId, string PlayerId, string AvatarId)`
  - `SelectAvatarResponse(bool Success, string? ErrorCode, string? AvatarId, int RoomVersion)`
  - `PlayerAvatarChangedEvent(string PlayerId, string AvatarId)`
  - `RoomErrorCodes.InvalidAvatarId`: Mã lỗi chuẩn hóa khi client gửi ID ngoài whitelist.

### 2.2. Server Layer (`Server/RoomInstance.cs`, `Server/RoomManager.cs`, `Server/RoomHub.cs`)
- **`RoomInstance.TrySelectAvatar(playerId, avatarId)`**:
  - Kiểm tra trạng thái phòng (`RoomStatus.Lobby`).
  - Xác thực ID nhân vật qua `AvatarCatalog.IsValid(avatarId)`.
  - Cập nhật nguyên tử thuộc tính `AvatarId` trên `PlayerSession`.
  - Tăng `Version++` và cập nhật `LastActivityAt`.
- **`RoomManager.SelectAvatar(request)`**: Điều phối request tới room instance tương ứng.
- **`RoomHub.SelectAvatar(request)`**: Xử lý gọi qua SignalR, broadcast sự kiện `PlayerAvatarChanged` tới toàn bộ client trong nhóm phòng.

### 2.3. Client & UI Layer (`Services/MultiplayerConnection.cs`, `Pages/AvatarPicker.razor`, `Pages/Lobby.razor`, `wwwroot/css/multiplayer.css`)
- **`MultiplayerConnection`**:
  - Thêm sự kiện `event Action<PlayerAvatarChangedEvent>? PlayerAvatarChanged`.
  - Đăng ký nhận SignalR event `"PlayerAvatarChanged"` và cập nhật trạng thái kết nối.
  - Phương thức `SelectAvatarAsync(SelectAvatarRequest request)`.
- **`AvatarPicker.razor`**:
  - Hiển thị danh sách 9 avatar dưới dạng lưới thẻ tương tác.
  - Mỗi thẻ hiển thị 4 hướng chuyển động (Xuống - Trái - Phải - Lên) bằng CSS sprite clipping chính xác `32x32px` với `image-rendering: pixelated`.
  - Hiển thị trạng thái đang chọn (`is-selected`), số lượng người trong phòng đang dùng cùng avatar, và chip tên người chơi.
  - Hỗ trợ phím tắt bàn phím (`Enter`, `Space`) và `aria-checked` trợ năng.
- **`Lobby.razor`**:
  - Tích hợp `<AvatarPicker>` cho người chơi trong phòng.
  - Cập nhật avatar preview thu nhỏ (`.mini-sprite`, `.player-avatar-preview-box`) trong danh sách thành viên đội và danh sách người chơi phòng.
  - Hiển thị huy hiệu phân biệt (`.discriminator-badge`) khi trùng avatar.
- **`wwwroot/css/multiplayer.css`**: Thiết kế giao diện pixel art chuyên nghiệp, sắc nét, đồng bộ phong cách cổ điển của trò chơi.

---

## 3. KẾT QUẢ KIỂM THỬ TỰ ĐỘNG

### 3.1. Unit Tests (`Tests/Multiplayer.UnitTests/AvatarTests.cs`)
- **97/97 passed (100%)**:
  - `Catalog_HasNinePipoyaAvatars_AllValid`: Kiểm tra 9 avatar hợp lệ với đường dẫn tài nguyên chuẩn.
  - `Catalog_ValidatesWhitelistedIds`: Kiểm tra whitelist chính xác, chấp nhận chữ hoa/chữ thường.
  - `Catalog_RejectsInvalidOrArbitraryIds`: Từ chối `null`, chuỗi rỗng, ID lạ (`superman`), path traversal (`../../../etc/passwd`).
  - `DefaultAvatar_IsQuang`: Xác nhận avatar mặc định của người chơi mới là `"quang"`.
  - `TrySelectAvatar_Success_UpdatesPlayerAndVersion`: Chọn thành công, cập nhật snapshot và tăng room version.
  - `TrySelectAvatar_RejectsInvalidAvatarId`: Từ chối ID không hợp lệ với lỗi `INVALID_AVATAR_ID`.
  - `TrySelectAvatar_RejectsNonExistentPlayer`: Từ chối người chơi không tồn tại với lỗi `PLAYER_NOT_FOUND`.
  - `Discriminator_AssignsStableMarkersForDuplicateAvatars`: Kiểm tra gán ký hiệu `#1`, `#2` ổn định, tất định cho người chơi trùng avatar, không gán cho avatar duy nhất.
  - `ConcurrentAvatarSelection_ThreadSafeAndConsistent`: Kiểm tra đồng thời 50 lượt thay đổi avatar trên 8 người chơi bảo đảm thread-safe tuyệt đối.

### 3.2. Integration Tests (`Tests/Multiplayer.IntegrationTests/AvatarIntegrationTests.cs`)
- **14/14 passed (100%)**:
  - `SignalR_SelectAvatar_BroadcastsToRoomAndUpdatesRoster`: Khởi chạy WebApplication máy chủ thật, 2 client người chơi và 1 admin. Kiểm tra broadcast `PlayerAvatarChanged` qua SignalR, từ chối ID sai, và xác minh discriminator sinh ra chính xác khi cả 2 người cùng chọn avatar `"han"`.

### 3.3. GameSmoke Test (`Tests/GameSmoke.csproj`)
- **PASS**:
  - Chế độ chơi đơn (Single-Player), 4 nhiệm vụ, hội thoại, tương tác NPC, câu đố, tính năng lưu/tiếp tục hoàn toàn nguyên vẹn, không chịu bất kỳ tác động tiêu cực nào từ hệ thống nhiều người chơi.

---

## 4. KẾT QUẢ KIỂM TRA GIAO DIỆN CHROME CDP (HEADLESS CHROME)

Kiểm tra tự động kịch bản thực tế trên Chrome CDP đa client (`verify_phase09.mjs`):
1. **Admin tạo phòng**: Phòng thi đấu "Giải Đấu Dân Chủ Phổ Thông", thêm Đội Sao Vàng và Đội Búa Liềm.
2. **Player A (Minh Đức) vào phòng và chọn avatar "Bảo"**:
   - Giao diện hiển thị toàn bộ 9 avatar kèm preview 4 hướng.
   - Thẻ nhân vật "Bảo" nhận trạng thái `✓ Đang chọn`.
   - Minh Đức vào Đội Sao Vàng với biểu tượng sprite của Bảo.
   - Ảnh minh chứng: `scratch/evidence/p09_01_player_a_selects_bao_preview.png`.
3. **Player B (Thanh Hằng) vào phòng và cũng chọn avatar "Bảo"**:
   - Hệ thống tự động phân định: `Thanh Hằng #2 (Bạn)` và `Minh Đức #1`.
   - Thẻ avatar hiển thị chip thành viên đang sử dụng kèm mã `#1`, `#2`.
   - Ảnh minh chứng: `scratch/evidence/p09_02_duplicate_avatar_discriminator.png`.
4. **Player B chuyển sang avatar "Kiều Anh"**:
   - Minh Đức sở hữu avatar "Bảo" độc nhất -> ký hiệu phân biệt tự động xóa bỏ.
   - Thanh Hằng sở hữu avatar "Kiều Anh" độc nhất -> không hiển thị ký hiệu thừa.
   - Ảnh minh chứng: `scratch/evidence/p09_03_player_b_switches_unique_avatar.png`.
5. **Giao diện Admin cập nhật danh sách người chơi**:
   - Admin nhìn thấy danh sách người chơi với ngoại hình và đội tương ứng đã chọn.
   - Ảnh minh chứng: `scratch/evidence/p09_04_admin_roster_view.png`.

---

## 5. KẾT LUẬN NGHIỆM THU
- **Đạt điều kiện nghiệm thu Phase 09**: Toàn bộ yêu cầu kỹ thuật và chức năng của Phase 09 đã được hoàn thành, kiểm thử tự động, kiểm thử tích hợp SignalR và xác minh hình ảnh trên Chrome CDP đạt 100%.
- Sẵn sàng chuyển tiếp sang **Phase 10: Ready và khóa lobby**.

