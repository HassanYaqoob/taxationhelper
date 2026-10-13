using System.Globalization;
using System.Text;

namespace Kira.Application.Ai;

public sealed record ExtractedLine(DateOnly Date, string Description, Direction Direction, decimal Amount, decimal? Balance,
    string Category, decimal Confidence, string Reason, IReadOnlyList<string> Flags);

public sealed record ReceiptItem(string Description, decimal Amount);

public sealed record ReceiptRead(string Merchant, DateOnly? Date, decimal Total, decimal Sst, string ReceiptNo, List<ReceiptItem> Items,
    string Category, decimal Confidence, string Reason, string PrintedMethod, string PrintedLast4, string PrintedProvider,
    string? ReliefCode, decimal ReliefConfidence, string ReliefReason);

public sealed record ScreenshotRead(string Institution, string AccountType, string Last4, decimal Amount, DateTime? When,
    string Recipient, string Reference, string Status);

public sealed record RecheckResult(Guid Id, string Category, decimal Confidence, string Reason);

/// <summary>All AI prompts live here, so they can be reviewed and tuned in one place.</summary>
public sealed class Extractors(IAiClient ai, IClock clock)
{
    const string MalayHelp =
        "Descriptions may be English or Malay: BAYARAN/PEMBAYARAN = payment, PINDAHAN = transfer, CAJ = charge, " +
        "KELUARAN TUNAI / CASH WDL = cash withdrawal, SIMPANAN/DEPOSIT = deposit, HIBAH or PROFIT = profit on an Islamic account (like interest), " +
        "KWSP = EPF, PERKESO = SOCSO, LHDN/HASiL = Inland Revenue, TNB = electricity, JomPAY = bill payment.";

    async Task<JsonElement> AskJsonAsync(string prompt, IReadOnlyList<AiAttachment>? files, CancellationToken ct) =>
        Json.ParseReply(await ai.CompleteAsync(new AiRequest(prompt + "\n\nReply with only the JSON. No markdown, no explanation.", files), ct));

    public void EnsureEnabled()
    {
        if (!ai.Enabled) throw new AiUnavailableException("AI isn't set up on this server. Add an Anthropic API key (Anthropic:ApiKey) and restart.");
    }

    /// <summary>Reads every transaction from a statement and suggests a category for each.</summary>
    public async Task<List<ExtractedLine>> StatementAsync(StatementContent input, string owner, string categories,
        IReadOnlyDictionary<string, string> rules, string flagsHelp, string extra, CancellationToken ct)
    {
        EnsureEnabled();
        var results = new List<ExtractedLine>();
        string? period = null;
        if (input.Text != null)
        {
            foreach (var chunk in Chunk(input.Text))
            {
                var json = await AskJsonAsync(StatementPrompt(owner, categories, rules, flagsHelp, extra, period, $"Statement text:\n\"\"\"\n{chunk}\n\"\"\""), null, ct);
                var p = json.Str("period");
                if (period == null && p.Length > 0) period = p;
                results.AddRange(ParseLines(json));
            }
        }
        else
        {
            var json = await AskJsonAsync(StatementPrompt(owner, categories, rules, flagsHelp, extra, null, "The statement is attached."), input.Attachments, ct);
            results.AddRange(ParseLines(json));
        }
        return results;
    }

    static IEnumerable<string> Chunk(string text, int size = 8000)
    {
        var cur = new StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            if (cur.Length + line.Length > size && cur.Length > 0) { yield return cur.ToString(); cur.Clear(); }
            cur.AppendLine(line.TrimEnd('\r'));
        }
        if (cur.ToString().Trim().Length > 0) yield return cur.ToString();
    }

    static string StatementPrompt(string owner, string categories, IReadOnlyDictionary<string, string> rules, string flagsHelp, string extra, string? period, string tail)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are an experienced Malaysian bookkeeper reading a bank, card or e-wallet statement for {owner}.");
        sb.AppendLine("Extract every transaction. Skip opening and closing balances, brought/carried forward lines, totals, headers, footers and adverts. Never invent transactions.");
        sb.AppendLine(MalayHelp);
        sb.AppendLine("For each transaction return:");
        sb.AppendLine($"- date: YYYY-MM-DD. Malaysian statements put the day before the month. If the year is missing use the statement period{(period != null ? $" ({period})" : "")}.");
        sb.AppendLine("- description: the full text, including continuation lines (payee name, reference), joined with spaces.");
        sb.AppendLine("- amount: a positive number without currency or commas.");
        sb.AppendLine("- direction: \"in\" for credits/deposits, \"out\" for debits/withdrawals. Check against the balance column. On a credit card statement purchases are \"out\" and payments to the card are \"in\".");
        sb.AppendLine("- balance: the balance after the line, or null.");
        sb.AppendLine("- category: exactly one id from the category list.");
        sb.AppendLine("- confidence: 0 to 1. Below 0.7 when the purpose is unclear, e.g. a transfer to a person's name with no reference.");
        sb.AppendLine("- reason: under 12 words explaining the category.");
        sb.AppendLine($"- flags: an array that may contain {flagsHelp}. Empty array if none apply.");
        if (extra.Length > 0) sb.AppendLine(extra);
        sb.AppendLine("Categories:");
        sb.AppendLine(categories);
        if (rules.Count > 0)
        {
            sb.AppendLine("The owner has set these rules (description keyword → category). Follow them:");
            foreach (var r in rules.Take(80)) sb.AppendLine($"- \"{r.Key}\" → {r.Value}");
        }
        sb.AppendLine("JSON shape: {\"period\":\"YYYY-MM-DD to YYYY-MM-DD or null\",\"transactions\":[{\"date\":\"2026-09-05\",\"description\":\"...\",\"amount\":120.5,\"direction\":\"out\",\"balance\":5400.2,\"category\":\"...\",\"confidence\":0.9,\"reason\":\"...\",\"flags\":[]}]}");
        sb.AppendLine("If there are no transactions return {\"period\":null,\"transactions\":[]}.");
        sb.AppendLine(tail);
        return sb.ToString();
    }

    IEnumerable<ExtractedLine> ParseLines(JsonElement json)
    {
        foreach (var t in json.Arr("transactions"))
        {
            var amount = Math.Abs(t.Dec("amount"));
            if (amount == 0) continue;
            var reason = t.Str("reason");
            var conf = Json.Clamp01(t.Dec("confidence", 0.5m));
            var date = t.Date("date");
            if (date == null) { date = clock.Today; reason = "Date unclear, please check. " + reason; conf = Math.Min(conf, 0.4m); }
            decimal? balance = t.TryGetProperty("balance", out var bv) && bv.ValueKind is JsonValueKind.Number or JsonValueKind.String ? t.Dec("balance") : null;
            var flags = t.Arr("flags").Where(f => f.ValueKind == JsonValueKind.String).Select(f => f.GetString() ?? "").Where(f => f.Length > 0).ToList();
            var desc = string.Join(' ', t.Str("description").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            yield return new ExtractedLine(date.Value, Json.Cut(desc, 400),
                t.Str("direction").Equals("in", StringComparison.OrdinalIgnoreCase) ? Direction.In : Direction.Out,
                Math.Round(amount, 2), balance, t.Str("category"), conf, Json.Cut(reason, 200), flags);
        }
    }

    public async Task<List<RecheckResult>> RecheckAsync(string owner, string categories,
        IEnumerable<BankLine> lines, CancellationToken ct)
    {
        EnsureEnabled();
        var payload = JsonSerializer.Serialize(lines.Select(l => new
        {
            id = l.Id, date = l.Date.ToString("yyyy-MM-dd"), description = l.Description, amount = l.Amount, direction = l.Direction.ToString().ToLowerInvariant()
        }));
        var json = await AskJsonAsync($$"""
            You are an experienced Malaysian bookkeeper for {{owner}}. Look again at these bank lines that were hard to categorise.
            {{MalayHelp}}
            Think about common Malaysian payees and what this business is likely to pay for. If a line is still genuinely unclear
            (for example a transfer to a person's name with no reference), keep confidence below 0.7 and say what would settle it.
            Categories:
            {{categories}}
            Lines: {{payload}}
            JSON shape: {"results":[{"id":"...","category":"account code","confidence":0.0,"reason":"under 15 words"}]}
            """, null, ct);
        return json.Arr("results")
            .Where(r => Guid.TryParse(r.Str("id"), out _))
            .Select(r => new RecheckResult(Guid.Parse(r.Str("id")), r.Str("category"), Json.Clamp01(r.Dec("confidence", 0.5m)), Json.Cut(r.Str("reason"), 200)))
            .ToList();
    }

    public async Task<ReceiptRead> ReceiptAsync(byte[] data, string contentType, ReliefYear reliefs, CancellationToken ct)
    {
        EnsureEnabled();
        var reliefList = string.Join("\n", reliefs.Items.Select(r => $"- {r.Code}: {r.Name} ({r.Hint})"));
        var json = await AskJsonAsync($$$"""
            Read this receipt or invoice for a person's personal records in Malaysia.
            JSON shape:
            {"merchant":"","date":"YYYY-MM-DD","total":0,"sst":0,"receiptNo":"","items":[{"description":"","amount":0}],
             "category":"one of: {{{string.Join(", ", PersonalCategories.All)}}}","confidence":0.0,"reason":"under 12 words",
             "printedPayment":{"method":"cash|card|ewallet|qr|transfer|unknown","last4":"digits or null","provider":"e.g. VISA, Mastercard, Touch 'n Go, DuitNow QR, or null"},
             "relief":{"code":"a relief code below, or null","confidence":0.0,"reason":"under 12 words"}}
            Rules: dates are day-first. total is the amount actually paid. sst is the SST/service tax if shown, else 0.
            Only suggest a relief when the purchase plausibly qualifies for this Malaysian personal tax relief (YA {{{reliefs.YearOfAssessment}}}):
            {{{reliefList}}}
            """, [new AiAttachment(data, contentType)], ct);
        var pp = json.Obj("printedPayment");
        var rel = json.Obj("relief");
        var relCode = rel.Str("code");
        var category = json.Str("category");
        return new ReceiptRead(
            Json.Cut(json.Str("merchant", "Unknown merchant"), 200), json.Date("date"),
            Math.Round(Math.Abs(json.Dec("total")), 2), Math.Round(Math.Abs(json.Dec("sst")), 2), Json.Cut(json.Str("receiptNo"), 60),
            json.Arr("items").Take(30).Select(i => new ReceiptItem(Json.Cut(i.Str("description"), 120), Math.Round(i.Dec("amount"), 2))).ToList(),
            PersonalCategories.All.Contains(category) ? category : "Other", Json.Clamp01(json.Dec("confidence", 0.5m)), Json.Cut(json.Str("reason"), 200),
            pp.Str("method", "unknown"), PaymentMatcher.Digits(pp.Str("last4")), Json.Cut(pp.Str("provider"), 40),
            reliefs.Items.Any(r => r.Code == relCode) ? relCode : null, Json.Clamp01(rel.Dec("confidence")), Json.Cut(rel.Str("reason"), 200));
    }

    public async Task<ScreenshotRead> ScreenshotAsync(byte[] data, string contentType, CancellationToken ct)
    {
        EnsureEnabled();
        var json = await AskJsonAsync("""
            This is a screenshot of a payment confirmation: a Malaysian banking app transfer or DuitNow screen, an e-wallet payment,
            a card terminal slip, or an online payment receipt.
            JSON shape:
            {"institution":"bank or e-wallet name, e.g. Maybank, CIMB, Public Bank, RHB, Hong Leong, Touch 'n Go, GrabPay, ShopeePay, Boost",
             "accountType":"bank|credit_card|debit_card|ewallet|unknown",
             "last4":"last 4 digits of the paying account or card if visible, else null",
             "amount":0,"dateTime":"YYYY-MM-DDTHH:mm or null","recipient":"","reference":"",
             "status":"successful|failed|pending|unknown"}
            """, [new AiAttachment(data, contentType)], ct);
        DateTime? when = DateTime.TryParse(json.Str("dateTime"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var w) ? w : null;
        return new ScreenshotRead(Json.Cut(json.Str("institution"), 60), json.Str("accountType", "unknown"), PaymentMatcher.Digits(json.Str("last4")),
            Math.Round(Math.Abs(json.Dec("amount")), 2), when, Json.Cut(json.Str("recipient"), 120), Json.Cut(json.Str("reference"), 80), json.Str("status", "unknown"));
    }

    public Task<string> AnswerAsync(string context, string question, CancellationToken ct)
    {
        EnsureEnabled();
        return ai.CompleteAsync(new AiRequest($"Records (the user's own data):\n{context}\n\nQuestion: {question}", null,
            "You are Kira, a friendly, practical assistant for Malaysian small business owners and individuals. Answer using only the records given. " +
            "Be concrete with numbers (RM), use plain language, stay under 220 words, no markdown headings. If the question touches tax law, give general " +
            "information and suggest confirming with a licensed tax agent. If the records don't contain the answer, say so.", 1500), ct);
    }
}
