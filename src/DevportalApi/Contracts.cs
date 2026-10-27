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
