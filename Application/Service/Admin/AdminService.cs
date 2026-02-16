using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.Users;
using Domain;
using Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Application.Service.Admin;

public class AdminService(
     UserManager<ApplicationUser> manager
    , ApplicationDbcontext dbcontext) : IAdminService
{
    private readonly UserManager<ApplicationUser> manager = manager;
    private readonly ApplicationDbcontext dbcontext = dbcontext;


    public async Task<Result> ResetPasswordAsync(string userName)
    {
        string tempPassword = "P@ssword1234";

        var user = await manager.FindByNameAsync(userName);

        if (user == null)
        {
            return Result.Failure(UserErrors.UserNotFound);
        }
        var result = await manager.RemovePasswordAsync(user);

        if (!result.Succeeded)
        {
            var error = result.Errors.First();
            return Result.Failure(new Error(error.Code, error.Description, StatusCodes.Status400BadRequest));
        }

        result = await manager.AddPasswordAsync(user, tempPassword);
        if (!result.Succeeded)
        {
            var error = result.Errors.First();
            return Result.Failure(new Error(error.Code, error.Description, StatusCodes.Status400BadRequest));
        }

        return Result.Success();
    }
    public async Task<Result<int>> BackfillHousingIdsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Get all shifts without HousingId
            var shiftsWithoutHousing = await dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Where(s => s.HousingId == null)
                .ToListAsync(cancellationToken);

            int updatedCount = 0;

            foreach (var shift in shiftsWithoutHousing)
            {
                if (shift.Rider?.Employee?.HousingId != null)
                {
                    shift.HousingId = shift.Rider.Employee.HousingId;
                    updatedCount++;
                }
            }

            await dbcontext.SaveChangesAsync(cancellationToken);

            return Result.Success(updatedCount);
        }
        catch (Exception ex)
        {
            return Result.Failure<int>(
                new Error("ServerError", $"Error backfilling housing IDs: {ex.Message}", 500));
        }
    }
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
                   roles = roles.Select(r => r.Name!).ToList(),
                   u.LastLogin
               })
                  .GroupBy(x => new { x.Id, x.UserName, x.Address, x.FullName, x.IsDisable, x.LastLogin })
                  .Select(c => new UserResponses(
                      c.Key.Id,
                      c.Key.FullName,
                      c.Key.Address,
                      c.Key.UserName,
                      c.Key.IsDisable,
                      c.SelectMany(x => x.roles),
                      c.Key.LastLogin
                      ))
                  .ToListAsync();
    public async Task<Result> DeletaUserAsync(string UserName)
    {
        if (await manager.FindByNameAsync(UserName) is not { } user)
            return Result.Failure(UserErrors.UserNotFound);

        var result = await manager.DeleteAsync(user);

        if (result.Succeeded)
            return Result.Success();

        var error = result.Errors.First();

        return Result.Failure(new Error(error.Code, error.Description, StatusCodes.Status400BadRequest));

    }
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
            userroles,
            user.LastLogin

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
            userroles,
            user.LastLogin
        );

        return Result.Success(response);
    }

    public async Task<Result> ToggleStatusAsync(string UserName)
    {
        if (await manager.FindByNameAsync(UserName) is not { } user)
            return Result.Failure(UserErrors.UserNotFound);

        user.IsDisable = !user.IsDisable;

        var result = await manager.UpdateAsync(user);
        if (result.Succeeded)
            return Result.Success();

        var error = result.Errors.First();
        return Result.Failure(new Error(error.Code, error.Description, StatusCodes.Status400BadRequest));
    }

}
