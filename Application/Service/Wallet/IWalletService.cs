using Application.Abstraction;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.Wallet;


public interface IWalletService
{
    /// <summary>
    /// Imports wallet records from an Excel file.
    /// The Excel must have a WorkingId column and an Amount column.
    /// The date is passed as a query-string parameter.
    /// Substitutions are resolved the same way as shift imports.
    /// If a record already exists for the resolved (WorkedRiderId + Date), it is updated.
    /// </summary>
    Task<Result<WalletImportResult>> ImportFromExcelAsync(
        Stream excelStream,
        DateOnly date,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all wallet records with housing name and AR names / IqamaNos
    /// for both the worked rider and (when present) the main rider.
    /// </summary>
    Task<Result<IEnumerable<WalletResponse>>> GetAllAsync(
        CancellationToken cancellationToken = default);
}

public record WalletResponse(
    int Id,
    DateOnly Date,
    decimal Amount,

    // The rider who actually worked
    int WorkedRiderId,
    string WorkedRiderWorkingId,
    string WorkedRiderNameAR,
    long WorkedRiderIqamaNo,
    string? WorkedRiderHousingName,

    // The original rider from the Excel (only when substitution)
    int? MainRiderId,
    string? MainRiderWorkingId,
    string? MainRiderNameAR,
    long? MainRiderIqamaNo,

    bool IsSubstitution,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

// ── Import result ────────────────────────────────────────────────────────────

public record WalletImportResult(
    int TotalRecords,
    int CreatedCount,
    int UpdatedCount,
    int DeletedCount,
    int ErrorCount,
    List<WalletImportError> Errors
);

public record WalletImportError(int RowNumber, string WorkingId, string Message);