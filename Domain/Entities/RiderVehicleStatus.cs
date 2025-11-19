using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities;

public class RiderVehicleStatus
{
    public int Id { get; set; }

    public int? EmployeeIqamaNo { get; set; }

    public string VehicleNumber { get; set; } = string.Empty;
    public Vehicle Vehicle { get; set; } = default!;

    public VehicleStatusType StatusType { get; set; }   // Take, Stop, Problem
    public string? Reason { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.Now;

    public bool IsActive { get; set; }


}

public enum VehicleStatusType
{
    Taken = 1,
    Returned = 2,
    Problem = 3,
    Stolen = 4,     
    BreakUp = 5
}
