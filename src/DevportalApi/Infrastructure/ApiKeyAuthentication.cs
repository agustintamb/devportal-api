using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DevportalApi.Infrastructure;

// Authentication scheme "ApiKey": accepts the demo key sent in the X-API-Key header.
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-API-Key";

    // Public on purpose, like the other demo credentials: it is documented in the spec.
    private const string DemoApiKey = "demo-api-key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var apiKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (apiKey != DemoApiKey)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        // The key identifies no user; an identity with an authentication type is enough to count as authenticated.
        var principal = new ClaimsPrincipal(new ClaimsIdentity(SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }

    // Runs for every 401 of an endpoint that uses this scheme (no key or a wrong one).
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = SchemeName;
        return ApiErrors.Problem(
                StatusCodes.Status401Unauthorized,
                "unauthorized",
                $"Falta la API key (header {HeaderName}) o no es válida.")
            .ExecuteAsync(Context);
    }
}
