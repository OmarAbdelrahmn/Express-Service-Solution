namespace Domain.Entities;

public class EmployeeStatusLog
{
    public int Id { get; set; }

    public long EmployeeIqamaNo { get; set; }
    public Employees Employee { get; set; } = default!;

    public string OldStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;

    public string ChangedBy { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow.AddHours(3);

    public string? Reason { get; set; }

    /// <summary>"StatusRequest" (went through approval) or "DirectUpdate" (rider update)</summary>
    public string ChangeSource { get; set; } = string.Empty;
}