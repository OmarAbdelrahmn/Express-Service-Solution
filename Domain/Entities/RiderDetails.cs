using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Domain.Entities;

public class RiderDetails
{
    public int Id { get; set; }
    public string? WorkingId { get; set; }
    public long EmployeeIqamaNo { get; set; }
    public string? TshirtSize { get; set; }
    public string? LicenseNumber { get; set; }
    public int CompanyId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public Company Company { get; set; } = default!;
    public Employees Employee { get; set; } = default!;
    public ICollection<RiderShift> RiderShifts { get; set; } = [];
                   
    public string? VehicleNumber { get; set; }
    public Vehicle? Vehicle { get; set; } 
}
