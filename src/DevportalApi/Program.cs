using DevportalApi.Data;
using DevportalApi.Endpoints;
using DevportalApi.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ASP.NET configuration merges appsettings.json and environment variables under the same key.
var allowedOrigins = builder.Configuration["ALLOWED_ORIGINS"]
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .WithHeaders("Content-Type", "Authorization", "X-API-Key")
    .AllowAnyMethod()));
builder.Services.AddOpenApi(options => options.AddBearerSecurity());

// Fails at startup (before serving requests) if JWT_SECRET is missing or too short.
var tokens = new Tokens(builder.Configuration["JWT_SECRET"]);
builder.Services.AddSingleton(tokens);

builder.Services.AddAuthentication().AddJwtBearer(options =>
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
});
builder.Services.AddAuthorization();

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
app.MapHealthEndpoints();
app.MapApisEndpoints();
app.MapClientsEndpoints();
app.MapAuthEndpoints();
app.MapDashboardEndpoints();
app.MapSubscriptionsEndpoints();

app.Run();
