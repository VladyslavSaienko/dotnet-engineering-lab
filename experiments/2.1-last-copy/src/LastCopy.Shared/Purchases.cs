using System.ComponentModel.DataAnnotations;

namespace LastCopy.Shared;

/// <summary>
/// The only thing that differs between variants: how a purchase is applied to the stock.
/// </summary>
public interface IPurchaseService
{
    Task<PurchaseResult> PurchaseAsync(int productId, int quantity, CancellationToken cancellationToken);
}

public sealed record PurchaseRequest(
    [Range(1, int.MaxValue)] int ProductId,
    [Range(1, int.MaxValue)] int Quantity);

public enum PurchaseOutcome
{
    Succeeded,
    InsufficientStock,
    ProductNotFound,
}

public sealed record PurchaseResult(PurchaseOutcome Outcome, long? PurchaseId = null)
{
    public static readonly PurchaseResult InsufficientStock = new(PurchaseOutcome.InsufficientStock);

    public static readonly PurchaseResult ProductNotFound = new(PurchaseOutcome.ProductNotFound);

    public static PurchaseResult Succeeded(long purchaseId) => new(PurchaseOutcome.Succeeded, purchaseId);
}
