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

        var page = await harness.ExpenseService.GetPagedAsync(ownerId, group.Id, 1, 2, CancellationToken.None);

        page.TotalCount.Should().Be(3);
        page.Items.Should().HaveCount(2);
    }

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
}
