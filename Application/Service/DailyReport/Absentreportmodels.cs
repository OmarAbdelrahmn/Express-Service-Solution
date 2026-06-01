namespace Application.Service.DailyReport;

public record AbsentRiderRow(
    string RiderNameAR,
    long IqamaNo,
    string HousingName,
    string WorkingId,
    bool HadShiftButLowHours,  // true = worked but < 8h, false = no shift at all
    float? WorkingHours         // null when no shift, actual hours when low-hours case
);

public record AbsentReportPayload(
    DateOnly ReportDate,
    int CompanyId,
    string CompanyName,
    List<AbsentRiderRow> Riders
);