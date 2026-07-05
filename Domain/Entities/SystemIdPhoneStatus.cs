namespace Domain.Entities;

public class SystemIdPhoneStatus
{
    public int Id { get; set; }
    public string SystemId { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public DateOnly StatusDate { get; set; }
    public string? Status { get; set; }
    public string? RawStatus { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public string? UploadedBy { get; set; }
}
