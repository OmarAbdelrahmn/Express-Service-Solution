using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace Domain.Entities;

public class RiderVehicleStatus
{
    public int Id { get; set; }

    public long? EmployeeIqamaNo { get; set; }
    public string VehicleNumber { get; set; } = string.Empty;
    public Vehicle Vehicle { get; set; } = default!;

    public VehicleStatusType StatusType { get; set; }
    public string? Reason { get; set; }

    public string? Permission { get; set; }
    public DateTime? PermissionStartDate { get; set; }
    public DateTime? PermissionEndDate { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow.AddHours(3);

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