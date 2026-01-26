using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities.Spare;

public class RiderAccessoryUsage
{
    public int Id { get; set; }
    public int RiderAccessoryId { get; set; }
    public RiderAccessory RiderAccessory { get; set; } = default!;

    public int RiderId { get; set; }
    public RiderDetails Rider { get; set; } = default!;

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow.AddHours(3);
}
