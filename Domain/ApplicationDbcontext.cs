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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        var cascadeFKs = modelBuilder.Model.GetEntityTypes()
            .SelectMany(t => t.GetForeignKeys())
            .Where(fk => !fk.IsOwnership && fk.DeleteBehavior == DeleteBehavior.Cascade);

        foreach (var fk in cascadeFKs)
            fk.DeleteBehavior = DeleteBehavior.Restrict;


        base.OnModelCreating(modelBuilder);

    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(warnings =>
            warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

}
