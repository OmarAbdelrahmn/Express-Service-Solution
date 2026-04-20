
using Domain.Entities.Petrol;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class VehiclePetrolCostConfiguration : IEntityTypeConfiguration<VehiclePetrolCost>
{
    public void Configure(EntityTypeBuilder<VehiclePetrolCost> b)
    {
        b.HasKey(x => x.Id);

        b.Property(x => x.PlateNumberE).HasMaxLength(30).IsRequired();
        b.Property(x => x.Cost).HasColumnType("decimal(18,2)");
        b.Property(x => x.UploadedBy).HasMaxLength(100);
        b.Property(x => x.ResolutionErrorMessage).HasMaxLength(500);

        // Index: find all costs for a vehicle in a month quickly
        b.HasIndex(x => new { x.VehicleNumber, x.Date });
        // Index: find all un-attributed rows for the background job
        b.HasIndex(x => x.IsAttributed);

        b.HasOne(x => x.Vehicle)
         .WithMany()
         .HasForeignKey(x => x.VehicleNumber)
         .HasPrincipalKey(v => v.VehicleNumber)
         .OnDelete(DeleteBehavior.Restrict)
         .IsRequired(false);

        b.HasMany(x => x.RiderPetrolCosts)
         .WithOne(r => r.VehiclePetrolCost)
         .HasForeignKey(r => r.VehiclePetrolCostId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RiderPetrolCostConfiguration : IEntityTypeConfiguration<RiderPetrolCost>
{
    public void Configure(EntityTypeBuilder<RiderPetrolCost> b)
    {
        b.HasKey(x => x.Id);

        b.Property(x => x.Cost).HasColumnType("decimal(18,2)");
        b.Property(x => x.Notes).HasMaxLength(500);

        // ── Indexes ───────────────────────────────────────────────────────

        // Rider monthly report: all costs for a rider in a month
        b.HasIndex(x => new { x.RiderIqamaNo, x.Date });

        // Vehicle monthly report: all riders for a vehicle in a month
        b.HasIndex(x => new { x.VehicleNumber, x.Date });

        // Unattributed admin view
        b.HasIndex(x => new { x.RiderIqamaNo, x.AttributionSource });

        // ── Relationships ─────────────────────────────────────────────────

        b.HasOne(x => x.VehiclePetrolCost)
         .WithMany(v => v.RiderPetrolCosts)
         .HasForeignKey(x => x.VehiclePetrolCostId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Rider)
         .WithMany()
         .HasForeignKey(x => x.RiderIqamaNo)
         .HasPrincipalKey(e => e.IqamaNo)
         .OnDelete(DeleteBehavior.Restrict)
         .IsRequired(false);

        b.HasOne(x => x.Vehicle)
         .WithMany()
         .HasForeignKey(x => x.VehicleNumber)
         .HasPrincipalKey(v => v.VehicleNumber)
         .OnDelete(DeleteBehavior.Restrict)
         .IsRequired(false);
    }
}