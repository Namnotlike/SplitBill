namespace SplitBill.Application.Notifications;

/// <summary>
/// Gửi email (CLAUDE.md mục 13.3). Interface thuần — implementation thật (MailKit) hoặc fallback
/// (log ra console khi chưa cấu hình SMTP) nằm ở SplitBill.Infrastructure.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken);
}
