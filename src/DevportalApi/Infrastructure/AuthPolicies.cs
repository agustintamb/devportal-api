using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace DevportalApi.Infrastructure;

// Who can call what. User tokens (POST /auth/token) have no "scope" claim and reach every JWT endpoint;
// OAuth client tokens (POST /oauth/token) only reach the endpoints their scopes grant.
public static class AuthPolicies
{
    public const string ApiKey = "ApiKey";
    public const string SubscriptionsRead = "SubscriptionsRead";

    public const string ScopeClaim = "scope";
    public const string SubscriptionsReadScope = "subscriptions:read";

    public static void Configure(AuthorizationOptions options)
    {
        // Used by RequireAuthorization() without a policy name: a valid JWT that is not a client token.
        options.DefaultPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => IsUserToken(context.User))
            .Build();

        options.AddPolicy(SubscriptionsRead, policy => policy
            .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => IsUserToken(context.User) || HasScope(context.User, SubscriptionsReadScope)));

        options.AddPolicy(ApiKey, policy => policy
            .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser());
    }

    private static bool IsUserToken(ClaimsPrincipal user) => !user.HasClaim(claim => claim.Type == ScopeClaim);

    private static bool HasScope(ClaimsPrincipal user, string scope) =>
        user.FindFirst(ScopeClaim)?.Value.Split(' ').Contains(scope) == true;
}
