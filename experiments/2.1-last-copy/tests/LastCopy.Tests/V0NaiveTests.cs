using System.Net;
using LastCopy.V0.Naive;

namespace LastCopy.Tests;

[Collection(SqlServerCollectionDefinition.Name)]
public sealed class V0NaiveTests(SqlServerFixture sql, ITestOutputHelper output) : IAsyncLifetime
{
    private VariantFactory<NaivePurchaseService> _factory = null!;
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new VariantFactory<NaivePurchaseService>(sql.GetConnectionString("inventory_v0"));
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    [Trait("Requirement", "FR-2.1-01")]
    public async Task Purchase_with_enough_stock_succeeds_and_decreases_stock()
    {
        var ct = TestContext.Current.CancellationToken;
        await _client.ResetAsync(productCount: 1, initialStock: 10, ct);

        var status = await _client.PurchaseAsync(productId: 1, quantity: 3, ct);
        var summary = await _client.GetSummaryAsync(ct);

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(7, summary.Products[0].RemainingStock);
        Assert.Equal(3, summary.Products[0].Sold);
    }

    [Fact]
    [Trait("Requirement", "AC-2.1-02")]
    public async Task Concurrent_purchases_of_the_last_copies_are_recorded_even_when_oversold()
    {
        var ct = TestContext.Current.CancellationToken;
        await _client.ResetAsync(productCount: 1, initialStock: 10, ct);

        var burst = await _client.PurchaseBurstAsync(productId: 1, requests: 1000, ct);
        var product = (await _client.GetSummaryAsync(ct)).Products[0];

        output.WriteLine(
            $"V0: created={burst.Created}, conflict={burst.Conflict}, other={burst.Other}, " +
            $"sold={product.Sold}, oversold={product.Oversold}, remaining={product.RemainingStock}");

        // V0 is expected to oversell; this test documents the effect rather than failing on it.
        // What must hold: every 201 response corresponds to a recorded purchase.
        Assert.Equal(0, burst.Other);
        Assert.Equal(burst.Created, product.Sold);
        Assert.True(product.Sold >= 10, "At least the available copies should be sold.");
    }

    [Fact]
    [Trait("Requirement", "AC-2.1-03")]
    public async Task Purchase_exceeding_stock_is_rejected_and_stock_is_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        await _client.ResetAsync(productCount: 1, initialStock: 5, ct);

        var status = await _client.PurchaseAsync(productId: 1, quantity: 6, ct);
        var product = (await _client.GetSummaryAsync(ct)).Products[0];

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(5, product.RemainingStock);
        Assert.Equal(0, product.Sold);
    }

    [Theory]
    [Trait("Requirement", "AC-2.1-04")]
    [InlineData(1, 0, HttpStatusCode.BadRequest)]
    [InlineData(1, -1, HttpStatusCode.BadRequest)]
    [InlineData(0, 1, HttpStatusCode.BadRequest)]
    [InlineData(999, 1, HttpStatusCode.NotFound)]
    public async Task Invalid_purchase_is_rejected_and_stock_is_unchanged(int productId, int quantity, HttpStatusCode expected)
    {
        var ct = TestContext.Current.CancellationToken;
        await _client.ResetAsync(productCount: 1, initialStock: 5, ct);

        var status = await _client.PurchaseAsync(productId, quantity, ct);
        var product = (await _client.GetSummaryAsync(ct)).Products[0];

        Assert.Equal(expected, status);
        Assert.Equal(5, product.RemainingStock);
    }
}
