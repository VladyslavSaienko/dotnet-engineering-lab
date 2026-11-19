using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LastCopy.Shared.Controllers;

/// <summary>FR-2.1-04: reset and inspect the data between runs. Available only in Development.</summary>
[ApiController]
[Route("test-support")]
[DevelopmentOnly]
public sealed class TestSupportController(InventoryDatabase database) : ControllerBase
{
    [HttpPost("reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Reset(ResetRequest request, CancellationToken cancellationToken)
    {
        await database.ResetAsync(request.ProductCount, request.InitialStock, cancellationToken);
        return NoContent();
    }

    [HttpGet("summary")]
    public Task<InventorySummary> Summary(CancellationToken cancellationToken) =>
        database.GetSummaryAsync(cancellationToken);
}

public sealed record ResetRequest(
    [Range(1, 100_000)] int ProductCount,
    [Range(0, int.MaxValue)] int InitialStock);

/// <summary>Returns 404 outside the Development environment, as if the endpoint did not exist.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DevelopmentOnlyAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var environment = context.HttpContext.RequestServices.GetRequiredService<IHostEnvironment>();
        if (!environment.IsDevelopment())
        {
            context.Result = new NotFoundResult();
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
