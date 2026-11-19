using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LastCopy.Shared.Controllers;

/// <summary>FR-2.1-01: the same API for every variant. Input validation comes from [ApiController].</summary>
[ApiController]
[Route("purchases")]
public sealed class PurchasesController(IPurchaseService purchases) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Purchase(PurchaseRequest request, CancellationToken cancellationToken)
    {
        var result = await purchases.PurchaseAsync(request.ProductId, request.Quantity, cancellationToken);

        return result.Outcome switch
        {
            PurchaseOutcome.Succeeded => Created($"/purchases/{result.PurchaseId}", null),
            PurchaseOutcome.InsufficientStock => Conflict(),
            PurchaseOutcome.ProductNotFound => NotFound(),
            _ => throw new UnreachableException($"Unexpected outcome {result.Outcome}."),
        };
    }
}
