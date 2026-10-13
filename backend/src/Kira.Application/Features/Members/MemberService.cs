using Kira.Application.Features.Auth;

namespace Kira.Application.Features.Members;

public sealed record MemberDto(Guid UserId, string Name, string Email, string Title, MemberRole Role, DateTime Since);
public sealed record AddAccountantRequest(string? Name, string Email, string? Password);
public sealed record AddAccountantResult(bool Created);

/// <summary>Who can see a company. Owners can give a tax agent read-only access.</summary>
public sealed class MemberService(IAppDbContext db, AccessService access, AuditService audit, ICurrentUser me, IPasswordHasher hasher, AuthService auth)
{
    public async Task<List<MemberDto>> ListAsync(Guid companyId, CancellationToken ct)
    {
        await access.ViewAsync(companyId, ct);
        return await db.Memberships.AsNoTracking().Where(m => m.CompanyId == companyId)
            .OrderByDescending(m => m.Role).ThenBy(m => m.User!.Name)
            .Select(m => new MemberDto(m.UserId, m.User!.Name, m.User.Email, m.User.Title, m.Role, m.CreatedAt)).ToListAsync(ct);
    }

    /// <summary>Gives a tax agent read-only access, creating their login if they don't have one yet.</summary>
    public async Task<AddAccountantResult> AddAccountantAsync(Guid companyId, AddAccountantRequest req, CancellationToken ct)
    {
        await access.EditAsync(companyId, ct);
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        if (!email.Contains('@')) throw new DomainException("Enter the accountant's email.");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        var created = false;
        if (user == null)
        {
            if (string.IsNullOrEmpty(req.Password)) throw new DomainException("They don't have a Kira login yet. Set a temporary password for them.");
            auth.ValidatePassword(req.Password);
            user = new User { Email = email, Name = string.IsNullOrWhiteSpace(req.Name) ? email : req.Name.Trim(), Title = "Tax agent", UsesPersonal = false, PasswordHash = hasher.Hash(req.Password) };
            db.Users.Add(user);
            created = true;
        }
        if (await db.Memberships.AnyAsync(m => m.CompanyId == companyId && m.UserId == user.Id, ct)) throw new DomainException("This person already has access.");
        db.Memberships.Add(new Membership { CompanyId = companyId, UserId = user.Id, Role = MemberRole.Accountant });
        audit.Log(companyId, "Gave accountant access", email);
        await db.SaveChangesAsync(ct);
        return new AddAccountantResult(created);
    }

    public async Task RemoveAsync(Guid companyId, Guid userId, CancellationToken ct)
    {
        await access.EditAsync(companyId, ct);
        if (userId == me.Id) throw new DomainException("You can't remove yourself.");
        var m = await db.Memberships.Include(x => x.User).FirstOrDefaultAsync(x => x.CompanyId == companyId && x.UserId == userId, ct) ?? throw new NotFoundException();
        if (m.Role == MemberRole.Owner) throw new DomainException("Owners can't be removed here.");
        db.Memberships.Remove(m);
        audit.Log(companyId, "Removed access", m.User?.Email ?? "");
        await db.SaveChangesAsync(ct);
    }
}
