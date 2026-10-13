namespace Kira.Application.Features.Personal;

// Every personal query is filtered by the signed-in user's id. No company id is ever accepted here.

public sealed record PaymentAccountDto(Guid Id, string Name, PaymentKind Kind, string Institution, string Last4, decimal OpeningBalance, decimal Balance, int SortOrder);
public sealed record PaymentAccountInput(string Name, PaymentKind Kind, string? Institution, string? Last4, decimal OpeningBalance);

public sealed class PaymentAccountService(IAppDbContext db, ICurrentUser me)
{
    public async Task<List<PaymentAccountDto>> ListAsync(CancellationToken ct)
    {
        var accounts = await db.PaymentAccounts.AsNoTracking().Where(a => a.UserId == me.Id).OrderBy(a => a.SortOrder).ThenBy(a => a.Name).ToListAsync(ct);
        var movement = await db.PersonalTransactions.AsNoTracking().Where(t => t.UserId == me.Id && t.PaymentAccountId != null)
            .GroupBy(t => t.PaymentAccountId!.Value)
            .Select(g => new { g.Key, Net = g.Sum(t => t.Direction == Direction.In ? t.Amount : -t.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Net, ct);
        return accounts.Select(a => new PaymentAccountDto(a.Id, a.Name, a.Kind, a.Institution, a.Last4, a.OpeningBalance,
            a.OpeningBalance + movement.GetValueOrDefault(a.Id), a.SortOrder)).ToList();
    }

    public async Task<PaymentAccountDto> AddAsync(PaymentAccountInput i, CancellationToken ct)
    {
        Validate(i);
        var n = await db.PaymentAccounts.CountAsync(a => a.UserId == me.Id, ct);
        var a = new PaymentAccount
        {
            UserId = me.Id, Name = Json.Cut(i.Name.Trim(), 100), Kind = i.Kind, Institution = Json.Cut(i.Institution?.Trim(), 80),
            Last4 = PaymentMatcher.Digits(i.Last4), OpeningBalance = i.OpeningBalance, SortOrder = n + 1
        };
        db.PaymentAccounts.Add(a);
        await db.SaveChangesAsync(ct);
        return new PaymentAccountDto(a.Id, a.Name, a.Kind, a.Institution, a.Last4, a.OpeningBalance, a.OpeningBalance, a.SortOrder);
    }

    public async Task UpdateAsync(Guid id, PaymentAccountInput i, CancellationToken ct)
    {
        Validate(i);
        var a = await db.PaymentAccounts.FirstOrDefaultAsync(x => x.Id == id && x.UserId == me.Id, ct) ?? throw new NotFoundException();
        a.Name = Json.Cut(i.Name.Trim(), 100); a.Kind = i.Kind; a.Institution = Json.Cut(i.Institution?.Trim(), 80);
        a.Last4 = PaymentMatcher.Digits(i.Last4); a.OpeningBalance = i.OpeningBalance;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var a = await db.PaymentAccounts.FirstOrDefaultAsync(x => x.Id == id && x.UserId == me.Id, ct) ?? throw new NotFoundException();
        if (await db.PersonalTransactions.AnyAsync(t => t.UserId == me.Id && t.PaymentAccountId == id, ct))
            throw new DomainException("This account has transactions. Delete or move them first.");
        var receipts = await db.Receipts.Where(r => r.UserId == me.Id && r.PaymentAccountId == id).ToListAsync(ct);
        foreach (var r in receipts) { r.PaymentAccountId = null; r.PaymentStatus = PaymentStatus.Unknown; r.PaymentNote = "The account was deleted."; }
        db.PaymentAccounts.Remove(a);
        await db.SaveChangesAsync(ct);
    }

    static void Validate(PaymentAccountInput i)
    {
        if (string.IsNullOrWhiteSpace(i.Name)) throw new DomainException("Give the account a name you'll recognise.");
        if (i.Name.Length > 100) throw new DomainException("That name is too long.");
    }
}
