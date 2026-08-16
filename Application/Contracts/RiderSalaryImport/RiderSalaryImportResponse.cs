namespace Application.Contracts.RiderSalaryImport;

public record RiderSalaryImportResponse(
    int TotalRows,
    int MatchedRiders,
    int RidersNotFound,
    int InvalidRows,
    IReadOnlyList<RiderSalaryImportRowResponse> Rows);

public record RiderSalaryImportRowResponse(
    int RowNumber,
    long? IqamaNo,
    decimal? Salary,
    RiderSalaryRiderResponse? Rider,
    string? ErrorMessage);

public record RiderSalaryRiderResponse(
    long IqamaNo,
    string NameAR,
    string NameEN,
    string Sponsor,
    bool OnCompany,
    string? HousingName,
    string? WorkingId,
    string? CompanyName);
