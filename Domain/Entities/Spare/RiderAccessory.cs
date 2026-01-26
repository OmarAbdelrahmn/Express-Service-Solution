using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities.Spare;

public class RiderAccessory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public string Location { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);

    public ICollection<RiderAccessoryUsage> RiderAccessoryUsages { get; set; } = [];
}