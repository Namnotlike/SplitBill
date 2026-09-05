namespace SplitBill.Infrastructure.Email;

/// <summary>Cấu hình SMTP (CLAUDE.md mục 13.3). Host rỗng/chưa cấu hình -> dùng ConsoleEmailSender thay vì gửi thật.</summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = "noreply@splitbill.local";
    public string FromName { get; set; } = "SplitBill";
    public bool UseSsl { get; set; } = true;
}
