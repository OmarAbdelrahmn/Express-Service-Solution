using Application.Abstraction;
using Application.Contracts.Employees;
using Domain.Entities;

namespace Application.Service.EscapedEmployee;

public interface IEscapedEmployeeService
{
    Task<Result<BackfillResult>> BackfillFleeingEmployeesAsync(string createdBy, CancellationToken ct = default);
    public record BackfillResult(
    int TotalCreated,
    List<long> CreatedIqamaNos
);
    Task<Result> ForceDeleteEscapedEmployeeAsync(long iqamaNo, CancellationToken ct = default);

    Task<Result> DeactivateEscapedEmployeeAsync(long iqamaNo, string deactivatedBy, CancellationToken ct = default);


    //Task<Result<EscapedEmployeeResponse>> CreateAsync(
    //    CreateEscapedEmployeeRequest request,
    //    CancellationToken ct = default);

    //Task<Result<EscapedEmployeeResponse>> ActivateReportedPathAsync(
    //    ActivateReportedPathRequest request,
    //    CancellationToken ct = default);

    //Task<Result<EscapedEmployeeResponse>> ActivateOutagePathAsync(
    //    ActivateOutagePathRequest request,
    //    CancellationToken ct = default);

    //Task<Result> ClearActivePathAsync(
    //    long employeeIqamaNo,
    //    string updatedBy,
    //    CancellationToken ct = default);

    //Task<Result<EscapedEmployeeResponse>> GetByIqamaAsync(
    //    long iqamaNo,
    //    CancellationToken ct = default);

    //Task<Result<IEnumerable<EscapedEmployeeSummaryResponse>>> GetAllAsync(
    //    CancellationToken ct = default);

    //Task<Result<IEnumerable<EscapedEmployeeSummaryResponse>>> GetDueForRemovalAsync(
    //    int daysThreshold = 10,
    //    CancellationToken ct = default);

    Task<Result<EscapedEmployeeStatsResponse>> GetStatsAsync(
        CancellationToken ct = default);

    //Task<Result<EscapedEmployeeResponse>> UpdateAsync(
    //    long iqamaNo,
    //    UpdateEscapedEmployeeRequest request,
    //    CancellationToken ct = default);

    //Task<Result> DeleteAsync(
    //    long iqamaNo,
    //    CancellationToken ct = default);

    //Task<List<EscapedNotificationItem>> GetPendingNotificationsAsync(
    //    CancellationToken ct = default);

    //Task MarkNotificationSentAsync(
    //    IEnumerable<long> iqamaNos,
    //    CancellationToken ct = default);
    Task<Result<IEnumerable<EscapedEmployeeSummaryResponse>>> GetAllEscapedAsync(CancellationToken ct = default);
    Task<Result<IEnumerable<EscapedEmployeeSummaryResponse>>> GetByPathAsync(EscapedPath path, CancellationToken ct = default);
    Task<Result<IEnumerable<EscapedEmployeeSummaryResponse>>> GetOverdueAsync(CancellationToken ct = default);

    // Path management
    Task<Result> SetReportedPathAsync(long iqamaNo, SetReportedPathRequest request, CancellationToken ct = default);
    Task<Result> SetOutagePathAsync(long iqamaNo, SetOutagePathRequest request, CancellationToken ct = default);
    Task<Result> SwitchPathAsync(long iqamaNo, SwitchPathRequest request, CancellationToken ct = default);

    // Notes
    Task<Result> UpdateNotesAsync(long iqamaNo, string notes, CancellationToken ct = default);

    // Deletion (after 60-day window or admin override)
    Task<Result> RemoveEscapedEmployeeAsync(long iqamaNo, string removedBy, CancellationToken ct = default);

   }