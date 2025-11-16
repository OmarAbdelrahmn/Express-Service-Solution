
using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.Users;
using Domain;
using Domain.Entities;
using Mapster;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Application.Admin;

public class AdminService(
     UserManager<ApplicationUser> manager
    , ApplicationDbcontext dbcontext ) : IAdminService
{
    private readonly UserManager<ApplicationUser> manager = manager;
    private readonly ApplicationDbcontext dbcontext = dbcontext;

    public async Task<IEnumerable<UserResponses>> GetAllUsers() =>
        await (from u in dbcontext.Users
               join ur in dbcontext.UserRoles
               on u.Id equals ur.UserId
               join r in dbcontext.Roles
               on ur.RoleId equals r.Id into roles
               select new
               {
                   u.Id,
                   u.UserName,
                   u.Address,
                   u.FullName,
                   u.IsDisable,
                   roles = roles.Select(r => r.Name!).ToList()
               })
                  .GroupBy(x => new { x.Id, x.UserName, x.Address, x.FullName, x.IsDisable })
                  .Select(c => new UserResponses(
                      c.Key.Id,
                      c.Key.FullName,
                      c.Key.Address,
                      c.Key.UserName,
                      c.Key.IsDisable,
                      c.SelectMany(x => x.roles)
                      ))
                  .ToListAsync();

    public async Task<Result<UserResponses>> GetUserAsync(string Id)
    {
        if (await manager.FindByIdAsync(Id) is not { } user)
            return Result.Failure<UserResponses>(UserErrors.UserNotFound);

        var userroles = await manager.GetRolesAsync(user);

        var response = new UserResponses
        (
            user.Id,
            user.FullName!,
            user.Address!,
            user.UserName!,
            user.IsDisable,
            userroles
        );

        return Result.Success(response);
    }
    public async Task<Result<UserResponses>> GetUser2Async(string UserName)
    {
        if (await manager.FindByNameAsync(UserName) is not { } user)
            return Result.Failure<UserResponses>(UserErrors.UserNotFound);

        var userroles = await manager.GetRolesAsync(user);

        var response = new UserResponses
        (
            user.Id,
            user.FullName!,
            user.Address!,
            user.UserName!,
            user.IsDisable,
            userroles
        );

        return Result.Success(response);
    }

    public async Task<Result> ToggleStatusAsync(string UserId)
    {
        if (await manager.FindByIdAsync(UserId) is not { } user)
            return Result.Failure(UserErrors.UserNotFound);

        user.IsDisable = !user.IsDisable;

        var result = await manager.UpdateAsync(user);
        if (result.Succeeded)
            return Result.Success();

        var error = result.Errors.First();
        return Result.Failure(new Error(error.Code, error.Description, StatusCodes.Status400BadRequest));
    }

}
