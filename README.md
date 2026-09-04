# SplitBill

Ứng dụng web chia tiền cho nhóm bạn trong các cuộc chơi có phát sinh chi phí (ăn uống, du lịch,
karaoke, thuê phòng...). Nhiều người cùng ứng tiền cho nhiều khoản chi khác nhau, mỗi khoản chia
theo quy tắc khác nhau — SplitBill tính ra danh sách lượt chuyển tiền tối thiểu để mọi người thu/chi
đúng số tiền của mình.

Đặc tả kỹ thuật đầy đủ (mô hình dữ liệu, luật nghiệp vụ, API, quyết định thiết kế) nằm ở
[`CLAUDE.md`](./CLAUDE.md) — đó mới là nguồn sự thật của dự án, file này chỉ là hướng dẫn chạy nhanh.

## Kiến trúc

```
SplitBill.sln
├── src/
│   ├── SplitBill.Domain/          # Entity, enum, exception nghiệp vụ — không phụ thuộc project nào
│   ├── SplitBill.Application/     # Service, DTO, validator, thuật toán chia tiền/settlement
│   ├── SplitBill.Infrastructure/  # EF Core DbContext, migrations, repository
│   ├── SplitBill.Api/             # ASP.NET Core Web API (REST, JWT)
│   └── SplitBill.Web/             # ASP.NET Core Razor Pages — gọi Api qua HTTP (BFF, không lộ JWT ra trình duyệt)
└── tests/
    ├── SplitBill.UnitTests/          # Thuật toán chia tiền + settlement (thuần, không cần DB)
    ├── SplitBill.IntegrationTests/   # Service layer đầy đủ, EF Core InMemory
    └── SplitBill.Web.Tests/          # SplitBillApiClient, form helpers, LoginModel
```

**Tech stack**: .NET 9 · ASP.NET Core Web API + Razor Pages · EF Core 9 (Code-First) · SQL Server ·
JWT Bearer + refresh-token rotation · FluentValidation · Serilog · Swashbuckle · xUnit + FluentAssertions.

## Yêu cầu

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- SQL Server — dùng SQL Server LocalDB (đi kèm Visual Studio trên Windows) hoặc bất kỳ instance SQL
  Server nào khác; xem [Docker](#chạy-bằng-docker) nếu muốn dùng container thay vì cài LocalDB.
- Công cụ EF Core CLI để chạy migration: `dotnet tool install --global dotnet-ef` (nếu chưa có).

## Chạy nhanh (local, không Docker)

### 1. Cấu hình JWT signing key (bắt buộc — Api fail-fast nếu thiếu)

```powershell
cd src/SplitBill.Api
dotnet user-secrets set "Jwt:SigningKey" "<chuỗi random ít nhất 32 ký tự>"
```

Không đặt giá trị thật vào `appsettings.json` — xem CLAUDE.md mục "Production-hardening" để biết lý do.

### 2. Tạo database + áp migration

Connection string mặc định trong `src/SplitBill.Api/appsettings.json` trỏ tới SQL Server LocalDB
(`(localdb)\MSSQLLocalDB`, database tên `SplitBill`). Đổi lại trong `appsettings.Development.json`
hoặc user-secrets nếu bạn dùng SQL Server khác.

```powershell
cd src/SplitBill.Api
dotnet ef database update --project ../SplitBill.Infrastructure --startup-project .
```

### 3. Chạy Api và Web (2 terminal riêng)

```powershell
# Terminal 1 — Api, mặc định http://localhost:5199
cd src/SplitBill.Api
dotnet run

# Terminal 2 — Web, mặc định http://localhost:5103
cd src/SplitBill.Web
dotnet run
```

Mở `http://localhost:5103`, đăng ký tài khoản mới rồi bắt đầu tạo nhóm.

> ⚠️ Nếu tự khởi động bằng `dotnet run --no-launch-profile` (bỏ qua `launchSettings.json`, ví dụ khi
> chạy từ script/CI), phải tự set `ASPNETCORE_ENVIRONMENT=Development` — nếu không, Web sẽ chạy ở chế
> độ Production và phục vụ CSS/JS **rỗng** (do `MapStaticAssets` của .NET 9 chỉ có asset nén sẵn sau
> khi `dotnet publish`, không phải sau `dotnet build`). Chi tiết xem CLAUDE.md mục 10b.

Swagger UI cho Api (chỉ bật ở Development): `http://localhost:5199/swagger`.

### 4. Chạy test

```powershell
dotnet test SplitBill.sln
```

100 test (33 unit + 50 integration + 17 web), toàn bộ chạy trên EF Core InMemory — không cần SQL
Server thật để chạy test.

## Chạy bằng Docker

```powershell
docker compose up --build
```

Lệnh trên khởi động 3 container: SQL Server, Api (áp migration tự động lúc container start), Web —
xem chi tiết ở [`docker-compose.yml`](./docker-compose.yml). Mặc định mở `http://localhost:5103`.
JWT signing key trong Docker được đặt qua biến môi trường `Jwt__SigningKey` trong `docker-compose.yml`
— **đổi giá trị đó trước khi dùng ngoài máy cá nhân**, giá trị mẫu chỉ để chạy thử nhanh.

## Tài khoản & dữ liệu mẫu

Không có seed data sẵn — đăng ký tài khoản qua `/Account/Register`, tạo nhóm, rồi thêm thành viên
(có tài khoản hoặc khách vãng lai) để bắt đầu dùng thử.

## Giới hạn đã biết

Xem mục "Giới hạn đã biết" trong `CLAUDE.md` — ví dụ: form Edit không khôi phục chính xác trọng số/%
gốc đã nhập lúc tạo khoản chi, ràng buộc mềm cho settlement (mục 6.4) chưa làm, chưa hỗ trợ đa tiền tệ.
