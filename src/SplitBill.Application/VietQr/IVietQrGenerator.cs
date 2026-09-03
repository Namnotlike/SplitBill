namespace SplitBill.Application.VietQr;

/// <summary>Sinh payload QR theo chuẩn EMVCo/VietQR (CLAUDE.md mục 9). Chỉ trả về chuỗi payload.</summary>
public interface IVietQrGenerator
{
    /// <param name="bankBin">Mã BIN ngân hàng 6 số.</param>
    /// <param name="accountNumber">Số tài khoản người nhận.</param>
    /// <param name="amount">Số tiền, đơn vị đồng.</param>
    /// <param name="content">Nội dung chuyển khoản (đã rút gọn theo luật ở mục 9).</param>
    string Generate(string bankBin, string accountNumber, long amount, string content);

    /// <summary>Rút gọn tên nhóm thành nội dung chuyển khoản tối đa 25 ký tự (CLAUDE.md mục 9).</summary>
    string BuildTransferContent(string groupName);
}
