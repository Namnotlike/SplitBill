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
}
