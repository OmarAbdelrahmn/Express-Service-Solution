using Domain.Entities;

namespace Application.Contracts.Employees;



public record SetReportedPathRequest(
    DateTime ReportedAt,
    string UpdatedBy,
    string? Notes = null
);

public record SetOutagePathRequest(
    DateTime DateOfOutage,
    string VisaNumber,
    string UpdatedBy,
    string? Notes = null
);

public record SwitchPathRequest(
    EscapedPath NewPath,
    DateTime? ReportedAt,       // required if NewPath == Reported
    DateTime? DateOfOutage,     // required if NewPath == Outage
    string? VisaNumber,         // required if NewPath == Outage
    string UpdatedBy,
    string? Notes = null
);

// ── Response Records ─────────────────────────────────────────────────────────

public record EscapedEmployeeSummaryResponse(
    long IqamaNo,
    string NameAR,
    string NameEN,
    string JobTitle,
    string? HousingName,
    DateOnly EscapedAt,
    EscapedPath ActivePath,
    DateTime? RemovalDeadline,
    int? RemainingDaysToRemoval,
    bool IsOverdue,
    bool TenDayNotificationSent,
    bool? IsActive
);

public record EscapedEmployeeDetailResponse(
    long IqamaNo,
    string NameAR,
    string NameEN,
    string JobTitle,
    string Country,
    string Phone,
    string? HousingName,
    string Sponsor,
    DateOnly EscapedAt,
    EscapedPath ActivePath,
    // Reported path
    bool? IsReported,
    DateTime? ReportedAt,
    // Outage path
    bool? IsOutage,
    DateTime? DateOfOutage,
    string? OutageVisaNumber,
    // Removal window
    DateTime? RemovalDeadline,
    int? RemainingDaysToRemoval,
    bool IsOverdue,
    bool TenDayNotificationSent,
    DateTime? TenDayNotificationSentAt,
    // Audit
    DateTime CreatedAt,
    string? CreatedBy,
    DateTime UpdatedAt,
    string? UpdatedBy,
    string? Notes
);

public record EscapedEmployeeStatsResponse(
    int TotalEscaped,
    int NonePathCount,
    int ReportedPathCount,
    int OutagePathCount,
    int OverdueCount,
    int DueWithin10DaysCount,
    int NotificationsSentCount
);

/// <summary>Create a new escaped-employee record (no path yet).</summary>
public record CreateEscapedEmployeeRequest(
    long EmployeeIqamaNo,
    DateOnly EscapedAt,
    string? Notes,
    string CreatedBy
);

/// <summary>Activate the Reported path (or re-activate after switching from Outage).</summary>
public record ActivateReportedPathRequest(
    long EmployeeIqamaNo,
    bool IsReported,
    DateTime ReportedAt,
    string UpdatedBy,
    string? Notes
);

/// <summary>Activate the Outage path (or re-activate after switching from Reported).</summary>
public record ActivateOutagePathRequest(
    long EmployeeIqamaNo,
    bool IsOutage,
    DateTime DateOfOutage,
    string OutageVisaNumber,
    string UpdatedBy,
    string? Notes
);

/// <summary>Patch general fields (EscapedAt, Notes) without touching path data.</summary>
public record UpdateEscapedEmployeeRequest(
    DateOnly? EscapedAt,
    string? Notes,
    string UpdatedBy
);

// ── Responses ─────────────────────────────────────────────────────────────────

public record EscapedEmployeeResponse(
    int Id,
    long EmployeeIqamaNo,
    string EmployeeNameAR,
    string EmployeeNameEN,
    string? HousingName,
    string? CompanyName,
    DateOnly EscapedAt,
    string ActivePath,

    // Reported path
    bool? IsReported,
    DateTime? ReportedAt,

    // Outage path
    bool? IsOutage,
    DateTime? DateOfOutage,
    string? OutageVisaNumber,

    // Shared deadline
    DateTime? RemovalDeadline,
    int? RemainingDaysToRemoval,
    bool IsOverdue,

    // Notification
    bool TenDayNotificationSent,
    DateTime? TenDayNotificationSentAt,

    // Audit
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? CreatedBy,
    string? UpdatedBy,
    string? Notes
);



// ── Notification payload (used internally by the job) ─────────────────────────

public record EscapedNotificationItem(
    long EmployeeIqamaNo,
    string EmployeeNameAR,
    string EmployeeNameEN,
    string? HousingName,
    DateOnly EscapedAt,
    string ActivePath,
    DateTime RemovalDeadline,
    int RemainingDays
);
public record EmpolyeeResponse(
    long IqamaNo,
    DateOnly IqamaEndM,
    DateOnly IqamaEndH,
    string PassportNo,
    DateOnly PassportEnd,
    long sponsorNo,
    string Sponsor,
    string JobTitle,
    string NameAR,
    string NameEN,
    string Country,
    string Phone,
    DateOnly DateOfBirth,
    string Status,
    string IBAN,
    bool INKSA,
    DateTime CreatedAt
    );
public record EmpolyeeRequest(
    long IqamaNo,
    DateOnly? IqamaEndM,
    DateOnly? IqamaEndH,
    string PassportNo,
    DateOnly? PassportEnd,
    string Sponsor,
    long sponsorNo,
    string JobTitle,
    string NameAR,
    string NameEN,
    string Country,
    string Phone,
    DateOnly? DateOfBirth,
    string Status,
    string IBAN,
    bool INKSA
    );
public record UEmpolyeeRequest(
    DateOnly? IqamaEndM,
    DateOnly? IqamaEndH,
    string? PassportNo,
    DateOnly? PassportEnd,
    string? Sponsor,
    long? sponsorNo,
    string? JobTitle,
    string? NameAR,
    string? NameEN,
    string? Country,
    string? Phone,
    DateOnly? DateOfBirth,
    string? Status,
    string? IBAN,
    bool? INKSA
    );
public record RiderResponse(
    long IqamaNo,
    bool IsEmployee,
    DateOnly IqamaEndM,
    DateOnly IqamaEndH,
    string PassportNo,
    DateOnly PassportEnd,
    string Sponsor,
    long sponsorNo,
    string JobTitle,
    string NameAR,
    string NameEN,
    string Country,
    string Phone,
    DateOnly DateOfBirth,
    string Status,
    string IBAN,
    bool INKSA,
    DateTime CreatedAt,
    string HousingAddress,
    string? WorkingId,
    long EmployeeIqamaNo,
    string? TshirtSize,
    string? LicenseNumber,
    string? CompanyName,
    int? RiderId,
    bool IsReported,
    bool IsOutage,
    DateTime? ReportedAt,
    DateTime? DateOfOutage,
    DateTime? UpdatedAd,
    bool? IsFreelancer          // ← add
    );

