namespace Kira.Application.Features.Accounting;

/// <summary>Turns reports into formatted spreadsheet sheets.</summary>
public static class ReportSheets
{
    public static string SafeName(string s) =>
        new string(s.Where(ch => char.IsLetterOrDigit(ch) || ch == ' ').ToArray()).Trim().Replace(' ', '-');

    public static Sheet Pnl(Company c, PnlDto p)
    {
        var rows = new List<object?[]> { new object?[] { c.Name }, new object?[] { $"Profit and loss, {p.From:dd MMM yyyy} to {p.To:dd MMM yyyy}" }, Array.Empty<object?>(), new object?[] { "Income", "RM" } };
        rows.AddRange(p.Income.Select(i => new object?[] { $"{i.Code} {i.Name}", i.Amount }));
        rows.Add(["Total income", p.TotalIncome]);
        rows.Add([]);
        var expHead = rows.Count;
        rows.Add(["Expenses", "RM"]);
        rows.AddRange(p.Expenses.Select(i => new object?[] { $"{i.Code} {i.Name}", i.Amount }));
        rows.Add(["Total expenses", p.TotalExpenses]);
        rows.Add([]);
        rows.Add([p.NetProfit >= 0 ? "Net profit" : "Net loss", p.NetProfit]);
        return new Sheet("Profit and loss", rows, [44, 18], [3, expHead]);
    }

    public static Sheet BalanceSheet(Company c, BalanceSheetDto b)
    {
        var rows = new List<object?[]> { new object?[] { c.Name }, new object?[] { $"Balance sheet as at {b.AsAt:dd MMM yyyy}" }, Array.Empty<object?>() };
        var heads = new List<int>();
        void Section(string title, List<BsRow> list, decimal total)
        {
            heads.Add(rows.Count);
            rows.Add([title, "RM"]);
            rows.AddRange(list.Select(r => new object?[] { (r.Code != null ? r.Code + " " : "") + r.Name, r.Amount }));
            rows.Add([$"Total {title.ToLowerInvariant()}", total]);
            rows.Add([]);
        }
        Section("Assets", b.Assets, b.TotalAssets);
        Section("Liabilities", b.Liabilities, b.TotalLiabilities);
        Section("Equity", b.Equity, b.TotalEquity);
        rows.Add(["Total liabilities and equity", b.TotalLiabilities + b.TotalEquity]);
        return new Sheet("Balance sheet", rows, [44, 18], heads.ToArray());
    }

    public static Sheet TrialBalance(Company c, TrialBalanceDto t)
    {
        var rows = new List<object?[]> { new object?[] { c.Name }, new object?[] { $"Trial balance as at {t.AsAt:dd MMM yyyy}" }, Array.Empty<object?>(), new object?[] { "Code", "Account", "Debit", "Credit" } };
        rows.AddRange(t.Rows.Select(r => new object?[] { r.Code, r.Name, r.Debit == 0 ? null : r.Debit, r.Credit == 0 ? null : r.Credit }));
        rows.Add(["Total", "", t.TotalDebit, t.TotalCredit]);
        return new Sheet("Trial balance", rows, [10, 40, 16, 16], [3]);
    }

    public static Sheet GeneralLedger(Company c, GeneralLedgerDto g)
    {
        var rows = new List<object?[]>
        {
            new object?[] { c.Name }, new object?[] { $"General ledger: {g.Code} {g.Name}, {g.From:dd MMM yyyy} to {g.To:dd MMM yyyy}" }, Array.Empty<object?>(),
            new object?[] { "Date", "Entry", "Description", "Debit", "Credit", "Balance" }, new object?[] { null, null, "Opening balance", null, null, g.Opening }
        };
        rows.AddRange(g.Rows.Select(r => new object?[] { r.Date, r.Number, r.Memo, r.Debit == 0 ? null : r.Debit, r.Credit == 0 ? null : r.Credit, r.Balance }));
        rows.Add([null, null, "Closing balance", null, null, g.Closing]);
        return new Sheet("General ledger", rows, [12, 8, 50, 14, 14, 16], [3]);
    }
}
