using Domain.Entities;
using Domain.Entities.Spare;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Reflection;

namespace Domain;

public class ApplicationDbcontext(DbContextOptions<ApplicationDbcontext> options) : IdentityDbContext<ApplicationUser, ApplicationRole, string>(options)
{

    public required DbSet<ApplicationUser> ApplicationUsers { get; set; }
    public required DbSet<ApplicationRole> ApplicationRoles { get; set; }
    public required DbSet<Company> Companies { get; set; }
    public required DbSet<Employees> Employees { get; set; }
    public required DbSet<EmployeeDocuments> EmployeeDocuments { get; set; }
    public required DbSet<Housing> Housings { get; set; }
    public required DbSet<RiderDetails> RiderDetails { get; set; }
    public required DbSet<RiderShift> RiderShifts { get; set; }
    public required DbSet<RiderShiftSubstitution> RiderShiftSubstitutions { get; set; }
    public required DbSet<Vehicle> Vehicles { get; set; }
    public required DbSet<DeletedEmployees> DeletedEmployees { get; set; }
    public required DbSet<RiderCompanyHistory> RiderCompanyHistory { get; set; }
    public required DbSet<RiderVehicleStatus> RiderVehicleStatus { get; set; }
    public required DbSet<TempRiderShiftComparison> TempRiderShiftComparisons { get; set; }
    public required DbSet<TempEmployeeUpdate> TempEmployeeUpdates { get; set; }
    public required DbSet<TempEmployeeStatusChange> TempEmployeeStatusChanges { get; set; }
    public required DbSet<TempVehicleOperation> TempVehicleOperations { get; set; }
    public required DbSet<RiderWorkingIdHistory> RiderWorkingIdHistories { get; set; }
    public required DbSet<SparePart> SpareParts { get; set; }
    public required DbSet<RiderAccessory> RiderAccessories { get; set; }
    public required DbSet<RiderAccessoryUsage> RiderAccessoryUsages { get; set; }
    public required DbSet<SparePartUsage> SparePartUsages { get; set; }
    public required DbSet<Supplier> Suppliers { get; set; }
    public required DbSet<Bill> Bills { get; set; }
    public required DbSet<BillItem> BillItems { get; set; }
    public required DbSet<Transfer> Transfers { get; set; }
    public required DbSet<TransferItem> TransferItems { get; set; }
    public required DbSet<Return> Returns { get; set; }
    public required DbSet<ReturnItem> ReturnItems { get; set; }
    public required DbSet<KetaFreeLancer> KetaFreeLancers { get; set; }
    public required DbSet<RiderMonthlyValidity> RiderMonthlyValidities { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        var cascadeFKs = modelBuilder.Model.GetEntityTypes()
            .SelectMany(t => t.GetForeignKeys())
            .Where(fk => !fk.IsOwnership && fk.DeleteBehavior == DeleteBehavior.Cascade);

        foreach (var fk in cascadeFKs)
            fk.DeleteBehavior = DeleteBehavior.Restrict;

        modelBuilder.Entity<RiderDetails>()
        .HasOne(r => r.Vehicle)
        .WithOne(v => v.RiderDetails)
        .HasForeignKey<RiderDetails>(r => r.VehicleNumber);

        modelBuilder.Entity<Employees>()
        .HasOne(e => e.RiderDetails)
        .WithOne(r => r.Employee)
        .HasForeignKey<RiderDetails>(r => r.EmployeeIqamaNo);

        modelBuilder.Entity<RiderVehicleStatus>()
        .HasOne(r => r.Vehicle)
        .WithMany(v => v.RiderVehicleStatuses)
        .HasForeignKey(r => r.VehicleNumber)
        .HasPrincipalKey(v => v.VehicleNumber);

        modelBuilder.Entity<RiderVehicleStatus>(entity =>
        {


            entity.Property(rvs => rvs.Permission)
            .IsRequired(false)
                .HasMaxLength(500);

            entity.Property(rvs => rvs.PermissionStartDate).IsRequired(false);
            entity.Property(rvs => rvs.PermissionEndDate).IsRequired(false);

            entity.Property(rvs => rvs.Timestamp)
                .HasDefaultValueSql("GETDATE()")
                .HasColumnType("datetime2");

            entity.Property(rvs => rvs.IsActive)
                .HasDefaultValue(false);

            entity.Property(rvs => rvs.StatusType)
                .IsRequired()
                .HasConversion<int>();

            entity.HasIndex(rvs => new { rvs.VehicleNumber, rvs.IsActive, rvs.StatusType });
            entity.HasIndex(rvs => new { rvs.EmployeeIqamaNo, rvs.IsActive });
            entity.HasIndex(rvs => rvs.Timestamp);
            entity.HasIndex(rvs => new { rvs.VehicleNumber, rvs.IsActive, rvs.PermissionEndDate })
                .HasFilter("[PermissionEndDate] IS NOT NULL");
        });


        modelBuilder.Entity<TempEmployeeUpdate>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Employee)
                .WithMany()
                .HasForeignKey(e => e.IqamaNo)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasIndex(e => e.IqamaNo);
            entity.HasIndex(e => e.IsResolved);
            entity.HasIndex(e => e.UploadedAt);
        });

        modelBuilder.Entity<TempEmployeeStatusChange>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Employee)
                .WithMany()
                .HasForeignKey(e => e.EmployeeIqamaNo)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Action)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.Reason)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.RequestedBy)
                .IsRequired()
                .HasMaxLength(100);

            entity.HasIndex(e => e.EmployeeIqamaNo);
            entity.HasIndex(e => e.IsResolved);
            entity.HasIndex(e => e.RequestedAt);
        });

        modelBuilder.Entity<TempVehicleOperation>(entity =>
        {


            entity.Property(t => t.VehicleStatusType)
                .IsRequired()
                .HasConversion<int>();

            entity.Property(t => t.Reason)
                .HasMaxLength(500);

            entity.Property(t => t.Permission)
                .HasMaxLength(500)
                            .IsRequired(false);

            entity.Property(t => t.PermissionEndDate)
            .IsRequired(false);

            entity.Property(t => t.RequestedAt)
                .HasDefaultValueSql("GETDATE()")
                .HasColumnType("datetime2");

            entity.Property(t => t.RequestedBy)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(t => t.IsResolved)
                .HasDefaultValue(false);

            entity.Property(t => t.Resolution)
                .HasMaxLength(50);

            entity.Property(t => t.ResolvedBy)
                .HasMaxLength(200);

            entity.Property(t => t.ResolvedAt)
                .HasColumnType("datetime2");

            entity.Property(t => t.AdminNotes)
                .HasMaxLength(1000);

            entity.HasOne(t => t.Rider)
                .WithMany()
                .HasForeignKey(t => t.RiderIqamaNo)
                .HasPrincipalKey(r => r.EmployeeIqamaNo)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(t => t.Vehicle)
                .WithMany()
                .HasForeignKey(t => t.VehicleNumber)
                .HasPrincipalKey(v => v.VehicleNumber)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(t => new { t.RiderIqamaNo, t.IsResolved });
            entity.HasIndex(t => new { t.IsResolved, t.VehicleStatusType })
                .HasFilter("[IsResolved] = 0");
            entity.HasIndex(e => e.RiderIqamaNo);
            entity.HasIndex(e => e.VehicleNumber);
            entity.HasIndex(e => e.IsResolved);
            entity.HasIndex(e => e.RequestedAt);
        });


        modelBuilder.Entity<RiderShiftSubstitution>(entity =>
            {
                entity.HasKey(s => s.Id);

                entity.HasIndex(s => new { s.ActualRiderWorkingId, s.IsActive });
                entity.HasIndex(s => new { s.SubstituteWorkingId, s.IsActive });

                entity.HasOne(s => s.ActualRider)
                    .WithMany()
                    .HasForeignKey(s => s.ActualRiderId)
                    .OnDelete(DeleteBehavior.Restrict)
                    .IsRequired(false);

                entity.Navigation(s => s.ActualRider)
                    .IsRequired(false);

                entity.Property(s => s.ActualRiderId)
                    .IsRequired(false);

                entity.Property(s => s.EndDate)
                    .IsRequired(false);

                entity.HasOne(s => s.SubstituteRider)
                    .WithMany()
                    .HasForeignKey(s => s.SubstituteRiderId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

        modelBuilder.Entity<RiderWorkingIdHistory>(entity =>
        {
            entity.HasKey(h => h.Id);

            entity.HasIndex(h => h.WorkingId);
            entity.HasIndex(h => h.RiderIqamaNo);
            entity.HasIndex(h => new { h.WorkingId, h.IsActive });
            entity.HasIndex(h => new { h.RiderIqamaNo, h.IsActive });

            entity.HasIndex(h => h.CompanyId);

            entity.Property(h => h.WorkingId)
                .IsRequired()
                .HasMaxLength(50);

            entity.HasOne(h => h.Employee)
                .WithMany()
                .HasForeignKey(h => h.RiderIqamaNo)
                .HasPrincipalKey(e => e.IqamaNo)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(h => h.Company)
                .WithMany()
                .HasForeignKey(h => h.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });


        foreach (var property in modelBuilder.Model.GetEntityTypes()
       .SelectMany(t => t.GetProperties())
       .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            property.SetColumnType("decimal(38, 0)");
        }

        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<HungerDisability>(entity =>
        {
            entity.HasKey(h => h.Id);

            entity.Property(h => h.ActualWorkingId)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(h => h.SubstituteWorkingId)
                .HasMaxLength(50);

            entity.Property(h => h.ShiftDate)
                .IsRequired();

            entity.Property(h => h.Days)
                .IsRequired();

            entity.Property(h => h.AcceptedDailyOrders)
                .IsRequired();

            entity.Property(h => h.CreatedAt)
                .IsRequired();

            // Relationship with ActualRider (the disabled rider)
            entity.HasOne(h => h.Rider)
                .WithMany()
                .HasForeignKey(h => h.ActualRiderId)
                .OnDelete(DeleteBehavior.Restrict);

            // Relationship with Company
            entity.HasOne(h => h.Company)
                .WithMany()
                .HasForeignKey(h => h.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            // Composite unique index to prevent duplicate records for same rider on same date
            entity.HasIndex(h => new { h.ActualRiderId, h.ShiftDate })
                .IsUnique()
                .HasDatabaseName("IX_HungerDisability_ActualRider_ShiftDate");

            // Index for performance on common queries
            entity.HasIndex(h => h.ActualWorkingId)
                .HasDatabaseName("IX_HungerDisability_ActualWorkingId");

            entity.HasIndex(h => h.ShiftDate)
                .HasDatabaseName("IX_HungerDisability_ShiftDate");

            entity.HasIndex(h => h.CompanyId)
                .HasDatabaseName("IX_HungerDisability_CompanyId");

            entity.HasIndex(h => h.SubstituteRiderId)
                .HasDatabaseName("IX_HungerDisability_SubstituteRiderId");

            entity.ToTable("HungerDisabilities");
        });


    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(warnings =>
            warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

}
