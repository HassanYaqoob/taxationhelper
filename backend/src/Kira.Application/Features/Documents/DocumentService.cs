namespace Kira.Application.Features.Documents;

public sealed record DocumentDto(Guid Id, string FileName, string ContentType, long Size, string Note, Guid? BankLineId, Guid? JournalEntryId, string? LinkedTo, DateTime UploadedAt);

public sealed class DocumentService(IAppDbContext db, AccessService access, AuditService audit, FileStore files, ICurrentUser me)
{
    static readonly string[] Allowed = [FileRules.Pdf, "image/*", FileRules.Xlsx, "text/csv", "text/plain"];

    public async Task<List<DocumentDto>> ListAsync(Guid companyId, Guid? bankLineId, Guid? journalEntryId, CancellationToken ct)
    {
        await access.ViewAsync(companyId, ct);
        var q = db.Documents.AsNoTracking().Where(d => d.CompanyId == companyId);
        if (bankLineId != null) q = q.Where(d => d.BankLineId == bankLineId);
        if (journalEntryId != null) q = q.Where(d => d.JournalEntryId == journalEntryId);
        var docs = await q.OrderByDescending(d => d.CreatedAt)
            .Select(d => new { d.Id, d.File!.FileName, d.File.ContentType, d.File.Size, d.Note, d.BankLineId, d.JournalEntryId, d.CreatedAt })
            .Take(1000).ToListAsync(ct);
        var lineIds = docs.Where(d => d.BankLineId != null).Select(d => d.BankLineId!.Value).Distinct().ToList();
        var lines = await db.BankLines.AsNoTracking().Where(l => lineIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => $"{l.Date:dd MMM yyyy} · {l.Description}", ct);
        return docs.Select(d => new DocumentDto(d.Id, d.FileName, d.ContentType, d.Size, d.Note, d.BankLineId, d.JournalEntryId,
            d.BankLineId is Guid b && lines.TryGetValue(b, out var t) ? t : null, d.CreatedAt)).ToList();
    }

    public async Task<DocumentDto> UploadAsync(Guid companyId, FileUpload upload, Guid? bankLineId, Guid? journalEntryId, string? note, CancellationToken ct)
    {
        await access.EditAsync(companyId, ct);
        var type = FileRules.Require(upload, Allowed);
        if (bankLineId != null && !await db.BankLines.AnyAsync(l => l.Id == bankLineId && l.CompanyId == companyId, ct)) throw new NotFoundException("Bank line not found.");
        if (journalEntryId != null && !await db.JournalEntries.AnyAsync(e => e.Id == journalEntryId && e.CompanyId == companyId, ct)) throw new NotFoundException("Entry not found.");
        if (bankLineId != null && journalEntryId == null)
            journalEntryId = await db.BankLines.Where(l => l.Id == bankLineId).Select(l => l.JournalEntryId).FirstOrDefaultAsync(ct);
        var file = files.Add(upload, type, companyId, null);
        var d = new Document { CompanyId = companyId, FileId = file.Id, BankLineId = bankLineId, JournalEntryId = journalEntryId, Note = Json.Cut(note, 500), UploadedById = me.Id };
        db.Documents.Add(d);
        audit.Log(companyId, "Uploaded document", file.FileName);
        await db.SaveChangesAsync(ct);
        return new DocumentDto(d.Id, file.FileName, type, file.Size, d.Note, d.BankLineId, d.JournalEntryId, null, d.CreatedAt);
    }

    public async Task<FileResult> DownloadAsync(Guid companyId, Guid id, CancellationToken ct)
    {
        await access.ViewAsync(companyId, ct);
        var f = await db.Documents.AsNoTracking().Where(d => d.Id == id && d.CompanyId == companyId)
            .Select(d => new { d.File!.Data, d.File.ContentType, d.File.FileName }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException();
        return new FileResult(f.Data, f.ContentType, f.FileName);
    }

    public async Task DeleteAsync(Guid companyId, Guid id, CancellationToken ct)
    {
        await access.EditAsync(companyId, ct);
        var d = await db.Documents.Include(x => x.File).FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct) ?? throw new NotFoundException();
        db.Documents.Remove(d);
        if (d.File != null) db.StoredFiles.Remove(d.File);
        audit.Log(companyId, "Deleted document", d.File?.FileName ?? "");
        await db.SaveChangesAsync(ct);
    }
}
