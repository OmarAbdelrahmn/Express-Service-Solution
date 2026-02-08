using Application.Abstraction;
using Application.Service.Empolyee;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Application.Service.Import.ImportService;

namespace Application.Service.Import;

public interface IImportService
{

    Task<Result<CompanyTransferImportResponse>> TransferRidersToCompanyAsync(
    IFormFile file,
    int newCompanyId,
    string uploadedBy);

    Task<Result<SparePartImportResponse>> ImportSparePartsAsync(IFormFile file, string uploadedBy);
    Task<Result<RiderAccessoryImportResponse>> ImportRiderAccessoriesAsync(IFormFile file, string uploadedBy);

    Task<Result<SubstitutionImportResponse>> SyncSubstitutionsFromExcelAsync(
     IFormFile file,
     string uploadedBy,
     CancellationToken cancellationToken = default);

    public record SubstitutionImportResponse(
    int TotalRecordsInExcel,
    int ActiveSubstitutionsCreated,
    int ActiveSubstitutionsRetained,
    int ActiveSubstitutionsStopped,
    int ValidationErrors,
    int ActualRiderNotFound,
    int SubstituteRiderNotFound,
    List<SubstitutionImportDetail> Details,
    List<string> ProcessingErrors,
    DateTime ProcessedAt
);

    public record SubstitutionImportDetail(
        int RowNumber,
        string ActualRiderWorkingId,
        string SubstituteWorkingId,
        SubstitutionImportStatus Status,
        string? Action,
        string? ActualRiderName,
        string? SubstituteRiderName,
        string? ErrorMessage
    );

    public enum SubstitutionImportStatus
    {
        Created = 1,           // New substitution created
        Retained = 2,          // Already exists, kept active
        Stopped = 3,           // Was active but not in Excel, stopped
        ActualRiderNotFound = 4,
        SubstituteRiderNotFound = 5,
        ValidationError = 6
    }
    Task<Result<VehicleRelocationImportResponse>> ImportVehicleRelocationsAsync(
    IFormFile file,
    string uploadedBy);


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

    Task<Result<VehicleAssignmentImportResponse>> ImportVehicleAssignmentsAsync(
    IFormFile file,
    string uploadedBy);

    Task<Result<VehicleUsageCheckResponse>> CheckVehicleUsageFromExcelAsync(
    IFormFile file,
    string uploadedBy);

    Task<Result<RiderVerificationResponse>> VerifyRidersFromExcelAsync(
        IFormFile file,
        string uploadedBy,
        Action<int, int>? progressCallback = null);

    Task<Result<WorkingIdSyncResponse>> SyncWorkingIdsFromExcelAsync(
    IFormFile file,
    string uploadedBy,
    Action<int, int>? progressCallback = null);

    Task<Result<RiderShiftBulkImportResponse>> BulkImportRiderShiftsAsync(
        IFormFile file,
        string uploadedBy,
        Action<int, int>? progressCallback = null);



}
// Application/DTOs/VehicleUsageCheckDtos.cs
// Application/DTOs/VehicleUsageCheckDtos.cs

public record SparePartImportResponse(
    int TotalRecords,
    int SuccessfulImports,
    int UpdatedRecords,
    int FailedRecords,
    List<SparePartImportRowResult> Results,
    List<string> Errors,
    DateTime ProcessedAt
);

public record SparePartImportRowResult(
    int RowNumber,
    bool Success,
    string Name,
    int Quantity,
    decimal Price,
    string Location,
    bool Created,
    bool Updated,
    List<string> Warnings,
    string? ErrorMessage
);

public record RiderAccessoryImportResponse(
    int TotalRecords,
    int SuccessfulImports,
    int UpdatedRecords,
    int FailedRecords,
    List<RiderAccessoryImportRowResult> Results,
    List<string> Errors,
    DateTime ProcessedAt
);

public record RiderAccessoryImportRowResult(
    int RowNumber,
    bool Success,
    string Name,
    int Quantity,
    decimal Price,
    string Location,
    bool Created,
    bool Updated,
    List<string> Warnings,
    string? ErrorMessage
);

public record RiderVerificationResponse(
    int TotalRecordsProcessed,
    int FullyMatched,
    int WorkingIdFoundNameMismatch,  // Counted but NOT included in Details (name changes ignored)
    int NameFoundWorkingIdMismatch,  // Counted AND unique ones in Details
    int CompletelyNotFound,          // Counted AND unique ones in Details
    int ErrorRecords,
    List<RiderVerificationDetail> Details,  // Now contains ONLY unique WorkingId errors + validation errors
    List<string> ProcessingErrors,          // Includes duplicate summary at the end
    DateTime ProcessedAt
);

public record RiderVerificationDetail(
    int RowNumber,
    string WorkingIdFromExcel,
    string NameARFromExcel,
    VerificationStatus Status,
    string? FoundInTable,           // "RiderDetails", "WorkingIdHistory", or "Both"
    string? ActualWorkingId,        // What's in the system
    string? ActualNameAR,           // What's in the system
    long? FoundIqamaNo,
    string? ErrorMessage
);

public enum VerificationStatus
{
    FullyMatched = 1,              // Both match
    WorkingIdFoundNameMismatch = 2, // ID exists, name different
    NameFoundWorkingIdMismatch = 3, // Name exists, ID different
    CompletelyNotFound = 4,         // Nothing found
    ValidationError = 5             // Invalid data in Excel
}

public record VehicleUsageCheckResponse(
    int TotalVehicles,
    int VehiclesInUse,
    int VehiclesAvailable,
    int VehiclesNotFound,
    int FailedRecords,
    List<VehicleUsageRowResult> Results,
    List<VehicleUsageError> Errors,
    DateTime ProcessedAt
);

public record VehicleUsageRowResult(
    int RowNumber,
    bool Success,
    string PlateNumberArabic,
    string VehicleNumber,
    string VehicleType,
    VehicleUsageStatus Status,
    RiderUsageInfo? RiderInfo,
    List<string> Warnings
);

public record VehicleUsageError(
    int RowNumber,
    string PlateNumber,
    string ErrorType,
    string ErrorMessage
);

public record RiderUsageInfo(
    long IqamaNumber,
    string RiderNameArabic,
    string RiderNameEnglish,
    string? WorkingId,
    string CompanyName
);

public enum VehicleUsageStatus
{
    InUse = 1,
    Available = 2,
    NotFound = 3
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

public record VehicleAssignmentImportResponse(
    int TotalRecords,
    int SuccessfulAssignments,
    int EmployeesConvertedToRiders,
    int FailedRecords,
    int EmployeeNotFound,
    int VehicleNotFound,
    int VehicleUnavailable,
    List<VehicleAssignmentRowResult> Results,
    List<string> Errors,
    DateTime ProcessedAt
);

public record VehicleAssignmentRowResult(
    int RowNumber,
    bool Success,
    string IqamaNo,
    string EmployeeNameEN,
    string EmployeeNameAR,
    string PlateNumberA,
    string VehicleNumber,
    bool WasConvertedToRider,
    bool VehicleAssigned,
    string? PreviousLocation,
    string? NewLocation,
    string? Permission,
    DateTime? PermissionStartDate,
    DateTime? PermissionEndDate,
    List<string> Warnings,
    string? ErrorMessage
);


public record WorkingIdSyncResponse(
    int TotalRecordsProcessed,
    int WorkingIdHistoriesAdded,
    int RiderDetailsCreated,
    int AlreadyCorrect,
    int NameNotFound,
    int DuplicatesSkipped,
    int ErrorRecords,
    List<WorkingIdSyncDetail> Details,
    List<string> ProcessingErrors,
    DateTime ProcessedAt
);

public record WorkingIdSyncDetail(
    int RowNumber,
    string WorkingIdFromExcel,
    string NameARFromExcel,
    SyncStatus Status,
    string? Action,
    long? FoundIqamaNo,
    string? CurrentWorkingId,
    string? CompanyName,
    string? ErrorMessage
);

public enum SyncStatus
{
    AlreadyCorrect = 1,           // Already has this WorkingId
    HistoryAdded = 2,             // Added to WorkingId history
    RiderDetailsCreated = 3,      // Created missing RiderDetails
    NameNotFound = 4,             // Name not found in system
    ValidationError = 5,          // Invalid data
    DuplicateSkipped = 6          // Duplicate WorkingId in Excel - skipped
}

public record CompanyTransferImportResponse(
    int TotalRecords,
    int SuccessfulTransfers,
    int FailedRecords,
    int EmployeeNotFound,
    int RiderDetailsNotFound,
    int CompanyNotFound,
    List<CompanyTransferRowResult> Results,
    List<string> Errors,
    DateTime ProcessedAt
);

public record CompanyTransferRowResult(
    int RowNumber,
    bool Success,
    string IqamaNo,
    string? NewWorkingId,
    string? OldWorkingId,
    int NewCompanyId,
    int? OldCompanyId,
    string? OldCompanyName,
    string? NewCompanyName,
    string? EmployeeNameEN,
    string? EmployeeNameAR,
    List<string> Warnings,
    string? ErrorMessage
);