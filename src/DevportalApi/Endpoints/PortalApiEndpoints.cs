namespace DevportalApi.Endpoints;

public enum PortalApiVersion { V1, V2 }

// Versioning by hand: the same Map…Endpoints functions register every resource twice, at the root (v1)
// and under /v2 (v2). Only the four lists change their handler in v2 (paged responses).
public static class PortalApiEndpoints
{
    public const string V2GroupName = "v2";

    public static IEndpointRouteBuilder MapPortalApi(this IEndpointRouteBuilder app)
    {
        MapVersion(app, PortalApiVersion.V1);
        // WithGroupName puts every /v2 endpoint in the "v2" OpenAPI document (and out of "v1").
        MapVersion(app.MapGroup("/v2").WithGroupName(V2GroupName), PortalApiVersion.V2);
        return app;
    }

    // "" for v1, "/v2" for v2: prefix for paths written in texts and headers.
    public static string PathPrefix(this PortalApiVersion version) => version == PortalApiVersion.V2 ? "/v2" : "";

    // ASP.NET requires unique endpoint names (they become the operationId), so v2 adds a suffix: listApis → listApisV2.
    public static string EndpointName(this PortalApiVersion version, string name) =>
        version == PortalApiVersion.V2 ? $"{name}V2" : name;

    private static void MapVersion(IEndpointRouteBuilder app, PortalApiVersion version)
    {
        app.MapHealthEndpoints(version);
        app.MapApisEndpoints(version);
        app.MapClientsEndpoints(version);
        app.MapAuthEndpoints(version);
        app.MapOAuthEndpoints(version);
        app.MapDashboardEndpoints(version);
        app.MapSubscriptionsEndpoints(version);
        app.MapUsageEndpoints(version);
    }
}
