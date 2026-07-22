using Domain.Entities.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;

public class TenantConfigration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> entity)
    {
        entity.ToTable("Tenants");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Code).IsRequired().HasMaxLength(32);
        entity.Property(x => x.Name).IsRequired().HasMaxLength(200);
        entity.HasIndex(x => x.Code).IsUnique();
    }
}

public class LegalEntityConfigration : IEntityTypeConfiguration<LegalEntity>
{
    public void Configure(EntityTypeBuilder<LegalEntity> entity)
    {
        entity.ToTable("LegalEntities");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Code).IsRequired().HasMaxLength(32);
        entity.Property(x => x.LegalName).IsRequired().HasMaxLength(300);
        entity.Property(x => x.BaseCurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength();
        entity.Property(x => x.TaxRegistrationNumber).HasMaxLength(64);
        entity.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        entity.HasOne(x => x.Tenant).WithMany(x => x.LegalEntities)
            .HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class BranchConfigration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> entity)
    {
        entity.ToTable("Branches");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Code).IsRequired().HasMaxLength(32);
        entity.Property(x => x.Name).IsRequired().HasMaxLength(200);
        entity.HasIndex(x => new { x.LegalEntityId, x.Code }).IsUnique();
        entity.HasOne(x => x.LegalEntity).WithMany(x => x.Branches)
            .HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PlatformAccountConfigration : IEntityTypeConfiguration<PlatformAccount>
{
    public void Configure(EntityTypeBuilder<PlatformAccount> entity)
    {
        entity.ToTable("PlatformAccounts");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Code).IsRequired().HasMaxLength(32);
        entity.Property(x => x.PlatformName).IsRequired().HasMaxLength(100);
        entity.Property(x => x.ExternalAccountReference).HasMaxLength(128);
        entity.HasIndex(x => new { x.LegalEntityId, x.Code }).IsUnique();
        entity.HasOne(x => x.LegalEntity).WithMany(x => x.PlatformAccounts)
            .HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class LegacyCompanyPlatformMappingConfigration : IEntityTypeConfiguration<LegacyCompanyPlatformMapping>
{
    public void Configure(EntityTypeBuilder<LegacyCompanyPlatformMapping> entity)
    {
        entity.ToTable("LegacyCompanyPlatformMappings");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => x.CompanyId).IsUnique();
        entity.HasIndex(x => new { x.PlatformAccountId, x.CompanyId }).IsUnique();
        entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.PlatformAccount).WithMany(x => x.LegacyCompanyMappings)
            .HasForeignKey(x => x.PlatformAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
