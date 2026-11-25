using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Bassign.Infrastructure.Persistence;

/// <summary>Lets `dotnet ef` build the context at design time. Uses BASSIGN_DB env var or LocalDB.</summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("BASSIGN_DB")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=Bassign;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=true";

        // Identity's DbContext decides which tables exist (e.g. passkeys, schema version 3) from IdentityOptions,
        // which it finds through the application's service provider. The running app has one; `dotnet ef` doesn't,
        // so give it the same setting here — otherwise migrations silently miss those tables.
        // Keep in step with AddIdentityCore in DependencyInjection.cs.
        var services = new ServiceCollection();
        services.Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .UseApplicationServiceProvider(services.BuildServiceProvider())
            .Options;

        return new AppDbContext(options);
    }
}
