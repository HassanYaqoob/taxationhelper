using Kira.Application.Features.Auth;
using Kira.Application.Features.Companies;

namespace Kira.Api.Controllers;

/// <summary>Sign-in uses a short-lived access token in the response body and a rotating refresh token in an HttpOnly cookie.</summary>
[ApiController, Route("api/v1/auth")]
public sealed class AuthController(AuthService auth, IConfiguration config) : ControllerBase
{
    bool SecureCookie => config.GetValue("Auth:SecureCookies", true);

    [HttpPost("login"), AllowAnonymous, EnableRateLimiting(RatePolicies.Auth)]
    public async Task<AuthResponse> Login(LoginRequest req, CancellationToken ct) => Issue(await auth.LoginAsync(req, ct));

    [HttpPost("register"), AllowAnonymous, EnableRateLimiting(RatePolicies.Auth)]
    public async Task<AuthResponse> Register(RegisterRequest req, CancellationToken ct) => Issue(await auth.RegisterAsync(req, ct));

    [HttpPost("refresh"), AllowAnonymous, EnableRateLimiting(RatePolicies.Auth)]
    public async Task<AuthResponse> Refresh(CancellationToken ct)
    {
        try { return Issue(await auth.RefreshAsync(Request.Cookies[Http.RefreshCookie], ct)); }
        catch (UnauthorizedException) { Response.ClearRefreshCookie(SecureCookie); throw; }
    }

    [HttpPost("logout"), AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(Request.Cookies[Http.RefreshCookie], ct);
        Response.ClearRefreshCookie(SecureCookie);
        return NoContent();
    }

    AuthResponse Issue(Session s)
    {
        Response.SetRefreshCookie(s, SecureCookie);
        return s.Response;
    }
}

[ApiController, Route("api/v1/me")]
public sealed class MeController(AuthService auth, CompanyService companies, IConfiguration config) : ControllerBase
{
    [HttpGet]
    public Task<MeDto> Get(CancellationToken ct) => auth.MeAsync(ct);

    [HttpPost("personal")]
    public Task<MeDto> EnablePersonal(CancellationToken ct) => auth.EnablePersonalAsync(ct);

    /// <summary>Changes the password, signs out other devices and returns a new session for this one.</summary>
    [HttpPost("password"), EnableRateLimiting(RatePolicies.Auth)]
    public async Task<AuthResponse> ChangePassword(ChangePasswordRequest req, CancellationToken ct)
    {
        var session = await auth.ChangePasswordAsync(req, ct);
        Response.SetRefreshCookie(session, config.GetValue("Auth:SecureCookies", true));
        return session.Response;
    }

    [HttpPost("companies")]
    public Task<WorkspaceDto> CreateCompany(CompanySetup setup, CancellationToken ct) => companies.CreateForMeAsync(setup, ct);
}
