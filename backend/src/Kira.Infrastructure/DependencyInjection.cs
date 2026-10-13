using Kira.Application.Common;
using Kira.Infrastructure.Ai;
using Kira.Infrastructure.Files;
using Kira.Infrastructure.Persistence;
using Kira.Infrastructure.Security;
using Kira.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kira.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connection = config.GetConnectionString("Kira")
            ?? throw new InvalidOperationException("Set ConnectionStrings:Kira to your SQL Server connection string.");

        services.AddSingleton<LedgerImmutabilityInterceptor>();
        // SQL Server is the supported database. "Postgres" exists only so a small test server
        // (where SQL Server's 2 GB memory minimum isn't available) can still run the app.
        var postgres = string.Equals(config["Database:Provider"], "Postgres", StringComparison.OrdinalIgnoreCase);
        if (postgres) AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        services.AddDbContext<KiraDbContext>((sp, o) =>
        {
            if (postgres)
                o.UseNpgsql(PostgresConnection(connection), pg =>
                {
                    pg.EnableRetryOnFailure(maxRetryCount: 5);
                    pg.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                });
            else
                o.UseSqlServer(connection, sql =>
                {
                    sql.EnableRetryOnFailure(maxRetryCount: 5);
                    sql.MigrationsAssembly(typeof(KiraDbContext).Assembly.FullName);
                    sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                });
            o.AddInterceptors(sp.GetRequiredService<LedgerImmutabilityInterceptor>());
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<KiraDbContext>());

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IClock, MalaysiaClock>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IStatementReader, StatementReader>();
        services.AddSingleton<IExcelWriter, ExcelWriter>();
        services.AddScoped<DemoSeeder>();

        services.Configure<AnthropicOptions>(config.GetSection(AnthropicOptions.Section));
        services.AddHttpClient<IAiClient, AnthropicClient>(c => c.Timeout = TimeSpan.FromMinutes(5));
        return services;
    }

    /// <summary>Accepts either a key=value connection string or a postgres:// URL (as hosting platforms provide).</summary>
    private static string PostgresConnection(string value)
    {
        if (!value.StartsWith("postgres", StringComparison.OrdinalIgnoreCase) || !value.Contains("://")) return value;
        var uri = new Uri(value);
        var userInfo = uri.UserInfo.Split(':', 2);
        return $"Host={uri.Host};Port={(uri.Port > 0 ? uri.Port : 5432)};Database={uri.AbsolutePath.TrimStart('/')};" +
               $"Username={Uri.UnescapeDataString(userInfo[0])};Password={(userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "")}";
    }
}
