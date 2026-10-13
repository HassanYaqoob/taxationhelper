namespace Kira.Application.Features.Accounting;

public sealed record AccountAmount(Guid Id, string Code, string Name, AccountType Type, TaxTreatment Tax, decimal Amount);
public sealed record PnlDto(DateOnly From, DateOnly To, List<AccountAmount> Income, List<AccountAmount> Expenses, decimal TotalIncome, decimal TotalExpenses, decimal NetProfit);
public sealed record BsRow(string Name, string? Code, decimal Amount);
public sealed record BalanceSheetDto(DateOnly AsAt, List<BsRow> Assets, List<BsRow> Liabilities, List<BsRow> Equity, decimal TotalAssets, decimal TotalLiabilities, decimal TotalEquity, bool Balanced);
public sealed record TbRow(string Code, string Name, decimal Debit, decimal Credit);
public sealed record TrialBalanceDto(DateOnly AsAt, List<TbRow> Rows, decimal TotalDebit, decimal TotalCredit);
public sealed record GlRow(Guid EntryId, int Number, DateOnly Date, string Memo, decimal Debit, decimal Credit, decimal Balance);
public sealed record GeneralLedgerDto(Guid AccountId, string Code, string Name, DateOnly From, DateOnly To, decimal Opening, List<GlRow> Rows, decimal Closing);
public sealed record MonthAmount(string Month, decimal Income, decimal Expenses);

/// <summary>Account totals for a period, built from journal lines.</summary>
public sealed class LedgerView(IReadOnlyList<Account> accounts, Dictionary<Guid, (decimal Dr, decimal Cr)> sums)
{
    public IReadOnlyList<Account> Accounts { get; } = accounts;
    public decimal Debit(Account a) => sums.TryGetValue(a.Id, out var s) ? s.Dr : 0;
    public decimal Credit(Account a) => sums.TryGetValue(a.Id, out var s) ? s.Cr : 0;
    public decimal Balance(Account a) => a.Natural(Debit(a), Credit(a));
    public IEnumerable<AccountBalance> Balances() => Accounts.Select(a => new AccountBalance(a, Debit(a), Credit(a)));
    public decimal Sum(Func<Account, bool> which) => Accounts.Where(which).Sum(Balance);

    public PnlDto Pnl(DateOnly from, DateOnly to)
    {
        List<AccountAmount> Rows(AccountType t) => Accounts.Where(a => a.Type == t)
            .Select(a => new AccountAmount(a.Id, a.Code, a.Name, a.Type, a.Tax, Balance(a))).Where(x => x.Amount != 0).ToList();
        var inc = Rows(AccountType.Income);
        var exp = Rows(AccountType.Expense).OrderByDescending(x => x.Amount).ToList();
        var ti = inc.Sum(x => x.Amount);
        var te = exp.Sum(x => x.Amount);
        return new PnlDto(from, to, inc, exp, ti, te, ti - te);
    }
}

public sealed class ReportService(IAppDbContext db, AccessService access, IClock clock)
{
    public Task<List<Account>> AccountsAsync(Guid companyId, CancellationToken ct) =>
        db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId).OrderBy(a => a.Code).ToListAsync(ct);

    /// <summary>Sums journal lines per account between two dates (from = null means since the start).</summary>
    public async Task<LedgerView> LedgerAsync(Guid companyId, DateOnly? from, DateOnly to, CancellationToken ct, List<Account>? accounts = null)
    {
        accounts ??= await AccountsAsync(companyId, ct);
        var query = from l in db.JournalLines
                    join e in db.JournalEntries on l.JournalEntryId equals e.Id
                    where e.CompanyId == companyId && e.Date <= to
                    select new { l.AccountId, l.Debit, l.Credit, e.Date };
        if (from is DateOnly f) query = query.Where(x => x.Date >= f);
        var sums = await query.GroupBy(x => x.AccountId)
            .Select(g => new { g.Key, Dr = g.Sum(x => x.Debit), Cr = g.Sum(x => x.Credit) })
            .ToDictionaryAsync(x => x.Key, x => (x.Dr, x.Cr), ct);
        return new LedgerView(accounts, sums);
    }

    public async Task<PnlDto> PnlAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await access.ViewAsync(companyId, ct);
        return (await LedgerAsync(companyId, from, to, ct)).Pnl(from, to);
    }

    public async Task<BalanceSheetDto> BalanceSheetAsync(Guid companyId, DateOnly asAt, CancellationToken ct)
    {
        await access.ViewAsync(companyId, ct);
        var c = await db.Companies.AsNoTracking().FirstAsync(x => x.Id == companyId, ct);
        var accounts = await AccountsAsync(companyId, ct);
        var all = await LedgerAsync(companyId, null, asAt, ct, accounts);
        var fyStart = c.FyStart(asAt);
        var current = (await LedgerAsync(companyId, fyStart, asAt, ct, accounts)).Pnl(fyStart, asAt).NetProfit;
        var total = all.Pnl(DateOnly.MinValue, asAt).NetProfit;

        List<BsRow> Section(AccountType t) => accounts.Where(a => a.Type == t)
            .Select(a => new BsRow(a.Name, a.Code, all.Balance(a))).Where(r => r.Amount != 0).ToList();
        var assets = Section(AccountType.Asset);
        var liabilities = Section(AccountType.Liability);
        var equity = Section(AccountType.Equity);
        if (total - current != 0) equity.Add(new BsRow("Profit from earlier years", null, total - current));
        equity.Add(new BsRow("Current year profit", null, current));
        var ta = assets.Sum(r => r.Amount);
        var tl = liabilities.Sum(r => r.Amount);
        var te = equity.Sum(r => r.Amount);
        return new BalanceSheetDto(asAt, assets, liabilities, equity, ta, tl, te, Math.Abs(ta - tl - te) < 0.01m);
    }

    public async Task<TrialBalanceDto> TrialBalanceAsync(Guid companyId, DateOnly asAt, CancellationToken ct)
    {
        await access.ViewAsync(companyId, ct);
        var ledger = await LedgerAsync(companyId, null, asAt, ct);
        var rows = new List<TbRow>();
        foreach (var a in ledger.Accounts)
        {
            var n = ledger.Debit(a) - ledger.Credit(a);
            if (n != 0) rows.Add(new TbRow(a.Code, a.Name, n > 0 ? n : 0, n < 0 ? -n : 0));
        }
        return new TrialBalanceDto(asAt, rows, rows.Sum(r => r.Debit), rows.Sum(r => r.Credit));
    }

    public async Task<GeneralLedgerDto> GeneralLedgerAsync(Guid companyId, Guid accountId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await access.ViewAsync(companyId, ct);
        var a = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == accountId && x.CompanyId == companyId, ct)
                ?? throw new NotFoundException("Account not found.");
        var before = db.JournalLines.Where(l => l.AccountId == accountId && db.JournalEntries.Any(e => e.Id == l.JournalEntryId && e.Date < from));
        var balance = a.Natural(await before.SumAsync(l => l.Debit, ct), await before.SumAsync(l => l.Credit, ct));
        var openingBalance = balance;
        var entries = await db.JournalEntries.AsNoTracking().Include(e => e.Lines)
            .Where(e => e.CompanyId == companyId && e.Date >= from && e.Date <= to && e.Lines.Any(l => l.AccountId == accountId))
            .OrderBy(e => e.Date).ThenBy(e => e.Number).ToListAsync(ct);
        var rows = new List<GlRow>();
        foreach (var e in entries)
        {
            var dr = e.Lines.Where(l => l.AccountId == accountId).Sum(l => l.Debit);
            var cr = e.Lines.Where(l => l.AccountId == accountId).Sum(l => l.Credit);
            balance += a.Natural(dr, cr);
            rows.Add(new GlRow(e.Id, e.Number, e.Date, e.Memo, dr, cr, balance));
        }
        return new GeneralLedgerDto(a.Id, a.Code, a.Name, from, to, openingBalance, rows, balance);
    }

    /// <summary>Income and expenses for each month of a financial year.</summary>
    public async Task<List<MonthAmount>> MonthlyAsync(Guid companyId, DateOnly fyStart, CancellationToken ct)
    {
        var fyEnd = Company.FyEnd(fyStart);
        var rows = await (from l in db.JournalLines
                          join e in db.JournalEntries on l.JournalEntryId equals e.Id
                          join a in db.Accounts on l.AccountId equals a.Id
                          where e.CompanyId == companyId && e.Date >= fyStart && e.Date <= fyEnd
                                && (a.Type == AccountType.Income || a.Type == AccountType.Expense)
                          group new { l.Debit, l.Credit } by new { e.Date.Year, e.Date.Month, a.Type } into g
                          select new { g.Key.Year, g.Key.Month, g.Key.Type, Dr = g.Sum(x => x.Debit), Cr = g.Sum(x => x.Credit) })
                         .ToListAsync(ct);
        var result = new List<MonthAmount>();
        for (var i = 0; i < 12; i++)
        {
            var m = fyStart.AddMonths(i);
            var inc = rows.Where(r => r.Year == m.Year && r.Month == m.Month && r.Type == AccountType.Income).Sum(r => r.Cr - r.Dr);
            var exp = rows.Where(r => r.Year == m.Year && r.Month == m.Month && r.Type == AccountType.Expense).Sum(r => r.Dr - r.Cr);
            result.Add(new MonthAmount(m.ToString("yyyy-MM"), inc, exp));
        }
        return result;
    }

    public DateOnly Today => clock.Today;
}
