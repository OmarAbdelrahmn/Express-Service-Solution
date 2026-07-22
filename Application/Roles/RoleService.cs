using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.Roles;
using Application.Contracts.Common;
using Domain;
using Domain.Entities;
using Mapster;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace Application.Roles;

public class RoleService(RoleManager<ApplicationRole> roleManager, ApplicationDbcontext dbcontext, IDistributedCache cache) : IRoleService
{
    private readonly RoleManager<ApplicationRole> roleManager = roleManager;
    private readonly ApplicationDbcontext dbcontext = dbcontext;
    private readonly IDistributedCache cache = cache;


    public async Task<Result<RoleDetailsResponse>> GetRoleByIdAsync(string RollId)
    {
        var role = await roleManager.FindByIdAsync(RollId);

        if (role == null)
            return Result.Failure<RoleDetailsResponse>(RolesErrors.NotFound);

        var permissions = await roleManager.GetClaimsAsync(role);

        var response = new RoleDetailsResponse(role.Id, role.Name!, role.IsDeleted);

        return Result.Success(response);
    }

    public async Task<Result<PagedResponse<RolesResponse>>> GetRolesAsync(PaginationRequest pagination, bool? IncludeDisable = false, CancellationToken cancellationToken = default)
    {
        var pageNumber = pagination.NormalizedPageNumber;
        var pageSize = pagination.NormalizedPageSize;
        var cacheKey = $"roles:{IncludeDisable == true}:{pageNumber}:{pageSize}";
        var cached = await cache.GetStringAsync(cacheKey, cancellationToken);

        if (cached is not null)
            return Result.Success(JsonSerializer.Deserialize<PagedResponse<RolesResponse>>(cached)!);

        var query = roleManager.Roles
            .Where(c => !c.IsDeleted || IncludeDisable == true)
            .AsNoTracking();
        var totalCount = await query.CountAsync(cancellationToken);
        var roles = await query
            .OrderBy(c => c.Name)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ProjectToType<RolesResponse>()
            .ToListAsync(cancellationToken);

        var response = new PagedResponse<RolesResponse>(roles, pageNumber, pageSize, totalCount);
        await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(response), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        }, cancellationToken);

        return Result.Success(response);
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
