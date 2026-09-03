namespace SplitBill.Domain.Enums;

/// <summary>Trạng thái của một lượt thanh toán được ghi nhận.</summary>
public enum SettlementStatus
{
    Pending = 0,
    Confirmed = 1,
    Rejected = 2,
}
