using System.Diagnostics.CodeAnalysis;
using Application.Service.Import;
using Application.Service.Petrol;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Domain.Entities.Petrol;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Accounting.Tests;

// Native SQL Server transaction/unique-index checks. All EF tables are mapped
// to session-local temporary tables; these tests never write application tables.
public class PetrolSqlServerTests
{
    [PetrolSqlFact]
    public async Task ExplicitPetrolAssignment_CreatesMinimalEmployeeRiderProfile_WithGeneratedSqlKeys()
    {
        await using var connection = new SqlConnection(Environment.GetEnvironmentVariable("PETROL_SQL_TEST_CONNECTION"));
        await connection.OpenAsync();
        await using var db = await CreateDb(connection);
        db.Employees.Add(new Employees { IqamaNo = 103, IsEmployee = true });
        var date = new DateOnly(2026, 10, 18);
        var cost = new VehiclePetrolCost { PlateNumberE = "AA111", VehicleNumber = "V1", Date = date, Cost = 12m };
        db.VehiclePetrolCosts.Add(cost);
        db.RiderPetrolCosts.Add(new RiderPetrolCost { VehiclePetrolCost = cost, VehicleNumber = "V1", Date = date,
            Cost = 12m, AttributionSource = PetrolAttributionSource.Unattributed });
        await db.SaveChangesAsync();
        var result = await new PetrolService(db).AssignRiderToUnattributedAsync("AA111", date, 103, "SQL test");
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Description : null);
        db.ChangeTracker.Clear();
        var profile = await db.RiderDetails.Include(r => r.Company).SingleAsync(r => r.EmployeeIqamaNo == 103);
        Assert.Null(profile.WorkingId);
        Assert.Equal("Unassigned", profile.Company.Name);
        Assert.Equal(103, (await db.RiderPetrolCosts.SingleAsync()).RiderIqamaNo);
        Assert.True((await db.Employees.SingleAsync(e => e.IqamaNo == 103)).IsEmployee);
    }

    [PetrolSqlFact]
    public async Task NotAttached_ExcludesUnknownPlates_ButKeepsRegisteredReturnedVehiclesAndAllDailyCosts()
    {
        await using var connection = new SqlConnection(Environment.GetEnvironmentVariable("PETROL_SQL_TEST_CONNECTION"));
        await connection.OpenAsync();
        await using var db = await CreateDb(connection);
        var date = new DateOnly(2026, 10, 18);
        var unknown = new VehiclePetrolCost { PlateNumberE = "UNKNOWN123", Date = date, Cost = 10m, HasResolutionError = true };
        var known = new VehiclePetrolCost { PlateNumberE = "AA111", VehicleNumber = "V1", Date = date, Cost = 5m };
        db.VehiclePetrolCosts.AddRange(unknown, known);
        db.RiderPetrolCosts.AddRange(new RiderPetrolCost { VehiclePetrolCost = unknown, VehicleNumber = null, Date = date,
            Cost = 10m, AttributionSource = PetrolAttributionSource.Unattributed },
            new RiderPetrolCost { VehiclePetrolCost = known, VehicleNumber = "V1", Date = date,
                Cost = 5m, AttributionSource = PetrolAttributionSource.Unattributed });
        db.RiderVehicleStatus.Add(new RiderVehicleStatus { VehicleNumber = "V1", EmployeeIqamaNo = 101,
            StatusType = VehicleStatusType.Returned, Timestamp = new DateTime(2026, 10, 1) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = new PetrolService(db);
        var result = await service.GetUnattributedCostsAsync(2026, 10);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Description : null);
        var row = Assert.Single(result.Value);
        Assert.Equal("AA111", row.PlateNumberE);
        Assert.Equal("ReturnedBeforeReportDate", row.ReasonCode);
        var daily = (await service.GetDailyReportAsync(date)).Value;
        Assert.Equal(15m, daily.TotalCost);
        Assert.Equal(1, daily.TotalUnattributedRows);
        Assert.Equal(1, daily.TotalUnknownVehicleRows);
        Assert.Equal(10m, daily.UnknownVehicleCost);
    }

    [PetrolSqlFact]
    public async Task ChassisSnapshotAndRepeatedUpload_PreserveUnchangedAssignment()
    {
        await using var connection = new SqlConnection(Environment.GetEnvironmentVariable("PETROL_SQL_TEST_CONNECTION"));
        await connection.OpenAsync();
        await using var db = await CreateDb(connection);
        var logger = new RecordingLogger();
        var service = new ImportService(db, null!, logger);
        Assert.True((await service.ImportVehicleAssignmentsAsync(File((101, " v 1 ")), "SQL test")).IsSuccess);
        Assert.Equal("V2", (await db.RiderDetails.SingleAsync(r => r.EmployeeIqamaNo == 102)).VehicleNumber);
        Assert.True((await service.ImportVehicleAssignmentsAsync(File((101, "V1")), "SQL test", true)).IsSuccess, logger.LastException?.ToString());
        Assert.Equal("V1", (await db.RiderDetails.SingleAsync(r => r.EmployeeIqamaNo == 101)).VehicleNumber);
        Assert.Null((await db.RiderDetails.SingleAsync(r => r.EmployeeIqamaNo == 102)).VehicleNumber);
        Assert.Equal(new DateTime(2026, 1, 1), (await db.RiderVehicleStatus.SingleAsync(s => s.VehicleNumber == "V1")).Timestamp);
        Assert.Equal(1, await db.RiderVehicleStatus.CountAsync(s => s.StatusType == VehicleStatusType.Returned));
    }

    [PetrolSqlFact]
    public async Task VehicleSwap_SucceedsWithSqlUniqueAssignmentIndex()
    {
        await using var connection = new SqlConnection(Environment.GetEnvironmentVariable("PETROL_SQL_TEST_CONNECTION"));
        await connection.OpenAsync();
        await using var db = await CreateDb(connection);
        var logger = new RecordingLogger();
        var result = await new ImportService(db, null!, logger).ImportVehicleAssignmentsAsync(File((101, "V2"), (102, "V1")), "SQL test");
        Assert.True(result.IsSuccess, logger.LastException?.ToString() ?? (result.IsFailure ? result.Error.Description : null));
        db.ChangeTracker.Clear();
        Assert.Equal("V2", (await db.RiderDetails.SingleAsync(r => r.EmployeeIqamaNo == 101)).VehicleNumber);
        Assert.Equal("V1", (await db.RiderDetails.SingleAsync(r => r.EmployeeIqamaNo == 102)).VehicleNumber);
        Assert.Equal(2, await db.RiderVehicleStatus.CountAsync(s => s.IsActive));
    }

    [PetrolSqlFact]
    public async Task FailureAfterReleasingVehicles_RollsBackReturnsAssignmentsAndAuditRows()
    {
        await using var connection = new SqlConnection(Environment.GetEnvironmentVariable("PETROL_SQL_TEST_CONNECTION"));
        await connection.OpenAsync();
        await using var db = await CreateDb(connection);
        var auditCount = await db.SystemAuditEvents.CountAsync();
        db.SaveCalls = 0;
        db.FailSaveNumber = 2;
        var result = await new ImportService(db, null!).ImportVehicleAssignmentsAsync(File((101, "V2"), (102, "V1")), "SQL test");
        Assert.True(result.IsFailure);
        Assert.Equal(2, db.SaveCalls);
        Assert.False(db.ChangeTracker.HasChanges());
        db.FailSaveNumber = null;
        await db.SaveChangesAsync(); // A later save cannot commit abandoned takes.
        Assert.Equal("V1", (await db.RiderDetails.SingleAsync(r => r.EmployeeIqamaNo == 101)).VehicleNumber);
        Assert.Equal("V2", (await db.RiderDetails.SingleAsync(r => r.EmployeeIqamaNo == 102)).VehicleNumber);
        Assert.Equal(2, await db.RiderVehicleStatus.CountAsync());
        Assert.Equal(2, await db.RiderVehicleStatus.CountAsync(s => s.IsActive));
        Assert.Equal(auditCount, await db.SystemAuditEvents.CountAsync());
    }

    private static async Task<TemporaryDb> CreateDb(SqlConnection connection)
    {
        var db = new TemporaryDb(new DbContextOptionsBuilder<ApplicationDbcontext>().UseSqlServer(connection).Options);
        foreach (var table in new[] { "Vehicles", "Employees", "RiderDetails", "Housings", "Companies", "RiderVehicleStatus", "SystemAuditEvents", "VehiclePetrolCosts", "RiderPetrolCosts" })
        {
            // Identifiers come exclusively from the fixed list above.
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT TOP(0) * INTO [#PetrolTest_{table}] FROM dbo.[{table}];";
            await command.ExecuteNonQueryAsync();
        }
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE UNIQUE INDEX IX_TestRiderVehicle ON #PetrolTest_RiderDetails(VehicleNumber) WHERE VehicleNumber IS NOT NULL;";
            command.CommandText += " ALTER TABLE #PetrolTest_RiderVehicleStatus ADD DEFAULT(0) FOR IsActive;";
            command.CommandText += " ALTER TABLE #PetrolTest_RiderVehicleStatus ADD DEFAULT(GETDATE()) FOR Timestamp;";
            await command.ExecuteNonQueryAsync();
        }
        db.Vehicles.AddRange(new Vehicle { VehicleNumber = "V1", PlateNumberA = "A1", PlateNumberE = "AA111", SerialNumber = 1 },
            new Vehicle { VehicleNumber = "V2", PlateNumberA = "A2", PlateNumberE = "BB222", SerialNumber = 2 });
        db.Employees.AddRange(new Employees { IqamaNo = 101 }, new Employees { IqamaNo = 102 });
        db.RiderDetails.AddRange(new RiderDetails { EmployeeIqamaNo = 101, CompanyId = 1, VehicleNumber = "V1" },
            new RiderDetails { EmployeeIqamaNo = 102, CompanyId = 1, VehicleNumber = "V2" });
        db.RiderVehicleStatus.AddRange(new RiderVehicleStatus { VehicleNumber = "V1", EmployeeIqamaNo = 101,
            StatusType = VehicleStatusType.Taken, IsActive = true, Timestamp = new DateTime(2026, 1, 1) },
            new RiderVehicleStatus { VehicleNumber = "V2", EmployeeIqamaNo = 102,
                StatusType = VehicleStatusType.Taken, IsActive = true, Timestamp = new DateTime(2026, 1, 1) });
        await db.SaveChangesAsync();
        return db;
    }

    private static IFormFile File(params (long Iqama, string Vehicle)[] rows)
    {
        var stream = new MemoryStream();
        using (var book = new XLWorkbook())
        {
            var sheet = book.AddWorksheet("Assignments");
            sheet.Cell(1, 1).Value = "IqamaNumber"; sheet.Cell(1, 2).Value = "VehicleNumber";
            for (var i = 0; i < rows.Length; i++)
            { sheet.Cell(i + 2, 1).Value = rows[i].Iqama; sheet.Cell(i + 2, 2).Value = rows[i].Vehicle; }
            book.SaveAs(stream);
        }
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "file", "assignments.xlsx");
    }

    private sealed class TemporaryDb : ApplicationDbcontext
    {
        [SetsRequiredMembers]
        public TemporaryDb(DbContextOptions<ApplicationDbcontext> options) : base(options) { }
        public int SaveCalls { get; set; }
        public int? FailSaveNumber { get; set; }
        protected override void OnModelCreating(ModelBuilder model)
        {
            base.OnModelCreating(model);
            foreach (var entity in model.Model.GetEntityTypes())
            {
                var table = entity.GetTableName();
                if (table != null) { entity.SetTableName("#PetrolTest_" + table); entity.SetSchema(null); }
            }
        }
        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
        {
            if (++SaveCalls == FailSaveNumber) throw new InvalidOperationException("Synthetic second-save failure");
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
        }
    }

    private sealed class RecordingLogger : ILogger<ImportService>
    {
        public Exception? LastException { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => LastException = exception;
    }
}

public sealed class PetrolSqlFactAttribute : FactAttribute
{
    public PetrolSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PETROL_SQL_TEST_CONNECTION")))
            Skip = "Set PETROL_SQL_TEST_CONNECTION to run native SQL Server checks in session-local temporary tables.";
    }
}
