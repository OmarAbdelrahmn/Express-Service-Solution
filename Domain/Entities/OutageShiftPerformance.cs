namespace Domain.Entities;

public class OutageShiftPerformance
{
    public int Id { get; set; }
    public string SystemId { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public DateOnly ShiftDate { get; set; }
    public int AcceptedOrders { get; set; }
    public int RejectedOrders { get; set; }
    public float WorkingHours { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public string? UploadedBy { get; set; }
}
