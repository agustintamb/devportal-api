using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DevportalApi.Tests;

// Boots the real API in memory (like supertest over an Express app) with test-only settings.
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string AllowedOrigin = "https://agustintamb.github.io";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"devportal-api-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DB_PATH", _dbPath);
        builder.UseSetting("JWT_SECRET", "test-only-jwt-secret-with-at-least-32-bytes");
        builder.UseSetting("ALLOWED_ORIGINS", $"http://localhost:3000,http://localhost:3100,{AllowedOrigin}");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
