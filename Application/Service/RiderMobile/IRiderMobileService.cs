using Application.Abstraction;
using Application.Contracts.RiderMobile;

namespace Application.Service.RiderMobile;

public interface IRiderMobileService
{
    Task<Result<RiderMobileLoginResponse>> LoginAsync(RiderMobileLoginRequest request, CancellationToken ct = default);
    Task<bool> IsSessionValidAsync(Guid sessionId, int riderId, string language, CancellationToken ct = default);
    Task<Result<RiderMobileLoginResponse>> SetLanguageAsync(Guid sessionId, int riderId, string language, CancellationToken ct = default);
    Task<Result> LogoutAsync(Guid sessionId, int riderId, CancellationToken ct = default);
    Task<Result<RiderMobileProfile>> GetProfileAsync(int riderId, string language, CancellationToken ct = default);
    Task<Result<RiderMobileDashboard>> GetDashboardAsync(int riderId, string language, CancellationToken ct = default);
    Task<Result<RiderMobileMonth>> GetMonthAsync(int riderId, string language, int year, int month, CancellationToken ct = default);
}
