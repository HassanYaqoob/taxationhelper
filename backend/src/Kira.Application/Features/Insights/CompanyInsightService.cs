using Kira.Application.Ai;
using Kira.Application.Features.Accounting;
using Kira.Application.Features.Banking;
using Kira.Application.Features.Compliance;
using Kira.Application.Features.Documents;

namespace Kira.Application.Features.Insights;

public sealed record AttentionItem(string Kind, string Title, string Detail, string Link);
public sealed record DashboardDto(string CompanyName, int FyYear, DateOnly FyStart, DateOnly FyEnd, decimal Revenue, decimal Expenses, decimal NetProfit,
    decimal Cash, decimal Receivables, List<MonthAmount> Months, List<BankAccountSummary> Banks, int PendingLines, int UnsureLines,
    List<AttentionItem> Attention, List<Deadline> Deadlines, EInvoiceStatus EInvoice);
public sealed record AuditDto(DateTime At, string User, string Action, string Detail);
public sealed record SearchHit(string Kind, string Title, string Detail, string Link, string? Id);
public sealed record AskRequest(string Question);
public sealed record AskResponse(string Answer);
public sealed record EntrySummary(Guid Id, int Number, DateOnly Date, string Memo, string Source, decimal Amount, string Accounts, bool Reversed, bool IsReversal, int Documents);
public sealed record EntryLineDto(Guid AccountId, string Code, string Name, decimal Debit, decimal Credit);
public sealed record EntryDetail(Guid Id, int Number, DateOnly Date, string Memo, string Source, string CreatedBy, DateTime CreatedAt, List<EntryLineDto> Lines,
    int? ReversedByNumber, int? ReversesNumber, List<DocumentDto> Documents);
public sealed record ManualEntry(DateOnly Date, string? Memo, List<LineInput> Lines);
public sealed record PettyCashSpend(DateOnly Date, Guid AccountId, decimal Amount, string? Memo);

/// <summary>Dashboard, journal browsing, activity log, search and "Ask Kira" for a company.</summary>
public sealed class CompanyInsightService(IAppDbContext db, AccessService access, ReportService reports, BankingService banking,
    ComplianceService compliance, AccountingService accounting, AuditService audit, Extractors ai, ICurrentUser me, IClock clock)
{
    public async Task<DashboardDto> DashboardAsync(Guid companyId, CancellationToken ct)
    {
        await access.ViewAsync(companyId, ct);
        var c = await db.Companies.AsNoTracking().FirstAsync(x => x.Id == companyId, ct);
        var today = clock.Today;
        var fyStart = c.FyStart(today);
        var fyEnd = Company.FyEnd(fyStart);
        var accounts = await reports.AccountsAsync(companyId, ct);
        var pnl = (await reports.LedgerAsync(companyId, fyStart, today, ct, accounts)).Pnl(fyStart, today);
        var all = await reports.LedgerAsync(companyId, null, today, ct, accounts);
        var banks = await banking.BankSummariesAsync(companyId, ct);
        var pending = await db.BankLines.AsNoTracking().Where(l => l.CompanyId == companyId && l.Status == BankLineStatus.Pending).ToListAsync(ct);
        var comp = await compliance.BuildAsync(c, ct);

        var attention = new List<AttentionItem>();
        var unsure = pending.Count(l => l.IsUnsure);
        if (pending.Count > 0) attention.Add(new("review", $"{pending.Count} bank lines to confirm", unsure > 0 ? $"{unsure} need your judgement" : "All have confident suggestions", "banking"));
        if (comp.Cash.PendingWithdrawals > 0)
            attention.Add(new("cash", $"{comp.Cash.PendingWithdrawals} cash withdrawal{(comp.Cash.PendingWithdrawals > 1 ? "s" : "")} to explain", $"RM {comp.Cash.PendingAmount:N2}. Petty cash or director drawings?", "banking"));
        if (comp.DueFromDirector > 0) attention.Add(new("director", $"Director owes the company RM {comp.DueFromDirector:N2}", "The Companies Act restricts loans to directors. Discuss with your accountant.", "tax"));
        if (comp.ForeignPayees.Count > 0) attention.Add(new("foreign", $"{comp.ForeignPayees.Count} payments to foreign payees this year", "Your accountant should check whether withholding tax applies.", "tax"));
        if (comp.Cp204.Status is "TooLow" or "TooHigh") attention.Add(new("cp204", comp.Cp204.Status == "TooLow" ? "CP204 estimate looks too low" : "CP204 estimate looks high", comp.Cp204.Advice, "tax"));
        if (banks.Any(b => b.LastLineDate == null || b.LastLineDate < today.AddDays(-45)))
            attention.Add(new("upload", "A bank statement is due", "Upload last month's statement to keep the books current.", "banking"));

        return new DashboardDto(c.Name, fyEnd.Year, fyStart, fyEnd, pnl.TotalIncome, pnl.TotalExpenses, pnl.NetProfit, banks.Sum(b => b.Balance),
            all.Sum(a => a.SystemKey == SystemAccounts.Receivable), await reports.MonthlyAsync(companyId, fyStart, ct), banks, pending.Count, unsure,
            attention, comp.Deadlines.Where(d => d.DaysLeft >= 0).Take(5).ToList(), comp.EInvoice);
    }

    public async Task<List<EntrySummary>> JournalAsync(Guid companyId, string? q, DateOnly? from, DateOnly? to, int take, CancellationToken ct)
    {
        await access.ViewAsync(companyId, ct);
        var query = db.JournalEntries.AsNoTracking().Where(e => e.CompanyId == companyId);
        if (from != null) query = query.Where(e => e.Date >= from);
        if (to != null) query = query.Where(e => e.Date <= to);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var number = int.TryParse(term.TrimStart('#'), out var n) ? n : -1;
            var amount = Json.ParseMoney(term);
            query = query.Where(e => e.Memo.Contains(term) || e.Number == number || (amount != null && e.Lines.Sum(l => l.Debit) == amount));
        }
        var list = await query.OrderByDescending(e => e.Date).ThenByDescending(e => e.Number).Take(Math.Clamp(take, 1, 2000))
            .Select(e => new
            {
                e.Id, e.Number, e.Date, e.Memo, e.Source, Amount = e.Lines.Sum(l => l.Debit),
                Accounts = e.Lines.Select(l => l.Account!.Name).Distinct().ToList(),
                Reversed = e.ReversedByEntryId != null, IsReversal = e.ReversesEntryId != null,
                Docs = db.Documents.Count(d => d.JournalEntryId == e.Id)
            }).ToListAsync(ct);
        return list.Select(e => new EntrySummary(e.Id, e.Number, e.Date, e.Memo, e.Source, e.Amount, string.Join(", ", e.Accounts), e.Reversed, e.IsReversal, e.Docs)).ToList();
    }

    public async Task<EntryDetail> EntryAsync(Guid companyId, Guid id, CancellationToken ct)
    {
        await access.ViewAsync(companyId, ct);
        var e = await db.JournalEntries.AsNoTracking().Include(x => x.Lines).ThenInclude(l => l.Account)
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct) ?? throw new NotFoundException();
        var by = await db.Users.Where(u => u.Id == e.CreatedById).Select(u => u.Name).FirstOrDefaultAsync(ct) ?? "";
        int? reversedBy = e.ReversedByEntryId == null ? null : await db.JournalEntries.Where(x => x.Id == e.ReversedByEntryId).Select(x => (int?)x.Number).FirstOrDefaultAsync(ct);
        int? reverses = e.ReversesEntryId == null ? null : await db.JournalEntries.Where(x => x.Id == e.ReversesEntryId).Select(x => (int?)x.Number).FirstOrDefaultAsync(ct);
        var docs = await db.Documents.AsNoTracking().Where(d => d.JournalEntryId == e.Id)
            .Select(d => new DocumentDto(d.Id, d.File!.FileName, d.File.ContentType, d.File.Size, d.Note, d.BankLineId, d.JournalEntryId, null, d.CreatedAt)).ToListAsync(ct);
        return new EntryDetail(e.Id, e.Number, e.Date, e.Memo, e.Source, by, e.CreatedAt,
            e.Lines.Select(l => new EntryLineDto(l.AccountId, l.Account?.Code ?? "", l.Account?.Name ?? "", l.Debit, l.Credit)).ToList(), reversedBy, reverses, docs);
    }

    public async Task<EntryNumberResult> PostManualAsync(Guid companyId, ManualEntry req, CancellationToken ct)
    {
        await access.EditAsync(companyId, ct);
        var memo = string.IsNullOrWhiteSpace(req.Memo) ? "Manual journal" : req.Memo.Trim();
        var e = await accounting.PostAsync(companyId, req.Date, memo, "manual", req.Lines ?? [], me.Id, ct);
        audit.Log(companyId, "Posted journal entry", $"#{e.Number} {e.Memo}");
        await db.SaveChangesAsync(ct);
        return new EntryNumberResult(e.Id, e.Number);
    }

    public async Task<EntryNumberResult> ReverseAsync(Guid companyId, Guid id, CancellationToken ct)
    {
        await access.EditAsync(companyId, ct);
        var r = await accounting.ReverseAsync(companyId, id, me.Id, ct);
        audit.Log(companyId, "Reversed journal entry", r.Memo);
        await db.SaveChangesAsync(ct);
        return new EntryNumberResult(r.Id, r.Number);
    }

    /// <summary>Records what petty cash was spent on: debit the expense, credit petty cash.</summary>
    public async Task<EntryNumberResult> PettyCashAsync(Guid companyId, PettyCashSpend req, CancellationToken ct)
    {
        await access.EditAsync(companyId, ct);
        if (req.Amount <= 0) throw new DomainException("Enter the amount spent.");
        var petty = await db.Accounts.FirstOrDefaultAsync(a => a.CompanyId == companyId && a.SystemKey == SystemAccounts.PettyCash, ct)
                    ?? throw new DomainException("No petty cash account.");
        var memo = string.IsNullOrWhiteSpace(req.Memo) ? "Petty cash spending" : req.Memo.Trim();
        var e = await accounting.PostAsync(companyId, req.Date, memo, "petty-cash", [new(req.AccountId, req.Amount, 0), new(petty.Id, 0, req.Amount)], me.Id, ct);
        audit.Log(companyId, "Recorded petty cash spending", $"RM {req.Amount:N2}, {e.Memo}");
        await db.SaveChangesAsync(ct);
        return new EntryNumberResult(e.Id, e.Number);
    }

    public async Task<List<AuditDto>> ActivityAsync(Guid companyId, int take, CancellationToken ct)
    {
        await access.ViewAsync(companyId, ct);
        return await (from a in db.AuditLogs.AsNoTracking()
                      where a.CompanyId == companyId
                      join u in db.Users on a.UserId equals u.Id into users
                      from u in users.DefaultIfEmpty()
                      orderby a.At descending
                      select new AuditDto(a.At, u != null ? u.Name : "Unknown", a.Action, a.Detail))
            .Take(Math.Clamp(take, 1, 2000)).ToListAsync(ct);
    }

    public async Task<List<SearchHit>> SearchAsync(Guid companyId, string? q, CancellationToken ct)
    {
        await access.ViewAsync(companyId, ct);
        var term = (q ?? "").Trim();
        if (term.Length < 2) return [];
        var amount = Json.ParseMoney(term);
        var hits = new List<SearchHit>();

        var entries = await db.JournalEntries.AsNoTracking().Where(e => e.CompanyId == companyId && (e.Memo.Contains(term) || (amount != null && e.Lines.Sum(l => l.Debit) == amount)))
            .OrderByDescending(e => e.Date).Take(12).Select(e => new { e.Id, e.Memo, e.Number, e.Date, Amount = e.Lines.Sum(l => l.Debit) }).ToListAsync(ct);
        hits.AddRange(entries.Select(e => new SearchHit("Entry", e.Memo, $"#{e.Number} · {e.Date:dd MMM yyyy} · RM {e.Amount:N2}", "transactions", e.Id.ToString())));

        var lines = await db.BankLines.AsNoTracking().Where(l => l.CompanyId == companyId && l.Status == BankLineStatus.Pending && (l.Description.Contains(term) || l.Amount == amount))
            .Take(8).ToListAsync(ct);
        hits.AddRange(lines.Select(l => new SearchHit("To review", l.Description, $"{l.Date:dd MMM yyyy} · RM {l.Amount:N2}", "banking", l.Id.ToString())));

        var docs = await db.Documents.AsNoTracking().Where(d => d.CompanyId == companyId && (d.File!.FileName.Contains(term) || d.Note.Contains(term)))
            .Take(6).Select(d => new { d.Id, d.File!.FileName, d.CreatedAt }).ToListAsync(ct);
        hits.AddRange(docs.Select(d => new SearchHit("Document", d.FileName, d.CreatedAt.ToString("dd MMM yyyy"), "documents", d.Id.ToString())));

        var accounts = await db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId && (a.Name.Contains(term) || a.Code == term)).Take(6).ToListAsync(ct);
        hits.AddRange(accounts.Select(a => new SearchHit("Account", $"{a.Code} {a.Name}", a.Type.ToString(), "reports", a.Id.ToString())));
        return hits;
    }

    public async Task<AskResponse> AskAsync(Guid companyId, AskRequest req, CancellationToken ct)
    {
        await access.ViewAsync(companyId, ct);
        if (string.IsNullOrWhiteSpace(req.Question)) throw new DomainException("Type a question.");
        var c = await db.Companies.AsNoTracking().FirstAsync(x => x.Id == companyId, ct);
        var today = clock.Today;
        var fyStart = c.FyStart(today);
        var pnl = (await reports.LedgerAsync(companyId, fyStart, today, ct)).Pnl(fyStart, today);
        var months = (await reports.MonthlyAsync(companyId, fyStart, ct)).Where(m => m.Income != 0 || m.Expenses != 0);
        var comp = await compliance.BuildAsync(c, ct);
        var recent = await db.JournalEntries.AsNoTracking().Where(e => e.CompanyId == companyId && e.Date >= fyStart).OrderByDescending(e => e.Date).Take(250)
            .Select(e => new { date = e.Date, e.Memo, amount = e.Lines.Sum(l => l.Debit), accounts = e.Lines.Select(l => l.Account!.Name).ToList() }).ToListAsync(ct);
        var context = JsonSerializer.Serialize(new
        {
            company = c.Name, industry = c.Industry, financialYear = $"{fyStart:yyyy-MM-dd} to {Company.FyEnd(fyStart):yyyy-MM-dd}", today = today.ToString("yyyy-MM-dd"),
            profitAndLoss = new { income = pnl.Income.Select(i => new { i.Name, i.Amount }), expenses = pnl.Expenses.Select(i => new { i.Name, i.Amount }), pnl.NetProfit },
            months, tax = new { comp.Tax.Chargeable, comp.Tax.Tax, comp.Tax.Cp204Paid, comp.Cp204.Advice }, comp.DueFromDirector,
            foreignPayees = comp.ForeignPayees.Count, recentEntries = recent
        });
        return new AskResponse(await ai.AnswerAsync(context, Json.Cut(req.Question.Trim(), 1000), ct));
    }
}
