using Application.Abstraction;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service;

public interface IImportService
{

    Task<Result<ImportStagingResponse>> ProcessExcelFileAsync(
        IFormFile file,
        string uploadedBy);

    Task<Result<IEnumerable<TempEmployeeRiderImportResponse>>> GetPendingImportsAsync(
        Guid? batchId = null);

    Task<Result<ImportStatisticsResponse>> GetImportStatisticsAsync(Guid batchId);


    Task<Result<ImportResolutionResponse>> ApproveValidRecordsAsync(
        Guid batchId,
        string resolvedBy,
        string? adminNotes = null);

    Task<Result> RejectBatchAsync(
        Guid batchId,
        string resolvedBy,
        string reason);

    Task<Result<ImportResolutionResponse>> ApproveSelectedRecordsAsync(
        List<int> recordIds,
        string resolvedBy,
        string? adminNotes = null);


    Task<Result<IEnumerable<ImportBatchSummary>>> GetAllBatchesAsync();
}

// DTOs
public record ImportStagingResponse(
    Guid BatchId,
    string FileName,
    int TotalRecords,
    int ValidRecords,
    int RecordsWithErrors,
    int RecordsWithWarnings,
    int NewEmployees,
    int ExistingEmployees,
    int NewRiders,
    List<string> CriticalErrors,
    DateTime ProcessedAt
);

public record TempEmployeeRiderImportResponse(
    int Id,
    int RowNumber,
    Guid BatchId,

    // Employee Info
    long IqamaNo,
    string? NameAR,
    string? NameEN,
    string? IqamaEndM,
    string? IqamaEndH,
    string? Phone,
    string? Status,

    // Rider Info
    string? WorkingId,
    string? CompanyName,
    int? CompanyId,
    string? LicenseNumber,

    // Status
    bool IsNewEmployee,
    bool IsNewRider,
    bool HasErrors,
    List<string> ValidationErrors,
    List<string> ValidationWarnings,

    DateTime UploadedAt,
    string? UploadedBy
);

public record ImportStatisticsResponse(
    Guid BatchId,
    string FileName,
    int TotalRecords,
    int ValidRecords,
    int RecordsWithErrors,
    int RecordsWithWarnings,
    int NewEmployees,
    int ExistingEmployees,
    int NewRiders,
    int ResolvedRecords,
    int PendingRecords,
    Dictionary<string, int> ErrorBreakdown,
    Dictionary<string, int> CompanyBreakdown,
    DateTime UploadedAt
);

public record ImportResolutionResponse(
    int TotalProcessed,
    int SuccessfulEmployees,
    int SuccessfulRiders,
    int Failed,
    List<string> Details,
    List<string> Errors
);

public record ImportBatchSummary(
    Guid BatchId,
    string FileName,
    int TotalRecords,
    int ValidRecords,
    int RecordsWithErrors,
    bool IsResolved,
    DateTime UploadedAt,
    string? UploadedBy
);


public class TempEmployeeRiderImport
{
    public int Id { get; set; }

    // Employee Data
    public long IqamaNo { get; set; }
    public string? IqamaEndM { get; set; } // Store as string to preserve format
    public string? IqamaEndH { get; set; } // Hijri date as string
    public string? PassportNo { get; set; }
    public string? PassportEnd { get; set; }
    public string? Sponsor { get; set; }
    public long? SponsorNo { get; set; }
    public string? JobTitle { get; set; }
    public string? NameAR { get; set; }
    public string? NameEN { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
    public string? DateOfBirth { get; set; }
    public string? Status { get; set; }
    public string? IBAN { get; set; }
    public bool INKSA { get; set; } = true;

    // RiderDetails Data
    public string? WorkingId { get; set; }
    public string? TshirtSize { get; set; }
    public string? LicenseNumber { get; set; }
    public string? CompanyName { get; set; } // From Excel
    public int? CompanyId { get; set; } // Resolved from CompanyName

    // Parsed Dates (after validation)
    public DateOnly? ParsedIqamaEndM { get; set; }
    public DateOnly? ParsedIqamaEndH { get; set; }
    public DateOnly? ParsedPassportEnd { get; set; }
    public DateOnly? ParsedDateOfBirth { get; set; }

    // Import Metadata
    public int RowNumber { get; set; } // Excel row number
    public bool IsNewEmployee { get; set; }
    public bool IsNewRider { get; set; }
    public string? ValidationErrors { get; set; } // JSON array of errors
    public string? ValidationWarnings { get; set; } // JSON array of warnings
    public bool HasErrors { get; set; }

    // Workflow
    public DateTime UploadedAt { get; set; } = DateTime.Now;
    public string? UploadedBy { get; set; }
    public bool IsResolved { get; set; } = false;
    public string? Resolution { get; set; } // "Approved" or "Rejected"
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
    public string? AdminNotes { get; set; }

    // Batch tracking
    public Guid BatchId { get; set; } // Group imports from same file
    public string? FileName { get; set; }
}