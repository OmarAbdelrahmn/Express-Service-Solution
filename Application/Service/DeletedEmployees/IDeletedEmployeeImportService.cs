using Application.Abstraction;

namespace Application.Service.DE;


public interface IDeletedEmployeeImportService
{
    Task<Result<ImportResult>> RestoreSingleEmployeeAsync(
        long iqamaNo,
        CancellationToken cancellationToken = default);

    Task<Result<BulkImportResult>> RestoreAllDeletedEmployeesAsync(
        CancellationToken cancellationToken = default);

    Task<Result<List<DeletedEmployeeSummary>>> GetDeletedEmployeesPreviewAsync(
        CancellationToken cancellationToken = default);
}



public record ImportResult(
    bool Success,
    long IqamaNo,
    string Message,
    EmployeeImportData? EmployeeData,
    RiderImportData? RiderData
);

public record BulkImportResult(
    int TotalRecords,
    int SuccessfulImports,
    int FailedImports,
    int SkippedRecords,
    List<ImportResult> Results
);

public record DeletedEmployeeSummary(
    long IqamaNo,
    string NameAR,
    string NameEN,
    string JobTitle,
    string Status,
    DateTime DeletedAt,
    bool HasRiderData
);

public record EmployeeImportData(
    long IqamaNo,
    string NameAR,
    string NameEN,
    bool IsEmployee
);

public record RiderImportData(
    int RiderId,
    string WorkingId,
    int CompanyId
);