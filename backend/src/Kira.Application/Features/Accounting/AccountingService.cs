namespace Kira.Application.Features.Accounting;

/// <summary>Posts balanced, immutable journal entries. The only way to correct one is a reversal.</summary>
public sealed class AccountingService(IAppDbContext db, IClock clock)
{
    public async Task<JournalEntry> PostAsync(Guid companyId, DateOnly date, string memo, string source, IEnumerable<LineInput> lines, Guid userId, CancellationToken ct = default)
    {
        var list = lines.ToList();
        var ids = list.Select(l => l.AccountId).Distinct().ToList();
        var found = await db.Accounts.CountAsync(a => a.CompanyId == companyId && ids.Contains(a.Id), ct);
        if (found != ids.Count) throw new DomainException("One of the accounts doesn't belong to this company.");
        var entry = JournalEntry.Create(companyId, await NextNumberAsync(companyId, ct), date, memo, source, userId, list);
        db.JournalEntries.Add(entry);
        return entry;
    }

    public async Task<JournalEntry> ReverseAsync(Guid companyId, Guid entryId, Guid userId, CancellationToken ct = default)
    {
        var e = await db.JournalEntries.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == entryId && x.CompanyId == companyId, ct)
                ?? throw new NotFoundException("Journal entry not found.");
        var r = e.CreateReversal(await NextNumberAsync(companyId, ct), clock.Today, userId);
        db.JournalEntries.Add(r);
        return r;
    }

    /// <summary>Next entry number, counting entries added earlier in this unit of work.</summary>
    async Task<int> NextNumberAsync(Guid companyId, CancellationToken ct)
    {
        var dbMax = await db.JournalEntries.Where(e => e.CompanyId == companyId).MaxAsync(e => (int?)e.Number, ct) ?? 0;
        var localMax = db.JournalEntries.Local.Where(e => e.CompanyId == companyId).Select(e => e.Number).DefaultIfEmpty(0).Max();
        return Math.Max(dbMax, localMax) + 1;
    }
}
