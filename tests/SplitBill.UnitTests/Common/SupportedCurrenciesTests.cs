using FluentAssertions;
using SplitBill.Application.Common;
using Xunit;

namespace SplitBill.UnitTests.Common;

/// <summary>Test cho CLAUDE.md mục 14 — Đa tiền tệ (mỗi nhóm cố định 1 loại, không quy đổi tỉ giá).</summary>
public sealed class SupportedCurrenciesTests
{
    [Theory]
    [InlineData("VND", true)]
    [InlineData("USD", true)]
    [InlineData("EUR", true)]
    [InlineData("JPY", false)]
    [InlineData("", false)]
    public void IsSupported_ChecksAgainstAllowList(string code, bool expected)
    {
        SupportedCurrencies.IsSupported(code).Should().Be(expected);
    }

    [Fact]
    public void IsSupported_Null_ReturnsFalse()
    {
        SupportedCurrencies.IsSupported(null).Should().BeFalse();
    }

    [Fact]
    public void Format_Vnd_AppendsSuffixSymbol()
    {
        SupportedCurrencies.Format(100_000, "VND").Should().Be("100.000đ");
    }

    [Fact]
    public void Format_Usd_PrependsPrefixSymbol()
    {
        SupportedCurrencies.Format(1_500, "USD").Should().Be("$1,500");
    }

    [Fact]
    public void Format_Eur_AppendsSuffixSymbol()
    {
        // EUR dùng cách nhóm số kiểu châu Âu/Việt Nam (dấu chấm) — xem ghi chú trong SupportedCurrencies.
        SupportedCurrencies.Format(1_500, "EUR").Should().Be("1.500€");
    }

    [Fact]
    public void Format_NegativeAmount_KeepsMinusSign()
    {
        SupportedCurrencies.Format(-50_000, "VND").Should().Be("-50.000đ");
    }

    [Fact]
    public void Format_UnknownCurrency_FallsBackToPlainNumber()
    {
        SupportedCurrencies.Format(1_000, "XYZ").Should().Be("1,000");
    }
}
