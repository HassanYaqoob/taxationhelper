using System.Security.Claims;
using System.Text;
using Kira.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Kira.Api.Hosting;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "kira-api";
    public string Audience { get; set; } = "kira-web";
    /// <summary>HMAC-SHA256 signing key, at least 32 characters. Set it via user-secrets or an environment variable.</summary>
    public string Key { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;

    public SymmetricSecurityKey SigningKey() => new(Encoding.UTF8.GetBytes(Key));
}

/// <summary>Short-lived access tokens (JWT). Long-lived sessions use rotating refresh tokens in an HttpOnly cookie.</summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options, IClock clock) : ITokenService
{
    public (string Token, DateTime ExpiresAt) CreateAccessToken(User user)
    {
        var o = options.Value;
        var expires = clock.UtcNow.AddMinutes(o.AccessTokenMinutes);
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience,
            IssuedAt = clock.UtcNow,
            NotBefore = clock.UtcNow,
            Expires = expires,
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Name, user.Name),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
            }),
            SigningCredentials = new SigningCredentials(o.SigningKey(), SecurityAlgorithms.HmacSha256)
        });
        return (token, expires);
    }
}
