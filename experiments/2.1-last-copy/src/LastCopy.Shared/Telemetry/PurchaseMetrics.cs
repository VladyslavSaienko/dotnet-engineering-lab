using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;

namespace LastCopy.Shared.Telemetry;

/// <summary>
/// FR-2.1-05: purchase metrics, identical for every variant.
/// Variant-specific instruments (retries for V1, deadlocks and lock timeouts for V2) belong here as well,
/// added together with the variants that produce them.
/// </summary>
public sealed class PurchaseMetrics
{
    public const string PurchasesInstrumentName = "lastcopy.purchases";
    public const string DurationInstrumentName = "lastcopy.purchase.duration";

    // Same boundaries as ASP.NET Core's http.server.request.duration, so both histograms can be compared directly.
    // The OpenTelemetry defaults are meant for milliseconds and would put almost every purchase in the first bucket.
    private static readonly double[] _durationBucketBoundaries =
        [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10];

    private readonly Counter<long> _purchases;
    private readonly Histogram<double> _duration;

    public PurchaseMetrics(IMeterFactory meterFactory, IOptions<LastCopyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(options);

        Variant = options.Value.Variant;

        var meter = meterFactory.Create(PurchaseTelemetry.Name);

        _purchases = meter.CreateCounter<long>(
            PurchasesInstrumentName,
            unit: "{purchase}",
            description: "Number of purchase attempts handled by the purchase service, by outcome.");

        _duration = meter.CreateHistogram(
            DurationInstrumentName,
            unit: "s",
            description: "Time the purchase service spent on a purchase attempt, by outcome.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = _durationBucketBoundaries });
    }

    public string Variant { get; }

    public void RecordPurchase(PurchaseOutcome outcome, TimeSpan duration)
    {
        var tags = new TagList
        {
            { PurchaseTelemetry.Tags.Variant, Variant },
            { PurchaseTelemetry.Tags.Outcome, PurchaseTelemetry.ToTagValue(outcome) },
        };

        _purchases.Add(1, tags);
        _duration.Record(duration.TotalSeconds, tags);
    }
}
