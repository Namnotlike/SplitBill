namespace SplitBill.Application.Splitting;

/// <summary>Số tiền một thành viên phải chịu (hoặc đã ứng), kết quả đầu ra của các phép chia tiền.</summary>
public sealed record MemberAmount(Guid MemberId, long Amount);
