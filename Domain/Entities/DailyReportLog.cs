namespace Domain.Entities;

public class DailyReportLog
{
    public int Id { get; set; }
    public DateOnly ReportDate { get; set; }       // The date of the data (yesterday)
    public DateTime SentAt { get; set; }
    public bool IsSent { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
}