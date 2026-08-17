using Application.Abstraction.Consts;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SurveyBasket.Abstraction.Consts;

namespace Application.Authentication;

public interface IIdentityBootstrapper
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public sealed class IdentityBootstrapper(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IOptions<IdentityBootstrapOptions> options,
    ILogger<IdentityBootstrapper> logger) : IIdentityBootstrapper
{
    // Used only to recognize and rotate the two legacy model-seeded credentials.
    private const string LegacyAdminPasswordHash =
        "AQAAAAIAAYagAAAAEA/zZpuqFzbTSnicQa4Tooll0FGxeDLCE2M5TALeSVR6BGE45Era3fs5IhF5zU2ZyQ==";
    private const string LegacyMasterPasswordHash =
        "AQAAAAIAAYagAAAAEFpg1iN3qC51jcJrS5Ea9/Ab1Xi7kXnwjCrMOynu6YUpw7q1mrTe8yz+5Cx2W01t5A==";

    private readonly IdentityBootstrapOptions bootstrapOptions = options.Value;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await EnsureAccountAsync(
            DefaultUsers.AdminId,
            DefaultUsers.AdminName,
            DefaultRoles.Admin,
            bootstrapOptions.AdminPassword,
            LegacyAdminPasswordHash);

        cancellationToken.ThrowIfCancellationRequested();

        await EnsureAccountAsync(
            DefaultUsers.MasterId,
            DefaultUsers.MasterName,
            DefaultRoles.Master,
            bootstrapOptions.MasterPassword,
            LegacyMasterPasswordHash);
    }

    private async Task EnsureAccountAsync(
        string userId,
        string userName,
        string roleName,
        string configuredPassword,
        string legacyPasswordHash)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
            throw new InvalidOperationException($"Required identity role '{roleName}' does not exist. Apply database migrations first.");

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            RequireConfiguredPassword(userName, configuredPassword);
            user = new ApplicationUser
            {
                Id = userId,
                UserName = userName,
                EmailConfirmed = true,
                LockoutEnabled = true
            };

            EnsureSucceeded(
                await userManager.CreateAsync(user, configuredPassword),
                $"create bootstrap account '{userName}'");
            logger.LogInformation("Created bootstrap identity account {UserName}.", userName);
        }
        else if (string.Equals(user.PasswordHash, legacyPasswordHash, StringComparison.Ordinal))
        {
            RequireConfiguredPassword(userName, configuredPassword);
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
            EnsureSucceeded(
                await userManager.ResetPasswordAsync(user, resetToken, configuredPassword),
                $"rotate legacy bootstrap password for '{userName}'");
            logger.LogWarning("Rotated a legacy model-seeded password for {UserName}.", userName);
            user.AccessFailedCount = 0;
            user.LockoutEnd = null;
        }

        if (!user.LockoutEnabled)
        {
            user.LockoutEnabled = true;
            EnsureSucceeded(
                await userManager.UpdateAsync(user),
                $"enable lockout protection for '{userName}'");
        }

        if (!await userManager.IsInRoleAsync(user, roleName))
        {
            EnsureSucceeded(
                await userManager.AddToRoleAsync(user, roleName),
                $"assign role '{roleName}' to '{userName}'");
        }
    }

    private static void RequireConfiguredPassword(string userName, string configuredPassword)
    {
        if (string.IsNullOrWhiteSpace(configuredPassword))
        {
            throw new InvalidOperationException(
                $"IdentityBootstrap password for '{userName}' is required because its account is missing or still uses a legacy seeded credential.");
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded)
            return;

        var errors = string.Join(", ", result.Errors.Select(error => $"{error.Code}: {error.Description}"));
        throw new InvalidOperationException($"Failed to {operation}. {errors}");
    }
}
