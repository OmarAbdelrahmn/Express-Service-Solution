using Application.Abstraction;
using Application.Contracts.Employees;
using static Application.Service.Empolyee.EmployeeService;

namespace Application.Service.Empolyee;

public interface IEmployeeService
{
    Task<bool> Togle(long iqama);
    Task<Result<IEnumerable<EmpolyeeResponse>>> GetAllEmployee();
    Task<Result<IEnumerable<DeletedEmployeeResponse>>> GetAlldeletedEmployee();
    Task<Result<IEnumerable<EmpolyeeResponse>>> Get(long IqamaNo);
    Task<Result<EmpolyeeResponse>> Get1(long IqamaNo);
    Task<Result<EmpolyeeResponse>> CreateAsync(EmpolyeeRequest Request);
    Task<Result<EmpolyeeResponse>> UpdateAsync(long IqamaNo, UEmpolyeeRequest Request);
    Task<Result> DeleteAsync(long IqamaNo, CancellationToken cancellationToken = default);
    Task<Result> AddEmployeeToHousing(long IqamaNo, string HousingName);
    Task<Result> ChangeEmployeeToHousing(long IqamaNo, string oldHousingName, string NewHousingName);
    Task<Result<IEnumerable<EmpolyeeResponse>>> Filter(EmployeeFilter filter);
    Task<Result<PagedList<EmpolyeeResponse>>> Filter2(EmployeeFilter2 filter);
    Task<List<EmpolyeeResponse>> SmartSearch(string keyword);

    Task<Result> RequestStatusChangeAsync(long IqamaNo, string newStatus, string reason, string requestedBy);
    Task<Result<IEnumerable<TempEmployeeStatusChangeResponse>>> GetPendingStatusChangesAsync();
    Task<Result> ResolveStatusChangeAsync(long IqamaNo, string resolution, string resolvedBy, string? adminNotes = null);

    Task<Result<StatusChangeStatisticsDto>> GetStatusChangeStatisticsAsync();
    Task<Result<IEnumerable<StatusChangeHistoryDto>>> GetStatusChangesByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<Result<EmployeeStatusHistoryResponse>> GetEmployeeStatusHistoryAsync(long IqamaNo);


}
public record EmployeeFilter(
    DateOnly? IqamaEndH = null,
    DateOnly? IqamaEndM = null,
    string? Sponsor = null,
    long? sponsorNo = null,
    DateOnly? PassportEnd = null,
    string? JobTitle = null,
    string? NameAR = null,
    string? NameEN = null,
    string? Country = null,
    string? Status = null,
    bool? INKSA = null,
    string? HousingName = null
);
public record EmployeeFilterr(
    DateOnly? IqamaEndH = null,
    DateOnly? IqamaEndM = null,
    string? Sponsor = null,
    long? sponsorNo = null,
    DateOnly? PassportEnd = null,
    string? JobTitle = null,
    string? NameAR = null,
    string? NameEN = null,
    string? Country = null,
    string? Status = null,
    bool? INKSA = null,
    string? HousingName = null,
    string? WorkingId = null,
    string? CompanyName = null
);

public sealed record EmployeeFilter2(
    DateOnly? IqamaEndH,
    DateOnly? IqamaEndM,
    string? Sponsor,
    long? sponsorNo,
    DateOnly? PassportEndH,
    DateOnly? PassportEndM,
    string? JobTitle,
    string? NameAR,
    string? NameEN,
    string? Country,
    string? Status,
    bool? INKSA,
    string? HousingName,
    string? SortBy = null,       // Example: "IqamaEndH"
    string? SortDirection = "ASC", // ASC / DESC
    int Page = 1,                 // Pagination (default first page)
    int PageSize = 15             // 15 records per page
);


public class PagedList<T>
{
    public List<T> Data { get; }
    public int TotalCount { get; }
    public int Page { get; }
    public int PageSize { get; }

    public PagedList(List<T> data, int totalCount, int page, int pageSize)
    {
        Data = data;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }
}

public static class EmployeeStatus
{
    public const string Enable = "enable";
    public const string Disable = "disable";
    public const string Fleeing = "fleeing";
    public const string Vacation = "vacation";
    public const string Accident = "accident";
    public const string Sick = "sick";

    public static readonly string[] ValidStatuses =
    {
        Enable, Disable, Fleeing, Vacation, Accident, Sick
    };

    public static bool IsValid(string status)
    {
        return ValidStatuses.Contains(status.ToLower());
    }
}