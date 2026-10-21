using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;

public class DailyWorkEntryConfigration : IEntityTypeConfiguration<DailyWorkEntry>
{
    public void Configure(EntityTypeBuilder<DailyWorkEntry> builder)
    {
        builder.HasKey(x => new { x.Date, x.EmployeeIqamaNo });
        builder.Property(x => x.WorkingHours).HasPrecision(5, 2);
        builder.HasOne(x => x.Employee).WithMany()
            .HasForeignKey(x => x.EmployeeIqamaNo).OnDelete(DeleteBehavior.Restrict);
    }
}
