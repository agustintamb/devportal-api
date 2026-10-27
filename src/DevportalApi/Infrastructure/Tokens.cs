using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DevportalApi.Infrastructure;

// Holds the HS256 signing key (from JWT_SECRET) and issues the API's access tokens.
public sealed class Tokens
{
    public const string Issuer = "devportal-api";
    public const string Audience = "devportal-portal";
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    // HS256 needs a key of at least 256 bits.
    private const int MinSecretBytes = 32;

    public Tokens(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException(
                "JWT_SECRET is not configured. Locally: dotnet user-secrets set JWT_SECRET \"<at least 32 characters>\" --project src/DevportalApi. In Render: set the JWT_SECRET environment variable.");
        }

        var secretBytes = Encoding.UTF8.GetBytes(secret);
        if (secretBytes.Length < MinSecretBytes)
        {
            throw new InvalidOperationException($"JWT_SECRET must be at least {MinSecretBytes} bytes long for HS256 (got {secretBytes.Length}).");
        }

        SigningKey = new SymmetricSecurityKey(secretBytes);
    }

    public SymmetricSecurityKey SigningKey { get; }

    public string Create(string username) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, username)]),
            Expires = DateTime.UtcNow.Add(Lifetime),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        });
}
