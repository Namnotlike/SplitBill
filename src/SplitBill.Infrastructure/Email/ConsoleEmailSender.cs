using Microsoft.Extensions.Logging;
using SplitBill.Application.Notifications;

namespace SplitBill.Infrastructure.Email;

/// <summary>
/// Fallback khi chưa cấu hình SMTP (<see cref="SmtpOptions.Host"/> rỗng) — chỉ ghi log nội dung email
/// thay vì gửi thật (CLAUDE.md mục 13.3, quyết định người dùng 2026-09-05: không fail-fast như
/// Jwt:SigningKey, môi trường dev/test chưa có SMTP thật thì âm thầm chuyển sang log).
/// </summary>
public sealed class ConsoleEmailSender : IEmailSender
{
    private readonly ILogger<ConsoleEmailSender> _logger;

    public ConsoleEmailSender(ILogger<ConsoleEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[EMAIL - chưa cấu hình SMTP, chỉ log] To: {ToEmail} | Subject: {Subject} | Body: {Body}",
            toEmail, subject, htmlBody);
        return Task.CompletedTask;
    }
}
