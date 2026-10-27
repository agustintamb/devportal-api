using DevportalApi.Data;
using DevportalApi.Endpoints;
using DevportalApi.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ASP.NET configuration merges appsettings.json and environment variables under the same key.
var allowedOrigins = builder.Configuration["ALLOWED_ORIGINS"]
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .WithHeaders("Content-Type", "Authorization", "X-API-Key")
    .AllowAnyMethod()));
builder.Services.AddOpenApi();

// A relative DB_PATH is resolved against the content root (the project folder with `dotnet run`).
var dbPath = builder.Configuration["DB_PATH"] ?? "data/portal.db";
var database = new Database(Path.Combine(builder.Environment.ContentRootPath, dbPath));
// Singleton: one shared instance; endpoints receive it as a handler parameter.
builder.Services.AddSingleton(database);

var app = builder.Build();

await database.InitializeAsync();

app.UseApiErrors();
app.UseCors();

app.MapOpenApi();
app.MapHealthEndpoints();
app.MapApisEndpoints();
app.MapClientsEndpoints();

app.Run();
