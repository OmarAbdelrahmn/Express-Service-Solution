using Application.Abstraction;
using Application.Contracts.DailyWork;

namespace Application.Service.DailyWork;

public interface IDailyWorkService
{
    Task<Result<DailyWorkResponse>> GetAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<Result<DailyWorkResponse>> EditAsync(EditDailyWorkRequest request, CancellationToken cancellationToken = default);
}
