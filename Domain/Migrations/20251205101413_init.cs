using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDisable = table.Column<bool>(type: "bit", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    From = table.Column<DateOnly>(type: "date", nullable: true),
                    To = table.Column<DateOnly>(type: "date", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeletedEmployees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IqamaNo = table.Column<long>(type: "bigint", nullable: false),
                    IqamaEndM = table.Column<DateOnly>(type: "date", nullable: false),
                    IqamaEndH = table.Column<DateOnly>(type: "date", nullable: false),
                    PassportNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PassportEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    Sponsor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JobTitle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NameAR = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NameEN = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Country = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DateOfBirth = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IBAN = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    INKSA = table.Column<bool>(type: "bit", nullable: false),
                    HousingId = table.Column<int>(type: "int", nullable: true),
                    WorkingId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TshirtSize = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LicenseNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    VehicleId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeletedEmployees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Housings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    ManagerIqamaNo = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Housings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RiderCompanyHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderId = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderCompanyHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    VehicleNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    VehicleType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SerialNumber = table.Column<int>(type: "int", nullable: false),
                    PlateNumberA = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PlateNumberE = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OwnerId = table.Column<int>(type: "int", nullable: false),
                    OwnerName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ManufactureYear = table.Column<int>(type: "int", nullable: false),
                    Manufacturer = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LicenseExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    VehicleImagePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LicenseImagePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExstraImage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExstraImage1 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.VehicleNumber);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    IqamaNo = table.Column<long>(type: "bigint", nullable: false),
                    IqamaEndM = table.Column<DateOnly>(type: "date", nullable: false),
                    IqamaEndH = table.Column<DateOnly>(type: "date", nullable: false),
                    PassportNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PassportEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    Sponsor = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SponsorNo = table.Column<int>(type: "int", nullable: false),
                    JobTitle = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    NameAR = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameEN = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Country = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    DateOfBirth = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Active"),
                    IBAN = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    INKSA = table.Column<bool>(type: "bit", nullable: false),
                    HousingId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.IqamaNo);
                    table.ForeignKey(
                        name: "FK_Employees_Housings_HousingId",
                        column: x => x.HousingId,
                        principalTable: "Housings",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RiderVehicleStatus",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeIqamaNo = table.Column<long>(type: "bigint", nullable: true),
                    VehicleNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StatusType = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderVehicleStatus", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderVehicleStatus_Vehicles_VehicleNumber",
                        column: x => x.VehicleNumber,
                        principalTable: "Vehicles",
                        principalColumn: "VehicleNumber",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeIqamaNo = table.Column<long>(type: "bigint", nullable: false),
                    ProfileImagePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PassportImagePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IqamaImagePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LicenseImagePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WorkPermitImagePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AdditionImage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AdditionImage1 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AdditionImage2 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AdditionImage3 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeDocuments_Employees_EmployeeIqamaNo",
                        column: x => x.EmployeeIqamaNo,
                        principalTable: "Employees",
                        principalColumn: "IqamaNo",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkingId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmployeeIqamaNo = table.Column<long>(type: "bigint", nullable: false),
                    TshirtSize = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LicenseNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VehicleNumber = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderDetails", x => x.Id);
                    table.UniqueConstraint("AK_RiderDetails_EmployeeIqamaNo", x => x.EmployeeIqamaNo);
                    table.ForeignKey(
                        name: "FK_RiderDetails_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderDetails_Employees_EmployeeIqamaNo",
                        column: x => x.EmployeeIqamaNo,
                        principalTable: "Employees",
                        principalColumn: "IqamaNo",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderDetails_Vehicles_VehicleNumber",
                        column: x => x.VehicleNumber,
                        principalTable: "Vehicles",
                        principalColumn: "VehicleNumber");
                });

            migrationBuilder.CreateTable(
                name: "TempEmployeeStatusChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeIqamaNo = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false),
                    Resolution = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AdminNotes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TempEmployeeStatusChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TempEmployeeStatusChanges_Employees_EmployeeIqamaNo",
                        column: x => x.EmployeeIqamaNo,
                        principalTable: "Employees",
                        principalColumn: "IqamaNo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TempEmployeeUpdates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IqamaNo = table.Column<long>(type: "bigint", nullable: false),
                    OldIqamaEndM = table.Column<DateOnly>(type: "date", nullable: true),
                    OldIqamaEndH = table.Column<DateOnly>(type: "date", nullable: true),
                    OldPassportNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldPassportEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    OldSponsor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldSponsorNo = table.Column<int>(type: "int", nullable: true),
                    OldJobTitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldNameAR = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldNameEN = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldCountry = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldPhone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldDateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    OldStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldIBAN = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldINKSA = table.Column<bool>(type: "bit", nullable: true),
                    NewIqamaEndM = table.Column<DateOnly>(type: "date", nullable: true),
                    NewIqamaEndH = table.Column<DateOnly>(type: "date", nullable: true),
                    NewPassportNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewPassportEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    NewSponsor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewSponsorNo = table.Column<int>(type: "int", nullable: true),
                    NewJobTitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewNameAR = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewNameEN = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewCountry = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewPhone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewDateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    NewStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewIBAN = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewINKSA = table.Column<bool>(type: "bit", nullable: true),
                    IsNewEmployee = table.Column<bool>(type: "bit", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false),
                    Resolution = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TempEmployeeUpdates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TempEmployeeUpdates_Employees_IqamaNo",
                        column: x => x.IqamaNo,
                        principalTable: "Employees",
                        principalColumn: "IqamaNo");
                });

            migrationBuilder.CreateTable(
                name: "RiderShifts",
                columns: table => new
                {
                    RiderId = table.Column<int>(type: "int", nullable: false),
                    WorkingId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ShiftDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AcceptedDailyOrders = table.Column<int>(type: "int", nullable: false),
                    RejectedDailyOrders = table.Column<int>(type: "int", nullable: false),
                    StackedDeliveries = table.Column<int>(type: "int", nullable: false),
                    RealRejectedDailyOrders = table.Column<int>(type: "int", nullable: false),
                    WorkingHours = table.Column<float>(type: "real", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    ShiftStatus = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderShifts", x => new { x.RiderId, x.ShiftDate, x.WorkingId });
                    table.ForeignKey(
                        name: "FK_RiderShifts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderShifts_RiderDetails_RiderId",
                        column: x => x.RiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderShiftSubstitutions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActualRiderId = table.Column<int>(type: "int", maxLength: 50, nullable: false),
                    ActualRiderWorkingId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubstituteRiderId = table.Column<int>(type: "int", nullable: false),
                    SubstituteWorkingId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    Reason = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderShiftSubstitutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderShiftSubstitutions_RiderDetails_ActualRiderId",
                        column: x => x.ActualRiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderShiftSubstitutions_RiderDetails_SubstituteRiderId",
                        column: x => x.SubstituteRiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TempRiderShiftComparisons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderId = table.Column<int>(type: "int", nullable: false),
                    WorkingId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ShiftDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    IsSubstitution = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    OriginalRiderWorkingId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldAcceptedDailyOrders = table.Column<int>(type: "int", nullable: true),
                    OldRejectedDailyOrders = table.Column<int>(type: "int", nullable: true),
                    OldRealRejectedDailyOrders = table.Column<int>(type: "int", nullable: true),
                    OldStackedDeliveries = table.Column<int>(type: "int", nullable: true),
                    OldWorkingHours = table.Column<float>(type: "real", nullable: true),
                    OldShiftStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OldCreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NewAcceptedDailyOrders = table.Column<int>(type: "int", nullable: false),
                    NewRejectedDailyOrders = table.Column<int>(type: "int", nullable: false),
                    NewRealRejectedDailyOrders = table.Column<int>(type: "int", nullable: false),
                    NewStackedDeliveries = table.Column<int>(type: "int", nullable: false),
                    NewWorkingHours = table.Column<float>(type: "real", nullable: false),
                    NewShiftStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TempRiderShiftComparisons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TempRiderShiftComparisons_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TempRiderShiftComparisons_RiderDetails_RiderId",
                        column: x => x.RiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TempVehicleOperations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderIqamaNo = table.Column<long>(type: "bigint", nullable: false),
                    VehiclePlateNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    VehicleNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    VehicleStatusType = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false),
                    Resolution = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AdminNotes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TempVehicleOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TempVehicleOperations_RiderDetails_RiderIqamaNo",
                        column: x => x.RiderIqamaNo,
                        principalTable: "RiderDetails",
                        principalColumn: "EmployeeIqamaNo",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TempVehicleOperations_Vehicles_VehicleNumber",
                        column: x => x.VehicleNumber,
                        principalTable: "Vehicles",
                        principalColumn: "VehicleNumber",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "IsDefault", "IsDeleted", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { "17B96C5D-F502-47TF-EE95-ABVN14A3CA22", "17B75EE9-DB35-480D-9F9F-18D2E499B004", false, false, "Master", "MASTER" },
                    { "77B96C5D-F502-47TF-EE95-ABVN14A3CA22", "A7B75EE9-DB35-480D-9F9F-18D2E499B004", true, false, "Member", "MEMBER" },
                    { "77B96CED-F902-47EF-AE95-ABBE14A8CA22", "B0AD2D39-253B-42E4-88F2-F6FE83A614A8", false, false, "Admin", "ADMIN" }
                });

            migrationBuilder.InsertData(
                table: "AspNetUsers",
                columns: new[] { "Id", "AccessFailedCount", "Address", "ConcurrencyStamp", "Email", "EmailConfirmed", "FullName", "IsDisable", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "SecurityStamp", "TwoFactorEnabled", "UserName" },
                values: new object[,]
                {
                    { "59724D2D-E2B5-4C67-AB6F-D93478347B03", 0, "", "B4555410-F5B0-45B1-B963-1B2351A0723C", null, true, "", false, false, null, null, "ADMIN", "AQAAAAIAAYagAAAAEJmaWg42o3LN4b53Ugf9Lyx/dpHrMCbetjuC+kOmui/c6ctQMU5JB3j9NXzB6J67yw==", null, false, "9FABB58491024B7BB140E4D6658B5BDA", false, "Admin" },
                    { "59726D2D-E2B5-4C67-AB6F-D93478317B03", 0, "", "B4555410-F5B0-45B1-B963-1B2351A0723C", null, true, "", false, false, null, null, "MASTER", "AQAAAAIAAYagAAAAEJVMSrwscEutv0axNqQUZiX8dm3+F6bj55iCGDtfRbC6xDq/mnk99wy2WkJy0oHDYg==", null, false, "9FABB58491024B7BB140E4D6658B5BDA", false, "Master" }
                });

            migrationBuilder.InsertData(
                table: "AspNetUserRoles",
                columns: new[] { "RoleId", "UserId" },
                values: new object[,]
                {
                    { "77B96CED-F902-47EF-AE95-ABBE14A8CA22", "59724D2D-E2B5-4C67-AB6F-D93478347B03" },
                    { "17B96C5D-F502-47TF-EE95-ABVN14A3CA22", "59726D2D-E2B5-4C67-AB6F-D93478317B03" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Name",
                table: "Companies",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDocuments_EmployeeIqamaNo",
                table: "EmployeeDocuments",
                column: "EmployeeIqamaNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_HousingId",
                table: "Employees",
                column: "HousingId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_Status",
                table: "Employees",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Housings_Name",
                table: "Housings",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiderDetails_CompanyId",
                table: "RiderDetails",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderDetails_VehicleNumber",
                table: "RiderDetails",
                column: "VehicleNumber",
                unique: true,
                filter: "[VehicleNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RiderShifts_CompanyId",
                table: "RiderShifts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderShifts_RiderId",
                table: "RiderShifts",
                column: "RiderId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderShifts_ShiftDate",
                table: "RiderShifts",
                column: "ShiftDate");

            migrationBuilder.CreateIndex(
                name: "IX_RiderShifts_ShiftStatus",
                table: "RiderShifts",
                column: "ShiftStatus");

            migrationBuilder.CreateIndex(
                name: "IX_RiderShifts_WorkingId",
                table: "RiderShifts",
                column: "WorkingId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderShiftSubstitutions_ActualRiderId",
                table: "RiderShiftSubstitutions",
                column: "ActualRiderId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderShiftSubstitutions_SubstituteRiderId",
                table: "RiderShiftSubstitutions",
                column: "SubstituteRiderId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderVehicleStatus_VehicleNumber_IsActive",
                table: "RiderVehicleStatus",
                columns: new[] { "VehicleNumber", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_TempEmployeeStatusChanges_EmployeeIqamaNo",
                table: "TempEmployeeStatusChanges",
                column: "EmployeeIqamaNo");

            migrationBuilder.CreateIndex(
                name: "IX_TempEmployeeStatusChanges_IsResolved",
                table: "TempEmployeeStatusChanges",
                column: "IsResolved");

            migrationBuilder.CreateIndex(
                name: "IX_TempEmployeeStatusChanges_RequestedAt",
                table: "TempEmployeeStatusChanges",
                column: "RequestedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TempEmployeeUpdates_IqamaNo",
                table: "TempEmployeeUpdates",
                column: "IqamaNo");

            migrationBuilder.CreateIndex(
                name: "IX_TempEmployeeUpdates_IsResolved",
                table: "TempEmployeeUpdates",
                column: "IsResolved");

            migrationBuilder.CreateIndex(
                name: "IX_TempEmployeeUpdates_UploadedAt",
                table: "TempEmployeeUpdates",
                column: "UploadedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TempRiderShiftComparisons_CompanyId",
                table: "TempRiderShiftComparisons",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_TempRiderShiftComparisons_IsResolved",
                table: "TempRiderShiftComparisons",
                column: "IsResolved");

            migrationBuilder.CreateIndex(
                name: "IX_TempRiderShiftComparisons_IsSubstitution",
                table: "TempRiderShiftComparisons",
                column: "IsSubstitution");

            migrationBuilder.CreateIndex(
                name: "IX_TempRiderShiftComparisons_RiderId_WorkingId_ShiftDate",
                table: "TempRiderShiftComparisons",
                columns: new[] { "RiderId", "WorkingId", "ShiftDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TempRiderShiftComparisons_ShiftDate_WorkingId",
                table: "TempRiderShiftComparisons",
                columns: new[] { "ShiftDate", "WorkingId" });

            migrationBuilder.CreateIndex(
                name: "IX_TempVehicleOperations_IsResolved",
                table: "TempVehicleOperations",
                column: "IsResolved");

            migrationBuilder.CreateIndex(
                name: "IX_TempVehicleOperations_RequestedAt",
                table: "TempVehicleOperations",
                column: "RequestedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TempVehicleOperations_RiderIqamaNo",
                table: "TempVehicleOperations",
                column: "RiderIqamaNo");

            migrationBuilder.CreateIndex(
                name: "IX_TempVehicleOperations_VehicleNumber",
                table: "TempVehicleOperations",
                column: "VehicleNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_PlateNumberA",
                table: "Vehicles",
                column: "PlateNumberA");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_SerialNumber",
                table: "Vehicles",
                column: "SerialNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_VehicleNumber",
                table: "Vehicles",
                column: "VehicleNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "DeletedEmployees");

            migrationBuilder.DropTable(
                name: "EmployeeDocuments");

            migrationBuilder.DropTable(
                name: "RiderCompanyHistory");

            migrationBuilder.DropTable(
                name: "RiderShifts");

            migrationBuilder.DropTable(
                name: "RiderShiftSubstitutions");

            migrationBuilder.DropTable(
                name: "RiderVehicleStatus");

            migrationBuilder.DropTable(
                name: "TempEmployeeStatusChanges");

            migrationBuilder.DropTable(
                name: "TempEmployeeUpdates");

            migrationBuilder.DropTable(
                name: "TempRiderShiftComparisons");

            migrationBuilder.DropTable(
                name: "TempVehicleOperations");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "RiderDetails");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "Vehicles");

            migrationBuilder.DropTable(
                name: "Housings");
        }
    }
}
