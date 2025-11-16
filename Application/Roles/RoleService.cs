using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.Roles;
using Domain;
using Domain.Entities;
using Mapster;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Application.Roles;

public class RoleService(RoleManager<ApplicationRole> roleManager, ApplicationDbcontext dbcontext) : IRoleService
{
    private readonly RoleManager<ApplicationRole> roleManager = roleManager;
    private readonly ApplicationDbcontext dbcontext = dbcontext;


    public async Task<Result<RoleDetailsResponse>> GetRoleByIdAsync(string RollId)
    {
        var role = await roleManager.FindByIdAsync(RollId);

        if (role == null)
            return Result.Failure<RoleDetailsResponse>(RolesErrors.NotFound);

        var permissions = await roleManager.GetClaimsAsync(role);

        var response = new RoleDetailsResponse(role.Id, role.Name!, role.IsDeleted);

        return Result.Success(response);
    }

    public async Task<Result<IEnumerable<RolesResponse>>> GetRolesAsync(bool? IncludeDisable = false)
    {
        var roles = await roleManager.Roles
            .Where(c => !c.IsDeleted || IncludeDisable == true)
            .ProjectToType<RolesResponse>()
            .ToListAsync();

        return Result.Success<IEnumerable<RolesResponse>>(roles);
    }

    public async Task<Result> ToggleStatusAsync(string RoleName)
    {
        if (await roleManager.FindByNameAsync(RoleName) is not { } role)
            return Result.Failure(RolesErrors.NotFound);

        role.IsDeleted = !role.IsDeleted;

        await roleManager.UpdateAsync(role);

        return Result.Success();
    }

    public async Task<Result> UpdateRoleAsync(RoleRequest request)
    {
        if (await roleManager.FindByNameAsync(request.OldName) is not { } role)
            return Result.Failure(RolesErrors.NotFound);

        var roleisexists = await roleManager.Roles.AnyAsync(x => x.Name == request.NewName);

        if (roleisexists)
            return Result.Failure(RolesErrors.DaplicatedRole);

        role.Name = request.NewName;

        var result = await roleManager.UpdateAsync(role);

        if (result.Succeeded)
        {
            await dbcontext.SaveChangesAsync();
            return Result.Success();

        }

        var error = result.Errors.First();
        return Result.Failure(new Error(error.Code, error.Description, StatusCodes.Status400BadRequest));


    }
}
