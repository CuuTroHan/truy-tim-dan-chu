# Multiplayer.LoadTests (Phase 39)

Đây là runner SignalR thật để đo handshake/join và vòng đo nhiều client. Build/run:

```powershell
dotnet run --project Tests/Multiplayer.LoadTests/Multiplayer.LoadTests.csproj -c Release -- --url=http://localhost:5000 --clients=20 --duration-seconds=120 --room-code=DC-XXXX
```

Chạy hai cấu hình bắt buộc 4×5 và 10×2 bằng fixture phòng tương ứng. JSON stdout ghi số client, thời lượng, lỗi và p50/p95/p99. CPU/RAM/bandwidth và browser render phải thu thêm trong Chrome/Performance Monitor; runner không tự ghi pass nếu chưa có các số đo đó.
