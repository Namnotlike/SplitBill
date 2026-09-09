using FluentAssertions;
using SplitBill.Application.Common;
using Xunit;

namespace SplitBill.UnitTests.Common;

/// <summary>Test cho CLAUDE.md mục 25.3 — bí mật bảo vệ POST /auth/google.</summary>
public sealed class InternalSecretComparerTests
{
    [Fact]
    public void Matches_EqualSecrets_ReturnsTrue()
    {
        InternalSecretComparer.Matches("abc123", "abc123").Should().BeTrue();
    }

    [Fact]
    public void Matches_DifferentSecrets_ReturnsFalse()
    {
        InternalSecretComparer.Matches("abc123", "abc124").Should().BeFalse();
    }

    [Fact]
    public void Matches_DifferentLengths_ReturnsFalse()
    {
        InternalSecretComparer.Matches("abc", "abc123").Should().BeFalse();
    }

    [Fact]
    public void Matches_ExpectedSecretNotConfigured_AlwaysReturnsFalse_EvenIfProvidedIsAlsoEmpty()
    {
        // Fail closed: chưa cấu hình GoogleAuth:InternalSecret (rỗng) không được coi là "khớp" với
        // 1 header rỗng — nếu không, endpoint /auth/google sẽ mở toang cho MỌI request không kèm
        // header nào, ngay khi quên cấu hình secret.
        InternalSecretComparer.Matches("", "").Should().BeFalse();
        InternalSecretComparer.Matches(null, "").Should().BeFalse();
        InternalSecretComparer.Matches("anything", "").Should().BeFalse();
        InternalSecretComparer.Matches("anything", null).Should().BeFalse();
    }

    [Fact]
    public void Matches_ProvidedMissing_ReturnsFalse()
    {
        InternalSecretComparer.Matches(null, "real-secret").Should().BeFalse();
        InternalSecretComparer.Matches("", "real-secret").Should().BeFalse();
    }
}
