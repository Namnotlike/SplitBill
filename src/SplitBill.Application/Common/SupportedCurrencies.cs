using System.Globalization;

namespace SplitBill.Application.Common;

/// <summary>
/// Danh sách tiền tệ được hỗ trợ (CLAUDE.md mục 14 — Đa tiền tệ, bổ sung 2026-09-05). Quyết định
/// người dùng: mỗi <c>Group</c> dùng CỐ ĐỊNH đúng 1 loại tiền (field <c>Group.Currency</c> đã có sẵn
/// từ đầu dự án, trước đó chưa từng được validate/dùng tới) — KHÔNG trộn nhiều tiền tệ trong 1 nhóm,
/// KHÔNG quy đổi tỉ giá, KHÔNG đổi currency sau khi nhóm đã tạo (đổi sẽ làm sai lệch mọi khoản chi cũ).
///
/// Đơn giản hóa có chủ đích: <c>Amount</c> (long) luôn là ĐƠN VỊ NGUYÊN của đồng tiền đó (đồng cho
/// VND, đô-la cho USD, euro cho EUR — KHÔNG phải cent/xu), không có phần thập phân cho bất kỳ tiền tệ
/// nào — khác với quy ước "cent là đơn vị nhỏ nhất" thường thấy (Stripe...). Chọn cách này để giữ
/// nguyên toàn bộ form nhập liệu hiện có (vẫn nhập số nguyên thuần), tránh phải viết lại logic quy đổi
/// thập phân ở mọi nơi cho một tính năng phụ (nhóm bạn chia tiền hiếm khi cần chính xác tới xu).
/// </summary>
public static class SupportedCurrencies
{
    // ⚠️ Cố tình dùng CultureInfo TƯỜNG MINH cho từng currency, KHÔNG dựa vào CultureInfo.CurrentCulture
    // (ambient culture của thread/server) — phát hiện qua test tự động: cùng 1 dòng code
    // `amount.ToString("N0")` cho ra dấu phân cách hàng nghìn KHÁC NHAU tùy hệ điều hành/server đang
    // chạy ở locale nào (máy dev này mặc định vi-VN nên "N0" ra dấu chấm, kể cả khi format USD — dễ
    // đọc nhầm "$1.500" thành "1 đô rưỡi" thay vì "1500 đô"). Ép cứng InvariantCulture (dấu phẩy hàng
    // nghìn kiểu US) cho USD, "vi-VN" (dấu chấm) cho VND/EUR để kết quả nhất quán bất kể server chạy ở
    // đâu, không phụ thuộc cấu hình locale ngoài tầm kiểm soát.
    private static readonly CultureInfo VietnameseStyleGrouping = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly CultureInfo UsStyleGrouping = CultureInfo.InvariantCulture;

    public sealed record Info(string Code, string Symbol, bool SymbolIsSuffix, CultureInfo NumberFormat);

    public static readonly IReadOnlyDictionary<string, Info> All = new Dictionary<string, Info>
    {
        ["VND"] = new Info("VND", "đ", SymbolIsSuffix: true, VietnameseStyleGrouping),
        ["USD"] = new Info("USD", "$", SymbolIsSuffix: false, UsStyleGrouping),
        ["EUR"] = new Info("EUR", "€", SymbolIsSuffix: true, VietnameseStyleGrouping),
    };

    public const string Default = "VND";

    public static bool IsSupported(string? code) => code is not null && All.ContainsKey(code);

    /// <summary>Định dạng số tiền kèm ký hiệu đúng vị trí (prefix/suffix) theo currency — dùng chung
    /// cho cả Api (nếu cần) lẫn Web (hiển thị), tránh viết trùng logic ở nhiều nơi.</summary>
    public static string Format(long amount, string currencyCode)
    {
        if (!All.TryGetValue(currencyCode, out var info))
        {
            // currency lạ (dữ liệu cũ trước khi validate) — hiện số trần theo invariant, không đoán ký hiệu.
            return amount.ToString("N0", UsStyleGrouping);
        }

        var formatted = amount.ToString("N0", info.NumberFormat);
        return info.SymbolIsSuffix ? $"{formatted}{info.Symbol}" : $"{info.Symbol}{formatted}";
    }
}
