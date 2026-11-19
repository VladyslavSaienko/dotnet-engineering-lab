using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LastCopy.Shared;

public static class InventoryServiceCollectionExtensions
{
    public const string ConnectionStringName = "inventory";

    public static IServiceCollection AddInventoryDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        services.AddSingleton(new SqlConnectionFactory(connectionString));
        services.AddSingleton<InventoryDatabase>();
        return services;
    }
}
