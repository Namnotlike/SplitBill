using FluentAssertions;
using SplitBill.Application.Export;
using Xunit;

namespace SplitBill.UnitTests.Export;

/// <summary>Test cho biện pháp giảm thiểu CSV/Formula Injection (CWE-1236) — phát hiện qua
/// security-review 2026-09-07. Xem ghi chú trong <see cref="CsvBuilder"/>.</summary>
public sealed class CsvBuilderTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"http://evil.example\",\"click\")", "'=HYPERLINK(\"\"http://evil.example\"\",\"\"click\"\")")]
    [InlineData("+1+1", "'+1+1")]
    [InlineData("-1+1", "'-1+1")]
    [InlineData("@SUM(1,1)", "'@SUM(1,1)")]
    public void AddRow_FieldStartsWithFormulaTriggerChar_PrefixedWithSingleQuote(string maliciousField, string expectedRawText)
    {
        var csv = new CsvBuilder().AddRow(maliciousField).ToString();

        // Field chứa dấu phẩy/ngoặc kép nên vẫn bị bọc trong ngoặc kép theo RFC 4180 (expectedRawText
        // đã escape ngoặc kép); điểm mấu chốt cần verify là dấu nháy đơn (') luôn đứng ngay trước ký
        // tự kích hoạt công thức gốc, để Excel/Sheets hiển thị nguyên văn thay vì tính toán.
        csv.Should().Contain(expectedRawText);
    }

    [Fact]
    public void AddRow_NormalField_NotPrefixed()
    {
        var csv = new CsvBuilder().AddRow("An toi ngon", 50_000).ToString();

        csv.Should().Be("An toi ngon,50000\r\n");
    }

    [Fact]
    public void AddRow_FieldWithCommaAndFormulaTrigger_QuotedAndPrefixed()
    {
        var csv = new CsvBuilder().AddRow("=1+1, roi sao").ToString();

        csv.Should().Be("\"'=1+1, roi sao\"\r\n");
    }

    // Regression: lần sửa đầu tiên áp luật chống công thức cho MỌI field (kể cả số), khiến số dư âm
    // hợp lệ như -50000 bị biến thành chuỗi "'-50000" — phá hỏng đúng công dụng của export CSV số dư
    // (Excel không còn tính tổng/so sánh được). Field kiểu số không phải text người dùng gõ tự do nên
    // không cần (và không được) áp phòng vệ này.
    [Fact]
    public void AddRow_NumericFieldStartsWithMinus_NotPrefixed()
    {
        var csv = new CsvBuilder().AddRow("Binh", -50_000L).ToString();

        csv.Should().Be("Binh,-50000\r\n");
    }
}
