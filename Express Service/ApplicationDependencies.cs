using Application.Authentication;
using Application.Roles;
using Application.Service.Admin;
using Application.Service.Auth;
using Application.Service.Backgroundimports;
using Application.Service.Dahsboard;
using Application.Service.DailyReport;
using Application.Service.Dashboard;
using Application.Service.DE;
using Application.Service.EmployeesFiles;
using Application.Service.Empolyee;
using Application.Service.Escaped;
using Application.Service.EscapedEmployee;
using Application.Service.Freelancer;
using Application.Service.Hungerdisa;
using Application.Service.HungerReports;
using Application.Service.Import;
using Application.Service.KetaValidation;
using Application.Service.Member;
using Application.Service.MonthlyValidity;
using Application.Service.Petrol;
using Application.Service.Reports;
using Application.Service.Return;
using Application.Service.RiderAccessory;
using Application.Service.Riders;
using Application.Service.SparePart;
using Application.Service.SupplierSer;
using Application.Service.temp;
using Application.Service.Transfer;
using Application.Service.User;
using Application.Service.Wallet;
using Domain;
using Domain.Entities;
using FluentValidation;
using Hangfire;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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
        Services.AddScoped<IDeletedEmployeeImportService, DeletedEmployeeImportService>();
        Services.AddSingleton<IBackgroundImportService, BackgroundImportService>();
        Services.AddScoped<ISparePartService, SparePartService>();
        Services.AddScoped<IRiderAccessoryService, RiderAccessoryService>();
        Services.AddScoped<ITransferService, TransferService>();
        Services.AddScoped<IBillService, BillService>();
        Services.AddScoped<ISupplierService, SupplierService>();
        Services.AddScoped<IReturnService, ReturnService>();
        Services.AddScoped<IFreelancerService, FreelancerService>();
        Services.AddScoped<IMonthlyValidityService, MonthlyValidityService>();
        Services.AddScoped<IHungerReportService, HungerReportService>();
        Services.AddScoped<IEmployeeDocumentsService, EmployeeDocumentsService>();
        Services.AddScoped<IDashboardService, DashboardService>();
        Services.AddScoped<IWalletService, WalletService>();
        Services.AddScoped<IEscapedEmployeeService, EscapedEmployeeService>();
        Services.AddScoped<IPetrolService, PetrolService>();

        #region Hnagfire + Daily Report Job
        // ── Hangfire ────────────────────────────────────────────────────────────────
        Services.AddHangfire(config =>
            config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseSqlServerStorage(configuration.GetConnectionString("DefaultConnection")));

        Services.AddHangfireServer(options =>
        {
            options.WorkerCount = 2;          // keep low — this isn't a heavy workload
            options.Queues = ["daily-reports", "default"];
        });

        // ── Daily Report Services ────────────────────────────────────────────────────
        Services.Configure<DailyReportSettings>(configuration.GetSection("DailyReport"));
        Services.AddScoped<IDailyReportJob, DailyReportJob>();
        Services.AddScoped<IDailyReportEmailSender, DailyReportEmailSender>();

#endregion

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
