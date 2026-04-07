namespace Application.Contracts.Employees;



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

public record EscapedEmployeeSummaryResponse(
    int Id,
    long EmployeeIqamaNo,
    string EmployeeNameAR,
    string EmployeeNameEN,
    DateOnly EscapedAt,
    string ActivePath,
    DateTime? RemovalDeadline,
    int? RemainingDaysToRemoval,
    bool IsOverdue,
    bool TenDayNotificationSent
);

public record EscapedEmployeeStatsResponse(
    int TotalEscaped,
    int WithReportedPath,
    int WithOutagePath,
    int WithNoPath,
    int Overdue,
    int DueWithin10Days,
    int DueWithin30Days,
    int NotificationPending
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
    int? RiderId
    );

