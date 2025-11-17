using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities;

public class RiderShift
{ 
    public int Id { get; set; }
    public int RiderId { get; set; }
    public int IqamaNo { get; set; }
    public string WorkingId { get; set; } = null!;
    public DateOnly ShiftDate { get; set; }
    public int DailyOrders { get; set; }
    public string ShiftStatus => DailyOrders > 14 ? "Active" : "Inactive";

    public RiderDetails Rider { get; set; } = null!;

}
//RiderId: 1, WorkingId: WD-1001, TotalOrders: 25
//RiderId: 1, WorkingId: WD-2002, TotalOrders: 20