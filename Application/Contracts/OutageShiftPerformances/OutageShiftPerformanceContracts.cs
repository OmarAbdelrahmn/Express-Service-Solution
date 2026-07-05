namespace Application.Contracts.OutageShiftPerformances;

public record ImportOutageShiftPerformanceRow(
    string SystemId,
    string PhoneNumber,
    int AcceptedOrders,
    int RejectedOrders,
    float WorkingHours
);

public record ImportOutageShiftPerformanceRequest(
    List<ImportOutageShiftPerformanceRow> Rows,
    DateOnly ShiftDate
);

public record OutageShiftPerformanceImportResponse(
    int TotalRowsProcessed,
    int RecordsCreated,
    List<string> Warnings
);

public record CreateOutageShiftPerformanceRequest(
    string SystemId,
    string PhoneNumber,
    int AcceptedOrders,
    int RejectedOrders,
    float WorkingHours
);

public record UpdateOutageShiftPerformanceRequest(
    string SystemId,
    string PhoneNumber,
    int AcceptedOrders,
    int RejectedOrders,
    float WorkingHours
);

public record OutageShiftPerformanceResponse(
    int Id,
    string SystemId,
    string PhoneNumber,
    DateOnly ShiftDate,
    int AcceptedOrders,
    int RejectedOrders,
    float WorkingHours,
    DateTime UploadedAt,
    string? UploadedBy
);
