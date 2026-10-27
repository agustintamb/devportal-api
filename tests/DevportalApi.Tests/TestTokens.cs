using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DevportalApi.Tests;

// Mints and validates JWTs independently of the API code, so tests check the real contract.
public static class TestTokens
{
    public const string Issuer = "devportal-api";
    public const string Audience = "devportal-portal";

    public static string Create(
        string secret = ApiFactory.JwtSecret,
        string audience = Audience,
        DateTime? expires = null)
    {
        var expiresAt = expires ?? DateTime.UtcNow.AddHours(1);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", "demo")]),
            IssuedAt = expiresAt.AddHours(-1),
            NotBefore = expiresAt.AddHours(-1),
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(Key(secret), SecurityAlgorithms.HmacSha256),
        });
    }

    public static Task<TokenValidationResult> ValidateAsync(string token) =>
        new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            IssuerSigningKey = Key(ApiFactory.JwtSecret),
            ClockSkew = TimeSpan.Zero,
        });

    private static SymmetricSecurityKey Key(string secret) => new(Encoding.UTF8.GetBytes(secret));
}
