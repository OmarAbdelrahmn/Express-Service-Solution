using Application.Abstraction;
using Application.Contracts.Users;


namespace Application.Admin;

public interface IAdminService
{
    Task<IEnumerable<UserResponses>> GetAllUsers();
    Task<Result<UserResponses>> GetUserAsync(string Id);
    Task<Result<UserResponses>> GetUser2Async(string UserName);
    Task<Result> ToggleStatusAsync(string UserName);
    Task<Result> DeletaUserAsync(string UserName);
    Task<Result<int>> BackfillHousingIdsAsync(CancellationToken cancellationToken = default);

}
