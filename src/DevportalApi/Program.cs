using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using DevportalApi.Data;
using DevportalApi.Endpoints;
using DevportalApi.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// `dotnet build` runs this file through the OpenAPI generator (GetDocument.Insider) only up to builder.Build().
var isGeneratingOpenApi = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

// ASP.NET configuration merges appsettings.json and environment variables under the same key.
var allowedOrigins = builder.Configuration["ALLOWED_ORIGINS"]
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .WithHeaders("Content-Type", "Authorization", "X-API-Key")
    .AllowAnyMethod()));

// The web defaults also accept numbers sent as strings ("3600"); Strict keeps JSON numbers (and the spec) as plain integers.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

var productionUrl = builder.Configuration["PROD_URL"] is { Length: > 0 } prodUrl
    ? prodUrl
    : OpenApiDocumentation.DefaultProductionUrl;
// One OpenAPI document per version (/openapi/v1.json and /openapi/v2.json); `dotnet build` writes both to openapi/.
foreach (var version in Enum.GetValues<PortalApiVersion>())
{
    builder.Services.AddOpenApi(version.ToString().ToLowerInvariant(), options => options
        .AddPortalApiDocumentation(version, productionUrl)
        .AddPortalApiSecurity(version));
}

// Fails at startup (before serving requests) if JWT_SECRET is missing or too short.
// The spec generator never serves requests, so it gets a random throwaway secret instead.
var jwtSecret = isGeneratingOpenApi
    ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    : builder.Configuration["JWT_SECRET"];
var tokens = new Tokens(jwtSecret);
builder.Services.AddSingleton(tokens);

// JWT is the default scheme (used by UseAuthentication on every request); ApiKey only runs for the policy that names it.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    // Keeps claim names as they are in the JWT ("sub") instead of mapping them to long .NET names.
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = Tokens.Issuer,
        ValidAudience = Tokens.Audience,
        IssuerSigningKey = tokens.SigningKey,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        // Default is 5 minutes of tolerance; zero makes "expired" mean exactly past `exp`.
        ClockSkew = TimeSpan.Zero,
    };
    options.Events = new JwtBearerEvents
    {
        // Runs for every 401 (no header, other scheme, invalid or expired token).
        OnChallenge = context =>
        {
            context.HandleResponse();
            context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;
            return ApiErrors.Problem(
                    StatusCodes.Status401Unauthorized,
                    "unauthorized",
                    "Falta el token de acceso o no es válido.")
                .ExecuteAsync(context.HttpContext);
        },
    };
})
.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization(AuthPolicies.Configure);

// A relative DB_PATH is resolved against the content root (the project folder with `dotnet run`).
var configuredDbPath = builder.Configuration["DB_PATH"];
var dbPath = string.IsNullOrWhiteSpace(configuredDbPath) ? "db/portal.db" : configuredDbPath;
var database = new Database(Path.Combine(builder.Environment.ContentRootPath, dbPath));
// Singleton: one shared instance; endpoints receive it as a handler parameter.
builder.Services.AddSingleton(database);

var app = builder.Build();

await database.InitializeAsync();

app.UseApiErrors();
app.UseCors();
// Explicit so they run after CORS: otherwise ASP.NET adds them first and 401s lose the CORS headers.
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapPortalApi();

app.Run();
