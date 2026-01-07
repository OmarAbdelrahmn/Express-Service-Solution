using Application.Abstraction;
using Application.Abstraction.Consts;
using Application.Abstraction.Errors;
using Application.Auth;
using Application.Authentication;
using Application.Contracts.Auth;
using Domain;
using Domain.Entities;
using Mapster;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace Application.Services.Auth;

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

        if (await manager.FindByNameAsync(request.UserName) is not { } user)
            return Result.Failure<AuthResponse>(UserErrors.InvalidCredentials);

        if (user.IsDisable)
            return Result.Failure<AuthResponse>(UserErrors.Disableuser);

        var userRole = await manager.GetRolesAsync(user);


        if(userRole.Contains("Member"))
            return Result.Failure<AuthResponse>(UserErrors.InvalidCredentials);
        

        var result = await signInMaganager.PasswordSignInAsync(user, request.Password, false, true);

        if (result.Succeeded)
        {
            var userRoles = await manager.GetRolesAsync(user);

            var (Token, ExpiresIn) = jwtProvider.GenerateToken(user, userRoles);

            await manager.UpdateAsync(user);

            var response = new AuthResponse(
                user.Id,
                user.UserName!,
                Token,
                ExpiresIn
            );

            user.LastLogin = DateTime.UtcNow.AddHours(3);

            return Result.Success(response);
        }

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
