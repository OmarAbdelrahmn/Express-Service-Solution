using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities;

public class RiderShift
{ 
    public int RiderId { get; set; }
    public int WorkingId { get; set; } 
    public DateOnly ShiftDate { get; set; }
    public int AcceptedDailyOrders { get; set; }
    public int RejectedDailyOrders { get; set; }
    public int RealRejectedDailyOrders { get; set; }
    public float WorkingHours { get; set; }
    public int CompanyId { get; set; }
    public string ShiftStatus { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public RiderDetails Rider { get; set; } = default!;
    public Company Company { get; set; } = default!;
}
