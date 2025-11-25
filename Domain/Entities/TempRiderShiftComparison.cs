using System;

namespace Domain.Entities;

public class TempRiderShiftComparison
{
    public int Id { get; set; }

    public int RiderId { get; set; }  
    public int WorkingId { get; set; } 
    public DateOnly ShiftDate { get; set; }
    public int CompanyId { get; set; }

    public bool IsSubstitution { get; set; }  
    public int? OriginalRiderWorkingId { get; set; }  

    public int? OldAcceptedDailyOrders { get; set; }
    public int? OldRejectedDailyOrders { get; set; }
    public int? OldRealRejectedDailyOrders { get; set; }
    public int? OldStackedDeliveries { get; set; }
    public float? OldWorkingHours { get; set; }
    public string? OldShiftStatus { get; set; }
    public DateTime? OldCreatedAt { get; set; }

    public int NewAcceptedDailyOrders { get; set; }
    public int NewRejectedDailyOrders { get; set; }
    public int NewRealRejectedDailyOrders { get; set; }
    public int NewStackedDeliveries { get; set; }
    public float NewWorkingHours { get; set; }
    public string NewShiftStatus { get; set; } = string.Empty;

    public DateTime UploadedAt { get; set; }
    public bool IsResolved { get; set; }

    public RiderDetails? Rider { get; set; } = null!;
    public Company? Company { get; set; } = null!;
}


public record ShiftComparisonResponse(
    int RiderId,
    int WorkingId,
    DateOnly ShiftDate,
    string RiderNameEN,
    string RiderNameAR,
    string CompanyName,
    int DailyOrderTarget,
    bool IsSubstitution,  
    int? OriginalRiderWorkingId,  
    string SubstitutionNote, 
    ShiftComparisonData OldData,
    ShiftComparisonData NewData,
    ComparisonAnalysis Analysis
);

public record ShiftComparisonData(
    int? AcceptedOrders,
    int? RejectedOrders,
    int? RealRejectedOrders,
    int? StackedDeliveries,
    float? WorkingHours,
    string? ShiftStatus,
    bool? HasRejectionProblem,
    decimal? PenaltyAmount,
    DateTime? RecordedAt
);

public record ComparisonAnalysis(
    bool HasChanges,
    int OrdersDifference,
    int RejectionsDifference,
    float HoursDifference,
    string StatusChange,
    decimal PenaltyDifference,
    string Recommendation,
    int? StackedDeliveries

);



public record ResolveComparisonsRequest(
    DateOnly ShiftDate,
    ResolutionChoice Choice,
    string ResolvedBy
);

public enum ResolutionChoice
{
    KeepOld = 1,      // Keep existing database data
    UseNew = 2,       // Replace with new Excel data
    KeepBoth = 3     // Keep old, add new as separate entry (optional)
}

public record ResolutionResult(
    int TotalResolved,
    int UpdatedShifts,
    int NewShiftsAdded,
    int UnchangedShifts,
    List<string> Details
);