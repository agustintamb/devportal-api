using Dapper;
using Microsoft.Data.Sqlite;

namespace DevportalApi.Data;

public sealed class Database
{
    private readonly string _filePath;
    private readonly string _connectionString;

    public Database(string filePath)
    {
        _filePath = Path.GetFullPath(filePath);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = _filePath }.ToString();
    }

    public async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    // Creates the tables if missing and seeds them only when the catalog is empty.
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        // `await using` disposes the connection and the transaction when the method ends (like try/finally).
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await connection.ExecuteAsync(ReadSql("schema.sql"), transaction: transaction);
        var apiCount = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM apis", transaction: transaction);
        if (apiCount == 0)
        {
            await connection.ExecuteAsync(ReadSql("seed.sql"), transaction: transaction);
        }

        await transaction.CommitAsync();
    }

    // The .sql files are compiled into the assembly as embedded resources (see DevportalApi.csproj).
    private static string ReadSql(string resourceName)
    {
        using var stream = typeof(Database).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
