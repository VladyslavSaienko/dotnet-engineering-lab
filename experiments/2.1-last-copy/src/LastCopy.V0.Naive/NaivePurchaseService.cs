using LastCopy.Shared;
using Microsoft.Data.SqlClient;

namespace LastCopy.V0.Naive;

/// <summary>
/// V0, the control variant: read the stock, check it in code, write the new value.
/// There is no concurrency control on purpose — this variant exists to show the problem.
/// </summary>
public sealed class NaivePurchaseService(SqlConnectionFactory connections) : IPurchaseService
{
    public async Task<PurchaseResult> PurchaseAsync(int productId, int quantity, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        var stock = await ReadStockAsync(connection, productId, cancellationToken);
        if (stock is null)
        {
            return PurchaseResult.ProductNotFound;
        }

        if (stock < quantity)
        {
            return PurchaseResult.InsufficientStock;
        }

        // Between the read above and this write, other requests may have bought the same copies.
        // Writing an absolute value silently overwrites their changes (lost update), so copies get oversold.
        await WriteStockAsync(connection, productId, stock.Value - quantity, cancellationToken);
        var purchaseId = await RecordPurchaseAsync(connection, productId, quantity, cancellationToken);

        return PurchaseResult.Succeeded(purchaseId);
    }

    private static async Task<int?> ReadStockAsync(SqlConnection connection, int productId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Stock FROM dbo.Products WHERE Id = @id;";
        command.Parameters.AddWithValue("@id", productId);

        return await command.ExecuteScalarAsync(cancellationToken) as int?;
    }

    private static async Task WriteStockAsync(SqlConnection connection, int productId, int newStock, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dbo.Products SET Stock = @stock WHERE Id = @id;";
        command.Parameters.AddWithValue("@stock", newStock);
        command.Parameters.AddWithValue("@id", productId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> RecordPurchaseAsync(SqlConnection connection, int productId, int quantity, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dbo.Purchases (ProductId, Quantity)
            OUTPUT INSERTED.Id
            VALUES (@productId, @quantity);
            """;
        command.Parameters.AddWithValue("@productId", productId);
        command.Parameters.AddWithValue("@quantity", quantity);

        return await command.ExecuteScalarAsync(cancellationToken) as long?
            ?? throw new InvalidOperationException("Inserting a purchase did not return its id.");
    }
}
