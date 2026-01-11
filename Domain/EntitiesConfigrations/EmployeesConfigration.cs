using Application.Abstraction.Consts;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;

public class EmployeesConfigration : IEntityTypeConfiguration<Employees>
{
    public void Configure(EntityTypeBuilder<Employees> builder)
    {

        builder.HasKey(e => e.IqamaNo);
        builder.Property(e => e.IqamaNo).ValueGeneratedNever();

        builder.Property(e => e.IqamaEndM).IsRequired();
        builder.Property(e => e.IqamaEndH).IsRequired();
        builder.Property(e => e.PassportNo).HasMaxLength(20);
        builder.Property(e => e.Sponsor).IsRequired().HasMaxLength(50);
        builder.Property(e => e.JobTitle).IsRequired().HasMaxLength(25);
        builder.Property(e => e.NameAR).IsRequired().HasMaxLength(100);
        builder.Property(e => e.NameEN).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Country).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Phone).IsRequired().HasMaxLength(15);
        builder.Property(e => e.DateOfBirth).IsRequired();
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20).HasDefaultValue("Active");
        builder.Property(e => e.IBAN).HasMaxLength(34);

        builder.HasIndex(e => e.Status).HasDatabaseName("IX_Employees_Status");



        // ✅ ADD THESE INDEXES FOR PERFORMANCE
        builder.HasIndex(e => e.NameAR);

        builder.HasIndex(e => e.NameEN);

        builder.HasIndex(e => e.HousingId);


    }
}

