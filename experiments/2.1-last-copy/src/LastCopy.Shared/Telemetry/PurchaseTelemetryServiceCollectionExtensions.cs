using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LastCopy.Shared.Telemetry;

public static class PurchaseTelemetryServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TService"/> as the variant's purchase logic, wrapped in
    /// <see cref="InstrumentedPurchaseService"/>. The variant itself stays unaware of telemetry.
    /// </summary>
    public static IServiceCollection AddInstrumentedPurchaseService<TService>(this IServiceCollection services)
        where TService : class, IPurchaseService
    {
        services.AddOptions<LastCopyOptions>()
            .BindConfiguration(LastCopyOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddMetrics();
        services.TryAddSingleton<PurchaseMetrics>();
        services.TryAddSingleton<TService>();
        services.AddSingleton<IPurchaseService>(provider => new InstrumentedPurchaseService(
            provider.GetRequiredService<TService>(),
            provider.GetRequiredService<PurchaseMetrics>()));

        return services;
    }
}
