using Microsoft.IdentityModel.JsonWebTokens;

namespace Kira.Api.Hosting;

/// <summary>Reads the signed-in user from the validated access token.</summary>
public sealed class CurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    public Guid Id => Guid.TryParse(http.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        ? id : throw new UnauthorizedException();

    public string IpAddress => http.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "";
    public string UserAgent => http.HttpContext?.Request.Headers.UserAgent.ToString() ?? "";
}
