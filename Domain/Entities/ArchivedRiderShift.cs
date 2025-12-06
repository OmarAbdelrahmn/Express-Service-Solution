using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities;

public class ArchivedRiderShift
{
    public int id { get; set; }
    public int RiderId { get; set; }
    public string WorkingId { get; set; }
    public DateOnly ShiftDate { get; set; }
    public int DailyOrders { get; set; }
    public string ShiftStatus { get; set; } = string.Empty;
}
