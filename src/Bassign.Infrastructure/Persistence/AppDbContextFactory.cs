using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bassign.Infrastructure.Persistence;

/// <summary>Lets `dotnet ef` build the context at design time. Uses BASSIGN_DB env var or LocalDB.</summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("BASSIGN_DB")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=Bassign;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=true";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
