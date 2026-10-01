using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZamzamWaterDelivery.Application.Interfaces;
using ZamzamWaterDelivery.Domain.Interfaces;
using ZamzamWaterDelivery.Infrastructure.Data;
using ZamzamWaterDelivery.Infrastructure.Data.Seed;
using ZamzamWaterDelivery.Infrastructure.Identity;
using ZamzamWaterDelivery.Infrastructure.Notifications;
using ZamzamWaterDelivery.Infrastructure.Reports;
using ZamzamWaterDelivery.Infrastructure.Repositories;

namespace ZamzamWaterDelivery.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

        // SQL Server locally; PostgreSQL when hosted (e.g. Railway, which provides a postgresql:// URL).
        if (IsPostgres(configuration["Database:Provider"], connectionString))
        {
            // Keep DateTime behaviour the same as on SQL Server (no forced UTC kinds).
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
            var npgsqlConnection = ToNpgsqlConnectionString(connectionString);
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(npgsqlConnection, pg => pg.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
        }
        else
        {
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(connectionString, sql =>
                {
                    sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                    sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                }));
        }

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.Configure<SeedSettings>(configuration.GetSection(SeedSettings.SectionName));
        services.Configure<NotificationChannelSettings>(configuration.GetSection(NotificationChannelSettings.SectionName));

        services.AddSingleton<ITokenService, TokenService>();

        services.AddScoped<INotificationChannel, SmsNotificationChannel>();
        services.AddScoped<INotificationChannel, WhatsAppNotificationChannel>();

        services.AddSingleton<IReportExporter, CsvReportExporter>();
        services.AddSingleton<IReportExporter, ExcelReportExporter>();
        services.AddSingleton<IReportExporter, PdfReportExporter>();

        return services;
    }

    private static bool IsPostgres(string? provider, string connectionString)
    {
        if (!string.IsNullOrWhiteSpace(provider))
            return provider.StartsWith("postgres", StringComparison.OrdinalIgnoreCase) || provider.Equals("Npgsql", StringComparison.OrdinalIgnoreCase);

        return connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            || connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Converts a postgresql://user:pass@host:port/db URL into an Npgsql key/value connection string.</summary>
    private static string ToNpgsqlConnectionString(string connectionString)
    {
        if (!connectionString.Contains("://")) return connectionString;

        var uri = new Uri(connectionString);
        var userInfo = uri.UserInfo.Split(':', 2);
        return $"Host={uri.Host};Port={(uri.Port > 0 ? uri.Port : 5432)};Database={uri.AbsolutePath.TrimStart('/')};" +
               $"Username={Uri.UnescapeDataString(userInfo[0])};Password={(userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "")};" +
               "SSL Mode=Prefer;Trust Server Certificate=true";
    }
}
