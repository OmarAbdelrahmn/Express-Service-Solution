using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities;

public class HungerDisability
{
    public int Id { get; set; }
    public int ActualRiderId { get; set; }
    public string ActualWorkingId { get; set; } = string.Empty;
    public int? SubstituteRiderId { get; set; }
    public string? SubstituteWorkingId { get; set; }
    public int Days { get; set; }
    public DateOnly ShiftDate { get; set; }
    public int CompanyId { get; set; }
    public int AcceptedDailyOrders { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public RiderDetails Rider { get; set; } = default!;
    public Company Company { get; set; } = default!;

}
