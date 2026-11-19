using System.Diagnostics;

namespace LastCopy.Shared.Telemetry;

/// <summary>
/// Adds metrics and a span around any <see cref="IPurchaseService"/>, so variants contain only
/// their concurrency mechanism and all of them are measured the same way (NFR-2.1-01).
/// </summary>
public sealed class InstrumentedPurchaseService(IPurchaseService inner, PurchaseMetrics metrics) : IPurchaseService
{
    public async Task<PurchaseResult> PurchaseAsync(int productId, int quantity, CancellationToken cancellationToken)
    {
        using var activity = PurchaseTelemetry.ActivitySource.StartActivity(PurchaseTelemetry.PurchaseActivityName);
        activity?.SetTag(PurchaseTelemetry.Tags.ProductId, productId);
        activity?.SetTag(PurchaseTelemetry.Tags.Quantity, quantity);
        activity?.SetTag(PurchaseTelemetry.Tags.Variant, metrics.Variant);

        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var result = await inner.PurchaseAsync(productId, quantity, cancellationToken);

            metrics.RecordPurchase(result.Outcome, Stopwatch.GetElapsedTime(startedAt));
            activity?.SetTag(PurchaseTelemetry.Tags.Outcome, PurchaseTelemetry.ToTagValue(result.Outcome));

            return result;
        }
        catch (Exception exception)
        {
            // Failures are not a purchase outcome (see SPEC open questions); they surface as failed spans
            // here and as 5xx in the ASP.NET Core http.server.* metrics.
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddException(exception);
            throw;
        }
    }
}
