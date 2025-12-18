using System;

namespace Domain.Entities;

public class RiderShiftSubstitution
{
    public int Id { get; set; }

    // Make nullable - might not have a current rider
    public int? ActualRiderId { get; set; }

    // This is the primary key for lookups
    public string ActualRiderWorkingId { get; set; } = string.Empty;

    // Track original rider for history
    public long? OriginalRiderIqamaNo { get; set; }

    public int SubstituteRiderId { get; set; }
    public string SubstituteWorkingId { get; set; } = string.Empty;

    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Reason { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    // Navigation properties
    public RiderDetails? ActualRider { get; set; }
    public RiderDetails SubstituteRider { get; set; } = default!;
}