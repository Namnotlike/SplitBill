using System.Globalization;
using FluentAssertions;
using SplitBill.Application.VietQr;
using Xunit;

namespace SplitBill.UnitTests.VietQr;

/// <summary>
/// Test cho CLAUDE.md mục 9. Không có sẵn payload mẫu đã xác nhận từ ngân hàng thật trong repo này,
/// nên bài test kiểm chứng tính đúng đắn CẤU TRÚC (TLV + CRC tự đối chiếu) thay vì so khớp một chuỗi
/// cố định — trước khi lên production nên quét thử bằng app ngân hàng thật để xác nhận thêm.
/// </summary>
public sealed class VietQrGeneratorTests
{
    private readonly VietQrGenerator _generator = new();

    [Fact]
    public void Generate_StartsWithPayloadFormatIndicator()
    {
        var payload = _generator.Generate("970436", "0011001234567", 450_000, "SplitBill Bo To");

        payload.Should().StartWith("000201"); // tag 00, len 02, value "01"
    }

    [Fact]
    public void Generate_ContainsVndCurrencyAndAmount()
    {
        var payload = _generator.Generate("970436", "0011001234567", 450_000, "SplitBill Bo To");

        payload.Should().Contain("5303704"); // tag 53 (currency), len 03, "704" = VND
        payload.Should().Contain("5406450000"); // tag 54 (amount), len 06, "450000"
    }

    [Fact]
    public void Generate_ContainsBankBinAndAccountNumber()
    {
        var payload = _generator.Generate("970436", "0011001234567", 450_000, "SplitBill Bo To");

        payload.Should().Contain("970436");
        payload.Should().Contain("0011001234567");
    }

    [Fact]
    public void Generate_Crc_IsSelfConsistent()
    {
        var payload = _generator.Generate("970436", "0011001234567", 450_000, "SplitBill Bo To");

        var withoutCrc = payload[..^4];
        var claimedCrc = payload[^4..];

        var recomputed = RecomputeCrc16(withoutCrc);
        recomputed.Should().Be(claimedCrc.ToUpperInvariant());
    }

    [Fact]
    public void Generate_DifferentAmount_ProducesDifferentCrc()
    {
        var payload1 = _generator.Generate("970436", "0011001234567", 100_000, "SplitBill Bo To");
        var payload2 = _generator.Generate("970436", "0011001234567", 200_000, "SplitBill Bo To");

        payload1[^4..].Should().NotBe(payload2[^4..]);
    }

    // Ví dụ trong CLAUDE.md mục 9: "Ăn tối quán Bò Tơ" -> budget 15 ký tự -> cắt theo từ.
    [Fact]
    public void BuildTransferContent_LongNameWithDiacritics_TruncatesAtWordBoundary()
    {
        var content = _generator.BuildTransferContent("Ăn tối quán Bò Tơ");

        content.Should().Be("SplitBill An toi quan Bo");
        content.Length.Should().BeLessThanOrEqualTo(25);
    }

    [Fact]
    public void BuildTransferContent_ShortName_UsesAsIs()
    {
        var content = _generator.BuildTransferContent("Đà Lạt");

        content.Should().Be("SplitBill Da Lat");
    }

    [Fact]
    public void BuildTransferContent_SingleWordLongerThanBudget_HardCuts()
    {
        var content = _generator.BuildTransferContent("Antoiquanbotoquanan");

        content.Length.Should().BeLessThanOrEqualTo(25);
        content.Should().StartWith("SplitBill ");
    }

    private static string RecomputeCrc16(string data)
    {
        const ushort polynomial = 0x1021;
        ushort crc = 0xFFFF;

        foreach (var b in System.Text.Encoding.ASCII.GetBytes(data))
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ polynomial) : (ushort)(crc << 1);
            }
        }

        return crc.ToString("X4", CultureInfo.InvariantCulture);
    }
}
