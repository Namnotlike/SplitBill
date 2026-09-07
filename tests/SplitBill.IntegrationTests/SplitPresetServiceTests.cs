using FluentAssertions;
using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using SplitBill.Domain.Exceptions;
using Xunit;

namespace SplitBill.IntegrationTests;

/// <summary>CLAUDE.md mục 21 — Preset cách chia hay dùng.</summary>
public sealed class SplitPresetServiceTests
{
    private static async Task<(TestHarness Harness, Guid OwnerId, Guid MemberId, GroupDto Group)> SetupAsync()
    {
        var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var memberId = await harness.RegisterUserAsync("b@example.com", "Binh");
        await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(memberId, "Binh"), CancellationToken.None);
        group = await harness.GroupService.GetByIdAsync(ownerId, group.Id, CancellationToken.None); // nap lai de co du 2 thanh vien
        return (harness, ownerId, memberId, group);
    }

    [Fact]
    public async Task CreateAsync_EqualMode_PersistsAndRoundTripsSplitConfig()
    {
        var (harness, ownerId, _, group) = await SetupAsync();
        var ownerMemberId = group.Members[0].Id;
        var binhMemberId = group.Members[1].Id;

        var preset = await harness.SplitPresetService.CreateAsync(ownerId, group.Id, new CreateSplitPresetRequest(
            "Toi & Binh chia doi", "Equal", new SplitConfigInput(MemberIds: [ownerMemberId, binhMemberId])), CancellationToken.None);

        preset.Name.Should().Be("Toi & Binh chia doi");
        preset.SplitMode.Should().Be("Equal");
        preset.SplitConfig.MemberIds.Should().BeEquivalentTo([ownerMemberId, binhMemberId]);
    }

    [Fact]
    public async Task GetByGroupIdAsync_ReturnsNewestFirst()
    {
        var (harness, ownerId, _, group) = await SetupAsync();
        var ownerMemberId = group.Members[0].Id;
        await harness.SplitPresetService.CreateAsync(ownerId, group.Id, new CreateSplitPresetRequest(
            "Preset 1", "Equal", new SplitConfigInput(MemberIds: [ownerMemberId])), CancellationToken.None);
        await harness.SplitPresetService.CreateAsync(ownerId, group.Id, new CreateSplitPresetRequest(
            "Preset 2", "Equal", new SplitConfigInput(MemberIds: [ownerMemberId])), CancellationToken.None);

        var presets = await harness.SplitPresetService.GetByGroupIdAsync(ownerId, group.Id, CancellationToken.None);

        presets.Should().HaveCount(2);
        presets[0].Name.Should().Be("Preset 2"); // moi tao truoc
        presets[1].Name.Should().Be("Preset 1");
    }

    [Fact]
    public async Task CreateAsync_ReferencesMemberNotInGroup_ThrowsMemberNotInGroup()
    {
        var (harness, ownerId, _, group) = await SetupAsync();
        var strangerMemberId = Guid.NewGuid();

        var act = () => harness.SplitPresetService.CreateAsync(ownerId, group.Id, new CreateSplitPresetRequest(
            "Preset", "Equal", new SplitConfigInput(MemberIds: [strangerMemberId])), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.MemberNotInGroup);
    }

    [Fact]
    public async Task CreateAsync_ReferencesInactiveMember_ThrowsMemberNotActive()
    {
        var (harness, ownerId, memberId, group) = await SetupAsync();
        var binhMemberId = group.Members[1].Id;
        await harness.GroupService.RemoveMemberAsync(ownerId, group.Id, binhMemberId, CancellationToken.None); // net = 0, roi duoc

        var act = () => harness.SplitPresetService.CreateAsync(ownerId, group.Id, new CreateSplitPresetRequest(
            "Preset", "Equal", new SplitConfigInput(MemberIds: [binhMemberId])), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.MemberNotActive);
    }

    [Fact]
    public async Task DeleteAsync_ByAuthor_Succeeds()
    {
        var (harness, ownerId, memberId, group) = await SetupAsync();
        var ownerMemberId = group.Members[0].Id;
        var preset = await harness.SplitPresetService.CreateAsync(memberId, group.Id, new CreateSplitPresetRequest(
            "Cua Binh", "Equal", new SplitConfigInput(MemberIds: [ownerMemberId])), CancellationToken.None);

        await harness.SplitPresetService.DeleteAsync(memberId, preset.Id, CancellationToken.None);

        var presets = await harness.SplitPresetService.GetByGroupIdAsync(ownerId, group.Id, CancellationToken.None);
        presets.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_ByOwnerNotAuthor_Succeeds()
    {
        var (harness, ownerId, memberId, group) = await SetupAsync();
        var ownerMemberId = group.Members[0].Id;
        var preset = await harness.SplitPresetService.CreateAsync(memberId, group.Id, new CreateSplitPresetRequest(
            "Cua Binh", "Equal", new SplitConfigInput(MemberIds: [ownerMemberId])), CancellationToken.None);

        await harness.SplitPresetService.DeleteAsync(ownerId, preset.Id, CancellationToken.None);

        var presets = await harness.SplitPresetService.GetByGroupIdAsync(ownerId, group.Id, CancellationToken.None);
        presets.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_ByOtherMemberNotAuthorNotOwner_ThrowsInsufficientRole()
    {
        var (harness, ownerId, memberId, group) = await SetupAsync();
        var ownerMemberId = group.Members[0].Id;
        var preset = await harness.SplitPresetService.CreateAsync(ownerId, group.Id, new CreateSplitPresetRequest(
            "Cua Nam", "Equal", new SplitConfigInput(MemberIds: [ownerMemberId])), CancellationToken.None);

        var act = () => harness.SplitPresetService.DeleteAsync(memberId, preset.Id, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.InsufficientRole);
    }
}
