using System.Globalization;
using System.Text;

namespace SplitBill.Application.VietQr;

/// <summary>
/// Cài đặt CLAUDE.md mục 9: payload EMVCo/VietQR (TLV + CRC16-CCITT False) và thuật toán rút gọn
/// nội dung chuyển khoản. Class thuần, không phụ thuộc EF Core/DbContext.
/// </summary>
public sealed class VietQrGenerator : IVietQrGenerator
{
    private const string GuidVietQr = "A000000727";
    private const string ServiceCodeAccount = "QRIBFTTA";
    private const string CurrencyVnd = "704";
    private const string CountryCode = "VN";
    private const string Prefix = "SplitBill ";
    private const int MaxContentLength = 25;

    public string Generate(string bankBin, string accountNumber, long amount, string content)
    {
        if (string.IsNullOrWhiteSpace(bankBin) || string.IsNullOrWhiteSpace(accountNumber))
        {
            throw new ArgumentException("Cần bankBin và accountNumber để sinh VietQR.");
        }

        var accountInfo = Tlv("00", bankBin) + Tlv("01", accountNumber);
        var merchantAccountInfo = Tlv("00", GuidVietQr) + Tlv("01", accountInfo) + Tlv("02", ServiceCodeAccount);
        var additionalData = Tlv("62", Tlv("08", content));

        var payloadWithoutCrc =
            Tlv("00", "01") +               // Payload Format Indicator
            Tlv("01", "12") +               // Point of Initiation Method: 12 = dynamic (có amount)
            Tlv("38", merchantAccountInfo) +
            Tlv("53", CurrencyVnd) +
            Tlv("54", amount.ToString(CultureInfo.InvariantCulture)) +
            Tlv("58", CountryCode) +
            additionalData +
            "6304"; // tag + length của chính field CRC, CRC tính luôn trên đoạn này

        var crc = ComputeCrc16(payloadWithoutCrc);
        return payloadWithoutCrc + crc.ToString("X4", CultureInfo.InvariantCulture);
    }

    public string BuildTransferContent(string groupName)
    {
        var normalized = RemoveDiacritics(groupName);
        var budget = MaxContentLength - Prefix.Length;

        if (normalized.Length <= budget)
        {
            return Prefix + normalized;
        }

        // Cắt theo từ (word boundary), không cắt giữa từ; nếu từ đầu tiên đã dài hơn budget thì
        // cắt cứng theo ký tự (CLAUDE.md mục 9).
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return Prefix;
        }

        if (words[0].Length >= budget)
        {
            return Prefix + words[0][..budget];
        }

        var builder = new StringBuilder(words[0]);
        foreach (var word in words.Skip(1))
        {
            var candidate = builder.Length + 1 + word.Length;
            if (candidate > budget)
            {
                break;
            }

            builder.Append(' ').Append(word);
        }

        return Prefix + builder;
    }

    private static string RemoveDiacritics(string input)
    {
        var withoutDStroke = input.Replace('đ', 'd').Replace('Đ', 'D');
        var normalized = withoutDStroke.Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark
                && (char.IsLetterOrDigit(c) || c == ' '))
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string Tlv(string id, string value) => id + value.Length.ToString("D2", CultureInfo.InvariantCulture) + value;

    /// <summary>CRC-16/CCITT-FALSE (poly 0x1021, init 0xFFFF) — chuẩn dùng trong EMVCo QR (tag 63).</summary>
    private static ushort ComputeCrc16(string data)
    {
        const ushort polynomial = 0x1021;
        ushort crc = 0xFFFF;

        foreach (var b in Encoding.ASCII.GetBytes(data))
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ polynomial) : (ushort)(crc << 1);
            }
        }

        return crc;
    }
}
