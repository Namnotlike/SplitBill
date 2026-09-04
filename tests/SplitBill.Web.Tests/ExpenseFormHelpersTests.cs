using FluentAssertions;
using SplitBill.Web.Services;
using Xunit;

namespace SplitBill.Web.Tests;

public sealed class ExpenseFormHelpersTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    [Fact]
    public void Equal_NoOneChecked_ReturnsError()
    {
        var rows = new List<MemberRowInput> { new() { MemberId = A, EqualParticipant = false } };

        var config = ExpenseFormHelpers.BuildSplitConfig("Equal", rows, [], out var error);

        config.Should().BeNull();
        error.Should().NotBeNull();
    }

    [Fact]
    public void Equal_SomeChecked_ReturnsMemberIds()
    {
        var rows = new List<MemberRowInput>
        {
            new() { MemberId = A, EqualParticipant = true },
            new() { MemberId = B, EqualParticipant = false },
        };

        var config = ExpenseFormHelpers.BuildSplitConfig("Equal", rows, [], out var error);

        error.Should().BeNull();
        config!.MemberIds.Should().Equal(A);
    }

    [Fact]
    public void Shares_ZeroWeightsIgnored()
    {
        var rows = new List<MemberRowInput>
        {
            new() { MemberId = A, ShareWeight = 2 },
            new() { MemberId = B, ShareWeight = 0 },
        };

        var config = ExpenseFormHelpers.BuildSplitConfig("Shares", rows, [], out var error);

        error.Should().BeNull();
        config!.Shares.Should().ContainSingle(s => s.MemberId == A && s.Weight == 2);
    }

    [Fact]
    public void Percentage_NoneEntered_ReturnsError()
    {
        var rows = new List<MemberRowInput> { new() { MemberId = A, Percentage = null } };

        var config = ExpenseFormHelpers.BuildSplitConfig("Percentage", rows, [], out var error);

        config.Should().BeNull();
        error.Should().NotBeNull();
    }

    [Fact]
    public void ExactAmount_EntersZero_StillCounted()
    {
        // ExactAmount cho phép 0 (loại người đó khỏi khoản chi này) — khác Shares/Percentage.
        var rows = new List<MemberRowInput> { new() { MemberId = A, ExactAmount = 0 } };

        var config = ExpenseFormHelpers.BuildSplitConfig("ExactAmount", rows, [], out var error);

        error.Should().BeNull();
        config!.ExactAmounts.Should().ContainSingle(e => e.MemberId == A && e.Amount == 0);
    }

    [Fact]
    public void Itemized_ItemWithNoConsumers_Excluded()
    {
        var items = new List<ItemInput>
        {
            new() { Name = "Lẩu", Price = 100_000, ConsumerMemberIds = [A, B] },
            new() { Name = "Trà đá", Price = 10_000, ConsumerMemberIds = [] }, // không ai ăn -> loại
        };

        var config = ExpenseFormHelpers.BuildSplitConfig("Itemized", [], items, out var error);

        error.Should().BeNull();
        config!.Items.Should().ContainSingle(i => i.Name == "Lẩu");
    }

    [Fact]
    public void Itemized_AllItemsInvalid_ReturnsError()
    {
        var items = new List<ItemInput> { new() { Name = "", Price = 0, ConsumerMemberIds = [] } };

        var config = ExpenseFormHelpers.BuildSplitConfig("Itemized", [], items, out var error);

        config.Should().BeNull();
        error.Should().NotBeNull();
    }

    [Fact]
    public void UnknownMode_ReturnsError()
    {
        var config = ExpenseFormHelpers.BuildSplitConfig("NotARealMode", [], [], out var error);

        config.Should().BeNull();
        error.Should().Contain("NotARealMode");
    }
}
