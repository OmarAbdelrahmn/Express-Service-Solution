using System.Text.Json.Serialization;

namespace Application.Contracts.Employees;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VehicleDocumentSheetRowStatus
{
    Updated, WouldUpdate, Unchanged, Skipped, NotFound, Invalid, Ambiguous
}

public sealed record VehicleDocumentDateChange(
    string Field,
    DateOnly? PreviousValue,
    DateOnly NewValue,
    string SourceValue,
    string SourceCalendar);

public sealed record VehicleDocumentSheetRowResult(
    int RowNumber,
    int? SerialNumber,
    string? VehicleNumber,
    string? PlateNumberA,
    VehicleDocumentSheetRowStatus Status,
    IReadOnlyList<VehicleDocumentDateChange> Changes,
    IReadOnlyList<string> Errors);

public sealed record VehicleDocumentSheetImportReport(
    string WorksheetName,
    bool DryRun,
    int TotalRows,
    int UpdatedCount,
    int WouldUpdateCount,
    int UnchangedCount,
    int SkippedCount,
    int NotFoundCount,
    int InvalidCount,
    int AmbiguousCount,
    IReadOnlyList<VehicleDocumentSheetRowResult> Rows);
