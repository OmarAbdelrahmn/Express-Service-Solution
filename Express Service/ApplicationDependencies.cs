using Application.Admin;
using Application.Auth;
using Application.Authentication;
using Application.Roles;
using Application.Service;
using Application.Service.DE;
using Application.Service.Empolyee;
using Application.Service.Member;
using Application.Service.Reports;
using Application.Service.Riders;
using Application.Services.Auth;
using Application.User;
using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;
using Domain;
using Domain.Entities;
using FluentValidation;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Express_Service;

public static class ApplicationDependencies
{
    public static IServiceCollection AddDependencies(this IServiceCollection Services, IConfiguration configuration)
    {
        Services.AddControllers();
        Services.AddEndpointsApiExplorer();
        Services.AddScoped<IJwtProvider, JwtProvider>();
        Services.AddScoped<IAuthService, AuthService>();
        Services.AddScoped<IUserService, UserServices>();
        Services.AddScoped<IAdminService, AdminService>();
        Services.AddScoped<IRoleService, RoleService>();
        Services.AddScoped<IEmployeeService, EmployeeService>();
        Services.AddScoped<ICompanyService, CompanyService>();
        Services.AddScoped<IHousingService, HousingService>();
        Services.AddScoped<IVehicleService, VehicleService>();
        Services.AddScoped<IRiderService, RiderService>();
        Services.AddScoped<IRiderSub, RiderSub>();
        Services.AddScoped<IRiderShiftService, RiderShiftService>();
        Services.AddScoped<IReportService, ReportService>();
        Services.AddScoped<ITemp, Temp>();
        Services.AddScoped<IHungerDisabilityService, HungerDisabilityService>();
        Services.AddScoped<IImportService, ImportService>();
        Services.AddScoped<IRiderWorkingIdHistoryService, RiderWorkingIdHistoryService>();
        Services.AddScoped<IMemberService, MemberService>();
        // In your Program.cs or Startup.cs
        Services.AddScoped<IDeletedEmployeeImportService, DeletedEmployeeImportService>();

        Services.AddAuth(configuration)
                .AddMappester()
                .AddFluentValidation()
                .AddSwagger()
                .AddDatabase(configuration)
                .AddCORS()
                .AddCaching()
                ;

        return Services;
    }

    public static IServiceCollection AddFluentValidation(this IServiceCollection Services)
    {
        Services
            .AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        return Services;
    }

    public static IServiceCollection AddSwagger(this IServiceCollection Services)
    {
        Services
            .AddSwaggerGen();
        return Services;
    }

    public static IServiceCollection AddMappester(this IServiceCollection Services)
    {
        var mappingConfig = TypeAdapterConfig.GlobalSettings;
        mappingConfig.Scan(Assembly.GetExecutingAssembly());

        Services.AddSingleton<IMapper>(new Mapper(mappingConfig));

        return Services;
    }

    public static IServiceCollection AddDatabase(this IServiceCollection Services, IConfiguration c)
    {
        var ConnectionString = c.GetConnectionString("DefaultConnection") ??
            throw new InvalidOperationException("Connection string is not found in the configuration file");

        Services.AddDbContext<ApplicationDbcontext>(options =>
    options.UseSqlServer(
        c.GetConnectionString("DefaultConnection")
    ));


        return Services;
    }

    public static IServiceCollection AddAuth(this IServiceCollection Services, IConfiguration configuration)
    {


        Services.AddIdentity<ApplicationUser, ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbcontext>()
            .AddDefaultTokenProviders();

        Services.Configure<JwtOptions>(configuration.GetSection("Jwt"));


        var Jwtsetting = configuration.GetSection("Jwt").Get<JwtOptions>();

        Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(o =>
        {
            o.SaveToken = true;
            o.TokenValidationParameters = new TokenValidationParameters
            {


                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidAudience = Jwtsetting?.Audience,
                ValidIssuer = Jwtsetting?.Issuer,

                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Jwtsetting?.Key!))
            };
        });
        Services.Configure<IdentityOptions>(options =>
        {
            // Default Lockout settings.
            //options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            //options.Lockout.MaxFailedAccessAttempts = 5;
            //options.Lockout.AllowedForNewUsers = true;
            options.Password.RequiredLength = 6;
            options.SignIn.RequireConfirmedEmail = false;
            options.User.RequireUniqueEmail = false;


        });

        return Services;
    }
    public static IServiceCollection AddCORS(this IServiceCollection Services)
    {
        Services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
                builder
                        .WithOrigins("http://localhost:3000")
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials());
        });
        return Services;
    }
    public static IServiceCollection AddCaching(this IServiceCollection Services)
    {
        Services.AddResponseCaching();
        Services.AddMemoryCache();
        return Services;
    }


}
