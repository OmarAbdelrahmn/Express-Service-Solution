using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities.Spare;

public class SparePartUsage
{

    public int Id { get; set; }
    public int SparePartId { get; set; }
    public SparePart SparePart { get; set; } = default!;

    public string VehicleNumber { get; set; } = string.Empty;
    public Vehicle Vehicle { get; set; } = default!;

    public int QuantityUsed { get; set; }
    public DateTime UsedAt { get; set; } = DateTime.UtcNow.AddHours(3);
}
