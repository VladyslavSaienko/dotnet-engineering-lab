using System.Net;
using System.Net.Http.Json;
using LastCopy.Shared;
using LastCopy.Shared.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace LastCopy.Tests;

/// <summary>One SQL Server container shared by all tests; each variant uses its own database.</summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string GetConnectionString(string databaseName) =>
        new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = databaseName }.ConnectionString;

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollectionDefinition : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "sql-server";
}

/// <summary>Hosts a variant in memory, pointed at the test database, with test-support endpoints enabled.</summary>
public sealed class VariantFactory<TVariantMarker>(string connectionString) : WebApplicationFactory<TVariantMarker>
    where TVariantMarker : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting($"ConnectionStrings:{InventoryServiceCollectionExtensions.ConnectionStringName}", connectionString);
    }
}

public sealed record BurstResult(int Created, int Conflict, int Other);

public static class InventoryClient
{
    public static async Task ResetAsync(this HttpClient client, int productCount, int initialStock, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            "/test-support/reset", new ResetRequest(productCount, initialStock), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<InventorySummary> GetSummaryAsync(this HttpClient client, CancellationToken cancellationToken) =>
        await client.GetFromJsonAsync<InventorySummary>("/test-support/summary", cancellationToken)
            ?? throw new InvalidOperationException("Summary endpoint returned no content.");

    public static async Task<HttpStatusCode> PurchaseAsync(
        this HttpClient client, int productId, int quantity, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            "/purchases", new PurchaseRequest(productId, quantity), cancellationToken);
        return response.StatusCode;
    }

    /// <summary>Sends all purchases at once to create maximum contention on one product.</summary>
    public static async Task<BurstResult> PurchaseBurstAsync(
        this HttpClient client, int productId, int requests, CancellationToken cancellationToken)
    {
        var statuses = await Task.WhenAll(
            Enumerable.Range(0, requests).Select(_ => client.PurchaseAsync(productId, 1, cancellationToken)));

        return new BurstResult(
            Created: statuses.Count(s => s == HttpStatusCode.Created),
            Conflict: statuses.Count(s => s == HttpStatusCode.Conflict),
            Other: statuses.Count(s => s is not HttpStatusCode.Created and not HttpStatusCode.Conflict));
    }
}
