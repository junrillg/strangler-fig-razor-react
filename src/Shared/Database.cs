using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Strangler.Common;

public static class Database
{
    public static IServiceCollection AddLabDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = configuration["Database:Host"] ?? "postgres",
            Database = configuration["Database:Name"] ?? "strangler",
            Username = configuration["Database:Username"] ?? "monolith_app",
            Password = Secrets.Read(configuration, "Database:PasswordFile", "/run/secrets/monolith_db_password"),
            MaxPoolSize = 20,
            Timeout = 3,
            CommandTimeout = 5,
            IncludeErrorDetail = false,
            ApplicationName = "strangler-monolith",
        };
        if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database) ||
            string.IsNullOrWhiteSpace(connection.Username))
        {
            throw new InvalidOperationException("Database host, name and username must be configured.");
        }

        services.AddSingleton(_ => NpgsqlDataSource.Create(connection.ConnectionString));
        services.AddScoped<Requisitions>();
        services.AddScoped<Notes>();
        return services;
    }
}
