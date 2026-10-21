using Kira.Application.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kira.Application.Features.Personal;

public sealed record ScreenshotView(string Institution, string Last4, decimal Amount, string? When, string Recipient, string Reference, string Status, bool AmountMatches, string Why);
public sealed record ReceiptDto(Guid Id, ReceiptStatus Status, string Merchant, DateOnly Date, decimal Total, decimal Sst, string ReceiptNo, List<ReceiptItem> Items,
    string Category, decimal Confidence, string Reason, string PrintedPayment, PaymentStatus PaymentStatus, Guid? PaymentAccountId, string? PaymentAccountName,
    PaymentSource PaymentSource, string PaymentNote, decimal PaymentConfidence, string? ReliefCode, string? SuggestedReliefCode, decimal ReliefConfidence,
    string ReliefReason, bool HasImage, bool HasScreenshot, ScreenshotView? Screenshot, Guid? TransactionId, bool StatementMatched);
public sealed record ReceiptUpdate(string? Merchant, DateOnly Date, decimal Total, decimal Sst, string Category);
public sealed record PaymentUpdate(PaymentStatus Status, Guid? AccountId);
public sealed record ReliefUpdate(string? Code, bool Dismiss = false);

/// <summary>
/// Receipts in the personal workspace. AI reads the receipt, suggests a category and a tax relief, and detects the payment method
/// printed on it. A payment screenshot is matched to one of the user's accounts. The user can always change the account manually.
/// </summary>
public sealed class ReceiptService(IAppDbContext db, ICurrentUser me, Extractors ai, IAiClient aiClient, FileStore files, IOptions<KiraRules> rules,
    IClock clock, ILogger<ReceiptService> log)
{
    static readonly string[] ImageOrPdf = ["image/*", FileRules.Pdf];
    const int MaxSections = 12;

    public async Task<List<ReceiptDto>> ListAsync(bool needsAttention, CancellationToken ct)
    {
        var query = db.Receipts.AsNoTracking().Where(r => r.UserId == me.Id);
        if (needsAttention) query = query.Where(r => r.Status != ReceiptStatus.Saved || r.PaymentStatus == PaymentStatus.Unknown);
        var list = await query.OrderByDescending(r => r.Date).ThenByDescending(r => r.CreatedAt).Take(1000).ToListAsync(ct);
        return await ToDtosAsync(list, ct);
    }

    public async Task<ReceiptDto> GetAsync(Guid id, CancellationToken ct) => (await ToDtosAsync([await LoadAsync(id, false, ct)], ct))[0];

    public async Task<FileResult> FileAsync(Guid id, bool screenshot, CancellationToken ct)
    {
        var r = await LoadAsync(id, false, ct);
        var fileId = (screenshot ? r.ScreenshotFileId : r.ImageFileId) ?? throw new NotFoundException("No file.");
        var f = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == fileId && x.UserId == me.Id, ct) ?? throw new NotFoundException();
        return new FileResult(f.Data, f.ContentType, f.FileName);
    }

    /// <summary>
    /// Saves a receipt. The browser sends the cleaned scan (cropped, straightened, long receipts joined) as one image, and
    /// for a long receipt also the same scan cut into readable sections for the AI to read.
    /// Only the scan is kept.
    /// </summary>
    /// <summary>A printable A4 PDF copy of the receipt scan. Receipts uploaded as PDF are returned unchanged.</summary>
    public async Task<FileResult> PdfAsync(Guid id, CancellationToken ct)
    {
        var r = await LoadAsync(id, false, ct);
        var fileId = r.ImageFileId ?? throw new NotFoundException("This receipt has no scan.");
        var f = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == fileId && x.UserId == me.Id, ct) ?? throw new NotFoundException();
        var name = FileRules.SafeName($"Receipt {(string.IsNullOrWhiteSpace(r.Merchant) ? "" : r.Merchant + " ")}{r.Date:yyyy-MM-dd}.pdf");
        if (f.ContentType == FileRules.Pdf) return new FileResult(f.Data, FileRules.Pdf, name);
        if (f.ContentType != "image/jpeg")
            throw new DomainException("This receipt was saved before scanning was added, so there's no PDF copy. Download the image, or scan it again.");
        var footer = string.Join(" | ", new[] { r.Merchant, r.Date.ToString("d MMM yyyy"), r.Total > 0 ? $"RM {r.Total:N2}" : "" }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return new FileResult(ReceiptPdf.FromJpeg(f.Data, footer), FileRules.Pdf, name);
    }

    public async Task<ReceiptDto> UploadAsync(FileUpload upload, CancellationToken ct) => await UploadAsync(upload, [], ct);

    public async Task<ReceiptDto> UploadAsync(FileUpload upload, IReadOnlyList<FileUpload> sections, CancellationToken ct)
    {
        var type = FileRules.Require(upload, ImageOrPdf);
        if (sections.Count > MaxSections) throw new DomainException($"A receipt can have at most {MaxSections} sections.");
        var images = new List<AiAttachment>();
        foreach (var s in sections)
        {
            var t = FileRules.Require(s, ["image/*"]);
            images.Add(new AiAttachment(s.Data, t));
        }
        if (images.Count == 0) images.Add(new AiAttachment(upload.Data, type));

        var file = files.Add(upload, type, null, me.Id);
        var r = new Receipt { UserId = me.Id, ImageFileId = file.Id, Date = clock.Today, Status = ReceiptStatus.Review };
        if (!aiClient.Enabled) r.Reason = "AI is off on this server. Fill in the details.";
        else
        {
            try
            {
                var read = await ai.ReceiptAsync(images, rules.Value.ReliefsFor(clock.Today.Year), ct);
                r.Merchant = read.Merchant; r.Date = read.Date ?? clock.Today; r.Total = read.Total; r.Sst = read.Sst; r.ReceiptNo = read.ReceiptNo;
                r.ItemsJson = JsonSerializer.Serialize(read.Items); r.Category = read.Category; r.Confidence = read.Confidence; r.Reason = read.Reason;
                if (read.ReliefCode != null) { r.SuggestedReliefCode = read.ReliefCode; r.ReliefConfidence = read.ReliefConfidence; r.ReliefReason = read.ReliefReason; }
                await ApplyPrintedPaymentAsync(r, read, ct);
            }
            catch (Exception ex) when (ex is AiUnavailableException or DomainException)
            {
                log.LogWarning(ex, "Receipt reading failed");
                r.Status = ReceiptStatus.Error; r.Reason = ex.Message;
            }
        }
        db.Receipts.Add(r);
        await db.SaveChangesAsync(ct);
        return await GetAsync(r.Id, ct);
    }

    async Task ApplyPrintedPaymentAsync(Receipt r, ReceiptRead read, CancellationToken ct)
    {
        var mine = await db.PaymentAccounts.AsNoTracking().Where(a => a.UserId == me.Id).ToListAsync(ct);
        var label = string.Join(" ", new[] { read.PrintedProvider, read.PrintedLast4.Length > 0 ? "••" + read.PrintedLast4 : "" }.Where(s => !string.IsNullOrWhiteSpace(s)));
        r.PrintedPayment = label.Length > 0 ? label : read.PrintedMethod is "unknown" or "" ? "" : read.PrintedMethod;
        if (read.PrintedMethod == "cash")
        {
            r.PaymentStatus = PaymentStatus.Cash; r.PaymentSource = PaymentSource.Receipt; r.PaymentNote = "\"Cash\" printed on the receipt"; r.PaymentConfidence = 0.9m;
            r.PaymentAccountId = mine.FirstOrDefault(a => a.Kind == PaymentKind.Cash)?.Id;
            return;
        }
        if (read.PrintedLast4.Length >= 3)
        {
            var m = PaymentMatcher.Match(mine, read.PrintedProvider, read.PrintedLast4, null);
            if (m.Account != null && m.Confidence >= 0.9m)
            {
                r.PaymentStatus = PaymentStatus.Account; r.PaymentAccountId = m.Account.Id; r.PaymentSource = PaymentSource.Receipt;
                r.PaymentNote = $"{label} printed on the receipt"; r.PaymentConfidence = m.Confidence;
            }
        }
    }

    public async Task<ReceiptDto> CreateManualAsync(CancellationToken ct)
    {
        var r = new Receipt { UserId = me.Id, Date = clock.Today, Status = ReceiptStatus.Review, Reason = "Entered by you", Confidence = 1 };
        db.Receipts.Add(r);
        await db.SaveChangesAsync(ct);
        return await GetAsync(r.Id, ct);
    }

    public async Task<ReceiptDto> UpdateAsync(Guid id, ReceiptUpdate u, CancellationToken ct)
    {
        var r = await LoadAsync(id, true, ct);
        r.Merchant = Json.Cut((u.Merchant ?? "").Trim(), 200); r.Date = u.Date;
        r.Total = Math.Round(Math.Abs(u.Total), 2); r.Sst = Math.Round(Math.Abs(u.Sst), 2);
        if (PersonalCategories.All.Contains(u.Category)) r.Category = u.Category;
        await SyncTransactionAsync(r, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ReceiptDto> SetPaymentAsync(Guid id, PaymentUpdate u, CancellationToken ct)
    {
        var r = await LoadAsync(id, true, ct);
        r.PaymentStatus = u.Status; r.PaymentSource = PaymentSource.Manual; r.PaymentConfidence = 1;
        switch (u.Status)
        {
            case PaymentStatus.Account:
                var a = await db.PaymentAccounts.FirstOrDefaultAsync(x => x.Id == u.AccountId && x.UserId == me.Id, ct) ?? throw new DomainException("Choose one of your accounts.");
                r.PaymentAccountId = a.Id; r.PaymentNote = $"Chosen by you: {a.Name}";
                break;
            case PaymentStatus.Cash:
                r.PaymentAccountId = (await db.PaymentAccounts.FirstOrDefaultAsync(x => x.UserId == me.Id && x.Kind == PaymentKind.Cash, ct))?.Id;
                r.PaymentNote = "Paid in cash";
                break;
            default:
                r.PaymentAccountId = null; r.PaymentNote = u.Status == PaymentStatus.NotPaid ? "Not paid yet" : "";
                break;
        }
        await SyncTransactionAsync(r, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Reads a payment screenshot (bank, last digits, amount, time) and matches it to one of the user's accounts.</summary>
    public async Task<ReceiptDto> UploadScreenshotAsync(Guid id, FileUpload upload, CancellationToken ct)
    {
        var r = await LoadAsync(id, true, ct);
        var type = FileRules.Require(upload, ImageOrPdf);
        var read = await ai.ScreenshotAsync(upload.Data, type, ct);
        if (r.ScreenshotFileId is Guid old)
        {
            var oldFile = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == old && f.UserId == me.Id, ct);
            if (oldFile != null) db.StoredFiles.Remove(oldFile);
        }
        r.ScreenshotFileId = files.Add(upload, type, null, me.Id).Id;
        var mine = await db.PaymentAccounts.AsNoTracking().Where(a => a.UserId == me.Id).ToListAsync(ct);
        var m = PaymentMatcher.Match(mine, read.Institution, read.Last4, read.AccountType);
        r.ScreenshotJson = JsonSerializer.Serialize(new
        {
            read.Institution, read.AccountType, read.Last4, read.Amount, When = read.When?.ToString("yyyy-MM-ddTHH:mm"),
            read.Recipient, read.Reference, read.Status, Why = m.Why
        });
        r.PaymentSource = PaymentSource.Screenshot;
        r.PaymentConfidence = m.Confidence;
        r.PaymentNote = m.Why;
        if (m.Account != null) { r.PaymentStatus = PaymentStatus.Account; r.PaymentAccountId = m.Account.Id; }
        if (r.Total == 0 && read.Amount > 0) r.Total = read.Amount;
        await SyncTransactionAsync(r, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ReceiptDto> SetReliefAsync(Guid id, ReliefUpdate u, CancellationToken ct)
    {
        var r = await LoadAsync(id, true, ct);
        if (u.Dismiss) { r.ReliefCode = null; r.ReliefDismissed = true; }
        else
        {
            if (u.Code != null && !rules.Value.ReliefsFor(r.Date.Year).Items.Any(i => i.Code == u.Code)) throw new DomainException("Unknown relief.");
            r.ReliefCode = u.Code; r.ReliefDismissed = u.Code == null;
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Saves the receipt to the user's records: creates a transaction, or links to a matching statement line already imported.</summary>
    public async Task<ReceiptDto> SaveAsync(Guid id, CancellationToken ct)
    {
        var r = await LoadAsync(id, true, ct);
        if (r.Total <= 0) throw new DomainException("Enter the total first.");
        if (string.IsNullOrWhiteSpace(r.Merchant)) throw new DomainException("Enter the merchant name.");
        r.Status = ReceiptStatus.Saved;
        await SyncTransactionAsync(r, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var r = await LoadAsync(id, true, ct);
        if (r.TransactionId is Guid tid)
        {
            var t = await db.PersonalTransactions.FirstOrDefaultAsync(x => x.Id == tid && x.UserId == me.Id, ct);
            if (t != null)
            {
                if (t.Source == TransactionSource.Receipt) db.PersonalTransactions.Remove(t);
                else { t.ReceiptId = null; }
            }
        }
        var fileIds = new[] { r.ImageFileId, r.ScreenshotFileId }.Where(x => x != null).Select(x => x!.Value).ToList();
        var stored = await db.StoredFiles.Where(f => fileIds.Contains(f.Id) && f.UserId == me.Id).ToListAsync(ct);
        db.StoredFiles.RemoveRange(stored);
        db.Receipts.Remove(r);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Keeps the receipt's transaction in step with the receipt once it has been saved.</summary>
    async Task SyncTransactionAsync(Receipt r, CancellationToken ct)
    {
        if (r.Status != ReceiptStatus.Saved) return;
        var t = r.TransactionId is Guid tid ? await db.PersonalTransactions.FirstOrDefaultAsync(x => x.Id == tid && x.UserId == me.Id, ct) : null;
        if (r.PaymentStatus == PaymentStatus.NotPaid)
        {
            if (t is { Source: TransactionSource.Receipt }) { db.PersonalTransactions.Remove(t); r.TransactionId = null; }
            return;
        }
        if (t == null && r.PaymentAccountId != null)
        {
            // A statement line for this payment may already be imported: link to it instead of adding a second one.
            var candidates = await db.PersonalTransactions.Where(x => x.UserId == me.Id && x.PaymentAccountId == r.PaymentAccountId
                && x.Source == TransactionSource.Statement && x.ReceiptId == null && x.Amount == r.Total).ToListAsync(ct);
            t = candidates.FirstOrDefault(x => x.IsSamePayment(r.PaymentAccountId, r.Total, r.Date));
            if (t != null) { t.ReceiptId = r.Id; t.StatementMatched = true; t.Category = r.Category; t.Reviewed = true; r.TransactionId = t.Id; return; }
        }
        if (t == null)
        {
            t = new PersonalTransaction { UserId = me.Id, Source = TransactionSource.Receipt, ReceiptId = r.Id, Direction = Direction.Out };
            db.PersonalTransactions.Add(t);
            r.TransactionId = t.Id;
        }
        if (t.Source == TransactionSource.Receipt)
        {
            t.PaymentAccountId = r.PaymentAccountId; t.Date = r.Date; t.Amount = r.Total; t.Description = Json.Cut(r.Merchant.ToUpperInvariant(), 300);
        }
        t.Category = r.Category; t.IsTransfer = false; t.Reviewed = true; t.Confidence = 1;
    }

    async Task<Receipt> LoadAsync(Guid id, bool tracked, CancellationToken ct)
    {
        var q = tracked ? db.Receipts : db.Receipts.AsNoTracking();
        return await q.FirstOrDefaultAsync(x => x.Id == id && x.UserId == me.Id, ct) ?? throw new NotFoundException("Receipt not found.");
    }

    internal async Task<List<ReceiptDto>> ToDtosAsync(List<Receipt> list, CancellationToken ct)
    {
        var names = await db.PaymentAccounts.AsNoTracking().Where(a => a.UserId == me.Id).ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var txIds = list.Where(r => r.TransactionId != null).Select(r => r.TransactionId!.Value).ToList();
        var matched = (await db.PersonalTransactions.AsNoTracking().Where(t => txIds.Contains(t.Id) && t.StatementMatched).Select(t => t.Id).ToListAsync(ct)).ToHashSet();
        return list.Select(r => new ReceiptDto(r.Id, r.Status, r.Merchant, r.Date, r.Total, r.Sst, r.ReceiptNo, Items(r.ItemsJson), r.Category, r.Confidence, r.Reason,
            r.PrintedPayment, r.PaymentStatus, r.PaymentAccountId, r.PaymentAccountId is Guid g ? names.GetValueOrDefault(g) : null, r.PaymentSource, r.PaymentNote,
            r.PaymentConfidence, r.ReliefCode, r.ReliefDismissed ? null : r.SuggestedReliefCode, r.ReliefConfidence, r.ReliefReason,
            r.ImageFileId != null, r.ScreenshotFileId != null, Screenshot(r), r.TransactionId, r.TransactionId is Guid t && matched.Contains(t))).ToList();
    }

    static List<ReceiptItem> Items(string json)
    {
        try { return JsonSerializer.Deserialize<List<ReceiptItem>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    static ScreenshotView? Screenshot(Receipt r)
    {
        if (string.IsNullOrEmpty(r.ScreenshotJson)) return null;
        try
        {
            var j = JsonDocument.Parse(r.ScreenshotJson).RootElement;
            var amount = j.Dec("Amount");
            var when = j.Str("When");
            return new ScreenshotView(j.Str("Institution"), j.Str("Last4"), amount, when.Length > 0 ? when : null, j.Str("Recipient"), j.Str("Reference"),
                j.Str("Status"), amount > 0 && Math.Abs(amount - r.Total) < 0.01m, j.Str("Why"));
        }
        catch (JsonException) { return null; }
    }
}
