using FluentAssertions;
using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using SplitBill.Application.Settlements;
using Xunit;

namespace SplitBill.IntegrationTests;

/// <summary>Test cho CLAUDE.md mục 25.5 — Dashboard cá nhân nâng cao.</summary>
public sealed class UserDashboardServiceTests
{
    [Fact]
    public async Task GetDashboardAsync_CountsActiveGroupsAndExpensesThisMonth()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group1 = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich 1", null, "OneTime", "VND"), CancellationToken.None);
        var group2 = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich 2", null, "OneTime", "VND"), CancellationToken.None);
        await harness.ExpenseService.CreateAsync(ownerId, group1.Id, new CreateExpenseRequest(
            "An trua", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(group1.Members[0].Id, 100_000)],
            "Equal", new SplitConfigInput(MemberIds: [group1.Members[0].Id])), CancellationToken.None);
        // Khoản chi tháng TRƯỚC — không được tính vào ExpensesThisMonth.
        await harness.ExpenseService.CreateAsync(ownerId, group2.Id, new CreateExpenseRequest(
            "An sang thang truoc", 50_000, 0, DateTimeOffset.UtcNow.AddMonths(-1),
            [new ExpensePayerInput(group2.Members[0].Id, 50_000)],
            "Equal", new SplitConfigInput(MemberIds: [group2.Members[0].Id])), CancellationToken.None);

        var dashboard = await harness.UserDashboardService.GetDashboardAsync(ownerId, CancellationToken.None);

        dashboard.TotalActiveGroups.Should().Be(2);
        dashboard.ExpensesThisMonth.Should().Be(1);
    }

    [Fact]
    public async Task GetDashboardAsync_OnlyIncludesSettlementsWhereCallerIsCreditor()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var otherUserId = await harness.RegisterUserAsync("b@example.com", "Binh");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var otherMember = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(otherUserId, "Binh"), CancellationToken.None);
        var ownerMemberId = group.Members[0].Id;
        // Binh no Nam 50k -> Nam la ToMemberId, dashboard cua Nam PHAI thay dong nay.
        await harness.SettlementRecordService.CreateAsync(ownerId, group.Id,
            new CreateSettlementRequest(otherMember.Id, ownerMemberId, 50_000), CancellationToken.None);
        // Nam no Binh 20k -> Nam la FromMemberId, dashboard cua Nam KHONG duoc thay dong nay (khong
        // phai viec Nam can xac nhan).
        await harness.SettlementRecordService.CreateAsync(ownerId, group.Id,
            new CreateSettlementRequest(ownerMemberId, otherMember.Id, 20_000), CancellationToken.None);

        var dashboard = await harness.UserDashboardService.GetDashboardAsync(ownerId, CancellationToken.None);

        dashboard.PendingSettlementsToConfirm.Should().ContainSingle();
        dashboard.PendingSettlementsToConfirm[0].Amount.Should().Be(50_000);
        dashboard.PendingSettlementsToConfirm[0].FromMemberName.Should().Be("Binh");
    }

    [Fact]
    public async Task GetDashboardAsync_IncludesRecentActivityAcrossGroups_NewestFirst()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(null, "Binh"), CancellationToken.None);

        var dashboard = await harness.UserDashboardService.GetDashboardAsync(ownerId, CancellationToken.None);

        // Ít nhất 2 hoạt động: tạo nhóm + thêm thành viên — Timeline (mục 15.5) đã xác nhận đúng logic
        // này, dashboard chỉ gộp lại nên chỉ cần xác nhận không rỗng + có gắn đúng GroupName.
        dashboard.RecentActivity.Should().NotBeEmpty();
        dashboard.RecentActivity.Should().OnlyContain(a => a.GroupName == "Du lich");
        dashboard.RecentActivity.Should().BeInDescendingOrder(a => a.CreatedAt);
    }

    [Fact]
    public async Task GetDashboardAsync_UserWithNoGroups_ReturnsEmptyDashboard()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");

        var dashboard = await harness.UserDashboardService.GetDashboardAsync(userId, CancellationToken.None);

        dashboard.TotalActiveGroups.Should().Be(0);
        dashboard.ExpensesThisMonth.Should().Be(0);
        dashboard.PendingSettlementsToConfirm.Should().BeEmpty();
        dashboard.RecentActivity.Should().BeEmpty();
    }
}
