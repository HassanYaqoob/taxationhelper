using Kira.Application.Ai;
using Kira.Application.Features.Insights;
using Microsoft.Extensions.Options;

namespace Kira.Application.Features.Personal;

public sealed record ReliefLine(string Code, string Name, string Hint, decimal Limit, decimal Claimed, int Receipts, bool New);
public sealed record ReliefSuggestion(Guid ReceiptId, string Merchant, DateOnly Date, decimal Amount, string Code, string Name, string Reason);
public sealed record ReliefSummary(int Year, List<ReliefLine> Lines, List<ReliefSuggestion> Suggestions, string FilingNote);
public sealed record CategoryAmount(string Category, decimal Amount);
public sealed record PersonalOverview(string Month, decimal MoneyIn, decimal Spending, List<CategoryAmount> ByCategory, List<PaymentAccountDto> Accounts,
    List<ReceiptDto> RecentReceipts, List<ReliefLine> Reliefs, List<AttentionItem> Attention, string FilingNote, List<string> Months);
public sealed record SpendingReport(int Year, List<string> Categories, List<string> Months, List<List<decimal>> Values);

public sealed class PersonalInsightService(IAppDbContext db, ICurrentUser me, PaymentAccountService accounts, ReceiptService receipts,
    Extractors ai, IExcelWriter excel, IOptions<KiraRules> rules, IClock clock)
{
    public async Task<ReliefSummary> ReliefsAsync(int? year, CancellationToken ct)
    {
        var y = year ?? clock.Today.Year;
        var def = rules.Value.ReliefsFor(y);
        var d0 = new DateOnly(y, 1, 1); var d1 = new DateOnly(y, 12, 31);
        var list = await db.Receipts.AsNoTracking().Where(r => r.UserId == me.Id && r.Date >= d0 && r.Date <= d1)
            .Select(r => new { r.Id, r.Merchant, r.Date, r.Total, r.ReliefCode, r.SuggestedReliefCode, r.ReliefDismissed, r.ReliefReason }).ToListAsync(ct);
        var lines = def.Items.Select(i =>
        {
            var tagged = list.Where(r => r.ReliefCode == i.Code).ToList();
            return new ReliefLine(i.Code, i.Name, i.Hint, i.Limit, tagged.Sum(r => r.Total), tagged.Count, i.New);
        }).ToList();
        var suggestions = list.Where(r => r.ReliefCode == null && r.SuggestedReliefCode != null && !r.ReliefDismissed)
            .Select(r => new ReliefSuggestion(r.Id, r.Merchant, r.Date, r.Total, r.SuggestedReliefCode!,
                def.Items.FirstOrDefault(i => i.Code == r.SuggestedReliefCode)?.Name ?? r.SuggestedReliefCode!, r.ReliefReason)).ToList();
        return new ReliefSummary(y, lines, suggestions, rules.Value.Compliance.PersonalFilingNote);
    }

    public async Task<PersonalOverview> OverviewAsync(string? month, CancellationToken ct)
    {
        var months = (await db.PersonalTransactions.AsNoTracking().Where(t => t.UserId == me.Id)
                .Select(t => new { t.Date.Year, t.Date.Month }).Distinct().ToListAsync(ct))
            .Select(x => $"{x.Year:0000}-{x.Month:00}").OrderByDescending(x => x).ToList();
        var m = month != null && months.Contains(month) ? month : months.FirstOrDefault() ?? clock.Today.ToString("yyyy-MM");
        var m0 = DateOnly.ParseExact(m + "-01", "yyyy-MM-dd");
        var m1 = m0.AddMonths(1);
        var inMonth = await db.PersonalTransactions.AsNoTracking().Where(t => t.UserId == me.Id && !t.IsTransfer && t.Date >= m0 && t.Date < m1).ToListAsync(ct);
        var byCategory = inMonth.Where(t => t.Direction == Direction.Out).GroupBy(t => t.Category)
            .Select(g => new CategoryAmount(g.Key, g.Sum(t => t.Amount))).OrderByDescending(c => c.Amount).ToList();
        var accountList = await accounts.ListAsync(ct);
        var recent = await receipts.ListAsync(false, ct);
        var relief = await ReliefsAsync(m0.Year, ct);

        var attention = new List<AttentionItem>();
        var noPayment = recent.Count(r => r.PaymentStatus == PaymentStatus.Unknown);
        if (noPayment > 0) attention.Add(new("payment", $"{noPayment} receipt{(noPayment > 1 ? "s" : "")} without payment details", "Mark cash, or upload the payment screenshot.", "receipts"));
        var unsaved = recent.Count(r => r.Status == ReceiptStatus.Review);
        if (unsaved > 0) attention.Add(new("receipts", $"{unsaved} receipt{(unsaved > 1 ? "s" : "")} to check and save", "Kira read them; confirm the details.", "receipts"));
        var unreviewed = await db.PersonalTransactions.CountAsync(t => t.UserId == me.Id && !t.Reviewed, ct);
        if (unreviewed > 0) attention.Add(new("review", $"{unreviewed} statement line{(unreviewed > 1 ? "s" : "")} to confirm", "Kira wasn't sure about the category.", "transactions"));
        if (relief.Suggestions.Count > 0)
            attention.Add(new("relief", $"{relief.Suggestions.Count} receipt{(relief.Suggestions.Count > 1 ? "s" : "")} might qualify for tax relief",
                string.Join(", ", relief.Suggestions.Take(2).Select(s => $"{s.Merchant} ({s.Name})")), "reliefs"));
        if (accountList.All(a => a.Kind == PaymentKind.Cash))
            attention.Add(new("accounts", "Add the accounts you pay from", "Optional, but it lets Kira tell which account paid each receipt.", "accounts"));

        return new PersonalOverview(m, inMonth.Where(t => t.Direction == Direction.In).Sum(t => t.Amount), byCategory.Sum(c => c.Amount), byCategory, accountList,
            recent.Take(5).ToList(), relief.Lines.Where(l => l.Claimed > 0).ToList(), attention, relief.FilingNote, months);
    }

    public async Task<SpendingReport> SpendingAsync(int? year, CancellationToken ct)
    {
        var y = year ?? clock.Today.Year;
        var d0 = new DateOnly(y, 1, 1); var d1 = new DateOnly(y, 12, 31);
        var rows = await db.PersonalTransactions.AsNoTracking()
            .Where(t => t.UserId == me.Id && !t.IsTransfer && t.Direction == Direction.Out && t.Date >= d0 && t.Date <= d1)
            .GroupBy(t => new { t.Category, t.Date.Month }).Select(g => new { g.Key.Category, g.Key.Month, Amount = g.Sum(t => t.Amount) }).ToListAsync(ct);
        var categories = rows.GroupBy(r => r.Category).OrderByDescending(g => g.Sum(r => r.Amount)).Select(g => g.Key).ToList();
        var months = Enumerable.Range(1, 12).Select(i => $"{y}-{i:00}").ToList();
        var values = categories.Select(c => Enumerable.Range(1, 12).Select(mo => rows.Where(r => r.Category == c && r.Month == mo).Sum(r => r.Amount)).ToList()).ToList();
        return new SpendingReport(y, categories, months, values);
    }

    public async Task<FileResult> SpendingExportAsync(int? year, CancellationToken ct)
    {
        var r = await SpendingAsync(year, ct);
        var relief = await ReliefsAsync(r.Year, ct);
        var rows = new List<object?[]> { new object?[] { "Personal spending" }, new object?[] { $"Year {r.Year}, transfers between your own accounts excluded" }, Array.Empty<object?>() };
        rows.Add(new object?[] { "Category" }.Concat(r.Months.Select(m => (object?)m[5..])).Append("Total").ToArray());
        for (var i = 0; i < r.Categories.Count; i++)
            rows.Add(new object?[] { r.Categories[i] }.Concat(r.Values[i].Select(v => (object?)(v == 0 ? null : v))).Append(r.Values[i].Sum()).ToArray());
        var reliefRows = new List<object?[]> { new object?[] { "Tax reliefs" }, new object?[] { $"Year of assessment {r.Year}. Check limits with HASiL before filing." },
            Array.Empty<object?>(), new object?[] { "Relief", "Limit", "Claimed", "Receipts" } };
        reliefRows.AddRange(relief.Lines.Select(l => new object?[] { l.Name, l.Limit, l.Claimed, l.Receipts }));
        var bytes = excel.Build(
        [
            new Sheet("Spending", rows, [22, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 13], [3]),
            new Sheet("Tax reliefs", reliefRows, [40, 14, 14, 10], [3])
        ]);
        return new FileResult(bytes, FileRules.ExcelContentType, $"Kira-personal-{r.Year}.xlsx");
    }

    public async Task<List<SearchHit>> SearchAsync(string? q, CancellationToken ct)
    {
        var term = (q ?? "").Trim();
        if (term.Length < 2) return [];
        var amount = Json.ParseMoney(term);
        var tx = await db.PersonalTransactions.AsNoTracking().Where(t => t.UserId == me.Id && (t.Description.Contains(term) || t.Amount == amount))
            .OrderByDescending(t => t.Date).Take(12).ToListAsync(ct);
        var rc = await db.Receipts.AsNoTracking().Where(r => r.UserId == me.Id && (r.Merchant.Contains(term) || r.Total == amount))
            .OrderByDescending(r => r.Date).Take(8).ToListAsync(ct);
        return tx.Select(t => new SearchHit("Transaction", t.Description, $"{t.Date:dd MMM yyyy} · RM {t.Amount:N2} · {t.Category}", "transactions", t.Id.ToString()))
            .Concat(rc.Select(r => new SearchHit("Receipt", r.Merchant, $"{r.Date:dd MMM yyyy} · RM {r.Total:N2}", "receipts", r.Id.ToString()))).ToList();
    }

    public async Task<AskResponse> AskAsync(AskRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Question)) throw new DomainException("Type a question.");
        var since = clock.Today.AddMonths(-12);
        var tx = await db.PersonalTransactions.AsNoTracking().Where(t => t.UserId == me.Id && t.Date >= since).OrderByDescending(t => t.Date).Take(600).ToListAsync(ct);
        var accountList = await accounts.ListAsync(ct);
        var relief = await ReliefsAsync(clock.Today.Year, ct);
        var context = JsonSerializer.Serialize(new
        {
            today = clock.Today.ToString("yyyy-MM-dd"),
            accounts = accountList.Select(a => new { a.Name, kind = a.Kind.ToString(), a.Balance }),
            monthlyByCategory = tx.Where(t => !t.IsTransfer && t.Direction == Direction.Out).GroupBy(t => new { m = t.Date.ToString("yyyy-MM"), t.Category })
                .Select(g => new { month = g.Key.m, category = g.Key.Category, amount = g.Sum(t => t.Amount) }),
            income = tx.Where(t => !t.IsTransfer && t.Direction == Direction.In).GroupBy(t => t.Date.ToString("yyyy-MM")).Select(g => new { month = g.Key, amount = g.Sum(t => t.Amount) }),
            recent = tx.Take(200).Select(t => new { date = t.Date.ToString("yyyy-MM-dd"), t.Description, t.Amount, direction = t.Direction.ToString(), t.Category, t.IsTransfer }),
            reliefs = relief.Lines.Select(l => new { l.Name, l.Limit, l.Claimed })
        });
        return new AskResponse(await ai.AnswerAsync(context, Json.Cut(req.Question.Trim(), 1000), ct));
    }
}
