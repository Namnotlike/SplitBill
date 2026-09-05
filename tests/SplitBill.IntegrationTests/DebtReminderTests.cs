using FluentAssertions;
using SplitBill.Application.Groups;
using SplitBill.Application.Reminders;
using SplitBill.Application.Settlements;
using SplitBill.Domain.Enums;
using Xunit;

namespace SplitBill.IntegrationTests;

/// <summary>CLAUDE.md mục 15.8 — Nhắc nợ tự động (phạm vi: Settlement Pending quá lâu).</summary>
public sealed class DebtReminderTests
{
    private static async Task<(TestHarness Harness, Guid OwnerId, Guid GuestUserId, GroupDto Group, Guid OwnerMemberId, Guid GuestMemberId)> SetupGroupAsync()
    {
        var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var guestUserId = await harness.RegisterUserAsync("b@example.com", "Binh");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var guestMember = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(guestUserId, "Binh"), CancellationToken.None);
        return (harness, ownerId, guestUserId, group, group.Members[0].Id, guestMember.Id);
    }

    /// <summary>Lùi CreatedAt của settlement về quá khứ — mô phỏng "đã Pending N ngày" mà không phải
    /// chờ thời gian thật (truy cập thẳng DbContext, đã có tiền lệ ở SettlementRecordServiceTests).</summary>
    private static async Task BackdateCreatedAtAsync(TestHarness harness, Guid settlementId, DateTimeOffset createdAt)
    {
        var settlement = harness.DbContext.Settlements.Single(s => s.Id == settlementId);
        settlement.CreatedAt = createdAt;
        await harness.DbContext.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunDueRemindersAsync_PendingOlderThanInterval_SendsReminderToToMember()
    {
        var (harness, ownerId, guestUserId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var now = DateTimeOffset.UtcNow;
        // Owner ghi nhận đã chuyển tiền cho Binh -> Binh (ToMember) là người cần xác nhận.
        var settlement = await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(ownerMemberId, guestMemberId, 100_000, null), CancellationToken.None);
        await BackdateCreatedAtAsync(harness, settlement.Id, now - DebtReminderRunner.ReminderInterval - TimeSpan.FromHours(1));
        // CreateAsync tự gửi thông báo "SettlementRecorded" riêng (CLAUDE.md mục 13) — bỏ qua để chỉ
        // đo đúng những gì DebtReminderRunner tạo ra, giống mẫu Clear() đã dùng ở các test khác trong
        // bộ test này.
        harness.EmailSender.SentEmails.Clear();

        var sentCount = await harness.DebtReminderRunner.RunDueRemindersAsync(now, CancellationToken.None);

        sentCount.Should().Be(1);
        var page = await harness.NotificationService.GetPagedAsync(guestUserId, 1, 20, CancellationToken.None);
        page.Items.Should().ContainSingle(n => n.Type == "SettlementReminder");
        harness.EmailSender.SentEmails.Should().ContainSingle(e => e.ToEmail == "b@example.com");
    }

    [Fact]
    public async Task RunDueRemindersAsync_RecentlyCreated_DoesNotSendReminder()
    {
        var (harness, ownerId, guestUserId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var now = DateTimeOffset.UtcNow;
        await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(ownerMemberId, guestMemberId, 100_000, null), CancellationToken.None);
        // Không backdate -> CreatedAt = now, chưa đủ ReminderInterval.

        var sentCount = await harness.DebtReminderRunner.RunDueRemindersAsync(now, CancellationToken.None);

        sentCount.Should().Be(0);
        var page = await harness.NotificationService.GetPagedAsync(guestUserId, 1, 20, CancellationToken.None);
        page.Items.Should().NotContain(n => n.Type == "SettlementReminder");
    }

    [Fact]
    public async Task RunDueRemindersAsync_AlreadyConfirmed_NeverReminded()
    {
        var (harness, ownerId, guestUserId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var now = DateTimeOffset.UtcNow;
        var settlement = await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(ownerMemberId, guestMemberId, 100_000, null), CancellationToken.None);
        await BackdateCreatedAtAsync(harness, settlement.Id, now - DebtReminderRunner.ReminderInterval - TimeSpan.FromHours(1));
        await harness.SettlementRecordService.ConfirmAsync(guestUserId, settlement.Id, CancellationToken.None);

        var sentCount = await harness.DebtReminderRunner.RunDueRemindersAsync(now, CancellationToken.None);

        sentCount.Should().Be(0);
        var page = await harness.NotificationService.GetPagedAsync(guestUserId, 1, 20, CancellationToken.None);
        page.Items.Should().NotContain(n => n.Type == "SettlementReminder");
    }

    [Fact]
    public async Task RunDueRemindersAsync_CalledTwiceRightAfter_OnlyNotifiesOnce()
    {
        var (harness, ownerId, guestUserId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var now = DateTimeOffset.UtcNow;
        var settlement = await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(ownerMemberId, guestMemberId, 100_000, null), CancellationToken.None);
        await BackdateCreatedAtAsync(harness, settlement.Id, now - DebtReminderRunner.ReminderInterval - TimeSpan.FromHours(1));

        var firstRunCount = await harness.DebtReminderRunner.RunDueRemindersAsync(now, CancellationToken.None);
        var secondRunCount = await harness.DebtReminderRunner.RunDueRemindersAsync(now, CancellationToken.None);

        firstRunCount.Should().Be(1);
        secondRunCount.Should().Be(0);
        var page = await harness.NotificationService.GetPagedAsync(guestUserId, 1, 20, CancellationToken.None);
        page.Items.Where(n => n.Type == "SettlementReminder").Should().ContainSingle();
    }

    [Fact]
    public async Task RunDueRemindersAsync_AfterAnotherFullInterval_SendsAgain()
    {
        var (harness, ownerId, guestUserId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var now = DateTimeOffset.UtcNow;
        var settlement = await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(ownerMemberId, guestMemberId, 100_000, null), CancellationToken.None);
        await BackdateCreatedAtAsync(harness, settlement.Id, now - DebtReminderRunner.ReminderInterval - TimeSpan.FromHours(1));

        await harness.DebtReminderRunner.RunDueRemindersAsync(now, CancellationToken.None);
        // Vẫn Pending, đã đủ 1 ReminderInterval kể từ lần nhắc trước -> phải nhắc lại (không phải chỉ
        // nhắc đúng 1 lần duy nhất trong toàn bộ vòng đời settlement).
        var laterCount = await harness.DebtReminderRunner.RunDueRemindersAsync(now + DebtReminderRunner.ReminderInterval + TimeSpan.FromHours(1), CancellationToken.None);

        laterCount.Should().Be(1);
        var page = await harness.NotificationService.GetPagedAsync(guestUserId, 1, 20, CancellationToken.None);
        page.Items.Count(n => n.Type == "SettlementReminder").Should().Be(2);
    }

    [Fact]
    public async Task RunDueRemindersAsync_GuestToMember_NoNotificationButMarkedProcessed()
    {
        var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var ownerMemberId = group.Members[0].Id;
        var guestMember = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(null, "Khach"), CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var settlement = await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(ownerMemberId, guestMember.Id, 50_000, null), CancellationToken.None);
        await BackdateCreatedAtAsync(harness, settlement.Id, now - DebtReminderRunner.ReminderInterval - TimeSpan.FromHours(1));
        harness.EmailSender.SentEmails.Clear();

        var sentCount = await harness.DebtReminderRunner.RunDueRemindersAsync(now, CancellationToken.None);

        sentCount.Should().Be(0);
        harness.EmailSender.SentEmails.Should().BeEmpty();
        // Mốc LastReminderSentAt PHẢI được cập nhật dù không gửi được gì — nếu không, settlement này sẽ
        // mãi bị coi là "chưa từng nhắc" và bị GetPendingDueForReminderAsync quét lại vô ích mỗi lượt.
        harness.DbContext.Settlements.Single(s => s.Id == settlement.Id).LastReminderSentAt.Should().Be(now);
    }
}
