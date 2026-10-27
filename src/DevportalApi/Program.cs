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

var app = builder.Build();

app.UseApiErrors();
app.UseCors();

app.MapOpenApi();
app.MapHealthEndpoints();

app.Run();
