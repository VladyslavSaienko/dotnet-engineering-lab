using Microsoft.Data.SqlClient;

namespace LastCopy.Shared;

/// <summary>
/// Schema, seeding and reporting shared by all variants (NFR-2.1-01).
/// Variants only differ in how <see cref="IPurchaseService"/> changes the stock.
/// </summary>
public sealed class InventoryDatabase(SqlConnectionFactory connections)
{
    private const string CreateDatabaseSql = """
        IF DB_ID(@name) IS NULL
        BEGIN
            DECLARE @sql NVARCHAR(MAX) = N'CREATE DATABASE ' + QUOTENAME(@name);
            EXEC (@sql);
        END
        """;

    private const string CreateSchemaSql = """
        IF OBJECT_ID(N'dbo.Products', N'U') IS NULL
        CREATE TABLE dbo.Products
        (
            Id           INT           NOT NULL PRIMARY KEY,
            Name         NVARCHAR(200) NOT NULL,
            InitialStock INT           NOT NULL,
            Stock        INT           NOT NULL,
            RowVersion   ROWVERSION    NOT NULL
        );

        IF OBJECT_ID(N'dbo.Purchases', N'U') IS NULL
        CREATE TABLE dbo.Purchases
        (
            Id           BIGINT    IDENTITY NOT NULL PRIMARY KEY,
            ProductId    INT       NOT NULL REFERENCES dbo.Products (Id),
            Quantity     INT       NOT NULL,
            CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
        );
        """;

    private const string ResetSql = """
        DELETE FROM dbo.Purchases;
        DELETE FROM dbo.Products;

        DECLARE @i INT = 1;
        WHILE @i <= @productCount
        BEGIN
            INSERT INTO dbo.Products (Id, Name, InitialStock, Stock)
            VALUES (@i, CONCAT(N'Book ', @i), @initialStock, @initialStock);
            SET @i += 1;
        END
        """;

    private const string SummarySql = """
        SELECT p.Id, p.InitialStock, p.Stock, COALESCE(SUM(pu.Quantity), 0) AS Sold
        FROM dbo.Products AS p
        LEFT JOIN dbo.Purchases AS pu ON pu.ProductId = p.Id
        GROUP BY p.Id, p.InitialStock, p.Stock
        ORDER BY p.Id;
        """;

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseExistsAsync(cancellationToken);

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = CreateSchemaSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ResetAsync(int productCount, int initialStock, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ResetSql;
        command.Parameters.AddWithValue("@productCount", productCount);
        command.Parameters.AddWithValue("@initialStock", initialStock);

        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<InventorySummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = SummarySql;

        var products = new List<ProductSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            products.Add(new ProductSummary(
                ProductId: reader.GetInt32(0),
                InitialStock: reader.GetInt32(1),
                RemainingStock: reader.GetInt32(2),
                Sold: reader.GetInt32(3)));
        }

        return new InventorySummary(products);
    }

    // Aspire creates the database itself; Testcontainers does not. Creating it here keeps both paths identical.
    private async Task EnsureDatabaseExistsAsync(CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(connections.ConnectionString);
        var databaseName = builder.InitialCatalog;
        if (string.IsNullOrEmpty(databaseName) || databaseName == "master")
        {
            return;
        }

        builder.InitialCatalog = "master";
        await using var master = new SqlConnection(builder.ConnectionString);
        await master.OpenAsync(cancellationToken);
        await using var command = master.CreateCommand();
        command.CommandText = CreateDatabaseSql;
        command.Parameters.AddWithValue("@name", databaseName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed record ProductSummary(int ProductId, int InitialStock, int RemainingStock, int Sold)
{
    public int Oversold => Math.Max(0, Sold - InitialStock);
}

public sealed record InventorySummary(IReadOnlyList<ProductSummary> Products)
{
    public int TotalSold => Products.Sum(p => p.Sold);

    public int TotalOversold => Products.Sum(p => p.Oversold);
}
