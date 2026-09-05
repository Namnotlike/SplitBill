using SplitBill.Application.Notifications;

namespace SplitBill.IntegrationTests;

/// <summary>Fake IEmailSender ghi lại mọi email "đã gửi" để test assert — không dùng thư viện mocking
/// (không có trong danh sách NuGet được phép, CLAUDE.md mục 2).</summary>
public sealed class FakeEmailSender : IEmailSender
{
    public List<(string ToEmail, string Subject, string HtmlBody)> SentEmails { get; } = new();

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        SentEmails.Add((toEmail, subject, htmlBody));
        return Task.CompletedTask;
    }
}
