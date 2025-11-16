using Application.Abstraction;
using Application.Contracts.Users;

namespace Application.User;

public interface IUserService
{
    Task<Result<UserProfileResponse>> GetUserProfile(string id);
    Task<Result> UpdateUserProfile(string id, UpdateUserProfileRequest request);
    Task<Result> ChangePassword(string id, ChangePasswordRequest request);
    Task<Result> ChangeRoleForUser(string UserName, string NewRole);
}
