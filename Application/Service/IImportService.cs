using Application.Abstraction;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Service;

public interface IImportService
{
    Task<Result<DirectImportResponse>> ImportEmployeesAndRidersAsync(
        IFormFile file,
        string uploadedBy);

    Task<Result<VehicleImportResponse>> ImportVehiclesAsync(
    IFormFile file,
    string uploadedBy);

    Task<Result<WorkingIdUpdateResponse>> UpdateRiderWorkingIdsAsync(
    IFormFile file,
    string uploadedBy);

    Task<Result<HousingAssignmentResponse>> BulkAssignEmployeesToHousingAsync(
    IFormFile file,
    string uploadedBy);

    Task<Result<DeletedEmployeeImportResponse>> ImportDeletedEmployeesAsync(
    IFormFile file,
    string uploadedBy);
}
public record HousingAssignmentResponse(
    int TotalRecords,
    int SuccessfulAssignments,
    int FailedRecords,
    int EmployeeNotFound,
    int HousingNotFound,
    int AlreadyAssigned,
    List<HousingAssignmentRowResult> Results,
    List<string> Errors,
    DateTime ProcessedAt
);

public record HousingAssignmentRowResult(
    int RowNumber,
    bool Success,
    string IqamaNo,
    string EmployeeNameEN,
    string EmployeeNameAR,
    string HousingName,
    bool IsRider,  // NEW: Indicates if this person is a rider
    string? CompanyName,  // NEW: Company name if rider
    bool WasAlreadyAssigned,
    string? PreviousHousing,
    List<string> Warnings,
    string? ErrorMessage
);
// DTOs
public record DirectImportResponse(
    int TotalRecords,
    int SuccessfulEmployees,
    int SuccessfulRiders,
    int FailedRecords,
    List<ImportRowResult> Results,
    List<string> Errors,
    DateTime ProcessedAt
);

public record ImportRowResult(
    int RowNumber,
    bool Success,
    string IqamaNo,
    string NameEN,
    string NameAR,
    string? CompanyName,
    bool EmployeeCreated,
    bool EmployeeUpdated,
    bool RiderCreated,
    bool RiderUpdated,
    List<string> Warnings,
    string? ErrorMessage
);


public record VehicleImportResponse(
    int TotalRecords,
    int SuccessfulVehicles,
    int UpdatedVehicles,
    int AssignedToRiders,
    int FailedRecords,
    List<VehicleImportRowResult> Results,
    List<string> Errors,
    DateTime ProcessedAt
);

public record VehicleImportRowResult(
    int RowNumber,
    bool Success,
    string VehicleNumber,
    string PlateNumberA,
    int SerialNumber,
    bool VehicleCreated,
    bool VehicleUpdated,
    bool AssignedToRider,
    string? AssignedRiderIqama,
    List<string> Changes,
    List<string> Warnings,
    string? ErrorMessage
);

public record WorkingIdUpdateResponse(
    int TotalRecords,
    int SuccessfulUpdates,
    int FailedRecords,
    int IqamaNotFound,
    int RiderDetailsNotFound,
    List<WorkingIdUpdateRowResult> Results,
    List<string> NotFoundIqamas,
    List<string> Errors,
    DateTime ProcessedAt
);

public record WorkingIdUpdateRowResult(
    int RowNumber,
    bool Success,
    string IqamaNo,
    string? NewWorkingId,
    string? OldWorkingId,
    string? RiderNameEN,
    string? RiderNameAR,
    string? ErrorMessage
);

public record DeletedEmployeeImportResponse(
    int TotalRecords,
    int SuccessfulImports,
    int FailedRecords,
    int DuplicateIqamas,
    List<DeletedEmployeeImportRowResult> Results,
    List<string> Errors,
    DateTime ProcessedAt
);

public record DeletedEmployeeImportRowResult(
    int RowNumber,
    bool Success,
    string IqamaNo,
    string? NameEN,
    string? NameAR,
    string? WorkingId,
    string? CompanyName,
    List<string> Warnings,
    string? ErrorMessage
);