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

    private static string Escape(object? field)
    {
        var text = field?.ToString() ?? string.Empty;
        if (text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.Contains('\r'))
        {
            return $"\"{text.Replace("\"", "\"\"")}\"";
        }

        return text;
    }
}
