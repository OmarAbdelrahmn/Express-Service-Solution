namespace Domain.Entities.Keeta;

public enum KeetaBreakRoundingPolicy { Floor = 1, Ceiling = 2, Nearest = 3 }
public enum KeetaBreakBatchStatus { Imported = 1, Draft = 2, Confirmed = 3, Superseded = 4 }
public enum KeetaBreakAssignmentStatus { Planned = 1, Confirmed = 2, Removed = 3 }

/// <summary>Immutable, date-effective staffing policy used by break-schedule batches.</summary>
public class KeetaBreakConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Version { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public decimal BreakPercentage { get; set; } = 5m;
    public KeetaBreakRoundingPolicy RoundingPolicy { get; set; } = KeetaBreakRoundingPolicy.Floor;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public string CreatedBy { get; set; } = string.Empty;
    public ICollection<KeetaBreakShiftDefinition> ShiftDefinitions { get; set; } = [];
    public ICollection<KeetaBreakShiftPattern> ShiftPatterns { get; set; } = [];
}

public class KeetaBreakShiftDefinition
{
    public int Id { get; set; }
    public Guid ConfigurationId { get; set; }
    public KeetaBreakConfiguration Configuration { get; set; } = null!;
    public string ShiftKey { get; set; } = string.Empty;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int MinimumRiders { get; set; }
    public int MaximumRiders { get; set; }
}

/// <summary>A normalized rider shift combination, such as 00:00-03:00 + 16:00-20:00 + 20:00-00:00.</summary>
public class KeetaBreakShiftPattern
{
    public int Id { get; set; }
    public Guid ConfigurationId { get; set; }
    public KeetaBreakConfiguration Configuration { get; set; } = null!;
    public string PatternKey { get; set; } = string.Empty;
    public string ShiftKeysJson { get; set; } = "[]";
    public int RiderCount { get; set; }
}

public class KeetaBreakBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConfigurationId { get; set; }
    public KeetaBreakConfiguration Configuration { get; set; } = null!;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public KeetaBreakBatchStatus Status { get; set; } = KeetaBreakBatchStatus.Imported;
    public string SourceFileName { get; set; } = string.Empty;
    public string ImportedBy { get; set; } = string.Empty;
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<KeetaBreakImportedRider> Riders { get; set; } = [];
    public ICollection<KeetaBreakAssignment> Assignments { get; set; } = [];
}

public class KeetaBreakImportedRider
{
    public long Id { get; set; }
    public Guid BatchId { get; set; }
    public KeetaBreakBatch Batch { get; set; } = null!;
    public string RiderNumber { get; set; } = string.Empty;
    public string RiderIdentifier { get; set; } = string.Empty;
    public string RiderName { get; set; } = string.Empty;
    public string? HousingGroup { get; set; }
    public string? Notes { get; set; }
    public string ShiftsJson { get; set; } = "[]";
    public string? ValidationError { get; set; }
}

/// <summary>A single whole-day rider break. Confirmed rows are the historical source of monthly counts.</summary>
public class KeetaBreakAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BatchId { get; set; }
    public KeetaBreakBatch Batch { get; set; } = null!;
    public string RiderIdentifier { get; set; } = string.Empty;
    public DateOnly BreakDate { get; set; }
    public KeetaBreakAssignmentStatus Status { get; set; } = KeetaBreakAssignmentStatus.Planned;
    public string AssignedShiftsJson { get; set; } = "[]";
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
}
