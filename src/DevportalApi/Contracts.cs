using System.ComponentModel;
using System.Text.Json.Serialization;

namespace DevportalApi;

public sealed record ApiDto(
    string Id,
    string Name,
    string Description,
    string Status,
    string Category,
    IReadOnlyList<string> Tags,
    string Version);

public sealed record ClientDto(string Name, string Slug);

public sealed record TokenRequest(string Username, string Password);

public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);

public sealed record StatDto(string Label, string Value, string Detail);

// SQLite INTEGER columns come back as long (64-bit), and Dapper matches constructor types exactly.
public sealed record ActivityDto(long Id, string Action, string Api, string Time, string Status);

public sealed record SubscriptionDto(Guid Id, string ApiId, string Plan, DateTimeOffset CreatedAt);

// Nullable on purpose: a missing field arrives as null and the endpoint answers 400 validation_error.
public sealed record CreateSubscriptionRequest(string? ApiId, string? Plan);

public sealed record UpdateSubscriptionRequest(string? Plan);

public sealed record UsageDto(int RequestsThisMonth, int MonthlyLimit, IReadOnlyList<ApiUsageDto> ByApi);

public sealed record ApiUsageDto(string ApiId, int Requests);

// OAuth 2.0 (RFC 6749) fixes these names in snake_case; [property: ...] puts the attribute on the generated property.
// The endpoint reads the form by hand; this record only documents the request body in the spec.
public sealed record OAuthTokenRequest(
    [property: JsonPropertyName("grant_type"), Description("Siempre `client_credentials`.")] string GrantType,
    [property: JsonPropertyName("client_id"), Description("Id del cliente, si no se envía en el header `Authorization: Basic`.")] string? ClientId = null,
    [property: JsonPropertyName("client_secret"), Description("Secreto del cliente, si no se envía en el header `Authorization: Basic`.")] string? ClientSecret = null,
    [property: JsonPropertyName("scope"), Description("Scopes separados por espacios; sin el parámetro se otorgan todos los del cliente.")] string? Scope = null);

public sealed record OAuthTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("scope")] string Scope);

public sealed record OAuthErrorResponse(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("error_description")] string ErrorDescription);
