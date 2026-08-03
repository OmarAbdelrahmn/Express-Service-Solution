namespace Application.Contracts.OutageShiftPerformances;

public record ImportOutageShiftPerformanceRow(
    string RiderId,
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
    int OutRiderInfoId,
    int AcceptedOrders,
    int RejectedOrders,
    float WorkingHours
);

public record UpdateOutageShiftPerformanceRequest(
    int OutRiderInfoId,
    int AcceptedOrders,
    int RejectedOrders,
    float WorkingHours
);

public record OutageShiftPerformanceResponse(
    int Id,
    int OutRiderInfoId,
    string RiderId,
    string? Name,
    DateOnly ShiftDate,
    int AcceptedOrders,
    int RejectedOrders,
    float WorkingHours,
    DateTime UploadedAt,
    string? UploadedBy
);
