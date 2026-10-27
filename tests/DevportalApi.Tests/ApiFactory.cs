using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace DevportalApi.Tests;

// Boots the real API in memory (like supertest over an Express app) with test-only settings.
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string AllowedOrigin = "https://agustintamb.github.io";
    public const string JwtSecret = "test-only-jwt-secret-with-at-least-32-bytes";

    // Each factory (one per test class) gets its own SQLite file, seeded on startup.
    public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"devportal-api-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DB_PATH", DbPath);
        builder.UseSetting("JWT_SECRET", JwtSecret);
        builder.UseSetting("ALLOWED_ORIGINS", $"http://localhost:3000,http://localhost:3100,{AllowedOrigin}");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // Pooled connections keep the file locked on Windows; release them before deleting.
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { DbPath, $"{DbPath}-wal", $"{DbPath}-shm" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
