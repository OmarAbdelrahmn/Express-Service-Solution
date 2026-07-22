using Application.Abstraction;
using Application.Contracts.Roles;
using Application.Contracts.Common;

namespace Application.Roles;

public interface IRoleService
{
    Task<Result<PagedResponse<RolesResponse>>> GetRolesAsync(PaginationRequest pagination, bool? IncludeDisable = true, CancellationToken cancellationToken = default);
    Task<Result<RoleDetailsResponse>> GetRoleByIdAsync(string RollId);
    Task<Result> ToggleStatusAsync(string RoleName);
    Task<Result> UpdateRoleAsync(RoleRequest request);
}
