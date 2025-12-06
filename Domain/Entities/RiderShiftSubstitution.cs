using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities;

public class RiderShiftSubstitution
{
    public int Id { get; set; }
    public int ActualRiderId { get; set; }
    public string ActualRiderWorkingId { get; set; } // ✅ ADD THIS
    public int SubstituteRiderId { get; set; } // ✅ ADD THIS
    public string SubstituteWorkingId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Reason { get; set; }
    public string CreatedBy { get; set; }
    public bool IsActive { get; set; }

    public RiderDetails ActualRider { get; set; }
    public RiderDetails SubstituteRider { get; set; } // ✅ ADD THIS navigation
}