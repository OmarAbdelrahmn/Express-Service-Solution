using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.Users;
using Domain.Entities;
using Mapster;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Application.User;

public class UserServices(UserManager<ApplicationUser> manager) : IUserService
{
    private readonly UserManager<ApplicationUser> manager = manager;

    public async Task<Result> ChangePassword(string id, ChangePasswordRequest request)
    {
        var user = await manager.FindByIdAsync(id);

        var result = await manager.ChangePasswordAsync(user!, request.CurrentPassword, request.NewPassord);

        if (result.Succeeded)
            return Result.Success();

        var error = result.Errors.First();

        return Result.Failure(new Error(error.Code, error.Description, StatusCodes.Status400BadRequest));
    }

    public async Task<Result> ChangeRoleForUser(string UserName, string NewRole)
    {
        var User = await manager.FindByNameAsync(UserName);

        if (User == null)
            return Result.Failure(UserErrors.UserNotFound);

        var Roles = await manager.GetRolesAsync(User);

        if(Roles == null || Roles.Count ==0)
            return Result.Failure(RolesErrors.somethingwrong);
        
        if (Roles.Contains(NewRole))
            return Result.Failure(RolesErrors.haveit);
        
        var RemoveRoleResult = await manager.RemoveFromRolesAsync(User, Roles);
        
        if (!RemoveRoleResult.Succeeded)
            return Result.Failure(RolesErrors.somethingwrong);

        var AddRoleResult = await manager.AddToRoleAsync(User, NewRole);

        if (!AddRoleResult.Succeeded)
            return Result.Failure(RolesErrors.somethingwrong);

        return Result.Success();
    }

    public async Task<Result<UserProfileResponse>> GetUserProfile(string id)
    {
        var user = await manager.Users
            .Where(i => i.Id == id)
            .ProjectToType<UserProfileResponse>()
            .SingleAsync();
        ;

        return Result.Success(user);
    }

    public async Task<Result> UpdateUserProfile(string id, UpdateUserProfileRequest request)
    {
        //var user = await manager.FindByIdAsync(id);
        //user = request.Adapt(user);
        //await manager.UpdateAsync(user!);
        await manager.Users
            .Where(i => i.Id == id)
            .ExecuteUpdateAsync(set =>
            set.SetProperty(x => x.FullName, request.FullName)
               .SetProperty(x => x.Address, request.Address));

        return Result.Success();
    }
}
