using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities;

public class RiderShift
{ 
    public int RiderId { get; set; }
    public int WorkingId { get; set; } 
    public DateOnly ShiftDate { get; set; }
    public int DailyOrders { get; set; }
    public int CompanyId { get; set; }
    public string ShiftStatus { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public RiderDetails Rider { get; set; } = default!;

}
//RiderId: 1, WorkingId: WD-1001, TotalOrders: 25
//RiderId: 1, WorkingId: WD-2002, TotalOrders: 20