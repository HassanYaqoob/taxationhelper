using Kira.Application.Features.Accounting;
using Microsoft.Extensions.Options;

namespace Kira.Application.Features.Compliance;

public sealed record Cp204Advice(decimal Estimate, decimal ProjectedTax, decimal PaidSoFar, int? NextRevisionMonth, DateOnly? NextRevisionBy, string Status, string Advice);
public sealed record AuditCriterion(string Name, string Value, string Limit, bool Within);
public sealed record AuditCheck(int Year, List<AuditCriterion> Criteria, int Met, bool LikelyQualifies, string Note);
public sealed record EInvoiceStatus(decimal Revenue12m, decimal Threshold, bool Required, string Note);
public sealed record Deadline(string Name, DateOnly Due, int DaysLeft, string Kind, Guid? ReminderId);
public sealed record FlaggedLine(Guid Id, DateOnly Date, string Description, decimal Amount, string Status);
public sealed record CashSummary(int PendingWithdrawals, decimal PendingAmount, decimal PettyCashBalance);
public sealed record ComplianceSummary(TaxEstimate Tax, DateOnly From, DateOnly To, Cp204Advice Cp204, AuditCheck Audit, EInvoiceStatus EInvoice,
    List<Deadline> Deadlines, decimal DueFromDirector, List<FlaggedLine> ForeignPayees, CashSummary Cash);
public sealed record ReminderInput(string Name, DateOnly DueDate, ReminderRepeat Repeat, string? Notes);
public sealed record ReminderDone(DateOnly? Next);

public sealed class ComplianceService(IAppDbContext db, AccessService access, ReportService reports, AuditService audit, IExcelWriter excel,
    IOptions<KiraRules> rules, IClock clock)
{
    KiraRules R => rules.Value;

    public async Task<ComplianceSummary> SummaryAsync(Guid companyId, CancellationToken ct)
    {
        await access.ViewAsync(companyId, ct);
        var c = await db.Companies.AsNoTracking().FirstAsync(x => x.Id == companyId, ct);
        return await BuildAsync(c, ct);
    }

    internal async Task<ComplianceSummary> BuildAsync(Company c, CancellationToken ct)
    {
        var today = clock.Today;
        var fyStart = c.FyStart(today);
        var fyEnd = Company.FyEnd(fyStart);
        var accounts = await reports.AccountsAsync(c.Id, ct);
        var ytd = await reports.LedgerAsync(c.Id, fyStart, today, ct, accounts);
        var all = await reports.LedgerAsync(c.Id, null, today, ct, accounts);
        var rule = R.TaxFor(fyEnd.Year);
        var estimate = TaxCalculator.Estimate(rule, c.IsSme, ytd.Balances());

        // CP204: project the year from the months elapsed so far.
        var monthsElapsed = Math.Clamp((today.Year - fyStart.Year) * 12 + today.Month - fyStart.Month + 1, 1, 12);
        var projected = TaxCalculator.Estimate(rule, c.IsSme, ytd.Balances(), 12m / monthsElapsed).Tax;
        var revisionMonths = R.Compliance.Cp204RevisionMonths.Count > 0 ? R.Compliance.Cp204RevisionMonths : [6, 9, 11];
        int? nextMonth = null; DateOnly? nextBy = null;
        foreach (var m in revisionMonths.OrderBy(x => x))
        {
            var by = fyStart.AddMonths(m).AddDays(-1);
            if (by >= today) { nextMonth = m; nextBy = by; break; }
        }
        var margin = R.Compliance.Cp204PenaltyMarginPct;
        string status, advice;
        if (c.Cp204Estimate <= 0) { status = "NotSet"; advice = "Enter your CP204 estimate in Settings so Kira can compare it with your actual results."; }
        else if (projected > c.Cp204Estimate * (1 + margin / 100))
        { status = "TooLow"; advice = $"Projected tax is more than {margin:0}% above your estimate, which can attract a penalty. Consider revising up" + (nextMonth != null ? $" in month {nextMonth}." : " with your tax agent."); }
        else if (projected < c.Cp204Estimate * 0.85m)
        { status = "TooHigh"; advice = "Your estimate looks high. Revising down" + (nextMonth != null ? $" in month {nextMonth}" : "") + $" would mean about RM {c.Cp204Estimate - projected:N0} less to pay this year."; }
        else { status = "OnTrack"; advice = "Your estimate is close to the projection. No change needed for now."; }
        var cp204 = new Cp204Advice(c.Cp204Estimate, projected, estimate.Cp204Paid, nextMonth, nextBy, status, advice);

        // Audit exemption: within 2 of 3 limits for the phase in force.
        var phase = R.AuditPhaseFor(fyEnd.Year);
        var totalAssets = all.Sum(a => a.Type == AccountType.Asset);
        var annualRevenue = estimate.Income * 12m / monthsElapsed;
        AuditCheck auditCheck;
        if (phase == null) auditCheck = new AuditCheck(fyEnd.Year, [], 0, false, "Audit exemption limits are not configured.");
        else
        {
            var criteria = new List<AuditCriterion>
            {
                new("Revenue", $"RM {annualRevenue:N0} (full-year projection)", $"RM {phase.Revenue:N0}", annualRevenue <= phase.Revenue),
                new("Total assets", $"RM {totalAssets:N0}", $"RM {phase.Assets:N0}", totalAssets <= phase.Assets),
                new("Employees", c.EmployeeCount.ToString(), phase.Employees.ToString(), c.EmployeeCount <= phase.Employees)
            };
            var met = criteria.Count(x => x.Within);
            auditCheck = new AuditCheck(fyEnd.Year, criteria, met, met >= 2,
                "Limits must be met for this year and the two before. Exempt companies still lodge unaudited financial statements with SSM.");
        }

        // e-Invoice: revenue over the last 12 months.
        var last12 = await reports.LedgerAsync(c.Id, today.AddYears(-1).AddDays(1), today, ct, accounts);
        var revenue12 = last12.Sum(a => a.Type == AccountType.Income);
        var eInvoice = new EInvoiceStatus(revenue12, R.Compliance.EInvoiceExemptBelow, revenue12 >= R.Compliance.EInvoiceExemptBelow, R.Compliance.EInvoiceNote);

        // Deadlines
        var deadlines = new List<Deadline>();
        void Add(string name, DateOnly due, string kind, Guid? id = null) => deadlines.Add(new Deadline(name, due, due.DayNumber - today.DayNumber, kind, id));
        if (nextBy is DateOnly nb) Add($"CP204 revision (month {nextMonth})", nb, "tax");
        var lastFyEnd = fyStart.AddDays(-1);
        var formCPrevious = EndOfMonth(lastFyEnd.AddMonths(R.Compliance.FormCMonthsAfterYearEnd));
        if (formCPrevious >= today) Add($"Form C, FY{lastFyEnd.Year}", formCPrevious, "tax");
        Add($"Form C, FY{fyEnd.Year}", EndOfMonth(fyEnd.AddMonths(R.Compliance.FormCMonthsAfterYearEnd)), "tax");
        if (c.IncorporatedOn is DateOnly inc)
        {
            var anniversary = new DateOnly(today.Year, inc.Month, Math.Min(inc.Day, DateTime.DaysInMonth(today.Year, inc.Month)));
            var due = anniversary.AddDays(30);
            if (due < today) due = anniversary.AddYears(1).AddDays(30);
            Add("SSM annual return", due, "ssm");
        }
        var reminders = await db.Reminders.AsNoTracking().Where(r => r.CompanyId == c.Id).ToListAsync(ct);
        foreach (var r in reminders) Add(r.Name, r.DueDate, "custom", r.Id);

        var dueFromDirector = all.Sum(a => a.SystemKey == SystemAccounts.DueFromDirector);
        var petty = all.Sum(a => a.SystemKey == SystemAccounts.PettyCash);
        var lines = await db.BankLines.AsNoTracking().Where(l => l.CompanyId == c.Id && l.Status != BankLineStatus.Ignored && l.Flags != "").ToListAsync(ct);
        var foreign = lines.Where(l => l.HasFlag(LineFlags.ForeignPayee) && l.Date >= fyStart).OrderByDescending(l => l.Date)
            .Select(l => new FlaggedLine(l.Id, l.Date, l.Description, l.Amount, l.Status.ToString())).ToList();
        var pendingCash = lines.Where(l => l.Status == BankLineStatus.Pending && l.HasFlag(LineFlags.CashWithdrawal)).ToList();

        return new ComplianceSummary(estimate, fyStart, today, cp204, auditCheck, eInvoice, deadlines.OrderBy(d => d.Due).ToList(), dueFromDirector, foreign,
            new CashSummary(pendingCash.Count, pendingCash.Sum(l => l.Amount), petty));
    }

    /// <summary>One workbook with everything a tax agent needs to prepare Form C.</summary>
    public async Task<FileResult> AccountantPackageAsync(Guid companyId, CancellationToken ct)
    {
        await access.ViewAsync(companyId, ct);
        var c = await db.Companies.AsNoTracking().FirstAsync(x => x.Id == companyId, ct);
        var sum = await BuildAsync(c, ct);
        var t = sum.Tax;
        var (from, to) = (sum.From, sum.To);

        var taxRows = new List<object?[]>
        {
            new object?[] { c.Name }, new object?[] { $"Tax summary for YA {t.YearOfAssessment}, {from:dd MMM yyyy} to {to:dd MMM yyyy} (estimate prepared in Kira)" }, Array.Empty<object?>(),
            new object?[] { "Item", "RM" },
            new object?[] { "Business income", t.Income }, new object?[] { "Deductible expenses", t.Deductible },
            new object?[] { "Entertainment", t.Entertainment }, new object?[] { "Entertainment added back", t.EntertainmentAddBack },
            new object?[] { "Fines and penalties (not deductible)", t.NonDeductible }, new object?[] { "Adjusted income", t.Adjusted },
            new object?[] { "Assets bought", t.AssetsBought }, new object?[] { $"Capital allowance estimate ({t.CapitalAllowancePct:0}%)", t.CapitalAllowance },
            new object?[] { "Chargeable income", t.Chargeable }
        };
        taxRows.AddRange(t.Tiers.Select(x => new object?[] { "  " + x.Label, x.Amount }));
        taxRows.Add(["Estimated tax", t.Tax]);
        taxRows.Add(["CP204 paid so far", t.Cp204Paid]);
        taxRows.Add(["Estimated balance (negative = overpaid)", t.Balance]);
        taxRows.Add([]);
        var notesHead = taxRows.Count;
        taxRows.Add(["Items for the tax agent to review", ""]);
        taxRows.Add(["Amount due from director", sum.DueFromDirector]);
        taxRows.Add(["Unexplained cash withdrawals (pending)", sum.Cash.PendingAmount]);
        taxRows.Add(["Petty cash balance", sum.Cash.PettyCashBalance]);
        taxRows.Add(["Payments to foreign payees (check withholding tax)", sum.ForeignPayees.Sum(f => f.Amount)]);
        taxRows.Add(["CP204 estimate on file", sum.Cp204.Estimate]);

        var accounts = await reports.AccountsAsync(companyId, ct);
        var names = accounts.ToDictionary(a => a.Id, a => $"{a.Code} {a.Name}");

        var foreign = new List<object?[]> { new object?[] { c.Name }, new object?[] { "Payments to foreign payees" }, Array.Empty<object?>(), new object?[] { "Date", "Description", "Amount", "Status" } };
        foreign.AddRange(sum.ForeignPayees.Select(f => new object?[] { f.Date, f.Description, f.Amount, f.Status }));

        var bankLines = await db.BankLines.AsNoTracking().Where(l => l.CompanyId == companyId && l.Date >= from && l.Date <= to).OrderBy(l => l.Date).ToListAsync(ct);
        var bank = new List<object?[]> { new object?[] { c.Name }, new object?[] { "Bank statement lines and how they were categorised" }, Array.Empty<object?>(),
            new object?[] { "Date", "Bank", "Description", "In", "Out", "Category", "Status", "Flags" } };
        bank.AddRange(bankLines.Select(l => new object?[] { l.Date, names.GetValueOrDefault(l.BankAccountId), l.Description,
            l.Direction == Direction.In ? l.Amount : null, l.Direction == Direction.Out ? l.Amount : null,
            l.SuggestedAccountId is Guid g ? names.GetValueOrDefault(g) : "", l.Status.ToString(), l.Flags.Replace("_", " ") }));

        var entries = await db.JournalEntries.AsNoTracking().Include(e => e.Lines)
            .Where(e => e.CompanyId == companyId && e.Date >= from && e.Date <= to).OrderBy(e => e.Date).ThenBy(e => e.Number).ToListAsync(ct);
        var capital = accounts.Where(a => a.Tax == TaxTreatment.Capital).Select(a => a.Id).ToHashSet();
        var fixedAssets = new List<object?[]> { new object?[] { c.Name }, new object?[] { "Fixed assets bought this year" }, Array.Empty<object?>(), new object?[] { "Date", "Description", "Account", "Cost" } };
        var journal = new List<object?[]> { new object?[] { c.Name }, new object?[] { $"Journal {from:dd MMM yyyy} to {to:dd MMM yyyy}" }, Array.Empty<object?>(),
            new object?[] { "Date", "Entry", "Description", "Account", "Debit", "Credit" } };
        foreach (var e in entries)
            foreach (var l in e.Lines)
            {
                journal.Add([e.Date, e.Number, e.Memo, names.GetValueOrDefault(l.AccountId), l.Debit == 0 ? null : l.Debit, l.Credit == 0 ? null : l.Credit]);
                if (capital.Contains(l.AccountId) && l.Debit > 0) fixedAssets.Add([e.Date, e.Memo, names.GetValueOrDefault(l.AccountId), l.Debit]);
            }

        var bytes = excel.Build(
        [
            new Sheet("Tax summary", taxRows, [52, 18], [3, notesHead]),
            ReportSheets.Pnl(c, await reports.PnlAsync(companyId, from, to, ct)),
            ReportSheets.BalanceSheet(c, await reports.BalanceSheetAsync(companyId, to, ct)),
            ReportSheets.TrialBalance(c, await reports.TrialBalanceAsync(companyId, to, ct)),
            new Sheet("Journal", journal, [12, 8, 50, 34, 14, 14], [3]),
            new Sheet("Bank lines", bank, [12, 26, 50, 14, 14, 34, 10, 18], [3]),
            new Sheet("Fixed assets", fixedAssets, [12, 50, 30, 14], [3]),
            new Sheet("Foreign payees", foreign, [12, 50, 14, 10], [3])
        ]);
        audit.Log(companyId, "Downloaded accountant package", $"YA {t.YearOfAssessment}");
        await db.SaveChangesAsync(ct);
        return new FileResult(bytes, FileRules.ExcelContentType, $"{ReportSheets.SafeName(c.Name)}-YA{t.YearOfAssessment}-Accountant-Package.xlsx");
    }

    public async Task<Guid> AddReminderAsync(Guid companyId, ReminderInput r, CancellationToken ct)
    {
        await access.EditAsync(companyId, ct);
        if (string.IsNullOrWhiteSpace(r.Name)) throw new DomainException("Give the reminder a name.");
        var x = new Reminder { CompanyId = companyId, Name = Json.Cut(r.Name.Trim(), 200), DueDate = r.DueDate, Repeat = r.Repeat, Notes = Json.Cut(r.Notes, 1000) };
        db.Reminders.Add(x);
        audit.Log(companyId, "Added reminder", x.Name);
        await db.SaveChangesAsync(ct);
        return x.Id;
    }

    public async Task<ReminderDone> CompleteReminderAsync(Guid companyId, Guid id, CancellationToken ct)
    {
        await access.EditAsync(companyId, ct);
        var r = await db.Reminders.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct) ?? throw new NotFoundException();
        var keep = r.Complete();
        if (!keep) db.Reminders.Remove(r);
        audit.Log(companyId, "Completed reminder", r.Name);
        await db.SaveChangesAsync(ct);
        return new ReminderDone(keep ? r.DueDate : null);
    }

    public async Task DeleteReminderAsync(Guid companyId, Guid id, CancellationToken ct)
    {
        await access.EditAsync(companyId, ct);
        var r = await db.Reminders.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct) ?? throw new NotFoundException();
        db.Reminders.Remove(r);
        audit.Log(companyId, "Deleted reminder", r.Name);
        await db.SaveChangesAsync(ct);
    }

    static DateOnly EndOfMonth(DateOnly d) => new(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
}
