using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Authentication;
using Application.Contracts.Users;
using Domain;
using Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Data;
using System.Security.Cryptography;

namespace Application.Service.Admin;

public class AdminService(
     UserManager<ApplicationUser> manager
    , ApplicationDbcontext dbcontext
    , IOptions<SupportPasswordResetOptions> supportResetOptions
    , ILogger<AdminService> logger) : IAdminService
{
    private readonly UserManager<ApplicationUser> manager = manager;
    private readonly ApplicationDbcontext dbcontext = dbcontext;
    private readonly SupportPasswordResetOptions supportResetOptions = supportResetOptions.Value;
    private readonly ILogger<AdminService> logger = logger;


    public async Task<Result<SupportPasswordResetResponse>> SupportResetPasswordAsync(
        string userName,
        string? supportKey,
        CancellationToken cancellationToken = default)
    {
        if (!supportResetOptions.IsConfigured)
        {
            logger.LogError("Support password reset is unavailable because its support key is not configured securely.");
            return Result.Failure<SupportPasswordResetResponse>(UserErrors.SupportResetUnavailable);
        }

        if (!supportResetOptions.Matches(supportKey))
        {
            logger.LogWarning("Rejected a support password reset request with an invalid support key.");
            return Result.Failure<SupportPasswordResetResponse>(UserErrors.SupportResetUnauthorized);
        }

        if (string.IsNullOrWhiteSpace(userName) || userName.Length > 256)
            return Result.Failure<SupportPasswordResetResponse>(UserErrors.SupportResetUserNotFound);

        var user = await manager.FindByNameAsync(userName.Trim());
        if (user is null)
        {
            logger.LogWarning("Support password reset requested for an unknown account.");
            return Result.Failure<SupportPasswordResetResponse>(UserErrors.SupportResetUserNotFound);
        }

        IDbContextTransaction? transaction = null;
        try
        {
            if (dbcontext.Database.IsRelational() && dbcontext.Database.CurrentTransaction is null)
                transaction = await dbcontext.Database.BeginTransactionAsync(cancellationToken);

            var temporaryPassword = GenerateTemporaryPassword();
            var resetToken = await manager.GeneratePasswordResetTokenAsync(user);
            var resetResult = await manager.ResetPasswordAsync(user, resetToken, temporaryPassword);
            if (!resetResult.Succeeded)
                return await RollBackFailureAsync(resetResult, transaction, cancellationToken);

            user.AccessFailedCount = 0;
            user.LockoutEnd = null;
            var unlockResult = await manager.UpdateAsync(user);
            if (!unlockResult.Succeeded)
                return await RollBackFailureAsync(unlockResult, transaction, cancellationToken);

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            logger.LogWarning(
                "Support password reset completed for account {UserId}; lockout was cleared and previous tokens were revoked.",
                user.Id);

            return Result.Success(new SupportPasswordResetResponse(
                user.UserName!,
                temporaryPassword,
                DateTimeOffset.UtcNow));
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private static async Task<Result<SupportPasswordResetResponse>> RollBackFailureAsync(
        IdentityResult result,
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
            await transaction.RollbackAsync(cancellationToken);

        var error = result.Errors.First();
        return Result.Failure<SupportPasswordResetResponse>(
            new Error(error.Code, error.Description, StatusCodes.Status400BadRequest));
    }

    private static string GenerateTemporaryPassword()
    {
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string digits = "23456789";
        const string symbols = "!@#$%&*+-_?";
        const int length = 20;
        var all = lower + upper + digits + symbols;
        var characters = new char[length];

        characters[0] = Pick(lower);
        characters[1] = Pick(upper);
        characters[2] = Pick(digits);
        characters[3] = Pick(symbols);
        for (var index = 4; index < characters.Length; index++)
            characters[index] = Pick(all);

        for (var index = characters.Length - 1; index > 0; index--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (characters[index], characters[swapIndex]) = (characters[swapIndex], characters[index]);
        }

        return new string(characters);
    }

    private static char Pick(string source) => source[RandomNumberGenerator.GetInt32(source.Length)];
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

        // Rotate the stamp so a token issued before disable/re-enable can never revive.
        var result = await manager.UpdateSecurityStampAsync(user);
        if (result.Succeeded)
            return Result.Success();

        var error = result.Errors.First();
        return Result.Failure(new Error(error.Code, error.Description, StatusCodes.Status400BadRequest));
    }

}
