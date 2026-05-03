namespace Application.Service.DailyReport;

public class DailyReportSettings
{
    public List<string> RecipientEmails { get; set; } = [];
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string SmtpUser { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public bool UseSsl { get; set; } = true;
    // In DailyReportSettings.cs — optional addition
    public string ImapHost { get; set; } = string.Empty; // fallback to SmtpHost if empty
}