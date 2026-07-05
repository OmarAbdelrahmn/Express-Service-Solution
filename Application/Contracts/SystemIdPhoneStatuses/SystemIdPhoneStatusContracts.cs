namespace Application.Contracts.SystemIdPhoneStatuses;

public record ImportSystemIdPhoneStatusCell(
    string SystemId,
    string PhoneNumber,
    string? Status
);

public record ImportSystemIdPhoneStatusRequest(
    List<ImportSystemIdPhoneStatusCell> Cells,
    DateOnly StatusDate
);

public record SystemIdPhoneStatusImportResponse(
    int TotalCellsProcessed,
    int RecordsCreated,
    int BlankCellsSkipped,
    List<string> Warnings
);

public record CreateSystemIdPhoneStatusRequest(
    string SystemId,
    string PhoneNumber,
    string? Status
);

public record UpdateSystemIdPhoneStatusRequest(
    string SystemId,
    string PhoneNumber,
    string? Status
);

public record SystemIdPhoneStatusResponse(
    int Id,
    string SystemId,
    string PhoneNumber,
    DateOnly StatusDate,
    string? Status,
    string? RawStatus,
    DateTime UploadedAt,
    string? UploadedBy
);
