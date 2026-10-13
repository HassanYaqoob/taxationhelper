using Kira.Application.Common;
using Kira.Domain.Common;
using Kira.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kira.Infrastructure.Persistence;

public sealed class KiraDbContext(DbContextOptions<KiraDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();
    public DbSet<BankLine> BankLines => Set<BankLine>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<CategoryRule> CategoryRules => Set<CategoryRule>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<PaymentAccount> PaymentAccounts => Set<PaymentAccount>();
    public DbSet<PersonalTransaction> PersonalTransactions => Set<PersonalTransaction>();
    public DbSet<Receipt> Receipts => Set<Receipt>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<decimal>().HavePrecision(18, 2);
        b.Properties<string>().HaveMaxLength(400);
        // Enums are stored as readable text.
        b.Properties<AccountType>().HaveConversion<string>().HaveMaxLength(20);
        b.Properties<TaxTreatment>().HaveConversion<string>().HaveMaxLength(20);
        b.Properties<MemberRole>().HaveConversion<string>().HaveMaxLength(20);
        b.Properties<Direction>().HaveConversion<string>().HaveMaxLength(10);
        b.Properties<BankLineStatus>().HaveConversion<string>().HaveMaxLength(20);
        b.Properties<PaymentKind>().HaveConversion<string>().HaveMaxLength(20);
        b.Properties<PaymentStatus>().HaveConversion<string>().HaveMaxLength(20);
        b.Properties<PaymentSource>().HaveConversion<string>().HaveMaxLength(20);
        b.Properties<ReceiptStatus>().HaveConversion<string>().HaveMaxLength(20);
        b.Properties<TransactionSource>().HaveConversion<string>().HaveMaxLength(20);
        b.Properties<ReminderRepeat>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KiraDbContext).Assembly);
}
