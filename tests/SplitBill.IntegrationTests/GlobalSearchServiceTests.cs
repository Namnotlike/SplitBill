using FluentAssertions;
using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using Xunit;

namespace SplitBill.IntegrationTests;

/// <summary>CLAUDE.md mục 25.8 — Tìm kiếm xuyên nhóm.</summary>
public sealed class GlobalSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmptyResult_NoDbScan()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");
        await harness.GroupService.CreateAsync(userId, new CreateGroupRequest("Du lich Da Lat", null, "OneTime", "VND"), CancellationToken.None);

        var result = await harness.GlobalSearchService.SearchAsync(userId, "", CancellationToken.None);

        result.Groups.Should().BeEmpty();
        result.Expenses.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_MatchesGroupNameCaseInsensitive()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");
        await harness.GroupService.CreateAsync(userId, new CreateGroupRequest("Du lich Da Lat", null, "OneTime", "VND"), CancellationToken.None);
        await harness.GroupService.CreateAsync(userId, new CreateGroupRequest("An nhau cuoi tuan", null, "OneTime", "VND"), CancellationToken.None);

        var result = await harness.GlobalSearchService.SearchAsync(userId, "DA LAT", CancellationToken.None);

        result.Groups.Should().ContainSingle().Which.GroupName.Should().Be("Du lich Da Lat");
    }

    [Fact]
    public async Task SearchAsync_MatchesExpenseTitleAcrossMultipleGroups()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group1 = await harness.GroupService.CreateAsync(userId, new CreateGroupRequest("Nhom A", null, "OneTime", "VND"), CancellationToken.None);
        var group2 = await harness.GroupService.CreateAsync(userId, new CreateGroupRequest("Nhom B", null, "OneTime", "VND"), CancellationToken.None);
        var memberId1 = group1.Members[0].Id;
        var memberId2 = group2.Members[0].Id;

        await harness.ExpenseService.CreateAsync(userId, group1.Id, new CreateExpenseRequest(
            "An pho bo", 100000, 0, DateTimeOffset.UtcNow, [new ExpensePayerInput(memberId1, 100000)], "Equal",
            new SplitConfigInput(MemberIds: [memberId1]), null, null, null), CancellationToken.None);
        await harness.ExpenseService.CreateAsync(userId, group2.Id, new CreateExpenseRequest(
            "Pho ga buoi sang", 50000, 0, DateTimeOffset.UtcNow, [new ExpensePayerInput(memberId2, 50000)], "Equal",
            new SplitConfigInput(MemberIds: [memberId2]), null, null, null), CancellationToken.None);
        await harness.ExpenseService.CreateAsync(userId, group1.Id, new CreateExpenseRequest(
            "Ve xem phim", 200000, 0, DateTimeOffset.UtcNow, [new ExpensePayerInput(memberId1, 200000)], "Equal",
            new SplitConfigInput(MemberIds: [memberId1]), null, null, null), CancellationToken.None);

        var result = await harness.GlobalSearchService.SearchAsync(userId, "pho", CancellationToken.None);

        result.Expenses.Should().HaveCount(2);
        result.Expenses.Should().Contain(e => e.Title == "An pho bo" && e.GroupName == "Nhom A");
        result.Expenses.Should().Contain(e => e.Title == "Pho ga buoi sang" && e.GroupName == "Nhom B");
        result.Expenses.Should().NotContain(e => e.Title == "Ve xem phim");
    }

    [Fact]
    public async Task SearchAsync_OnlyIncludesGroupsCallerIsActiveMemberOf()
    {
        using var harness = TestHarness.Create();
        var owner = await harness.RegisterUserAsync("a@example.com", "Nam");
        var stranger = await harness.RegisterUserAsync("b@example.com", "La");
        await harness.GroupService.CreateAsync(owner, new CreateGroupRequest("Nhom rieng cua Nam", null, "OneTime", "VND"), CancellationToken.None);

        var result = await harness.GlobalSearchService.SearchAsync(stranger, "Nhom", CancellationToken.None);

        result.Groups.Should().BeEmpty();
    }
}
