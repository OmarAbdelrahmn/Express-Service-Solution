using Application.Abstraction;
using Application.Contracts.Employees;

namespace Application.Service.EscapedEmployee;

public interface IEscapedEmployeeService
{
    Task<Result<EscapedEmployeeResponse>> CreateAsync(
        CreateEscapedEmployeeRequest request,
        CancellationToken ct = default);

    Task<Result<EscapedEmployeeResponse>> ActivateReportedPathAsync(
        ActivateReportedPathRequest request,
        CancellationToken ct = default);

    Task<Result<EscapedEmployeeResponse>> ActivateOutagePathAsync(
        ActivateOutagePathRequest request,
        CancellationToken ct = default);

    Task<Result> ClearActivePathAsync(
        long employeeIqamaNo,
        string updatedBy,
        CancellationToken ct = default);

    Task<Result<EscapedEmployeeResponse>> GetByIqamaAsync(
        long iqamaNo,
        CancellationToken ct = default);

    Task<Result<IEnumerable<EscapedEmployeeSummaryResponse>>> GetAllAsync(
        CancellationToken ct = default);

    Task<Result<IEnumerable<EscapedEmployeeSummaryResponse>>> GetDueForRemovalAsync(
        int daysThreshold = 10,
        CancellationToken ct = default);

    Task<Result<EscapedEmployeeStatsResponse>> GetStatsAsync(
        CancellationToken ct = default);

    Task<Result<EscapedEmployeeResponse>> UpdateAsync(
        long iqamaNo,
        UpdateEscapedEmployeeRequest request,
        CancellationToken ct = default);

    Task<Result> DeleteAsync(
        long iqamaNo,
        CancellationToken ct = default);

    Task<List<EscapedNotificationItem>> GetPendingNotificationsAsync(
        CancellationToken ct = default);

    Task MarkNotificationSentAsync(
        IEnumerable<long> iqamaNos,
        CancellationToken ct = default);
}