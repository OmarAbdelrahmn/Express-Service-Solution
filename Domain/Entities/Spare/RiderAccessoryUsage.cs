using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities.Spare;

public class RiderAccessoryUsage
{
    public int Id { get; set; }
    public int RiderAccessoryId { get; set; }
    public RiderAccessory RiderAccessory { get; set; } = default!;

    public int RiderId { get; set; }
    public RiderDetails Rider { get; set; } = default!;

    [Column(TypeName = "decimal(18,2)")]
    public decimal? Cost { get; set; }
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public string? Location { get; set; }
}
