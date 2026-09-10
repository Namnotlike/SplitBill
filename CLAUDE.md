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
| Test | xUnit + FluentAssertions + EF Core InMemory (unit) / Testcontainers hoặc LocalDB (integration). E2E: Microsoft.Playwright (bổ sung 2026-09-08, quyết định người dùng — xem mục 24) |
| Log | Serilog |
| API docs | Swashbuckle (Swagger) |
| Password hashing | `Microsoft.AspNetCore.Identity.PasswordHasher<User>` (chỉ dùng riêng class hasher, KHÔNG cài toàn bộ ASP.NET Core Identity/EF Identity) |
| Gửi email | MailKit (bổ sung 2026-09-05, quyết định người dùng — xem mục 13.3) |
| Web Push | `WebPush` 1.0.13 (bổ sung 2026-09-09, quyết định người dùng — xem mục 25.7) |

Không thêm thư viện NuGet nào ngoài danh sách trên nếu chưa hỏi người dùng.

**Cấu hình JWT (chốt cụ thể, tránh mỗi lần code lại đoán):**
- Access token: thời hạn 30 phút, ký HS256, claim tối thiểu `sub` (UserId), `email`.
- Refresh token: chuỗi random 256-bit, thời hạn 14 ngày, lưu **hash** (SHA-256) trong bảng `RefreshToken` (xem 4.4), không lưu plaintext. Mỗi lần refresh thì thu hồi token cũ, phát token mới (rotation).
- Password reset token (bổ sung 2026-09-07, xem mục 16): cùng cơ chế random 256-bit + hash SHA-256 (bảng `PasswordResetToken`) như refresh token, nhưng thời hạn ngắn hơn nhiều — mặc định 30 phút — và chỉ dùng được ĐÚNG 1 LẦN (khác refresh token, có thể refresh nhiều lần trước khi hết hạn).
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

> ⚠️ **Lỗ hổng bảo mật/nghiệp vụ phát hiện + sửa qua `security-review` (2026-09-07):** "phải thuộc
> `GroupId`" ở trên (`ValidateMembersBelongToGroup`) trước đây chỉ kiểm tra thành viên còn tồn tại
> trong `group.Members`, **không lọc `GroupMember.IsActive`** — một thành viên đã rời nhóm (chỉ rời
> được khi `net == 0` tại thời điểm rời) vẫn có thể bị gán làm payer/split MỚI qua `POST/PUT
> /expenses` hoặc `POST /groups/{id}/recurring-expenses` nếu ai đó biết `GroupMemberId` cũ của họ —
> tái tạo đúng lỗ hổng "nợ ma" đã sửa cho Recurring Expense Runner (mục 15.7) nhưng qua đường thủ
> công. Người đã rời nhóm không còn xem được `/groups/{id}/balances` (yêu cầu caller đang active) nên
> không có cách nào biết/tranh chấp.
>
> **Đã sửa** bằng `ValidateMembersAreActive` (thêm cạnh `ValidateMembersBelongToGroup` trong cả
> `ExpenseService` và `RecurringExpenseService` — 2 bản riêng, không dùng chung vì 2 class độc lập):
> mọi payer/split phải là thành viên đang active, ném `403 MEMBER_NOT_ACTIVE` nếu không. **Ngoại lệ
> quan trọng cho `PUT /expenses/{id}`:** chỉ áp luật này cho tham chiếu **MỚI** — một `GroupMemberId`
> đã có mặt trong `Payers`/`Splits` GỐC của chính khoản chi đó (trước khi sửa) được miễn trừ, vì `PUT`
> luôn gửi lại TOÀN BỘ `Payers`/`Splits` (không phải patch từng phần); nếu áp luật cho cả tham chiếu
> cũ, mọi khoản chi lịch sử có 1 người tham gia đã rời nhóm sau đó sẽ **vĩnh viễn không sửa được nữa**
> dù chỉ đổi `Title`/`Note` — một hồi quy nghiêm trọng hơn cả lỗ hổng đang sửa. `RecurringExpenseTemplate`
> không có API Update (chỉ Create/Deactivate) nên không cần khái niệm "tham chiếu cũ được miễn trừ".
> Test: `CreateAsync_MemberLeftGroup_ThrowsMemberNotActive`,
> `UpdateAsync_AddsNewReferenceToMemberWhoLeftGroup_ThrowsMemberNotActive`,
> `UpdateAsync_KeepsExistingReferenceToMemberWhoLeftGroup_Succeeds` (`ExpenseServiceTests`);
> `CreateAsync_MemberLeftGroup_ThrowsMemberNotActive` (`RecurringExpenseTests`).

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
POST   /auth/forgot-password Body: { email } → luôn trả 204, không tiết lộ email tồn tại hay không
                             (mục 16). Nếu khớp tài khoản, gửi email chứa link đặt lại mật khẩu.
POST   /auth/reset-password Body: { token, newPassword } → đổi mật khẩu bằng token nhận qua email
                             (mục 16). Sai/hết hạn/đã dùng → 400 INVALID_RESET_TOKEN.
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

> ⚠️ **Bug thật thứ 2 phát hiện trong cùng class, lần này qua CI chạy trên GitHub Actions
> (`ubuntu-latest`) thật lần đầu tiên (2026-09-08 — xem mục 24.6):** `CsvBuilder.AddRow` dùng
> `StringBuilder.AppendLine(...)`, nối `Environment.NewLine` — `"\r\n"` trên Windows (mọi lần
> `dotnet test` trước đó đều chạy trên máy dev Windows nên không bao giờ lộ ra) nhưng CHỈ `"\n"` trên
> Linux. RFC 4180 (chuẩn mà chính class này ghi trong doc comment là tuân theo) **bắt buộc** dòng CSV
> kết thúc bằng CRLF bất kể hệ điều hành server đang chạy — nghĩa là đây không chỉ là lỗi làm 4 test
> trong `CsvBuilderTests` fail trên CI, mà là bug thật ảnh hưởng production: container `SplitBill.Api`
> chạy Linux (`docker-compose.yml`, mục 10b) trước nay vẫn xuất CSV sai chuẩn CRLF cho người dùng thật,
> chỉ là chưa ai (kể cả bộ test) từng chạy trên môi trường non-Windows để phát hiện ra. Đã sửa: nối
> cứng `"\r\n"` bằng `Append(...).Append("\r\n")`, không dùng `AppendLine`/`Environment.NewLine` — cùng
> nguyên tắc "không bao giờ dựa vào giá trị ambient của môi trường chạy để quyết định format output"
> đã áp dụng cho `SupportedCurrencies.Format` (mục 14.2, cũng từng dính đúng lớp lỗi này với
> `CultureInfo.CurrentCulture`). Đây là bằng chứng cụ thể cho giá trị của việc thật sự chạy CI trên
> runner khác OS với máy dev, không chỉ viết workflow rồi tin là đúng.

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

> ⚠️ **Lỗ hổng bảo mật/nghiệp vụ phát hiện + sửa qua `security-review` (2026-09-07):**
> `ProcessTemplateAsync` trước đây gán thẳng `ExpensePayer`/`ExpenseSplit` từ các `GroupMemberId`
> đóng băng trong `PayersJson`/`SplitConfigJson` lúc tạo mẫu, **không kiểm tra các thành viên đó còn
> `IsActive` trong nhóm hay không** — khác với đường tạo Expense tương tác (`ExpenseService`,
> `RecurringExpenseService.CreateAsync`) vốn validate qua `ValidateMembersBelongToGroup` (dù validate
> đó cũng chỉ kiểm tra "còn tồn tại trong `group.Members`", không lọc `IsActive` — một lỗ hổng rộng
> hơn, lúc phát hiện đã cố tình CHƯA sửa ngay vì là quyết định thiết kế khác, ngoài phạm vi lần sửa
> này; **đã sửa riêng ngay sau đó cùng ngày — xem mục 5.4**). Một thành viên chỉ rời được nhóm khi
> `net == 0` **tại thời điểm rời**
> (`GroupService.RemoveMemberAsync`), nhưng không có gì ngăn 1 mẫu định kỳ tiếp tục gán tiền cho họ ở
> lần chạy sau đó — và sau khi rời, họ không còn xem được `/groups/{id}/balances` (yêu cầu caller đang
> active) nên hoàn toàn không biết/không tranh chấp được khoản nợ "ma" này.
> **Quyết định người dùng (2026-09-07): "tắt mẫu + báo nhóm"** thay vì âm thầm bỏ qua thành viên đó
> hay vẫn sinh khoản chi. Đã sửa: `ProcessTemplateAsync` gom mọi `GroupMemberId` được tham chiếu
> (Payers + mọi hình thức `SplitConfigInput`: MemberIds/Shares/Percentages/ExactAmounts/Items), nếu có
> bất kỳ id nào không còn active → **không sinh Expense**, đặt `template.IsActive = false`, gửi thông
> báo loại `"RecurringTemplateDeactivated"` cho mọi thành viên active còn lại (có tài khoản) để họ chủ
> động tạo lại mẫu (loại bỏ người đã rời) nếu vẫn muốn dùng tiếp. `IRecurringExpenseRunner.
> RunDueTemplatesAsync` vẫn trả về đúng số Expense THẬT SỰ được sinh — mẫu bị tắt không tính vào số
> đếm này. Test: `RunDueTemplatesAsync_TemplateReferencesMemberWhoLeftGroup_
> DeactivatesTemplateInsteadOfCreatingExpense` (`RecurringExpenseTests`).

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

---

## 16. Quên mật khẩu — bổ sung 2026-09-07

Sau khi mục 15 hoàn thành, người dùng yêu cầu gợi ý tính năng mới; trong lúc rà soát để gợi ý, phát
hiện `/auth/*` chỉ có `register/login/refresh/logout` — **chưa từng có luồng "quên mật khẩu"**, một
thiếu sót thực tế (không phải tính năng "thêm cho vui") vì app đã có sẵn kênh gửi email (MailKit, mục
13.3) nhưng chưa dùng cho việc này. Người dùng chốt làm mục này trước tiên, trước khi làm tiếp các gợi
ý còn lại.

### 16.1 Mô hình dữ liệu

```csharp
PasswordResetToken {
    Guid Id
    Guid UserId
    string TokenHash        // SHA-256 của token plaintext, KHÔNG lưu plaintext — cùng mẫu RefreshToken
    DateTimeOffset ExpiresAt
    DateTimeOffset? UsedAt  // token CHỈ DÙNG ĐƯỢC 1 LẦN — khác RefreshToken (refresh được nhiều lần
                             // trước khi hết hạn), dùng rồi thì dù còn hạn cũng không dùng lại được
    DateTimeOffset CreatedAt
}
```

Không soft-delete, không cần đọc lại qua API (chỉ AuthService tự dùng nội bộ). `JwtOptions` có thêm
`PasswordResetTokenMinutes` (mặc định 30) — ngắn hơn nhiều so với `RefreshTokenDays` (14 ngày) vì đây
chỉ là "cửa sổ" để người dùng mở email và bấm link, không phải phiên đăng nhập.

### 16.2 API

```
POST /api/v1/auth/forgot-password   Body: { email } → luôn trả 204 dù email có tồn tại hay không
                                     (không tiết lộ — cùng nguyên tắc INVALID_CREDENTIALS ở mục 8).
                                     Guest (PasswordHash null) cũng coi như "không tìm thấy".
POST /api/v1/auth/reset-password    Body: { token, newPassword } → đổi mật khẩu. Token sai/hết hạn/
                                     đã dùng → 400 INVALID_RESET_TOKEN.
```

Đổi mật khẩu thành công **thu hồi TOÀN BỘ `RefreshToken` hiện có của user** (`IRefreshTokenRepository.
RevokeAllForUserAsync`) — đăng xuất mọi phiên khác, phòng trường hợp mật khẩu cũ đã bị lộ (đây chính
là kịch bản "quên/lộ mật khẩu", nên xử lý bảo thủ thay vì chỉ đổi PasswordHash rồi thôi).

> ⚠️ **Lỗ hổng bảo mật phát hiện + sửa qua `security-review` (2026-09-08):** dù response body của
> `POST /auth/forgot-password` luôn giống hệt nhau (204 rỗng) bất kể email có tồn tại hay không —
> đúng như mục đích "không tiết lộ" nêu trên — 2 nhánh xử lý lại tốn thời gian rất khác nhau: nhánh
> "không tồn tại/khách vãng lai" trả về gần như tức thì (1 câu SELECT), còn nhánh "tồn tại" phải ghi
> `PasswordResetToken` vào DB rồi gửi email — với `SmtpEmailSender` (mục 13.3) là cả 1 phiên SMTP thật
> qua mạng (connect + STARTTLS + auth + gửi), có thể mất hàng trăm mili-giây tới vài giây. Chênh lệch
> **độ trễ response** này là một kênh rò rỉ độc lập với nội dung response — kẻ tấn công đo thời gian
> phản hồi (không cần đọc response) vẫn dò được email nào đã đăng ký, phá vỡ đúng mục tiêu bảo mật đã
> nêu ở đầu mục 16.2. Đã sửa bằng cách áp **sàn thời gian tối thiểu chung** (500ms,
> `AuthService.ForgotPasswordMinDuration`) cho cả 2 nhánh — `Task.WhenAll(work, Task.Delay(500ms))`:
> nhánh nhanh luôn "chờ thêm" cho đủ sàn, nhánh chậm (đã gửi email) hiếm khi bị ảnh hưởng vì thường đã
> tốn hơn sàn này. Cân nhắc nhưng KHÔNG chọn phương án chuyển việc gửi email sang fire-and-forget (dù
> cũng loại bỏ được phụ thuộc thời gian) — vì `CancellationToken` của request có thể bị hủy ngay sau
> khi response trả về, làm email không gửi được nếu tách khỏi luồng chính mà không tự quản lý DI scope
> mới, phức tạp hơn hẳn so với lợi ích. Đây không phải giải pháp tuyệt đối (email gửi chậm bất thường
> vẫn có thể lộ), nhưng đưa case điển hình về gần như không phân biệt được — mức giảm thiểu thực tế cho
> lớp lỗi này. Test: `ForgotPasswordAsync_UnknownEmail_TakesAtLeastAsLongAsKnownEmail_NoTimingSideChannel`
> (`AuthServiceTests`).

### 16.3 Web

`Pages/Account/ForgotPassword.cshtml` (nhập email, POST) và `Pages/Account/ResetPassword.cshtml`
(`?token=...` từ link email, nhập mật khẩu mới 2 lần). Link "Quên mật khẩu?" thêm vào `Login.cshtml`
cạnh ô mật khẩu. `ForgotPassword` luôn hiện đúng 1 thông báo chung chung dù email tồn tại hay không
(kể cả khi API trả lỗi validate) — không được để lộ oracle cho việc dò email tồn tại qua khác biệt
UI. `ResetPassword` thành công thì mời đăng nhập lại ngay (không tự đăng nhập hộ — người dùng vừa đổi
mật khẩu, để họ tự gõ lại mật khẩu mới là xác nhận hợp lý).

> ⚠️ **Bug thật phát hiện + sửa cùng lúc làm tính năng này (2026-09-07):** `NotificationService.
> BuildHtmlBody` (mục 13) từ trước tới nay luôn gửi `LinkUrl` dạng **ĐƯỜNG DẪN TƯƠNG ĐỐI**
> (vd `/Expenses/Index/{groupId}`) thẳng vào `<a href>` của email. Một URL tương đối không có nghĩa gì
> khi mở từ 1 email client (không có "trang hiện tại" nào để tính tương đối theo) — nghĩa là **mọi**
> link "Xem chi tiết" trong **mọi** email thông báo (khoản chi mới, settlement, nhắc nợ...) từ trước
> tới nay đều là link hỏng khi bấm trực tiếp từ ứng dụng email, chỉ tình cờ chưa bị phát hiện vì chưa
> có email nào THỰC SỰ cần người dùng bấm link để hoàn tất 1 hành động — "quên mật khẩu" là ca đầu
> tiên bắt buộc phải có link hoạt động được, nên lỗi lộ ra ngay khi viết test. Đã sửa: thêm
> `SplitBill.Application.Common.WebOptions` (`Web:BaseUrl` trong appsettings, mặc định
> `http://localhost:5103` khớp cổng Web — mục 10b), `NotificationService` giờ ghép `BaseUrl` + đường
> dẫn tương đối thành URL TUYỆT ĐỐI CHỈ khi dựng nội dung email; cột `Notification.LinkUrl` lưu DB
> (dùng cho trang `/Notifications` same-origin) không đổi, vẫn tương đối. Test:
> `NotifyAsync_WithLinkUrl_EmailBodyUsesAbsoluteUrl_ButStoredNotificationKeepsRelativeUrl`
> (`NotificationServiceTests`).

Đã verify sống trên trình duyệt (luồng đầy đủ, không phải chỉ đọc code): đăng ký tài khoản mới → đăng
xuất → bấm "Quên mật khẩu?" ở trang Login → nhập email → hiện đúng thông báo chung chung "Nếu email
này có tài khoản..." → lấy link đặt lại mật khẩu từ log `ConsoleEmailSender` (chưa cấu hình SMTP thật)
→ xác nhận link đúng dạng TUYỆT ĐỐI (`http://localhost:5103/Account/ResetPassword?token=...`, không
phải đường dẫn tương đối) → mở link, nhập mật khẩu mới 2 lần → hiện đúng "Đổi mật khẩu thành công" →
đăng nhập lại bằng mật khẩu MỚI thành công, vào thẳng trang Nhóm của tôi.

---

## 17. Thống kê chi tiêu theo thời gian (biểu đồ) — bổ sung 2026-09-07

Hạng mục 2/8 trong danh sách gợi ý sau mục 16. Trang mới `Groups/Statistics.cshtml` — biểu đồ cột
"Chi tiêu theo thời gian" (tổng `TotalAmount` theo từng tháng, nhãn `MM/yyyy`) và biểu đồ tròn "Chi
tiêu theo danh mục" (tái dùng `ExpenseCategoryOptions`, mục 15.3), truy cập từ nút "📊 Thống kê" trên
`Groups/Details`.

**Không thêm API endpoint riêng** — `StatisticsModel.OnGetAsync` tái dùng đúng mẫu gộp-toàn-bộ-trang
đã có ở `SummaryModel` (mục 15.9, PDF tổng kết): gọi `GET /groups/{id}/expenses` lặp qua mọi trang
(API giới hạn tối đa 100/trang) rồi tự tính `GroupBy` theo tháng/danh mục ở tầng Web (C#), không cần
service/endpoint mới. Cả 2 trang cố tình dùng cùng quy ước "chỉ tính `TotalAmount`, không cộng
`ExtraFeeAmount`" cho breakdown theo danh mục, để không lệch số nếu người dùng đối chiếu 2 trang.

**Vẽ biểu đồ bằng Chart.js qua CDN** (`cdnjs.cloudflare.com`, cùng mẫu `qrcodejs` đã dùng ở
`SettlementPlan.cshtml` — mục 2 chỉ giới hạn NuGet phía backend, không áp dụng cho thư viện JS phía
client). Màu chữ/trục/viền đọc trực tiếp từ CSS custom properties `--sb-*` (`getComputedStyle` lúc
render) để biểu đồ tự thích ứng dark mode (mục 15.1) mà không cần vẽ lại — đã verify sống cả 2 theme.

> ⚠️ **Bug thật phát hiện lúc verify sống (2026-09-07):** version `4.4.4` của Chart.js dùng lúc viết
> code đầu tiên **không tồn tại trên cdnjs** (`GET .../Chart.js/4.4.4/chart.umd.min.js` trả về 404,
> body 548 byte là trang lỗi JSON của cdnjs, không phải file JS) — script tag "load" thành công về mặt
> HTML (không báo lỗi console vì trình duyệt coi 404 với `Content-Type` bất kỳ vẫn là response hợp lệ
> cho thẻ `<script>`, chỉ đơn giản không định nghĩa `window.Chart`) nên 2 canvas hiện trống trơn mà
> KHÔNG có lỗi nào trong console — trông như bug JS logic chứ không phải sai version. Chẩn đoán bằng
> `fetch()` trực tiếp từng version ứng viên từ console trang đang mở, tìm ra `4.4.1` mới là bản có
> thật (200, ~200KB). Đã sửa lại đúng version. **Bài học quy trình:** không đoán số version thư viện
> CDN từ trí nhớ rồi tin luôn — nếu không verify sống được ngay, ít nhất phải tự `fetch()`/`curl` xác
> nhận URL trả về 200 trước khi tin tưởng đưa vào code, vì lỗi này hoàn toàn im lặng (không exception,
> không log) và chỉ lộ ra khi nhìn trực tiếp vào trang render.
>
> Riêng dự án này cũng **không có Razor runtime compilation** (`Microsoft.AspNetCore.Mvc.Razor.
> RuntimeCompilation` chưa được cài — khác với static assets, vốn phục vụ trực tiếp từ đĩa khi
> `ASPNETCORE_ENVIRONMENT=Development`, xem mục 10b) — sửa file `.cshtml` khi server `SplitBill.Web`
> đang chạy **không** tự áp dụng, phải dừng và `dotnet run` lại mới thấy thay đổi. Từng nhầm tưởng
> fix không có tác dụng vì reload trang vẫn thấy version cũ.

Đã verify sống trên trình duyệt: nhóm 0 khoản chi → đúng "Chưa có khoản chi nào trong nhóm này để
thống kê" thay vì canvas trống; thêm 2 khoản chi khác danh mục ("Ăn uống" 100.000đ, "Di chuyển"
50.000đ, cùng tháng) → 3 thẻ số liệu đúng (Tổng 150.000đ, Số khoản chi 2, Số tháng có phát sinh 1),
biểu đồ cột hiện đúng 1 cột "09/2026" cao 150.000, biểu đồ tròn hiện đúng 2 phần theo tỉ lệ 2:1 kèm
icon+nhãn danh mục; bật dark mode (`localStorage['sb-theme']='dark'`) → chữ trục/legend đổi màu sáng
đọc được trên nền tối, không còn hiện tượng "chữ tối trên nền tối".

---

## 18. Nhân bản nhóm (Duplicate Group) — bổ sung 2026-09-07

Hạng mục 3/8 trong danh sách gợi ý sau mục 16. Dùng cho kịch bản "chuyến đi năm sau với cùng hội bạn"
mà không phải thêm lại từng người.

`POST /api/v1/groups/{id}/duplicate` — Body: `{ "name": null }` (rỗng/null tự đặt `"{Tên gốc} (bản
sao)"`). Tạo `Group` **MỚI HOÀN TOÀN**, copy `Name`/`Description`/`Type`/`Currency`/`SimplifyDebts` +
toàn bộ `GroupMember` đang `IsActive` (kể cả khách vãng lai) từ nhóm nguồn.

**Cố tình KHÔNG copy:** `Expense`, `Settlement`, `AuditLog`, `RecurringExpenseTemplate`, `ShareToken`
(nhóm mới luôn có `ShareToken` riêng do `IShareTokenGenerator` sinh mới), `IsArchived` (luôn `false`
dù nhóm nguồn đã lưu trữ). Đây là "nhân bản khung nhóm", không phải "sao lưu toàn bộ lịch sử" — nếu
copy cả Expense/Settlement thì bất biến `Σ net = 0` của nhóm mới sẽ vô nghĩa (nợ cũ đã settle ở nhóm
gốc không nên tự động "sống lại" ở nhóm mới).

**Quyền hạn:** người gọi chỉ cần là thành viên **đang active** của nhóm nguồn (không bắt buộc Owner)
— hành động này không ghi gì vào nhóm nguồn, chỉ đọc danh sách thành viên mà mọi thành viên vốn đã
xem được qua `GET /groups/{id}`. Người gọi luôn trở thành **Owner** của nhóm mới; mọi thành viên khác
được copy sang với Role **Member**, kể cả nếu họ là Owner ở nhóm nguồn — đơn giản hóa có chủ đích: nếu
nhóm nguồn có nhiều Owner, các Owner còn lại không tự động giữ quyền ở bản sao, có thể được cấp lại
qua `POST /groups/{id}/members/{memberId}/role` như bình thường (mục 4.4).

Web: nút "🧬 Nhân bản nhóm" trên `Groups/Details`, 1-click (không có form nhập tên riêng — đổi tên sau
ở trang Sửa nhóm nếu muốn), redirect thẳng sang trang Details của nhóm mới kèm thông báo thành công.

> ⚠️ **Bug thật phát hiện lúc verify sống bằng browser automation (2026-09-07):** nút "Nhân bản nhóm"
> lúc cài đặt đầu tiên có `onsubmit="return confirm('Tạo 1 nhóm mới...')"`. Gọi `form.requestSubmit()`
> qua CDP (`Runtime.evaluate`) khiến tab **treo cứng hoàn toàn** — dialog `confirm()` là native, chặn
> toàn bộ vòng lặp sự kiện của tab, đúng y cảnh báo đã biết về việc tránh trigger `alert`/`confirm`/
> `prompt` qua automation. Không phải lỗi logic nghiệp vụ, nhưng đủ nghiêm trọng để đổi thiết kế: bỏ
> hẳn `confirm()` — hành động này vốn **không phá hủy gì** (không đụng nhóm nguồn, lỡ tạo thì xóa nhóm
> mới đi là xong), nên không thực sự cần xác nhận, khớp các nút 1-click khác đã có trên trang (vd "Đổi
> link mới"). Rút kinh nghiệm chung: `window.confirm()`/`alert()`/`prompt()` không chỉ là vấn đề riêng
> của việc test tự động — chúng chặn cả JS lẫn mọi tương tác trang, nên chỉ dùng cho hành động THẬT SỰ
> phá hủy/không hoàn tác được (vd xóa vĩnh viễn), không dùng mặc định cho mọi nút quan trọng.

Đã verify sống trên trình duyệt (luồng đầy đủ): nhóm nguồn có 2 thành viên (Owner + 1 khách vãng lai)
→ bấm "Nhân bản nhóm" → chuyển thẳng tới nhóm mới "Nhom Thong Ke (bản sao)" kèm banner "✅ Đã nhân bản
thành nhóm mới..." → đúng 2 thành viên đã copy (Owner giữ nguyên vai trò Owner, khách vẫn là khách) →
trang Khoản chi của nhóm mới đúng "Chưa có khoản chi nào" (không copy dữ liệu tài chính, đúng thiết
kế).

---

## 19. Bình luận trên khoản chi — bổ sung 2026-09-07

Hạng mục 4/8 trong danh sách gợi ý sau mục 16. Thành viên thắc mắc/giải thích ngay dưới 1 `Expense`
(vd "sao khoản này tính cả phần của tao?") mà không cần rời trang.

### 19.1 Mô hình dữ liệu

```csharp
ExpenseComment {
    Guid Id
    Guid ExpenseId
    Guid AuthorMemberId        // GroupMemberId của người viết — LUÔN là chính caller đã đăng nhập,
                                // không cho viết hộ người khác (cùng nguyên tắc guest ở mục 4.4)
    string Content             // tối đa 2000 ký tự
    bool IsDeleted              // soft-delete, có HasQueryFilter — không phải dữ liệu tài chính
                                // (mục 1) nhưng vẫn theo quy ước soft-delete chung của app cho nhất
                                // quán + giữ được lịch sử nếu cần tra cứu sau này
    DateTimeOffset CreatedAt
}
```

Không có `UpdatedAt`/sửa bình luận — cố tình chỉ Create + Delete để giữ tính năng nhỏ gọn (MVP).

### 19.2 API

```
GET    /api/v1/expenses/{expenseId}/comments   Toàn bộ bình luận, sort CreatedAt tăng dần (không
                                                phân trang — số bình luận trên 1 khoản chi luôn nhỏ)
POST   /api/v1/expenses/{expenseId}/comments    Body: { content }
DELETE /api/v1/expense-comments/{commentId}
```

**Quyền hạn:** mọi thành viên đang active của nhóm đều bình luận được. Xóa thì **chỉ tác giả bình
luận hoặc Owner** của nhóm mới xóa được (403 `INSUFFICIENT_ROLE` nếu không đủ quyền) — khớp mẫu "đổi
tên người khác chỉ Owner" ở mục 4.4.

**Cố tình KHÔNG thêm sự kiện thông báo mới** cho bình luận — mục 13 đã chốt đúng 4 sự kiện kích hoạt
thông báo ("không hơn, không tự ý thêm sự kiện khác ngoài danh sách này"); bình luận không nằm trong
danh sách đó nên không trigger `NotificationService`.

`ExpenseCommentService` (namespace `SplitBill.Application.Expenses`) là service riêng, tách khỏi
`ExpenseService` đã lớn sẵn — tự có `LoadExpenseAsync`/`LoadGroupAsync`/`ResolveCallerMember` riêng
(chấp nhận trùng lặp nhỏ giữa các service, cùng mẫu `NotificationService`/`RecurringExpenseService`
đã áp dụng từ trước, thay vì kéo thêm 1 base class dùng chung).

### 19.3 Web

Phần "💬 Bình luận" nằm ở cuối trang `Expenses/Edit.cshtml` (không có trang "chi tiết khoản chi" riêng
— Edit là nơi tự nhiên nhất để xem/tương tác với 1 khoản chi cụ thể). Nút "Xóa" chỉ hiện với tác giả
hoặc Owner (`EditModel.MyMemberId`/`IsOwner`, tự suy từ claim `NameIdentifier` khớp `Group.Members`,
cùng mẫu đã dùng ở `SettlementPlan.cshtml.cs`).

Đã verify sống trên trình duyệt: mở khoản chi có 0 bình luận → đúng "Chưa có bình luận nào" → gửi 1
bình luận → hiện đúng "💬 Bình luận (1)" kèm tên tác giả + thời gian + nội dung + nút "Xóa" (vì mình
là tác giả) → bấm Xóa → quay lại đúng "Chưa có bình luận nào" (soft-delete hoạt động, query filter
loại bỏ đúng bản ghi đã xóa).

---

## 20. "Ai đang nợ tôi" tổng hợp xuyên nhóm — bổ sung 2026-09-07

Hạng mục 5/8 trong danh sách gợi ý sau mục 16. Khác widget "Tổng quan số dư của bạn" (mục 15.4, gộp
theo TỪNG NHÓM) — widget này gộp theo **TỪNG NGƯỜI**: "Bình nợ tôi 50k ở nhóm A và nợ tôi 30k ở nhóm
B" hiển thị chung dưới 1 thẻ "Bình", mỗi nhóm 1 dòng riêng.

### 20.1 Quyết định thiết kế quan trọng

**Chỉ áp dụng cho counterparty có tài khoản (`UserId` khác null).** Khách vãng lai không có danh tính
ổn định xuyên nhóm — "Bình (khách)" ở nhóm A và "Bình (khách)" ở nhóm B là 2 `GroupMember` độc lập
hoàn toàn, không có gì đảm bảo là cùng 1 người ngoài đời để gộp lại. Khoản nợ của khách vãng lai vẫn
xem được bình thường ở `settlement-plan` của đúng nhóm đó, chỉ là không xuất hiện ở widget xuyên nhóm
này.

**Không suy ra "ai nợ ai" từ Σ net riêng — tái dùng ĐÚNG giao dịch đã có trong settlement-plan của
từng nhóm** (`BuildSimplifiedPlanAsync`/`BuildDirectPlanAsync`, tùy `Group.SimplifyDebts` — mục 6.2/
6.5). Lý do: "ai nợ ai" chỉ có nghĩa xác định ở mức giao dịch cụ thể — khi `SimplifyDebts = true`,
thuật toán gộp nợ có thể route khoản nợ của 1 người qua người khác để giảm số lượt chuyển (mục 6.2),
nên 2 người có thể không có "quan hệ nợ trực tiếp" nào trong plan dù cả hai đều ở chung 1 nhóm — widget
này chỉ hiển thị đúng những gì `settlement-plan` ĐÃ quyết định là 1 giao dịch cụ thể giữa họ, không tự
suy ra một con số khác.

**Không cộng gộp số tiền giữa các nhóm** — mỗi nhóm có thể dùng 1 loại tiền tệ khác nhau (mục 14),
giữ nguyên danh sách riêng theo nhóm trong cùng 1 thẻ người (`CounterpartyBalanceDto.Groups`).

### 20.2 API

```
GET /api/v1/users/me/counterparty-balances
```

Trả `IReadOnlyList<CounterpartyBalanceDto>` — mỗi phần tử là 1 người (`CounterpartyUserId`,
`CounterpartyDisplayName`) kèm danh sách `Groups` (`GroupId`, `GroupName`, `Currency`, `Amount` —
dương = họ nợ mình, âm = mình nợ họ). `IBalanceService.GetCounterpartyBalancesAsync` cài đặt bằng
cách lặp qua mọi nhóm đang active (tái dùng `GetByUserIdAsync`, cùng nguồn dữ liệu với
`GetMyOverviewAsync` mục 15.4), với mỗi nhóm gọi lại chính 2 hàm private `BuildSimplifiedPlanAsync`/
`BuildDirectPlanAsync` đã có sẵn cho `/settlement-plan`, lọc ra giao dịch có mình là 1 trong 2 bên rồi
gộp theo `UserId` của bên còn lại.

### 20.3 Web

Trang chủ (`Pages/Index.cshtml`) thêm 1 thẻ "Ai đang nợ bạn / bạn đang nợ ai" ngay dưới widget tổng
quan cá nhân (mục 15.4), chỉ hiện khi đã đăng nhập và có ít nhất 1 counterparty. Mỗi người 1 thẻ nhỏ,
bên trong liệt kê từng nhóm kèm dòng chữ "Nợ bạn X" (dương, `.sb-amount-positive`) hoặc "Bạn nợ X"
(âm, `.sb-amount-negative`), bấm vào tên nhóm dẫn thẳng tới `/Groups/SettlementPlan` của đúng nhóm đó.
Lỗi gọi API chỉ ẩn lặng lẽ widget này (cùng nguyên tắc với widget mục 15.4), không chặn phần còn lại
của trang chủ.

Đã verify sống trên trình duyệt (luồng đầy đủ, 2 tài khoản thật): tạo tài khoản thứ 2 ("BinhTester"),
tham gia 2 nhóm khác nhau qua link chia sẻ (mục 15.6), tạo 1 khoản chi ở mỗi nhóm theo 2 CHIỀU khác
nhau (nhóm A: Owner ứng tiền, chiều ngược ở nhóm B: BinhTester ứng tiền) → trang chủ hiện đúng 1 thẻ
duy nhất "BinhTester" (không tách thành 2 thẻ riêng dù là 2 nhóm khác nhau — xác nhận gộp đúng theo
người), bên trong đúng 2 dòng theo 2 nhóm với chiều "Nợ bạn"/"Bạn nợ" ngược nhau đúng như đã tạo, mỗi
dòng đúng tên nhóm + số tiền theo đúng tiền tệ của nhóm đó.

---

## 21. Preset cách chia hay dùng — bổ sung 2026-09-07

Hạng mục 6/8 trong danh sách gợi ý sau mục 16. Lưu lại 1 kiểu chia (vd "Tôi & Bình chia đôi") để chọn
nhanh lúc tạo khoản chi mới, đỡ phải tick lại từng người mỗi lần.

### 21.1 Mô hình dữ liệu

```csharp
SplitPreset {
    Guid Id
    Guid GroupId
    string Name
    string SplitMode
    string SplitConfigJson     // cùng shape SplitConfigInput với Expense.SplitConfigJson (mục 4.1)
    Guid CreatedByMemberId
    bool IsDeleted              // soft-delete, có HasQueryFilter — cùng quy ước ExpenseComment (mục 19)
    DateTimeOffset CreatedAt
}
```

**Cố tình KHÔNG lưu `Payers`/`TotalAmount`** — chỉ phần "chia cho ai, theo tỉ lệ nào" là cái lặp lại
nhiều lần giữa các khoản chi khác nhau; "ai ứng tiền lần này, bao nhiêu tiền" luôn khác nhau nên nhập
mới mỗi lần, không có gì để lưu thành preset.

### 21.2 API

```
GET    /api/v1/groups/{groupId}/split-presets
POST   /api/v1/groups/{groupId}/split-presets    Body: { name, splitMode, splitConfig }
DELETE /api/v1/split-presets/{presetId}
```

**Quyền hạn:** mọi thành viên active đều tạo được. Xóa thì chỉ tác giả hoặc Owner (403
`INSUFFICIENT_ROLE` — khớp mẫu `ExpenseCommentService.DeleteAsync`, mục 19/4.4). Lúc tạo, mọi
`GroupMemberId` được tham chiếu trong `SplitConfig` phải **đang active trong nhóm** — 2 bước validate
tách riêng (`MEMBER_NOT_IN_GROUP` nếu không tồn tại, `MEMBER_NOT_ACTIVE` nếu tồn tại nhưng đã rời),
cùng nguyên tắc `ExpenseService.ValidateMembersAreActive` (mục 5.4); không có khái niệm "tham chiếu cũ
được miễn trừ" vì preset không có API Update, chỉ Create/Delete.

`SplitPresetService` (namespace `SplitBill.Application.Expenses`) là service riêng, tách khỏi
`ExpenseService`, cùng mẫu `ExpenseCommentService`/`NotificationService` đã áp dụng từ trước.

### 21.3 Web

Trang `Expenses/Create.cshtml` thêm khối "📁 Preset đã lưu" (nút áp dụng + nút "×" xóa cho mỗi preset)
và ô "Lưu cách chia hiện tại thành preset" + nút "💾 Lưu thành preset".

- **"Lưu thành preset" tái dùng logic server-side có sẵn** — nút dùng `formaction="?handler=SavePreset"`
  trỏ CÙNG 1 `<form>` chính (không lồng form riêng, HTML không cho phép), nên `OnPostSavePresetAsync`
  nhận được đúng `Input.SplitMode`/`Rows`/`Items` đã bind sẵn, gọi thẳng `ExpenseFormHelpers.
  BuildSplitConfig` đã có, không cần viết lại logic dựng SplitConfig ở phía client. Nút này (và nút
  xóa preset) có `formnovalidate` để không bị chặn bởi validate `required`/`min` của Title/TotalAmount
  — lưu/xóa preset không cần điền các trường đó.
- **"Áp dụng preset" là thao tác thuần client-side** (không round-trip server) — JS điền lại đúng các
  input `Rows[i].*`/`Items[i].*` đã render sẵn theo dữ liệu preset (tra hàng theo `MemberId` ẩn trong
  mỗi `<tr>`), dùng lại đúng cấu trúc DOM đã có, kể cả với món ăn (Itemized) — cùng mẫu khôi phục
  `SplitConfigJson` đã dùng ở `Expenses/Edit.cshtml` (mục 4.1). `addItemRow()` sửa lại để `return` phần
  tử vừa thêm (trước đây không trả về gì) — cần thiết để script điền tên/giá/người ăn vào đúng dòng vừa
  tạo.

Đã verify sống trên trình duyệt (luồng đầy đủ): lưu preset "Toi & Binh chia doi" (Equal, 2/3 thành
viên) → hiện đúng trong danh sách preset → bấm áp dụng ở lần tải trang MỚI (không phải cùng phiên vừa
lưu) → đúng 2 checkbox tự tick lại, người thứ 3 vẫn bỏ trống → tạo hẳn 1 khoản chi dùng preset này,
lưu thành công với đúng cách chia đã áp dụng → xóa preset → danh sách preset rỗng trở lại (xác nhận
qua cả giao diện lẫn log Api có đúng 1 lệnh `DELETE .../split-presets/{id}` trả `204`).

## 22. Đa ngôn ngữ (i18n) — bổ sung 2026-09-07

Hạng mục 7/8 trong danh sách gợi ý sau mục 16. Chỉ áp dụng cho **`SplitBill.Web`** (giao diện) — API
(`SplitBill.Api`) không đổi gì, mọi `errorCode` vẫn là hằng số tiếng Anh như từ đầu dự án (mục 8),
message tiếng Việt trả về từ API vẫn giữ nguyên (không có khái niệm "API trả lời theo ngôn ngữ client")
— việc dịch chỉ xảy ra ở tầng hiển thị Razor Pages.

### 22.1 Cơ chế: `IStringLocalizer`/`IViewLocalizer` + "tiếng Việt làm resource key"

Dùng thẳng cơ chế localization có sẵn của ASP.NET Core, không viết bộ dịch riêng:

- **Không tạo `Resources/*.vi.resx`** — chuỗi tiếng Việt hardcode ngay trong `.cshtml` (vd
  `@Localizer["Khoản chi"]`) đóng vai trò VỪA LÀ nội dung hiển thị mặc định VỪA LÀ resource key. Khi
  không tìm thấy bản dịch cho culture hiện tại (bao gồm cả culture mặc định `vi`), `IStringLocalizer`
  tự fallback về đúng chuỗi key truyền vào — tức là hiển thị nguyên văn tiếng Việt. Nhờ vậy không cần
  duy trì file `.vi.resx` song song (vốn sẽ trùng lặp 100% nội dung `.cshtml`, chỉ tổ khó đồng bộ khi
  sửa văn án), chỉ cần viết `Resources/**/*.en.resx` cho tiếng Anh.
- Mỗi trang `.cshtml` có file resource riêng, đường dẫn mirror y hệt cây thư mục `Pages/` (quy ước
  chuẩn của ASP.NET Core, tự động — không cấu hình gì thêm): `Pages/Index.cshtml` ↔
  `Resources/Pages/Index.en.resx`, `Pages/Account/Login.cshtml` ↔
  `Resources/Pages/Account/Login.en.resx`, v.v. Text dùng chung nhiều trang (navbar, footer, nút đổi
  ngôn ngữ) nằm ở `SharedResource.cs` (class marker rỗng) ↔ `Resources/SharedResource.en.resx`, tiêm
  qua `IStringLocalizer<SplitBill.Web.SharedResource>` trong `_Layout.cshtml`.
- `Program.cs` đăng ký `builder.Services.AddLocalization(o => o.ResourcesPath = "Resources")` +
  `AddRazorPages().AddViewLocalization()`, cấu hình `RequestLocalizationOptions` với 2 culture hỗ trợ
  (`vi` mặc định, `en`). **Cố tình CHỈ đăng ký `RequestCultureProviders = [new
  CookieRequestCultureProvider()]`** — KHÔNG dùng danh sách provider mặc định của framework
  (`QueryString` → `Cookie` → `AcceptLanguageHeader`). Nghĩa là query string `?culture=en` trên URL
  KHÔNG có tác dụng đổi ngôn ngữ (đã tự kiểm chứng: mở lại đúng 1 trang đang ở cookie `en` với
  `?culture=vi` trên URL, trang vẫn hiện tiếng Anh — vì query string không nằm trong danh sách provider
  nào cả, không phải do thứ tự ưu tiên). Đường dẫn duy nhất để đổi ngôn ngữ là qua `/SetLanguage` (dưới
  đây), ghi cookie rồi mới có hiệu lực. Quyết định có chủ đích: 1 cookie provider duy nhất là đủ cho
  nhu cầu "người dùng bấm nút đổi ngôn ngữ, giữ nguyên tới lần sau" — không cần query string (dễ bị
  chia sẻ nhầm link kèm `?culture=` không mong muốn) hay Accept-Language header (trình duyệt tự đoán,
  khó kiểm soát/test). `app.UseRequestLocalization()` đặt sau `UseHttpsRedirection()`, trước
  `UseRouting()`.
- Đổi ngôn ngữ: `Pages/SetLanguage.cshtml.cs` — `GET /SetLanguage?culture=en&returnUrl=...` ghi cookie
  `CookieRequestCultureProvider.DefaultCookieName` (hạn 1 năm) rồi redirect về `returnUrl` (validate
  bằng `Url.IsLocalUrl`, không redirect mù ra domain ngoài). Nút bấm đổi ngôn ngữ nằm trên navbar
  (`_Layout.cshtml`), tự truyền `returnUrl` là URL trang hiện tại để không bị "giật" về trang chủ mỗi
  lần đổi ngôn ngữ.

### 22.2 `IStringLocalizer<T>` khác `IViewLocalizer` — bẫy nối chuỗi

`_ViewImports.cshtml` tiêm sẵn `IViewLocalizer Localizer` (dùng trực tiếp trong mọi `.cshtml`, không
cần khai báo lại từng trang) — đây là kiểu chuẩn để dịch text trong Razor View. Điểm khác biệt quan
trọng so với `IStringLocalizer<T>` (dùng ở `SharedResource`/service thuần): `IViewLocalizer[...]` trả
về `LocalizedHtmlString` (cho phép chứa HTML), trong khi `IStringLocalizer<T>[...]` trả về
`LocalizedString` thuần (an toàn nối chuỗi `+` vì nó implicit-convert đúng qua `.ToString()`).

> ⚠️ **Bug thật phát hiện qua verify sống (không phải qua test — kiến trúc test hiện tại của
> `SplitBill.Web.Tests` không render Razor View thật, xem mục 10b)**: `Groups/Details.cshtml` ban đầu
> viết nhãn khách vãng lai bằng `member.UserId is null ? " " + Localizer["(khách)"] : ""` (nối chuỗi
> `string` với kết quả `Localizer[...]`). Vì `Localizer` ở đây là `IViewLocalizer` (trả
> `LocalizedHtmlString`), phép `+` giữa `string` và `LocalizedHtmlString` không gọi override
> `ToString()` mà rơi về `object.ToString()` mặc định của CLR — hiển thị sống trên trình duyệt ra
> đúng nguyên văn `Microsoft.AspNetCore.Mvc.Localization.LocalizedHtmlString` thay vì "(khách)"/
> "(guest)". Đã sửa bằng cách bỏ hẳn nối chuỗi, xuất riêng qua khối Razor gốc:
> ```csharp
> @if (member.UserId is null)
> {
>     @: @Localizer["(khách)"]
> }
> ```
> Đã grep toàn bộ `src/SplitBill.Web/Pages` tìm mẫu `+ Localizer[`/`Localizer[...] +` để xác nhận đây
> là chỗ DUY NHẤT mắc lỗi này. Đã cross-check: dùng `Localizer[...]` trong ngữ cảnh thuộc tính HTML
> (`title="@Localizer[...]"`, `placeholder="@Localizer[...]"`, có ở `Create.cshtml`/`Expenses/Index.cshtml`/
> `Groups/Details.cshtml`) KHÔNG bị lỗi này — verify bằng `element.getAttribute(...)` đọc đúng bản dịch
> — vì Razor tự xử lý việc ghi giá trị thuộc tính đúng cách, chỉ riêng phép `+` tường minh trong code
> block là con đường duy nhất dẫn tới bug. **Rút kinh nghiệm cho code sau này: không bao giờ nối chuỗi
> `+` với kết quả `IViewLocalizer` trong `.cshtml` — luôn xuất trực tiếp bằng `@Localizer[...]` (Razor
> tự render đúng), tách các đoạn văn bản bằng `@if`/nhiều thẻ `@:` nếu cần ghép động, không dùng
> `string.Concat` hay `+`.**

### 22.3 Phạm vi đã dịch (trung thực, không phóng đại)

> ✅ **Cập nhật 2026-09-08:** đã dịch nốt toàn bộ các trang còn lại liệt kê là "CHƯA dịch" ở lần đầu
> triển khai (2026-09-07). Mục này giữ nguyên cấu trúc gốc nhưng nay phản ánh trạng thái ĐÃ HOÀN THÀNH.

**Đã dịch nội dung trang** (kiểm chứng qua build sạch + verify sống cả 2 culture `vi`/`en`, xem danh
sách xác nhận ở cuối mục): navbar/footer (`_Layout.cshtml`), `Index.cshtml` (trang chủ),
`Account/Login.cshtml`, `Account/Register.cshtml`, `Account/ForgotPassword.cshtml`,
`Account/ResetPassword.cshtml`, `Account/Profile.cshtml`, `Groups/Index.cshtml`, `Groups/Details.cshtml`,
`Groups/Balances.cshtml`, `Groups/SettlementPlan.cshtml`, `Groups/Timeline.cshtml`,
`Groups/Statistics.cshtml`, `Groups/RecurringExpenses.cshtml`, `Groups/Summary.cshtml`,
`Expenses/Index.cshtml`, `Expenses/Create.cshtml`, `Expenses/Edit.cshtml`, `Notifications/Index.cshtml`,
`Public/Group.cshtml`. Riêng `ViewData["Title"]` (tiêu đề tab trình duyệt) của mọi trang vẫn hardcode
tiếng Việt, chưa qua `Localizer` — chỉ nội dung thân trang được dịch, không phải "đầy đủ" theo đúng
nghĩa đen. `Account/Logout.cshtml` (chỉ có `<form>` POST rỗng, không có text hiển thị),
`Shared/Components/NotificationBadge/Default.cshtml` (chỉ icon + số, không có text), và `Error.cshtml`
(trang lỗi scaffold mặc định của ASP.NET Core, vốn đã là tiếng Anh từ đầu, không thuộc phạm vi "dịch
VI→EN" của tính năng này) không cần đụng tới.

**Cố tình vẫn KHÔNG dịch** (ranh giới có chủ đích, không phải thiếu sót): nội dung do người dùng tự
nhập (tên nhóm, tên khoản chi, ghi chú, tên thành viên, nội dung bình luận...) không bao giờ dịch, kể
cả trên các trang đã dịch — chỉ nhãn/label cố định của giao diện mới thuộc phạm vi tính năng này. Nội
dung do **API tự sinh** bằng tiếng Việt cũng ngoài phạm vi (Web chỉ hiển thị nguyên văn, không dịch
lại): `AuditLog.Summary` (`Groups/Timeline`, mục 15.5) và `Notification.Title`/`Notification.Message`
(`Notifications/Index`, mục 13) — dịch những chuỗi này cần đổi ở tầng `SplitBill.Application`
(`GroupService.BuildSummary`/`NotificationService`), không phải việc của lớp `IViewLocalizer` ở Web.
`ExpenseCategoryOptions`/`SupportedCurrencies` (nhãn danh mục, tên tiền tệ) cũng chưa được đưa vào hệ
thống resource — dropdown "Danh mục" vẫn hiện nhãn tiếng Việt ("📦 Khác", "🍜 Ăn uống"...) ở mọi trang
dùng nó dù trang đã ở chế độ `en`.

Đã verify sống trên trình duyệt (cả 2 chiều `vi`/`en`, tạo dữ liệu thật — nhóm, thành viên khách, 2
khoản chi, 1 bình luận, 1 yêu cầu quên mật khẩu — không chỉ đọc trang trống): tất cả 19 trang liệt kê ở
trên hiện đúng nhãn tiếng Anh khi đổi `culture=en`, không còn chuỗi `LocalizedHtmlString` nào lộ ra
(kể cả câu ghép động dạng "X transfers to Y", "Page {0}/{1}...", "Settlement plan ({0} transactions)"
dùng `Localizer["...", args]`); đổi lại `culture=vi` → mọi nhãn (kể cả các key mới thêm) trở về đúng
tiếng Việt gốc qua đúng cơ chế fallback. Không có lỗi console nào phát sinh (kể cả ở `Groups/Statistics`,
nơi 1 chuỗi dịch được nhúng vào literal JavaScript cho nhãn biểu đồ Chart.js). `dotnet test`: 236/236
pass (không đổi do phần dịch thêm — Razor rendering không nằm trong phạm vi test tự động hiện có, xem
giới hạn đã nêu ở mục 10b).

## 23. PWA (cài đặt thành ứng dụng, dùng ngoại tuyến giới hạn) — bổ sung 2026-09-07

Hạng mục 8/8 (cuối cùng) trong danh sách gợi ý sau mục 16 — `SplitBill.Web` giờ thỏa điều kiện
installability chuẩn của Chrome/Edge: có Web App Manifest hợp lệ + Service Worker đã đăng ký có xử lý
`fetch` + icon tối thiểu 192×192 và 512×512.

### 23.1 Nguyên tắc bắt buộc: không bao giờ cache dữ liệu tài chính

Đây là ràng buộc quan trọng nhất của tính năng này, xuất phát trực tiếp từ nguyên tắc "Không bao giờ
lưu số dư vào DB" ở mục 1 — số dư/khoản chi luôn phải tính lại từ nguồn mới nhất, **kể cả ở tầng
trình duyệt**. Nếu Service Worker cache HTML của `Groups/Balances`/`Expenses/Index`/`SettlementPlan`,
người dùng mở lại app lúc mất mạng (hoặc mạng chập chờn) có thể thấy số dư/khoản chi CŨ mà tưởng là
mới — nguy hiểm hơn nhiều so với việc không mở được trang, vì sai số tiền có thể dẫn tới quyết định
chuyển khoản sai. Vì vậy `wwwroot/service-worker.js` áp dụng đúng 1 nguyên tắc: **chỉ cache asset
tĩnh không đổi theo dữ liệu** (`/css/`, `/js/`, `/lib/`, `/icons/` — cache-first, an toàn vì tên file
tự đổi theo `asp-append-version`/fingerprint khi nội dung đổi). **Mọi request khác — toàn bộ trang
HTML, mọi API call, mọi ảnh hóa đơn — luôn network-only**, không có nhánh nào trong `fetch` handler
phục vụ lại từ cache. Request không phải `GET` (form POST tạo/sửa/xóa khoản chi, settlement...)
không bị can thiệp — dòng đầu tiên trong `fetch` handler return sớm nếu `method !== 'GET'`.

Khi mất mạng hoàn toàn (không tải được trang nào), fallback duy nhất là `offline.html` — 1 trang tĩnh
độc lập, không có style/script phụ thuộc mạng (inline CSS, không gọi Google Fonts/Bootstrap CDN), chỉ
báo trung lập "Bạn đang ngoại tuyến" kèm nút "Thử lại" (`location.reload()`) — **không hiển thị bất kỳ
số liệu cũ nào**. `offline.html` được precache lúc `install` cùng với `site.css`/`site.js`, nên luôn
sẵn sàng phục vụ kể cả lần đầu cài đặt app rồi mất mạng ngay sau đó.

### 23.2 Web App Manifest

`wwwroot/manifest.webmanifest` — `display: "standalone"` (mở như app riêng, không thanh địa chỉ),
`theme_color`/`background_color` khớp `--sb-brand`/`--sb-brand-light` (site.css, mục 10b) để màn hình
splash lúc mở app không bị lệch tông. 3 icon: `icon-192.png`, `icon-512.png` (`purpose: "any"`) và
`icon-maskable-512.png` (`purpose: "maskable"`, riêng — theo đúng khuyến nghị của Chrome, không dùng
`"any maskable"` gộp chung vì maskable cần vùng an toàn ở giữa mà icon "any" thì không).

> ⚠️ **Icon là hình vuông bo góc màu thương hiệu đơn sắc, KHÔNG có logo/chữ** — dự án không có sẵn
> công cụ thiết kế hay thư viện xử lý ảnh nào được phép dùng (mục 2 "không thêm NuGet ngoài danh sách
> nếu chưa hỏi người dùng", và máy dev không có Node/ImageMagick/Pillow cài sẵn). Sinh bằng 1 script
> Python độc lập dùng thuần thư viện chuẩn (`zlib`+`struct`, tự ghi byte PNG thủ công, không phụ thuộc
> `Pillow`) — không phải một phần của solution .NET, chỉ chạy 1 lần để xuất file PNG tĩnh, script không
> được lưu lại trong repo. Đây là icon placeholder chức năng (đủ để cài app, hiển thị đúng trên
> homescreen/taskbar), không phải sản phẩm thiết kế cuối cùng — nếu cần bộ nhận diện thật, cần thay 3
> file PNG này bằng thiết kế do designer cung cấp, không cần đổi gì khác (manifest tham chiếu đúng tên
> file cố định).

`_Layout.cshtml` liên kết `<link rel="manifest">` + `<meta name="theme-color">` +
`<link rel="apple-touch-icon">` (Safari/iOS không đọc Web App Manifest, cần thẻ riêng này để "Thêm vào
màn hình chính" có icon đúng thay vì chụp ảnh chụp màn hình trang).

### 23.3 Đăng ký Service Worker

`wwwroot/js/site.js` (cuối file) — đăng ký sau sự kiện `load` (không cạnh tranh băng thông với tải
trang lần đầu), bọc `.catch()` im lặng vì đây là tính năng bổ trợ (cài app/dùng ngoại tuyến giới hạn),
lỗi đăng ký (trình duyệt cũ, chạy qua HTTP không phải localhost...) không được phép làm gián đoạn luồng
chính. `service-worker.js` đặt ở gốc `wwwroot/` (scope mặc định `"/"`, bao trọn toàn site) — không đặt
trong `wwwroot/js/` vì scope của Service Worker mặc định chỉ bao thư mục chứa nó trở xuống.

### 23.4 Giới hạn đã biết

- Không có "background sync" hay "push notification" qua Service Worker — nằm ngoài phạm vi 8 tính
  năng gợi ý ban đầu, không tự ý thêm (mục 12 "không tự ý thêm tính năng ngoài phạm vi milestone").
- Không tăng version `CACHE_NAME` (`splitbill-static-v1`) tự động theo build — nếu sau này đổi chiến
  lược cache (thêm/bớt asset tĩnh cần precache), phải tự đổi hậu tố `v1` → `v2` thủ công để Service
  Worker cũ bị `activate` dọn cache theo đúng logic đã viết (`caches.keys()` xóa mọi cache khác
  `CACHE_NAME` hiện tại).

**Đã test thật kịch bản mất mạng** (không phải suy luận từ code): tải trang chủ bình thường (Service
Worker `activate`, precache `offline.html` + asset tĩnh) → tắt hẳn tiến trình `dotnet run` của
`SplitBill.Web` (mô phỏng đúng lỗi "target actively refused connection" mà trình duyệt gặp khi mất
mạng — `fetch(request)` trong Service Worker reject giống hệt cả 2 trường hợp) → tải lại trang → trình
duyệt hiện đúng `offline.html` ("Bạn đang ngoại tuyến", tiêu đề tab "Ngoại tuyến - SplitBill"), không
phải trang lỗi mặc định của Chrome. Khởi động lại `SplitBill.Web` → tải lại trang → về đúng trang chủ
bình thường với dữ liệu số dư mới nhất (không phải bản cache cũ).

> **Phát hiện phụ ngoài phạm vi PWA lúc test, đã sửa riêng ngay sau đó (2026-09-08, người dùng xác
> nhận):** khi tạm dừng `SplitBill.Api` (không phải `SplitBill.Web`) để chạy `dotnet test`, tải lại
> trang chủ ra lỗi 500 chưa được xử lý — `HttpRequestException` từ `SplitBillApiClient` (Api không
> phản hồi được ở tầng kết nối, khác `ApiException` vốn chỉ bắt lỗi HTTP có response) không được bắt ở
> `Index.cshtml.cs`. Đây là lỗ hổng có sẵn từ mục 15.4 (2026-09-05), không liên quan tới Service
> Worker. Grep toàn bộ `SplitBill.Web` tìm mẫu `catch (ApiException)` phát hiện thêm **5 điểm khác**
> mắc đúng lớp lỗi này: `Expenses/Create.cshtml.cs` (`LoadPresetsAsync`), `Expenses/Edit.cshtml.cs`
> (`OnGetReceiptImageAsync`), `Account/ForgotPassword.cshtml.cs`, `Account/Logout.cshtml.cs`, và đặc
> biệt `NotificationBadgeViewComponent` — component này chạy trên **mọi trang đã đăng nhập** qua
> `_Layout.cshtml`, nghĩa là Api sập trước khi sửa sẽ làm sập TOÀN BỘ trang chứ không riêng gì trang
> chủ. Đã sửa cả 6 điểm bằng `catch (Exception ex) when (ex is ApiException or HttpRequestException)`
> — dùng exception filter (C# 9+) thay vì 2 khối `catch` lặp lại thân giống hệt nhau, và cố tình không
> bắt `Exception` trần để không vô tình nuốt luôn các lỗi lập trình thật (NullReferenceException...).
> Không gộp thành 1 helper/base class dùng chung — theo đúng tiền lệ đã chọn ở mục 19.2 (chấp nhận
> trùng lặp nhỏ giữa các PageModel/ViewComponent độc lập). Thêm `IndexModelTests` (2 test: Api không
> phản hồi được thì không throw và cả 2 widget rỗng; chưa đăng nhập thì không gọi API) — đây là
> `SplitBill.Web.Tests` đầu tiên giả lập lỗi tầng kết nối (`FakeHttpMessageHandler` ném thẳng
> `HttpRequestException` thay vì trả `HttpResponseMessage`), các test trước đó chỉ giả lập response
> HTTP có status code. Verify sống: tắt hẳn `SplitBill.Api`, giữ `SplitBill.Web` chạy, hard-reload
> (`Ctrl+Shift+R`, bỏ qua cache trình duyệt) trang chủ → đúng 200 OK, widget số dư/counterparty biến
> mất thay vì trang lỗi 500; khởi động lại Api, hard-reload lại → về đúng dữ liệu số dư mới nhất.
> `dotnet test`: 235/235 pass (233 cũ + 2 test mới).

Đã verify sống trên trình duyệt: `manifest.webmanifest` trả đúng `Content-Type: application/manifest+json`,
`service-worker.js` trả `Content-Type: text/javascript`, cả 3 icon trả `200`/`image/png`; sau khi tải
trang chủ, `navigator.serviceWorker.getRegistrations()` xác nhận đã đăng ký & `activated` đúng scope
`"/"`; `caches.open('splitbill-static-v1').keys()` xác nhận đúng danh sách asset tĩnh + `offline.html`
đã được precache, không có trang HTML động (Balances/Expenses/...) nào lọt vào cache. `dotnet build`:
0 warning, 0 error. `dotnet test`: 233/233 pass (không đổi — tính năng này thuần phía trình duyệt,
không có logic C# nào để unit test).

---

## 24. E2E test qua Playwright — bổ sung 2026-09-08

Sau khi CI/CD (mục "CI" trong README.md), i18n (mục 22) và các lỗ hổng bảo mật phát hiện qua
`security-review` (2026-09-07/08) đều đã hoàn thành, người dùng chọn làm tiếp hạng mục còn lại:
**thêm Microsoft.Playwright, viết E2E test** — quyết định người dùng (2026-09-08), bổ sung
`Microsoft.Playwright` vào danh sách NuGet được phép ở mục 2.

Cả 3 project test trước đó đều KHÔNG đi qua toàn bộ chồng công nghệ thật: `SplitBill.UnitTests` chỉ
test thuật toán thuần; `SplitBill.IntegrationTests` gọi thẳng Application service qua `TestHarness`
(bỏ qua hoàn toàn tầng Controller/HTTP/Razor Pages); `SplitBill.Web.Tests` không render Razor View
thật (CLAUDE.md mục 10b). Project mới `tests/SplitBill.E2ETests` lấp đúng khoảng trống đó: lái 1
trình duyệt Chromium headless thật qua **Chromium → Kestrel của `SplitBill.Web` → HTTP → Kestrel của
`SplitBill.Api` → EF Core**, xác nhận cookie đăng nhập (BFF, mục 10b), việc render HTML thật, và toàn
bộ pipeline HTTP thật sự khớp shape dữ liệu với nhau.

### 24.1 Kiến trúc: Kestrel thật, không phải TestServer

`Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<T>` mặc định host ứng dụng qua `TestServer` —
gọi được bằng `HttpClient` in-process nhưng KHÔNG lắng nghe cổng mạng thật, nên trình duyệt Playwright
(một tiến trình Chromium thật, tách biệt khỏi tiến trình test) không kết nối được.

> ⚠️ **Không có API `UseKestrel()` sẵn trên .NET 9** cho `WebApplicationFactory` — đã tìm hiểu qua
> WebSearch để xác nhận (không suy đoán): tính năng này chỉ có từ **.NET 10**. Với .NET 9 (bản dự án
> đang dùng), chỉ đơn giản override `CreateHost` để build 1 host Kestrel duy nhất rồi trả thẳng về sẽ
> ném `InvalidCastException: Unable to cast ... KestrelServerImpl ... to ... TestServer` — vì
> `EnsureServer()` (gọi ngầm bởi `CreateClient()`/`Server`/`Services`) LUÔN ép kiểu giá trị trả về của
> `CreateHost` sang `TestServer`, bất kể `CreateHost` đã override gì. Đã tìm và áp dụng đúng mẫu cộng
> đồng đã kiểm chứng (xem
> https://danieldonbavand.com/2022/06/13/using-playwright-with-the-webapplicationfactory-to-test-a-blazor-application/):
> build **2 host từ cùng 1 `IHostBuilder`** — build lần 1 (chưa gắn Kestrel) ra 1 "vỏ" `TestServer` chỉ
> để thỏa mãn ép kiểu nội bộ của `WebApplicationFactory` (không bao giờ dùng để gọi HTTP thật), rồi mới
> `ConfigureWebHost(webHostBuilder => webHostBuilder.UseKestrel())` và build lần 2 để có host Kestrel
> THẬT — khởi động, đọc lại địa chỉ cổng ngẫu nhiên (`UseUrls("http://127.0.0.1:0")`) đã bind qua
> `IServerAddressesFeature`. `KestrelWebApplicationFactory<TEntryPoint>` (base class dùng chung cho cả
> `ApiTestFactory`/`WebTestFactory`) đóng gói đúng mẫu này, cộng thêm `StopRealHostAsync()` — gọi tường
> minh trước khi `DisposeAsync()` của factory, vì lớp cơ sở chỉ biết dọn "vỏ" `TestServer`, không biết
> gì về host Kestrel thật thứ 2.

### 24.2 Ba lỗi thật phát hiện khi dựng hạ tầng (không suy luận tay — verify bằng chạy thực tế từng bước)

> ⚠️ **Config override qua `ConfigureAppConfiguration` KHÔNG có tác dụng cho giá trị Program.cs đọc
> EAGER (đọc ngay, đóng gói vào 1 biến local) — chỉ có tác dụng cho thứ đọc LAZY (qua `IOptions<T>`
> hoặc mutate `IServiceCollection`).** Cả `SplitBill.Api/Program.cs` (đọc `jwtOptions.SigningKey` để
> fail-fast) lẫn `SplitBill.Web/Program.cs` (đọc `Api:BaseUrl` vào biến `apiBaseUrl` rồi đóng gói vào
> closure của `AddHttpClient`) đều đọc `builder.Configuration[...]` **NGAY SAU** `WebApplication.
> CreateBuilder(args)` — thời điểm này chạy trước khi `WebApplicationFactory`'s `ConfigureWebHost`/
> `ConfigureAppConfiguration` callback (chỉ chạy lúc `IHostBuilder.Build()`, ở bước SAU trong luồng dựng
> host) kịp gắn vào. Lần thử đầu tiên dùng `ConfigureAppConfiguration` để cấp `Jwt:SigningKey`/
> `Api:BaseUrl` giả **"tình cờ" không lộ lỗi cho `Jwt:SigningKey`** vì máy dev cục bộ đã có sẵn
> `dotnet user-secrets set Jwt:SigningKey ...` (mục 10b) — giá trị đó mới thực sự được dùng, không phải
> giá trị test cấp. Với `Api:BaseUrl` thì lộ ngay: request thật từ `SplitBillApiClient` bay thẳng tới
> cổng `5199` mặc định trong `appsettings.json` thay vì cổng ngẫu nhiên của `ApiTestFactory`, xác nhận
> qua log Serilog thấy `HttpRequestException: ... refused ...` khi gọi `/auth/register`. **Đã sửa**:
> đặt biến môi trường (`Environment.SetEnvironmentVariable("Jwt__SigningKey", ...)` /
> `"Api__BaseUrl"`) **TRONG CONSTRUCTOR** của `ApiTestFactory`/`WebTestFactory`, TRƯỚC KHI host được
> kích hoạt — `WebApplication.CreateBuilder(args)` tự thêm `AddEnvironmentVariables()` làm 1 nguồn cấu
> hình đọc đồng bộ NGAY LÚC ĐÓ, nên kịp áp dụng trước dòng code eager-read của Program.cs. Cùng quy ước
> `"__"` → `":"` đã dùng cho `docker-compose.yml` (mục 10b, biến `Database__AutoMigrateOnStartup`).
> Ngược lại, việc gỡ/thay `DbContextOptions<SplitBillDbContext>` qua `ConfigureServices` (mục 24.3) vẫn
> hoạt động đúng dù đăng ký muộn — vì đó là mutate trực tiếp `IServiceCollection`, không phải đọc 1
> biến local đã đóng gói từ trước.

> ⚠️ **`page.WaitForURLAsync(...)` gọi SAU `page.ClickAsync(...)` luôn timeout 30s dù luồng thật hoàn
> toàn thành công.** Nguyên nhân: `ClickAsync` trên 1 nút submit đã TỰ CHỜ XONG điều hướng phát sinh
> bởi chính cú click đó trước khi trả về (hành vi auto-wait chuẩn của Playwright) — gọi
> `WaitForURLAsync` SAU đó là chờ một sự kiện điều hướng TƯƠNG LAI không còn xảy ra nữa (điều hướng đã
> xảy ra RỒI). Phát hiện qua log Serilog: `POST /auth/register` → 200, tiếp theo `GET /users/me`,
> `/groups`, `/notifications/unread-count` đều 200 (đúng luồng `SignInHelper.SignInAsync` +
> `Groups/Index.OnGetAsync` + `NotificationBadgeViewComponent`) — server xử lý hoàn toàn đúng, chỉ có
> assertion phía test sai cách chờ. **Đã sửa**: dùng `Assertions.Expect(page).ToHaveURLAsync(regex)`
> thay `WaitForURLAsync` — kiểm tra URL HIỆN TẠI trước, chỉ retry (tối đa 5s) nếu chưa khớp, nên an
> toàn với cả 2 tình huống (đã điều hướng xong hoặc đang chờ điều hướng).

> ⚠️ **URL thật của 1 trang gốc trong thư mục (`Index.cshtml`) KHÔNG có hậu tố `/Index`** — quy ước
> route mặc định của Razor Pages tự lược bỏ "Index" (`Pages/Groups/Index.cshtml` → route `/Groups`,
> `Pages/Expenses/Index.cshtml` với `@page "{groupId:guid}"` → route `/Expenses/{groupId}`, KHÔNG phải
> `/Groups/Index`/`/Expenses/Index/{groupId}`). `asp-page="/Groups/Index"` trong `.cshtml` dùng đúng
> TÊN TRANG (không phải route) nên không bị ảnh hưởng — chỉ URL cuối cùng trình duyệt thấy là rút gọn.
> Test code tự gõ tay URL/regex ban đầu giả định sai theo tên file, sửa lại theo route thật đã quan sát
> được (`page.Url`) qua chạy thực tế, không suy đoán theo tên `.cshtml`.

### 24.3 DB InMemory cho Api: xung đột 2 provider trong cùng 1 service provider

`ApiTestFactory` cần thay `UseSqlServer` (LocalDB, chỉ chạy được trên Windows — mục 2) bằng EF Core
InMemory (cùng pattern `TestHarness` đã dùng ở `SplitBill.IntegrationTests`) để chạy được cả trên CI
(`ubuntu-latest`, không có LocalDB). Cách làm tưởng chừng hiển nhiên — gỡ descriptor
`DbContextOptions<SplitBillDbContext>` khỏi `IServiceCollection` rồi `AddDbContext(UseInMemoryDatabase(...))`
— build được nhưng request thật ném `InvalidOperationException: Services for database providers
'SqlServer', 'InMemory' have been registered in the service provider. Only a single database provider
can be registered in a service provider.`

> ⚠️ Nguyên nhân: gỡ 1 descriptor `DbContextOptions<T>` KHÔNG undo được tác dụng phụ mà lệnh
> `UseSqlServer(...)` gốc (trong `Program.cs`) đã gây ra lúc `AddDbContext` chạy lần đầu — nó đăng ký
> sẵn dịch vụ nội bộ của provider SqlServer thẳng vào `IServiceCollection` DÙNG CHUNG của cả app. Nếu
> `DbContextOptions` mới chỉ đơn thuần `UseInMemoryDatabase(...)` mà không chỉ định service provider
> riêng, EF Core lúc resolve vẫn nhìn thấy CẢ HAI provider cùng có mặt trong service provider gốc và từ
> chối chạy. **Đã sửa** bằng đúng mẫu chính thức của Microsoft cho tình huống swap provider này: dựng 1
> `ServiceProvider` RIÊNG chỉ chứa `AddEntityFrameworkInMemoryDatabase()`, rồi gọi
> `options.UseInternalServiceProvider(inMemoryServiceProvider)` trỏ `DbContextOptions` mới về đúng
> service provider cô lập đó — không đụng gì tới service provider gốc của app (vẫn còn dịch vụ
> SqlServer, nhưng không còn được EF Core dùng tới cho `SplitBillDbContext` nữa).

### 24.4 `Program.cs` cần `public partial class Program` để test tham chiếu được

`WebApplicationFactory<TEntryPoint>` cần `TEntryPoint` là 1 type công khai truy cập được từ project
test. `SplitBill.Api/Program.cs` đã có sẵn `public partial class Program;` từ trước (dùng cho
`SplitBill.IntegrationTests`). `SplitBill.Web/Program.cs` **CHƯA CÓ** — bổ sung thêm, cùng mẫu, để
`SplitBill.E2ETests` build được (`WebTestFactory : KestrelWebApplicationFactory<SplitBill.Web.Program>`).

### 24.5 Nội dung test đã viết

Cố tình viết ít nhưng SÂU (3 file, 4 test) — mỗi test là 1 luồng dài (nhiều bước liên tiếp), không phải
nhiều test atomic rời rạc, vì chi phí khởi động 2 Kestrel host + 1 trình duyệt là cố định 1 lần cho cả
`[Collection("E2E")]` (dùng chung `E2EFixture` qua `ICollectionFixture`), còn chi phí drive từng bước
qua trình duyệt thật (fill form, click, chờ điều hướng) là chi phí thật trên MỖI test — gộp thành luồng
dài tận dụng tối đa 1 lần setup mà vẫn phủ được nhiều bước nghiệp vụ nối tiếp nhau.

- `AuthFlowTests`: đăng ký → redirect `/Groups` → đăng xuất → redirect `/` (trang chủ, KHÔNG PHẢI
  `/Account/Login` — xác nhận qua đọc `LogoutModel.OnPostAsync`, `RedirectToPage("/Index")`), xác nhận
  form đăng xuất trên navbar biến mất (đã thật sự đăng xuất, không chỉ đổi URL); đăng nhập sai mật khẩu
  → hiện đúng câu chung chung "Email hoặc mật khẩu không đúng." (mục 8, không tiết lộ email tồn tại).
- `GroupExpenseFlowTests`: đăng ký → tạo nhóm (VND) → thêm 1 khách vãng lai → tạo khoản chi 100.000đ do
  Owner ứng toàn bộ, chia đều 2 người → xác nhận trang Số dư đúng "+50.000đ"/"-50.000đ" → xác nhận trang
  Kế hoạch thanh toán đúng 1 giao dịch "Guest Friend chuyển cho Owner Tester" 50.000đ — đúng bài toán
  cốt lõi của cả dự án (mục 1) đi qua toàn bộ pipeline thật, không chỉ qua thuật toán thuần (mục 7) hay
  service in-process (`SplitBill.IntegrationTests`).
- `SettlementFlowTests` (bổ sung ngay sau, cùng ngày 2026-09-08) — lấp đúng khoảng trống
  `GroupExpenseFlowTests` để lại: test đó dừng ở việc XEM kế hoạch thanh toán, không thực sự ghi
  nhận/xác nhận giao dịch nào. Dùng **2 tài khoản thật** (không phải 1 tài khoản + 1 khách vãng lai như
  test trước) vì Ghi nhận/Xác nhận settlement đòi hỏi mỗi bên tự thao tác từ đúng phiên đăng nhập của
  mình (`SettlementPlanModel.MyMemberId` so khớp `NameIdentifier` claim — khách vãng lai không đăng
  nhập được, mục 2 "Guest... không đăng nhập được"). Vì form "Thêm thành viên" trên Web CHỈ hỗ trợ
  khách vãng lai (mục 13.4), người thứ 2 phải **tham gia qua link chia sẻ** (mục 15.6) — nhân tiện test
  này cũng là bài kiểm tra E2E đầu tiên cho luồng đó. Đi hết vòng đời `Settlement`: Payer tự ghi nhận
  "Tôi đã chuyển khoản này" (Pending) → Owner tự "Xác nhận" (Confirmed) → xác nhận số dư CẢ HAI bên đều
  về đúng "Đã cân bằng" và trang Kế hoạch thanh toán không còn giao dịch nào — đúng bất biến
  `Σ net = 0` sau khi 1 `Settlement` thật được Confirmed (mục 6.1), verify bằng trình duyệt thật thay
  vì chỉ bằng unit test cho công thức.

DB dùng chung `[Collection("E2E")]` (1 `ApiTestFactory`/instance InMemory DB cho cả 4 test) nhưng mỗi
test tự đăng ký (các) email `Guid.NewGuid()` riêng và tự mở (các) `IBrowserContext` riêng (cookie cô
lập từng test/từng user) — không có state rò rỉ giữa các test dù chạy tuần tự trên cùng DB.

### 24.6 CI

`.github/workflows/ci.yml` thêm bước `Install Playwright browsers` (chạy
`playwright.ps1 install --with-deps chromium`) SAU bước build, TRƯỚC bước test — bắt buộc vì
`E2EFixture` tuy có tự gọi `Microsoft.Playwright.Program.Main(["install", "chromium"])` lúc
`InitializeAsync` (tiện cho máy dev chưa cài Playwright thủ công) nhưng chỉ tải browser binary, KHÔNG
cài thư viện hệ thống Linux (libnss3, libatk...) mà `ubuntu-latest` không có sẵn — thiếu `--with-deps`
thì Chromium headless sẽ không khởi động được trên CI dù build/test project không báo lỗi gì.
`ApiTestFactory` dùng EF Core InMemory (mục 24.3) nên không cần SQL Server/LocalDB thật, khớp nguyên
tắc CI hiện có (mục "CI" trong README.md).

> ✅ **Cập nhật 2026-09-08 — đã verify thật trên GitHub Actions**, không còn là "chưa verify" nữa: repo
> đã có remote (`https://github.com/Namnotlike/SplitBill`, quyết định người dùng), `git push` kích hoạt
> đúng workflow. Bước `Install Playwright browsers` (`--with-deps chromium`) chạy đúng như dự kiến
> trên `ubuntu-latest`. Lần chạy ĐẦU TIÊN thất bại — nhưng không phải vì Playwright/E2E: `Test` step
> báo 4 test fail trong `CsvBuilderTests` (UnitTests), một bug thật hoàn toàn không liên quan tới E2E —
> xem callout ⚠️ ở mục 8 ("`CsvBuilder` dùng `Environment.NewLine` thay vì `\"\r\n\"` cứng"). Đây chính
> xác là loại lỗi mà chạy CI trên OS khác máy dev (Windows) sinh ra để bắt được — bug đã tồn tại từ lâu,
> chưa từng lộ ra vì mọi lần `dotnet test` trước đó đều chạy trên Windows. Sau khi sửa, lần chạy tiếp
> theo (commit `25ddd20`) **thành công**, 240/240 test pass, ~1 phút 30 giây. Xem
> https://github.com/Namnotlike/SplitBill/actions.

Đã verify sống (thật sự chạy qua trình duyệt Chromium headless, không phải chỉ review code): cả 4 test
pass cục bộ, kèm log HTTP request/response đầy đủ xác nhận từng bước (POST `/auth/register` → 200, GET
`/users/me`/`/groups`/`/notifications/unread-count` → 200, POST `.../expenses` → 201, POST
`.../settlements` → tạo Pending, POST `.../confirm` → Confirmed, GET
`.../balances`/`.../settlement-plan` → 200 — không có bước nào bị mock/giả lập). `dotnet build
SplitBill.sln --configuration Release`: 0 warning, 0 error. `dotnet test SplitBill.sln --configuration
Release`: **240/240 pass** (69 UnitTests + 25 Web.Tests + 142 IntegrationTests + 4 E2ETests —
không có test cũ nào bị ảnh hưởng bởi việc thêm `public partial class Program;` vào `SplitBill.Web/
Program.cs`).

---

## 25. Danh sách tính năng bổ sung ngoài kế hoạch — bổ sung 2026-09-09

Sau khi CI/CD, i18n, PWA và E2E test (mục 24) đều đã hoàn thành, người dùng yêu cầu gợi ý tiếp các tính
năng "ngoài kế hoạch" (khác hẳn danh sách mục 15, vốn do chính người dùng liệt kê trước khi hỏi ý kiến
— lần này ngược lại: người dùng hỏi AI gợi ý). Đã đề xuất 9 hạng mục, người dùng chốt **"Làm hết"**
(cùng tinh thần mục 15 — làm toàn bộ, từ dễ đến khó, mỗi hạng mục build + test + verify sống + commit
riêng). 3 hạng mục cần thêm NuGet package (Đăng nhập Google, Web Push, 2FA/TOTP) sẽ hỏi lại người dùng
trước khi thêm dependency, đúng CLAUDE.md mục 2 — "Làm hết" chỉ phê duyệt phạm vi tính năng, không tự
động phê duyệt từng NuGet package cụ thể bên trong.

Tiến độ: 1. Khôi phục khoản chi/thanh toán đã xóa — đã làm (mục 25.1). 2. Miễn nợ — đã làm (mục 25.2).
3. Đăng nhập bằng Google — đã làm (mục 25.3, người dùng đã đồng ý thêm
`Microsoft.AspNetCore.Authentication.Google`). 4. Xuất/backup dữ liệu nhóm dạng JSON — đã làm (mục
25.4, không cần NuGet mới). 5. Dashboard cá nhân nâng cao — đã làm (mục 25.5, không cần NuGet mới).
6. Mẫu nhóm tái sử dụng — đã làm (mục 25.6, không cần NuGet mới). 7. Thông báo đẩy trình duyệt (Web
Push) — đã làm (mục 25.7, người dùng đã đồng ý thêm package `WebPush`). 8. Tìm kiếm xuyên nhóm — đã
làm (mục 25.8, không cần NuGet mới). 9. Đăng nhập 2 lớp / TOTP — đã làm (mục 25.9, verify trước rồi
xác nhận KHÔNG cần NuGet mới — chỉ cần BCL). **Toàn bộ 9/9 hạng mục đã hoàn thành.**

### 25.1 Khôi phục khoản chi/thanh toán đã xóa (hạng mục 1/9)

Soft-delete (`Expense.IsDeleted`/`Settlement.IsDeleted`) đã có sẵn từ M1 (mục 1 "Không xóa cứng dữ liệu
tài chính, chỉ soft delete + audit log") nhưng trước tính năng này không có cách nào **xem lại/khôi
phục** qua UI hay API — dữ liệu vẫn còn nguyên trong DB (ẩn sau global query filter `!IsDeleted`, mục
4.3) nhưng "biến mất" khỏi mọi trang, xóa nhầm 1 khoản chi/thanh toán trước đây là vĩnh viễn về mặt
thao tác (dù kỹ thuật viên có thể sửa DB trực tiếp).

**API mới** (`ExpensesController`/`SettlementsController`):

```
GET  /api/v1/groups/{groupId}/deleted-expenses      Danh sách khoản chi ĐÃ xóa của nhóm
POST /api/v1/expenses/{expenseId}/restore           Khôi phục — 400 EXPENSE_NOT_DELETED nếu chưa xóa
GET  /api/v1/groups/{groupId}/deleted-settlements    Danh sách settlement ĐÃ xóa của nhóm
POST /api/v1/settlements/{settlementId}/restore      Khôi phục — 400 SETTLEMENT_NOT_DELETED nếu chưa xóa
```

- Repository thêm `GetByIdIncludingDeletedAsync`/`GetDeletedByGroupIdAsync` (cả `IExpenseRepository`
  và `ISettlementRepository`), cài đặt bằng `.IgnoreQueryFilters()` — bản ghi cần đọc lại chính là bản
  ghi có `IsDeleted = true` nên phương thức `GetByIdAsync` hiện có (đi qua global query filter) sẽ
  luôn trả `null`.
- **Quyền hạn khôi phục Expense**: cố tình KHÔNG hạn chế hơn quyền Xóa (mục 4.4: "Tạo/sửa/xóa
  Expense... Owner ✅ Member ✅") — mọi thành viên active đều xóa được nên cũng khôi phục được, giữ đối
  xứng thay vì tự thêm ràng buộc mới ngoài bảng phân quyền gốc. Khôi phục KHÔNG re-validate "Payers/
  Splits còn active" (mục 5.4) — không đổi Payers/Splits, cùng nguyên tắc miễn trừ đã áp dụng cho sửa
  1 khoản chi lịch sử.
- **Quyền hạn khôi phục Settlement**: mirror đúng `DeleteAsync` — chỉ người ghi nhận
  (`RecordedByMemberId`) hoặc Owner. Settlement chỉ xóa được khi đang `Pending` (xem mục 8), nên
  `Status` giữ nguyên `Pending` sau khi khôi phục, không cần gán lại.
- Audit log dùng Action `"Restored"` (từ vựng đã có sẵn từ mục 15.6, `"Created"|"Updated"|"Deleted"|
  "Restored"`), `AfterJson` = snapshot sau khi khôi phục — để Timeline (mục 15.5) hiển thị đủ tên/số
  tiền, không chỉ "có 1 bản ghi được khôi phục".

**Web**: trang mới `Groups/RecentlyDeleted.cshtml` (nút "♻️ Đã xóa gần đây" trên `Groups/Details`) —
2 danh sách (khoản chi đã xóa / settlement đã xóa), mỗi dòng có nút "♻️ Khôi phục" (POST form, không
`window.confirm()` — hành động này không phá hủy gì thêm, khớp nguyên tắc đã chọn ở mục 18 cho "Nhân
bản nhóm": chỉ dùng confirm cho hành động THẬT SỰ không hoàn tác được).

> ⚠️ **Bug thật phát hiện lúc verify sống (không phải qua test — kiến trúc test hiện tại của
> `SplitBill.IntegrationTests` gọi thẳng service, không render UI/Timeline qua HTTP thật):** sau khi
> khôi phục 1 khoản chi, trang Timeline (mục 15.5, `GroupService.BuildSummary`) hiện nguyên văn
> **"Restored Expense"** (tiếng Anh, không dịch) thay vì câu tiếng Việt như mọi hành động khác —
> `BuildSummary` switch theo `(EntityType, Action)` chưa có case nào khớp `("Expense", "Restored")`
> hay `("Settlement", "Restored")`, nên rơi vào nhánh `default: return $"{log.Action} {log.EntityType}"`
> (dòng dự phòng cuối cùng, vốn chỉ nhằm không bao giờ throw chứ không nhằm hiển thị cho người dùng
> thật). Đúng lớp lỗi mà mục 15.6 từng gặp khi thêm Action `"Restored"` cho `GroupMember` (rejoin qua
> link chia sẻ): mỗi lần thêm 1 giá trị Action mới vào từ vựng AuditLog, phải nhớ thêm case tương ứng
> trong `BuildSummary`, nếu không Timeline âm thầm hiện chuỗi kỹ thuật thay vì câu tiếng Việt — không
> có compiler nào bắt được thiếu sót này vì `switch` trên tuple `(string, string)` không exhaustive.
> Đã sửa: thêm `case ("Expense", "Restored")` và `case ("Settlement", "Restored")`, parse `AfterJson`
> giống hệt mẫu case `"Created"` của cùng entity. Verify lại: Timeline hiện đúng "đã khôi phục khoản
> chi "X" (100.000đ)" / "đã khôi phục khoản thanh toán 10.000đ (từ A đến B)". Test hồi quy:
> `GetAuditLogsAsync_ExpenseRestored_SummaryIncludesTitleAndAmount`,
> `GetAuditLogsAsync_SettlementRestored_SummaryIncludesAmountAndMembers` (`GroupServiceTests`).

Đã verify sống trên trình duyệt (luồng đầy đủ, không chỉ đọc code): tạo khoản chi 100.000đ → xóa → mở
"Đã xóa gần đây" → đúng hiện khoản chi vừa xóa kèm số tiền/ngày → bấm Khôi phục → quay lại đúng danh
sách Khoản chi, khoản chi đã trở lại; lặp lại tương tự với 1 settlement Pending (ghi nhận → xóa → xem
"Đã xóa gần đây" → khôi phục) → `Lịch sử ghi nhận` ở trang Kế hoạch thanh toán hiện lại đúng dòng
Pending đó. Cả 2 luồng đều xác nhận Timeline hiện đúng dòng "đã khôi phục..." bằng tiếng Việt (sau khi
sửa bug ở trên). `dotnet build`: 0 warning, 0 error. `dotnet test`: **249/249 pass** (69 UnitTests + 25
Web.Tests + 151 IntegrationTests + 4 E2ETests — 9 test mới: 3 `ExpenseServiceTests`, 5
`SettlementRecordServiceTests`, 2 `GroupServiceTests`; không có test cũ nào bị ảnh hưởng).

### 25.2 Miễn nợ (hạng mục 2/9)

Một khoản nợ nhỏ (vd 5.000đ lẻ do làm tròn) nhiều khi không đáng để bắt người khác chuyển khoản thật —
"Miễn nợ" cho phép chủ nợ tự tay xóa khoản nợ đó mà không cần một lượt chuyển tiền nào xảy ra.

**Thiết kế:** KHÔNG tạo entity/bảng mới. Tận dụng `Settlement` hiện có, thêm 1 cột
`bool IsWaived` (mặc định `false`, migration `AddSettlementIsWaived`). Khi miễn nợ, `SettlementRecordService.
WaiveAsync` tạo thẳng 1 `Settlement` ở trạng thái **Confirmed** (không qua `Pending`) với `IsWaived =
true`, `RecordedByMemberId`/`ConfirmedByMemberId` đều là chính người miễn nợ, `ConfirmedAt = now`.

- **Vì sao không cần sửa `BalanceCalculator`/thuật toán settlement (mục 6):** `BalanceCalculator` chỉ
  nhìn `Status == Confirmed` để tính vào `net` (mục 6.1), hoàn toàn không biết tới cờ `IsWaived` — một
  khoản nợ đã miễn tự động làm `net` về đúng như đã "trả xong", không cần đổi 1 dòng nào trong
  `BalanceCalculator`/`GreedySettlementSolver`/`OptimalSettlementSolver`. `IsWaived` CHỈ dùng để hiển
  thị/audit phân biệt đúng bản chất sự kiện (badge "Đã miễn nợ" thay vì "Confirmed", Summary Timeline
  khác câu) — theo đúng CLAUDE.md mục 12 "chạy lại toàn bộ test ở mục 7 khi sửa thuật toán settlement",
  không cần chạy lại vì thuật toán không hề bị đụng tới (đã verify: không sửa file nào trong
  `SplitBill.Application/Settlement/`).
- **Quyền hạn:** chỉ chủ nợ (`request.ToMemberId`, người ĐANG được nợ) mới miễn được — khớp nguyên tắc
  "chỉ người NHẬN mới xác nhận" của `ConfirmAsync` (mục 8), KHÔNG cho Owner miễn nợ thay người khác
  (khác hẳn quyền Create/Delete Expense/Settlement vốn mở cho mọi Member — miễn nợ đụng trực tiếp tới
  quyền lợi tài chính cá nhân của đúng 1 người, không phải việc chung của nhóm).
- **API:** `POST /api/v1/groups/{groupId}/settlements/waive` — body giống hệt `CreateSettlementRequest`
  (`FromMemberId`, `ToMemberId`, `Amount`, `Note?`).
- **Audit log:** dùng chung Action `"Created"` với ghi nhận thanh toán thường (không thêm Action mới) —
  về bản chất vẫn là "tạo mới 1 Settlement", `BuildSummary` (Timeline, mục 15.5) phân biệt qua
  `SettlementDto.IsWaived`: `"đã miễn nợ {amount} cho {tên người nợ}"` thay vì `"đã ghi nhận chuyển...
  từ ... đến ..."`.
- **Cố tình KHÔNG thêm sự kiện thông báo mới** — cùng nguyên tắc đã áp dụng cho Bình luận khoản chi
  (mục 19.2): mục 13 chốt đúng 4 sự kiện thông báo, "không hơn, không tự ý thêm".
- **`DeleteAsync` không cần sửa gì** để chặn xóa settlement đã miễn nợ — `DeleteAsync` vốn đã chỉ cho
  xóa khi `Status == Pending` (mục 8), mà `WaiveAsync` luôn tạo thẳng `Confirmed`, nên tự động không
  xóa được, đúng ý muốn (1 khoản nợ đã miễn là quyết định chốt, không nên xóa nhầm).

**Web:** `Groups/SettlementPlan.cshtml` có 2 điểm vào — nút "🤝 Miễn nợ này" ngay trên mỗi đề xuất gộp
nợ (chỉ hiện khi `Model.MyMemberId == tx.ToMemberId`, tức đang xem đúng vai trò chủ nợ của giao dịch
đó) và khối form thủ công "🤝 Miễn nợ" (tương tự "Ghi nhận thanh toán khác") cho trường hợp không nằm
trong danh sách đề xuất. Bảng "Lịch sử ghi nhận" hiện badge riêng màu xanh dương nhạt "Đã miễn nợ" thay
vì "Confirmed" cho các dòng `IsWaived`.

> ⚠️ **Bug thật phát hiện trước khi verify sống (rà soát code, không phải lỗi runtime đã xảy ra):**
> `SettlementPlanModel` vốn chỉ có 1 `[BindProperty] RecordInput NewSettlement` cho form "Ghi nhận
> thanh toán khác", với `Amount` gắn `[Range(1, long.MaxValue)]`. Thêm `[BindProperty] RecordInput
> WaiveDebt` (form Miễn nợ) CÙNG kiểu `RecordInput` sẽ khiến ASP.NET Core Razor Pages validate **CẢ
> HAI** property `[BindProperty]` mỗi lần POST — bất kể handler nào thực sự chạy. Submit form "Ghi
> nhận" (chỉ điền `NewSettlement.*`) khiến `WaiveDebt.Amount` giữ nguyên giá trị mặc định `0`, tự động
> fail `[Range(1,...)]` và làm `ModelState.IsValid == false` cho TOÀN BỘ request — nếu
> `OnPostRecordAsync` vẫn dựa vào `!ModelState.IsValid` như code gốc, mọi lần ghi nhận thanh toán bình
> thường sẽ bị chặn nhầm bởi 1 form khác mà người dùng còn chưa đụng tới, dù tự bản thân form đó không
> có lỗi gì. Đã sửa TRƯỚC khi build/test: bỏ `[Range]` khỏi `RecordInput.Amount`, thay bằng validate
> thủ công `Amount <= 0` ngay trong từng handler (`OnPostRecordAsync`/`OnPostWaiveAsync`), không phụ
> thuộc `ModelState.IsValid` nữa. Bài học chung: 2 `[BindProperty]` khác nhau dùng CHUNG 1 class DTO
> trên cùng 1 PageModel là rủi ro tiềm ẩn — validation attribute trên field của form A luôn được áp
> dụng ngay cả khi chỉ form B được submit.

Đã verify sống trên trình duyệt (không chỉ qua test): nhóm có đề xuất "Guest Friend chuyển cho Restore
Tester 50.000đ" → đăng nhập đúng Restore Tester (chủ nợ) → nút "🤝 Miễn nợ này" hiện đúng trên card đề
xuất → bấm → chuyển thẳng về đúng "Mọi người đã cân bằng, không cần chuyển gì thêm." (0 giao dịch, số
dư về 0 mà KHÔNG có giao dịch chuyển tiền nào xảy ra) → bảng "Lịch sử ghi nhận" hiện đúng badge xanh
dương "Đã miễn nợ" (phân biệt rõ với badge vàng "Pending" của 1 settlement khác cùng bảng) → Timeline
hiện đúng "Restore Tester đã miễn nợ 50.000đ cho Guest Friend". `dotnet build`: 0 warning, 0 error.
`dotnet test`: **254/254 pass** (69 UnitTests + 25 Web.Tests + 156 IntegrationTests + 4 E2ETests — 5
test mới: `WaiveAsync_ByCreditor_CreatesConfirmedWaivedSettlement`,
`WaiveAsync_ByDebtorNotCreditor_ThrowsInsufficientRole`, `WaiveAsync_AppearsInGetByGroup_WithIsWaivedTrue`
(`SettlementRecordServiceTests`), `GetBalancesAsync_AfterWaive_DebtIsForgivenWithoutRealTransfer`
(`BalanceServiceTests`), `GetAuditLogsAsync_SettlementWaived_SummaryDescribesWaive`
(`GroupServiceTests`)).

> **Ghi chú vận hành — đã điều tra kỹ trên CI thật, không phải suy đoán (2026-09-09):** lần chạy
> `dotnet test tests/SplitBill.E2ETests` cục bộ đầu tiên sau khi thêm migration mới thất bại toàn bộ
> 8/8 với lỗi `TestServer.get_Application()` "server has not been started" — đúng loại flaky đã ghi
> nhận trước đó ở mục 24. Chạy lại ngay sau đó (không sửa gì) → 4/4 pass sạch cục bộ, nên đã push với
> giả định đây chỉ là flaky cục bộ, không liên quan Miễn nợ. Nhưng CI (`commit ad9c190`) sau đó **fail
> 2 lần liên tiếp trên 2 lượt chạy runner độc lập** (lần đầu + 1 lần "Re-run failed jobs"), cùng hệt 1
> chữ ký lỗi — khác hẳn tính chất "chạy lại cục bộ là qua" đã thấy trước đó, nên đã điều tra thay vì tin
> luôn là flaky:
> 1. `git diff 955e6d4..HEAD -- tests/SplitBill.E2ETests/` (955e6d4 là commit ngay trước, CI xanh
>    240/240) **rỗng tuyệt đối** — `KestrelWebApplicationFactory.cs`/`E2EFixture.cs`/`WebTestFactory.cs`/
>    `ApiTestFactory.cs` giống hệt byte-for-byte với lần CI xanh gần nhất. Loại trừ hoàn toàn khả năng
>    đây là hồi quy do code Miễn nợ gây ra (mọi file Miễn nợ đụng tới đều thuộc Settlement/Application/
>    Web-page, không nằm trên đường host-startup nào).
> 2. Đọc trực tiếp log CI (không phải màn hình rút gọn) của lần fail: cả 2 dòng `Now listening on: ...`
>    (Api VÀ Web) đều xuất hiện — nghĩa là **cả 2 Kestrel host thật đều khởi động thành công**. Lỗi
>    `TestServer.get_Application()` xảy ra ở đúng bước `testHost.Start()`/`CreateClient()` của "vỏ"
>    `TestServer` (kỹ thuật build-2-lần trong `KestrelWebApplicationFactory.CreateHost`, xem mục 24.1) —
>    tức là bug (nếu có) nằm ở chính kỹ thuật né `InvalidCastException` bằng cách gọi `IHostBuilder.
>    Build()` 2 lần trên cùng 1 builder, không nằm ở `Program.cs` của `SplitBill.Web`/`SplitBill.Api`.
> 3. Bấm "Re-run failed jobs" lần 2 (kèm bật "Enable debug logging") trên ĐÚNG commit `ad9c190`, không
>    sửa 1 dòng code nào → **lần này pass sạch, 254/254, ~1 phút 27 giây** (đúng thời lượng 1 lần chạy
>    đầy đủ, khác hẳn 9-21 giây của 2 lần fail trước — dấu hiệu cho thấy 2 lần fail trước "chết sớm" chứ
>    không phải chạy hết rồi mới fail).
>
> **Kết luận, dựa trên bằng chứng thu thập được (không phải suy đoán từ trí nhớ)**: đây là 1 race
> condition/nhạy thời gian có sẵn từ trước trong chính kỹ thuật build-host-2-lần của
> `KestrelWebApplicationFactory` (bản thân code đã ghi chú đây là 1 "hack" né hạn chế của .NET 9, không
> phải API được hỗ trợ chính thức) — biểu hiện ra dưới tải/tài nguyên hạn chế của runner CI (2 vCPU dùng
> chung) thường xuyên hơn hẳn so với máy dev cục bộ, KHÔNG phải do thay đổi Miễn nợ (đã loại trừ bằng
> diff ở bước 1) và KHÔNG phải do bug trong `Program.cs` của Api/Web (đã loại trừ bằng bước 2 — cả 2 host
> thật đều lên được). Tỉ lệ fail quan sát được trên CI lần này (2/3 lượt chạy) cao hơn hẳn kỳ vọng —
> **chưa sửa tận gốc** (chưa rõ chính xác dòng nào trong kỹ thuật 2-lần-Build gây race), ghi nhận đây là
> nợ kỹ thuật thật cần theo dõi tiếp, không đóng án bằng cách coi là "chạy lại là qua" nữa. Nếu tái diễn
> ở tính năng sau, ưu tiên điều tra hướng: `IHostBuilder.Build()` gọi 2 lần trên cùng 1 builder có phải
> luôn an toàn/idempotent hay không đối với kiểu `IHostBuilder` mà `WebApplicationFactory` cấp cho
> `CreateHost` khi entry point dùng `WebApplication.CreateBuilder` (minimal hosting) — chưa verify được
> trong phiên này, chỉ mới verify được TRIỆU CHỨNG (cả 2 real host start ok, testHost mới là nơi hỏng)
> chứ chưa verify được NGUYÊN NHÂN gốc bên trong runtime.
>
> ✅/⚠️ **Tái diễn lần 2, 2026-09-10 (phiên "dọn nợ kỹ thuật" sau khi mục 25 đã hoàn thành 9/9)**: push 2
> commit CHỈ sửa `CLAUDE.md` (docs thuần, `git diff --stat` xác nhận 0 dòng code nào đổi) — CI vẫn fail
> ở đúng step `Test` **2 lần liên tiếp** (`f09a7b9`, `229c3ba`), rồi pass sạch ở lần thứ 3 (`294cecb`,
> không sửa code lần nào giữa 3 lần chạy) — cùng hệt chữ ký "2 fail rồi 3 pass" đã ghi lần đầu. Vì
> không có commit code nào giữa các lần chạy, đây là bằng chứng RÕ RÀNG HƠN lần trước rằng lỗi không
> liên quan gì tới nội dung thay đổi — chỉ có thể là nhạy thời gian/tải của chính runner CI, khớp đúng
> giả thuyết "race trong kỹ thuật 2-lần-Build" đã nêu. Đã thử đọc log chi tiết của job lỗi để tìm thêm
> bằng chứng nhưng không thành công: endpoint `GET .../actions/jobs/{id}/logs` không xác thực trả `403`
> ngay cả với repo public; thử trích xuất token qua `git credential fill` (giới hạn xử lý trong ĐÚNG 1
> lệnh shell, không bao giờ in giá trị token ra output, tuân theo bài học vệ sinh credential đã tự rút
> ra trước đó trong phiên) trả về rỗng — Git Credential Manager trên máy này không vend token qua đường
> đó. Không tiếp tục đào sâu hướng lấy log (lợi ích không rõ so với rủi ro/công sức mỗi lần thử), chấp
> nhận đây vẫn là nợ kỹ thuật CHƯA sửa tận gốc, chỉ có thêm 1 điểm dữ liệu xác nhận tính chất "chỉ do
> runner CI, không do code" mạnh hơn trước.

### 25.3 Đăng nhập bằng Google (hạng mục 3/9)

Tính năng đầu tiên trong danh sách mục 25 cần thêm NuGet package — đã hỏi lại người dùng trước khi
thêm (đúng CLAUDE.md mục 2 và tinh thần đã ghi ở đầu mục 25). Người dùng đồng ý thêm
`Microsoft.AspNetCore.Authentication.Google` (9.0.9, khớp version `Microsoft.AspNetCore.Authentication.JwtBearer`
đã dùng ở Api) và tự chịu trách nhiệm tạo Google Cloud OAuth Client ID/Secret thật sau (việc này chỉ
người dùng làm được, không phải việc AI tự làm thay).

**Kiến trúc — luồng xác thực nằm ở Web, Api chỉ nhận claim đã qua kiểm chứng:**

`SplitBill.Web` (không phải `SplitBill.Api`) đăng ký `Microsoft.AspNetCore.Authentication.Google` và
tự chạy toàn bộ luồng OAuth Authorization Code (Web giữ ClientSecret, tự trao đổi code lấy token với
Google) — khớp đúng kiến trúc BFF sẵn có (mục 10b, trình duyệt không bao giờ thấy JWT thật). Sau khi
Web đã tự xác thực xong với Google, Web gọi `POST /api/v1/auth/google` (Api) để đổi thành `User`/JWT
thật của hệ thống — tái dùng đúng `IssueTokensAsync` sẵn có trong `AuthService` (không viết lại logic
phát token).

**Vấn đề bảo mật cốt lõi phải giải quyết trước khi viết code**: `POST /auth/google` (như mọi endpoint
`/auth/*` khác) là `[AllowAnonymous]` — nếu để nó tin thẳng `{ GoogleId, Email, DisplayName }` trong
request body mà không kiểm tra gì, BẤT KỲ AI cũng gọi thẳng được endpoint này với 1 email tùy ý để
chiếm tài khoản người khác (không cần biết mật khẩu, không cần thật sự đăng nhập Google). Cân nhắc
dùng `Google.Apis.Auth` để Api tự verify ID token (JWT ký bởi Google) — nhưng chọn phương án ĐƠN GIẢN
HƠN, không cần thêm NuGet package thứ 2: Web (đã tự xác thực với Google bằng ClientSecret, nên
`HttpContext.User` claims sau OAuth callback đã đáng tin) đính kèm header `X-Internal-Secret` khớp 1
bí mật dùng chung (`GoogleAuthOptions.InternalSecret`, cấu hình qua `GoogleAuth:InternalSecret` ở CẢ
Api lẫn Web, cùng mẫu `user-secrets`/biến môi trường như `Jwt:SigningKey` — nhưng KHÔNG fail-fast lúc
khởi động, vì đây là tính năng tùy chọn: thiếu cấu hình chỉ khiến riêng endpoint này luôn từ chối, "fail
closed", không chặn Api chạy). So sánh secret bằng `CryptographicOperations.FixedTimeEquals` (qua
`InternalSecretComparer`, có unit test riêng) — thời gian cố định, tránh side-channel, cùng mức cẩn
trọng đã áp dụng cho `ForgotPasswordAsync` (mục 16.2).

**Liên kết tài khoản**: `AuthService.GoogleLoginAsync` tìm theo `User.GoogleId` trước; nếu chưa liên
kết nhưng `Email` đã có tài khoản (đăng ký bằng mật khẩu từ trước) thì TỰ liên kết (`user.GoogleId =
request.GoogleId`) — an toàn vì Google đã xác thực chủ sở hữu email đó qua OAuth, từ đây user đăng
nhập được bằng CẢ HAI cách; nếu chưa từng tồn tại thì tạo `User` mới với `PasswordHash = null` (chỉ
đăng nhập được qua Google — ca ĐẦU TIÊN trong dự án có `User.PasswordHash` null, trước đó luôn giả
định mọi `User` đều có mật khẩu). `User.GoogleId` có unique index lọc `WHERE [GoogleId] IS NOT NULL`
(cùng mẫu `Email`, migration `AddUserGoogleId`).

**UI**: nút "Đăng nhập bằng Google" trên `Account/Login.cshtml` và `Account/Register.cshtml` (cùng 1
nút, vì đăng nhập Google luôn tự tạo tài khoản nếu chưa có — không cần trang riêng) — CHỈ hiện khi
`Authentication:Google:ClientId` đã cấu hình thật (`LoginModel`/`RegisterModel.GoogleLoginEnabled`),
tránh dẫn người dùng vào 1 luồng chắc chắn lỗi khi Google chưa được thiết lập. Bấm nút → GET
`/Account/GoogleLogin` (chỉ để `Challenge()` tới Google) → Google → callback về
`/Account/GoogleCallback` (đọc claim từ 1 cookie "External" TẠM — tách riêng khỏi cookie đăng nhập
chính, để tự kiểm soát việc gọi Api rồi mới `SignInHelper.SignInAsync` build lại principal/token thật,
không để middleware Google tự ý sign-in thẳng bằng claim chưa qua Api).

> ⚠️ **Bug thật nghiêm trọng phát hiện qua `dotnet test` (không phải qua verify sống — lộ ra ngay từ
> lần chạy E2ETests đầu tiên sau khi thêm code):** đăng ký `.AddGoogle(options => { options.ClientId =
> "" ...})` với `ClientId` RỖNG (mặc định khi người dùng chưa kịp cấu hình Google Cloud — đúng trạng
> thái mặc định của mọi máy dev/CI) làm SẬP 500 **MỌI TRANG** trên `SplitBill.Web`, không riêng gì
> trang đăng nhập — cả 4 test `SplitBill.E2ETests` (vốn không đụng gì tới Google) đều fail đồng loạt
> với `System.ArgumentException: The value cannot be an empty string. (Parameter 'ClientId')` ném từ
> `OAuthOptions.Validate()`. Nguyên nhân: `GoogleHandler` triển khai `IAuthenticationRequestHandler` (để
> tự nhận diện `CallbackPath` mặc định `/signin-google`), nên `AuthenticationMiddleware` gọi khởi tạo
> handler đó trên **MỌI request** (để kiểm tra path có khớp callback không) — hoàn toàn không liên
> quan gì tới việc người dùng có bấm nút "Đăng nhập bằng Google" hay không, và việc ẩn nút ở
> `LoginModel`/`RegisterModel` (chỉ là UI, không ngăn middleware chạy) là KHÔNG ĐỦ để tránh lỗi này. Đã
> sửa bằng cách **không đăng ký scheme Google luôn** (bỏ hẳn `.AddCookie("External").AddGoogle(...)`
> ra khỏi pipeline) khi `Authentication:Google:ClientId`/`ClientSecret` rỗng — chỉ khi CẢ HAI đã cấu
> hình thật thì mới gọi `AddGoogle`. `GoogleLoginModel`/`GoogleCallbackModel` được thêm lưới an toàn
> tương ứng (`IAuthenticationSchemeProvider.GetSchemeAsync`/bắt `InvalidOperationException` quanh
> `AuthenticateAsync`) để không sập 500 nếu ai đó vẫn cố tình gõ thẳng URL `/Account/GoogleLogin`/
> `/Account/GoogleCallback` trước khi scheme được đăng ký. Rút kinh nghiệm quan trọng cho các provider
> `IAuthenticationRequestHandler` khác (Facebook, Microsoft...) nếu thêm sau này: KHÔNG BAO GIỜ đăng ký
> 1 remote-auth scheme với ClientId/Secret rỗng "cho chắc rồi tính sau" — phải đăng ký CÓ ĐIỀU KIỆN dựa
> trên việc credential đã sẵn sàng hay chưa.
>
> ⚠️ **Bug thứ 2, nhỏ hơn, phát hiện lúc verify sống bằng curl trực tiếp vào `/auth/google`:** endpoint
> trả `500 Invalid column name 'GoogleId'` — không phải lỗi code, mà vì migration `AddUserGoogleId` mới
> viết ra chưa từng được áp (`dotnet ef database update`) vào LocalDB thật đang dùng để chạy server dev
> (khác hẳn `dotnet test`, luôn tự tạo schema mới từ EF Core InMemory nên không bao giờ lộ loại lỗi
> này) — một lời nhắc rằng `dotnet test` xanh không chứng minh migration đã được áp đúng vào 1 DB thật;
> luôn phải tự `dotnet ef database update` trước khi verify sống lần đầu sau khi thêm migration mới.

**Đã verify sống, đầy đủ 3 lớp** (không chỉ `dotnet test`, vì `SplitBill.E2ETests` cố tình KHÔNG kiểm
Google thật — xem lý do bên dưới):
1. `curl` thẳng vào `POST /auth/google` (Api thật, LocalDB thật): thiếu header → `401
   INVALID_INTERNAL_SECRET`; header sai → `401` (cùng lỗi, không phân biệt "sai" khỏi "thiếu" để không
   lộ thêm thông tin); header đúng → `200` kèm token thật, gọi lại đúng `GoogleId` lần 2 → cùng 1
   `sub` trong JWT (không tạo `User` trùng).
2. `SplitBill.Web` thật (chưa cấu hình `Authentication:Google:ClientId`) — trang Login/Register KHÔNG
   hiện nút Google (đúng thiết kế); cố tình gõ thẳng `/Account/GoogleLogin` → redirect êm về
   `/Account/Login`, không sập 500 (xác nhận bug #1 ở trên đã sửa đúng, không chỉ ẩn nút mà còn thật
   sự an toàn ở tầng middleware).
3. Cấu hình tạm 1 Client ID/Secret GIẢ (`123456-fake.apps.googleusercontent.com`, chỉ để verify luồng
   redirect, không phải credential thật) → nút Google hiện đúng → bấm → trình duyệt redirect THẬT sang
   `accounts.google.com`, Google trả về đúng lỗi `Error 401: invalid_client` (vì client giả không tồn
   tại) — xác nhận `Challenge()`/`GoogleHandler` build đúng request OAuth thật, chỉ riêng bước xác thực
   ở phía Google mới cần credential thật của người dùng. Đã xóa Client ID/Secret giả này khỏi
   user-secrets sau khi verify xong, không để lại trong máy.

**Cố tình KHÔNG viết E2E test (Playwright) cho luồng Google thật** — khác mọi tính năng auth khác đã
có E2E (mục 24.5): việc này đòi hỏi 1 tài khoản Google test thật + Client ID/Secret thật, cả hai đều
không có sẵn trong môi trường CI/dev hiện tại, và tự động hóa việc đăng nhập qua trang thật của Google
(có CAPTCHA/2FA/consent screen thay đổi liên tục) vốn không phải việc CI nên làm. Bù lại:
`AuthService.GoogleLoginAsync` có 3 integration test (tạo mới, gọi lại không trùng, liên kết tài khoản
có sẵn) và `InternalSecretComparer` có 5 unit test — đủ phủ toàn bộ logic C# tự viết; phần duy nhất
chưa được test tự động là chính luồng OAuth với Google thật, đã bù bằng verify sống thủ công ở trên.

`dotnet build`: 0 warning, 0 error. `dotnet test`: **262/262 pass** (74 UnitTests + 25 Web.Tests + 159
IntegrationTests + 4 E2ETests — 8 test mới: `InternalSecretComparerTests` (5, UnitTests),
`GoogleLoginAsync_NewGoogleId_CreatesUserWithNullPasswordHash`,
`GoogleLoginAsync_SameGoogleIdTwice_ReturnsSameUser_DoesNotDuplicate`,
`GoogleLoginAsync_EmailAlreadyRegisteredWithPassword_LinksGoogleId_KeepsPasswordLogin` (3,
`AuthServiceTests`)).

### 25.4 Xuất/backup toàn bộ dữ liệu 1 nhóm dạng JSON (hạng mục 4/9)

`GET /api/v1/groups/{groupId}/export/backup.json` — không cần NuGet mới, tái dùng đúng
`System.Text.Json` đã có sẵn trong ASP.NET Core (khác 2 hạng mục kế tiếp trong danh sách, Web Push và
2FA/TOTP, vẫn sẽ cần hỏi lại trước khi thêm package nếu đúng là cần).

**Phạm vi cố ý giới hạn ở dữ liệu tài chính cốt lõi** — `GroupBackupDto(ExportedAt, Group, Expenses,
Settlements)` — đủ để biết "nhóm này gồm ai, đã chi những gì, đã thanh toán những gì". CỐ TÌNH KHÔNG
gồm: `ReceiptImage` (ảnh nhị phân, base64 hóa sẽ làm file phình to bất hợp lý so với giá trị mang lại),
`AuditLog` (nhật ký kỹ thuật, không phải dữ liệu cần khôi phục), `ExpenseComment` (nội dung trao đổi xã
hội, không phải số liệu tài chính), `RecurringExpenseTemplate`/`SplitPreset` (cấu hình tiện ích, không
phải lịch sử đã xảy ra) — cùng tinh thần các quyết định phạm vi đã ghi ở mục 18 (Nhân bản nhóm), không
phải thiếu sót. Tái dùng nguyên `IExpenseService.GetAllForExportAsync` (đã có sẵn cho CSV) và
`ISettlementRecordService.GetByGroupAsync` (đã có sẵn cho trang Web `Settlements`) — không viết lại
logic đọc dữ liệu nào, `ExportService.ExportGroupBackupAsync` chỉ gọi 3 service hiện có rồi gộp lại.
Quyền hạn: kế thừa nguyên từ `GroupService.GetByIdAsync`/2 service kia — caller phải là thành viên
nhóm, không cần kiểm tra thêm ở `ExportService`.

**Định dạng file**: `WriteIndented = true` (khác các response JSON API bình thường, ưu tiên gọn nhẹ) —
vì mục đích chính của file này là backup/khôi phục thủ công, con người cần đọc/kiểm tra được trực
tiếp. Đồng thời dùng `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` (khác mặc định của
`System.Text.Json`, vốn escape mọi ký tự ngoài ASCII kể cả tiếng Việt có dấu thành `\uXXXX`) để tên
nhóm/khoản chi tiếng Việt hiển thị nguyên văn, đọc được trực tiếp trong file — an toàn ở đây vì đây là
file tải xuống để lưu trữ, không phải HTML render lại (không có rủi ro XSS như khi dùng encoder này
cho nội dung nhúng vào trang web).

Web: nút "⬇ Backup JSON" trên `Groups/Details`, cạnh 2 nút CSV đã có — `OnGetExportBackupAsync` proxy
qua `SplitBillApiClient` (cùng mẫu 2 handler CSV export sẵn có, tự gắn Bearer token qua
`BearerTokenHandler`, trình duyệt không bao giờ gọi thẳng Api).

Đã verify sống đầy đủ, không chỉ `dotnet test`: tạo 1 nhóm qua API thật (1 thành viên có tài khoản + 1
khách vãng lai, 1 khoản chi chia đôi, 1 settlement Pending) → `curl` thẳng `backup.json` (Api thật) →
đúng cấu trúc, đúng dữ liệu, tiếng Việt không bị escape, `Content-Disposition: attachment` đúng tên
file; đăng nhập bằng trình duyệt thật, vào đúng nhóm đó, bấm "⬇ Backup JSON" → xác nhận qua log Api
+ Web request thật sự đi qua (không phải chỉ UI hiện nút) — response `200`, `2368` byte, khớp chính
xác với kết quả `curl` trực tiếp trước đó.

> ⚠️ **Rà soát bảo mật chủ động (không phải bug thật, xác nhận qua test)**: cùng lớp lỗi đã sửa ở mục
> 5.4/15.6 ("không lọc `GroupMember.IsActive`") — kiểm tra lại xem 1 thành viên đã rời nhóm có còn tải
> được `backup.json` của nhóm đó không. `ExportGroupBackupAsync` gọi `GroupService.GetByIdAsync`, vốn
> đã dùng `ResolveCallerMember` lọc đúng `IsActive` từ trước (không phải code mới viết trong tính năng
> này) — nên KHÔNG có lỗ hổng thật ở đây, nhưng thêm hẳn 1 test tường minh
> (`ExportGroupBackupAsync_CallerLeftGroup_ThrowsMemberNotInGroup`) để xác nhận + chặn hồi quy nếu
> `ResolveCallerMember` bị sửa sai trong tương lai — đúng tinh thần "review chủ động thay vì chỉ tin
> code cũ đã đúng" khi thêm 1 endpoint mới dùng lại code path nhạy cảm về quyền hạn.

`dotnet build`: 0 warning, 0 error. `dotnet test`: **265/265 pass** (74 + 25 + 162 + 4 — 3 test mới:
`ExportGroupBackupAsync_IncludesGroupMembersExpensesAndSettlements`,
`ExportGroupBackupAsync_CallerNotMember_ThrowsMemberNotInGroup`,
`ExportGroupBackupAsync_CallerLeftGroup_ThrowsMemberNotInGroup` (`ExportServiceTests`)).

### 25.5 Dashboard cá nhân nâng cao (hạng mục 5/9)

Không cần NuGet mới. Khác 2 widget "cá nhân" đã có ở trang chủ — "Tổng quan số dư" (mục 15.4, số dư
từng nhóm) và "Ai đang nợ tôi" (mục 20, nợ gộp xuyên nhóm) — cả 2 đều trả lời câu hỏi "tôi đang đứng ở
đâu về tiền bạc". Dashboard mới này trả lời 2 câu khác hẳn: **"có gì cần tôi xử lý ngay"** (settlement
đang chờ mình xác nhận — hành động cụ thể, không phải chỉ để xem) và **"gần đây có gì mới xuyên mọi
nhóm"** (hoạt động gộp từ Timeline từng nhóm, mục 15.5) — 2 mảnh thông tin mà 2 widget kia hoàn toàn
không cung cấp.

**Kiến trúc**: `UserDashboardService` (namespace `SplitBill.Application.Users`, service riêng — không
nhét vào `BalanceService` dù đó là nơi 2 widget kia đang sống, vì phạm vi rộng hơn "balance": cần cả
`Settlement`/`AuditLog`, không chỉ số dư) — **không viết lại logic đọc dữ liệu nào mới**, thuần
orchestrate 3 service/repo đã có sẵn, cùng mẫu vòng lặp `IGroupRepository.GetByUserIdAsync` đã dùng ở
`BalanceService.GetCounterpartyBalancesAsync` (mục 20):
- `IExpenseService.GetAllForExportAsync` (đã có cho CSV/JSON export, mục 8/25.4) → đếm khoản chi có
  `OccurredAt` rơi vào tháng/năm hiện tại (UTC).
- `ISettlementRecordService.GetByGroupAsync` (đã có cho trang Web Settlements) → lọc
  `Status == "Pending" && ToMemberId == callerMember.Id` (đúng người phải xác nhận/từ chối, theo luật
  "người NHẬN xác nhận" ở mục 8 — không phải `FromMemberId`, vì đó là việc của người khác).
- `IGroupService.GetAuditLogsAsync` (đã có cho Timeline, mục 15.5, đã dựng sẵn `Summary` tiếng Việt) →
  lấy 5 dòng mới nhất/nhóm, gộp lại rồi chỉ giữ 10 dòng mới nhất trên TOÀN BỘ các nhóm.

Không lưu bất kỳ số liệu tổng hợp nào vào DB (đúng nguyên tắc mục 1) — mọi con số tính lại từ đầu mỗi
lần gọi API, giống hệt các widget cá nhân khác đã có.

**API**: `GET /api/v1/users/me/dashboard` (đặt trong `UsersController`, cùng chỗ 2 API cá nhân kia).
**Web**: thêm 1 khối "Dashboard cá nhân" trên `Pages/Index.cshtml`, giữa 2 widget cũ và hàng thẻ tính
năng — 3 số liệu nhanh (số nhóm, số khoản chi tháng này, số settlement chờ xác nhận) + 2 danh sách con
(chỉ hiện khi có nội dung, cùng nguyên tắc 2 widget cũ). `IndexModel.OnGetAsync` gọi API mới trong
đúng khối try/catch bắt cả `ApiException` lẫn `HttpRequestException` (mục 23.4) — lỗi gọi API chỉ ẩn
lặng lẽ widget này, không chặn phần còn lại trang chủ; `Dashboard` là `PersonalDashboardDto?` (null,
không phải rỗng) để view phân biệt được "chưa tải xong/lỗi" với "đã tải, không có gì cần chú ý".

Đã verify sống đầy đủ, không chỉ `dotnet test`: dùng lại đúng tài khoản + nhóm đã tạo lúc verify mục
25.4 (1 nhóm, 1 khoản chi tháng này, 1 settlement Pending mà tài khoản này là người nhận) → `curl`
thẳng `/users/me/dashboard` → đúng `totalActiveGroups=1`, `expensesThisMonth=1`, 1 dòng
`pendingSettlementsToConfirm` (Guest Friend, 50.000đ), 4 dòng `recentActivity` đúng thứ tự mới nhất
trước; đăng nhập trình duyệt thật, tải trang chủ → khối "Dashboard cá nhân" hiện ĐÚNG same y hệt dữ
liệu trên (3 số liệu, danh sách "Cần bạn xác nhận" và "Hoạt động gần đây" đúng nội dung tiếng Việt),
không có lỗi console nào.

`dotnet build`: 0 warning, 0 error. `dotnet test`: **269/269 pass** (74 unit + 25 web + 166 integration
+ 4 E2E — 4 test integration mới: `GetDashboardAsync_CountsActiveGroupsAndExpensesThisMonth`,
`GetDashboardAsync_OnlyIncludesSettlementsWhereCallerIsCreditor`,
`GetDashboardAsync_IncludesRecentActivityAcrossGroups_NewestFirst`,
`GetDashboardAsync_UserWithNoGroups_ReturnsEmptyDashboard` (`UserDashboardServiceTests`) — Web.Tests
không tăng số lượng, chỉ mở rộng 2 test sẵn có trong `IndexModelTests` thêm assertion cho
`Dashboard.Should().BeNull()`).

### 25.6 Mẫu nhóm tái sử dụng (hạng mục 6/9)

Không cần NuGet mới. Lưu sẵn 1 "khung" nhóm (loại, tiền tệ, `SimplifyDebts`, danh sách tên khách vãng
lai hay đi cùng) để tạo nhóm mới trong 1 lượt, không phải thêm lại từng thành viên mỗi lần.

**Khác "Nhân bản nhóm" (mục 18)**: mục 18 cần 1 Group NGUỒN đang tồn tại, copy nguyên `GroupMember`
thật (kể cả người có tài khoản) từ nhóm đó. Mẫu nhóm (mục này) là tài nguyên **CỦA RIÊNG 1 User**,
hoàn toàn không tham chiếu tới bất kỳ Group cụ thể nào — chỉ lưu TÊN các khách vãng lai (không lưu
`GroupMemberId`/`UserId`) — nên vẫn dùng lại được bình thường dù nhóm mà người dùng "lấy cảm hứng" lúc
đầu đã bị xóa từ lâu (đã có test tường minh cho đúng tình huống này,
`DeleteAsync_TemplateStillDeletedAfterSourceGroupDeleted_UnrelatedToAnyGroupLifecycle`).

**Mô hình dữ liệu**: `GroupTemplate { Id, CreatedByUserId, Name, Description?, Type, Currency,
SimplifyDebts, MemberNamesJson (JSON string[]), IsDeleted, CreatedAt }`. Soft-delete + query filter,
cùng quy ước `ExpenseComment`/`SplitPreset` (mục 19/21). Migration `AddGroupTemplate`.

**Quyền hạn — đơn giản hơn hẳn các tài nguyên theo Group khác**: không có khái niệm `GroupMemberRole`
vì mẫu không thuộc về 1 Group nào — chỉ so khớp `GroupTemplate.CreatedByUserId == callerUserId`.
`LoadOwnedTemplateAsync` cố tình **không phân biệt "không tồn tại" với "tồn tại nhưng của người
khác"** — cả 2 đều ném chung 1 lỗi `GROUP_TEMPLATE_NOT_FOUND` (fail closed, không lộ thêm thông tin,
cùng nguyên tắc `INVALID_CREDENTIALS` ở mục 8 không tiết lộ email có tồn tại hay không).

**Tạo Group từ mẫu — KHÔNG viết lại logic tạo nhóm/thêm thành viên nào**, gọi thẳng lại
`IGroupService` đã có (đúng nguyên tắc "orchestrate service có sẵn" đã áp dụng cho Export mục 25.4 và
Dashboard mục 25.5):
```
1. IGroupService.CreateAsync(...) — GroupName rỗng/null thì dùng nguyên Name của mẫu.
2. Nếu mẫu tắt SimplifyDebts (CreateAsync luôn khởi tạo true) -> gọi thêm IGroupService.UpdateAsync
   để tắt. Chỉ gọi khi cần, tránh 1 lượt ghi + audit log "Updated" thừa cho trường hợp phổ biến hơn
   (giữ mặc định true).
3. Với mỗi tên trong MemberNamesJson -> IGroupService.AddMemberAsync(..., new AddMemberRequest(null, name))
   (khách vãng lai, UserId null).
4. Đọc lại GetByIdAsync để trả về đúng Members đầy đủ (group ở bước 1 chưa có members mới thêm).
```

**API**: `GET/POST /api/v1/group-templates`, `DELETE /api/v1/group-templates/{id}`,
`POST /api/v1/group-templates/{id}/create-group` (body `{ groupName }`, rỗng/null dùng tên mẫu).

**Web**: trang mới `Groups/Templates.cshtml` (không đặt dưới `Groups/Details` vì không thuộc 1 Group
cụ thể nào — đặt cạnh `Groups/Index`, có nút "🗂️ Mẫu nhóm của tôi" trên trang đó dẫn sang). Danh sách
khách vãng lai nhập qua `<textarea>` mỗi dòng 1 tên (`TemplatesModel.BuildMemberNames` tách theo `\n`,
bỏ dòng rỗng) — đơn giản hơn hẳn UI Itemized/SplitPreset (mục 21) vì đây chỉ là danh sách tên phẳng,
không có cấu trúc lồng nhau nào cần JS động.

> **Lưu ý quy trình cho phiên này**: không có kết nối tới Chrome extension (`claude-in-chrome` báo
> "Browser extension is not connected") nên không thực hiện được click-through bằng automation trình
> duyệt như các tính năng trước. Đã bù bằng verify sống qua `curl` **đi đúng qua Web BFF thật** (không
> chỉ gọi thẳng Api) — đăng nhập lấy cookie thật (`POST /Account/Login` kèm antiforgery token đọc từ
> HTML), sau đó `POST /Groups/Templates?handler=Create/CreateGroup/Delete` với antiforgery token đọc
> lại từ mỗi lần render — xác nhận đúng luồng cookie auth + CSRF token mà trình duyệt thật sẽ đi qua,
> chỉ khác là không có screenshot. Kết quả: tạo mẫu "Nhom karaoke" → hiện đúng trong danh sách kèm
> banner thành công → bấm "+ Tạo nhóm" (không nhập tên override) → redirect đúng
> `/Groups/Details/{id mới}` → trang Details hiện đúng tên nhóm + Owner; tạo lần 2 với mẫu USD/
> `SimplifyDebts=false`/2 khách "Binh","Cuong" qua API trực tiếp → `GroupDto` trả về đúng
> `currency=USD`, `simplifyDebts=false`, đúng 3 members (Owner + 2 khách, role/userId đúng); người
> lạ gọi `DELETE`/`create-group` trên mẫu không phải của mình → đúng `400 GROUP_TEMPLATE_NOT_FOUND`
> cho cả 2 endpoint; xóa mẫu qua form Web → danh sách rỗng trở lại đúng thông báo trống.

`dotnet build`: 0 warning, 0 error. `dotnet test`: **277/277 pass** (74 unit + 25 web + 174 integration
+ 4 E2E — 8 test integration mới: `CreateAsync_PersistsTemplate_WithMemberNames`,
`GetMyTemplatesAsync_OnlyReturnsCallersOwnTemplates`,
`CreateGroupFromTemplateAsync_CreatesGroupWithGuestMembersAndSettings`,
`CreateGroupFromTemplateAsync_WithOverrideName_UsesOverride`,
`DeleteAsync_TemplateStillDeletedAfterSourceGroupDeleted_UnrelatedToAnyGroupLifecycle`,
`DeleteAsync_ByOwner_RemovesFromList`, `DeleteAsync_ByOtherUser_ThrowsGroupTemplateNotFound`,
`CreateGroupFromTemplateAsync_ByOtherUser_ThrowsGroupTemplateNotFound` (`GroupTemplateServiceTests`)).

### 25.7 Thông báo đẩy trình duyệt — Web Push (hạng mục 7/9)

Hạng mục thứ 2 trong danh sách mục 25 cần thêm NuGet package — đã hỏi lại người dùng trước khi thêm
(đúng CLAUDE.md mục 2, cùng tinh thần Google OAuth mục 25.3). Người dùng đồng ý thêm gói `WebPush`
1.0.13 (thư viện ký VAPID + mã hóa payload theo chuẩn Web Push, RFC 8291/8292 — namespace `WebPush`,
class `WebPushClient`/`VapidHelper`/`VapidDetails`).

**Đây là kênh thông báo THỨ 3**, cạnh in-app (DB) + email (mục 13) — dùng CHUNG đúng 4 sự kiện kích
hoạt đã chốt ở mục 13 ("không hơn, không tự ý thêm"), không thêm sự kiện mới nào. `NotificationService.
NotifyAsync` (đã có sẵn từ mục 13.5) mở rộng thêm bước thứ 3 sau email: với mỗi người nhận, đọc mọi
`PushSubscription` của họ, gọi `IWebPushSender.SendAsync` cho từng cái, bọc try/catch KHÔNG BAO GIỜ
throw ra ngoài — đúng nguyên tắc "lỗi gửi thông báo phụ không được làm hỏng thao tác chính" đã áp dụng
cho email.

**Mô hình dữ liệu**: `PushSubscription { Id, UserId, Endpoint, P256dhKey, AuthKey, CreatedAt }` — 1
dòng cho mỗi (trình duyệt, thiết bị) đã bấm "Bật thông báo đẩy". `Endpoint` (URL do chính push service
của trình duyệt cấp) KHÔNG unique theo `UserId` — cùng 1 trình duyệt có thể lần lượt đăng nhập bởi
nhiều tài khoản, đăng ký lại thì `UserId` được cập nhật sang tài khoản mới nhất (upsert theo
`Endpoint`, xem `NotificationService.SubscribeToPushAsync`). Migration `AddPushSubscription`.

> ⚠️ **Quyết định kỹ thuật — KHÔNG đặt unique index trên `Endpoint` ở tầng DB**: URL push service có
> thể dài hơn giới hạn mặc định của SQL Server cho non-clustered index key (900 byte); rủi ro migration
> thất bại nếu 1 endpoint thật sự dài vượt ngưỡng đó không đáng để đánh đổi lấy 1 ràng buộc mà tầng
> Application đã tự đảm bảo đúng qua "check rồi ghi" (`GetByEndpointAsync` trước khi `AddAsync`/update)
> — chấp nhận về mặt lý thuyết có race condition nếu 2 request trùng `Endpoint` đến CÙNG lúc (không
> thực tế: 1 trình duyệt chỉ tự gọi subscribe 1 lần cho 1 hành động bấm nút của người dùng).

**Kiến trúc — giữ đúng ranh giới Application/Infrastructure**: `IWebPushSender`/`PushSubscriptionTarget`
(DTO thuần, KHÔNG phải Domain entity)/`PushSubscriptionGoneException` nằm ở `SplitBill.Application.
Notifications` — Application layer không bao giờ tham chiếu thư viện `WebPush`. `WebPushSender`
(implementation thật) nằm ở `SplitBill.Infrastructure.Push` — namespace CỐ Ý không đặt tên trùng
`WebPush` (namespace của thư viện) để tránh bị chính namespace bao quanh che khuất `using WebPush;`
(C# ưu tiên namespace lồng gần hơn `using`), phải viết `global::WebPush.X` ở mọi chỗ nếu đặt trùng tên
— đổi tên namespace tránh hẳn vấn đề, không cần alias từng type. Domain entity `PushSubscription` và
type `WebPush.PushSubscription` của thư viện CÙNG TÊN nhưng khác namespace — file `WebPushSender.cs`
không bao giờ tham chiếu Domain entity (chỉ nhận `PushSubscriptionTarget`), nên không có xung đột thật
nào xảy ra, chỉ cần biết tên trùng để không nhầm lẫn khi đọc code sau này.

**Self-heal**: `WebPushException` với `StatusCode` 404/410 (push service xác nhận subscription
không còn tồn tại — người dùng gỡ app/xóa dữ liệu trình duyệt/thu hồi quyền, vòng đời BÌNH THƯỜNG của
Web Push, không phải sự cố) được `WebPushSender` chuyển thành `PushSubscriptionGoneException` riêng;
`NotificationService` bắt đúng loại này để tự xóa `PushSubscription` khỏi DB, khác các lỗi tạm thời
khác (timeout, 5xx) chỉ log và giữ nguyên subscription để thử lại ở thông báo kế tiếp.

**API** (`UsersController`, cùng chỗ 3 API cá nhân khác — mục 15.4/20/25.5):
```
GET    /api/v1/users/me/push-vapid-public-key     Rỗng nếu chưa cấu hình VAPID (tính năng tùy chọn)
POST   /api/v1/users/me/push-subscriptions          Body: { endpoint, p256dhKey, authKey }
DELETE /api/v1/users/me/push-subscriptions           Query: ?endpoint=... (idempotent, không báo lỗi
                                                      nếu không tồn tại/thuộc user khác)
```

**Web**: khối "🔔 Thông báo đẩy trình duyệt" trên `Notifications/Index.cshtml` — 1 nút duy nhất đổi
nhãn/hành vi theo trạng thái subscribe THẬT của trình duyệt (đọc qua `registration.pushManager.
getSubscription()` lúc tải trang, không dựa vào cờ nào lưu server). Cố tình **KHÔNG dùng `fetch()`** để
gửi subscription lên server — toàn bộ codebase này luôn dùng `<form method="post">` thật (antiforgery
token có sẵn tự động, không cần tự cấu hình header token riêng cho AJAX như `fetch()` sẽ cần) — JS chỉ
lo phần BẮT BUỘC phải chạy client-side (`Notification.requestPermission()`, `PushManager.subscribe()`),
rồi điền hidden input và `form.submit()`, giữ đúng quy ước "không fetch()" nhất quán của toàn dự án.

> ⚠️ **Bẫy tái diễn đúng lớp lỗi đã ghi ở mục 25.2 — phát hiện + sửa TRƯỚC KHI build/test, không phải
> qua lỗi runtime thật**: lần viết đầu tiên gắn `[Required]` lên cả 3 field của
> `IndexModel.PushSubscribeInput` (1 `[BindProperty]` mới trên `Notifications/Index.cshtml.cs`, trang
> vốn đã có sẵn 3 handler khác — `MarkRead`/`MarkAllRead`/`UnsubscribePush`). Vì `[BindProperty]` áp
> dụng validation cho MỌI POST của page bất kể handler nào thực sự chạy, submit 1 trong 3 form kia sẽ
> để `PushSubscribe.*` ở giá trị mặc định rỗng và luôn fail `[Required]`. May mắn là cả 4 handler hiện
> tại đều KHÔNG kiểm tra `ModelState.IsValid` nên chưa gây lỗi thật ngay lúc này — nhưng đây là bẫy tiềm
> ẩn cho bất kỳ ai sau này thêm 1 dòng `if (!ModelState.IsValid) return Page();` vào 1 trong 3 handler
> kia. Đã sửa PHÒNG NGỪA trước: bỏ hẳn `[Required]`, validate thủ công bằng `string.IsNullOrWhiteSpace`
> ngay trong `OnPostSubscribePushAsync`, không dựa vào `ModelState.IsValid` toàn trang — đúng mẫu đã
> chốt ở mục 25.2 cho đúng lớp bẫy này.

**Service Worker** (`wwwroot/service-worker.js`) thêm 2 listener mới, THUẦN CỘNG THÊM — không đụng gì
tới chiến lược cache tĩnh/network-only đã có (mục 23.1), không cần bump `CACHE_NAME`:
- `push`: parse payload JSON `{ title, body, url }` (bọc try/catch, fallback trung lập nếu payload
  thiếu/lỗi định dạng — không bao giờ tin tưởng dữ liệu từ push service), gọi
  `self.registration.showNotification(...)`.
- `notificationclick`: focus tab SplitBill đang mở (nếu có) và điều hướng tới đúng URL, hoặc mở tab
  mới — không bao giờ mở trùng nhiều tab cho cùng 1 lần bấm thông báo.

**Sinh cặp khóa VAPID cho dev/test**: không có endpoint API nào lộ ra để tự sinh khóa (rủi ro bảo mật
không cần thiết) — dùng `WebPush.VapidHelper.GenerateVapidKeys()` qua 1 script console throwaway (cùng
mẫu script Python sinh icon PWA ở mục 23.2 — không lưu lại trong repo), rồi cấu hình qua
`dotnet user-secrets set "WebPush:VapidPublicKey/VapidPrivateKey/VapidSubject"` như mọi bí mật khác
(`Jwt:SigningKey`, `GoogleAuth:InternalSecret`).

Đã verify sống đầy đủ, kể cả gọi THẬT tới push service của Google (FCM) qua network thật, không chỉ
`dotnet test` (vốn dùng `FakeWebPushSender`, không đi qua mạng): sinh 1 cặp khóa VAPID thật, cấu hình
qua user-secrets, khởi động Api+Web thật → `GET /users/me/push-vapid-public-key` trả đúng public key
vừa sinh → `POST /users/me/push-subscriptions` với 1 endpoint FCM giả (không phải subscription thật,
vì không có trình duyệt thật để lấy) → trigger sự kiện "Được thêm vào nhóm mới" (mục 13, thêm thành
viên bằng `UserId`) → log Api xác nhận `WebPushSender` ĐÃ THẬT SỰ gọi ra `fcm.googleapis.com` (không
throw lỗi chưa xử lý — không có dòng `ERR]` nào), FCM trả về lỗi đúng như dự đoán cho endpoint giả (404/
410-equivalent) → `NotificationService` tự self-heal xóa `PushSubscription` (xác nhận qua log
`DELETE FROM [PushSubscriptions]`) → in-app notification + email vẫn ghi nhận đầy đủ, không bị ảnh
hưởng bởi lỗi push (đúng thiết kế "kênh phụ không chặn kênh chính"); test lại `POST`/`DELETE` subscribe/
unsubscribe qua API trực tiếp → cả 2 đều `204`; trang `Notifications/Index` (qua cookie đăng nhập thật,
không chỉ gọi thẳng Api) render đúng `data-vapid-public-key` với public key thật, đủ 3 phần tử UI
(`sb-push-toggle-btn`/`sb-push-subscribe-form`/`sb-push-unsubscribe-form`); `service-worker.js` phục vụ
qua Web xác nhận có đủ 2 listener `push`/`notificationclick` mới.

> ✅ **Cập nhật 2026-09-10 — click-through một phần bằng trình duyệt Chromium thật** (kết nối Chrome
> extension có sẵn ở phiên này): đăng nhập thật → `/Notifications` → bấm "Bật thông báo đẩy" → xác
> nhận qua console (không có lỗi JS nào) và qua `Notification.permission` (đổi đúng từ chưa-gọi sang
> `"default"` — trạng thái "đang chờ người dùng quyết định", đúng như kỳ vọng ngay sau khi
> `requestPermission()` được gọi) rằng nút bấm đúng là đã gọi `Notification.requestPermission()` mà
> không ném lỗi. **Không đi hết được toàn bộ luồng**: hộp thoại xin quyền thông báo của Chrome là UI
> gốc của trình duyệt (không phải DOM của trang), nằm ngoài vùng mà công cụ chụp ảnh màn hình dựa trên
> CDP `Page.captureScreenshot` bao phủ, và các trang cấu hình nội bộ dùng để cấp quyền trước
> (`chrome://settings/content/notifications`) bị chặn tường minh bởi chính extension ("Can't interact
> with browser-internal... URLs") — đây là giới hạn có chủ đích của công cụ tự động hóa trình duyệt,
> không phải lỗi của app. Vì vậy bước `PushManager.subscribe()` thực sự chạy xong (sau khi người dùng
> BẤM "Cho phép" trên hộp thoại gốc) vẫn CHƯA verify được bằng trình duyệt thật — chỉ verify được nửa
> đầu (nút bấm đúng, gọi đúng API trình duyệt, không lỗi) chứ chưa phải toàn bộ luồng tới lúc nhận
> push thật. Cần một người dùng thật tự bấm "Cho phép" trên hộp thoại đó (hoặc chạy Chrome với cờ
> `--unsafely-treat-insecure-origin-as-secure`/policy pre-grant riêng ngoài phạm vi công cụ hiện có) để
> đóng nốt khoảng verify còn lại.

`dotnet build`: 0 warning, 0 error. `dotnet test`: **282/282 pass** (74 unit + 25 web + 179 integration
+ 4 E2E — 5 test integration mới: `SubscribeToPushAsync_ThenNotifyAsync_SendsPushWithAbsoluteUrl`,
`SubscribeToPushAsync_SameEndpointTwice_UpsertsInsteadOfDuplicating`,
`NotifyAsync_SubscriptionGone_SelfHeals_RemovesSubscriptionFromDb`,
`UnsubscribeFromPushAsync_RemovesSubscription_NoLongerReceivesPush`,
`UnsubscribeFromPushAsync_ByOtherUser_DoesNothing_Idempotent` (`NotificationServiceTests`)).

### 25.8 Tìm kiếm xuyên nhóm (hạng mục 8/9)

Không cần NuGet mới. Khác tính năng "Tìm kiếm/lọc khoản chi" đã có (mục 15.2, `GET /groups/{id}/
expenses?title=...`) — chỉ tìm trong ĐÚNG 1 nhóm đang xem — tính năng này quét TOÀN BỘ nhóm caller
đang tham gia cùng lúc, khớp cả **tên nhóm** lẫn **tiêu đề khoản chi**, phù hợp khi không nhớ khoản chi
đó nằm ở nhóm nào.

**Kiến trúc — không viết lại logic đọc dữ liệu nào mới**, thuần orchestrate 2 thứ đã có sẵn (cùng
nguyên tắc Export mục 25.4/Dashboard mục 25.5/Mẫu nhóm mục 25.6): `IGroupRepository.GetByUserIdAsync`
(đã lọc đúng nhóm caller đang active, cùng vòng lặp mẫu `UserDashboardService`/`BalanceService.
GetCounterpartyBalancesAsync`) để lấy danh sách nhóm + khớp tên; `IExpenseService.GetPagedAsync` với
`ExpenseFilter(Title: query)` (đã có sẵn từ mục 15.2 — "chứa, không phân biệt hoa thường") gọi **cho
từng nhóm** để đẩy việc lọc tiêu đề xuống tận DB, không tự tải hết khoản chi rồi lọc ở C#.

Giới hạn kết quả (tránh 1 lượt tìm kiếm trả về hàng trăm dòng nếu user có rất nhiều nhóm/khoản chi):
tối đa 10 khoản chi khớp mỗi nhóm, gộp lại rồi chỉ giữ 30 dòng mới nhất trên TOÀN BỘ kết quả — cùng
mẫu `RecentActivityPerGroupPageSize`/`RecentActivityLimit` của `UserDashboardService` (mục 25.5).
Query rỗng/chỉ có khoảng trắng trả về 2 danh sách rỗng NGAY, không quét DB nào cả — tránh 1 lượt "tìm
kiếm mọi thứ" vô nghĩa.

**API**: `GET /api/v1/users/me/search?q=...` (`UsersController`, cùng chỗ 3 API cá nhân xuyên-nhóm
khác — mục 15.4/20/25.5).

**Web**: ô tìm kiếm đặt thẳng trên navbar (`_Layout.cshtml`) — chỉ hiện khi đã đăng nhập, vì tìm kiếm
quét toàn bộ nhóm CỦA NGƯỜI ĐANG ĐĂNG NHẬP. Cố tình dùng `<form method="get">` (không phải `fetch()`),
submit trực tiếp tới trang mới `Pages/Search.cshtml` — khớp quy ước "không fetch()" nhất quán của
toàn dự án (đã nêu lại ở mục 25.7), và cho phép kết quả tìm kiếm có URL riêng
(`/Search?q=...`), bookmark/chia sẻ được, giống thiết kế filter khoản chi ở mục 15.2. Trang `Search`
đặt ở gốc `Pages/` (không phải dưới `Groups/`) — cùng lý do đã dùng cho `GroupTemplates` (mục 25.6):
không thuộc về 1 nhóm cụ thể nào.

Đã verify sống đầy đủ, không chỉ `dotnet test`: `curl` thẳng Api — tạo nhóm "Du lich Da Lat" + 1 khoản
chi "An pho bo Da Lat" (cả 2 đều đặt tên KHÔNG dấu, chỉ khác hoa/thường) → `GET /users/me/search?
q=da%20lat` trả đúng CẢ nhóm lẫn khoản chi với query viết HOA "DA LAT" → xác nhận đúng
`OrdinalIgnoreCase` hoạt động (không phân biệt hoa/thường). **Chưa verify khớp không dấu** (vd gõ "da
lat" tìm ra tên nhóm có dấu "Đà Lạt") — `OrdinalIgnoreCase` chỉ xử lý hoa/thường, KHÔNG tự bỏ dấu tiếng
Việt; đây là giới hạn thật của cài đặt hiện tại (không phải đã kiểm chứng rồi bỏ qua), ghi nhận để
không phóng đại phạm vi đã test. Tiếp tục: đăng nhập qua Web BFF thật (cookie + antiforgery) → xác
nhận navbar có ô tìm kiếm → `GET /Search?q=pho` (route thật, không gọi thẳng Api) render đúng cả tên
khoản chi, tên nhóm, và số tiền định dạng đúng "150.000"; test thêm 2 trạng thái biên qua cùng phiên
đăng nhập: query rỗng → đúng câu hướng dẫn "Nhập từ khóa..."; query không khớp gì → đúng "Không tìm
thấy kết quả nào cho...".

`dotnet build`: 0 warning, 0 error. `dotnet test`: **286/286 pass** (74 unit + 25 web + 183 integration
+ 4 E2E — 4 test integration mới: `SearchAsync_EmptyQuery_ReturnsEmptyResult_NoDbScan`,
`SearchAsync_MatchesGroupNameCaseInsensitive`, `SearchAsync_MatchesExpenseTitleAcrossMultipleGroups`,
`SearchAsync_OnlyIncludesGroupsCallerIsActiveMemberOf` (`GlobalSearchServiceTests`)).

### 25.9 Đăng nhập 2 lớp / TOTP (hạng mục 9/9 — HOÀN THÀNH TOÀN BỘ danh sách mục 25)

Hạng mục thứ 3 cần cân nhắc NuGet package — nhưng **KHÔNG cần thêm gì cả**, đã verify TRƯỚC KHI triển
khai: TOTP (RFC 6238) chỉ cần `System.Security.Cryptography.HMACSHA1` (BCL thuần), nên chạy 5 test
vector chính thức của RFC 6238 Phụ lục B (HMAC-SHA1, secret ASCII "12345678901234567890", X=30, T0=0)
qua 1 script console throwaway — **cả 5 đều khớp** (`94287082` ở Time=59, v.v.) — trước khi viết code
thật, không suy đoán rồi tin luôn. Base32 (mã hóa secret để hiển thị/QR) cũng chỉ là RFC 4648, tự viết
encoder/decoder ~30 dòng, verify round-trip qua cùng script. Mã hóa secret tại rest dùng
`System.Security.Cryptography.AesGcm` (cũng thuần BCL) — không dùng ASP.NET Core Data Protection vì
`SplitBill.Application` là `Sdk="Microsoft.NET.Sdk"` (thư viện thuần, không phải Web SDK) nên
`Microsoft.AspNetCore.DataProtection.Abstractions` sẽ là 1 NuGet package MỚI phải hỏi — tự viết AES-GCM
trực tiếp (cùng mẫu `InternalSecretComparer` mục 25.3: crypto thuần BCL ngay trong Application layer)
tránh được hoàn toàn nhu cầu đó.

**4 điểm thiết kế bắt buộc phải chốt trước khi viết code (rà soát chủ động trước khi bắt tay vào, không
phải phát hiện muộn qua bug):**

1. **Đường lùi khi mất điện thoại** — nếu chỉ cho tắt 2FA bằng mã TOTP, người mất điện thoại sẽ khóa
   tài khoản VĨNH VIỄN (đúng kịch bản support-ticket phổ biến nhất của mọi hệ thống 2FA thật). Đã quyết
   định: sinh 10 **mã dự phòng** lúc bật 2FA (`TwoFactorRecoveryCode`, chỉ lưu hash SHA-256 — cùng mẫu
   `RefreshToken`/`PasswordResetToken`), mỗi mã dùng được đúng 1 lần; `DisableAsync` VÀ luồng hoàn tất
   đăng nhập (`/auth/login/2fa`) chấp nhận CẢ mã TOTP lẫn mã dự phòng, còn `EnableAsync`/
   `RegenerateRecoveryCodesAsync` chỉ chấp nhận TOTP (phải còn quyền truy cập app xác thực).
2. **6 chữ số, KHÔNG PHẢI 8** — RFC 6238 Phụ lục B dùng 8 số chỉ để ví dụ trong spec không mơ hồ; MỌI
   app xác thực thật (Google/Microsoft Authenticator, Authy) mặc định 6 số. `TotpService` ép cứng
   `Digits = 6` (`% 1_000_000`, không phải `% 10^8`) và `otpauth://` URI ghi tường minh `digits=6` —
   sai chỗ này sẽ khiến MỌI thiết bị thật thất bại 100% mà không lộ ra qua bất kỳ unit test tự viết nào
   (test tự viết luôn tự nhất quán với chính nó).
3. **2FA phải áp dụng cho CẢ đăng nhập qua Google** (mục 25.3) — nếu `GoogleLoginAsync` bỏ qua cổng
   2FA, tính năng vô nghĩa với user đã liên kết cả 2 cách đăng nhập. Cả `LoginAsync` và `GoogleLoginAsync`
   đều đi qua đúng 1 điểm hội tụ `AuthService.CompleteLoginAsync` (private) — không có đường tắt nào
   bỏ qua kiểm tra `user.TwoFactorEnabled`. Test riêng `GoogleLoginAsync_TwoFactorEnabled_ReturnsChallenge_NotBypassed`
   khóa lại đúng bất biến này.
4. **Rate limit cho `/auth/login/2fa`** — endpoint này là bề mặt brute-force rõ ràng nhất (đoán 1 trong
   1 triệu tổ hợp 6 số). Đặt trong `AuthController` (đã có `[EnableRateLimiting("auth")]` ở mức class,
   xem mục 10b) nên tự động thừa hưởng đúng giới hạn 10 request/phút/IP — đã xác nhận bằng cách đọc lại
   code (`[EnableRateLimiting]` ở class áp dụng cho MỌI action, không cần lặp lại per-action), không suy
   đoán suông.

**Mô hình dữ liệu**: `User` thêm `TwoFactorEnabled`, `TwoFactorSecretEncrypted` (Base64 AES-GCM,
`null` nếu chưa từng thiết lập — CÓ THỂ khác null dù `TwoFactorEnabled=false`, nghĩa là "đang thiết lập
chưa xác nhận"), `TwoFactorLastUsedTimeStep` (chống replay — dùng lại đúng 1 mã trong cùng cửa sổ ±1
bước 30s). `TwoFactorRecoveryCode` (soft, không có `ExpiresAt` — chỉ hết hiệu lực khi `UsedAt` hoặc bị
thay bộ mới). `TwoFactorChallenge` — "vé tạm" sau khi qua được bước mật khẩu/Google nhưng CHƯA hoàn tất
đăng nhập, cùng mẫu thiết kế `PasswordResetToken` (chỉ lưu hash, thời hạn ngắn mặc định 5 phút, dùng 1
lần). Migration `AddTwoFactorAuth`.

> ⚠️ **Quyết định kỹ thuật — KHÔNG unique index trên `TwoFactorRecoveryCode.CodeHash`/`TwoFactorChallenge.
> TokenHash` theo kiểu filtered như `Email`/`GoogleId`** — 2 bảng này đã unique toàn phần (không có giá
> trị NULL để cần filter), cùng mẫu `PasswordResetToken.TokenHash`/`RefreshToken.TokenHash` có sẵn.

**Kiến trúc — điểm hội tụ duy nhất**: `AuthService.CompleteLoginAsync` (private) là nơi DUY NHẤT quyết
định "phát token thật ngay" hay "trả về challenge chờ mã 2FA" — cả `LoginAsync` (sau khi verify mật
khẩu) và `GoogleLoginAsync` (sau khi resolve/tạo User) đều gọi đúng hàm này, không tự ý quyết định
riêng. `ITwoFactorService` (setup/enable/disable/regenerate + `VerifyCodeOrRecoveryAsync` dùng chung
bởi cả `DisableAsync` lẫn `AuthService.CompleteTwoFactorLoginAsync`) tách biệt hoàn toàn khỏi
`AuthService` — đúng mẫu `NotificationService`/`ExpenseCommentService` đã áp dụng cho các concern có
thể tách rời từ service lớn hơn.

**API**:
```
POST /api/v1/auth/login/2fa                       Hoàn tất đăng nhập — { challengeToken, code } -> AuthTokens
GET  /api/v1/users/me/2fa/status                   { enabled }
POST /api/v1/users/me/2fa/setup                    Sinh secret mới (CHƯA bật) -> { secretBase32, otpAuthUri }
POST /api/v1/users/me/2fa/enable                   { code } -> 10 mã dự phòng (plaintext, CHỈ 1 lần)
POST /api/v1/users/me/2fa/disable                  { code } — chấp nhận TOTP HOẶC mã dự phòng
POST /api/v1/users/me/2fa/recovery-codes/regenerate { code } — CHỈ chấp nhận TOTP -> 10 mã dự phòng MỚI
```

**Web**: `Account/TwoFactor.cshtml` (trang cài đặt, link từ `Account/Profile`) — 3 ô nhập mã
(Enable/Disable/Regenerate) CỐ TÌNH là `string` thuần không `[Required]`, validate thủ công trong từng
handler — bài học trực tiếp từ mục 25.2 (`[BindProperty]` áp dụng validation cho MỌI POST của trang bất
kể handler nào chạy). QR vẽ bằng `qrcodejs` (CDN, cùng thư viện đã dùng cho VietQR mục 9), payload
`otpauth://` giữ nguyên qua TempData (`Peek`, không phải indexer — sống sót qua nhiều lượt gõ sai mã
mà không phải gọi lại `/2fa/setup`, vì gọi lại sẽ sinh secret MỚI làm vô hiệu QR vừa quét).

`Account/TwoFactorChallenge.cshtml` (bước 2 của đăng nhập) — challenge token chuyển từ `LoginModel`/
`GoogleCallbackModel` sang trang này qua **TempData, KHÔNG qua query string** (tránh lộ qua URL/lịch sử
trình duyệt/referrer header). `LoginModel.OnPostAsync`/`GoogleCallbackModel.OnGetAsync` đều kiểm tra
`LoginResult.RequiresTwoFactor` trước khi gọi `SignInHelper.SignInAsync` — chỉ đăng nhập cookie thật khi
đã có `AuthTokens` thật (`RequiresTwoFactor=false` hoặc đã qua bước 2 thành công).

Đã verify sống đầy đủ, kể cả tính TOÁN THẬT mã 6 số bằng cùng thuật toán RFC 6238 (không phải mock)
qua 1 script console throwaway thứ 2 (nhận `secret` làm arg, in ra mã hiện tại — dùng để lái toàn bộ
kịch bản curl bên dưới, không lưu lại trong repo):
1. `curl` thẳng Api (không qua Web): đăng ký → status `enabled:false` → `/2fa/setup` trả đúng
   `otpauth://...&digits=6&period=30` → `/2fa/enable` với mã thật → nhận đúng 10 mã dự phòng → status
   `enabled:true` → `/auth/login` giờ trả `requiresTwoFactor:true` (không có `tokens`) → mã sai bị
   `401 INVALID_TWO_FACTOR_CODE` → mã đúng hoàn tất đăng nhập, nhận `AuthTokens` thật → đăng nhập LẦN 2
   dùng 1 MÃ DỰ PHÒNG thay vì TOTP → thành công → dùng LẠI đúng mã dự phòng đó lần 2 → đúng `401` (đã
   tiêu thụ) → `/2fa/disable` bằng mã dự phòng còn lại → status về `enabled:false` → `/auth/login` giờ
   trả thẳng `AuthTokens`, không còn `requiresTwoFactor`.
2. Đi ĐÚNG qua Web BFF thật (cookie + antiforgery, không gọi thẳng Api — cùng phương pháp mục 25.6/
   25.7): đăng ký qua form thật → trang `/Account/TwoFactor` đúng "Đang tắt" → bấm "Bật xác thực 2 lớp"
   → reload đúng hiện QR + secret (đọc lại từ TempData) → xác nhận bằng mã thật → đúng "Đang bật" +
   10 mã dự phòng hiển thị → đăng xuất → đăng nhập lại bằng mật khẩu → redirect đúng
   `/Account/TwoFactorChallenge` (CHƯA đăng nhập, cookie chưa có claim) → nhập mã thật → redirect đúng
   `/Groups/Index`, navbar hiện đúng tên tài khoản — xác nhận toàn bộ vòng đời qua đúng luồng trình
   duyệt thật sẽ đi (không chỉ gọi API).

> ✅ **Cập nhật 2026-09-10 — đã verify sống bằng trình duyệt Chromium thật (kết nối Chrome extension có
> sẵn ở phiên này, khác lần đầu triển khai)**: chạy `SplitBill.Api` + `SplitBill.Web` thật (LocalDB,
> `ASPNETCORE_ENVIRONMENT=Development`), đăng ký 1 tài khoản mới qua form thật → vào `/Account/
> TwoFactor` → bấm "Bật xác thực 2 lớp" → **QR code render đúng trên trình duyệt thật** (trước đó chỉ
> verify qua curl/text, chưa từng nhìn thấy QR thật) → tính mã TOTP thật từ secret hiển thị (PowerShell
> `HMACSHA1`, cùng thuật toán RFC 6238 đã verify khớp test vector chính thức) → xác nhận & bật 2FA
> thành công, 10 mã dự phòng hiện đúng → đăng xuất (xác nhận qua log Api có `POST /auth/logout` —
> phát hiện lần đầu bấm "Đăng xuất" qua `read_page` ref KHÔNG thực sự submit form, phải dùng
> `computer` click theo tọa độ mới ăn; nhắc lại đúng bài học ở mục 15.6/18 về việc verify sống phải
> xác nhận qua log server, không chỉ tin UI) → đăng nhập lại bằng mật khẩu → redirect đúng
> `/Account/TwoFactorChallenge` → **xác nhận tường minh navbar KHÔNG hiện trạng thái đã đăng nhập ở
> bước này** (kiểm tra kỹ vì nghi ngờ ban đầu — do cookie cũ chưa logout — cứ tưởng là lỗi bypass 2FA,
> hóa ra chỉ là phương pháp test sai, không phải bug thật) → nhập mã TOTP thật → hoàn tất đăng nhập,
> redirect đúng `/Groups/Index`, navbar hiện đúng tên tài khoản → xác nhận qua log Api có đúng
> `POST /auth/login/2fa` trả `200`. Khoảng "Chưa verify được" ở bản gốc (chỉ tính mã TOTP gián tiếp,
> chưa từng thấy QR/luồng UI thật) nay đã đóng.
>
> **Vẫn chưa verify được** (giới hạn thành thật còn lại, không phóng đại): quét QR bằng 1 app xác thực
> di động THẬT (Google Authenticator...) — không có thiết bị di động trong môi trường này. Đã verify
> gián tiếp đầy đủ bằng cách tính đúng mã TOTP từ CHÍNH secret trong QR đó bằng thuật toán RFC 6238 đã
> verify khớp test vector chính thức, và xác nhận Api chấp nhận đúng mã đó — nhưng bản thân việc app
> điện thoại đọc đúng nội dung ảnh QR (không phải lỗi encode ảnh) chưa được xác nhận bằng mắt người.

> ⚠️ **Rủi ro vận hành phát hiện qua rà soát chủ động (advisor, trước khi commit — không phải bug đã
> xảy ra thật), đã vá 1 phần:** `TwoFactor:EncryptionKey` (khóa AES-GCM mã hóa `TwoFactorSecretEncrypted`
> — mục "Mô hình dữ liệu" ở trên) nếu bị đổi SAU KHI user đã bật 2FA sẽ làm `AesGcm.Decrypt` ném
> `CryptographicException` (tag mismatch) — khác `Jwt:SigningKey` (JWT ngắn hạn, đổi key chỉ buộc đăng
> nhập lại, dự án đã chấp nhận đánh đổi này từ đầu), `TwoFactorSecretEncrypted` sống lâu dài, không có
> "tự làm mới" nào tương đương.
> - **`VerifyCodeOrRecoveryAsync`** (dùng chung bởi `DisableAsync` VÀ luồng hoàn tất đăng nhập
>   `/auth/login/2fa`) đã được vá: giải mã lỗi ở nhánh TOTP giờ bị bắt (`catch (CryptographicException)`)
>   và rơi êm xuống thử **mã dự phòng** thay vì để lỗi lọt ra ngoài chặn đứng luôn cả đường thoát hiểm đó
>   — nếu không vá, đúng cái an toàn dựng riêng cho tình huống "mất khả năng dùng TOTP" (điểm thiết kế
>   #1 ở trên) sẽ bị chính lỗi hạ tầng này vô hiệu hóa theo, một mâu thuẫn thiết kế nghiêm trọng hơn cả
>   lỗi gốc. Test: `VerifyCodeOrRecoveryAsync_SecretDecryptionFails_FallsBackToRecoveryCode`. Vì nhánh
>   này rơi êm về `401 INVALID_TWO_FACTOR_CODE` thông thường (không tự báo lỗi hạ tầng cho client như
>   `EnableAsync`), đã thêm `_logger.LogWarning(...)` (`ILogger<TwoFactorService>`, tiêm qua DI như mọi
>   service khác — không cần sửa `Program.cs` vì `AddScoped<ITwoFactorService, TwoFactorService>()` tự
>   resolve) — nếu không log, operator không có cách nào phân biệt "user gõ sai mã" với
>   "`TwoFactor:EncryptionKey` đã bị đổi" khi nhận báo cáo hàng loạt user không đăng nhập được.
> - **`EnableAsync`/`RegenerateRecoveryCodesAsync`** (không có mã dự phòng nào thay thế được — mã dự
>   phòng chỉ được cấp SAU khi `EnableAsync` thành công) ném thẳng `DomainException` với errorCode mới
>   `TWO_FACTOR_DECRYPTION_FAILED` (map `500`, `ExceptionHandlingMiddleware`) thay vì để
>   `CryptographicException` lọt ra ngoài thành `500 INTERNAL_SERVER_ERROR` chung chung không rõ nguyên
>   nhân. Test: `EnableAsync_SecretDecryptionFails_ThrowsTwoFactorDecryptionFailed`.
> - **Vẫn CHƯA có đường tự phục hồi nào** cho đúng người dùng bị lỗi này (đã bật 2FA, secret không giải
>   mã được, VÀ không còn mã dự phòng nào — ví dụ đã dùng hết) — trường hợp đó vẫn cần operator can thiệp
>   DB thủ công (`UPDATE Users SET TwoFactorEnabled=0, TwoFactorSecretEncrypted=NULL WHERE Id=...`). Đây
>   là giới hạn có chủ đích của MVP (đổi `EncryptionKey` sau khi đã có user bật 2FA là thao tác vận hành
>   hiếm gặp, không đáng để xây hẳn 1 luồng migrate-secret phức tạp), ghi lại rõ ràng thay vì để ẩn.

`dotnet build`: 0 warning, 0 error. `dotnet test`: **301/301 pass** (74 unit + 26 web + 197 integration
+ 4 E2E — 14 test integration mới: 9 trong `TwoFactorServiceTests` (7 gốc + 2 test hồi quy giải mã lỗi
ở trên), 5 trong `AuthServiceTests`
(`LoginAsync_TwoFactorEnabled_ReturnsChallengeInsteadOfTokens`,
`CompleteTwoFactorLoginAsync_ValidChallengeAndCode_IssuesRealTokens`,
`CompleteTwoFactorLoginAsync_WrongCode_ThrowsInvalidTwoFactorCode`,
`CompleteTwoFactorLoginAsync_ChallengeAlreadyUsed_ThrowsInvalidTwoFactorChallenge`,
`GoogleLoginAsync_TwoFactorEnabled_ReturnsChallenge_NotBypassed`); 1 test web mới
(`OnPostAsync_RequiresTwoFactor_RedirectsToChallengePage_DoesNotSignIn` trong `LoginModelTests`, cộng
2 test cũ sửa lại theo shape `LoginResult` mới).

---

**Toàn bộ 9 hạng mục trong danh sách bổ sung ngoài kế hoạch (mục 25) đã hoàn thành.**
