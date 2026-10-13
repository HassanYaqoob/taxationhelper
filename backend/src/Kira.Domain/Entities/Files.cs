using Kira.Domain.Common;

namespace Kira.Domain.Entities;

/// <summary>Binary content kept apart from business rows so lists stay fast.</summary>
public class StoredFile : Entity
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public byte[] Data { get; set; } = [];
    /// <summary>Owner scope: exactly one of these is set.</summary>
    public Guid? CompanyId { get; set; }
    public Guid? UserId { get; set; }
}

/// <summary>Supporting document (bill, invoice, e-invoice, licence) attached to company records.</summary>
public class Document : Entity
{
    public Guid CompanyId { get; set; }
    public Guid FileId { get; set; }
    public StoredFile? File { get; set; }
    public Guid? BankLineId { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string Note { get; set; } = "";
    public Guid UploadedById { get; set; }
}
