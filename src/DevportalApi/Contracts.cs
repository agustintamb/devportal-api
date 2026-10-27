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
