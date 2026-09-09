using FluentAssertions;
using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using SplitBill.Application.Settlements;
using SplitBill.Domain.Exceptions;
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

        csv.Should().StartWith("Ngày,Tiêu đề,Danh mục");
        // Tiêu đề có dấu phẩy -> phải được escape trong ngoặc kép (RFC 4180).
        csv.Should().Contain("\"An toi, ngon\"");
        // Không truyền Category lúc tạo -> mặc định "Other" (CLAUDE.md mục 15.3).
        csv.Should().Contain(",Other,");
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

    // ===== Xuất/backup JSON toàn bộ dữ liệu nhóm (CLAUDE.md mục 25.4, bổ sung 2026-09-09) =====

    [Fact]
    public async Task ExportGroupBackupAsync_IncludesGroupMembersExpensesAndSettlements()
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
        await harness.SettlementRecordService.CreateAsync(ownerId, group.Id,
            new CreateSettlementRequest(guest.Id, group.Members[0].Id, 50_000), CancellationToken.None);

        var backup = await harness.ExportService.ExportGroupBackupAsync(ownerId, group.Id, CancellationToken.None);

        backup.Group.Id.Should().Be(group.Id);
        backup.Group.Members.Should().HaveCount(2);
        backup.Expenses.Should().ContainSingle(e => e.Title == "An toi");
        backup.Settlements.Should().ContainSingle(s => s.FromMemberId == guest.Id && s.Amount == 50_000);
        backup.ExportedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ExportGroupBackupAsync_CallerNotMember_ThrowsMemberNotInGroup()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var outsiderId = await harness.RegisterUserAsync("b@example.com", "Binh");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);

        var act = () => harness.ExportService.ExportGroupBackupAsync(outsiderId, group.Id, CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task ExportGroupBackupAsync_CallerLeftGroup_ThrowsMemberNotInGroup()
    {
        // Cùng lớp lỗi đã sửa ở CLAUDE.md mục 5.4/15.6 (không lọc GroupMember.IsActive) — thành viên
        // đã rời nhóm không được phép tiếp tục tải backup dữ liệu của nhóm đó. GroupService.
        // ResolveCallerMember (dùng bởi GetByIdAsync, mà ExportGroupBackupAsync gọi qua) đã lọc
        // IsActive từ trước, test này chỉ để xác nhận tường minh + chặn hồi quy trong tương lai.
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var memberUserId = await harness.RegisterUserAsync("b@example.com", "Binh");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var member = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(memberUserId, "Binh"), CancellationToken.None);
        // Rời nhóm chỉ được phép khi net == 0 — không có expense/settlement nào nên hợp lệ ngay.
        await harness.GroupService.RemoveMemberAsync(ownerId, group.Id, member.Id, CancellationToken.None);

        var act = () => harness.ExportService.ExportGroupBackupAsync(memberUserId, group.Id, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.MemberNotInGroup);
    }
}
