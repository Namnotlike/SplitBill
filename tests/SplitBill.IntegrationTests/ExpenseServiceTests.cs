using FluentAssertions;
using SplitBill.Application.Common;
using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using SplitBill.Domain.Exceptions;
using Xunit;

namespace SplitBill.IntegrationTests;

public sealed class ExpenseServiceTests
{
    private static async Task<(TestHarness Harness, Guid OwnerId, GroupDto Group, Guid OwnerMemberId, Guid GuestMemberId)> SetupGroupAsync()
    {
        var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var guest = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(null, "Binh"), CancellationToken.None);
        return (harness, ownerId, group, group.Members[0].Id, guest.Id);
    }

    [Fact]
    public async Task CreateAsync_EqualSplit_SumsMatch_NoWarnings()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();

        var result = await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "An toi", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 100_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId])), CancellationToken.None);

        result.Data.Splits.Sum(s => s.Amount).Should().Be(100_000);
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_PayerSplitMismatch_ProducesSplitTotalMismatchWarning()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();

        var result = await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "An toi", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 80_000)], // lệch so với Total 100k
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId])), CancellationToken.None);

        result.Warnings.Should().ContainSingle(w => w.Code == WarningCodes.SplitTotalMismatch);
    }

    [Fact]
    public async Task CreateAsync_MemberNotInGroup_ThrowsMemberNotInGroup()
    {
        var (harness, ownerId, group, ownerMemberId, _) = await SetupGroupAsync();
        var strangerMemberId = Guid.NewGuid();

        var act = () => harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "An toi", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 100_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, strangerMemberId])), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.MemberNotInGroup);
    }

    // ⚠️ Bảo mật/nghiệp vụ (security-review 2026-09-07): ValidateMembersBelongToGroup chỉ kiểm tra
    // "còn tồn tại trong nhóm", không lọc IsActive — một thành viên đã rời nhóm vẫn có thể bị gán làm
    // payer/split MỚI nếu ai đó biết GroupMemberId cũ của họ, tái tạo đúng lỗ hổng "nợ ma" đã sửa cho
    // Recurring Expense Runner (mục 15.7) nhưng qua đường tạo/sửa Expense thủ công. 4 test dưới đây
    // khóa lại hành vi đã sửa: chặn tham chiếu MỚI tới thành viên không active, nhưng KHÔNG chặn giữ
    // nguyên tham chiếu CŨ khi sửa khoản chi lịch sử (nếu không, mọi khoản chi có người tham gia đã
    // rời nhóm sau đó sẽ vĩnh viễn không sửa được nữa dù chỉ đổi Title).
    [Fact]
    public async Task CreateAsync_MemberLeftGroup_ThrowsMemberNotActive()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        await harness.GroupService.RemoveMemberAsync(ownerId, group.Id, guestMemberId, CancellationToken.None); // net = 0, rời được

        var act = () => harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "An toi", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 100_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId])), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.MemberNotActive);
    }

    [Fact]
    public async Task UpdateAsync_AddsNewReferenceToMemberWhoLeftGroup_ThrowsMemberNotActive()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var created = await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "An toi", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 100_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId])), CancellationToken.None); // chỉ owner, chưa dính guest
        await harness.GroupService.RemoveMemberAsync(ownerId, group.Id, guestMemberId, CancellationToken.None); // net = 0, rời được

        // Sửa lại để THÊM guest (đã rời nhóm) vào splits — tham chiếu MỚI, phải bị chặn.
        var act = () => harness.ExpenseService.UpdateAsync(ownerId, created.Data.Id, new UpdateExpenseRequest(
            "An toi 2", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 100_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId]),
            RowVersion: created.Data.RowVersion), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.MemberNotActive);
    }

    [Fact]
    public async Task UpdateAsync_KeepsExistingReferenceToMemberWhoLeftGroup_Succeeds()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        // 2 khoản chi đối xứng để net của guest = 0 dù vẫn có mặt trong lịch sử: A guest nợ owner
        // 50k, B owner nợ guest 50k -> guest net = -50000 + 50000 = 0, đủ điều kiện rời nhóm.
        var expenseA = await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "Khoan A", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 100_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId])), CancellationToken.None);
        await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "Khoan B", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(guestMemberId, 100_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId])), CancellationToken.None);
        await harness.GroupService.RemoveMemberAsync(ownerId, group.Id, guestMemberId, CancellationToken.None); // net = 0, rời được

        // Sửa Khoản A: chỉ đổi Title, GIỮ NGUYÊN payers/splits gốc (đã có guest từ trước) -> phải cho
        // qua, không được vì guest hiện đã rời nhóm mà chặn luôn cả việc sửa khoản chi lịch sử.
        var updated = await harness.ExpenseService.UpdateAsync(ownerId, expenseA.Data.Id, new UpdateExpenseRequest(
            "Khoan A (da sua)", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 100_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId]),
            RowVersion: expenseA.Data.RowVersion), CancellationToken.None);

        updated.Data.Title.Should().Be("Khoan A (da sua)");
    }

    [Fact]
    public async Task CreateAsync_DuplicatePayer_ThrowsDuplicateMemberId()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();

        var act = () => harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "An toi", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 50_000), new ExpensePayerInput(ownerMemberId, 50_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId])), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.DuplicateMemberId);
    }

    [Fact]
    public async Task UpdateAsync_StaleRowVersion_ThrowsConcurrencyConflict()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var created = await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "An toi", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 100_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId])), CancellationToken.None);

        var act = () => harness.ExpenseService.UpdateAsync(ownerId, created.Data.Id, new UpdateExpenseRequest(
            "An toi 2", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 100_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId]),
            RowVersion: Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8])), CancellationToken.None); // sai RowVersion

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_CorrectRowVersion_Succeeds()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var created = await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "An toi", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 100_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId])), CancellationToken.None);

        var updated = await harness.ExpenseService.UpdateAsync(ownerId, created.Data.Id, new UpdateExpenseRequest(
            "An toi 2", 200_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 200_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId]),
            RowVersion: created.Data.RowVersion), CancellationToken.None);

        updated.Data.Title.Should().Be("An toi 2");
        updated.Data.TotalAmount.Should().Be(200_000);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletes_ThenGetByIdThrowsExpenseNotFound()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var created = await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "An toi", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 100_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId])), CancellationToken.None);

        await harness.ExpenseService.DeleteAsync(ownerId, created.Data.Id, CancellationToken.None);

        var act = () => harness.ExpenseService.GetByIdAsync(ownerId, created.Data.Id, CancellationToken.None);
        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.ExpenseNotFound);
    }

    [Fact]
    public void PreviewSplit_IsPureAndDeterministicForShares()
    {
        using var harness = TestHarness.Create();
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());

        var result = harness.ExpenseService.PreviewSplit(new PreviewSplitRequest(
            100_000, 0, "Shares",
            new SplitConfigInput(Shares: [new SharesInput(a, 1), new SharesInput(b, 1)])));

        result.Splits.Sum(s => s.Amount).Should().Be(100_000);
    }

    [Fact]
    public async Task GetPagedAsync_ReturnsCorrectTotalCount()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        for (var i = 0; i < 3; i++)
        {
            await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
                $"Expense {i}", 10_000, 0, DateTimeOffset.UtcNow.AddMinutes(-i),
                [new ExpensePayerInput(ownerMemberId, 10_000)],
                "Equal",
                new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId])), CancellationToken.None);
        }

        var page = await harness.ExpenseService.GetPagedAsync(ownerId, group.Id, 1, 2, ExpenseFilter.Empty, CancellationToken.None);

        page.TotalCount.Should().Be(3);
        page.Items.Should().HaveCount(2);
    }

    // ===== Tìm kiếm/lọc khoản chi (CLAUDE.md mục 15.2) — bổ sung 2026-09-05 =====

    [Fact]
    public async Task GetPagedAsync_FilterByTitle_IsCaseInsensitiveContains()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        await CreateSimpleExpenseAsync(harness, ownerId, group.Id, ownerMemberId, guestMemberId, "Ăn tối nhà hàng");
        await CreateSimpleExpenseAsync(harness, ownerId, group.Id, ownerMemberId, guestMemberId, "Xăng xe");

        var page = await harness.ExpenseService.GetPagedAsync(
            ownerId, group.Id, 1, 20, new ExpenseFilter(Title: "NHÀ HÀNG"), CancellationToken.None);

        page.Items.Should().ContainSingle(e => e.Title == "Ăn tối nhà hàng");
    }

    [Fact]
    public async Task GetPagedAsync_FilterByPayer_OnlyReturnsExpensesPaidByThatMember()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "Owner tra", 10_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 10_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId])), CancellationToken.None);
        await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "Guest tra", 10_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(guestMemberId, 10_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId])), CancellationToken.None);

        var page = await harness.ExpenseService.GetPagedAsync(
            ownerId, group.Id, 1, 20, new ExpenseFilter(PayerMemberId: guestMemberId), CancellationToken.None);

        page.Items.Should().ContainSingle(e => e.Title == "Guest tra");
    }

    [Fact]
    public async Task GetPagedAsync_FilterByAmountRange_ExcludesOutOfRange()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        await CreateSimpleExpenseAsync(harness, ownerId, group.Id, ownerMemberId, guestMemberId, "Nho", 5_000);
        await CreateSimpleExpenseAsync(harness, ownerId, group.Id, ownerMemberId, guestMemberId, "Vua", 50_000);
        await CreateSimpleExpenseAsync(harness, ownerId, group.Id, ownerMemberId, guestMemberId, "Lon", 500_000);

        var page = await harness.ExpenseService.GetPagedAsync(
            ownerId, group.Id, 1, 20, new ExpenseFilter(MinAmount: 10_000, MaxAmount: 100_000), CancellationToken.None);

        page.Items.Should().ContainSingle(e => e.Title == "Vua");
    }

    [Fact]
    public async Task GetPagedAsync_FilterByDateRange_ExcludesOutOfRange()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var now = DateTimeOffset.UtcNow;
        await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "Cu", 10_000, 0, now.AddDays(-10),
            [new ExpensePayerInput(ownerMemberId, 10_000)], "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId])), CancellationToken.None);
        await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "Gan day", 10_000, 0, now,
            [new ExpensePayerInput(ownerMemberId, 10_000)], "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId])), CancellationToken.None);

        var page = await harness.ExpenseService.GetPagedAsync(
            ownerId, group.Id, 1, 20, new ExpenseFilter(FromDate: now.AddDays(-1)), CancellationToken.None);

        page.Items.Should().ContainSingle(e => e.Title == "Gan day");
    }

    // ===== Nhãn/danh mục khoản chi (CLAUDE.md mục 15.3) — bổ sung 2026-09-05 =====

    [Fact]
    public async Task CreateAsync_WithCategory_IsSavedAndReturned()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();

        var result = await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "Bun cha", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 100_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId]),
            Category: "Food"), CancellationToken.None);

        result.Data.Category.Should().Be("Food");
    }

    [Fact]
    public async Task CreateAsync_NoCategory_DefaultsToOther()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();

        var result = await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "Khong ro", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 100_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId])), CancellationToken.None);

        result.Data.Category.Should().Be("Other");
    }

    [Fact]
    public async Task CreateAsync_InvalidCategory_ThrowsValidationFailed()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();

        var act = () => harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "Sai danh muc", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 100_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId]),
            Category: "KhongTonTai"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task GetPagedAsync_FilterByCategory_OnlyReturnsMatchingCategory()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "An sang", 20_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 20_000)], "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId]), Category: "Food"), CancellationToken.None);
        await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "Taxi", 20_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 20_000)], "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId]), Category: "Transport"), CancellationToken.None);

        var page = await harness.ExpenseService.GetPagedAsync(
            ownerId, group.Id, 1, 20, new ExpenseFilter(Category: "Food"), CancellationToken.None);

        page.Items.Should().ContainSingle(e => e.Title == "An sang");
    }

    [Fact]
    public async Task UpdateAsync_ChangesCategory()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var created = await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "An toi", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 100_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId]),
            Category: "Food"), CancellationToken.None);

        var updated = await harness.ExpenseService.UpdateAsync(ownerId, created.Data.Id, new UpdateExpenseRequest(
            "An toi", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 100_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId]),
            created.Data.RowVersion,
            Category: "Entertainment"), CancellationToken.None);

        updated.Data.Category.Should().Be("Entertainment");
    }

    private static Task CreateSimpleExpenseAsync(
        TestHarness harness, Guid ownerId, Guid groupId, Guid ownerMemberId, Guid guestMemberId, string title, long amount = 10_000) =>
        harness.ExpenseService.CreateAsync(ownerId, groupId, new CreateExpenseRequest(
            title, amount, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, amount)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId])), CancellationToken.None);

    // ===== SplitConfigJson — bổ sung 2026-09-05. Trước đó DB đã lưu field này (ExpenseService dòng
    // 81/129) nhưng ExpenseDto chưa từng trả nó ra qua API, khiến form Edit trên Web phải suy ngược
    // trọng số/%/danh sách món ăn từ ExpenseSplit.Amount cuối cùng (không chính xác, và với Itemized
    // thì hoàn toàn không suy ngược được). Test ở đây xác nhận API giờ trả đúng input gốc, round-trip
    // được qua System.Text.Json (đúng type SplitConfigInput như phía Web sẽ deserialize). =====

    [Fact]
    public async Task CreateAsync_Shares_ReturnsSplitConfigJson_RoundTripsOriginalWeights()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();

        var result = await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "An toi", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 100_000)],
            "Shares",
            new SplitConfigInput(Shares: [new SharesInput(ownerMemberId, 1), new SharesInput(guestMemberId, 3)])), CancellationToken.None);

        result.Data.SplitConfigJson.Should().NotBeNullOrEmpty();

        var parsed = System.Text.Json.JsonSerializer.Deserialize<SplitConfigInput>(result.Data.SplitConfigJson!);
        parsed!.Shares.Should().BeEquivalentTo(new[] { new SharesInput(ownerMemberId, 1), new SharesInput(guestMemberId, 3) });
    }

    [Fact]
    public async Task CreateAsync_Itemized_ReturnsSplitConfigJson_RoundTripsItemsAndConsumers()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();

        var result = await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "Lau + Nuoc ngot", 150_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 150_000)],
            "Itemized",
            new SplitConfigInput(Items:
            [
                new ItemizedInput("Lau", 100_000, [ownerMemberId, guestMemberId]),
                new ItemizedInput("Nuoc ngot", 50_000, [guestMemberId]),
            ])), CancellationToken.None);

        var parsed = System.Text.Json.JsonSerializer.Deserialize<SplitConfigInput>(result.Data.SplitConfigJson!);

        parsed!.Items.Should().HaveCount(2);
        parsed.Items.Should().ContainSingle(i => i.Name == "Lau" && i.Price == 100_000 && i.ConsumerMemberIds.Count == 2);
        parsed.Items.Should().ContainSingle(i => i.Name == "Nuoc ngot" && i.Price == 50_000 && i.ConsumerMemberIds.Single() == guestMemberId);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesSplitConfigJson_WithNewConfig()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var created = await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "An toi", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 100_000)],
            "Shares",
            new SplitConfigInput(Shares: [new SharesInput(ownerMemberId, 1), new SharesInput(guestMemberId, 1)])), CancellationToken.None);

        var updated = await harness.ExpenseService.UpdateAsync(ownerId, created.Data.Id, new UpdateExpenseRequest(
            "An toi", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 100_000)],
            "Percentage",
            new SplitConfigInput(Percentages: [new PercentageInput(ownerMemberId, 30), new PercentageInput(guestMemberId, 70)]),
            RowVersion: created.Data.RowVersion), CancellationToken.None);

        var parsed = System.Text.Json.JsonSerializer.Deserialize<SplitConfigInput>(updated.Data.SplitConfigJson!);
        parsed!.Shares.Should().BeNull();
        parsed.Percentages.Should().BeEquivalentTo(new[] { new PercentageInput(ownerMemberId, 30), new PercentageInput(guestMemberId, 70) });
    }

    // ===== Thông báo "khoản chi mới" (CLAUDE.md mục 13) — bổ sung 2026-09-05 =====

    [Fact]
    public async Task CreateAsync_NotifiesOtherMembersWithAccount_ExcludesCreatorAndGuestsWithoutAccount()
    {
        var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var otherUserId = await harness.RegisterUserAsync("c@example.com", "Chi");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var otherMember = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(otherUserId, "Chi"), CancellationToken.None);
        var guestMember = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(null, "Khach vang lai"), CancellationToken.None);
        var ownerMemberId = group.Members[0].Id;
        // Chi đã có sẵn 1 thông báo "MemberAdded" từ AddMemberAsync ở trên — đánh dấu đã đọc để phép
        // đếm dưới đây chỉ phản ánh đúng thông báo "ExpenseCreated" sắp tạo.
        await harness.NotificationService.MarkAllAsReadAsync(otherUserId, CancellationToken.None);
        harness.EmailSender.SentEmails.Clear(); // bỏ qua email "MemberAdded" đã gửi lúc AddMember ở trên

        await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "An toi", 90_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 90_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, otherMember.Id, guestMember.Id])), CancellationToken.None);

        // Chi (có tài khoản) nhận thông báo; Nam (người tạo) và khách vãng lai thì KHÔNG.
        var chiPage = await harness.NotificationService.GetPagedAsync(otherUserId, 1, 20, CancellationToken.None);
        chiPage.Items.Should().ContainSingle(n => n.Type == "ExpenseCreated" && !n.IsRead);
        (await harness.NotificationService.GetUnreadCountAsync(ownerId, CancellationToken.None)).Should().Be(0);
        harness.EmailSender.SentEmails.Should().ContainSingle(e => e.ToEmail == "c@example.com");
    }
}
