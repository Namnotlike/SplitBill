using FluentAssertions;
using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using Xunit;

namespace SplitBill.IntegrationTests;

public sealed class ExportServiceTests
{
    [Fact]
    public async Task ExportExpensesCsvAsync_ContainsHeaderAndExpenseRow()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var guest = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(null, "Binh"), CancellationToken.None);
        await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "An toi, ngon", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(group.Members[0].Id, 100_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [group.Members[0].Id, guest.Id])), CancellationToken.None);

        var csv = await harness.ExportService.ExportExpensesCsvAsync(ownerId, group.Id, CancellationToken.None);

        csv.Should().StartWith("Ngày,Tiêu đề");
        // Tiêu đề có dấu phẩy -> phải được escape trong ngoặc kép (RFC 4180).
        csv.Should().Contain("\"An toi, ngon\"");
        csv.Should().Contain("Nam: 50000");
        csv.Should().Contain("Binh: 50000");
    }

    [Fact]
    public async Task ExportBalancesCsvAsync_ContainsHeaderAndBalanceRows()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var guest = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(null, "Binh"), CancellationToken.None);
        await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "An toi", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(group.Members[0].Id, 100_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [group.Members[0].Id, guest.Id])), CancellationToken.None);

        var csv = await harness.ExportService.ExportBalancesCsvAsync(ownerId, group.Id, CancellationToken.None);

        // Header cột 2 chứa dấu phẩy nên bị escape trong ngoặc kép (RFC 4180) — đúng hành vi mong đợi.
        csv.Should().StartWith("Thành viên,\"Số dư");
        csv.Should().Contain("Nam,50000");
        csv.Should().Contain("Binh,-50000");
    }
}
