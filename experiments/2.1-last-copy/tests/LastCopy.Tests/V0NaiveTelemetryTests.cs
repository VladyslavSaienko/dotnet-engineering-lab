using System.Diagnostics.Metrics;
using System.Net;
using LastCopy.Shared.Telemetry;
using LastCopy.V0.Naive;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace LastCopy.Tests;

/// <summary>Verifies that V0, hosted as in production, reports purchase metrics through the shared decorator.</summary>
[Collection(SqlServerCollectionDefinition.Name)]
public sealed class V0NaiveTelemetryTests(SqlServerFixture sql) : IAsyncLifetime
{
    private VariantFactory<NaivePurchaseService> _factory = null!;
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new VariantFactory<NaivePurchaseService>(sql.GetConnectionString("inventory_v0_telemetry"));
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    [Trait("Requirement", "FR-2.1-05")]
    public async Task Purchases_through_the_api_are_counted_by_outcome_with_the_v0_variant_tag()
    {
        var ct = TestContext.Current.CancellationToken;
        var meterFactory = _factory.Services.GetRequiredService<IMeterFactory>();
        using var counter = new MetricCollector<long>(meterFactory, PurchaseTelemetry.Name, PurchaseMetrics.PurchasesInstrumentName);
        using var duration = new MetricCollector<double>(meterFactory, PurchaseTelemetry.Name, PurchaseMetrics.DurationInstrumentName);
        await _client.ResetAsync(productCount: 1, initialStock: 5, ct);

        Assert.Equal(HttpStatusCode.Created, await _client.PurchaseAsync(productId: 1, quantity: 3, ct));
        Assert.Equal(HttpStatusCode.Conflict, await _client.PurchaseAsync(productId: 1, quantity: 3, ct));
        Assert.Equal(HttpStatusCode.NotFound, await _client.PurchaseAsync(productId: 999, quantity: 1, ct));
        // Rejected by model validation before the purchase service runs, so it is not a purchase outcome.
        Assert.Equal(HttpStatusCode.BadRequest, await _client.PurchaseAsync(productId: 1, quantity: 0, ct));

        var measurements = counter.GetMeasurementSnapshot();
        Assert.Equal(3, measurements.Count);
        Assert.All(measurements, m => Assert.Equal("v0-naive", m.Tags[PurchaseTelemetry.Tags.Variant]));
        Assert.Equal(
            ["insufficient_stock", "product_not_found", "succeeded"],
            measurements.Select(m => (string?)m.Tags[PurchaseTelemetry.Tags.Outcome]).Order());
        Assert.Equal(3, duration.GetMeasurementSnapshot().Count);
    }
}
