# Chạy mô phỏng lớp 60 người

Mô phỏng này chỉ dùng dữ liệu giả `N01-TV01`…`N08-TV08`, SQLite tạm và server Development cục bộ. Nó không thay đổi cấu hình production hoặc cơ sở dữ liệu hiện hữu.

```powershell
cd R:\Ki9\MLn\TruyTimDanChu
.\scripts\run-class-simulation.ps1 -Mode Full
```

Lần chạy Full mất khoảng 7 phút, tạo 56 SignalR bot, 4 browser người chơi và 1 browser admin. Mỗi đội có hai bot leader (`TV01`, `TV02`) thực hiện nhiệm vụ; bốn browser ghi hai góc Nhóm 01 và hai góc Nhóm 08. Nếu máy đã có FFmpeg, script dùng bản đó; nếu không, script tải bản portable qua HTTPS, kiểm tra SHA-256 companion trước khi dùng. Có thể chỉ định bản đã kiểm soát bằng `-FfmpegPath C:\tools\ffmpeg.exe`.

Kiểm tra nhanh orchestration (8 nhóm × 2 bot, không tạo video):

```powershell
.\scripts\run-class-simulation.ps1 -Mode Dry -Headless
```

Nghiệm thu một lần Full bằng thư mục mới nhất trong `artifacts\simulation\`:

- `session-60p-8groups.mp4` mở được, dài 6–8 phút; bố cục admin trên, nhóm 01/08 dưới.
- `raw-webm\admin.webm`, `team01-player1.webm`, `team01-player2.webm`, `team08-player1.webm`, `team08-player2.webm` vẫn còn để đối chiếu.
- `screenshots\` có lobby, countdown, gameplay, puzzle, results, history.
- `session.json` báo `pass: true`; `BIEN_BAN_MO_PHONG.md` báo PASS và 8 nhóm Completed với thứ hạng Nhóm 01→08.
- `results.csv` có đúng header và 8 dòng kết quả; `events.ndjson` và `latency.csv` không chứa token.
- Sau script, `Get-NetTCPConnection -LocalPort <port>` không còn process server (port được chọn tự động), và SQLite nằm ngay trong thư mục run.

Trước khi ký nghiệm thu, chạy hồi quy chuẩn:

```powershell
dotnet build TruyTimDanChu.csproj -c Release
dotnet build Server/TruyTimDanChu.Server.csproj -c Release
dotnet test Tests/Multiplayer.UnitTests/Multiplayer.UnitTests.csproj -c Release
dotnet test Tests/Multiplayer.IntegrationTests/Multiplayer.IntegrationTests.csproj -c Release
dotnet test Tests/Multiplayer.Simulation.Tests/Multiplayer.Simulation.Tests.csproj -c Release
dotnet run --project Tests/GameSmoke.csproj -c Release
git diff --check
```
