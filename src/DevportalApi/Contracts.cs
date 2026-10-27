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
