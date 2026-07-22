using Application.Authentication;
using Application.EmailWarmup;
using Application.Roles;
using Application.Service.Admin;
using Application.Service.AI;
using Application.Service.Accounting;
using Application.Service.Auth;
using Application.Service.Backgroundimports;
using Application.Service.Dahsboard;
using Application.Service.DailyReport;
using Application.Service.Dashboard;
using Application.Service.DE;
using Application.Service.EmailWarmup;
using Application.Service.EmployeesFiles;
using Application.Service.Empolyee;
using Application.Service.EscapedEmployee;
using Application.Service.Freelancer;
using Application.Service.HousingInventory;
using Application.Service.Hungerdisa;
using Application.Service.HungerReports;
using Application.Service.Import;
using Application.Service.InventoryAudit;
using Application.Service.KetaValidation;
using Application.Service.Member;
using Application.Service.MonthlyValidity;
using Application.Service.Orders;
using Application.Service.Organization;
using Application.Service.Ledger;
using Application.Service.FinancialAccess;
using Application.Service.FinancialOperations;
using Application.Service.Compensation;
using Application.Service.PlatformImports;
using Application.Service.RiderPayroll;
using Application.Service.AccountingStorage;
using Application.Service.AccountingPosting;
using Application.Service.AccountingOutbox;
using Application.Service.AccountingFiles;
using Application.Service.Petrol;
using Application.Service.Reminder;
using Application.Service.Reports;
using Application.Service.Return;
using Application.Service.RiderAccessory;
using Application.Service.Riders;
using Application.Service.SparePart;
using Application.Service.SupplierSer;
using Application.Service.temp;
using Application.Service.OutageShiftPerformances;
using Application.Service.Transfer;
using Application.Service.TransporterShifts;
using Application.Service.User;
using Application.Service.VehiclePermission;
using Application.Service.Wallet;
using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;
using Asp.Versioning;
using Domain;
using Domain.Entities;
using FluentValidation;
using Hangfire;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
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
        Services.AddScoped<IOrganizationSettingsService, OrganizationSettingsService>();
        Services.AddScoped<ILedgerService, LedgerService>();
        Services.AddScoped<IFinancialAccessService, FinancialAccessService>();
        Services.AddScoped<IFinancialOperationsService, FinancialOperationsService>();
        Services.AddScoped<ICompensationService, CompensationService>();
        Services.AddScoped<IPlatformImportService, PlatformImportService>();
        Services.AddSingleton<IPrivateAccountingFileStorage, EncryptedPrivateAccountingFileStorage>();
        Services.AddScoped<IAccountingFileService, AccountingFileService>();
        Services.AddScoped<IAccountingPostingService, AccountingPostingService>();
        Services.AddScoped<IAccountingOutboxJob, AccountingOutboxJob>();
        Services.AddScoped<IAccountingOutboxDispatcher, LoggingAccountingOutboxDispatcher>();
        Services.AddScoped<IRiderPayrollService, RiderPayrollService>();
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
        Services.AddScoped<IInventoryAuditService, InventoryAuditService>();
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
        Services.AddScoped<IVehiclePermissionRenewalJob, VehiclePermissionRenewalJob>();
        Services.AddScoped<IGeminiService, GeminiService>();
        Services.AddScoped<IEmailWarmupJob, EmailWarmupJob>();
        Services.AddScoped<IOrderService, OrderService>();
        Services.AddScoped<ITransporterShiftService, TransporterShiftService>();
        Services.AddScoped<IOutRiderInfoService, OutRiderInfoService>();
        Services.AddScoped<IOutageShiftPerformanceService, OutageShiftPerformanceService>();
        Services.AddScoped<ICostTrackingService, CostTrackingService>();
        Services.AddMemoryCache();
        Services.AddScoped<IAiConfirmationStore, AiConfirmationStore>();
        Services.AddScoped<IAiToolDispatcher, AiToolDispatcher>();
        Services.AddScoped<IHousingInventorySyncService, HousingInventorySyncService>();
        Services.AddScoped<IReminderService, ReminderService>();
        Services.AddScoped<IAbsentReportJob, AbsentReportJob>();
        Services.AddScoped<IItemMovementReportService, ItemMovementReportService>();
        Services.AddScoped<IAccountingPeriodService, AccountingPeriodService>();
        Services.AddScoped<IAccountingImportService, AccountingImportService>();
        Services.AddScoped<IAccountingSalaryService, AccountingSalaryService>();
        Services.AddScoped<IAccountingPaymentService, AccountingPaymentService>();
        Services.AddScoped<IRiderAccountingProfileService, RiderAccountingProfileService>();
        Services.AddScoped<ICompanyFinanceService, CompanyFinanceService>();
        Services.AddScoped<IAccountingReportService, AccountingReportService>();

        Services.AddScoped<IDailyReportJob, DailyReportJob>();
        Services.AddScoped<IAbsentReportEmailSender, AbsentReportEmailSender>();

        Services.AddScoped<IMonthlyProgressReportJob, MonthlyProgressReportJob>();
        Services.AddScoped<IMonthlyProgressReportEmailSender, MonthlyProgressReportEmailSender>();

        Services.AddSingleton<ReportScheduler>();

        Services.Configure<DailyReportSettings>(
            configuration.GetSection("DailyReport"));
        Services.Configure<AccountingStorageOptions>(configuration.GetSection(AccountingStorageOptions.SectionName));

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
                .AddCaching(configuration)
                .AddApiVersioning(options =>
                {
                    options.DefaultApiVersion = new ApiVersion(1, 0);
                    options.AssumeDefaultVersionWhenUnspecified = true;
                    options.ReportApiVersions = true;
                    options.ApiVersionReader = ApiVersionReader.Combine(
                        new QueryStringApiVersionReader("api-version"),
                        new HeaderApiVersionReader("x-api-version"));
                })
                .AddApiExplorer(options =>
                {
                    options.GroupNameFormat = "'v'VVV";
                    options.SubstituteApiVersionInUrl = false;
                });
                ;

        return Services;
    }

    public static IServiceCollection AddFluentValidation(this IServiceCollection Services)
    {
        Services
            .AddValidatorsFromAssembly(typeof(IJwtProvider).Assembly);

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
        mappingConfig.Scan(typeof(IJwtProvider).Assembly);

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

        var jwtSettings = configuration.GetSection("Jwt").Get<JwtOptions>()
            ?? throw new InvalidOperationException("JWT configuration is missing.");

        if (string.IsNullOrWhiteSpace(jwtSettings.Key) ||
            string.IsNullOrWhiteSpace(jwtSettings.Issuer) ||
            string.IsNullOrWhiteSpace(jwtSettings.Audience) ||
            jwtSettings.ExpiryIn <= 0)
        {
            throw new InvalidOperationException(
                "JWT configuration must be supplied through secure configuration.");
        }

        Services.Configure<JwtOptions>(configuration.GetSection("Jwt"));

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
                ValidAudience = jwtSettings.Audience,
                ValidIssuer = jwtSettings.Issuer,

                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key))
            };
        });
        Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        Services.Configure<IdentityOptions>(options =>
        {
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;
            options.Password.RequiredLength = 12;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.User.RequireUniqueEmail = true;


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
    public static IServiceCollection AddCaching(this IServiceCollection Services, IConfiguration configuration)
    {
        Services.AddResponseCaching();
        var redisConnection = configuration.GetConnectionString("Redis") ?? configuration["Redis:ConnectionString"];
        if (string.IsNullOrWhiteSpace(redisConnection))
            Services.AddDistributedMemoryCache();
        else
            Services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);

        Services.AddMemoryCache();
        Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter("api", limiter =>
            {
                limiter.PermitLimit = 100;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
            options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(context =>
                System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });
        return Services;
    }


}
