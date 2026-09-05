using FluentAssertions;
using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using SplitBill.Application.Settlements;
using SplitBill.Application.Users;
using SplitBill.Domain.Exceptions;
using Xunit;

namespace SplitBill.IntegrationTests;

public sealed class BalanceServiceTests
{
    private static async Task<(TestHarness Harness, Guid OwnerId, GroupDto Group, Guid OwnerMemberId, Guid GuestMemberId)> SetupGroupWithExpenseAsync()
    {
        var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var guest = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(null, "Binh"), CancellationToken.None);
        var ownerMemberId = group.Members[0].Id;

        // B1 (CLAUDE.md mục 7.2): A ứng 300k, chia đều A/B/C -> ở đây chỉ 2 người nên A:+150k, B:-150k
        await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "An toi", 300_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 300_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guest.Id])), CancellationToken.None);

        return (harness, ownerId, group, ownerMemberId, guest.Id);
    }

    [Fact]
    public async Task GetBalancesAsync_ComputesCorrectNet()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupWithExpenseAsync();

        var balances = await harness.BalanceService.GetBalancesAsync(ownerId, group.Id, CancellationToken.None);

        balances.Single(b => b.MemberId == ownerMemberId).Net.Should().Be(150_000);
        balances.Single(b => b.MemberId == guestMemberId).Net.Should().Be(-150_000);
        balances.Sum(b => b.Net).Should().Be(0);
    }

    [Fact]
    public async Task GetSettlementPlanAsync_Simplified_ReturnsSingleTransaction()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupWithExpenseAsync();

        var plan = await harness.BalanceService.GetSettlementPlanAsync(ownerId, group.Id, CancellationToken.None);

        plan.Simplified.Should().BeTrue();
        plan.Transactions.Should().ContainSingle();
        plan.Transactions[0].FromMemberId.Should().Be(guestMemberId);
        plan.Transactions[0].ToMemberId.Should().Be(ownerMemberId);
        plan.Transactions[0].Amount.Should().Be(150_000);
    }

    [Fact]
    public async Task GetSettlementPlanAsync_NoBankInfo_VietQrIsNull()
    {
        var (harness, ownerId, group, _, _) = await SetupGroupWithExpenseAsync();

        var plan = await harness.BalanceService.GetSettlementPlanAsync(ownerId, group.Id, CancellationToken.None);

        plan.Transactions[0].VietQr.Should().BeNull();
    }

    [Fact]
    public async Task GetSettlementPlanAsync_WithBankInfo_VietQrIsPresent()
    {
        var (harness, ownerId, group, _, _) = await SetupGroupWithExpenseAsync();
        await harness.UserService.UpdateProfileAsync(ownerId, new UpdateProfileRequest("Nam", "0011001234567", "970436"), CancellationToken.None);

        var plan = await harness.BalanceService.GetSettlementPlanAsync(ownerId, group.Id, CancellationToken.None);

        plan.Transactions[0].VietQr.Should().NotBeNull();
        plan.Transactions[0].VietQr!.Payload.Should().StartWith("000201");
    }

    // ===== Đa tiền tệ (CLAUDE.md mục 14) — bổ sung 2026-09-05 =====

    [Fact]
    public async Task GetSettlementPlanAsync_NonVndGroup_VietQrIsNull_EvenWithBankInfo()
    {
        var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich My", null, "OneTime", "USD"), CancellationToken.None);
        var guest = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(null, "Binh"), CancellationToken.None);
        var ownerMemberId = group.Members[0].Id;
        await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "Dinner", 300, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 300)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guest.Id])), CancellationToken.None);
        // Owner CÓ khai báo tài khoản ngân hàng — nhưng nhóm là USD nên VietQR vẫn phải null
        // (VietQR là chuẩn ngân hàng Việt Nam, không áp dụng cho USD/EUR — CLAUDE.md mục 9, 14).
        await harness.UserService.UpdateProfileAsync(ownerId, new UpdateProfileRequest("Nam", "0011001234567", "970436"), CancellationToken.None);

        var plan = await harness.BalanceService.GetSettlementPlanAsync(ownerId, group.Id, CancellationToken.None);

        plan.Transactions.Should().ContainSingle();
        plan.Transactions[0].VietQr.Should().BeNull();
    }

    [Fact]
    public async Task GetSettlementPlanAsync_NonSimplified_ReturnsDirectTransaction()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupWithExpenseAsync();
        await harness.GroupService.UpdateAsync(ownerId, group.Id, new UpdateGroupRequest(null, null, false, null), CancellationToken.None);

        var plan = await harness.BalanceService.GetSettlementPlanAsync(ownerId, group.Id, CancellationToken.None);

        plan.Simplified.Should().BeFalse();
        plan.Transactions.Should().ContainSingle(t => t.FromMemberId == guestMemberId && t.ToMemberId == ownerMemberId && t.Amount == 150_000);
    }

    [Fact]
    public async Task GetBalancesAsync_NonMember_ThrowsMemberNotInGroup()
    {
        var (harness, _, group, _, _) = await SetupGroupWithExpenseAsync();
        var strangerId = await harness.RegisterUserAsync("c@example.com", "La");

        var act = () => harness.BalanceService.GetBalancesAsync(strangerId, group.Id, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.MemberNotInGroup);
    }

    // ===== Ràng buộc mềm (CLAUDE.md mục 6.4) — bổ sung 2026-09-05. Test end-to-end qua toàn bộ
    // pipeline thật (DB InMemory, không phải gọi thẳng SocialSettlementPlanner như ở UnitTests) để
    // xác nhận GetPastSettlementPairsAsync đọc đúng lịch sử Settlement từ DB và truyền vào planner. =====

    [Fact]
    public async Task GetSettlementPlanAsync_WithTieAndPastSettlement_RoutesThroughPastPair()
    {
        var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var nam = group.Members[0];
        var binh = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(null, "Binh"), CancellationToken.None);
        var chi = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(null, "Chi"), CancellationToken.None);
        var dung = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(null, "Dung"), CancellationToken.None);

        // Nam trả 100k, Binh gánh toàn bộ -> Nam:+100k, Binh:-100k.
        await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "Khoan 1", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(nam.Id, 100_000)],
            "ExactAmount",
            new SplitConfigInput(ExactAmounts: [new ExactAmountInput(binh.Id, 100_000)])), CancellationToken.None);

        // Chi trả 100k, Dung gánh toàn bộ -> Chi:+100k, Dung:-100k.
        await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "Khoan 2", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(chi.Id, 100_000)],
            "ExactAmount",
            new SplitConfigInput(ExactAmounts: [new ExactAmountInput(dung.Id, 100_000)])), CancellationToken.None);

        // Balances: Nam:+100k, Chi:+100k (hòa), Binh:-100k, Dung:-100k (hòa) -> 2 cách ghép đều tối
        // thiểu 2 giao dịch. Ghi nhận Binh từng chuyển tiền cho Chi trước đó -> planner phải ưu tiên
        // ghép Binh->Chi thay vì Binh->Nam.
        await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(binh.Id, chi.Id, 5_000, "Tra truoc do"), CancellationToken.None);

        var plan = await harness.BalanceService.GetSettlementPlanAsync(ownerId, group.Id, CancellationToken.None);

        plan.TransactionCount.Should().Be(2);
        plan.Transactions.Should().Contain(t => t.FromMemberId == binh.Id && t.ToMemberId == chi.Id && t.Amount == 100_000);
        plan.Transactions.Should().Contain(t => t.FromMemberId == dung.Id && t.ToMemberId == nam.Id && t.Amount == 100_000);
    }
}
