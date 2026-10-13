using Kira.Application.Common;
using Microsoft.AspNetCore.Identity;

namespace Kira.Infrastructure.Security;

/// <summary>ASP.NET Core Identity's hasher: PBKDF2 with HMAC-SHA512, salted, 100,000 iterations (V3 format).</summary>
public sealed class PasswordHasher : IPasswordHasher
{
    static readonly PasswordHasher<object> Inner = new();
    static readonly object Subject = new();

    public string Hash(string password) => Inner.HashPassword(Subject, password);

    public bool Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash)) return false;
        try { return Inner.VerifyHashedPassword(Subject, hash, password) != PasswordVerificationResult.Failed; }
        catch (FormatException) { return false; }
    }
}
