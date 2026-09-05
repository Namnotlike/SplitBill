using FluentAssertions;
using SplitBill.Application.Groups;
using Xunit;

namespace SplitBill.UnitTests.Groups;

/// <summary>Test cho CLAUDE.md mục 14 — validate Currency khi tạo nhóm (mỗi nhóm cố định 1 loại tiền
/// trong danh sách được hỗ trợ).</summary>
public sealed class CreateGroupRequestValidatorTests
{
    private readonly CreateGroupRequestValidator _validator = new();

    [Theory]
    [InlineData("VND")]
    [InlineData("USD")]
    [InlineData("EUR")]
    public void Validate_SupportedCurrency_IsValid(string currency)
    {
        var result = _validator.Validate(new CreateGroupRequest("Du lich", null, "OneTime", currency));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyCurrency_IsValid_DefaultsToVndLater()
    {
        // Rỗng thì GroupService tự mặc định VND (SupportedCurrencies.Default) — validator không chặn.
        var result = _validator.Validate(new CreateGroupRequest("Du lich", null, "OneTime", null));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_UnsupportedCurrency_IsInvalid()
    {
        var result = _validator.Validate(new CreateGroupRequest("Du lich", null, "OneTime", "JPY"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Currency");
    }
}
