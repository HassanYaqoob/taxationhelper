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
        services.AddDbContext<KiraDbContext>((sp, o) => o
            .UseSqlServer(connection, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 5);
                sql.MigrationsAssembly(typeof(KiraDbContext).Assembly.FullName);
                sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            })
            .AddInterceptors(sp.GetRequiredService<LedgerImmutabilityInterceptor>()));
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
}
