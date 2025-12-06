using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Contracts
{
    class temp
    {
    }
}
public record TempEmployeeUpdateResponse(
    int Id,
    long IqamaNo,
    string EmployeeNameAR,
    string EmployeeNameEN,
    bool IsNewEmployee,
    EmployeeUpdateComparison Comparison,
    DateTime UploadedAt,
    string? UploadedBy,
    bool IsResolved
);

public record EmployeeUpdateComparison(
    FieldChange<DateOnly?>? IqamaEndM,
    FieldChange<DateOnly?>? IqamaEndH,
    FieldChange<string?>? PassportNo,
    FieldChange<DateOnly?>? PassportEnd,
    FieldChange<string?>? Sponsor,
    FieldChange<string?>? JobTitle,
    FieldChange<string?>? NameAR,
    FieldChange<string?>? NameEN,
    FieldChange<string?>? Country,
    FieldChange<string?>? Phone,
    FieldChange<DateTime?>? DateOfBirth,
    FieldChange<string?>? Status,
    FieldChange<string?>? IBAN,
    FieldChange<bool?>? INKSA
);

public record FieldChange<T>(
    T? OldValue,
    T? NewValue,
    bool HasChanged
);

public record TempEmployeeStatusChangeResponse(
    int Id,
    long EmployeeIqamaNo,
    string EmployeeNameAR,
    string EmployeeNameEN,
    string Action,
    string Reason,
    string RequestedBy,
    DateTime RequestedAt,
    bool IsResolved,
    string? Resolution,
    string? ResolvedBy,
    DateTime? ResolvedAt
);

public record TempVehicleOperationResponse(
    int Id,
    long RiderIqamaNo,
    string RiderNameAR,
    string RiderNameEN,
    string VehiclePlateNumber,
    string VehicleNumber,
    string OperationType,
    string Reason,
    DateTime RequestedAt,
    string RequestedBy,
    bool IsResolved,
    string? Resolution,
    string? ResolvedBy,
    DateTime? ResolvedAt,
    VehicleOperationValidation Validation
);

public record VehicleOperationValidation(
    bool IsValid,
    List<string> Errors,
    List<string> Warnings
);

public record BulkResolutionRequest(
    List<int> Ids,
    string Resolution, // "Approved" or "Rejected"
    string ResolvedBy,
    string? AdminNotes
);

public record BulkResolutionResponse(
    int TotalProcessed,
    int SuccessCount,
    int FailedCount,
    List<string> Details
);