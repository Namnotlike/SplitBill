using SplitBill.Application.Expenses;

namespace SplitBill.Web.Services;

/// <summary>Một hàng thành viên trong form tạo/sửa khoản chi (dùng chung Create + Edit).</summary>
public sealed class MemberRowInput
{
    public Guid MemberId { get; set; }
    public long? PayerAmount { get; set; }
    public bool EqualParticipant { get; set; }
    public decimal? ShareWeight { get; set; }
    public decimal? Percentage { get; set; }
    public long? ExactAmount { get; set; }
}

public static class ExpenseFormHelpers
{
    public static SplitConfigInput? BuildSplitConfig(string mode, List<MemberRowInput> rows, out string? error)
    {
        error = null;

        switch (mode)
        {
            case "Equal":
                var memberIds = rows.Where(r => r.EqualParticipant).Select(r => r.MemberId).ToList();
                if (memberIds.Count == 0)
                {
                    error = "Cần chọn ít nhất 1 người tham gia chia đều.";
                    return null;
                }

                return new SplitConfigInput(MemberIds: memberIds);

            case "Shares":
                var shares = rows.Where(r => r.ShareWeight is > 0)
                    .Select(r => new SharesInput(r.MemberId, r.ShareWeight!.Value)).ToList();
                if (shares.Count == 0)
                {
                    error = "Cần nhập trọng số (share) cho ít nhất 1 người.";
                    return null;
                }

                return new SplitConfigInput(Shares: shares);

            case "Percentage":
                var percentages = rows.Where(r => r.Percentage is > 0)
                    .Select(r => new PercentageInput(r.MemberId, r.Percentage!.Value)).ToList();
                if (percentages.Count == 0)
                {
                    error = "Cần nhập % cho ít nhất 1 người.";
                    return null;
                }

                return new SplitConfigInput(Percentages: percentages);

            case "ExactAmount":
                var exact = rows.Where(r => r.ExactAmount is not null)
                    .Select(r => new ExactAmountInput(r.MemberId, r.ExactAmount!.Value)).ToList();
                if (exact.Count == 0)
                {
                    error = "Cần nhập số tiền cho ít nhất 1 người.";
                    return null;
                }

                return new SplitConfigInput(ExactAmounts: exact);

            default:
                error = $"Chế độ chia '{mode}' chưa được hỗ trợ trên giao diện này.";
                return null;
        }
    }
}
