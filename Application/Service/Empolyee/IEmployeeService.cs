using Application.Abstraction;
using Application.Contracts.Employees;
using Application.Contracts.Roles;
using Application.Contracts.Users;
using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.Empolyee;

public interface IEmployeeService
{
    Task<Result<IEnumerable<EmpolyeeResponse>>>GetAllEmployee();
    Task<Result<IEnumerable<DeletedEmployees>>>GetAlldeletedEmployee();
    Task<Result<IEnumerable<EmpolyeeResponse>>>Get(long IqamaNo);
    Task<Result<EmpolyeeResponse>> CreateAsync(EmpolyeeRequest Request);
    Task<Result<EmpolyeeResponse>> UpdateAsync(long IqamaNo, UEmpolyeeRequest Request);
    Task<Result> DeleteAsync(long IqamaNo, CancellationToken cancellationToken = default);
    Task<Result> AddEmployeeToHousing(long IqamaNo , string HousingName);
    Task<Result> ChangeEmployeeToHousing(long IqamaNo , string oldHousingName , string NewHousingName);
    Task<Result<IEnumerable<EmpolyeeResponse>>> Filter(EmployeeFilter filter);
    Task<Result<PagedList<EmpolyeeResponse>>> Filter2(EmployeeFilter2 filter);
    Task<List<EmpolyeeResponse>> SmartSearch(string keyword);

    Task<Result> RequestEnableEmployeeAsync(long IqamaNo, string reason, string requestedBy);
    Task<Result> RequestDisableEmployeeAsync(long IqamaNo, string reason, string requestedBy);
    Task<Result<IEnumerable<TempEmployeeStatusChangeResponse>>> GetPendingStatusChangesAsync();
    Task<Result> ResolveStatusChangesAsync(EBulkResolutionRequest request);


}
public record EmployeeFilter(
    DateOnly? IqamaEndH = null,
    DateOnly? IqamaEndM = null,
    string? Sponsor = null,
    int? SponsorNo = null,
    DateOnly? PassportEnd = null,
    string? JobTitle = null,
    string? NameAR = null,
    string? NameEN = null,
    string? Country = null,
    string? Status = null,
    bool? INKSA = null,
    string? HousingName = null
);

public sealed record EmployeeFilter2(
    DateOnly? IqamaEndH,
    DateOnly? IqamaEndM,
    string? Sponsor,
    int? SponsorNo,
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
