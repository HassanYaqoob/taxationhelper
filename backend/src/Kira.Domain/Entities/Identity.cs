using Kira.Domain.Common;

namespace Kira.Domain.Entities;

public class User : Entity
{
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    /// <summary>Whether this person uses the private personal workspace.</summary>
    public bool UsesPersonal { get; set; } = true;
    public int FailedLogins { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

/// <summary>Long-lived sign-in token. Only a SHA-256 hash is stored; each use rotates it.</summary>
public class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? ReplacedById { get; set; }
    public string CreatedByIp { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public bool IsActive(DateTime now) => RevokedAt == null && ExpiresAt > now;
}
