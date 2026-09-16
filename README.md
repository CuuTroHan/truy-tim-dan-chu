# Truy tìm Dân chủ — Bốn mảnh của Tiếng Nói

Game phiêu lưu 2D trên web, viết bằng Blazor WebAssembly (.NET 10) và Canvas 2D. Toàn bộ mã nguồn, bài kiểm thử và bản xuất bản nằm trong **thư mục `TruyTimDanChu` này**; không cần cơ sở dữ liệu hoặc dịch vụ bên ngoài.

## Chạy game

Máy cần cài .NET 10 SDK. Mở PowerShell trong thư mục này rồi chạy:

```powershell
dotnet run --project .\TruyTimDanChu.csproj
```

Trình duyệt thường tự mở `http://localhost:5267`; nếu không, mở đúng địa chỉ `http://localhost:...` được in trong cửa sổ lệnh. Hãy chạy thử và tải game một lần trên laptop trước buổi thuyết trình. Không mở `index.html` bằng `file://` vì trình duyệt sẽ chặn WebAssembly/service worker.

Nếu muốn đưa lên static hosting, dùng các file trong `dist\wwwroot`. Máy chủ cần phục vụ `.wasm` với MIME `application/wasm` và hỗ trợ trang mặc định `index.html`. Bản publish không cần .NET trên máy chủ.

## Điều khiển

- `WASD` hoặc phím mũi tên: di chuyển Quang.
- `E`, `Space`, `Enter`: tương tác, hiện hết hoặc tiếp tục hội thoại.
- `H`: gợi ý; `Esc`: tạm dừng; `M`: bật/tắt âm thanh.
- Biểu tượng `▤`: mở Sổ Tiếng Nói. Câu đố và menu dùng chuột hoặc `Tab`/`Enter`.
- `Ctrl+Shift+P` hoặc thêm `?presenter=1` vào URL: chế độ diễn tập, nhảy tới từng chương. Chế độ này không ghi đè bản lưu chính.

Game tự lưu ở các mốc nhiệm vụ và sau khi nhặt manh mối. Nút **Tiếp tục lượt trước** ở màn hình đầu sẽ khôi phục tiến độ gần nhất. Bắt đầu lượt mới xóa bản lưu cũ trong trình duyệt đang dùng.

## Nội dung

Quang đi tìm “dân chủ” tại thị trấn hư cấu Minh Đăng. Bốn nhiệm vụ xoay quanh chủ thể quyền lực, đường đi của góp ý, đối thoại/ phản hồi và kiểm chứng một phản ánh. Các nhân vật Hán, Phương, Quang, Dũng, Bảo, Kiều Anh, Nam, Ninh và Trọng chỉ mượn tên do nhóm cung cấp; ngoại hình và lời thoại đều hư cấu.

Nội dung tham khảo dàn ý thuyết trình Chủ đề 3 của nhóm, có màn nguồn và bốn bảng kiến thức phụ trong thư viện. Đây là trò chơi học tập, không thay thế tài liệu pháp lý gốc.

## Kiểm thử và cấu trúc

```powershell
dotnet run --project .\Tests\GameSmoke.csproj -c Release
dotnet build .\TruyTimDanChu.csproj -c Release
dotnet publish .\TruyTimDanChu.csproj -c Release -o .\dist
```

- `Game\GameEngine.cs`: nhiệm vụ, hội thoại, va chạm, câu đố và save trong C#.
- `Pages\Home.razor`: HUD, hộp thoại, câu đố và menu.
- `wwwroot\js\game.js`: vẽ Canvas, nhận phím và tạo hiệu ứng âm thanh nhẹ.
- `wwwroot\css\app.css`: giao diện.
- `Tests`: bài kiểm thử luồng chơi.
- `dist\wwwroot`: bản web đã xuất bản, dùng để đem đi chạy trên máy chủ tĩnh.

Game không dùng hình ảnh/font/âm thanh từ CDN. Khuyến nghị Chrome hoặc Edge desktop, màn hình 1280×720 trở lên. Bản game được thiết kế cho buổi demo khoảng 10–15 phút; thời lượng thực tế tùy tốc độ đọc và khám phá.

## Ghi công asset

Sprite nhân vật sử dụng **PIPOYA FREE RPG Character Sprites 32x32** của Pipoya. Bộ asset cho phép sử dụng và chỉnh sửa trong dự án cá nhân hoặc thương mại, nhưng không cho phép phân phối hoặc bán lại asset như một gói độc lập. Xem nguồn chính thức tại https://pipoya.itch.io/pipoya-free-rpg-character-sprites-32x32 và bản ghi giấy phép trong `wwwroot/assets/pipoya/LICENSE.md`.

## Multiplayer — Phase 01

Đã có màn `/online` kiểm tra kết nối SignalR và phiên bản game. Chưa có tạo phòng, đội hoặc gameplay online. Nút **CHƠI ONLINE** nằm trên màn hình đầu; single-player vẫn chạy độc lập.

Mở hai terminal tại thư mục dự án:

```bash
# Terminal 1: server, http://localhost:5080
 dotnet run --project Server/TruyTimDanChu.Server.csproj

# Terminal 2: client, http://localhost:5267
 dotnet run --project TruyTimDanChu.csproj
```

Mở `http://localhost:5267/online`: phải thấy **Đã kết nối máy chủ**. Tắt server: trạng thái chuyển sang mất kết nối (mất mạng im lặng có thể cần khoảng 12 giây); bật lại server rồi bấm **THỬ LẠI**. Mỗi lần kết nối được giới hạn 8 giây. Rời màn online sẽ hủy kết nối; vào lại tạo kết nối mới. Phase này thử lại thủ công, chưa phục hồi phiên người chơi của phase 27.

Cấu hình client: `wwwroot/appsettings.json` dùng `/hubs/room` cùng origin; `wwwroot/appsettings.Development.json` dùng `http://localhost:5080/hubs/room`. HTTPS client cần Hub HTTPS tương ứng để tránh mixed content; cấu hình local mặc định dùng HTTP ở cả hai phía. Không đặt secret trong các file cấu hình client công khai.

Server: `Server/appsettings.Development.json` chỉ cho origin local đã chỉ định. Ngoài Development, cấu hình `Multiplayer:AllowedOrigins` cho origin cụ thể nếu client chạy khác origin; mặc định không cho cross-origin. Kiểm tra Origin áp dụng cả đường WebSocket; đây không phải xác thực người dùng. Phase 01 chỉ expose Handshake, không có lệnh quản trị/gameplay.

Khi thay giao thức hoặc nội dung bản đồ, tăng version trong `Shared/ConnectionProtocol.cs` và phát hành client/server tương ứng. Server trả mã lỗi khi lệch protocol/content; client đóng kết nối đó và yêu cầu tải lại. Chưa cấu hình host client cùng server trong phase này.

Kiểm thử:

```bash
dotnet test Tests/Multiplayer.UnitTests/Multiplayer.UnitTests.csproj -c Release
dotnet test Tests/Multiplayer.IntegrationTests/Multiplayer.IntegrationTests.csproj -c Release
dotnet run --project Tests/GameSmoke.csproj -c Release
dotnet build TruyTimDanChu.csproj -c Release
dotnet build Server/TruyTimDanChu.Server.csproj -c Release
```

Integration test chạy Kestrel trên cổng loopback ngẫu nhiên với client SignalR thật, bao gồm handshake, sai phiên bản, CORS/WebSocket Origin, restart/retry, timeout và dispose khi đang kết nối. Test sử dụng trực tiếp source `MultiplayerConnection.cs` của client để kiểm tra vòng đời mà không kéo WASM vào tiến trình test.

Tham khảo API: [SignalR .NET client — Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/signalr/dotnet-client?view=aspnetcore-10.0).

Khi rebuild client trong lúc dev server đang chạy và thấy runtime fingerprint trả 404, dừng rồi chạy lại tiến trình client và hard reload Chrome. Trang cần nạp `TruyTimDanChu.styles.css` để áp dụng CSS riêng của các component.

## Phase 40 — publish và vận hành Windows

Publish tái lập (script chỉ xóa thư mục con dưới `artifacts`):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-windows.ps1 -Runtime win-x64
```

Kết quả nằm trong `artifacts\publish\server` và `artifacts\publish\manifest.json`. Copy `Server\appsettings.Production.example.json` thành file cấu hình riêng ngoài publish, thay hostname/connection string; certificate HTTPS và password phải lấy từ Windows Certificate Store hoặc environment (`ASPNETCORE_Kestrel__Certificates__Default__Path/Password`), tuyệt đối không commit.

SQLite production đặt ngoài thư mục binary, ví dụ `C:\ProgramData\TruyTimDanChu\data`. Trước deploy: kiểm tra `/ready`, dừng/kết thúc match active theo policy, tạo backup timestamp và thử mở/query backup. Sau deploy/restart kiểm tra `/health`, `/ready`, `/`, SignalR handshake và `/api/rooms/.../matches`. Match Active/Paused khi process restart được đánh dấu `Interrupted`; history Completed vẫn giữ nguyên.

Có thể chạy binary từ publish bằng `dotnet TruyTimDanChu.Server.dll --environment Production` với `ASPNETCORE_URLS`/certificate đã cấu hình. Khi dùng Windows Service, service account chỉ cần read/execute `Program Files\TruyTimDanChu\app` và read/write `ProgramData\TruyTimDanChu\data|logs|backup`; stop/start không xóa data. Rollback: stop service, đổi về thư mục binary version trước, chỉ restore DB backup khi schema tương thích, start và kiểm tra `/ready`/history.

Publish smoke tối thiểu: poll `/health` và `/ready`, GET `index.html`, kết nối `wss://<host>/hubs/room`, thử handshake đúng/sai version, tạo room/join, restart rồi xem history. Không coi publish thành công là Phase 40 PASS nếu chưa có hai máy client HTTPS, cache/version mismatch, full journey và biên bản 14 tiêu chí.
