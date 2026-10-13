namespace Kira.Application.Common;

/// <summary>Database access. Implemented with EF Core and SQL Server in the infrastructure layer.</summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Company> Companies { get; }
    DbSet<Membership> Memberships { get; }
    DbSet<Account> Accounts { get; }
    DbSet<JournalEntry> JournalEntries { get; }
    DbSet<JournalLine> JournalLines { get; }
    DbSet<BankLine> BankLines { get; }
    DbSet<StoredFile> StoredFiles { get; }
    DbSet<Document> Documents { get; }
    DbSet<CategoryRule> CategoryRules { get; }
    DbSet<Reminder> Reminders { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<PaymentAccount> PaymentAccounts { get; }
    DbSet<PersonalTransaction> PersonalTransactions { get; }
    DbSet<Receipt> Receipts { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>The signed-in user making the request.</summary>
public interface ICurrentUser
{
    Guid Id { get; }
    string IpAddress { get; }
    string UserAgent { get; }
}

/// <summary>Business dates are in Malaysia time regardless of the server's time zone.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
    DateOnly Today { get; }
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) CreateAccessToken(User user);
}

public sealed record AiAttachment(byte[] Data, string MediaType);
public sealed record AiRequest(string Prompt, IReadOnlyList<AiAttachment>? Attachments = null, string? System = null, int? MaxTokens = null);

/// <summary>Large language model used to read statements, receipts and screenshots and to answer questions.</summary>
public interface IAiClient
{
    bool Enabled { get; }
    Task<string> CompleteAsync(AiRequest request, CancellationToken ct);
}

/// <summary>Either extracted text (CSV, Excel, text) or the original file for the AI to read (PDF, image).</summary>
public sealed record StatementContent(string? Text, IReadOnlyList<AiAttachment> Attachments);

public interface IStatementReader
{
    StatementContent Read(FileUpload file);
}

public sealed record Sheet(string Name, IReadOnlyList<object?[]> Rows, double[] Widths, int[] HeaderRows);

public interface IExcelWriter
{
    byte[] Build(IEnumerable<Sheet> sheets);
}

/// <summary>An uploaded file, already read into memory and size-checked by the API layer.</summary>
public sealed record FileUpload(string FileName, string ContentType, byte[] Data);

/// <summary>A file to send back to the browser.</summary>
public sealed record FileResult(byte[] Data, string ContentType, string FileName);
