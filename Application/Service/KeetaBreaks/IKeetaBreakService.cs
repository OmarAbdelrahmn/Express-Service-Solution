using Application.Abstraction;
using Application.Contracts.KeetaBreaks;

namespace Application.Service.KeetaBreaks;

public interface IKeetaBreakService
{
    Task<Result<List<KeetaBreakConfigurationResponse>>> GetConfigurationsAsync(CancellationToken cancellationToken = default);
    Task<Result<KeetaBreakConfigurationResponse>> CreateConfigurationAsync(CreateKeetaBreakConfigurationRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<KeetaBreakCapacityPlanResponse>> CreateCapacityPlanAsync(CreateKeetaBreakCapacityPlanRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteConfigurationVersionAsync(int version, CancellationToken cancellationToken = default);
}
