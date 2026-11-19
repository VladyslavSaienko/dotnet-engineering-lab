using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using LastCopy.Shared;
using LastCopy.Shared.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Options;

namespace LastCopy.Tests;

/// <summary>Unit tests for the decorator that records purchase metrics and spans for every variant.</summary>
public sealed class InstrumentedPurchaseServiceTests : IDisposable
{
    private const string Variant = "test-variant";

    private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
    private readonly IMeterFactory _meterFactory;
    private readonly PurchaseMetrics _metrics;

    public InstrumentedPurchaseServiceTests()
    {
        _meterFactory = _services.GetRequiredService<IMeterFactory>();
        _metrics = new PurchaseMetrics(_meterFactory, Options.Create(new LastCopyOptions { Variant = Variant }));
    }

    public void Dispose() => _services.Dispose();

    public static TheoryData<PurchaseOutcome, string> Outcomes => new()
    {
        { PurchaseOutcome.Succeeded, "succeeded" },
        { PurchaseOutcome.InsufficientStock, "insufficient_stock" },
        { PurchaseOutcome.ProductNotFound, "product_not_found" },
    };

    [Theory]
    [Trait("Requirement", "FR-2.1-05")]
    [MemberData(nameof(Outcomes))]
    public async Task Purchase_increments_the_counter_once_with_outcome_and_variant_tags(PurchaseOutcome outcome, string expectedTag)
    {
        using var counter = new MetricCollector<long>(_meterFactory, PurchaseTelemetry.Name, PurchaseMetrics.PurchasesInstrumentName);
        var service = new InstrumentedPurchaseService(new StubPurchaseService(ResultFor(outcome)), _metrics);

        await service.PurchaseAsync(productId: 1, quantity: 1, TestContext.Current.CancellationToken);

        var measurement = Assert.Single(counter.GetMeasurementSnapshot());
        Assert.Equal(1, measurement.Value);
        Assert.Equal(expectedTag, measurement.Tags[PurchaseTelemetry.Tags.Outcome]);
        Assert.Equal(Variant, measurement.Tags[PurchaseTelemetry.Tags.Variant]);
    }

    [Theory]
    [Trait("Requirement", "FR-2.1-05")]
    [MemberData(nameof(Outcomes))]
    public async Task Purchase_records_its_duration_in_seconds_with_outcome_and_variant_tags(PurchaseOutcome outcome, string expectedTag)
    {
        using var duration = new MetricCollector<double>(_meterFactory, PurchaseTelemetry.Name, PurchaseMetrics.DurationInstrumentName);
        var inner = new StubPurchaseService(ResultFor(outcome), delay: TimeSpan.FromMilliseconds(20));
        var service = new InstrumentedPurchaseService(inner, _metrics);

        await service.PurchaseAsync(productId: 1, quantity: 1, TestContext.Current.CancellationToken);

        var measurement = Assert.Single(duration.GetMeasurementSnapshot());
        Assert.Equal("s", duration.Instrument?.Unit);
        // A 20 ms purchase is about 0.02 in seconds; a value recorded in milliseconds would exceed the upper bound.
        Assert.InRange(measurement.Value, 0.015, 10);
        Assert.Equal(expectedTag, measurement.Tags[PurchaseTelemetry.Tags.Outcome]);
        Assert.Equal(Variant, measurement.Tags[PurchaseTelemetry.Tags.Variant]);
    }

    [Fact]
    [Trait("Requirement", "FR-2.1-05")]
    public async Task Failed_purchase_is_not_counted_as_an_outcome_and_the_exception_propagates()
    {
        using var counter = new MetricCollector<long>(_meterFactory, PurchaseTelemetry.Name, PurchaseMetrics.PurchasesInstrumentName);
        using var duration = new MetricCollector<double>(_meterFactory, PurchaseTelemetry.Name, PurchaseMetrics.DurationInstrumentName);
        var service = new InstrumentedPurchaseService(new ThrowingPurchaseService(), _metrics);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PurchaseAsync(productId: 1, quantity: 1, TestContext.Current.CancellationToken));

        Assert.Empty(counter.GetMeasurementSnapshot());
        Assert.Empty(duration.GetMeasurementSnapshot());
    }

    [Fact]
    [Trait("Requirement", "NFR-2.1-03")]
    public async Task Purchase_creates_a_span_with_product_quantity_variant_and_outcome_tags()
    {
        // The activity source is static, so spans from tests running in parallel arrive here too.
        // A product id no other test uses picks out the span created by this test.
        const int productId = 424_242;
        var spans = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PurchaseTelemetry.Name,
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        var service = new InstrumentedPurchaseService(new StubPurchaseService(PurchaseResult.InsufficientStock), _metrics);

        await service.PurchaseAsync(productId, quantity: 3, TestContext.Current.CancellationToken);

        var span = Assert.Single(spans, s => Equals(s.GetTagItem(PurchaseTelemetry.Tags.ProductId), productId));
        Assert.Equal(PurchaseTelemetry.PurchaseActivityName, span.OperationName);
        Assert.Equal(3, span.GetTagItem(PurchaseTelemetry.Tags.Quantity));
        Assert.Equal(Variant, span.GetTagItem(PurchaseTelemetry.Tags.Variant));
        Assert.Equal("insufficient_stock", span.GetTagItem(PurchaseTelemetry.Tags.Outcome));
    }

    private static PurchaseResult ResultFor(PurchaseOutcome outcome) => outcome switch
    {
        PurchaseOutcome.Succeeded => PurchaseResult.Succeeded(purchaseId: 42),
        PurchaseOutcome.InsufficientStock => PurchaseResult.InsufficientStock,
        PurchaseOutcome.ProductNotFound => PurchaseResult.ProductNotFound,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    private sealed class StubPurchaseService(PurchaseResult result, TimeSpan delay = default) : IPurchaseService
    {
        public async Task<PurchaseResult> PurchaseAsync(int productId, int quantity, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return result;
        }
    }

    private sealed class ThrowingPurchaseService : IPurchaseService
    {
        public Task<PurchaseResult> PurchaseAsync(int productId, int quantity, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated database failure.");
    }
}
