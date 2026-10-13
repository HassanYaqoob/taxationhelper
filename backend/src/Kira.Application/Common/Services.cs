namespace Kira.Application.Common;

/// <summary>
/// Tenant isolation. Every company operation calls ViewAsync or EditAsync first. Personal operations never
/// take a company id and always filter by the caller's own user id, so company members can't reach personal data.
/// </summary>
public sealed class AccessService(IAppDbContext db, ICurrentUser me)
{
    public async Task<Membership> ViewAsync(Guid companyId, CancellationToken ct = default) =>
        await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.CompanyId == companyId && m.UserId == me.Id, ct)
        ?? throw new ForbiddenException("You don't have access to this company.");

    public async Task<Membership> EditAsync(Guid companyId, CancellationToken ct = default)
    {
        var m = await ViewAsync(companyId, ct);
        if (m.Role != MemberRole.Owner) throw new ForbiddenException("Your accountant access is read-only.");
        return m;
    }
}

public sealed class AuditService(IAppDbContext db, ICurrentUser me)
{
    /// <summary>Adds an audit record. It is saved together with the caller's next SaveChanges.</summary>
    public void Log(Guid companyId, string action, string detail) =>
        db.AuditLogs.Add(new AuditLog { CompanyId = companyId, UserId = me.Id, Action = action, Detail = detail.Length > 500 ? detail[..500] : detail });
}

/// <summary>Learned categorisation rules ("tnb" → Utilities) for a company or a person.</summary>
public sealed class RuleService(IAppDbContext db)
{
    public async Task<Dictionary<string, string>> GetAsync(Guid? companyId, Guid? userId, CancellationToken ct = default)
    {
        var list = await db.CategoryRules.AsNoTracking().Where(r => r.CompanyId == companyId && r.UserId == userId).ToListAsync(ct);
        return list.GroupBy(r => r.Pattern).ToDictionary(g => g.Key, g => g.OrderBy(r => r.CreatedAt).Last().Value);
    }

    /// <summary>Remembers a choice. Returns the merchant key, or "" if the description had none.</summary>
    public async Task<string> LearnAsync(Guid? companyId, Guid? userId, string description, string value, CancellationToken ct = default)
    {
        var key = MerchantKey.From(description);
        if (key.Length == 0) return "";
        var rule = await db.CategoryRules.FirstOrDefaultAsync(r => r.CompanyId == companyId && r.UserId == userId && r.Pattern == key, ct);
        if (rule == null) db.CategoryRules.Add(new CategoryRule { CompanyId = companyId, UserId = userId, Pattern = key, Value = value });
        else rule.Value = value;
        return key;
    }
}

/// <summary>Saves uploaded bytes as a StoredFile owned by a company or a user.</summary>
public sealed class FileStore(IAppDbContext db)
{
    public StoredFile Add(FileUpload f, string contentType, Guid? companyId, Guid? userId)
    {
        var file = new StoredFile
        {
            FileName = FileRules.SafeName(f.FileName), ContentType = contentType, Size = f.Data.LongLength,
            Sha256 = FileRules.Sha256(f.Data), Data = f.Data, CompanyId = companyId, UserId = userId
        };
        db.StoredFiles.Add(file);
        return file;
    }
}
