using Kira.Application.Ai;
using Kira.Application.Features.Accounting;
using Kira.Application.Features.Companies;

namespace Kira.Application.Features.Banking;

public sealed record BankAccountSummary(Guid Id, string Code, string Name, string Last4, decimal Balance, int PendingCount, DateOnly? LastLineDate);
public sealed record BankLineDto(Guid Id, Guid BankAccountId, string BankName, DateOnly Date, string Description, Direction Direction, decimal Amount,
    Guid? AccountId, string? AccountName, decimal Confidence, string Reason, IReadOnlyList<string> Flags, BankLineStatus Status, Guid? JournalEntryId,
    int Documents, string SourceFile);
public sealed record BankingOverview(List<BankAccountSummary> Banks, int Pending, int PendingUnsure, int PendingCash, int PendingForeign, bool AiEnabled);
public sealed record ImportResult(int Added, int Duplicates, bool UsedAi, string Message);
public sealed record SetAccountRequest(Guid AccountId, bool Remember = true);
public sealed record SetAccountResult(int Similar, string Pattern);
public sealed record CashRequest(string Choice);
public sealed record RuleDto(Guid Id, string Pattern, string Value, string Label);
public sealed record CountResult(int Count);
public sealed record EntryNumberResult(Guid Id, int Number);
public sealed record RecheckSummary(int Rechecked, int Improved);

public sealed class BankingService(IAppDbContext db, AccessService access, AccountingService accounting, ReportService reports,
    Extractors ai, IAiClient aiClient, IStatementReader reader, AuditService audit, ICurrentUser me, RuleService rules, IClock clock)
{
    const string CompanyFlags = "\"cash_withdrawal\" (ATM or over-the-counter cash taken out, cash cheque) and \"foreign_payee\" (payee based outside Malaysia, e.g. Meta Platforms Ireland, Google Asia Pacific, Amazon Web Services, or a foreign-currency charge)";

    public async Task<BankingOverview> OverviewAsync(Guid companyId, CancellationToken ct)
    {
        await access.ViewAsync(companyId, ct);
        var banks = await BankSummariesAsync(companyId, ct);
        var pending = await db.BankLines.AsNoTracking().Where(l => l.CompanyId == companyId && l.Status == BankLineStatus.Pending).ToListAsync(ct);
        return new BankingOverview(banks, pending.Count, pending.Count(l => l.IsUnsure), pending.Count(l => l.HasFlag(LineFlags.CashWithdrawal)),
            pending.Count(l => l.HasFlag(LineFlags.ForeignPayee)), aiClient.Enabled);
    }

    public async Task<List<BankAccountSummary>> BankSummariesAsync(Guid companyId, CancellationToken ct)
    {
        var ledger = await reports.LedgerAsync(companyId, null, clock.Today, ct);
        var stats = await db.BankLines.AsNoTracking().Where(l => l.CompanyId == companyId && l.Status != BankLineStatus.Ignored)
            .GroupBy(l => l.BankAccountId)
            .Select(g => new { g.Key, Pending = g.Count(l => l.Status == BankLineStatus.Pending), Last = g.Max(l => (DateOnly?)l.Date) })
            .ToListAsync(ct);
        return ledger.Accounts.Where(a => a.IsBank).Select(a =>
        {
            var s = stats.FirstOrDefault(x => x.Key == a.Id);
            return new BankAccountSummary(a.Id, a.Code, a.Name, a.Last4, ledger.Balance(a), s?.Pending ?? 0, s?.Last);
        }).ToList();
    }

    public async Task<List<BankLineDto>> LinesAsync(Guid companyId, BankLineStatus status, Guid? bankAccountId, string? month, string? q, CancellationToken ct)
    {
        await access.ViewAsync(companyId, ct);
        var query = db.BankLines.AsNoTracking().Where(l => l.CompanyId == companyId && l.Status == status);
        if (bankAccountId != null) query = query.Where(l => l.BankAccountId == bankAccountId);
        if (month != null && DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out var m0))
        {
            var m1 = m0.AddMonths(1);
            query = query.Where(l => l.Date >= m0 && l.Date < m1);
        }
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(l => l.Description.Contains(q.Trim()));
        var list = await query.OrderBy(l => l.Date).ThenBy(l => l.Description).Take(1000).ToListAsync(ct);
        return await ToDtosAsync(companyId, list, ct);
    }

    async Task<List<BankLineDto>> ToDtosAsync(Guid companyId, List<BankLine> list, CancellationToken ct)
    {
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId).ToDictionaryAsync(a => a.Id, ct);
        var ids = list.Select(l => l.Id).ToList();
        var docs = await db.Documents.AsNoTracking().Where(d => d.BankLineId != null && ids.Contains(d.BankLineId.Value))
            .GroupBy(d => d.BankLineId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return list.Select(l => new BankLineDto(l.Id, l.BankAccountId, accounts.TryGetValue(l.BankAccountId, out var b) ? b.Name : "", l.Date, l.Description,
            l.Direction, l.Amount, l.SuggestedAccountId,
            l.SuggestedAccountId is Guid g && accounts.TryGetValue(g, out var a) ? $"{a.Code} {a.Name}" : null,
            l.Confidence, l.Reason, l.FlagList, l.Status, l.JournalEntryId, docs.GetValueOrDefault(l.Id), l.SourceFile)).ToList();
    }

    static string Key(DateOnly d, decimal a, Direction dir, string desc)
    {
        var low = desc.ToLowerInvariant();
        return $"{d:yyyy-MM-dd}|{a:0.00}|{dir}|{(low.Length > 40 ? low[..40] : low)}";
    }

    /// <summary>
    /// Reads a statement with AI (or a plain CSV without AI), suggests a category for each line, and stores the lines
    /// for review. Lines already imported are skipped; genuine same-day repeats are kept.
    /// </summary>
    public async Task<ImportResult> ImportAsync(Guid companyId, Guid bankAccountId, FileUpload file, CancellationToken ct)
    {
        await access.EditAsync(companyId, ct);
        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId, ct);
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId).ToListAsync(ct);
        var bank = accounts.FirstOrDefault(a => a.Id == bankAccountId && a.IsBank) ?? throw new DomainException("Choose one of the company's bank accounts.");
        var ruleMap = await rules.GetAsync(companyId, null, ct);
        var input = reader.Read(file);

        List<ExtractedLine> found;
        bool usedAi;
        if (aiClient.Enabled)
        {
            var owner = $"{company.Name}, a {(string.IsNullOrWhiteSpace(company.Industry) ? "small" : company.Industry)} company in Malaysia (account: {bank.Name})";
            found = await ai.StatementAsync(input, owner, CompanyService.CategoryList(accounts, bank.Id), ruleMap, CompanyFlags, "", ct);
            usedAi = true;
        }
        else
        {
            if (input.Text == null) throw new AiUnavailableException("Reading PDFs and images needs AI. Add the Anthropic API key, or upload a CSV export.");
            found = StatementCsv.Parse(input.Text)
                .Select(r => new ExtractedLine(r.Date, r.Description, r.Direction, r.Amount, r.Balance, "", 0, "Not categorised (AI is off)", [])).ToList();
            usedAi = false;
        }

        var existing = await db.BankLines.Where(l => l.CompanyId == companyId && l.BankAccountId == bank.Id)
            .Select(l => new { l.Date, l.Amount, l.Direction, l.Description }).ToListAsync(ct);
        var have = existing.GroupBy(x => Key(x.Date, x.Amount, x.Direction, x.Description)).ToDictionary(g => g.Key, g => g.Count());
        var seen = new Dictionary<string, int>();
        var batch = Guid.NewGuid().ToString("N")[..8];
        var added = 0;
        foreach (var t in found)
        {
            var k = Key(t.Date, t.Amount, t.Direction, t.Description);
            seen[k] = seen.GetValueOrDefault(k) + 1;
            if (seen[k] <= have.GetValueOrDefault(k)) continue;

            var code = t.Category; var conf = t.Confidence; var reason = t.Reason;
            var mk = MerchantKey.From(t.Description);
            if (mk.Length > 0 && ruleMap.TryGetValue(mk, out var ruled)) { code = ruled; conf = 1; reason = "Matched your rule"; }
            var target = accounts.FirstOrDefault(a => a.Code == code && a.Id != bank.Id);
            if (target == null) conf = Math.Min(conf, 0.4m);
            var flags = t.Flags.Where(f => f is LineFlags.CashWithdrawal or LineFlags.ForeignPayee).Distinct().ToList();
            if (flags.Contains(LineFlags.CashWithdrawal)) { target = null; reason = "Cash left the bank. Petty cash or director drawings?"; }
            db.BankLines.Add(new BankLine
            {
                CompanyId = companyId, BankAccountId = bank.Id, Date = t.Date, Description = t.Description, Direction = t.Direction, Amount = t.Amount,
                Balance = t.Balance, SuggestedAccountId = target?.Id, Confidence = conf, Reason = reason, Flags = string.Join(',', flags),
                ImportBatch = batch, SourceFile = Json.Cut(FileRules.SafeName(file.FileName), 200)
            });
            added++;
        }
        audit.Log(companyId, "Imported bank statement", $"{bank.Name}: {file.FileName}, {added} lines");
        await db.SaveChangesAsync(ct);
        var dup = found.Count - added;
        var msg = found.Count == 0 ? "No transactions were found in this file. Check it's a bank statement."
            : $"{added} lines added{(dup > 0 ? $", {dup} already imported" : "")}.";
        return new ImportResult(added, dup, usedAi, msg);
    }

    public async Task<SetAccountResult> SetAccountAsync(Guid companyId, Guid lineId, SetAccountRequest req, CancellationToken ct)
    {
        await access.EditAsync(companyId, ct);
        var line = await PendingAsync(companyId, lineId, ct);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == req.AccountId && a.CompanyId == companyId, ct) ?? throw new DomainException("Unknown account.");
        if (account.Id == line.BankAccountId) throw new DomainException("A bank line can't be posted to its own bank account.");
        line.SuggestedAccountId = account.Id; line.Confidence = 1; line.Reason = "Chosen by you";
        var key = req.Remember ? await rules.LearnAsync(companyId, null, line.Description, account.Code, ct) : "";
        var similar = key.Length == 0 ? 0 : (await SimilarAsync(companyId, line, ct)).Count(l => l.SuggestedAccountId != account.Id);
        await db.SaveChangesAsync(ct);
        return new SetAccountResult(similar, key);
    }

    public async Task<CountResult> ApplySimilarAsync(Guid companyId, Guid lineId, CancellationToken ct)
    {
        await access.EditAsync(companyId, ct);
        var line = await db.BankLines.FirstOrDefaultAsync(l => l.Id == lineId && l.CompanyId == companyId, ct) ?? throw new NotFoundException();
        var n = 0;
        foreach (var o in await SimilarAsync(companyId, line, ct))
        {
            o.SuggestedAccountId = line.SuggestedAccountId; o.Confidence = 1; o.Reason = "Matched your rule"; n++;
        }
        await db.SaveChangesAsync(ct);
        return new CountResult(n);
    }

    async Task<List<BankLine>> SimilarAsync(Guid companyId, BankLine line, CancellationToken ct)
    {
        var key = MerchantKey.From(line.Description);
        if (key.Length == 0) return [];
        var others = await db.BankLines.Where(l => l.CompanyId == companyId && l.Status == BankLineStatus.Pending && l.Id != line.Id && l.Direction == line.Direction).ToListAsync(ct);
        return others.Where(l => MerchantKey.From(l.Description) == key && !l.HasFlag(LineFlags.CashWithdrawal)).ToList();
    }

    public async Task<EntryNumberResult> AcceptAsync(Guid companyId, Guid lineId, CancellationToken ct)
    {
        await access.EditAsync(companyId, ct);
        var line = await PendingAsync(companyId, lineId, ct);
        var e = await PostLineAsync(companyId, line, ct);
        audit.Log(companyId, "Posted bank line", $"{line.Description}, RM {line.Amount:N2} (entry #{e.Number})");
        await db.SaveChangesAsync(ct);
        return new EntryNumberResult(e.Id, e.Number);
    }

    /// <summary>Cash withdrawals: petty cash for business spending, or money the director took personally.</summary>
    public async Task<EntryNumberResult> ExplainCashAsync(Guid companyId, Guid lineId, CashRequest req, CancellationToken ct)
    {
        await access.EditAsync(companyId, ct);
        var line = await PendingAsync(companyId, lineId, ct);
        var director = req.Choice == "director";
        var key = director ? SystemAccounts.DueFromDirector : SystemAccounts.PettyCash;
        var acc = await db.Accounts.FirstOrDefaultAsync(a => a.CompanyId == companyId && a.SystemKey == key, ct)
                  ?? throw new DomainException("The petty cash or director account is missing from the chart of accounts.");
        line.SuggestedAccountId = acc.Id; line.Confidence = 1; line.Reason = director ? "Director drawings" : "Petty cash for business";
        var e = await PostLineAsync(companyId, line, ct);
        audit.Log(companyId, "Explained cash withdrawal", $"RM {line.Amount:N2} as {acc.Name} (entry #{e.Number})");
        await db.SaveChangesAsync(ct);
        return new EntryNumberResult(e.Id, e.Number);
    }

    public async Task IgnoreAsync(Guid companyId, Guid lineId, CancellationToken ct)
    {
        await access.EditAsync(companyId, ct);
        var line = await PendingAsync(companyId, lineId, ct);
        line.Status = BankLineStatus.Ignored;
        audit.Log(companyId, "Ignored bank line", $"{line.Description}, RM {line.Amount:N2}");
        await db.SaveChangesAsync(ct);
    }

    public async Task<CountResult> AcceptConfidentAsync(Guid companyId, decimal min, CancellationToken ct)
    {
        await access.EditAsync(companyId, ct);
        var lines = await db.BankLines.Where(l => l.CompanyId == companyId && l.Status == BankLineStatus.Pending && l.SuggestedAccountId != null && l.Confidence >= min)
            .OrderBy(l => l.Date).ToListAsync(ct);
        lines = lines.Where(l => !l.HasFlag(LineFlags.CashWithdrawal)).ToList();
        foreach (var l in lines) await PostLineAsync(companyId, l, ct);
        if (lines.Count > 0) audit.Log(companyId, "Posted confident bank lines", $"{lines.Count} lines at {min:P0} or more");
        await db.SaveChangesAsync(ct);
        return new CountResult(lines.Count);
    }

    /// <summary>Asks the AI to look again at the unsure lines, with the full chart of accounts.</summary>
    public async Task<RecheckSummary> RecheckAsync(Guid companyId, CancellationToken ct)
    {
        await access.EditAsync(companyId, ct);
        var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId, ct);
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId).ToListAsync(ct);
        var unsure = (await db.BankLines.Where(l => l.CompanyId == companyId && l.Status == BankLineStatus.Pending).OrderBy(l => l.Date).ToListAsync(ct))
            .Where(l => (l.SuggestedAccountId == null || l.Confidence < 0.7m) && !l.HasFlag(LineFlags.CashWithdrawal)).Take(100).ToList();
        if (unsure.Count == 0) return new RecheckSummary(0, 0);
        var results = await ai.RecheckAsync($"{company.Name} ({company.Industry})", CompanyService.CategoryList(accounts.Where(a => !a.IsBank), null), unsure, ct);
        var improved = 0;
        foreach (var r in results)
        {
            var l = unsure.FirstOrDefault(x => x.Id == r.Id);
            var a = accounts.FirstOrDefault(x => x.Code == r.Category && !x.IsBank);
            if (l == null || a == null) continue;
            if (r.Confidence >= 0.7m && l.Confidence < 0.7m) improved++;
            l.SuggestedAccountId = a.Id; l.Reason = r.Reason; l.Confidence = r.Confidence;
        }
        await db.SaveChangesAsync(ct);
        return new RecheckSummary(unsure.Count, improved);
    }

    public async Task<List<RuleDto>> RulesAsync(Guid companyId, CancellationToken ct)
    {
        await access.ViewAsync(companyId, ct);
        var names = await db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId).ToDictionaryAsync(a => a.Code, a => a.Name, ct);
        var list = await db.CategoryRules.AsNoTracking().Where(r => r.CompanyId == companyId && r.UserId == null).OrderBy(r => r.Pattern).ToListAsync(ct);
        return list.Select(r => new RuleDto(r.Id, r.Pattern, r.Value, names.TryGetValue(r.Value, out var n) ? $"{r.Value} {n}" : r.Value)).ToList();
    }

    public async Task DeleteRuleAsync(Guid companyId, Guid ruleId, CancellationToken ct)
    {
        await access.EditAsync(companyId, ct);
        var r = await db.CategoryRules.FirstOrDefaultAsync(x => x.Id == ruleId && x.CompanyId == companyId, ct) ?? throw new NotFoundException();
        db.CategoryRules.Remove(r);
        await db.SaveChangesAsync(ct);
    }

    async Task<BankLine> PendingAsync(Guid companyId, Guid lineId, CancellationToken ct) =>
        await db.BankLines.FirstOrDefaultAsync(l => l.Id == lineId && l.CompanyId == companyId && l.Status == BankLineStatus.Pending, ct)
        ?? throw new NotFoundException("This bank line isn't waiting for review any more.");

    async Task<JournalEntry> PostLineAsync(Guid companyId, BankLine line, CancellationToken ct)
    {
        var e = await accounting.PostAsync(companyId, line.Date, line.Description, "bank", line.ToJournalLines(), me.Id, ct);
        line.Status = BankLineStatus.Posted;
        line.JournalEntryId = e.Id;
        var docs = await db.Documents.Where(d => d.BankLineId == line.Id && d.JournalEntryId == null).ToListAsync(ct);
        foreach (var d in docs) d.JournalEntryId = e.Id;
        return e;
    }
}
