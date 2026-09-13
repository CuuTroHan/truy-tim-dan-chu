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
