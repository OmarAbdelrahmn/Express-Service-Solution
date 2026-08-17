using Application.Abstraction;
using Application.Abstraction.Consts;
using Application.Abstraction.Errors;
using Application.Authentication;
using Application.Contracts.Auth;
using Domain.Entities;
using Mapster;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Service.Auth;

public class AuthService(
    UserManager<ApplicationUser> manager,
    SignInManager<ApplicationUser> signInManager
    , IJwtProvider jwtProvider,
    ILogger<AuthService> logger,
    RoleManager<ApplicationRole> _roleManager
    ) : IAuthService
{
    private readonly UserManager<ApplicationUser> manager = manager;
    private readonly SignInManager<ApplicationUser> signInMaganager = signInManager;
    private readonly IJwtProvider jwtProvider = jwtProvider;
    private readonly ILogger<AuthService> logger = logger;
    private readonly RoleManager<ApplicationRole> roleManager = _roleManager;

    public async Task<Result<AuthResponse>> SingInAsync(AuthRequest request)
    {
        var userName = request.UserName?.Trim();
        if (string.IsNullOrWhiteSpace(userName) ||
            userName.Length > 256 ||
            string.IsNullOrEmpty(request.Password) ||
            request.Password.Length > 128)
        {
            logger.LogWarning("Rejected an admin login request with invalid input shape.");
            return Result.Failure<AuthResponse>(UserErrors.InvalidCredentials);
        }

        if (await manager.FindByNameAsync(userName) is not { } user)
        {
            logger.LogWarning("Admin login failed because account {UserName} was not found.", userName);
            return Result.Failure<AuthResponse>(UserErrors.InvalidCredentials);
        }

        if (user.IsDisable)
        {
            logger.LogWarning("Admin login rejected for disabled account {UserId}.", user.Id);
            return Result.Failure<AuthResponse>(UserErrors.Disableuser);
        }

        var userRole = await manager.GetRolesAsync(user);


        if (userRole.Contains("Member"))
        {
            logger.LogWarning("Admin login rejected because account {UserId} has the Member role.", user.Id);
            return Result.Failure<AuthResponse>(UserErrors.InvalidCredentials);
        }


        var result = await signInMaganager.PasswordSignInAsync(user, request.Password, false, true);

        if (result.Succeeded)
        {
            var userRoles = await manager.GetRolesAsync(user);

            var (Token, ExpiresIn) = jwtProvider.GenerateToken(user, userRoles);

            user.LastLogin = DateTime.UtcNow;


            await manager.UpdateAsync(user);
            logger.LogInformation("Admin login succeeded for account {UserId}.", user.Id);

            var response = new AuthResponse(
                user.Id,
                user.UserName!,
                Token,
                ExpiresIn
            );

            return Result.Success(response);
        }

        logger.LogWarning(
            "Admin login failed for account {UserId}. LockedOut: {LockedOut}; NotAllowed: {NotAllowed}; FailedCount: {FailedCount}; LockoutEnd: {LockoutEnd}.",
            user.Id,
            result.IsLockedOut,
            result.IsNotAllowed,
            user.AccessFailedCount,
            user.LockoutEnd);

        var error = result.IsNotAllowed ?
             UserErrors.EmailNotConfirmed :
             result.IsLockedOut ?
             UserErrors.userLockedout :
             UserErrors.InvalidCredentials;


        return Result.Failure<AuthResponse>(error);

    }

    public async Task<Result> RegisterAsync(RegisterRequest request)
    {
        var emailisex = await manager.Users.AnyAsync(i => i.UserName == request.UserName);

        if (emailisex)
            return Result.Failure(UserErrors.EmailAlreadyExist);

        var user = request.Adapt<ApplicationUser>();

        var result = await manager.CreateAsync(user, request.Password);

        if (result.Succeeded)
        {
            var role = await roleManager.FindByIdAsync(DefaultRoles.MemberRoleId);

            await manager.AddToRoleAsync(user, role!.Name!);

            return Result.Success();
        }
        var errors = result.Errors.First();
        return Result.Failure(new Error(errors.Code, errors.Description, StatusCodes.Status400BadRequest));

    }

    public async Task<Result> AdminRegisterAsync(RegisterRequest request)
    {
        var emailisex = await manager.Users.AnyAsync(i => i.UserName == request.UserName);

        if (emailisex)
            return Result.Failure(UserErrors.EmailAlreadyExist);

        var user = request.Adapt<ApplicationUser>();

        var result = await manager.CreateAsync(user, request.Password);

        if (result.Succeeded)
        {
            var role = await roleManager.FindByIdAsync(DefaultRoles.AdminRoleId);

            await manager.AddToRoleAsync(user, role!.Name!);

            return Result.Success();
        }
        var errors = result.Errors.First();
        return Result.Failure(new Error(errors.Code, errors.Description, StatusCodes.Status400BadRequest));
    }

    public async Task<Result> MasterRegisterAsync(RegisterRequest request)
    {
        var emailisex = await manager.Users.AnyAsync(i => i.UserName == request.UserName);

        if (emailisex)
            return Result.Failure(UserErrors.EmailAlreadyExist);

        var user = request.Adapt<ApplicationUser>();

        var result = await manager.CreateAsync(user, request.Password);

        if (result.Succeeded)
        {
            var role = await roleManager.FindByIdAsync(DefaultRoles.MasterRoleId);

            await manager.AddToRoleAsync(user, role!.Name!);

            return Result.Success();
        }
        var errors = result.Errors.First();
        return Result.Failure(new Error(errors.Code, errors.Description, StatusCodes.Status400BadRequest));
    }
}
