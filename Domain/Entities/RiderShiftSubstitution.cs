using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities;

public class RiderShiftSubstitution
{
    public int Id { get; set; }
    public string OriginalWorkingId { get; set; } = null!;  // Assigned rider
    public string SubstituteWorkingId { get; set; } = null!;  // Actual rider
    public DateOnly ShiftDate { get; set; }
    public string Reason { get; set; } = string.Empty;  // "Sick", "Leave", "Cover", etc.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; } = null!;
}
