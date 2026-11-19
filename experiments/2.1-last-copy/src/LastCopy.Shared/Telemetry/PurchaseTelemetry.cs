using System.Diagnostics;

namespace LastCopy.Shared.Telemetry;

/// <summary>Names shared by the purchase meter, the activity source and their tags.</summary>
public static class PurchaseTelemetry
{
    /// <summary>Name of both the meter and the activity source. Registered with OpenTelemetry in ServiceDefaults.</summary>
    public const string Name = "LastCopy.Purchases";

    public const string PurchaseActivityName = "lastcopy.purchase";

    public static class Tags
    {
        public const string Variant = "variant";
        public const string Outcome = "outcome";
        public const string ProductId = "product_id";
        public const string Quantity = "quantity";
    }

    public static class Outcomes
    {
        public const string Succeeded = "succeeded";
        public const string InsufficientStock = "insufficient_stock";
        public const string ProductNotFound = "product_not_found";
    }

    // ActivitySource has no factory like IMeterFactory; a single static instance is the recommended pattern.
    internal static readonly ActivitySource ActivitySource = new(Name);

    internal static string ToTagValue(PurchaseOutcome outcome) => outcome switch
    {
        PurchaseOutcome.Succeeded => Outcomes.Succeeded,
        PurchaseOutcome.InsufficientStock => Outcomes.InsufficientStock,
        PurchaseOutcome.ProductNotFound => Outcomes.ProductNotFound,
        _ => throw new UnreachableException($"Unexpected outcome {outcome}."),
    };
}
