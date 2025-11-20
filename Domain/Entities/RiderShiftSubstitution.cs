using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities;

public class RiderShiftSubstitution
{
    public int Id { get; set; }
    public int ActualRiderId { get; set; } //the one who is working
    public int SubstituteWorkingId { get; set; } //usied to work
    public string? Reason { get; set; } = string.Empty;  // "Sick", "Leave", "Cover
    public DateTime StartDate { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? EndDate { get; set; }
    public RiderDetails ActualRider { get; set; } = default!;

}
