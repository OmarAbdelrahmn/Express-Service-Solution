using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Domain;

/// <summary>
/// Keeps EF Core tooling independent from runtime-only host configuration such as JWT secrets.
/// The connection is not opened while generating a migration; deployments must still provide
/// <c>ConnectionStrings__DefaultConnection</c> before applying one.
/// </summary>
public class ApplicationDbcontextFactory : IDesignTimeDbContextFactory<ApplicationDbcontext>
{
    public ApplicationDbcontext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=(localdb)\\mssqllocaldb;Database=ExpressServiceDesignTime;Trusted_Connection=True;TrustServerCertificate=True";

        return new ApplicationDbcontext(
            new DbContextOptionsBuilder<ApplicationDbcontext>()
                .UseSqlServer(connectionString)
                .Options);
    }
}
