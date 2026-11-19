using Microsoft.Data.SqlClient;

namespace LastCopy.Shared;

public sealed class SqlConnectionFactory(string connectionString)
{
    public string ConnectionString { get; } = connectionString;

    public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
