using Application.Abstraction;
using Application.Contracts.Users;


namespace Application.Service.Admin;

public interface IAdminService
{
    Task<IEnumerable<UserResponses>> GetAllUsers();
    Task<Result<UserResponses>> GetUserAsync(string Id);
    Task<Result<UserResponses>> GetUser2Async(string UserName);
    Task<Result> ToggleStatusAsync(string UserName);
    Task<Result> DeletaUserAsync(string UserName);
    Task<Result<int>> BackfillHousingIdsAsync(CancellationToken cancellationToken = default);


    Task<Result<SupportPasswordResetResponse>> SupportResetPasswordAsync(
        string userName,
        string? supportKey,
        CancellationToken cancellationToken = default);
}

public sealed record SupportPasswordResetResponse(
    string UserName,
    string TemporaryPassword,
    DateTimeOffset ResetAtUtc);

public sealed record SupportPasswordResetRequest(string UserName);
