using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities;

public class RiderDetails
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? WorkingId { get; set; }
    public string? TshirtSize { get; set; }
    public string? LicenseNumber { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;


    public Employees User { get; set; } = default!;
    public ICollection<RiderShift> RiderShifts { get; set; } = [];

}
