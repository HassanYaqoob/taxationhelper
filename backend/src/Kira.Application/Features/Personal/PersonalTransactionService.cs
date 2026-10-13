using Kira.Application.Ai;

namespace Kira.Application.Features.Personal;

public sealed record PTxDto(Guid Id, Guid? AccountId, string? AccountName, DateOnly Date, string Description, Direction Direction, decimal Amount, string Category,
    decimal Confidence, string Reason, bool Reviewed, bool IsTransfer, TransactionSource Source, Guid? ReceiptId, bool StatementMatched);
public sealed record PTxInput(Guid? AccountId, DateOnly Date, string? Description, Direction Direction, decimal Amount, string Category);
public sealed record PTxUpdate(string? Category, bool? IsTransfer, bool Remember = true);
public sealed record SimilarResult(int Similar, string Pattern);
public sealed record PImportResult(int Added, int Duplicates, int MatchedReceipts, int Transfers, bool UsedAi, string Message);

public sealed class PersonalTransactionService(IAppDbContext db, ICurrentUser me, Extractors ai, IAiClient aiClient, IStatementReader reader, RuleService rules)
{
    public async Task<List<PTxDto>> ListAsync(Guid? accountId, string? month, bool review, string? q, CancellationToken ct)
    {
        var query = db.PersonalTransactions.AsNoTracking().Where(t => t.UserId == me.Id);
        if (accountId != null) query = query.Where(t => t.PaymentAccountId == accountId);
        if (review) query = query.Where(t => !t.Reviewed);
        if (month != null && DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out var m0))
        {
            var m1 = m0.AddMonths(1);
            query = query.Where(t => t.Date >= m0 && t.Date < m1);
        }
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(t => t.Description.Contains(q.Trim()));
        var list = await query.OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt).Take(2000).ToListAsync(ct);
        var names = await db.PaymentAccounts.AsNoTracking().Where(a => a.UserId == me.Id).ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        return list.Select(t => new PTxDto(t.Id, t.PaymentAccountId, t.PaymentAccountId is Guid g ? names.GetValueOrDefault(g) : null, t.Date, t.Description,
            t.Direction, t.Amount, t.Category, t.Confidence, t.Reason, t.Reviewed, t.IsTransfer, t.Source, t.ReceiptId, t.StatementMatched)).ToList();
    }

    public async Task<Guid> AddAsync(PTxInput i, CancellationToken ct)
    {
        if (i.Amount <= 0) throw new DomainException("Enter an amount.");
        if (i.AccountId != null && !await db.PaymentAccounts.AnyAsync(a => a.Id == i.AccountId && a.UserId == me.Id, ct)) throw new NotFoundException("Account not found.");
        var cat = PersonalCategories.All.Contains(i.Category) ? i.Category : "Other";
        var t = new PersonalTransaction
        {
            UserId = me.Id, PaymentAccountId = i.AccountId, Date = i.Date, Description = Json.Cut((i.Description ?? "").Trim(), 300), Direction = i.Direction,
            Amount = Math.Round(i.Amount, 2), Category = cat, IsTransfer = cat == PersonalCategories.Transfers, Source = TransactionSource.Manual
        };
        db.PersonalTransactions.Add(t);
        await db.SaveChangesAsync(ct);
        return t.Id;
    }

    public async Task<SimilarResult> UpdateAsync(Guid id, PTxUpdate u, CancellationToken ct)
    {
        var t = await Mine(id, ct);
        var similar = 0; var key = "";
        if (u.Category != null)
        {
            if (!PersonalCategories.All.Contains(u.Category)) throw new DomainException("Unknown category.");
            t.Category = u.Category;
            t.IsTransfer = u.Category == PersonalCategories.Transfers;
            if (u.Remember)
            {
                key = await rules.LearnAsync(null, me.Id, t.Description, u.Category, ct);
                if (key.Length > 0) similar = (await SimilarAsync(t, ct)).Count;
            }
        }
        if (u.IsTransfer is bool tr) { t.IsTransfer = tr; if (tr) t.Category = PersonalCategories.Transfers; }
        t.Reviewed = true; t.Confidence = 1; t.Reason = "Checked by you";
        await db.SaveChangesAsync(ct);
        return new SimilarResult(similar, key);
    }

    public async Task<int> ApplySimilarAsync(Guid id, CancellationToken ct)
    {
        var t = await Mine(id, ct);
        var list = await SimilarAsync(t, ct);
        foreach (var o in list) { o.Category = t.Category; o.IsTransfer = t.IsTransfer; o.Reviewed = true; o.Confidence = 1; o.Reason = "Matched your rule"; }
        await db.SaveChangesAsync(ct);
        return list.Count;
    }

    async Task<List<PersonalTransaction>> SimilarAsync(PersonalTransaction t, CancellationToken ct)
    {
        var key = MerchantKey.From(t.Description);
        if (key.Length == 0) return [];
        var others = await db.PersonalTransactions.Where(x => x.UserId == me.Id && x.Id != t.Id && x.Direction == t.Direction && x.Category != t.Category).ToListAsync(ct);
        return others.Where(x => MerchantKey.From(x.Description) == key).ToList();
    }

    public async Task ConfirmAsync(Guid id, CancellationToken ct)
    {
        var t = await Mine(id, ct);
        t.Reviewed = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var t = await Mine(id, ct);
        if (t.ReceiptId != null)
        {
            var r = await db.Receipts.FirstOrDefaultAsync(x => x.Id == t.ReceiptId && x.UserId == me.Id, ct);
            if (r != null) { r.TransactionId = null; r.Status = ReceiptStatus.Review; }
        }
        db.PersonalTransactions.Remove(t);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Imports a statement for one of the user's accounts. A line matching a receipt already saved (same account, same amount,
    /// within 3 days) confirms that receipt instead of being counted twice. Moves between the user's own accounts
    /// (e-wallet reloads, card bill payments) are marked as transfers.
    /// </summary>
    public async Task<PImportResult> ImportAsync(Guid accountId, FileUpload file, CancellationToken ct)
    {
        var account = await db.PaymentAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId && a.UserId == me.Id, ct) ?? throw new NotFoundException("Choose one of your accounts.");
        var mine = await db.PaymentAccounts.AsNoTracking().Where(a => a.UserId == me.Id).ToListAsync(ct);
        var ruleMap = await rules.GetAsync(null, me.Id, ct);
        var input = reader.Read(file);

        List<ExtractedLine> found; bool usedAi;
        if (aiClient.Enabled)
        {
            var own = string.Join("; ", mine.Where(a => a.Id != account.Id && a.Kind != PaymentKind.Cash)
                .Select(a => $"{a.Name} ({a.Kind}, {a.Institution}{(a.Last4.Length > 0 ? $" ••{a.Last4}" : "")})"));
            var extra = $"The person's own other accounts are: {(own.Length > 0 ? own : "none listed")}. Money moving between their own accounts " +
                        "(e-wallet reloads such as Touch 'n Go or GrabPay, credit card bill payments, transfers to their own savings) must use category \"Transfers\" and the flag \"own_transfer\".";
            found = await ai.StatementAsync(input, $"a person in Malaysia (statement for their {account.Name}, {account.Kind})",
                string.Join("\n", PersonalCategories.All.Select(c => $"- {c}")), ruleMap, "\"own_transfer\"", extra, ct);
            usedAi = true;
        }
        else
        {
            if (input.Text == null) throw new AiUnavailableException("Reading PDFs and images needs AI. Add the Anthropic API key, or upload a CSV export.");
            found = StatementCsv.Parse(input.Text).Select(r => new ExtractedLine(r.Date, r.Description, r.Direction, r.Amount, r.Balance, "Other", 0.3m, "Not categorised (AI is off)", [])).ToList();
            usedAi = false;
        }

        var existing = await db.PersonalTransactions.Where(t => t.UserId == me.Id && t.PaymentAccountId == account.Id).ToListAsync(ct);
        var batch = Guid.NewGuid().ToString("N")[..8];
        int added = 0, duplicates = 0, matched = 0, transfers = 0;
        foreach (var l in found)
        {
            if (existing.Any(t => t.Source == TransactionSource.Statement && t.Date == l.Date && t.Amount == l.Amount && t.Direction == l.Direction &&
                                  string.Equals(t.Description, l.Description, StringComparison.OrdinalIgnoreCase)))
            { duplicates++; continue; }
            var receiptTx = l.Direction == Direction.Out
                ? existing.FirstOrDefault(t => t.Source == TransactionSource.Receipt && !t.StatementMatched && t.IsSamePayment(account.Id, l.Amount, l.Date))
                : null;
            if (receiptTx != null) { receiptTx.StatementMatched = true; matched++; continue; }

            var cat = PersonalCategories.All.Contains(l.Category) ? l.Category : "Other";
            var conf = l.Confidence; var reason = l.Reason;
            var key = MerchantKey.From(l.Description);
            if (key.Length > 0 && ruleMap.TryGetValue(key, out var ruled) && PersonalCategories.All.Contains(ruled)) { cat = ruled; conf = 1; reason = "Matched your rule"; }
            var isTransfer = cat == PersonalCategories.Transfers || l.Flags.Contains(LineFlags.OwnTransfer);
            if (isTransfer) { cat = PersonalCategories.Transfers; transfers++; }
            var t = new PersonalTransaction
            {
                UserId = me.Id, PaymentAccountId = account.Id, Date = l.Date, Description = l.Description, Direction = l.Direction, Amount = l.Amount,
                Category = cat, Confidence = conf, Reason = reason, Reviewed = conf >= 0.7m, IsTransfer = isTransfer, Source = TransactionSource.Statement, ImportBatch = batch
            };
            db.PersonalTransactions.Add(t);
            existing.Add(t);
            added++;
        }
        await db.SaveChangesAsync(ct);
        var msg = found.Count == 0 ? "No transactions found. Check it's a statement for this account."
            : $"{added} added" + (matched > 0 ? $", {matched} matched to receipts you saved" : "") + (transfers > 0 ? $", {transfers} transfers between your accounts" : "") +
              (duplicates > 0 ? $", {duplicates} already imported" : "") + ".";
        return new PImportResult(added, duplicates, matched, transfers, usedAi, msg);
    }

    async Task<PersonalTransaction> Mine(Guid id, CancellationToken ct) =>
        await db.PersonalTransactions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == me.Id, ct) ?? throw new NotFoundException();
}
