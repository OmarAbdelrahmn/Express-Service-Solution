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
            ?? "Server=db33462.public.databaseasp.net; Database=db33462; User Id=db33462; Password=9d#J_Fe73-Xr; Encrypt=True; TrustServerCertificate=True; MultipleActiveResultSets=True;";

        return new ApplicationDbcontext(
            new DbContextOptionsBuilder<ApplicationDbcontext>()
                .UseSqlServer(connectionString)
                .Options);
    }
}
