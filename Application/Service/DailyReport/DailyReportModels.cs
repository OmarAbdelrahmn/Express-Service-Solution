namespace Application.Service.DailyReport;

// One row in the PDF table
public record ShiftReportRow(
    string RiderNameAR,
    long IqamaNo,
    string HousingName,
    int AcceptedOrders,
    float WorkingHours,
    string Section           // "Top 5" or "Bottom 5"
);

// Per-company block
public record CompanyReportBlock(
    string CompanyName,
    // Key = Housing name, Value = rows inside that housing
    Dictionary<string, List<ShiftReportRow>> RowsByHousing,
    int TotalShifts
);

// Full report payload
public record DailyReportPayload(
    DateOnly ReportDate,
    List<CompanyReportBlock> Companies,
    int GrandTotalShifts
);