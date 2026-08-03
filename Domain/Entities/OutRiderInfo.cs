namespace Domain.Entities;

public class OutRiderInfo
{
    public int Id { get; set; }
    public string RiderId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public string? CreatedBy { get; set; }
    public List<OutageShiftPerformance> OutageShiftPerformances { get; set; } = [];
}
