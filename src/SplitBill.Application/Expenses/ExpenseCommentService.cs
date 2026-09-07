using SplitBill.Application.Abstractions;
using SplitBill.Domain.Entities;
using SplitBill.Domain.Enums;
using SplitBill.Domain.Exceptions;

namespace SplitBill.Application.Expenses;

/// <summary>Cài đặt CLAUDE.md mục 19. Tách khỏi <see cref="ExpenseService"/> (giống mẫu
/// NotificationService/RecurringExpenseService) để không phình to service đã lớn sẵn — tự có
/// LoadExpenseAsync/LoadGroupAsync/ResolveCallerMember riêng, chấp nhận trùng lặp nhỏ giữa các
/// service thay vì kéo thêm 1 base class dùng chung.</summary>
public sealed class ExpenseCommentService : IExpenseCommentService
{
    private readonly IExpenseCommentRepository _commentRepository;
    private readonly IExpenseRepository _expenseRepository;
    private readonly IGroupRepository _groupRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ExpenseCommentService(
        IExpenseCommentRepository commentRepository,
        IExpenseRepository expenseRepository,
        IGroupRepository groupRepository,
        IUnitOfWork unitOfWork)
    {
        _commentRepository = commentRepository;
        _expenseRepository = expenseRepository;
        _groupRepository = groupRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<ExpenseCommentDto>> GetByExpenseIdAsync(Guid callerUserId, Guid expenseId, CancellationToken cancellationToken)
    {
        var expense = await LoadExpenseAsync(expenseId, cancellationToken);
        var group = await LoadGroupAsync(expense.GroupId, cancellationToken);
        ResolveCallerMember(group, callerUserId);

        var comments = await _commentRepository.GetByExpenseIdAsync(expenseId, cancellationToken);
        return comments.OrderBy(c => c.CreatedAt).Select(ToDto).ToList();
    }

    public async Task<ExpenseCommentDto> CreateAsync(Guid callerUserId, Guid expenseId, CreateExpenseCommentRequest request, CancellationToken cancellationToken)
    {
        var expense = await LoadExpenseAsync(expenseId, cancellationToken);
        var group = await LoadGroupAsync(expense.GroupId, cancellationToken);
        var caller = ResolveCallerMember(group, callerUserId);

        var comment = new ExpenseComment
        {
            Id = Guid.NewGuid(),
            ExpenseId = expenseId,
            AuthorMemberId = caller.Id,
            Content = request.Content,
            IsDeleted = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await _commentRepository.AddAsync(comment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ExpenseCommentDto(comment.Id, comment.ExpenseId, caller.Id, caller.DisplayName, comment.Content, comment.CreatedAt);
    }

    public async Task DeleteAsync(Guid callerUserId, Guid commentId, CancellationToken cancellationToken)
    {
        var comment = await _commentRepository.GetByIdAsync(commentId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ExpenseCommentNotFound, "Không tìm thấy bình luận.");
        var expense = await LoadExpenseAsync(comment.ExpenseId, cancellationToken);
        var group = await LoadGroupAsync(expense.GroupId, cancellationToken);
        var caller = ResolveCallerMember(group, callerUserId);

        if (comment.AuthorMemberId != caller.Id && caller.Role != GroupMemberRole.Owner)
        {
            throw new DomainException(ErrorCodes.InsufficientRole, "Chỉ tác giả bình luận hoặc Owner của nhóm mới xóa được.");
        }

        comment.IsDeleted = true;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<Group> LoadGroupAsync(Guid groupId, CancellationToken cancellationToken) =>
        await _groupRepository.GetByIdWithMembersAsync(groupId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.GroupNotFound, "Không tìm thấy nhóm.");

    private async Task<Expense> LoadExpenseAsync(Guid expenseId, CancellationToken cancellationToken) =>
        await _expenseRepository.GetByIdAsync(expenseId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ExpenseNotFound, "Không tìm thấy khoản chi.");

    private static GroupMember ResolveCallerMember(Group group, Guid callerUserId) =>
        group.Members.FirstOrDefault(m => m.UserId == callerUserId && m.IsActive)
            ?? throw new DomainException(ErrorCodes.MemberNotInGroup, "Bạn không phải thành viên của nhóm này.");

    private static ExpenseCommentDto ToDto(ExpenseComment c) => new(
        c.Id, c.ExpenseId, c.AuthorMemberId, c.AuthorMember?.DisplayName ?? "?", c.Content, c.CreatedAt);
}
