namespace SplitBill.Domain.Exceptions;

/// <summary>Hằng số errorCode dùng trong toàn bộ ứng dụng (xem CLAUDE.md mục 8).</summary>
public static class ErrorCodes
{
    public const string MemberHasOutstandingBalance = "MEMBER_HAS_OUTSTANDING_BALANCE";
    public const string InsufficientRole = "INSUFFICIENT_ROLE";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string SplitTotalMismatch = "SPLIT_TOTAL_MISMATCH";
    public const string MemberNotInGroup = "MEMBER_NOT_IN_GROUP";
    // Gán payer/split cho 1 thành viên đã rời nhóm (IsActive = false) — CHỈ chặn khi đây là tham
    // chiếu MỚI (thành viên đó không có mặt trong Payers/Splits gốc trước khi sửa); giữ nguyên tham
    // chiếu cũ khi sửa khoản chi lịch sử vẫn được phép (security-review 2026-09-07, xem CLAUDE.md mục 5.4).
    public const string MemberNotActive = "MEMBER_NOT_ACTIVE";
    public const string DuplicateMemberId = "DUPLICATE_MEMBER_ID";
    public const string LastOwnerCannotBeRemoved = "LAST_OWNER_CANNOT_BE_REMOVED";
    public const string SettlementSameMember = "SETTLEMENT_SAME_MEMBER";
    public const string SettlementNotPending = "SETTLEMENT_NOT_PENDING";
    public const string EmailAlreadyRegistered = "EMAIL_ALREADY_REGISTERED";
    public const string GroupNotFound = "GROUP_NOT_FOUND";
    public const string InvalidRefreshToken = "INVALID_REFRESH_TOKEN";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string ExpenseNotFound = "EXPENSE_NOT_FOUND";
    public const string MemberNotFound = "MEMBER_NOT_FOUND";
    public const string NotificationNotFound = "NOTIFICATION_NOT_FOUND";
    // Tham gia nhóm qua link chia sẻ (CLAUDE.md mục 15.6, bổ sung 2026-09-05).
    public const string AlreadyGroupMember = "ALREADY_GROUP_MEMBER";
    // Khoản chi định kỳ (CLAUDE.md mục 15.7, bổ sung 2026-09-05).
    public const string GroupTypeNotRecurring = "GROUP_TYPE_NOT_RECURRING";
    public const string RecurringExpenseNotFound = "RECURRING_EXPENSE_NOT_FOUND";
    // Quên mật khẩu (CLAUDE.md mục 16, bổ sung 2026-09-07).
    public const string InvalidResetToken = "INVALID_RESET_TOKEN";
    // Bình luận khoản chi (CLAUDE.md mục 19, bổ sung 2026-09-07).
    public const string ExpenseCommentNotFound = "EXPENSE_COMMENT_NOT_FOUND";
    // Preset cách chia (CLAUDE.md mục 21, bổ sung 2026-09-07).
    public const string SplitPresetNotFound = "SPLIT_PRESET_NOT_FOUND";
    // Khôi phục khoản chi/thanh toán đã xóa (CLAUDE.md mục 24, bổ sung 2026-09-09).
    public const string ExpenseNotDeleted = "EXPENSE_NOT_DELETED";
    public const string SettlementNotDeleted = "SETTLEMENT_NOT_DELETED";
}
