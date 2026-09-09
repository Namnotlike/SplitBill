using FluentAssertions;
using SplitBill.Application.Groups;
using SplitBill.Domain.Exceptions;
using Xunit;

namespace SplitBill.IntegrationTests;

/// <summary>CLAUDE.md mục 25.6 — Mẫu nhóm tái sử dụng.</summary>
public sealed class GroupTemplateServiceTests
{
    [Fact]
    public async Task CreateAsync_PersistsTemplate_WithMemberNames()
    {
        var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");

        var template = await harness.GroupTemplateService.CreateAsync(userId, new CreateGroupTemplateRequest(
            "Nhom phuot cuoi tuan", "Hoi ban than", "OneTime", "VND", true, ["Binh", "Cuong"]), CancellationToken.None);

        template.Name.Should().Be("Nhom phuot cuoi tuan");
        template.MemberNames.Should().BeEquivalentTo(["Binh", "Cuong"]);
        template.Currency.Should().Be("VND");
    }

    [Fact]
    public async Task GetMyTemplatesAsync_OnlyReturnsCallersOwnTemplates()
    {
        var harness = TestHarness.Create();
        var userA = await harness.RegisterUserAsync("a@example.com", "Nam");
        var userB = await harness.RegisterUserAsync("b@example.com", "Binh");
        await harness.GroupTemplateService.CreateAsync(userA, new CreateGroupTemplateRequest(
            "Mau cua A", null, "OneTime", "VND", true, []), CancellationToken.None);
        await harness.GroupTemplateService.CreateAsync(userB, new CreateGroupTemplateRequest(
            "Mau cua B", null, "OneTime", "VND", true, []), CancellationToken.None);

        var templatesOfA = await harness.GroupTemplateService.GetMyTemplatesAsync(userA, CancellationToken.None);

        templatesOfA.Should().ContainSingle().Which.Name.Should().Be("Mau cua A");
    }

    [Fact]
    public async Task CreateGroupFromTemplateAsync_CreatesGroupWithGuestMembersAndSettings()
    {
        var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var template = await harness.GroupTemplateService.CreateAsync(userId, new CreateGroupTemplateRequest(
            "Nhom phuot", "Hoi ban than", "OneTime", "USD", false, ["Binh", "Cuong"]), CancellationToken.None);

        var group = await harness.GroupTemplateService.CreateGroupFromTemplateAsync(
            userId, template.Id, new CreateGroupFromTemplateRequest(null), CancellationToken.None);

        group.Name.Should().Be("Nhom phuot"); // GroupName null -> dung nguyen ten mau
        group.Currency.Should().Be("USD");
        group.SimplifyDebts.Should().BeFalse(); // mau tat SimplifyDebts -> phai duoc ap dung
        group.Members.Should().HaveCount(3); // Owner (Nam) + 2 khach vang lai tu mau
        group.Members.Should().Contain(m => m.DisplayName == "Binh" && m.UserId == null);
        group.Members.Should().Contain(m => m.DisplayName == "Cuong" && m.UserId == null);
        group.Members.Should().Contain(m => m.DisplayName == "Nam" && m.Role == "Owner");
    }

    [Fact]
    public async Task CreateGroupFromTemplateAsync_WithOverrideName_UsesOverride()
    {
        var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var template = await harness.GroupTemplateService.CreateAsync(userId, new CreateGroupTemplateRequest(
            "Ten mau", null, "OneTime", "VND", true, []), CancellationToken.None);

        var group = await harness.GroupTemplateService.CreateGroupFromTemplateAsync(
            userId, template.Id, new CreateGroupFromTemplateRequest("Chuyen di 2027"), CancellationToken.None);

        group.Name.Should().Be("Chuyen di 2027");
    }

    [Fact]
    public async Task DeleteAsync_TemplateStillDeletedAfterSourceGroupDeleted_UnrelatedToAnyGroupLifecycle()
    {
        // Xác nhận đúng thiết kế mục 25.6: mẫu KHÔNG phụ thuộc vào 1 Group cụ thể nào còn tồn tại hay
        // không — khác "Nhân bản nhóm" (mục 18) vốn cần nhóm nguồn đang sống. Ở đây chỉ cần xác nhận
        // template không tham chiếu Group nào cả nên vẫn tạo được nhóm mới dù không có "nhóm gốc".
        var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var template = await harness.GroupTemplateService.CreateAsync(userId, new CreateGroupTemplateRequest(
            "Mau doc lap", null, "OneTime", "VND", true, ["Duy"]), CancellationToken.None);

        // Tạo và xóa hẳn 1 nhóm khác không liên quan, xác nhận không ảnh hưởng gì tới việc dùng mẫu.
        var otherGroup = await harness.GroupService.CreateAsync(userId, new CreateGroupRequest("Nhom khac", null, "OneTime", "VND"), CancellationToken.None);
        await harness.GroupService.DeleteAsync(userId, otherGroup.Id, CancellationToken.None);

        var group = await harness.GroupTemplateService.CreateGroupFromTemplateAsync(
            userId, template.Id, new CreateGroupFromTemplateRequest(null), CancellationToken.None);

        group.Members.Should().Contain(m => m.DisplayName == "Duy");
    }

    [Fact]
    public async Task DeleteAsync_ByOwner_RemovesFromList()
    {
        var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var template = await harness.GroupTemplateService.CreateAsync(userId, new CreateGroupTemplateRequest(
            "Xoa toi", null, "OneTime", "VND", true, []), CancellationToken.None);

        await harness.GroupTemplateService.DeleteAsync(userId, template.Id, CancellationToken.None);

        var templates = await harness.GroupTemplateService.GetMyTemplatesAsync(userId, CancellationToken.None);
        templates.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_ByOtherUser_ThrowsGroupTemplateNotFound()
    {
        var harness = TestHarness.Create();
        var owner = await harness.RegisterUserAsync("a@example.com", "Nam");
        var stranger = await harness.RegisterUserAsync("b@example.com", "La");
        var template = await harness.GroupTemplateService.CreateAsync(owner, new CreateGroupTemplateRequest(
            "Cua Nam", null, "OneTime", "VND", true, []), CancellationToken.None);

        var act = () => harness.GroupTemplateService.DeleteAsync(stranger, template.Id, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.GroupTemplateNotFound);
    }

    [Fact]
    public async Task CreateGroupFromTemplateAsync_ByOtherUser_ThrowsGroupTemplateNotFound()
    {
        var harness = TestHarness.Create();
        var owner = await harness.RegisterUserAsync("a@example.com", "Nam");
        var stranger = await harness.RegisterUserAsync("b@example.com", "La");
        var template = await harness.GroupTemplateService.CreateAsync(owner, new CreateGroupTemplateRequest(
            "Cua Nam", null, "OneTime", "VND", true, []), CancellationToken.None);

        var act = () => harness.GroupTemplateService.CreateGroupFromTemplateAsync(
            stranger, template.Id, new CreateGroupFromTemplateRequest(null), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.GroupTemplateNotFound);
    }
}
