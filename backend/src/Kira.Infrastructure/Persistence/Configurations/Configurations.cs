using Kira.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kira.Infrastructure.Persistence.Configurations;

// Foreign keys use NoAction so SQL Server never sees multiple cascade paths; the application decides what to delete.

internal sealed class UserConfig : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.Property(x => x.Email).HasMaxLength(254);
        b.Property(x => x.Name).HasMaxLength(150);
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.PasswordHash).HasMaxLength(500);
        b.HasIndex(x => x.Email).IsUnique();
    }
}

internal sealed class RefreshTokenConfig : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshTokens");
        b.Property(x => x.TokenHash).HasMaxLength(64);
        b.Property(x => x.CreatedByIp).HasMaxLength(64);
        b.Property(x => x.UserAgent).HasMaxLength(300);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.UserId, x.RevokedAt });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CompanyConfig : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> b)
    {
        b.ToTable("Companies");
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.RegistrationNo).HasMaxLength(60);
        b.Property(x => x.Industry).HasMaxLength(200);
        b.Property(x => x.Address).HasMaxLength(400);
    }
}

internal sealed class MembershipConfig : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> b)
    {
        b.ToTable("Memberships");
        b.HasIndex(x => new { x.UserId, x.CompanyId }).IsUnique();
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AccountConfig : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> b)
    {
        b.ToTable("Accounts");
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(150);
        b.Property(x => x.SystemKey).HasMaxLength(40);
        b.Property(x => x.Last4).HasMaxLength(4);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class JournalEntryConfig : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> b)
    {
        b.ToTable("JournalEntries");
        b.Property(x => x.Memo).HasMaxLength(300);
        b.Property(x => x.Source).HasMaxLength(30);
        b.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.Date });
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.JournalEntryId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        b.Ignore(x => x.Total);
    }
}

internal sealed class JournalLineConfig : IEntityTypeConfiguration<JournalLine>
{
    public void Configure(EntityTypeBuilder<JournalLine> b)
    {
        b.ToTable("JournalLines");
        b.HasOne(l => l.Account).WithMany().HasForeignKey(l => l.AccountId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(l => l.AccountId);
    }
}

internal sealed class BankLineConfig : IEntityTypeConfiguration<BankLine>
{
    public void Configure(EntityTypeBuilder<BankLine> b)
    {
        b.ToTable("BankLines");
        b.Property(x => x.Flags).HasMaxLength(100);
        b.Property(x => x.ImportBatch).HasMaxLength(20);
        b.Property(x => x.SourceFile).HasMaxLength(200);
        b.Property(x => x.Confidence).HasPrecision(5, 4);
        b.HasIndex(x => new { x.CompanyId, x.Status });
        b.HasIndex(x => new { x.CompanyId, x.BankAccountId, x.Date });
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.SuggestedAccountId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.NoAction);
        b.Ignore(x => x.FlagList);
        b.Ignore(x => x.IsUnsure);
    }
}

internal sealed class CategoryRuleConfig : IEntityTypeConfiguration<CategoryRule>
{
    public void Configure(EntityTypeBuilder<CategoryRule> b)
    {
        b.ToTable("CategoryRules");
        b.Property(x => x.Pattern).HasMaxLength(100);
        b.Property(x => x.Value).HasMaxLength(60);
        b.HasIndex(x => new { x.CompanyId, x.UserId, x.Pattern });
    }
}

internal sealed class StoredFileConfig : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> b)
    {
        b.ToTable("StoredFiles");
        b.Property(x => x.FileName).HasMaxLength(200);
        b.Property(x => x.ContentType).HasMaxLength(100);
        b.Property(x => x.Sha256).HasMaxLength(64);
        b.Property(x => x.Data).HasColumnType("varbinary(max)");
        b.HasIndex(x => x.CompanyId);
        b.HasIndex(x => x.UserId);
    }
}

internal sealed class DocumentConfig : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> b)
    {
        b.ToTable("Documents");
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasOne(x => x.File).WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<BankLine>().WithMany().HasForeignKey(x => x.BankLineId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => x.CompanyId);
    }
}

internal sealed class ReminderConfig : IEntityTypeConfiguration<Reminder>
{
    public void Configure(EntityTypeBuilder<Reminder> b)
    {
        b.ToTable("Reminders");
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => x.CompanyId);
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AuditLogConfig : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");
        b.Property(x => x.Action).HasMaxLength(100);
        b.Property(x => x.Detail).HasMaxLength(500);
        b.HasIndex(x => new { x.CompanyId, x.At });
    }
}

internal sealed class PaymentAccountConfig : IEntityTypeConfiguration<PaymentAccount>
{
    public void Configure(EntityTypeBuilder<PaymentAccount> b)
    {
        b.ToTable("PaymentAccounts");
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.Institution).HasMaxLength(80);
        b.Property(x => x.Last4).HasMaxLength(4);
        b.HasIndex(x => x.UserId);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PersonalTransactionConfig : IEntityTypeConfiguration<PersonalTransaction>
{
    public void Configure(EntityTypeBuilder<PersonalTransaction> b)
    {
        b.ToTable("PersonalTransactions");
        b.Property(x => x.Description).HasMaxLength(400);
        b.Property(x => x.Category).HasMaxLength(40);
        b.Property(x => x.Reason).HasMaxLength(200);
        b.Property(x => x.ImportBatch).HasMaxLength(20);
        b.Property(x => x.Confidence).HasPrecision(5, 4);
        b.HasIndex(x => new { x.UserId, x.Date });
        b.HasIndex(x => new { x.UserId, x.PaymentAccountId });
        b.HasIndex(x => x.ReceiptId);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<PaymentAccount>().WithMany().HasForeignKey(x => x.PaymentAccountId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class ReceiptConfig : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> b)
    {
        b.ToTable("Receipts");
        b.Property(x => x.Merchant).HasMaxLength(200);
        b.Property(x => x.ReceiptNo).HasMaxLength(60);
        b.Property(x => x.ItemsJson).HasMaxLength(20000).HasColumnType("nvarchar(max)"); // overrides the 400 default
        b.Property(x => x.ScreenshotJson).HasMaxLength(2000);
        b.Property(x => x.Category).HasMaxLength(40);
        b.Property(x => x.PrintedPayment).HasMaxLength(60);
        b.Property(x => x.ReliefCode).HasMaxLength(40);
        b.Property(x => x.SuggestedReliefCode).HasMaxLength(40);
        b.Property(x => x.Confidence).HasPrecision(5, 4);
        b.Property(x => x.PaymentConfidence).HasPrecision(5, 4);
        b.Property(x => x.ReliefConfidence).HasPrecision(5, 4);
        b.HasIndex(x => new { x.UserId, x.Date });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.ImageFileId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.ScreenshotFileId).OnDelete(DeleteBehavior.NoAction);
    }
}
