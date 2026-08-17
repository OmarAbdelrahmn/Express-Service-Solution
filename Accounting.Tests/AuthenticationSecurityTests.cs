using Application.Authentication;
using Application.Service.Admin;
using Domain;
using Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using Xunit;

namespace Accounting.Tests;

public class AuthenticationSecurityTests
{
    private const string OriginalPassword = "Original!Password12";
    private const string SupportKey = "phone-support-key-that-is-long-and-random";

    [Fact]
    public async Task SupportReset_RejectsWrongKey_WithoutChangingAccount()
    {
        await using var provider = CreateIdentityProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbcontext>();
        var user = await CreateLockedUserAsync(manager);
        var originalHash = user.PasswordHash;
        var service = new AdminService(
            manager,
            db,
            Options.Create(new SupportPasswordResetOptions { Key = SupportKey }),
            NullLogger<AdminService>.Instance);

        var result = await service.SupportResetPasswordAsync(user.UserName!, "wrong-key");
        var stored = await manager.FindByIdAsync(user.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("SupportReset.Unauthorized", result.Error.Code);
        Assert.Equal(originalHash, stored!.PasswordHash);
        Assert.NotNull(stored.LockoutEnd);
        Assert.Equal(4, stored.AccessFailedCount);
    }

    [Fact]
    public async Task SupportReset_WithCorrectKey_ReplacesPasswordAndClearsLockout()
    {
        await using var provider = CreateIdentityProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbcontext>();
        var user = await CreateLockedUserAsync(manager);
        var oldSecurityStamp = user.SecurityStamp;
        var service = new AdminService(
            manager,
            db,
            Options.Create(new SupportPasswordResetOptions { Key = SupportKey }),
            NullLogger<AdminService>.Instance);

        var beforeReset = await signInManager.CheckPasswordSignInAsync(user, OriginalPassword, true);

        var first = await service.SupportResetPasswordAsync(user.UserName!, SupportKey);
        var firstPassword = first.Value.TemporaryPassword;
        var second = await service.SupportResetPasswordAsync(user.UserName!, SupportKey);
        var stored = await manager.FindByIdAsync(user.Id);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.True(beforeReset.IsLockedOut);
        Assert.NotEqual(firstPassword, second.Value.TemporaryPassword);
        Assert.False(await manager.CheckPasswordAsync(stored!, OriginalPassword));
        Assert.True(await manager.CheckPasswordAsync(stored!, second.Value.TemporaryPassword));
        Assert.Null(stored!.LockoutEnd);
        Assert.Equal(0, stored.AccessFailedCount);
        Assert.NotEqual(oldSecurityStamp, stored.SecurityStamp);
        Assert.True((await signInManager.CheckPasswordSignInAsync(
            stored,
            second.Value.TemporaryPassword,
            true)).Succeeded);
    }

    [Fact]
    public async Task JwtAccountValidator_RejectsDisabledUserAndChangedSecurityStamp()
    {
        await using var db = CreateDbContext();
        var user = new ApplicationUser
        {
            Id = "jwt-user",
            UserName = "jwt-user",
            NormalizedUserName = "JWT-USER",
            SecurityStamp = "stamp-1"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new JwtAccountValidator(db);

        Assert.True(await validator.IsCurrentAsync(user.Id, "stamp-1"));
        Assert.False(await validator.IsCurrentAsync(user.Id, "old-stamp"));

        user.IsDisable = true;
        await db.SaveChangesAsync();

        Assert.False(await validator.IsCurrentAsync(user.Id, "stamp-1"));
    }

    [Fact]
    public void JwtProvider_UsesUtcExpiryAndIncludesSecurityStamp()
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "tests",
            Audience = "tests",
            Key = "a-test-signing-key-that-is-at-least-32-bytes-long",
            ExpiryIn = 300
        });
        var provider = new JwtProvider(options);
        var user = new ApplicationUser
        {
            Id = "jwt-user",
            UserName = "jwt-user",
            SecurityStamp = "current-stamp"
        };
        var before = DateTime.UtcNow;

        var generated = provider.GenerateToken(user, ["Admin"]);
        var after = DateTime.UtcNow;
        var token = new JwtSecurityTokenHandler().ReadJwtToken(generated.Token);

        Assert.InRange(token.ValidTo, before.AddMinutes(299), after.AddMinutes(301));
        Assert.Equal("current-stamp", token.Claims.Single(x => x.Type == JwtProvider.SecurityStampClaimType).Value);
    }

    [Fact]
    public async Task IdentityBootstrapper_RotatesLegacySeededCredentialsAndEnablesLockout()
    {
        await using var provider = CreateIdentityProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        Assert.True((await roleManager.CreateAsync(new ApplicationRole
        {
            Id = "77B96CED-F902-47EF-AE95-ABBE14A8CA22",
            Name = "Admin"
        })).Succeeded);
        Assert.True((await roleManager.CreateAsync(new ApplicationRole
        {
            Id = "17B96C5D-F502-47TF-EE95-ABVN14A3CA22",
            Name = "Master"
        })).Succeeded);

        var admin = await CreateLegacyBootstrapUserAsync(
            manager,
            "59724D2D-E2B5-4C67-AB6F-D93478347B03",
            "Admin",
            "AQAAAAIAAYagAAAAEA/zZpuqFzbTSnicQa4Tooll0FGxeDLCE2M5TALeSVR6BGE45Era3fs5IhF5zU2ZyQ==");
        var master = await CreateLegacyBootstrapUserAsync(
            manager,
            "59726D2D-E2B5-4C67-AB6F-D93478317B03",
            "Master",
            "AQAAAAIAAYagAAAAEFpg1iN3qC51jcJrS5Ea9/Ab1Xi7kXnwjCrMOynu6YUpw7q1mrTe8yz+5Cx2W01t5A==");

        const string newAdminPassword = "New!AdminPassword123";
        const string newMasterPassword = "New!MasterPassword123";
        var bootstrapper = new IdentityBootstrapper(
            manager,
            roleManager,
            Options.Create(new IdentityBootstrapOptions
            {
                AdminPassword = newAdminPassword,
                MasterPassword = newMasterPassword
            }),
            NullLogger<IdentityBootstrapper>.Instance);

        await bootstrapper.InitializeAsync();

        admin = (await manager.FindByIdAsync(admin.Id))!;
        master = (await manager.FindByIdAsync(master.Id))!;
        Assert.True(await manager.CheckPasswordAsync(admin, newAdminPassword));
        Assert.True(await manager.CheckPasswordAsync(master, newMasterPassword));
        Assert.True(admin.LockoutEnabled);
        Assert.True(master.LockoutEnabled);
        Assert.True(await manager.IsInRoleAsync(admin, "Admin"));
        Assert.True(await manager.IsInRoleAsync(master, "Master"));
    }

    private static async Task<ApplicationUser> CreateLockedUserAsync(UserManager<ApplicationUser> manager)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = $"user-{Guid.NewGuid():N}",
            LockoutEnabled = true,
            AccessFailedCount = 4,
            LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        var result = await manager.CreateAsync(user, OriginalPassword);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(x => x.Description)));
        return user;
    }

    private static async Task<ApplicationUser> CreateLegacyBootstrapUserAsync(
        UserManager<ApplicationUser> manager,
        string id,
        string userName,
        string legacyHash)
    {
        var user = new ApplicationUser
        {
            Id = id,
            UserName = userName,
            LockoutEnabled = false
        };
        Assert.True((await manager.CreateAsync(user, OriginalPassword)).Succeeded);
        user.PasswordHash = legacyHash;
        Assert.True((await manager.UpdateAsync(user)).Succeeded);
        return user;
    }

    private static ServiceProvider CreateIdentityProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication();
        services.AddDataProtection();
        services.AddDbContext<ApplicationDbcontext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<ApplicationRole>()
            .AddSignInManager()
            .AddEntityFrameworkStores<ApplicationDbcontext>()
            .AddDefaultTokenProviders();
        return services.BuildServiceProvider();
    }

    private static ApplicationDbcontext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbcontext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
