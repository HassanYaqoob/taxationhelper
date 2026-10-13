using System.Globalization;
using System.Text;

namespace Kira.Application.Ai;

public sealed record RawLine(DateOnly Date, string Description, Direction Direction, decimal Amount, decimal? Balance);

/// <summary>
/// Reads a bank CSV without AI, by recognising common column headers (English and Malay).
/// Used when no AI key is configured.
/// </summary>
public static class StatementCsv
{
    static readonly string[] DateFormats =
        ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "yyyy-MM-dd", "dd-MM-yyyy", "d-M-yyyy", "dd MMM yyyy", "d MMM yyyy", "dd-MMM-yyyy", "dd MMM yy", "dd.MM.yyyy"];

    public static DateOnly? ParseDate(string s) =>
        DateOnly.TryParseExact(s.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static List<RawLine> Parse(string text)
    {
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count < 2) throw new DomainException("The file has no rows.");
        var delim = new[] { ',', ';', '\t', '|' }.OrderByDescending(c => lines[0].Count(x => x == c)).First();
        var hi = lines.FindIndex(l => IsHeader(l.ToLowerInvariant()));
        if (hi < 0) throw new DomainException("Couldn't find a header row with Date and Description. Turn on AI to read any format.");
        var head = Split(lines[hi], delim).Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(params string[] names) => head.FindIndex(h => names.Any(h.Contains));
        int cDate = Col("date", "tarikh"), cDesc = Col("description", "keterangan", "details", "particular", "transaction", "butiran");
        int cDr = Col("debit", "withdrawal", "pengeluaran"), cCr = Col("credit", "deposit", "simpanan");
        int cAmt = Col("amount", "amaun"), cBal = Col("balance", "baki");
        if (cDate < 0 || cDesc < 0) throw new DomainException("The CSV needs Date and Description columns.");

        var result = new List<RawLine>();
        foreach (var l in lines.Skip(hi + 1))
        {
            var f = Split(l, delim);
            string Get(int i) => i >= 0 && i < f.Count ? f[i].Trim() : "";
            if (ParseDate(Get(cDate)) is not DateOnly date) continue;
            var dr = cDr >= 0 ? Json.ParseMoney(Get(cDr)) : null;
            var cr = cCr >= 0 ? Json.ParseMoney(Get(cCr)) : null;
            decimal amount; Direction dir;
            if (dr is > 0m) { amount = dr.Value; dir = Direction.Out; }
            else if (cr is > 0m) { amount = cr.Value; dir = Direction.In; }
            else if (cAmt >= 0 && Json.ParseMoney(Get(cAmt)) is decimal a && a != 0)
            {
                var raw = Get(cAmt).ToUpperInvariant();
                dir = a < 0 || raw.EndsWith("DR") || raw.EndsWith('-') ? Direction.Out : Direction.In;
                amount = Math.Abs(a);
            }
            else continue;
            result.Add(new RawLine(date, Json.Cut(Get(cDesc), 400), dir, amount, cBal >= 0 ? Json.ParseMoney(Get(cBal)) : null));
        }
        return result;
    }

    static bool IsHeader(string l) =>
        (l.Contains("date") || l.Contains("tarikh")) &&
        new[] { "desc", "keterangan", "details", "particular", "transaction", "butiran" }.Any(l.Contains);

    static List<string> Split(string line, char d)
    {
        var res = new List<string>();
        var cur = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (ch == d && !quoted) { res.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(ch);
        }
        res.Add(cur.ToString());
        return res;
    }
}
