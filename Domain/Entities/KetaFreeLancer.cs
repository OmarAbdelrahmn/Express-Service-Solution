namespace Domain.Entities;

public class KetaFreeLancer
{
    public int Id { get; set; }
    public int RiderId { get; set; }
    public string WorkingId { get; set; } = string.Empty;
    public string Month { get; set; } = string.Empty;
    public int TotalOrders { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);

    public RiderDetails Rider { get; set; } = default!;

}
