# Báo cáo Phase 02 — Single-player chạy qua lớp phiên chơi

Ngày: 15/09/2026.

**Trạng thái: Hoàn thành nghiệm thu toàn diện** (Unit test 10/10 pass, GameSmoke pass, Integration test 7/7 pass, Chrome DevTools Protocol automation pass 100%). Sẵn sàng chuyển sang Phase 03.

## Chức năng đã triển khai

- Thêm interface `IGameSession` chuẩn hóa toàn bộ vòng đời và thao tác gameplay.
- Triển khai `SinglePlayerSession` đóng gói logic chơi đơn, tách bạch 3 mô hình miền:
  - `PlayerState`: Vị trí (`X`, `Y`), hướng nhìn (`Facing`), hoạt ảnh (`Walking`), thông tin nhận diện người chơi.
  - `TeamProgress`: Tiến độ nhiệm vụ dùng chung (`Chapter`, các mảng cờ `Spoken`, `Lamps`, `Clues`, `Lore`, trạng thái câu đố `Mirrors`, `DraftSequence`, `RiverSigns`, `FinaleSequence`, số mảnh `Shards`).
  - `LocalUiState`: Trạng thái UI cá nhân (`Panel`, danh sách thoại `Dialogue`, vị trí thoại `DialogueIndex`, thông báo `Toast`, âm thanh `Muted`, tỷ lệ chữ `TextScale`).
- Cập nhật `Pages/Home.razor` phụ thuộc vào `IGameSession` thay vì liên kết trực tiếp `GameEngine`.
- Di chuyển `GameEngine` vào project `Shared` (`Shared/Game/`) để dùng chung cho cả Client, Server authoritative và các bộ test tự động.
- Giữ nguyên 100% cấu trúc `SaveData` và tính năng lưu/khôi phục tiến độ trên `localStorage`.
- Giữ nguyên chế độ Trình chiếu (`PresenterJump`) cho người thuyết trình độc lập trong single-player.

## File thay đổi chính

| Phần | File |
|---|---|
| Model phiên chơi | `Shared/Game/SessionModels.cs` (`PlayerState`, `TeamProgress`, `LocalUiState`, `IGameSession`) |
| Phiên chơi đơn | `Shared/Game/SinglePlayerSession.cs` |
| Engine lõi | `Shared/Game/GameEngine.cs` (chuyển vị trí sang Shared) |
| Giao diện client | `Pages/Home.razor` (sử dụng `IGameSession`) |
| Dự án kiểm thử | `Tests/GameSmoke.csproj` (tham chiếu `Shared.csproj`) |
| Test tự động | `Tests/Multiplayer.UnitTests/SinglePlayerSessionTests.cs` |
| Theo dõi tiến độ | `TIEN_DO_MULTIPLAYER.md`, báo cáo này |

## Kết quả kiểm thử tự động

| Lệnh/kiểm tra | Kết quả |
|---|---|
| `dotnet build Shared/TruyTimDanChu.Shared.csproj -c Release` | PASS, 0 warning, 0 error |
| `dotnet build TruyTimDanChu.csproj -c Release` | PASS, 0 warning, 0 error |
| `dotnet run --project Tests/GameSmoke.csproj -c Release` | PASS: mở đầu, 4 nhiệm vụ, lựa chọn sai, phản hồi cuối và lưu/tiếp tục |
| `dotnet test Tests/Multiplayer.UnitTests/Multiplayer.UnitTests.csproj -c Release` | 10 pass (thêm 5 test mới cho `SinglePlayerSession`), 0 fail |
| `dotnet test Tests/Multiplayer.IntegrationTests/Multiplayer.IntegrationTests.csproj -c Release` | 7 pass, 0 fail |

## Kết quả kiểm chứng giao diện Chrome (CDP Automation)

Đã chạy kịch bản kiểm thử trình duyệt qua Chrome DevTools Protocol (`scratch/verify_phase02.mjs`):

1. **Khởi động và chơi qua lớp session:** Bấm BẮT ĐẦU HÀNH TRÌNH -> hoàn thành thoại mở đầu -> nhận nhiệm vụ chương "Bốn ngọn đèn" (ảnh minh chứng `scratch/evidence/p02_01_chapter_lights.png`).
2. **Menu tạm dừng:** Mở menu tạm dừng cá nhân thành công (ảnh `scratch/evidence/p02_02_pause_menu.png`).
3. **Lưu và khôi phục tiến độ:** Reload trình duyệt -> nút TIẾP TỤC LƯỢT TRƯỚC hiển thị -> bấm tiếp tục -> khôi phục đúng chương "Bốn ngọn đèn" từ `localStorage` (ảnh `scratch/evidence/p02_03_resumed_game.png`).
4. **Chế độ trình chiếu (Presenter):** Mở `/?presenter=1` -> hiển thị modal chọn điểm bắt đầu diễn tập (ảnh `scratch/evidence/p02_04_presenter_mode.png`).
5. **Độc lập mạng khi server tắt:** Tắt máy chủ (port 5080) -> single-player tiếp tục hoạt động 100% trơn tru, không có lỗi mạng hay treo UI (ảnh `scratch/evidence/p02_05_singleplayer_running_offline.png`).

**Kết luận Phase 02:** Đạt đầy đủ điều kiện nghiệm thu. Chế độ single-player đã được trừu tượng hóa an toàn qua lớp phiên chơi, sẵn sàng kết nối phiên multiplayer.

