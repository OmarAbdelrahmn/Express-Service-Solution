using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities;

public class RiderDetails
{
    public int Id { get; set; }
    public int? WorkingId { get; set; }
    public int EmployeeIqamaNo { get; set; }
    public string? TshirtSize { get; set; }
    public string? LicenseNumber { get; set; }
    public int CompanyId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public Company Company { get; set; } = default!;
    public Employees Employee { get; set; } = default!;
    public ICollection<RiderShift> RiderShifts { get; set; } = [];
                   
    public string? VehicleNumber { get; set; }
    public Vehicle? Vehicle { get; set; } 
}
