using Dapper;
using DevportalApi.Data;
using Microsoft.Data.Sqlite;

namespace DevportalApi.Tests;

public sealed class DatabaseTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"devportal-api-db-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Initialize_NewDatabase_CreatesFolderAndSeedsTables()
    {
        var database = new Database(Path.Combine(_folder, "nested", "portal.db"));

        await database.InitializeAsync();

        Assert.Equal(3, await CountAsync(database, "apis"));
        Assert.Equal(8, await CountAsync(database, "clients"));
        Assert.Equal(5, await CountAsync(database, "activity"));
        Assert.Equal(0, await CountAsync(database, "subscriptions"));
    }

    [Fact]
    public async Task Initialize_RunTwice_DoesNotDuplicateSeed()
    {
        var database = new Database(Path.Combine(_folder, "portal.db"));

        await database.InitializeAsync();
        await database.InitializeAsync();

        Assert.Equal(3, await CountAsync(database, "apis"));
        Assert.Equal(8, await CountAsync(database, "clients"));
        Assert.Equal(5, await CountAsync(database, "activity"));
    }

    [Fact]
    public void Startup_CreatesDatabaseAtConfiguredPath()
    {
        using var factory = new ApiFactory();

        factory.CreateClient();

        Assert.True(File.Exists(factory.DbPath));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static async Task<long> CountAsync(Database database, string table)
    {
        await using var connection = await database.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {table}");
    }
}
