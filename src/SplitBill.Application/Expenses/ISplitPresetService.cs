namespace SplitBill.Application.Expenses;

/// <summary>CLAUDE.md mục 21 — Preset cách chia hay dùng.</summary>
public interface ISplitPresetService
{
    Task<IReadOnlyList<SplitPresetDto>> GetByGroupIdAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken);

    Task<SplitPresetDto> CreateAsync(Guid callerUserId, Guid groupId, CreateSplitPresetRequest request, CancellationToken cancellationToken);

    /// <summary>Chỉ tác giả hoặc Owner của nhóm mới xóa được (403 INSUFFICIENT_ROLE nếu không đủ
    /// quyền) — cùng mẫu <see cref="IExpenseCommentService.DeleteAsync"/> (mục 19/4.4).</summary>
    Task DeleteAsync(Guid callerUserId, Guid presetId, CancellationToken cancellationToken);
}
