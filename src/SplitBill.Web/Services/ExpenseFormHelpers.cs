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

/// <summary>
/// Một món ăn trong form SplitMode.Itemized. Name/Price PHẢI để nullable (không phải string/long
/// trơn): dòng món ăn trống tự thêm sẵn lúc tải trang (xem Create.cshtml/Edit.cshtml) luôn có mặt
/// trong DOM cho MỌI SplitMode (chỉ ẩn bằng CSS display:none khi không phải Itemized — input ẩn kiểu
/// này vẫn được trình duyệt submit bình thường, không giống input disabled). Nếu Name/Price không
/// nullable, ASP.NET Core coi non-nullable reference type là implicit-required và "" không parse
/// được thành long, khiến ModelState invalid ở MỌI SplitMode trừ Itemized — form Tạo/Sửa khoản chi
/// sập hoàn toàn không rõ lý do (OnPostAsync trả về Page() không có thông báo lỗi nào). Bug thật đã
/// gặp: tạo khoản chi Equal/Shares/... không bao giờ lưu được, submit "thành công" im lặng quay lại
/// đúng trang Tạo. Nullable ở đây + lọc bỏ dòng trống trong BuildSplitConfig là cách sửa đúng.
/// </summary>
public sealed class ItemInput
{
    public string? Name { get; set; }
    public long? Price { get; set; }
    public List<Guid> ConsumerMemberIds { get; set; } = new();
}

public static class ExpenseFormHelpers
{
    public static SplitConfigInput? BuildSplitConfig(string mode, List<MemberRowInput> rows, List<ItemInput> items, out string? error)
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

            case "Itemized":
                var validItems = items
                    .Where(i => !string.IsNullOrWhiteSpace(i.Name) && i.Price is > 0 && i.ConsumerMemberIds.Count > 0)
                    .Select(i => new ItemizedInput(i.Name!, i.Price!.Value, i.ConsumerMemberIds))
                    .ToList();
                if (validItems.Count == 0)
                {
                    error = "Cần ít nhất 1 món có tên, giá > 0 và ít nhất 1 người ăn.";
                    return null;
                }

                return new SplitConfigInput(Items: validItems);

            default:
                error = $"Chế độ chia '{mode}' chưa được hỗ trợ trên giao diện này.";
                return null;
        }
    }
}
