using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kira.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    /// <summary>
    /// Applies EF Core migrations (or creates the schema if the project has no migrations yet) and seeds demo data.
    /// Controlled by Database:AutoMigrate and Seed:Demo.
    /// </summary>
    public static async Task InitialiseDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var config = sp.GetRequiredService<IConfiguration>();
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Kira.Database");
        var db = sp.GetRequiredService<KiraDbContext>();

        if (config.GetValue("Database:AutoMigrate", true))
        {
            if (db.Database.GetMigrations().Any())
            {
                log.LogInformation("Applying database migrations…");
                await db.Database.MigrateAsync(ct);
            }
            else
            {
                log.LogWarning("No EF Core migrations found. Creating the schema directly. Add migrations before production (see README).");
                await db.Database.EnsureCreatedAsync(ct);
            }
        }

        if (config.GetValue("Seed:Demo", false))
            await sp.GetRequiredService<DemoSeeder>().SeedAsync(ct);
    }
}
