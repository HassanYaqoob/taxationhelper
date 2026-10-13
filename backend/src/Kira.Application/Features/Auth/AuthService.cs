using System.Security.Cryptography;
using Kira.Application.Features.Companies;
using Microsoft.Extensions.Options;

namespace Kira.Application.Features.Auth;

public sealed record WorkspaceDto(string Kind, Guid Id, string Name, string Role);
public sealed record MeDto(Guid Id, string Name, string Email, string Title, bool UsesPersonal, IReadOnlyList<WorkspaceDto> Workspaces, bool AiEnabled);
public sealed record AuthResponse(string AccessToken, DateTime ExpiresAt, MeDto Me);
/// <summary>An issued session: the response body plus the raw refresh token, which the API puts in an HttpOnly cookie.</summary>
public sealed record Session(AuthResponse Response, string RefreshToken, DateTime RefreshExpiresAt);
public sealed record LoginRequest(string Email, string Password);
public sealed record RegisterRequest(string Name, string Email, string Password, bool Personal, CompanySetup? Company);
public sealed record ChangePasswordRequest(string Current, string New);

public sealed class AuthOptions
{
    public const string Section = "Auth";
    public int RefreshTokenDays { get; set; } = 14;
    public int MaxFailedLogins { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int MinPasswordLength { get; set; } = 10;
}

public sealed class AuthService(IAppDbContext db, ITokenService tokens, IPasswordHasher hasher, ICurrentUser current, IClock clock,
    IAiClient ai, CompanyService companies, IOptions<AuthOptions> options)
{
    AuthOptions O => options.Value;

    public async Task<Session> LoginAsync(LoginRequest req, CancellationToken ct)
    {
        var email = Normalise(req.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        // Same message for unknown email and wrong password, so accounts can't be discovered.
        const string bad = "Email or password is incorrect.";
        if (user == null) { hasher.Hash(req.Password ?? ""); throw new DomainException(bad); }
        if (user.LockedUntil > clock.UtcNow)
            throw new DomainException("Too many failed attempts. Try again in a few minutes.");
        if (!hasher.Verify(user.PasswordHash, req.Password ?? ""))
        {
            user.FailedLogins++;
            if (user.FailedLogins >= O.MaxFailedLogins) { user.LockedUntil = clock.UtcNow.AddMinutes(O.LockoutMinutes); user.FailedLogins = 0; }
            await db.SaveChangesAsync(ct);
            throw new DomainException(bad);
        }
        user.FailedLogins = 0; user.LockedUntil = null; user.LastLoginAt = clock.UtcNow;
        return await IssueAsync(user, ct);
    }

    public async Task<Session> RegisterAsync(RegisterRequest req, CancellationToken ct)
    {
        var email = Normalise(req.Email);
        if (string.IsNullOrWhiteSpace(req.Name)) throw new DomainException("Enter your name.");
        if (!email.Contains('@') || email.Length > 254) throw new DomainException("Enter a valid email address.");
        ValidatePassword(req.Password);
        if (!req.Personal && req.Company == null) throw new DomainException("Choose personal, company, or both.");
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) throw new DomainException("An account with this email already exists. Sign in instead.");

        var user = new User { Email = email, Name = req.Name.Trim(), PasswordHash = hasher.Hash(req.Password), UsesPersonal = req.Personal };
        db.Users.Add(user);
        if (req.Personal) AddCashAccount(user.Id);
        if (req.Company != null)
        {
            var c = companies.Create(user.Id, req.Company);
            user.Title = $"Owner, {c.Name}";
        }
        return await IssueAsync(user, ct);
    }

    /// <summary>Exchanges a refresh token for a new access token. The refresh token is rotated on every use.</summary>
    public async Task<Session> RefreshAsync(string? rawToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(rawToken)) throw new UnauthorizedException();
        var hash = Hash(rawToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct) ?? throw new UnauthorizedException();
        if (token.RevokedAt != null)
        {
            // A rotated token was used again: treat as theft and sign the user out everywhere.
            await RevokeAllAsync(token.UserId, ct);
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedException();
        }
        if (!token.IsActive(clock.UtcNow)) throw new UnauthorizedException();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == token.UserId, ct) ?? throw new UnauthorizedException();
        var session = await IssueAsync(user, ct, token);
        return session;
    }

    public async Task LogoutAsync(string? rawToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(rawToken)) return;
        var hash = Hash(rawToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is { RevokedAt: null }) { token.RevokedAt = clock.UtcNow; await db.SaveChangesAsync(ct); }
    }

    public async Task<MeDto> MeAsync(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == current.Id, ct) ?? throw new UnauthorizedException();
        return await MeForAsync(user, ct);
    }

    public async Task<MeDto> EnablePersonalAsync(CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == current.Id, ct) ?? throw new UnauthorizedException();
        if (!user.UsesPersonal)
        {
            user.UsesPersonal = true;
            if (!await db.PaymentAccounts.AnyAsync(a => a.UserId == user.Id && a.Kind == PaymentKind.Cash, ct)) AddCashAccount(user.Id);
            await db.SaveChangesAsync(ct);
        }
        return await MeForAsync(user, ct);
    }

    /// <summary>Changes the password, signs out every other device, and returns a fresh session for this one.</summary>
    public async Task<Session> ChangePasswordAsync(ChangePasswordRequest req, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == current.Id, ct) ?? throw new UnauthorizedException();
        if (!hasher.Verify(user.PasswordHash, req.Current ?? "")) throw new DomainException("Your current password is incorrect.");
        ValidatePassword(req.New);
        user.PasswordHash = hasher.Hash(req.New);
        await RevokeAllAsync(user.Id, ct);
        return await IssueAsync(user, ct);
    }

    public void ValidatePassword(string? password)
    {
        if ((password ?? "").Length < O.MinPasswordLength)
            throw new DomainException($"Use a password of at least {O.MinPasswordLength} characters.");
    }

    async Task<Session> IssueAsync(User user, CancellationToken ct, RefreshToken? replacing = null)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var refresh = new RefreshToken
        {
            UserId = user.Id, TokenHash = Hash(raw), ExpiresAt = clock.UtcNow.AddDays(O.RefreshTokenDays),
            CreatedByIp = Json.Cut(current.IpAddress, 64), UserAgent = Json.Cut(current.UserAgent, 300)
        };
        db.RefreshTokens.Add(refresh);
        if (replacing != null) { replacing.RevokedAt = clock.UtcNow; replacing.ReplacedById = refresh.Id; }
        await db.SaveChangesAsync(ct);
        var (access, expires) = tokens.CreateAccessToken(user);
        return new Session(new AuthResponse(access, expires, await MeForAsync(user, ct)), raw, refresh.ExpiresAt);
    }

    async Task RevokeAllAsync(Guid userId, CancellationToken ct)
    {
        var active = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in active) t.RevokedAt = clock.UtcNow;
    }

    async Task<MeDto> MeForAsync(User u, CancellationToken ct)
    {
        var ws = new List<WorkspaceDto>();
        if (u.UsesPersonal) ws.Add(new WorkspaceDto("personal", u.Id, u.Name, "Owner"));
        var members = await db.Memberships.AsNoTracking().Include(m => m.Company).Where(m => m.UserId == u.Id).ToListAsync(ct);
        ws.AddRange(members.OrderBy(m => m.Company!.Name).Select(m => new WorkspaceDto("company", m.CompanyId, m.Company!.Name, m.Role.ToString())));
        return new MeDto(u.Id, u.Name, u.Email, u.Title, u.UsesPersonal, ws, ai.Enabled);
    }

    void AddCashAccount(Guid userId) =>
        db.PaymentAccounts.Add(new PaymentAccount { UserId = userId, Name = "Cash", Kind = PaymentKind.Cash, SortOrder = 99 });

    static string Normalise(string? email) => (email ?? "").Trim().ToLowerInvariant();
    static string Hash(string raw) => FileRules.Sha256(System.Text.Encoding.UTF8.GetBytes(raw));
}
