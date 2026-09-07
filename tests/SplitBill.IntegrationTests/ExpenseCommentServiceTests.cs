using FluentAssertions;
using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using SplitBill.Domain.Exceptions;
using Xunit;

namespace SplitBill.IntegrationTests;

/// <summary>CLAUDE.md mục 19 — Bình luận trên khoản chi.</summary>
public sealed class ExpenseCommentServiceTests
{
    private static async Task<(TestHarness Harness, Guid OwnerId, Guid MemberId, GroupDto Group, Guid ExpenseId)> SetupAsync()
    {
        var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var memberId = await harness.RegisterUserAsync("b@example.com", "Binh");
        await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(memberId, "Binh"), CancellationToken.None);
        var ownerMemberId = group.Members[0].Id;

        var expense = await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "An toi", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 100_000)],
            "Equal", new SplitConfigInput(MemberIds: [ownerMemberId])), CancellationToken.None);

        return (harness, ownerId, memberId, group, expense.Data.Id);
    }

    [Fact]
    public async Task CreateAsync_ValidContent_ReturnsCommentWithAuthorDisplayName()
    {
        var (harness, ownerId, _, _, expenseId) = await SetupAsync();

        var comment = await harness.ExpenseCommentService.CreateAsync(ownerId, expenseId, new CreateExpenseCommentRequest("Sao khoan nay dat vay?"), CancellationToken.None);

        comment.Content.Should().Be("Sao khoan nay dat vay?");
        comment.AuthorDisplayName.Should().Be("Nam");
        comment.ExpenseId.Should().Be(expenseId);
    }

    [Fact]
    public async Task GetByExpenseIdAsync_ReturnsCommentsOrderedByCreatedAt()
    {
        var (harness, ownerId, memberId, _, expenseId) = await SetupAsync();
        await harness.ExpenseCommentService.CreateAsync(ownerId, expenseId, new CreateExpenseCommentRequest("Binh luan 1"), CancellationToken.None);
        await harness.ExpenseCommentService.CreateAsync(memberId, expenseId, new CreateExpenseCommentRequest("Binh luan 2"), CancellationToken.None);

        var comments = await harness.ExpenseCommentService.GetByExpenseIdAsync(ownerId, expenseId, CancellationToken.None);

        comments.Should().HaveCount(2);
        comments[0].Content.Should().Be("Binh luan 1");
        comments[1].Content.Should().Be("Binh luan 2");
        comments[1].AuthorDisplayName.Should().Be("Binh");
    }

    [Fact]
    public async Task CreateAsync_CallerNotMember_ThrowsMemberNotInGroup()
    {
        var (harness, _, _, _, expenseId) = await SetupAsync();
        var strangerId = await harness.RegisterUserAsync("c@example.com", "La");

        var act = () => harness.ExpenseCommentService.CreateAsync(strangerId, expenseId, new CreateExpenseCommentRequest("Xin chao"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.MemberNotInGroup);
    }

    [Fact]
    public async Task DeleteAsync_ByAuthor_Succeeds_AndCommentNoLongerListed()
    {
        var (harness, _, memberId, _, expenseId) = await SetupAsync();
        var comment = await harness.ExpenseCommentService.CreateAsync(memberId, expenseId, new CreateExpenseCommentRequest("Cua Binh"), CancellationToken.None);

        await harness.ExpenseCommentService.DeleteAsync(memberId, comment.Id, CancellationToken.None);

        var comments = await harness.ExpenseCommentService.GetByExpenseIdAsync(memberId, expenseId, CancellationToken.None);
        comments.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_ByOwnerNotAuthor_Succeeds()
    {
        var (harness, ownerId, memberId, _, expenseId) = await SetupAsync();
        var comment = await harness.ExpenseCommentService.CreateAsync(memberId, expenseId, new CreateExpenseCommentRequest("Cua Binh"), CancellationToken.None);

        // Owner khong phai tac gia nhung van xoa duoc (khop quyen "doi ten nguoi khac chi Owner").
        await harness.ExpenseCommentService.DeleteAsync(ownerId, comment.Id, CancellationToken.None);

        var comments = await harness.ExpenseCommentService.GetByExpenseIdAsync(ownerId, expenseId, CancellationToken.None);
        comments.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_ByOtherMemberNotAuthorNotOwner_ThrowsInsufficientRole()
    {
        var (harness, ownerId, memberId, group, expenseId) = await SetupAsync();
        var comment = await harness.ExpenseCommentService.CreateAsync(ownerId, expenseId, new CreateExpenseCommentRequest("Cua Nam"), CancellationToken.None);

        // Binh khong phai tac gia (Nam moi la tac gia) va cung khong phai Owner -> phai bi chan.
        var act = () => harness.ExpenseCommentService.DeleteAsync(memberId, comment.Id, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.InsufficientRole);
    }

    [Fact]
    public async Task DeleteAsync_UnknownComment_ThrowsExpenseCommentNotFound()
    {
        var (harness, ownerId, _, _, _) = await SetupAsync();

        var act = () => harness.ExpenseCommentService.DeleteAsync(ownerId, Guid.NewGuid(), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.ExpenseCommentNotFound);
    }
}
