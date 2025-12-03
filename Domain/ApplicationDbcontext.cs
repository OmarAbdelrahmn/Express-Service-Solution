using Domain.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Domain;

public class ApplicationDbcontext(DbContextOptions<ApplicationDbcontext> options) : IdentityDbContext<ApplicationUser,ApplicationRole,string>(options)
{
    //public required DbSet<RefreshToken> RefreshTokens { get; set; }

    public required DbSet<ApplicationUser> ApplicationUsers { get; set; }

    public required DbSet<ApplicationRole> ApplicationRoles { get; set; }
    public required DbSet<Company> Companies{ get; set; }
    public required DbSet<Employees> Employees{ get; set; }
    public required DbSet<EmployeeDocuments> EmployeeDocuments{ get; set; }
    public required DbSet<Housing> Housings{ get; set; }
    public required DbSet<RiderDetails> RiderDetails{ get; set; }
    public required DbSet<RiderShift> RiderShifts{ get; set; }
    public required DbSet<RiderShiftSubstitution> RiderShiftSubstitutions{ get; set; }
    public required DbSet<Vehicle> Vehicles { get; set; }
    public required DbSet<DeletedEmployees> DeletedEmployees { get; set; }
    public required DbSet<ArchivedRiderShift> ArchivedRiderShifts { get; set; }
    public required DbSet<RiderCompanyHistory> RiderCompanyHistory { get; set; }
    public required DbSet<RiderVehicleStatus> RiderVehicleStatus { get; set; }
    public required DbSet<TempRiderShiftComparison> TempRiderShiftComparisons { get; set; }
    public required DbSet<TempEmployeeUpdate> TempEmployeeUpdates { get; set; }
    public required DbSet<TempEmployeeStatusChange> TempEmployeeStatusChanges { get; set; }
    public required DbSet<TempVehicleOperation> TempVehicleOperations { get; set; }

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

        // TempEmployeeStatusChange configuration
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

        // TempVehicleOperation configuration
        modelBuilder.Entity<TempVehicleOperation>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Rider)
                .WithMany()
                .HasForeignKey(e => e.RiderIqamaNo)
                .HasPrincipalKey(r => r.EmployeeIqamaNo)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Vehicle)
                .WithMany()
                .HasForeignKey(e => e.VehicleNumber)
                .HasPrincipalKey(v => v.VehicleNumber)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.VehiclePlateNumber)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.Reason)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.RequestedBy)
                .IsRequired()
                .HasMaxLength(100);

            entity.HasIndex(e => e.RiderIqamaNo);
            entity.HasIndex(e => e.VehicleNumber);
            entity.HasIndex(e => e.IsResolved);
            entity.HasIndex(e => e.RequestedAt);
        });
        //modelBuilder.Entity<EmployeeDocuments>()
        //.HasOne(ed => ed.Employee)
        //.WithOne(r => r.EmployeeDocuments)
        //.HasForeignKey(ed => ed.EmployeeIqamaNo);
        modelBuilder.Entity<RiderShiftSubstitution>()
    .Property(x => x.EndDate)
    .HasDefaultValueSql("GETUTCDATE()");
        modelBuilder.Entity<Employees>()
        .Property(x => x.DateOfBirth)
        .HasConversion(
            v => v.ToDateTime(TimeOnly.MinValue),
            v => DateOnly.FromDateTime(v)
        );

        base.OnModelCreating(modelBuilder);

    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(warnings =>
            warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

}
