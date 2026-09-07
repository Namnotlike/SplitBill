# SplitBill — Đặc tả kỹ thuật cho Claude Code

> File này là nguồn sự thật duy nhất cho dự án. Đọc toàn bộ trước khi viết code.
> Khi có mâu thuẫn giữa file này và code hiện có, ưu tiên file này và báo lại cho người dùng.

---

## 1. Tổng quan

**SplitBill** là ứng dụng web chia tiền cho nhóm bạn trong các cuộc chơi có phát sinh chi phí (ăn uống, du lịch, karaoke, thuê phòng...).

Bài toán cốt lõi: nhiều người cùng ứng tiền cho nhiều khoản chi khác nhau, mỗi khoản chia theo quy tắc khác nhau. Cuối cuộc chơi, hệ thống phải tính ra **danh sách lượt chuyển tiền tối thiểu** sao cho mọi người đều thu/chi đúng đúng số tiền của mình. Người này có thể chuyển thẳng cho người kia, không bắt buộc phải chuyển qua người tạo nhóm.

### Nguyên tắc bất di bất dịch

1. **Mọi số tiền là số nguyên**, đơn vị nhỏ nhất của tiền tệ (VND = đồng). Kiểu `long`. Tuyệt đối không dùng `double`/`float` cho tiền.
2. **Không bao giờ lưu số dư (balance) vào DB.** Số dư luôn được tính lại từ danh sách `Expense` + `Settlement`. Điều này đảm bảo sửa một hóa đơn cũ vẫn ra kết quả đúng.
3. **Không xóa cứng dữ liệu tài chính.** Chỉ soft delete + ghi audit log.
4. Tách bạch **"ai ứng tiền"** (`payers`) và **"ai chịu tiền"** (`splits`). Đây là điểm mấu chốt của toàn bộ thiết kế.

---

## 2. Tech stack

| Thành phần | Lựa chọn |
|---|---|
| Runtime | .NET 9 |
| Web | ASP.NET Core Web API (Controllers, không dùng Minimal API) |
| ORM | EF Core 9, Code-First + Migrations |
| DB | SQL Server 2019+ |
| Auth | JWT Bearer + share-link token cho khách chưa đăng ký |
| Validation | FluentValidation |
| Mapping | Không dùng AutoMapper. Viết extension method `ToDto()` thủ công. |
| Test | xUnit + FluentAssertions + EF Core InMemory (unit) / Testcontainers hoặc LocalDB (integration) |
| Log | Serilog |
| API docs | Swashbuckle (Swagger) |
| Password hashing | `Microsoft.AspNetCore.Identity.PasswordHasher<User>` (chỉ dùng riêng class hasher, KHÔNG cài toàn bộ ASP.NET Core Identity/EF Identity) |
| Gửi email | MailKit (bổ sung 2026-09-05, quyết định người dùng — xem mục 13.3) |

Không thêm thư viện NuGet nào ngoài danh sách trên nếu chưa hỏi người dùng.

**Cấu hình JWT (chốt cụ thể, tránh mỗi lần code lại đoán):**
- Access token: thời hạn 30 phút, ký HS256, claim tối thiểu `sub` (UserId), `email`.
- Refresh token: chuỗi random 256-bit, thời hạn 14 ngày, lưu **hash** (SHA-256) trong bảng `RefreshToken` (xem 4.4), không lưu plaintext. Mỗi lần refresh thì thu hồi token cũ, phát token mới (rotation).
- Guest (khách vãng lai, `GroupMember.UserId == null`) không đăng nhập được — họ chỉ tồn tại trong phạm vi 1 nhóm và mọi thao tác ghi (tạo expense, ghi settlement...) phải do một `User` đã đăng nhập thực hiện thay mặt. Đây là giới hạn có chủ đích của MVP, xem thêm 4.4.

---

## 3. Cấu trúc solution

```
SplitBill.sln
├── src/
│   ├── SplitBill.Domain/          # Entity, enum, value object, exception nghiệp vụ. KHÔNG tham chiếu project nào.
│   ├── SplitBill.Application/     # Service, DTO, validator, thuật toán settlement. Tham chiếu Domain.
│   ├── SplitBill.Infrastructure/  # DbContext, EF config, Migrations, Repository. Tham chiếu Application.
│   └── SplitBill.Api/             # Controller, DI, middleware, auth. Tham chiếu Infrastructure.
└── tests/
    ├── SplitBill.UnitTests/       # Trọng tâm: thuật toán chia tiền + settlement
    └── SplitBill.IntegrationTests/
```

Thuật toán settlement phải nằm ở `SplitBill.Application/Settlement/` dưới dạng class thuần, **không phụ thuộc EF Core hay DbContext**, để test dễ dàng.

---

## 4. Mô hình dữ liệu

### 4.1 Entities

```csharp
// ===== Người dùng =====
User {
    Guid Id
    string? Email              // null nếu là khách chưa đăng ký
    string? PasswordHash
    string DisplayName
    string? BankAccountNumber  // dùng sinh VietQR
    string? BankBin            // mã BIN ngân hàng, dùng sinh VietQR
    DateTimeOffset CreatedAt
}

// ===== Nhóm / Cuộc chơi =====
Group {
    Guid Id
    string Name
    string? Description
    GroupType Type             // OneTime | Recurring
    string Currency            // "VND" mặc định
    Guid CreatedByUserId
    string ShareToken          // chuỗi random 22 ký tự, dùng cho link chia sẻ
    bool SimplifyDebts         // bật/tắt tối ưu gộp nợ, mặc định true
    bool IsArchived
    bool IsDeleted
    DateTimeOffset CreatedAt
}

// ===== Thành viên trong nhóm =====
GroupMember {
    Guid Id
    Guid GroupId
    Guid? UserId               // null = khách vãng lai, chỉ có tên
    string DisplayName         // snapshot tên tại thời điểm thêm vào nhóm
    GroupMemberRole Role       // Owner | Member — xem 4.4
    bool IsActive              // false = đã rời nhóm (vẫn giữ lịch sử)
    DateTimeOffset JoinedAt
}

// ===== Refresh token (đăng nhập) =====
RefreshToken {
    Guid Id
    Guid UserId
    string TokenHash           // SHA-256 của refresh token, KHÔNG lưu plaintext
    DateTimeOffset ExpiresAt
    DateTimeOffset? RevokedAt
    DateTimeOffset CreatedAt
}
```

> **Quan trọng:** mọi tham chiếu tới "người" trong `Expense`, `ExpensePayer`, `ExpenseSplit`, `Settlement` đều dùng **`GroupMemberId`**, KHÔNG dùng `UserId`. Lý do: cho phép khách chưa có tài khoản tham gia chia tiền.

```csharp
// ===== Khoản chi =====
Expense {
    Guid Id
    Guid GroupId
    string Title
    long TotalAmount           // đơn vị đồng, > 0
    long ExtraFeeAmount        // VAT/tip/phụ phí, >= 0, phân bổ theo tỉ lệ phần gốc
    SplitMode SplitMode
    string? SplitConfigJson    // lưu input gốc của người dùng (shares, %, itemized...)
    string? Note
    string? ReceiptImageUrl    // URL nội bộ dạng "/api/v1/expenses/{id}/receipt-image", KHÔNG phải
                                // đường dẫn ổ đĩa hay cloud storage — ảnh thật lưu ở bảng ReceiptImage (xem dưới)
    DateTimeOffset OccurredAt
    Guid CreatedByMemberId
    bool IsDeleted
    DateTimeOffset CreatedAt
    DateTimeOffset UpdatedAt
    byte[] RowVersion          // optimistic concurrency, xem 4.4

    ICollection<ExpensePayer> Payers
    ICollection<ExpenseSplit> Splits
}

// Ai đã móc tiền ra
ExpensePayer {
    Guid Id
    Guid ExpenseId
    Guid GroupMemberId
    long Amount                // > 0
}

// Ai phải gánh bao nhiêu
ExpenseSplit {
    Guid Id
    Guid ExpenseId
    Guid GroupMemberId
    long Amount                // có thể = 0 (người bị loại trừ khỏi khoản này)
}

// ===== Ảnh hóa đơn — lưu thẳng trong SQL Server (byte[]/varbinary(max)), quyết định của người
// dùng ngày 2026-09-03: KHÔNG dùng local disk hay cloud storage riêng =====
ReceiptImage {
    Guid Id
    Guid ExpenseId             // unique — mỗi expense chỉ giữ 1 ảnh gần nhất, upload lại thì ghi đè
    byte[] Content              // varbinary(max)
    string ContentType         // "image/jpeg" | "image/png" | "image/webp"
    string FileName
    long SizeBytes
    DateTimeOffset CreatedAt
}

// ===== Thanh toán thực tế đã diễn ra =====
Settlement {
    Guid Id
    Guid GroupId
    Guid FromMemberId
    Guid ToMemberId
    long Amount                // > 0
    SettlementStatus Status    // Pending | Confirmed | Rejected
    Guid RecordedByMemberId
    Guid? ConfirmedByMemberId
    DateTimeOffset? ConfirmedAt
    string? Note
    bool IsDeleted
    DateTimeOffset CreatedAt
    byte[] RowVersion          // optimistic concurrency, xem 4.4
}

// ===== Nhật ký thay đổi =====
AuditLog {
    Guid Id
    Guid GroupId
    string EntityType          // "Expense" | "Settlement" | "GroupMember" | "Group"
    Guid EntityId
    string Action              // "Created" | "Updated" | "Deleted" | "Restored"
    Guid ActorMemberId
    string? BeforeJson
    string? AfterJson
    DateTimeOffset CreatedAt
}
```

### 4.2 Enums

```csharp
enum GroupType        { OneTime = 0, Recurring = 1 }
enum GroupMemberRole  { Member = 0, Owner = 1 }
enum SettlementStatus { Pending = 0, Confirmed = 1, Rejected = 2 }
enum SplitMode {
    Equal       = 0,  // chia đều
    Shares      = 1,  // theo phần: 1, 1, 2, 0.5
    Percentage  = 2,  // theo %
    ExactAmount = 3,  // nhập tay từng người
    Itemized    = 4,  // gán theo món
}
```

### 4.3 Cấu hình EF Core

- Tiền: cột `BIGINT NOT NULL`.
- `Group.ShareToken`: unique index.
- `GroupMember`: index `(GroupId, UserId)`.
- `Expense`: index `(GroupId, IsDeleted, OccurredAt DESC)`.
- Global query filter cho `IsDeleted == false` trên `Expense`, `Settlement`, `Group`. Khi cần xem lịch sử thì dùng `.IgnoreQueryFilters()`.
- Tất cả FK: `DeleteBehavior.Restrict`. Không cascade delete dữ liệu tài chính.
  > ⚠️ **Hệ quả cần biết** (phát hiện qua test tự động 2026-09-03): với `DeleteBehavior.Restrict`,
  > khi sửa `Expense` và cần thay toàn bộ `Payers`/`Splits`, KHÔNG được chỉ dựa vào
  > navigation-collection (`Clear()`/`Add()` hay gán lại `expense.Payers = newList`) — cả hai cách
  > đều từng ném lỗi lúc `SaveChanges()` (`InvalidOperationException` "relationship severed", rồi
  > `DbUpdateConcurrencyException` "entity does not exist in the store") vì EF Core suy luận sai
  > Insert/Update/Delete khi đổi cả graph qua navigation trên entity cha đã tracked. Cách đúng, đã
  > verify bằng test: xóa và thêm **tường minh qua `DbSet`** (`ExpensePayers.RemoveRange(...)` +
  > `ExpensePayers.AddRange(...)`, xem `IExpenseRepository.RemovePayers/AddPayers`), rồi mới gán
  > navigation collection bằng CÙNG list reference đó (chỉ để tiện map DTO, không phục vụ tracking).
  > Áp dụng quy tắc này cho mọi collection con dùng FK Restrict tương tự sau này.
- `ExpensePayer` và `ExpenseSplit` cấu hình là owned collection hoặc entity riêng đều được, nhưng phải load cùng `Expense` bằng `.Include()`.
- `GroupMember`: unique index `(GroupId, UserId)` **khi `UserId IS NOT NULL`** (filtered unique index) — một user không được là 2 member cùng lúc trong 1 nhóm. Khách vãng lai (`UserId == null`) không bị ràng buộc này.
- `ExpensePayer`, `ExpenseSplit`: unique index `(ExpenseId, GroupMemberId)` — chặn trùng ở tầng DB, không chỉ dựa vào validate ở Application.
- `User.Email`: unique index, **filtered** `WHERE Email IS NOT NULL` (khách vãng lai không có email nên không được tính vào ràng buộc unique).
- `RefreshToken.TokenHash`: unique index; index thêm `(UserId, RevokedAt)` để truy vấn token còn hiệu lực nhanh.
- `Expense.RowVersion` và `Settlement.RowVersion`: cấu hình `IsRowVersion()` (SQL Server `rowversion`/`timestamp`). Khi `PUT /expenses/{id}` hoặc các thao tác ghi vào `Settlement` gặp `DbUpdateConcurrencyException` → trả HTTP 409 với `errorCode = "CONCURRENCY_CONFLICT"`.

### 4.4 Phân quyền theo `GroupMemberRole`

Mỗi `GroupMember` có `Role`. Người tạo nhóm (`Group.CreatedByUserId`) luôn được gán `Role = Owner` cho `GroupMember` tương ứng của họ; mọi thành viên khác mặc định `Role = Member`. Một nhóm luôn có **ít nhất 1 Owner tại mọi thời điểm** — không cho phép hạ quyền hoặc xóa Owner cuối cùng.

| Hành động | Owner | Member |
|---|---|---|
| Tạo/sửa/xóa Expense, Settlement | ✅ | ✅ |
| Thêm thành viên mới | ✅ | ✅ |
| Đổi tên hiển thị của **chính mình** | ✅ | ✅ |
| Đổi tên hiển thị của **người khác** | ✅ | ❌ |
| Rời nhóm (tự đánh dấu `IsActive = false`) — chỉ khi `net == 0` | ✅ (nếu còn Owner khác) | ✅ |
| Đánh dấu `IsActive = false` cho **người khác** | ✅ | ❌ |
| `PATCH /groups/{id}` (đổi tên nhóm, `SimplifyDebts`, archive) | ✅ | ❌ |
| `DELETE /groups/{id}` (soft delete nhóm) | ✅ | ❌ |
| `POST /groups/{id}/share-token/rotate` | ✅ | ❌ |
| Gán/thu hồi quyền Owner cho member khác | ✅ | ❌ |

Vi phạm trả lỗi `403` với `errorCode = "INSUFFICIENT_ROLE"`. Có thể có nhiều Owner trong 1 nhóm (ví dụ nhóm du lịch nhiều người đồng tổ chức); thêm endpoint `POST /groups/{id}/members/{memberId}/role` (body `{ "role": "Owner" | "Member" }`, chỉ Owner gọi được) để chuyển nhượng.

---

## 5. Luật nghiệp vụ chia tiền

### 5.1 Quy trình tính `Splits` từ input

Service `IExpenseSplitCalculator` nhận `TotalAmount`, `ExtraFeeAmount`, `SplitMode`, `SplitConfig` → trả về `List<(Guid MemberId, long Amount)>`.

| Mode | Input | Cách tính |
|---|---|---|
| `Equal` | danh sách memberId | chia đều `TotalAmount` |
| `Shares` | memberId → weight (decimal, > 0) | `amount_i = Total * w_i / Σw` |
| `Percentage` | memberId → phần trăm | `amount_i = Total * p_i / 100`. Cảnh báo nếu `Σp ≠ 100` nhưng vẫn cho lưu. |
| `ExactAmount` | memberId → số tiền | dùng nguyên. Cảnh báo nếu `Σ ≠ Total`. |
| `Itemized` | danh sách item {tên, giá, danh sách người ăn} | mỗi item chia đều cho người được gán, rồi cộng dồn theo người |

`ExtraFeeAmount` luôn được phân bổ **theo tỉ lệ phần gốc** của từng người, tính sau cùng, và cũng áp dụng luật làm tròn bên dưới.

**Pipeline tính toán bắt buộc theo đúng thứ tự (không được đảo):**

```
1. Tính split gốc theo SplitMode/SplitConfig trên TotalAmount (bảng trên + luật làm tròn 5.2).
2. Phân bổ ExtraFeeAmount theo tỉ lệ split gốc của bước 1 (một lượt làm tròn riêng, largest
   remainder, tie-break theo GroupMemberId ordinal — KHÔNG round-robin dù SplitMode là Equal,
   vì round-robin chỉ áp dụng cho phần gốc). Cộng vào split gốc → ra "split đã lưu" (ExpenseSplit.Amount).
3. So sánh Σpayers.Amount với Σ ExpenseSplit.Amount (đã gồm fee) để tính delta theo 5.3.
4. Delta CHỈ dùng khi tính balance (BalanceCalculator), KHÔNG ghi đè lại ExpenseSplit trong DB.
```

Kết quả của bước 1–2 mới là dữ liệu được lưu vào bảng `ExpenseSplit`.

### 5.2 Luật làm tròn (BẮT BUỘC tuân thủ chính xác)

Dùng **largest remainder method** với tie-break xác định:

```
1. Tính phần nguyên: base_i = floor(Total * w_i / Σw)
2. remainder = Total - Σ base_i          // luôn 0 <= remainder < số người
3. Tính phần dư thập phân của từng người, sắp xếp giảm dần.
   Tie-break: so sánh GroupMemberId.ToString() theo thứ tự ordinal, nhỏ hơn đứng trước.
4. Cộng thêm 1 đơn vị cho `remainder` người đầu tiên trong danh sách đã sắp xếp.
```

Với `SplitMode.Equal`, thay bước 3 bằng **round-robin xoay vòng**: người bắt đầu nhận phần dư được xác định bởi `startIndex = |ExpenseId.GetHashCode()| % memberCount` — mục đích là phần lẻ không luôn rơi vào cùng một người qua nhiều hóa đơn. Việc sắp xếp member phải theo `GroupMemberId` ordinal để kết quả xác định (deterministic) và test được.

Kết quả bắt buộc: `Σ splits == TotalAmount + ExtraFeeAmount` với mọi mode trừ `ExactAmount` và `Percentage`.

### 5.3 Xử lý lệch tổng

Người dùng **được phép** lưu khoản chi mà `Σ splits ≠ Σ payers`. Nhưng để toán học nhất quán (`Σ net` toàn nhóm phải bằng 0), xử lý như sau:

```
delta = Σ payers.Amount - Σ splits.Amount

if (delta != 0):
    - API trả về warning code "SPLIT_TOTAL_MISMATCH" kèm giá trị delta, KHÔNG chặn lưu.
    - Khi tính balance, phần delta được cộng vào split của (các) người ứng tiền,
      chia theo tỉ lệ số tiền họ đã ứng, dùng đúng luật làm tròn ở 5.2.
    - UI hiện cảnh báo vàng: "Lệch {delta}đ so với tổng hóa đơn — phần chênh do người ứng tiền chịu."
```

Cách này giữ được sự tự do cho người dùng mà không phá vỡ bất biến `Σ net = 0`.

**Quan trọng — delta là điều chỉnh ẢO, không phải dữ liệu lưu trữ:** `BalanceCalculator` áp dụng delta **tại thời điểm tính balance**, không bao giờ ghi đè `ExpenseSplit` trong DB. Vì vậy màn hình chi tiết hóa đơn (hiển thị `ExpenseSplit` gốc) và `/balances` có thể lệch nhau về tổng — đây là chủ đích, không phải bug. UI chi tiết hóa đơn phải tự hiển thị dòng cảnh báo `SPLIT_TOTAL_MISMATCH` bên cạnh, không được "sửa" số liệu Split để khớp.

**Trường hợp payer không có mặt trong `Splits`** (ví dụ A trả tiền hộ toàn bộ nhưng không ăn, nên không có `ExpenseSplit` nào của A): khi phân bổ delta cho A, `BalanceCalculator` coi A có split ảo bằng 0 làm điểm xuất phát rồi cộng phần delta được chia theo tỉ lệ — không cần và không được tạo record `ExpenseSplit` mới trong DB cho trường hợp này.

### 5.4 Validation

- `TotalAmount > 0`.
- `Payers` không rỗng, mọi `Amount > 0`, không trùng `GroupMemberId`.
- `Splits` không rỗng, mọi `Amount >= 0`, không trùng `GroupMemberId`.
- Mọi `GroupMemberId` phải thuộc `GroupId` của expense.
- `Settlement.FromMemberId != ToMemberId`.
- Không cho phép `Σ payers.Amount == 0`.

---

## 6. Thuật toán tối ưu lượt chuyển tiền

Namespace: `SplitBill.Application.Settlement`

### 6.1 Bước 1 — tính số dư ròng

```csharp
public sealed record MemberBalance(Guid MemberId, long Net);

// net > 0 : chủ nợ, người khác phải trả cho họ
// net < 0 : con nợ
// Bất biến: Σ net == 0
```

Công thức cho mỗi thành viên `m`:

```
net[m] =   Σ (số tiền m đã ứng trong mọi expense chưa xóa)
         - Σ (số tiền m phải chịu, đã bao gồm phân bổ delta ở 5.3)
         + Σ (settlement Confirmed mà m là From)
         - Σ (settlement Confirmed mà m là To)
```

> ⚠️ **Sửa lỗi so với bản nháp trước:** dấu của From/To bị ngược. `FromMemberId` là người CHUYỂN
> tiền (con nợ trả nợ), `ToMemberId` là người NHẬN tiền (chủ nợ được trả) — khớp với `POST
> /settlements/{id}/confirm` ở mục 8 ("Người NHẬN xác nhận" = ToMemberId xác nhận đã nhận). Khi một
> settlement Confirmed được ghi nhận, nợ của From phải giảm (net tiến về 0 từ phía âm → **cộng**
> Amount vào net[From]) và phần được nhận của To cũng giảm tương ứng (net tiến về 0 từ phía dương →
> **trừ** Amount khỏi net[To]). Nếu làm ngược lại (trừ From, cộng To) thì một settlement hợp lệ sẽ
> đẩy số dư ra xa 0 hơn thay vì về 0 — sai hoàn toàn về nghiệp vụ.

Settlement ở trạng thái `Pending` **không** được tính vào balance. Cần viết unit test khẳng định `Σ net == 0` với dữ liệu ngẫu nhiên.

### 6.2 Bước 2 — Greedy (mặc định, luôn có)

Class `GreedySettlementSolver : ISettlementSolver`. Đảm bảo tối đa `n-1` giao dịch, độ phức tạp `O(n log n)`.

```
1. Tách thành 2 danh sách: debtors (net < 0, lấy trị tuyệt đối) và creditors (net > 0).
2. Sắp xếp cả hai giảm dần theo số tiền.
   Tie-break: GroupMemberId ordinal tăng dần (để kết quả deterministic).
3. Lặp: lấy debtor lớn nhất và creditor lớn nhất còn lại.
   amount = min(debtor.Remaining, creditor.Remaining)
   Ghi nhận giao dịch debtor -> creditor, số tiền = amount.
   Trừ amount khỏi cả hai. Ai về 0 thì bỏ khỏi danh sách.
4. Dừng khi một trong hai danh sách rỗng.
```

### 6.3 Bước 3 — Tối ưu tuyệt đối bằng DP bitmask

Class `OptimalSettlementSolver : ISettlementSolver`.

Số giao dịch tối thiểu thật sự là `n - (số nhóm con rời nhau có tổng bằng 0)`. Đây là bài toán NP-hard (quy về Partition), nhưng nhóm thực tế nhỏ nên xử lý được:

```
Gọi n = số người có net != 0.
sum[mask] = tổng net của các người trong mask.
zero[mask] = (sum[mask] == 0)

dp[mask] = số nhóm con tổng-0 nhiều nhất có thể tách được từ mask (phần tử không nằm trong
           nhóm tổng-0 nào coi là "leftover", không đóng góp vào dp nhưng vẫn hợp lệ)
dp[0] = 0
dp[mask] = max(
    dp[mask \ lowBit(mask)],                          // bit thấp nhất của mask KHÔNG tách vào nhóm nào
    max over mọi tập con sub của mask, sub chứa lowBit(mask), zero[sub] == true:
        dp[mask \ sub] + 1                             // tách sub thành 1 nhóm tổng-0
)

Số giao dịch tối thiểu = n - dp[fullMask]
```

> ⚠️ **Sửa lỗi so với bản nháp trước:** nhánh `dp[mask \ lowBit(mask)]` là bắt buộc, không được bỏ qua.
> Nếu chỉ xét nhánh "tách sub chứa lowBit" mà thiếu nhánh "để lowBit làm leftover", DP sẽ cho kết quả
> sai trong trường hợp phần tử ở bit thấp nhất không thể ghép vào bất kỳ nhóm tổng-0 nào, nhưng các
> phần tử còn lại trong mask vẫn ghép được với nhau (ví dụ mask = {A:+5, B:+3, C:-3} — A không ghép
> tổng-0 với ai, nhưng {B,C} vẫn là 1 nhóm tổng-0 hợp lệ; thiếu nhánh trên sẽ làm dp[mask] = 0 thay vì 1).

Duyệt tập con bằng kỹ thuật `for (sub = mask; sub > 0; sub = (sub - 1) & mask)` → tổng `O(3^n)`.

Sau khi biết cách phân hoạch tối ưu, chạy greedy **trong từng nhóm con** để sinh ra danh sách giao dịch cụ thể.

**Quy tắc chọn solver:**

```csharp
ISettlementSolver Choose(int nonZeroMemberCount) =>
    nonZeroMemberCount <= 15 ? new OptimalSettlementSolver()
                             : new GreedySettlementSolver();
```

Bọc `OptimalSettlementSolver` trong timeout 2 giây; nếu quá thì fallback về greedy.

### 6.4 Bước 4 — Ràng buộc mềm (tie-break xã hội)

Khi có nhiều phương án cùng số giao dịch tối thiểu, ưu tiên theo thứ tự:

1. Cặp (from, to) đã từng có `Settlement` với nhau trong nhóm này → điểm cao hơn.
2. Tránh để một người phải thực hiện quá 3 lượt chuyển đi.
3. Ưu tiên số tiền tròn hơn (chia hết cho 1000đ) khi chênh lệch không đáng kể.

Triển khai bằng `ISettlementRanker` chấm điểm và chọn phương án tốt nhất.

> ✅ **Đã triển khai (2026-09-05)**, không còn là "milestone sau" nữa. Kiến trúc thực tế:
> - `SocialSettlementPlanner.Plan(balances, pastPairs)` (namespace `SplitBill.Application.Settlement`,
>   class thuần không phụ thuộc EF Core) sinh 3 phương án ứng viên bằng 3
>   `SettlementTieBreakStrategy` khác nhau — `OrdinalAscending` (baseline, hành vi gốc mục 6.2 không
>   đổi), `PastPairAffinity` (nhắm tiêu chí 1), `LoadBalancing` (nhắm tiêu chí 2) — rồi gọi
>   `ISettlementRanker.SelectBest(...)` chọn phương án tốt nhất.
> - `SocialSettlementRanker : ISettlementRanker` chấm điểm lexicographic đúng thứ tự 3 tiêu chí trên;
>   tiêu chí 2 đo bằng tổng phần vượt ngưỡng 3 lượt chuyển (không phải cờ nhị phân), tiêu chí 3 chỉ
>   thật sự quyết định khi 2 tiêu chí đầu hòa (đúng tinh thần "chênh lệch không đáng kể").
> - `GreedySettlementSolver` nhận thêm `SettlementTieBreakStrategy` — khi có HÒA ĐIỂM thật (nhiều
>   debtor/creditor cùng Remaining lớn nhất), thay vì luôn lấy phần tử đầu theo ordinal, re-chọn trong
>   đúng nhóm hòa đó theo strategy. Nếu không có hòa điểm nào, mọi strategy cho kết quả giống hệt nhau
>   (đã có test xác nhận) — đây là bản chất của "ràng buộc mềm": chỉ có tác dụng KHI có nhiều phương án
>   ngang nhau thật sự, không bao giờ làm tăng số giao dịch.
>
> ⚠️ **Lỗi thật phát hiện lúc viết test (quan trọng, dễ tái phạm nếu sửa lại thuật toán này sau này):**
> `OptimalSettlementSolver`'s DP bitmask (mục 6.3) LUÔN bắt buộc mỗi mask chỉ xét các subset chứa đúng
> bit thấp nhất ("lowBit") của mask đó (kỹ thuật chuẩn để tránh trùng lặp trong bitmask DP). Lần cài
> đặt đầu tiên chỉ so sánh điểm past-pair CỦA RIÊNG subset đang xét ở mỗi mức — nhưng nếu thành viên có
> cặp quen thuộc KHÔNG PHẢI là lowBit ở mức đang xét, tie-break không bao giờ có cơ hội tác động (vì
> thuật toán về cấu trúc không xét đến các subset không chứa lowBit), khiến kết quả **flaky ngẫu nhiên
> 50/50** tùy thứ tự ordinal của GUID sinh ra mỗi lần chạy — phát hiện qua
> `GetSettlementPlanAsync_WithTieAndPastSettlement_RoutesThroughPastPair` (IntegrationTests) thất bại
> ngẫu nhiên dù `SocialSettlementPlannerTests` (UnitTests, chạy trước) lại "tình cờ" pass do may mắn về
> thứ tự GUID trong lần chạy đó — verify bằng cách chạy lại filter test 20 lần liên tiếp, thấy fail rate
> ~40%. Sửa đúng: DP phải dùng **2 khóa lexicographic** — `dpCount[mask]` (số nhóm tổng-0, khóa chính,
> không đổi) VÀ `dpScore[mask]` (tổng điểm past-pair CỘNG DỒN qua đệ quy — `dpScore[mask^sub] +
> điểm(sub)`, không phải chỉ điểm của riêng `sub`). Khóa phụ phải lan truyền qua đệ quy để "nhìn xa"
> được: chọn nhóm cho lowBit hiện tại có thể ảnh hưởng tới việc phần còn lại (xử lý ở mức đệ quy sâu
> hơn) có ghép được cặp quen thuộc hay không. Sau khi sửa, chạy lại filter test 20/20 lần đều pass.
> Khi `pastPairs` rỗng (mặc định), `dpScore` luôn bằng 0 ở mọi mask nên tie-break không bao giờ kích
> hoạt — hành vi DP giống hệt bản gốc, không ảnh hưởng gì tới các test S1–S7/S3b đã có từ trước.

### 6.5 Chế độ không tối ưu

Khi `Group.SimplifyDebts == false`, **không** gộp nợ. Thay vào đó sinh giao dịch trực tiếp theo từng expense: với mỗi expense, mỗi người trong `splits` nợ (các) người trong `payers` theo tỉ lệ số tiền đã ứng. Sau đó chỉ gộp các cặp trùng (from, to) lại với nhau.

Lý do tồn tại chế độ này: nhiều người khó chịu khi app bảo họ nợ một người mà họ không hề ăn chung.

### 6.6 Settlement đã Confirmed ảnh hưởng thế nào khi `SimplifyDebts = false`

`net[m]` ở 6.1 luôn tính đúng theo settlement đã Confirmed, bất kể `SimplifyDebts` bật hay tắt — đây là số dư "thật". Vấn đề chỉ nằm ở cách hiển thị **breakdown theo từng expense** ở chế độ không gộp nợ, vì một `Settlement` không gắn với expense cụ thể nào.

Quy tắc: khi hiển thị `/settlement-plan` với `SimplifyDebts = false`, tính danh sách nợ thô theo từng expense như 6.5 (gọi tổng nợ thô từ X sang Y là `raw[X,Y]`), sau đó **co giãn tỉ lệ (proportional scale-down)** theo phần đã thực trả:

```
đã trả (X → Y) = Σ Settlement.Amount đã Confirmed có FromMemberId=X, ToMemberId=Y
còn lại[X,Y] = raw[X,Y] - đã trả(X → Y)   // có thể âm nếu trả dư — dồn phần dư sang net tổng, không hiển thị số âm
```

Đây là hiển thị mang tính tham khảo cho người dùng biết "khoản nào liên quan tới ai", **không phải sổ cái chính xác tuyệt đối theo từng expense** — số dư đúng để quyết toán vẫn luôn là `net[m]` ở 6.1. Nếu `Σ còn lại[X,Y]` theo cặp không khớp tuyệt đối với `net`, làm tròn chênh lệch về phía cặp có `raw` lớn nhất, dùng luật 5.2.

---

## 7. Test cases bắt buộc

Viết trước ở `SplitBill.UnitTests`. Code phải chạy đúng 100% các case sau.

### 7.1 Làm tròn

| Case | Input | Kết quả mong đợi |
|---|---|---|
| R1 | 100.000đ chia đều 3 người | 33.334 / 33.333 / 33.333, tổng = 100.000 |
| R2 | 10đ chia đều 3 người | 4 / 3 / 3 |
| R3 | 1đ chia đều 5 người | 1 / 0 / 0 / 0 / 0 |
| R4 | 100.000đ theo shares 1:1:2 | 25.000 / 25.000 / 50.000 |
| R5 | 100.000đ chia đều 3 người (SplitMode.Equal), gọi 2 lần với ExpenseId khác nhau | người nhận phần dư phải khác nhau. ⚠️ Sửa so với bản nháp trước: PHẢI dùng `SplitMode.Equal`, không phải `Shares`. Round-robin theo ExpenseId (mục 5.2) chỉ áp dụng cho `Equal` — `Shares` (kể cả khi trọng số bằng nhau 1:1:1) luôn dùng largest remainder với tie-break cố định theo GroupMemberId ordinal, nên gọi 2 lần với ExpenseId khác nhau sẽ ra CÙNG một người nhận phần dư, không phải khác nhau. |
| R6 | Total 300.000 + ExtraFee 30.000, chia đều 4 | tổng splits = 330.000 |

### 7.2 Số dư ròng

| Case | Kịch bản | Kết quả |
|---|---|---|
| B1 | A ứng 300k, chia đều A/B/C | A: +200k, B: -100k, C: -100k |
| B2 | A ứng 200k và B ứng 100k, chia đều A/B/C | A: +100k, B: 0, C: -100k |
| B3 | A ứng 100k, splits chỉ ghi 80k (lệch 20k) | delta 20k cộng vào split của A → Σ net = 0 |
| B4 | Có settlement Pending | balance KHÔNG đổi |
| B5 | Expense bị soft delete | không tính vào balance |
| B6 | Random 50 expense, 8 người | `Σ net == 0` tuyệt đối |

### 7.3 Thuật toán settlement

| Case | net | Số giao dịch tối thiểu | Ghi chú |
|---|---|---|---|
| S1 | A:+100, B:-100 | 1 | ca đơn giản |
| S2 | A:+100, B:-50, C:-50 | 2 | |
| S3 | A:+50, B:-50, C:+30, D:-30 | 2 | ⚠️ mô phỏng tay cho thấy Greedy ở 6.2 (sort giảm dần, ghép lớn nhất-lớn nhất) đã ra đúng 2 ngay, KHÔNG cần Optimal. Ghi chú "greedy naive dễ ra 3" trong bản gốc là sai — case này không phân biệt được Greedy vs Optimal. Giữ lại làm test cơ bản cho tách nhóm, nhưng KHÔNG dùng case này để chứng minh Optimal tốt hơn Greedy. |
| S4 | A:+40, B:+30, C:-70, D:+20, E:-20 | 3 | tách được nhóm {D,E}. ⚠️ mô phỏng tay cũng cho Greedy ra 3 (bằng Optimal) — case này cũng không thực sự bẫy được Greedy sai. |
| S3b | (CẦN BỔ SUNG) một bộ net thực sự khiến Greedy 6.2 ra nhiều giao dịch hơn Optimal (ví dụ dạng A:+4,B:+3,C:-1,D:-2,E:-4 hoặc tương tự — phải verify bằng cách chạy thử cả hai thuật toán, không suy luận tay) | phải nhỏ hơn số Greedy cho ra | Bắt buộc có ít nhất 1 case dạng này, nếu không `OptimalSettlementSolver` không có test nào chứng minh nó cần thiết so với Greedy |
| S5 | tất cả net = 0 | 0 | không sinh giao dịch nào |
| S6 | 12 người net ngẫu nhiên tổng 0 | — | kiểm tra: sau khi áp dụng mọi giao dịch, net mọi người = 0; và số giao dịch <= n-1 |
| S7 | 30 người | — | phải fallback greedy, chạy < 100ms |

**Invariant test bắt buộc cho mọi kết quả settlement:**
- Mọi `amount > 0`.
- Không có giao dịch tự chuyển cho chính mình.
- Áp dụng toàn bộ giao dịch vào net → mọi người về 0.
- Số giao dịch `<= n - 1`.

---

## 8. API

Base path: `/api/v1`. Trả về `application/json`, camelCase.

### Hệ thống

```
GET    /health                          Health check (không cần auth), trả 200 nếu kết nối DB OK
```

### Xác thực (không cần JWT)

```
POST   /auth/register       Body: { email, password, displayName } → tạo User, trả access+refresh token
POST   /auth/login          Body: { email, password } → trả access+refresh token
POST   /auth/refresh        Body: { refreshToken } → thu hồi token cũ, trả cặp token mới (rotation)
POST   /auth/logout         Body: { refreshToken } → thu hồi (Revoke) refresh token đó
```

Sai email/password trả `401` với `errorCode = "INVALID_CREDENTIALS"`, không tiết lộ email có tồn tại hay không.

### Hồ sơ người dùng

```
GET    /users/me                        Thông tin user hiện tại
PATCH  /users/me                        Đổi DisplayName, BankAccountNumber, BankBin (dùng cho VietQR)
```

### Nhóm

```
POST   /groups                          Tạo nhóm
GET    /groups                          Danh sách nhóm của user hiện tại
GET    /groups/{id}                     Chi tiết nhóm + members
PATCH  /groups/{id}                     Đổi tên, SimplifyDebts, archive
DELETE /groups/{id}                     Soft delete
GET    /groups/shared/{shareToken}      Xem nhóm qua link chia sẻ (không cần đăng nhập, read-only)
POST   /groups/{id}/share-token/rotate  Đổi token chia sẻ
```

### Thành viên

```
POST   /groups/{id}/members               Thêm thành viên (có tài khoản hoặc khách)
PATCH  /groups/{id}/members/{memberId}    Đổi tên hiển thị
DELETE /groups/{id}/members/{memberId}    Đánh dấu IsActive = false
POST   /groups/{id}/members/{memberId}/role  Gán/thu hồi quyền Owner (body { "role": "Owner" | "Member" }, chỉ Owner gọi được — xem mục 4.4)
```

Không cho xóa thành viên nếu họ đang có `net != 0`. Trả lỗi `MEMBER_HAS_OUTSTANDING_BALANCE`.

### Khoản chi

```
GET    /groups/{id}/expenses            Phân trang, mặc định 20/trang, sort OccurredAt desc
POST   /groups/{id}/expenses            Tạo
GET    /expenses/{expenseId}
PUT    /expenses/{expenseId}            Sửa (ghi audit log)
DELETE /expenses/{expenseId}            Soft delete
POST   /expenses/preview-split          Tính thử splits mà không lưu — dùng cho UI realtime
POST   /expenses/{expenseId}/receipt-image   Upload ảnh hóa đơn (multipart/form-data), trả về ReceiptImageUrl
GET    /expenses/{expenseId}/receipt-image   Tải ảnh hóa đơn — yêu cầu JWT + phải là thành viên nhóm
GET    /groups/{id}/export/expenses.csv      Xuất CSV danh sách khoản chi (bổ sung 2026-09-04)
GET    /groups/{id}/export/balances.csv      Xuất CSV số dư từng người (bổ sung 2026-09-04)
```

Ảnh lưu trong bảng `ReceiptImage` (SQL Server, `varbinary(max)`) — không dùng local disk hay cloud storage. `GET` bắt buộc `[Authorize]` + kiểm tra thành viên nhóm (khác với việc phục vụ file tĩnh qua static files, vốn không kiểm tra quyền — đã cân nhắc và chọn cách này để tránh lộ ảnh hóa đơn tài chính cho người ngoài group).

> ⚠️ **Lỗ hổng bảo mật phát hiện + sửa qua `security-review` (2026-09-07):** `CsvBuilder.Escape`
> (dùng bởi 2 endpoint export CSV ở trên) trước đây chỉ escape đúng chuẩn RFC 4180 (dấu
> phẩy/ngoặc kép/xuống dòng), không phòng CSV/Formula Injection (CWE-1236). `Expense.Title`,
> `Expense.Note`, `GroupMember.DisplayName` đều là text tự do người dùng đặt, không giới hạn ký tự —
> một thành viên ác ý có thể đặt tên/tiêu đề dạng `=HYPERLINK("http://attacker.example/...",...)`; khi
> người khác (thường là Owner) mở file CSV xuất ra bằng Excel/LibreOffice/Sheets, ô bắt đầu bằng
> `=`/`+`/`-`/`@` bị hiểu là công thức và tự chạy (rò rỉ dữ liệu ô khác, hoặc DDE trên Excel cũ). Đã
> sửa: `CsvBuilder.Escape` thêm dấu nháy đơn (`'`) trước ký tự kích hoạt công thức đầu tiên — nhưng
> **CHỈ áp dụng cho field kiểu `string`**, không áp cho field số (`TotalAmount`, `ExtraFeeAmount`,
> `Net`...). Lý do quan trọng: lần sửa đầu tiên áp luật này cho MỌI field kể cả số, khiến một số dư âm
> hợp lệ như `-50000` (hoàn toàn do server tự format từ `long`, không phải input tự do) bị biến thành
> chuỗi `"'-50000"` — phá hỏng khả năng tính tổng/so sánh trong Excel, tức là phá luôn công dụng chính
> của việc xuất CSV số dư, mà không tăng thêm an toàn thực sự (số không thể chứa công thức). Phát hiện
> bug hồi quy này ngay khi chạy lại `dotnet test` sau lần sửa đầu — nhắc nhở: biện pháp chống CSV
> injection kiểu "thêm `'` cho mọi field bắt đầu bằng `=+-@`" là cạm bẫy phổ biến, phải phân biệt field
> text tự do với field số/enum do server tự sinh. Test: `CsvBuilderTests` (UnitTests).

`PUT /expenses/{expenseId}` và các endpoint ghi vào `Settlement` yêu cầu client gửi kèm `rowVersion` (base64) lấy từ lần đọc gần nhất; server so khớp trước khi ghi, lệch thì trả `409 CONCURRENCY_CONFLICT` (xem 4.3/4.4).

`POST /expenses` request body:

```jsonc
{
  "title": "Ăn tối quán Bò Tơ",
  "totalAmount": 1250000,
  "extraFeeAmount": 125000,
  "occurredAt": "2026-09-03T19:30:00+07:00",
  "payers": [
    { "memberId": "...", "amount": 1000000 },
    { "memberId": "...", "amount": 375000 }
  ],
  "splitMode": "Shares",
  "splitConfig": {
    "shares": [
      { "memberId": "...", "weight": 1 },
      { "memberId": "...", "weight": 2 }
    ]
  }
}
```

Response luôn kèm mảng `warnings`:

```jsonc
{
  "data": { /* expense đã tạo, có splits đã tính */ },
  "warnings": [
    { "code": "SPLIT_TOTAL_MISMATCH", "message": "Lệch 20.000đ so với tổng hóa đơn", "delta": 20000 }
  ]
}
```

### Số dư & thanh toán

```
GET    /groups/{id}/balances            Số dư ròng từng người
GET    /groups/{id}/settlement-plan     Danh sách lượt chuyển tiền tối ưu
GET    /groups/{id}/settlements         Danh sách settlement (mọi trạng thái) của nhóm — bổ sung khi
                                         làm frontend 2026-09-03, bản gốc thiếu endpoint liệt kê
POST   /groups/{id}/settlements         Ghi nhận đã chuyển (status = Pending)
POST   /settlements/{id}/confirm        Người NHẬN xác nhận
POST   /settlements/{id}/reject         Người NHẬN từ chối
DELETE /settlements/{id}                Soft delete, chỉ khi còn Pending
```

### Nhật ký

```
GET    /groups/{id}/audit-logs          Phân trang, mặc định 20/trang, sort CreatedAt desc
```

`GET /settlement-plan` response:

```jsonc
{
  "simplified": true,
  "transactionCount": 3,
  "transactions": [
    {
      "fromMemberId": "...", "fromName": "Bình",
      "toMemberId": "...",   "toName": "An",
      "amount": 450000,
      "vietQr": {
        "bankBin": "970436", "accountNumber": "...", "amount": 450000,
        "content": "SplitBill Bo To",
        "payload": "00020101021238570010A00000072701270006970436011300110012345670208QRIBFTTA5303704540645000058020VN62110807SplitBill Bo To6304XXXX"
      }
    }
  ]
}
```

### Quy tắc chung

- Chỉ thành viên của nhóm mới được ghi/sửa. Link chia sẻ chỉ cho đọc.
- Phân quyền chi tiết theo `GroupMemberRole` (Owner/Member) xem bảng ở mục 4.4. Vi phạm trả `403 INSUFFICIENT_ROLE`.
- Lỗi trả về theo RFC 7807 (`ProblemDetails`), có thêm field `errorCode` dạng chuỗi hằng.
- Mọi thao tác ghi lên `Expense`/`Settlement` phải sinh `AuditLog` trong cùng transaction.
- Xung đột cập nhật đồng thời (RowVersion lệch) trả `409 CONCURRENCY_CONFLICT`.
- Toàn bộ endpoint dưới `/groups`, `/expenses`, `/settlements`, `/users/me` yêu cầu JWT Bearer hợp lệ, trừ `/groups/shared/{shareToken}`.

---

## 9. VietQR

Sinh chuỗi QR theo chuẩn EMVCo/VietQR để người dùng quét chuyển khoản ngay. Chỉ cần trả về **payload string**, việc render ảnh QR để frontend làm.

> ⚠️ **Làm rõ so với bản nháp trước:** ví dụ response ở mục 8 ban đầu thiếu field chứa chuỗi payload
> đã sinh (chỉ có `content` — nội dung chuyển khoản dạng text ngắn). Đã bổ sung field `payload` vào
> object `vietQr` — đây mới là chuỗi EMVCo/VietQR thật để frontend đưa vào thư viện vẽ QR (ví dụ
> `qrcode.js`), KHÔNG phải `content`.

- Cần `BankBin` (mã BIN 6 số) + `AccountNumber` của người nhận.
- Nội dung chuyển khoản: bỏ dấu tiếng Việt, tối đa 25 ký tự, dạng `SplitBill {tên nhóm rút gọn}`.
- Nếu người nhận chưa khai báo tài khoản ngân hàng → trả `vietQr: null`, không báo lỗi.

**Thuật toán rút gọn tên nhóm (chốt cụ thể):**

```
1. Bỏ dấu tiếng Việt (NFD normalize, strip combining marks), bỏ ký tự không phải chữ/số/khoảng trắng.
2. budget = 25 - len("SplitBill ")   // = 15 ký tự còn lại cho tên nhóm
3. Nếu tên nhóm (sau bước 1) <= budget → dùng nguyên.
4. Nếu dài hơn: cắt theo từ (word boundary) sao cho vừa budget, không cắt giữa từ;
   nếu chỉ riêng từ đầu tiên đã dài hơn budget thì cắt cứng theo ký tự tại đúng budget.
5. Không thêm "..." (tốn ký tự trong ngân sách 25 ký tự vốn đã chật).
```

Ví dụ: "Ăn tối quán Bò Tơ" → "An toi quan Bo To" (17 ký tự) > budget 15 → cắt theo từ → "An toi quan Bo" (14 ký tự) → nội dung cuối: `SplitBill An toi quan Bo`.

Viết thành service riêng `IVietQrGenerator` với unit test đối chiếu payload mẫu.

---

## 10. Thứ tự triển khai

Làm tuần tự, mỗi milestone phải build được và test xanh trước khi sang bước tiếp.

**M1 — Nền tảng**
Solution 4 project, DbContext, entities (gồm `RefreshToken`, `RowVersion`, `GroupMemberRole`), migration đầu tiên, health check endpoint.

**M2 — Thuật toán (làm trước khi có API)**
`ExpenseSplitCalculator` + `BalanceCalculator` + `GreedySettlementSolver` + `OptimalSettlementSolver`, kèm **toàn bộ test ở mục 7** (bao gồm case S3b cần bổ sung — bắt buộc chứng minh Optimal tốt hơn Greedy bằng chạy thực tế, không suy luận tay). Đây là phần khó nhất, làm kỹ ngay từ đầu.

**M3 — CRUD cơ bản**
Auth JWT (`/auth/*`, `PasswordHasher<User>`), `/users/me`, API nhóm, thành viên, phân quyền Owner/Member (4.4), khoản chi. Chưa cần settlement API.

**M4 — Số dư & thanh toán**
`/balances`, `/settlement-plan`, ghi nhận và xác nhận thanh toán, audit log.

**M5 — Hoàn thiện**
Link chia sẻ, VietQR, upload ảnh hóa đơn, chế độ `SimplifyDebts = false`, xuất dữ liệu (quyết định
người dùng 2026-09-04: CSV khoản chi + số dư, xem mục 8 `/export/expenses.csv` và `/export/balances.csv`).

**M6 — Về sau**
~~Ràng buộc mềm (6.4)~~ — đã làm 2026-09-05 (xem mục 6.4), itemized split — đã làm (xem mục 10b),
~~thông báo~~ — đã làm 2026-09-05 (xem mục 13), ~~đa tiền tệ~~ — đã làm 2026-09-05 (xem mục 14). Toàn
bộ mục 6 (M6) đã hoàn thành.

**M7 — Frontend (SplitBill.Web)**
Đã triển khai theo quyết định người dùng 2026-09-03 (xem mục 10b ngay dưới đây). Không thuộc phạm
vi backend gốc nhưng nay là 1 project chính thức trong solution.

---

## 10b. Frontend — SplitBill.Web (bổ sung 2026-09-03)

Bản đặc tả gốc chỉ mô tả backend. Theo yêu cầu người dùng, đã bổ sung 1 project **ASP.NET Core
Razor Pages** (`src/SplitBill.Web`, .NET 9) làm giao diện, gọi thẳng REST API ở mục 8 qua HTTP —
**không** gọi service tầng Application in-process, giữ đúng ranh giới API là nguồn sự thật duy nhất
cho nghiệp vụ. `SplitBill.Web` tham chiếu `SplitBill.Application` chỉ để dùng lại DTO (record thuần,
không kéo theo EF Core), tránh khai báo lại request/response shape.

### Kiến trúc xác thực (BFF — Backend For Frontend)

Trình duyệt **không bao giờ thấy JWT thật**. Luồng:

1. Người dùng đăng nhập/đăng ký qua Razor Page → gọi `POST /auth/login` hoặc `/auth/register`.
2. `AuthTokens` (access + refresh token) nhận về được lưu làm **claim trong cookie đăng nhập**
   (`CookieAuthenticationDefaults.AuthenticationScheme`), mã hóa bởi ASP.NET Core Data Protection.
3. Mọi request tới API sau đó đi qua `BearerTokenHandler` (một `DelegatingHandler` gắn vào
   `HttpClient` của `SplitBillApiClient`) — tự đọc access token từ cookie, gắn header
   `Authorization: Bearer`. Nếu access token sắp hết hạn (còn &lt; 30s), tự gọi `/auth/refresh`
   trước (qua client "ApiRaw" riêng, không gắn `BearerTokenHandler`, để tránh đệ quy), rồi
   `SignInAsync` lại để cập nhật cookie.
4. `GroupMemberId` của user hiện tại trong 1 nhóm cụ thể được suy ra tại runtime bằng cách so khớp
   claim `NameIdentifier` (UserId) với `GroupMember.UserId` trong `GroupDto.Members` — không lưu
   sẵn, vì 1 user có `GroupMemberId` khác nhau ở mỗi nhóm.

### Danh sách trang

```
/                              Trang chủ
/Account/Register              Đăng ký
/Account/Login                 Đăng nhập
/Account/Logout                Đăng xuất (POST-only)
/Account/Profile               Sửa tên hiển thị + tài khoản ngân hàng (cho VietQR)
/Groups                        Danh sách nhóm của tôi + form tạo nhóm
/Groups/Details/{id}           Chi tiết nhóm: thành viên, link chia sẻ, SimplifyDebts
/Public/Group/{shareToken}     Xem nhóm qua link chia sẻ — [AllowAnonymous]
/Expenses/{groupId}            Danh sách khoản chi
/Expenses/Create/{groupId}     Thêm khoản chi (form động theo SplitMode)
/Expenses/Edit/{expenseId}     Sửa khoản chi (có RowVersion chống ghi đè)
/Groups/Balances/{id}          Số dư từng người
/Groups/SettlementPlan/{id}    Kế hoạch thanh toán + VietQR + ghi nhận/xác nhận thanh toán
```

### Production-hardening (bổ sung 2026-09-04)

- **JWT SigningKey**: KHÔNG còn giá trị thật trong `appsettings.json` (để rỗng). Dev dùng
  `dotnet user-secrets set "Jwt:SigningKey" "<chuỗi random >= 32 byte>"` trong `src/SplitBill.Api`;
  Production đặt qua biến môi trường `Jwt__SigningKey` hoặc Key Vault. API **fail-fast khi khởi
  động** (ném `InvalidOperationException` rõ ràng) nếu key rỗng hoặc < 32 byte — tránh chạy nhầm với
  key yếu/rỗng do lỗi thao tác (từng gặp: `RandomNumberGenerator.Fill` không tồn tại trên .NET
  Framework của Windows PowerShell 5.1, khiến key bị lưu toàn số 0 — đã phát hiện và sửa ngay).
- **CORS**: bật theo whitelist từ config `Cors:AllowedOrigins` (mặc định chỉ
  `http://localhost:5103` — origin của `SplitBill.Web`). Đã verify: origin trong whitelist nhận
  header `Access-Control-Allow-Origin`, origin lạ thì không.
- **Rate limiting**: `AuthController` giới hạn 10 request/phút/IP (`FixedWindowLimiter`, dùng
  `Microsoft.AspNetCore.RateLimiting` có sẵn trong framework, không cần NuGet thêm), trả `429` khi
  vượt. Đã verify bằng cách gọi `/auth/login` liên tiếp 13 lần: 10 lần đầu 401 (sai mật khẩu), 3 lần
  sau 429.
  > ⚠️ Lỗi thật phát hiện khi rà soát 2026-09-04: bài verify ở trên chỉ gọi từ 1 IP nên không phát
  > hiện ra `options.AddFixedWindowLimiter("auth", ...)` (không có partition key) thực chất tạo
  > **1 hạn ngạch dùng chung cho MỌI client**, không phải "10 request/phút/IP" như mô tả — vài user
  > đăng nhập cùng lúc có thể vô tình khóa đăng nhập của tất cả người khác trong 1 phút. Đã sửa bằng
  > `options.AddPolicy("auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(partitionKey:
  > httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", factory: ...))` để mỗi IP có
  > hạn ngạch riêng — đây mới là cách đúng để dùng `Microsoft.AspNetCore.RateLimiting` theo per-client,
  > `AddFixedWindowLimiter` một mình KHÔNG tự phân theo client.
- **Fail-fast lúc khởi động phải thoát với exit code khác 0**: khối `try/catch` bọc `Main` trong
  `Program.cs` (SplitBill.Api) trước đây chỉ `Log.Fatal(ex, ...)` mà không set exit code, nên khi
  khởi động thất bại (ví dụ thiếu `Jwt:SigningKey`) process vẫn thoát với **exit code 0** — từng thấy
  trực tiếp dòng "[exited with code 0]" khi quên cấu hình. Docker/K8s/systemd coi exit code 0 là "tắt
  bình thường" nên sẽ KHÔNG tự restart hay báo lỗi. Đã sửa: thêm `Environment.ExitCode = 1;` trong
  catch (không dùng `Environment.Exit()` để khối `finally` vẫn chạy xong việc flush log Serilog).
- **Upload ảnh hóa đơn phải validate `Content-Type` thật, không chỉ đuôi file**: `ExpensesController`
  trước đây chỉ whitelist đuôi file (`.jpg/.jpeg/.png/.webp`); `Content-Type` do client gửi lên được
  lưu thẳng vào `ReceiptImage.ContentType` và trả lại y nguyên khi `GET`. Vì đuôi file người dùng đặt
  tùy ý, một thành viên nhóm có thể up file `.jpg` với `Content-Type: text/html` chứa mã độc — rủi ro
  stored-XSS phạm vi trong nhóm nếu sau này có UI hiển thị ảnh inline. Đã sửa: whitelist thêm
  `file.ContentType` (`image/jpeg|image/png|image/webp`) khi upload, và thêm header
  `X-Content-Type-Options: nosniff` khi serve lại (phòng vệ thêm dù response đã có
  `Content-Disposition: attachment` sẵn từ `fileDownloadName`).
- **2 endpoint có trong đặc tả nhưng chưa từng được cài đặt, đã bổ sung 2026-09-04**:
  `GET /groups/{id}/audit-logs` (mục 8 — `IAuditLogRepository` trước đây chỉ có `AddAsync`, dữ liệu
  ghi đầy đủ nhưng không đọc lại được qua API) và `POST /groups/{id}/members/{memberId}/role` (mục
  4.4 — chuyển nhượng/thu hồi quyền Owner; dữ liệu đã hỗ trợ nhiều Owner nhưng trước đó không có API
  nào gán thêm được). Cả hai đã có test ở `GroupServiceTests`.

### Giới hạn đã biết (chưa làm, để tránh phình phạm vi)

- ~~Form tạo/sửa khoản chi chỉ hỗ trợ Equal/Shares/Percentage/ExactAmount, chưa có UI Itemized~~ —
  đã bổ sung UI `Itemized` (bảng món ăn động, thêm/xóa dòng qua JS, xem `Create.cshtml`/`Edit.cshtml`)
  cùng đợt thiết kế lại giao diện 2026-09-04; cả 5 `SplitMode` đều có form trên Web.
- ~~Form Edit không thể khôi phục chính xác trọng số/% gốc đã nhập lúc tạo (API chỉ lưu
  ExpenseSplit.Amount cuối cùng, không lưu SplitConfigJson có cấu trúc đọc lại được)~~ — **sai, đã sửa
  2026-09-05**: `ExpenseService` vốn đã lưu `SplitConfigJson` từ trước (dòng ghi `SplitConfigJson =
  JsonSerializer.Serialize(request.SplitConfig)` khi Create/Update), chỉ là `ExpenseDto` chưa từng trả
  field này ra qua API nên Web luôn phải suy ngược từ Amount cuối cùng (không chính xác khi có làm
  tròn, và với Itemized thì hoàn toàn không suy ngược được — Items luôn trống). Đã thêm
  `SplitConfigJson` vào `ExpenseDto`, `EditModel.MapToInput` giờ ưu tiên đọc trực tiếp từ đó (fallback
  về suy ngược từ Amount nếu null/parse lỗi — dữ liệu tạo trước bản sửa này). Verify: tạo khoản chi
  Itemized trên trình duyệt thật, mở lại trang Sửa, món ăn + người ăn hiển thị đúng y hệt lúc tạo
  (trước đây luôn trống hoàn toàn). Xem test `ExpenseServiceTests` (API trả đúng JSON) và
  `EditModelTests` (Web parse + map đúng, kể cả fallback khi JSON null/lỗi).
- Vẽ mã QR dùng thư viện `qrcodejs` từ CDN `cdnjs.cloudflare.com` (đã xác nhận CDN
  `cdn.jsdelivr.net` KHÔNG truy cập được từ môi trường máy chủ dev — trả lỗi 503/404 liên tục khi
  test; nếu đổi CDN trong tương lai, ưu tiên cdnjs).
- Trang Web khi chạy dev phải set `ASPNETCORE_ENVIRONMENT=Development` tường minh nếu khởi động qua
  `dotnet run --no-launch-profile` (bỏ qua `launchSettings.json`), nếu không sẽ chạy ở chế độ
  Production và ẩn traceback lỗi thật.
  > ⚠️ Hệ quả nghiêm trọng hơn phát hiện ngày 2026-09-04: ở môi trường Production, `app.MapStaticAssets()`
  > (ASP.NET Core 9) trông đợi các asset đã được nén sẵn (Brotli/Gzip) sinh ra ở bước `dotnet publish`.
  > Nếu chỉ chạy `dotnet build` + `dotnet run` (không publish) mà môi trường lại là Production, mọi
  > file tĩnh (`site.css`, `bootstrap.min.css`, `site.js`...) vẫn trả về **HTTP 200 nhưng body rỗng**
  > (`Content-Length: 0`, không có `Content-Type`) — trang chạy được nhưng hoàn toàn không có style,
  > trông như "giao diện xấu"/không load CSS dù Network tab báo 200 OK, dễ đánh lừa là lỗi CDN hay cache
  > trình duyệt. Cách chẩn đoán: kiểm tra `document.styleSheets[i].cssRules.length` — bằng 0 dù link
  > load 200 nghĩa là body rỗng. Luôn chạy dev bằng `ASPNETCORE_ENVIRONMENT=Development` để static
  > assets được phục vụ trực tiếp từ đĩa (live-edit được), không qua đường publish/nén.
- CSS "sticky footer" kiểu cũ (`.footer { position: absolute; bottom: 0; }` + `body { margin-bottom: 60px; }`,
  mẫu mặc định của ASP.NET Core Razor Pages scaffold) bị lỗi đè chồng nội dung khi trang ngắn hơn viewport:
  absolute positioning không đẩy được nội dung ra khỏi vùng footer nếu tổng chiều cao trang gần bằng
  chiều cao viewport. Đã thay bằng flexbox chuẩn: `body { min-height: 100vh; display: flex; flex-direction: column; }`,
  `body > .container { flex: 1 0 auto; }`, `.footer { flex-shrink: 0; margin-top: 2rem; }` — footer luôn
  nằm đúng cuối nội dung thật, không bao giờ đè lên, bất kể trang dài hay ngắn.
- ~~Chưa có bộ test tự động cho `SplitBill.Web`~~ — đã bổ sung `tests/SplitBill.Web.Tests` (2026-09-04):
  17 test cho `SplitBillApiClient` (parse response, escape lỗi thành `ApiException` đúng errorCode),
  `ExpenseFormHelpers.BuildSplitConfig` (đủ 5 mode kể cả Itemized), và `LoginModel` (có dựng
  `HttpContext`/cookie auth thật để test `SignInAsync`, không cần thư viện mocking).
  > ⚠️ Bug thật phát hiện khi viết test: nếu `GET /users/me` trả 200 nhưng body thiếu field (hoặc
  > `DisplayName` null vì `System.Text.Json` KHÔNG enforce non-nullable reference type lúc runtime),
  > `SignInHelper.SignInAsync` sập với `ArgumentNullException` không được bắt (catch cũ chỉ bắt
  > `ApiException`), làm hỏng toàn bộ luồng đăng nhập dù bước đăng nhập cookie fallback đã thành
  > công. Đã sửa: catch rộng hơn (mọi exception, miễn cookie fallback đã đăng nhập) + tự phòng vệ
  > `DisplayName` rỗng/null bằng tên fallback thay vì tin tưởng type signature của DTO.
- Đã thiết kế lại giao diện (2026-09-04) sau phản hồi người dùng "giao diện rất xấu": `wwwroot/css/site.css`
  giờ là một design system nhỏ dựa trên CSS custom properties (`--sb-brand`, `--sb-ink`, `--sb-radius`...),
  font "Be Vietnam Pro" (Google Fonts, hỗ trợ dấu tiếng Việt tốt), nút bo tròn pill, card có shadow nhẹ,
  và các class tiện ích riêng: `.sb-amount-positive`/`.sb-amount-negative` (số dư dương/âm tô màu),
  `.sb-balance-card` (+ `.positive`/`.negative`) cho lưới số dư, `.sb-auth-card` cho trang đăng nhập/đăng ký,
  `.fw-600`/`.fw-700` (Bootstrap không có sẵn 2 class này, phải tự định nghĩa). Mọi trang Razor Pages mới
  nên tái dùng các class/biến này thay vì viết style rời rạc, để giữ giao diện nhất quán.
- Bổ sung `README.md` (hướng dẫn chạy nhanh, không lặp lại nội dung đặc tả ở đây) và đóng gói Docker
  (2026-09-04): `src/SplitBill.Api/Dockerfile`, `src/SplitBill.Web/Dockerfile`, `docker-compose.yml`
  ở gốc repo (SQL Server + Api + Web, `docker compose up --build`).
  > ⚠️ Bug thật phát hiện lúc viết README: `src/SplitBill.Api/Properties/launchSettings.json` trước đó
  > có `applicationUrl` mặc định là cổng **5036**, trong khi `SplitBill.Web/appsettings.json`
  > (`Api:BaseUrl`) và CORS mặc định lại giả định Api chạy ở cổng **5199** — 2 giá trị này không hề
  > khớp nhau. Suốt phiên làm việc trước đó phải tự set `$env:ASPNETCORE_URLS="http://localhost:5199"`
  > thủ công mỗi lần chạy Api mới hoạt động đúng; nếu chạy đúng theo hướng dẫn mặc định (`dotnet run`,
  > không override) thì Web sẽ không gọi được Api (connection refused vì Api thực ra lắng nghe ở 5036).
  > Đã sửa `launchSettings.json` đổi cổng về 5199 để khớp với phần còn lại của hệ thống — từ nay
  > `dotnet run` không cần override gì cũng chạy đúng theo README.
  > Đồng thời thêm cờ `Database:AutoMigrateOnStartup` (mặc định `false`, chỉ bật qua env
  > `Database__AutoMigrateOnStartup=true` trong `docker-compose.yml`) để container tự áp migration lúc
  > khởi động mà không đổi hành vi mặc định của luồng dev local (vẫn `dotnet ef database update` thủ
  > công như README mô tả), và bật `EnableRetryOnFailure()` cho `UseSqlServer` để chịu được lỗi kết nối
  > tạm thời (giúp container Api không crash nếu khởi động trước khi SQL Server sẵn sàng nhận login).
  > **Lưu ý:** môi trường làm việc hiện tại không có sẵn Docker CLI nên chưa tự chạy được
  > `docker compose up --build` để verify end-to-end — cấu hình đã rà soát kỹ bằng mắt (đường dẫn COPY,
  > tên service, biến môi trường khớp với `Program.cs`/`appsettings.json`) nhưng nếu Docker sẵn có ở máy
  > khác, nên tự `docker compose up --build` 1 lần trước khi tin tưởng hoàn toàn.
- **Bổ sung UI upload/xem ảnh hóa đơn (2026-09-05)**: `Expenses/Edit.cshtml` giờ có card "Ảnh hóa đơn"
  — hiện thumbnail nếu đã có, form upload `.jpg/.jpeg/.png/.webp` tối đa 10MB (khớp giới hạn phía Api).
  `SplitBillApiClient` thêm `UploadReceiptImageAsync`/`GetReceiptImageAsync`; trang Edit có 2 handler
  mới: `OnPostUploadReceiptAsync` (multipart upload) và `OnGetReceiptImageAsync` (proxy ảnh — trình
  duyệt KHÔNG BAO GIỜ gọi thẳng endpoint ảnh của Api vì thiếu Bearer token sẽ bị 401, luôn phải qua
  proxy này để `SplitBillApiClient` tự gắn token). API upload/download ảnh vốn đã có từ trước, chỉ là
  chưa từng có UI Web nào dùng tới.
  > ⚠️ **Bug nghiêm trọng phát hiện khi verify trực tiếp trên trình duyệt (không phải qua test, vì kiến
  > trúc test hiện tại không chạy qua pipeline model-binding/validation thật của ASP.NET Core):** form
  > Tạo khoản chi **luôn thất bại âm thầm với MỌI SplitMode trừ Itemized**, từ đúng lúc UI Itemized được
  > thêm vào (đợt redesign 2026-09-04) tới trước khi phát hiện. Nguyên nhân: `Create.cshtml`/`Edit.cshtml`
  > luôn tự thêm sẵn 1 dòng "món ăn" trống vào DOM lúc tải trang, kể cả khi SplitMode đang chọn không
  > phải Itemized (chỉ ẩn bằng CSS `display:none`, các `<input>` bên trong VẪN được trình duyệt submit
  > bình thường — khác với thuộc tính `disabled`). `ItemInput.Name` (string) và `ItemInput.Price` (long)
  > lúc đó KHÔNG nullable, nên: (1) ASP.NET Core coi `string Name` không nullable là **implicit required**
  > (do dự án bật `Nullable enable`), dòng trống có `Name=""` → lỗi "The Name field is required"; (2)
  > `Price` kiểu `long` không parse được chuỗi rỗng `""` → lỗi "The value '' is invalid". Cả 2 lỗi này
  > khiến `ModelState.IsValid == false`, `OnPostAsync` trả về `Page()` NGAY LẬP TỨC mà không hề set
  > `ErrorMessage` — người dùng thấy y hệt trang Tạo lúc đầu, tưởng bấm Lưu không có phản ứng gì, không
  > một dòng lỗi nào hiển thị. Bug này lẽ ra phải chặn được HOÀN TOÀN việc tạo khoản chi Equal/Shares/
  > Percentage/ExactAmount trên Web trong suốt khoảng thời gian đó.
  > Đã sửa 2 việc:
  > 1. `ItemInput.Name`/`Price` đổi thành nullable (`string?`/`long?`); `BuildSplitConfig` đã sẵn logic
  >    lọc bỏ dòng rỗng (`Where(i => !string.IsNullOrWhiteSpace(i.Name) && i.Price is > 0 && ...)`) nên
  >    chỉ cần đổi kiểu là dòng trống tự động được bỏ qua thay vì làm sập ModelState.
  > 2. Thêm `<div asp-validation-summary="All">` (ẩn khi `ViewData.ModelState.IsValid`) vào cả
  >    `Create.cshtml` và `Edit.cshtml` — để nếu tương lai có lỗi validate tương tự, người dùng THẤY
  >    ĐƯỢC lỗi thay vì trang "không phản ứng gì" như lần này.
  > **Bài học về quy trình**: bug này tồn tại xuyên suốt nhiều lần "verify trên trình duyệt" trước đó
  > trong cùng phiên làm việc — nhưng mọi lần verify trước đều tình cờ dùng đúng SplitMode Itemized (vì
  > đang test tính năng Itemized), nên dòng món ăn trống luôn được điền đủ dữ liệu, "vô tình" né được
  > lỗi. Verify 1 happy-path không đại diện cho MỌI đường dẫn — khi thêm tính năng chạm vào 1 form dùng
  > chung cho nhiều mode/nhánh, phải test LẦN LƯỢT từng nhánh chính, không chỉ nhánh đang phát triển.

---

## 11. Quy ước code

- C# 13, `nullable enable`, `ImplicitUsings enable`.
- Dùng `record` cho DTO, `sealed class` cho service.
- Async toàn bộ ở tầng I/O, hậu tố `Async`, luôn truyền `CancellationToken`.
- Không dùng `.Result` hay `.Wait()`.
- Không đặt logic nghiệp vụ trong Controller. Controller chỉ: validate → gọi service → map response.
- Đặt tên biến/class bằng tiếng Anh. Comment và message trả về người dùng bằng tiếng Việt.
- Mọi phép tính tiền viết trong `Application`, không rải rác ở Controller hay Repository.
- Không dùng `decimal` cho tiền. Nếu cần tỉ lệ trung gian (shares, %) thì dùng `decimal` cho **trọng số**, nhưng kết quả cuối luôn quy về `long`.

---

## 12. Ghi chú cho AI khi làm việc

- Nếu một yêu cầu trong file này không rõ, **hỏi lại người dùng** thay vì tự quyết.
- Khi sửa thuật toán settlement, chạy lại toàn bộ test ở mục 7 trước khi báo hoàn thành.
- Không tự ý thêm tính năng ngoài phạm vi milestone đang làm.
- Khi tạo migration, đặt tên có nghĩa (`AddSettlementConfirmation`), không dùng tên mặc định.
- Ưu tiên viết test trước cho mọi logic liên quan tới tiền.

---

## 13. Thông báo (Notifications) — bổ sung 2026-09-05, M6

Quyết định người dùng: gửi **cả 2 kênh** — trong app (in-app, lưu DB) và email. 4 sự kiện kích hoạt
(không hơn, không tự ý thêm sự kiện khác ngoài danh sách này):

1. **Khoản chi mới trong nhóm** — báo cho mọi thành viên khác (có tài khoản, không phải khách vãng
   lai, không phải người tạo) khi `POST /groups/{id}/expenses` thành công.
2. **Có người ghi nhận đã chuyển tiền cho mình** — báo cho `ToMemberId` (nếu có tài khoản) khi
   `POST /groups/{id}/settlements` thành công.
3. **Settlement của mình được xác nhận/từ chối** — báo cho `FromMemberId` (nếu có tài khoản) khi
   `POST /settlements/{id}/confirm` hoặc `/reject` thành công.
4. **Được thêm vào nhóm mới** — báo cho user khi được thêm vào nhóm bằng `UserId` (không áp dụng cho
   khách vãng lai thêm bằng `DisplayName`).

Guest (`GroupMember.UserId == null`) **không bao giờ** nhận thông báo (không có tài khoản để đăng
nhập xem in-app, không có email để gửi) — luôn lọc theo `GroupMember.User is not null` trước khi tạo
thông báo.

### 13.1 Mô hình dữ liệu

```csharp
Notification {
    Guid Id
    Guid UserId             // người nhận — LUÔN là User có tài khoản, không dùng GroupMemberId
    Guid GroupId
    string Type             // "ExpenseCreated" | "SettlementRecorded" | "SettlementConfirmed" |
                             // "SettlementRejected" | "MemberAdded"
    string Title
    string Message
    string? LinkUrl         // đường dẫn tương đối trên Web, vd "/Expenses/Index/{groupId}"
    bool IsRead
    DateTimeOffset CreatedAt
}
```

Không soft-delete, không global query filter (giống `AuditLog`) — thông báo cũ vẫn giữ nguyên lịch sử.
Index `(UserId, IsRead, CreatedAt DESC)` để truy vấn "chưa đọc" và phân trang nhanh.

### 13.2 API

```
GET  /api/v1/notifications                Phân trang (mặc định 20/trang), sort CreatedAt desc,
                                           chỉ của user hiện tại (theo JWT sub)
GET  /api/v1/notifications/unread-count   Số thông báo chưa đọc — dùng cho badge
POST /api/v1/notifications/{id}/read      Đánh dấu 1 thông báo đã đọc (chỉ chủ sở hữu)
POST /api/v1/notifications/read-all       Đánh dấu tất cả đã đọc
```

### 13.3 Gửi email

- Thư viện: **MailKit** (bổ sung vào danh sách NuGet được phép ở mục 2 — quyết định người dùng
  2026-09-05, thay vì `System.Net.Mail` đã bị Microsoft khuyến cáo không dùng cho code mới).
  > ⚠️ Ghi nhận rủi ro đã biết (2026-09-05): bản lúc thêm (4.14.0) bị NuGet cảnh báo NU1902
  > (moderate severity, GHSA-9j88-vvj5-vhgr) — đã thử các version 4.9.0/4.13.0/4.14.0, cảnh báo vẫn còn
  > tại thời điểm đó (chưa có bản vá). **Đã nâng cấp lên 4.17.0 ngày 2026-09-07** — verify bằng
  > `dotnet list package --vulnerable --include-transitive` (không còn cảnh báo nào) và
  > `dotnet build` (0 warning liên quan NU1902). Vẫn giữ nguyên chỉ dùng tính năng gửi SMTP cơ bản.
- `IEmailSender.SendAsync(toEmail, subject, htmlBody, cancellationToken)` — interface thuần trong
  `SplitBill.Application`, 2 cài đặt ở `SplitBill.Infrastructure`:
  - `SmtpEmailSender` (MailKit thật) — dùng khi `Smtp:Host` có cấu hình.
  - `ConsoleEmailSender` (fallback) — chỉ ghi log (Serilog) nội dung email thay vì gửi thật, dùng khi
    `Smtp:Host` rỗng/chưa cấu hình. Đây là **quyết định người dùng 2026-09-05**: môi trường dev/test
    chưa có SMTP thật, không fail-fast như `Jwt:SigningKey` — thiếu cấu hình SMTP thì âm thầm chuyển
    sang log, không chặn ứng dụng chạy. Program.cs chọn implementation dựa trên
    `string.IsNullOrWhiteSpace(Smtp:Host)`.
- Lỗi gửi email (SMTP timeout, sai cấu hình...) **không được** làm hỏng thao tác nghiệp vụ chính (tạo
  khoản chi, ghi nhận settlement...) — luôn bọc try/catch quanh việc gửi thông báo, chỉ log lỗi, không
  throw ra ngoài. In-app notification (ghi DB) vẫn phải thành công độc lập với email.

### 13.4 Giới hạn đã biết

Form "Thêm thành viên" trên Web (`Groups/Details.cshtml`) **chỉ hỗ trợ thêm khách vãng lai bằng
DisplayName** (`AddMemberRequest(null, NewMember.DisplayName)` — luôn truyền `UserId: null`), không có
ô nào để thêm thành viên bằng UserId của 1 tài khoản đã có sẵn. Hệ quả: thông báo "được thêm vào nhóm
mới" (mục 13, sự kiện #4) trên thực tế **chỉ có thể kích hoạt qua gọi API trực tiếp**
(`POST /groups/{id}/members` với `userId` khác null), không bao giờ xảy ra khi thao tác hoàn toàn qua
giao diện Web hiện tại. API vẫn hỗ trợ đầy đủ, chỉ là chưa có UI tìm/chọn user theo email để thêm.

### 13.5 Vị trí gọi trong code

`NotificationService` (namespace `SplitBill.Application.Notifications`) là service thuần nhận
danh sách người nhận + nội dung, tự ghi DB + gọi `IEmailSender` — được `ExpenseService`,
`SettlementRecordService`, `GroupService` gọi ở đúng 4 điểm nêu ở mục 13 (sau khi
`_unitOfWork.SaveChangesAsync` của thao tác chính đã thành công, không gộp chung 1 transaction với dữ
liệu tài chính — thông báo là hệ quả phụ, không phải một phần bất biến `Σ net = 0`).

> ⚠️ **Lỗ hổng bảo mật phát hiện + sửa qua `security-review` (2026-09-07):** `NotificationService.
> BuildHtmlBody` ghép `message` (và trước đây cả `linkUrl`) thẳng vào chuỗi HTML mà **không
> HtmlEncode**, trong khi `SmtpEmailSender` gửi body dưới dạng `TextPart("html")` — mail client render
> thật, không hiển thị dạng chữ thô. `message` luôn mang theo dữ liệu người dùng tự đặt không giới hạn
> ký tự (`GroupMember.DisplayName`, `Expense.Title`...), nên một thành viên ác ý có thể đặt tên/tiêu đề
> chứa thẻ `<a>`/`<img>` giả mạo; khi trigger 1 trong 4 sự kiện thông báo (mục 13), mọi thành viên khác
> nhận được email chứa markup độc hại đó **gửi từ đúng địa chỉ SMTP hợp lệ của app** — dễ lừa hơn hẳn
> phishing thông thường vì người nhận vốn tin tưởng kênh này. Đã sửa: `BuildHtmlBody` giờ
> `WebUtility.HtmlEncode(message)` trước khi ghép, và chỉ chấp nhận `linkUrl` dạng đường dẫn tương đối
> bắt đầu bằng `/` (không phải `//`) làm phòng vệ theo chiều sâu — dù trên thực tế `linkUrl` luôn do
> chính service nội bộ tự dựng (`"/Expenses/Index/{groupId}"`), không phải input người dùng trực tiếp.
> `title` không cần encode vì chỉ dùng làm `Subject` (MimeKit tự xử lý header, không render HTML).
> Test hồi quy: `NotifyAsync_MessageContainsHtml_EmailBodyIsHtmlEncoded`,
> `NotifyAsync_LinkUrlIsAbsolute_DroppedFromEmailBody` (`NotificationServiceTests`).

---

## 14. Đa tiền tệ — bổ sung 2026-09-05, M6 (hạng mục cuối cùng)

Quyết định người dùng: **mỗi `Group` dùng CỐ ĐỊNH đúng 1 loại tiền** (field `Group.Currency` đã có sẵn
từ đầu dự án, trước đó chưa từng được validate/dùng tới thực sự) — KHÔNG trộn nhiều tiền tệ trong 1
nhóm, KHÔNG quy đổi tỉ giá, KHÔNG cho đổi currency sau khi nhóm đã tạo (`UpdateGroupRequest` vốn đã
không có field Currency — đúng ý, giữ nguyên). Nhờ vậy bất biến `Σ net = 0` và toàn bộ thuật toán
settlement (mục 6) **không đổi một dòng nào** — vẫn chỉ là số nguyên `long`, không cần biết đơn vị là gì.

Danh sách tiền tệ được hỗ trợ: **VND, USD, EUR** (`SplitBill.Application.Common.SupportedCurrencies`).

### 14.1 Đơn giản hóa có chủ đích: không có phần thập phân

`Amount` (long) luôn là **ĐƠN VỊ NGUYÊN** của đồng tiền đó (đồng cho VND, đô-la cho USD, euro cho EUR)
— KHÔNG phải cent/xu như quy ước "cent là đơn vị nhỏ nhất" thường thấy (Stripe...). Lý do: một app chia
tiền bạn bè hiếm khi cần chính xác tới xu, và cách này giữ nguyên được TOÀN BỘ form nhập liệu hiện có
(vẫn nhập số nguyên thuần qua `<input type="number">`), tránh phải viết lại logic quy đổi thập phân
(nhân/chia 100, định dạng 2 chữ số lẻ...) ở mọi form Create/Edit khoản chi, Settlement — đổi lại là
USD/EUR hiển thị "$150" thay vì "$150.00" (không có ".00"). Coi đây là giới hạn cố ý cho MVP, không
phải thiếu sót.

### 14.2 Validate & định dạng

- `CreateGroupRequestValidator` chặn `Currency` không nằm trong danh sách hỗ trợ (rỗng thì
  `GroupService` tự gán mặc định VND qua `SupportedCurrencies.Default`, không coi là lỗi).
- `SupportedCurrencies.Format(amount, currencyCode)` — hàm dùng chung (cả Api lẫn Web, Web tham chiếu
  thẳng vì đã có sẵn cơ chế dùng lại DTO của Application) để định dạng đúng ký hiệu + vị trí
  (`đ`/`€` là suffix, `$` là prefix).
  > ⚠️ Bug thật phát hiện qua test tự động lúc viết tính năng này: lần cài đặt đầu tiên dùng
  > `amount.ToString("N0")` KHÔNG chỉ định `CultureInfo` — dựa vào ambient culture của thread/server.
  > Trên máy dev này (culture mặc định `vi-VN`), `"N0"` luôn ra dấu CHẤM phân cách hàng nghìn, **kể cả
  > khi format USD** (`"$1.500"` — dễ đọc nhầm thành "1 đô rưỡi" thay vì 1500 đô, vì dấu chấm thường là
  > dấu thập phân trong tiếng Anh). Nếu server production chạy ở locale khác (vd `en-US`), kết quả sẽ
  > lại khác nữa — hoàn toàn không kiểm soát được. Đã sửa: ép cứng `CultureInfo` tường minh cho từng
  > currency (`vi-VN`-style dấu chấm cho VND/EUR, `InvariantCulture`-style dấu phẩy cho USD) ngay trong
  > `SupportedCurrencies`, không bao giờ dựa vào `CultureInfo.CurrentCulture`. Điều này cũng vô tình làm
  > 5 chỗ hiển thị VND cũ (trước đây tự viết `.ToString("N0")` rời rạc, cũng dựa vào ambient culture)
  > trở nên đáng tin cậy hơn, không phụ thuộc locale server nữa.
- `BalanceService.BuildVietQr` trả `null` nếu `Group.Currency != "VND"` — VietQR là chuẩn chuyển khoản
  ngân hàng Việt Nam, không áp dụng được cho USD/EUR (mục 9), bất kể thành viên đã khai báo tài khoản
  ngân hàng hay chưa.
- Form Tạo nhóm trên Web (`Groups/Index.cshtml`) có dropdown chọn Currency (mặc định VND) — trước đây
  luôn hardcode `"VND"` khi gọi API, không có cách nào tạo nhóm khác VND qua giao diện. Nhãn các ô nhập
  số tiền trên form Tạo/Sửa khoản chi và Ghi nhận thanh toán hiển thị `(@Model.Group.Currency)` thay vì
  hardcode `(đ)`.

---

## 15. Danh sách tính năng bổ sung sau M6 — theo yêu cầu người dùng 2026-09-05

Sau khi M6 hoàn thành 100%, người dùng yêu cầu gợi ý thêm tính năng hay, rồi chốt **làm toàn bộ** danh
sách gợi ý, theo thứ tự từ dễ đến khó, mỗi tính năng build + test (chạy lại toàn bộ mục 7 nếu đụng tới
thuật toán) + commit riêng:

1. Dark mode — mục 15.1 (đã làm).
2. Tìm kiếm/lọc khoản chi — mục 15.2 (đã làm).
3. Nhãn/danh mục khoản chi — mục 15.3 (đã làm).
4. Bảng tổng quan cá nhân ở trang chủ — mục 15.4 (đã làm).
5. Timeline hoạt động nhóm — mục 15.5 (đã làm).
6. Tham gia nhóm qua link chia sẻ — mục 15.6 (đã làm).
7. Khoản chi định kỳ — mục 15.7 (đã làm).
8. Nhắc nợ tự động — mục 15.8 (đã làm).
9. Xuất PDF tổng kết chuyến đi — mục 15.9 (đã làm). Toàn bộ 9 hạng mục đã hoàn thành.

### 15.1 Dark mode

Tận dụng toàn bộ hệ thống CSS custom properties `--sb-*` đã có sẵn trong `site.css` (mục 10b) — không
cần viết lại rule nào, chỉ cần định nghĩa lại giá trị các token dưới selector
`:root[data-theme="dark"]`.

- Thuộc tính `data-theme` (light/dark, do site tự định nghĩa) và `data-bs-theme` (chuẩn Bootstrap
  5.3+, để các thành phần Bootstrap thuần chưa được `site.css` ghi đè riêng — dropdown, modal, close
  button... — tự đổi theo) được đặt cùng lúc trên `<html>`.
- Đặt theme **trong `<head>`, bằng `<script>` inline, TRƯỚC** các thẻ `<link>` CSS hoàn tất render —
  đọc `localStorage['sb-theme']`, fallback `prefers-color-scheme` của hệ điều hành nếu người dùng chưa
  từng bấm nút chọn, mặc định light nếu trình duyệt chặn cả hai. Làm sớm như vậy để tránh FOUC (trang
  nháy sáng rồi mới chuyển tối khi tải).
  > Đã verify sống: tải lại trang khi đã lưu `dark` trong localStorage, trang vào thẳng dark mode
  > ngay từ khung hình đầu, không có nháy trắng.
- Nút bấm 🌙/☀️ trên navbar (`_Layout.cshtml`, id `sb-theme-toggle`) đảo `data-theme`/`data-bs-theme`
  và ghi lại `localStorage`, xử lý trong `site.js`. Icon thể hiện HÀNH ĐỘNG sẽ xảy ra khi bấm (đang
  sáng hiện 🌙 nghĩa là bấm để chuyển tối), không phải theme hiện tại.
  > ⚠️ Bug phát hiện qua live-verify: `.card` trong `site.css` không khai báo `background` tường minh,
  > trước giờ "ăn theo" mặc định `--bs-card-bg` của Bootstrap (`transparent`/trắng, trùng ngẫu nhiên
  > với nền trang ở light mode). Khi bật `data-bs-theme="dark"`, Bootstrap 5.3+ tự đổi `--bs-card-bg`
  > sang xám riêng của nó (`rgb(33,37,41)`) — KHÁC với token `--sb-card` (`#1c1c2b`) mà navbar và các
  > khối khác trong site đang dùng, làm card bị lệch tông so với phần còn lại của trang dù cả hai đều
  > "nền tối". Đã sửa: `.card` khai báo tường minh `background: var(--sb-card)`, không dựa vào biến
  > Bootstrap suy ra. Rút kinh nghiệm cho các rule CSS sau này: bất kỳ chỗ nào muốn nền theo đúng thiết
  > kế riêng của site (không phải theo mặc định Bootstrap) đều phải set tường minh qua token `--sb-*`,
  > không được để trống rồi trông chờ Bootstrap suy ra đúng ý.

### 15.2 Tìm kiếm/lọc khoản chi

`GET /groups/{id}/expenses` nhận thêm 6 query param optional, tất cả áp dụng **trước** phân trang
(lọc rồi mới đếm `TotalCount`/`Skip`/`Take`, không phải lọc trên 1 trang đã cắt sẵn):

- `title` — so khớp kiểu "chứa" (`Contains`), không phân biệt hoa/thường.
- `payerMemberId` — chỉ trả khoản chi có member này trong `Payers` (không xét `Splits`).
- `fromDate`/`toDate` — lọc theo `OccurredAt`, cả hai đều dạng so sánh `>=`/`<=` (bao gồm biên).
- `minAmount`/`maxAmount` — lọc theo `TotalAmount`.

Field nào không truyền thì bỏ qua tiêu chí đó — gọi không kèm param nào giữ nguyên hành vi cũ (xem
tất cả). Đóng gói trong `ExpenseFilter` (record, `SplitBill.Application.Expenses`), truyền xuống
`IExpenseRepository.GetPagedAsync`.

> ⚠️ Lưu ý kỹ thuật: lọc `Title` dùng `e.Title.ToLower().Contains(...)` chứ không dùng
> `EF.Functions.Like`, vì cần dịch được sang cả hai provider EF Core project đang dùng — SQL Server
> (production) và InMemory (unit/integration test, CLAUDE.md mục 2) — cùng cho ra kết quả không phân
> biệt hoa/thường ở cả hai.

Web (`Expenses/Index.cshtml`) có form lọc `method="get"` (title, dropdown người ứng tiền lấy từ
`Group.Members`, khoảng ngày, khoảng số tiền) — dùng GET để kết quả lọc có URL riêng, bookmark/chia
sẻ được, và nút Trang trước/sau chỉ cần lặp lại đúng `asp-route-*` hiện tại. Trang cũng được bổ sung
điều hướng Trang trước/sau (trước đây trang Expenses/Index chỉ hiển thị số trang dạng text, không có
nút bấm chuyển trang thật).

### 15.3 Nhãn/danh mục khoản chi

Thêm `Expense.Category` (`ExpenseCategory` enum, `Other = 0` mặc định) — danh sách **cố định**, không
cho người dùng tự đặt nhãn tùy ý, để bảng thống kê chi tiêu sau này (M6+) có trục phân loại nhất quán
thay vì tràn lan nhãn tự do khó gộp nhóm:

```csharp
enum ExpenseCategory { Other = 0, Food = 1, Transport = 2, Accommodation = 3, Entertainment = 4, Shopping = 5 }
```

- `Other = 0` để mọi `Expense` tạo trước tính năng này tự động rơi vào đây sau migration
  (`AddColumn<int>` default `0`), không cần script backfill riêng.
- `CreateExpenseRequest`/`UpdateExpenseRequest` nhận thêm `Category` (string, optional) — null/rỗng
  mặc định "Other"; giá trị không khớp tên enum nào ném `ValidationFailed` (không âm thầm rơi về
  Other, để người dùng biết ngay nếu gõ sai tên khi gọi thẳng API).
- `ExpenseFilter` (mục 15.2) có thêm field `Category` — lọc theo tên enum, nhưng ở tầng lọc một giá
  trị không hợp lệ được **khoan dung** (coi như không lọc) thay vì ném lỗi, khác với lúc tạo/sửa —
  chủ đích: một filter sai không nên chặn xem danh sách, trong khi một lần lưu dữ liệu sai thì nên
  báo ngay.
- `SplitBill.Application.Common.ExpenseCategoryOptions` — danh sách nhãn tiếng Việt + icon dùng
  chung cho cả 3 trang Web (Create/Edit/Index) và có thể tái dùng cho CSV/thống kê sau này, tránh khai
  báo trùng mảng nhãn ở nhiều nơi (theo đúng mẫu `SupportedCurrencies` ở mục 14).
- `GET /groups/{id}/export/expenses.csv` (mục 8) có thêm cột "Danh mục".
- Web: dropdown Danh mục trên form Create/Edit (`asp-for` + `<option>` liệt kê thủ công — ASP.NET Core
  `SelectTagHelper` tự đánh dấu `selected` đúng theo giá trị hiện tại của model dù không dùng
  `asp-items`, đã dùng sẵn mẫu này cho `Group.Currency`/`Group.Type`); cột "Danh mục" + dropdown lọc
  trên `Expenses/Index`.

Đã verify sống trên trình duyệt: tạo khoản chi với Category "Shopping" → hiển thị đúng "🛍️ Mua sắm"
trong danh sách (khoản chi cũ tạo trước tính năng này hiển thị đúng "📦 Khác"); lọc theo Category
"Shopping" → chỉ còn đúng khoản chi vừa tạo; mở lại form Edit của khoản chi đó → dropdown Danh mục
đã pre-select đúng "Shopping".

### 15.4 Bảng tổng quan cá nhân ở trang chủ

`IBalanceService.GetMyOverviewAsync(callerUserId)` — số dư của user hiện tại trong **TỪNG** nhóm họ
đang là thành viên `IsActive`, trả `PersonalGroupBalanceDto(GroupId, GroupName, Currency, Net)` cho
mỗi nhóm. Cài đặt tận dụng lại `IGroupRepository.GetByUserIdAsync` (đã lọc đúng nhóm + `Include`
`Members` sẵn có từ trước) và `ComputeBalancesAsync` private method sẵn có trong `BalanceService`,
không viết lại logic tính balance.

> ⚠️ Quyết định thiết kế quan trọng: **KHÔNG cộng gộp `Net` thành một con số tổng duy nhất** giữa các
> nhóm — vì mỗi nhóm có thể dùng đơn vị tiền tệ khác nhau (CLAUDE.md mục 14, quyết định người dùng
> 2026-09-05 "mỗi nhóm 1 loại tiền cố định"). Cộng thẳng `+100.000đ` của nhóm VND với `-50$` của nhóm
> USD ra một con số vô nghĩa và có thể đánh lừa người dùng tưởng mình đang dư/nợ ít hơn thực tế. Trang
> chủ hiển thị **danh sách riêng từng nhóm**, mỗi dòng tự định dạng theo đúng `Currency` của nhóm đó
> (`SupportedCurrencies.Format`), không có dòng "tổng" nào gộp chúng lại.

API: `GET /api/v1/users/me/balances-overview` (đặt trong `UsersController` vì đây là dữ liệu tổng hợp
theo **user**, không theo group — khác các endpoint `/groups/{id}/balances` hiện có).

Web: `Pages/Index.cshtml` — widget "Tổng quan số dư của bạn" chỉ hiện khi đã đăng nhập VÀ có ít nhất
1 nhóm; mỗi nhóm là 1 card link thẳng tới `/Groups/Balances/{id}`, tái dùng class
`.sb-balance-card`/`.positive`/`.negative` đã có sẵn từ trang Balances (mục 10b) để đồng bộ giao diện.
Gọi API lỗi (vd token vừa hết hạn) chỉ ẩn lặng lẽ widget này, không chặn phần còn lại của trang chủ.

Đã verify sống trên trình duyệt: đăng nhập user có 2 nhóm ("Trip to NYC" - USD, "Test Receipt" - VND)
→ widget hiện đúng cả 2 dòng, mỗi dòng format đúng theo Currency riêng, link điều hướng đúng
`/Groups/Balances/{id}` của từng nhóm.

### 15.5 Timeline hoạt động nhóm

Phát hiện quan trọng lúc thiết kế: đề bài gốc là "gộp Expense + Settlement + audit log thành 1 dòng
thời gian" — nhưng bảng `AuditLog` (đã có sẵn từ M1, ghi mọi `Created`/`Updated`/`Deleted` trên
`Expense`/`Settlement`/`GroupMember`/`Group` trong cùng transaction với thao tác gốc, CLAUDE.md mục 8
"Mọi thao tác ghi... phải sinh AuditLog") **chính là** nguồn dữ liệu đã-gộp-sẵn đó. Không cần viết
query hợp nhất 3 nguồn riêng — chỉ cần một tầng hiển thị biến `AuditLog.BeforeJson`/`AfterJson` (vốn
là JSON kỹ thuật, khó đọc trực tiếp) thành 1 câu tiếng Việt.

- `AuditLogDto` có thêm field `Summary` (string) — dựng sẵn ở `GroupService.BuildSummary` (Application
  layer, đúng quy ước mục 11 "không đặt logic nghiệp vụ ở Controller/View"), parse `Before`/`AfterJson`
  theo từng cặp `(EntityType, Action)` thành câu mô tả kèm tên/số tiền cụ thể. Parse lỗi (JSON không
  đúng shape, dữ liệu cũ) rơi về mô tả chung chung (`"{Action} {EntityType}"`) thay vì ném lỗi — một
  dòng lịch sử hỏng không được làm sập cả trang Timeline.
- Trang Web mới `Groups/Timeline.cshtml` (nút "🕒 Hoạt động" trên `Groups/Details`) chỉ gọi
  `GET /groups/{id}/audit-logs` (endpoint đã có từ đợt rà soát 2026-09-04) và render thẳng
  `Summary` — không tự suy diễn JSON ở Razor.

> ⚠️ Bug thật phát hiện + sửa khi làm tính năng này: mọi audit log `Action = "Deleted"` (Expense,
> Settlement, GroupMember) từ trước tới nay đều ghi `BeforeJson = null` — nghĩa là lịch sử chỉ biết
> "có 1 khoản chi/thanh toán/thành viên bị xóa" mà **không biết là cái nào** (tên khoản chi, số tiền,
> tên thành viên bị xóa đều mất, vì Delete không giống Update — không có "after" để suy ra, và before
> chưa từng được chụp). Đây là lỗ hổng có sẵn trong toàn bộ audit trail, không chỉ ảnh hưởng Timeline
> mới mà ảnh hưởng cả ý nghĩa ban đầu của bảng `AuditLog`. Đã sửa ở cả 3 nơi
> (`ExpenseService.DeleteAsync`, `SettlementRecordService.DeleteAsync`, `GroupService.RemoveMemberAsync`):
> chụp `before = ToDto(entity)` **trước khi** đánh dấu `IsDeleted`/`IsActive = false`, rồi truyền vào
> `WriteAuditLogAsync` thay vì `null`. Không sửa `Group.Deleted` (khi nhóm bị soft-delete, chính nhóm
> đó biến mất khỏi mọi query có `IsActive`/global query filter nên không còn cách nào xem lại timeline
> của nó — chụp before ở đây không có giá trị thực tế).

Đã verify sống trên trình duyệt: tạo nhóm mới → thêm 1 thành viên → thêm 1 khoản chi → xóa khoản chi đó
→ trang Timeline hiện đúng 4 dòng theo thứ tự mới nhất trước: "đã xóa khoản chi "X" (80.000đ)", "đã
thêm khoản chi "X" (80.000đ)", "đã thêm {tên} vào nhóm", "đã tạo nhóm" — xác nhận cả tên lẫn số tiền
khoản chi đã xóa hiển thị đúng (đúng bug đã sửa ở trên).

### 15.6 Tham gia nhóm qua link chia sẻ

Trước đây `GET /groups/shared/{shareToken}` chỉ cho xem (read-only, `[AllowAnonymous]`) — mọi thành
viên mới đều phải do Owner/Member hiện có gọi `POST /groups/{id}/members` thêm tay. Bổ sung
`POST /groups/shared/{shareToken}/join` — **yêu cầu đăng nhập** (khác hẳn endpoint xem ở trên), tự
thêm chính người gọi vào nhóm với `Role = Member`, `DisplayName` lấy theo tên tài khoản hiện tại
(không cần nhập tay vì đây là tự thêm mình, không phải Owner gõ tên hộ người khác như luồng cũ).

- `IGroupService.JoinViaShareTokenAsync(callerUserId, shareToken)` — ném `ALREADY_GROUP_MEMBER` nếu
  caller đã là thành viên `IsActive` của nhóm; ném `GROUP_NOT_FOUND` nếu `shareToken` sai/đã đổi.
  > ⚠️ Điểm kỹ thuật quan trọng: `GroupMember` có unique index `(GroupId, UserId)` lọc theo
  > `WHERE UserId IS NOT NULL` — **không** lọc thêm theo `IsActive` (CLAUDE.md mục 4.3). Nghĩa là nếu
  > 1 user đã từng là thành viên rồi rời nhóm (`IsActive = false`), hàng cũ với `UserId` đó **vẫn tồn
  > tại** và chặn insert 1 hàng `GroupMember` MỚI cùng `(GroupId, UserId)` — join lại kiểu tạo mới sẽ
  > ném `DbUpdateException` (vi phạm unique index) chứ không im lặng thành công. Vì vậy khi phát hiện
  > `existing` (dù `IsActive = false`), phải **kích hoạt lại CÙNG `GroupMemberId`** (`existing.IsActive
  > = true`) thay vì tạo `GroupMember` mới — nhờ vậy còn giữ nguyên được lịch sử `Expense`/`Settlement`
  > cũ đã gắn với `GroupMemberId` này từ trước khi rời nhóm.
  > Action ghi vào `AuditLog` cho lần tham gia lại này là `"Restored"` (đã có sẵn trong từ vựng Action ở
  > mục 4.1: `"Created"|"Updated"|"Deleted"|"Restored"`), KHÔNG dùng chung `"Updated"` với 1 lần đổi tên
  > thường — vì đổi tên chính mình (`UpdateMemberAsync` khi `target.Id == caller.Id`) cũng có
  > `ActorMemberId == EntityId` giống hệt trường hợp tham gia lại; nếu gộp chung Action, `BuildSummary`
  > (mục 15.5) không còn cách nào phân biệt 2 sự kiện khác hẳn nhau này.
- API: `POST /api/v1/groups/shared/{shareToken}/join` trong `GroupsController` — **không** có
  `[AllowAnonymous]` (khác `GetBySharedTokenAsync`), nên tự động yêu cầu JWT hợp lệ nhờ `[Authorize]`
  ở class.
- Web: `Public/Group.cshtml` — nút "+ Tham gia nhóm này" chỉ hiện khi `User.Identity.IsAuthenticated`;
  người xem ẩn danh thấy 2 nút Đăng nhập/Đăng ký, cả hai mang `returnUrl` trỏ về đúng trang chia sẻ này
  để quay lại tự động sau khi đăng nhập/đăng ký xong (`LoginModel`/`RegisterModel` đều đã hỗ trợ
  `ReturnUrl` — `RegisterModel` trước đây CHƯA có, đã bổ sung tương tự `LoginModel`). Lỗi từ
  `OnPostJoinAsync` (vd đã là thành viên) hiển thị dạng banner **phía trên**, không thay thế toàn bộ nội
  dung trang — sửa 1 lỗi UI nhỏ phát hiện khi làm tính năng này: cấu trúc `if/else if` gốc giữa
  `ErrorMessage` và `Group` là loại trừ lẫn nhau, nhưng 2 field này có thể cùng khác null (lỗi tham gia
  NHƯNG nhóm vẫn xem được) — đổi thành 2 khối `if` độc lập.

Đã verify sống trên trình duyệt (tài khoản mới hoàn toàn, luồng đầy đủ): xem link chia sẻ khi chưa đăng
nhập → đúng chỉ thấy nút Đăng nhập/Đăng ký, không có nút Tham gia; bấm Đăng ký (mang `returnUrl`) →
đăng ký xong tự quay lại đúng trang chia sẻ, giờ đã thấy nút "+ Tham gia nhóm này"; bấm Tham gia →
chuyển thẳng tới `/Groups/Details/{id}` với thông báo "Bạn đã tham gia nhóm... với tên Joiner", danh
sách thành viên tăng từ 2 lên 3; trang Timeline hiện đúng dòng "Joiner đã tham gia nhóm qua link chia
sẻ"; bấm Tham gia lần 2 → hiện đúng lỗi "Bạn đã là thành viên của nhóm này" mà trang vẫn hiện được nội
dung nhóm bên dưới (không bị thay thế hoàn toàn).

### 15.7 Khoản chi định kỳ

`GroupType.Recurring` từ nay có ý nghĩa thật: nhóm loại này có thể tạo **mẫu khoản chi định kỳ**
(`RecurringExpenseTemplate`) — giữ nguyên Title/TotalAmount/ExtraFeeAmount/SplitMode/SplitConfig/
Payers/Category/Note, tự động sinh 1 `Expense` mới mỗi khi tới hạn theo chu kỳ Daily/Weekly/Monthly.
Nhóm `OneTime` không được tạo mẫu (`GROUP_TYPE_NOT_RECURRING`).

**Kiến trúc 2 tầng, tách rõ "định nghĩa mẫu" khỏi "sinh khoản chi":**

- `IRecurringExpenseService` (CRUD mẫu — Create/GetByGroupId/Deactivate) — có `callerUserId`, chạy
  trong request HTTP bình thường như mọi service khác.
- `IRecurringExpenseRunner.RunDueTemplatesAsync(asOf, ct)` — quét mọi mẫu `IsActive` có
  `NextRunAt <= asOf`, tự sinh `Expense` + advance `NextRunAt`. **Không có `callerUserId`** vì đây là
  hành động hệ thống tự động, không phải ai đó đang đăng nhập gọi API. Nhận `asOf` làm tham số thay vì
  tự đọc `DateTimeOffset.UtcNow` bên trong — để unit/integration test kiểm được logic mà không phải
  chờ thời gian thật trôi qua (chỉ cần đặt `NextRunAt` của mẫu vào quá khứ so với `asOf` truyền vào).
- `RecurringExpenseBackgroundService` (`SplitBill.Api.BackgroundJobs`, `IHostedService`) — CHỈ lo vòng
  lặp `PeriodicTimer` (quét mỗi 1 giờ, cộng thêm 1 lượt quét ngay lúc khởi động để mẫu tới hạn từ trước
  khi restart không phải chờ tới 1 giờ), tự tạo `IServiceScope` mỗi lượt quét (bắt buộc vì
  `IRecurringExpenseRunner` là Scoped, không inject thẳng được vào 1 Singleton `BackgroundService`
  theo khuyến nghị chính thức của .NET). Toàn bộ logic nghiệp vụ nằm ở Runner, không nằm ở đây.

**Quy tắc "bù kỳ đã lỡ" (CATCH-UP):** nếu server tắt lâu ngày khiến `NextRunAt` bị lỡ nhiều kỳ (ví dụ
mẫu Daily nhưng lỡ mất 10 ngày), Runner **chỉ sinh đúng 1 `Expense`** cho lượt quét này — KHÔNG bù lại
10 khoản chi (tránh dồn cục gây hoảng cho người dùng), rồi nhảy `NextRunAt` thẳng tới kỳ hợp lệ tiếp
theo (lặp `Advance()` tới khi `> asOf`) chứ không chỉ +1 chu kỳ (nếu chỉ +1, mẫu sẽ vẫn "quá hạn" và
bị xử lý lại ngay ở lượt quét kế tiếp).

> ⚠️ Bug thật phát hiện + sửa lúc viết tính năng này: lần cài đặt đầu tiên của Runner ghi
> `AuditLog.AfterJson = null` cho khoản chi tự sinh (khác hẳn `ExpenseService.CreateAsync`, luôn ghi
> đầy đủ `ExpenseDto`) — do Runner không dùng chung `ExpenseService` (xem lý do tách ở trên) nên không
> có sẵn `ToDto()` để tái dùng. Hệ quả: Timeline hoạt động nhóm (mục 15.5) hiện dòng cụt lủn "đã thêm 1
> khoản chi" cho MỌI khoản chi tự sinh từ mẫu định kỳ — không có tên/số tiền, mất hết giá trị so với
> khoản chi tạo tay dù về bản chất đều là cùng 1 loại sự kiện `("Expense", "Created")`. Đã sửa: Runner
> tự dựng 1 `ExpenseDto` đầy đủ (copy các field vừa gán cho `Expense` entity) làm `AfterJson`, khớp
> đúng những gì `BuildSummary` (mục 15.5) mong đợi parse được.

Tái dùng logic đã có: `ExpenseCategoryParser` (tách từ `ExpenseService.ParseCategory` cũ thành
`SplitBill.Application.Common.ExpenseCategoryParser`, dùng chung cho cả `ExpenseService` và
`RecurringExpenseService` — tránh khai báo trùng luật "null/rỗng mặc định Other, tên sai thì báo lỗi").

Web: `Groups/RecurringExpenses.cshtml` — chỉ hiện nút "🔁 Khoản chi định kỳ" trên `Groups/Details` khi
`Group.Type == "Recurring"`; form tạo mẫu tái dùng nguyên UI "Cách chia" (radio SplitMode + bảng thành
viên + form Itemized) từ `Expenses/Create.cshtml`, chỉ thay `OccurredAt` bằng cặp `Interval` (dropdown
Daily/Weekly/Monthly) + `FirstRunAt` (ngày sinh khoản chi đầu tiên).

Đã verify sống trên trình duyệt: tạo nhóm loại "Dùng lại nhiều lần" → nút "🔁 Khoản chi định kỳ" hiện
đúng (nhóm `OneTime` khác không có nút này, và cố tình truy cập thẳng URL bị chặn với thông báo "Chỉ
nhóm loại \"Dùng lại nhiều lần\" mới có khoản chi định kỳ", trang Details vẫn hiển thị được bên dưới);
tạo mẫu "Tiền nhà tháng" 4.000.000đ chu kỳ Monthly → hiện đúng trong danh sách mẫu với trạng thái "Đang
chạy"; bấm "Tắt" → chuyển đúng thành "Đã tắt", nút Tắt biến mất. Riêng phần Runner tự sinh Expense theo
lịch (không thể chờ thật 1 giờ để quan sát trực tiếp) được phủ bởi 10 integration test chạy trực tiếp
`RunDueTemplatesAsync` với `asOf` giả lập, bao gồm cả case bù kỳ đã lỡ và Timeline hiển thị đầy đủ.

### 15.8 Nhắc nợ tự động

**Quyết định phạm vi quan trọng:** đề bài gốc là "nhắc khi Expense hoặc Settlement chưa xác
nhận/chưa thanh toán quá N ngày". Sau khi thiết kế, tính năng CHỈ triển khai đúng 1 trường hợp:
**Settlement còn `Pending` quá lâu** (nhắc người NHẬN — `ToMember` — xác nhận/từ chối). Cố tình
**không** triển khai "nhắc thành viên có số dư âm chung chung" — lý do: số dư (`net`) của 1 thành viên
được cộng dồn từ nhiều `Expense` qua nhiều thời điểm khác nhau (mục 6.1), không có 1 mốc "bắt đầu nợ"
duy nhất để tính "quá N ngày" một cách rõ ràng và nhất quán — mọi cách quy ước mốc đó (ví dụ "kể từ
expense gần nhất khiến net < 0") đều tùy tiện và dễ gây hiểu lầm hơn là hữu ích. Ngược lại, mỗi
`Settlement` có `CreatedAt` là mốc thời gian rõ ràng, duy nhất, không tranh cãi — nên chỉ trường hợp
này được triển khai.

- `Settlement.LastReminderSentAt` (nullable) — lần gần nhất đã nhắc; `null` = chưa từng nhắc. Dùng để
  vừa tránh nhắc lại ngay lập tức mỗi lượt quét, vừa cho phép **nhắc LẶP LẠI** mỗi khi qua thêm 1 chu
  kỳ (`DebtReminderRunner.ReminderInterval`, chốt 3 ngày) nếu settlement vẫn còn Pending — không phải
  chỉ nhắc đúng 1 lần duy nhất trong toàn bộ vòng đời.
- Điều kiện tới hạn: `Status == Pending && (LastReminderSentAt ?? CreatedAt) <= asOf - ReminderInterval`
  (`ISettlementRepository.GetPendingDueForReminderAsync`).
- `IDebtReminderRunner.RunDueRemindersAsync(asOf, ct)` — cùng mẫu thiết kế với
  `IRecurringExpenseRunner` (mục 15.7): nhận `asOf` làm tham số thay vì tự đọc `UtcNow`, để test được
  mà không cần chờ thời gian thật. Người nhận là khách vãng lai (không có `User`) vẫn được cập nhật
  `LastReminderSentAt` (dù không gửi được thông báo nào) — nếu không, settlement đó sẽ mãi bị coi là
  "chưa từng nhắc" và bị quét lại vô ích mỗi lượt.
- `DebtReminderBackgroundService` (`SplitBill.Api.BackgroundJobs`) — quét mỗi 6 giờ (đủ mịn so với
  ngưỡng ngày của `ReminderInterval`, không cần quét sát giờ như khoản chi định kỳ), cùng mẫu
  `PeriodicTimer` + quét ngay lúc khởi động như `RecurringExpenseBackgroundService`.
- Dùng chung `INotificationService`/kênh email đã có (mục 13) — loại thông báo mới `"SettlementReminder"`.

Đã verify sống, đầy đủ vòng đời thật: ghi nhận 1 settlement Pending (Joiner → Timeline Tester,
75.000đ) → lùi `CreatedAt` của đúng bản ghi đó 4 ngày bằng `sqlcmd` trực tiếp trên LocalDB (mô phỏng
"đã Pending 4 ngày" mà không cần chờ thật) → khởi động lại Api (kích hoạt lượt quét ngay lúc khởi động)
→ log xác nhận đã `UPDATE Settlements SET LastReminderSentAt`, ghi `Notification` mới, và
`ConsoleEmailSender` log đúng email "Nhắc xác nhận thanh toán" gửi tới đúng địa chỉ người nhận → đăng
nhập lại đúng tài khoản người nhận, trang `/Notifications` hiện đúng cả 2 thông báo (thông báo gốc
"Có người ghi nhận đã chuyển tiền" lúc tạo + thông báo nhắc mới).

### 15.9 Xuất PDF tổng kết chuyến đi

Quyết định kỹ thuật: trang HTML thuần `Groups/Summary.cshtml`, in qua `window.print()` của trình
duyệt (người dùng chọn "Lưu dưới dạng PDF" trong hộp thoại in để có file PDF) — **không** thêm thư
viện sinh PDF phía server, giữ đúng nguyên tắc mục 2 "không thêm NuGet ngoài danh sách nếu chưa hỏi
người dùng".

Nội dung trang: tổng chi tiêu + số khoản chi + số thành viên, **chi tiêu theo danh mục** (tận dụng
nhãn/danh mục khoản chi ở mục 15.3 — đúng ý "paving the way for future spending stats" đã nêu khi làm
tính năng đó), số dư từng người, kế hoạch thanh toán, và danh sách đầy đủ mọi khoản chi. Trang tự gộp
toàn bộ các trang phân trang của `GET /groups/{id}/expenses` (API giới hạn tối đa 100/trang) để không
bỏ sót khoản chi nào nếu nhóm có hơn 100 khoản.

CSS `@media print` (site.css) ẩn `header`, `.footer`, và mọi phần tử `.no-print` (nút "In/Lưu PDF",
dòng chữ chân trang) khi in — chỉ nội dung tổng kết xuất hiện trên bản in/PDF, không có navbar hay nút
bấm không cần thiết trong file PDF cuối cùng.

Đã verify sống trên trình duyệt: nhóm có 0 khoản chi (đã xóa hết) → trang hiện đúng "Chưa có khoản chi
nào"/"Chưa có khoản chi/thanh toán nào được ghi nhận nên không có số dư để hiển thị" thay vì bảng
trống; thêm 1 khoản chi "Ăn sáng chung" 150.000đ danh mục Ăn uống, chia 3 người, 1 người ứng toàn bộ →
trang hiện đúng: tổng 150.000đ, breakdown danh mục "🍜 Ăn uống 150.000đ", số dư đúng (+100.000đ người
ứng, -50.000đ mỗi người còn lại), kế hoạch thanh toán đúng 2 giao dịch, danh sách khoản chi đúng 1
dòng; nút "🖨️ In / Lưu PDF" gọi đúng `window.print()`; xác nhận CSS `@media print` đã tải (đọc trực
tiếp qua `document.styleSheets`) và 2 phần tử `.no-print` tồn tại đúng vị trí.

---

**Toàn bộ 9 tính năng ở mục 15 đã hoàn thành (2026-09-05)**, theo đúng yêu cầu người dùng "Làm hết các
chức năng gợi ý trên" sau khi M6 hoàn thành 100%.
