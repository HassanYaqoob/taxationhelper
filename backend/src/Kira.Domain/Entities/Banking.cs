using Kira.Domain.Common;

namespace Kira.Domain.Entities;

/// <summary>A line read from a company bank statement, waiting to be confirmed and posted.</summary>
public class BankLine : Entity
{
    public Guid CompanyId { get; set; }
    public Guid BankAccountId { get; set; }
    public DateOnly Date { get; set; }
    public string Description { get; set; } = "";
    public Direction Direction { get; set; }
    public decimal Amount { get; set; }
    public decimal? Balance { get; set; }
    public Guid? SuggestedAccountId { get; set; }
    public decimal Confidence { get; set; }
    public string Reason { get; set; } = "";
    /// <summary>Comma-separated <see cref="LineFlags"/>.</summary>
    public string Flags { get; set; } = "";
    public BankLineStatus Status { get; set; } = BankLineStatus.Pending;
    public Guid? JournalEntryId { get; set; }
    public string ImportBatch { get; set; } = "";
    public string SourceFile { get; set; } = "";

    public bool HasFlag(string flag) => Flags.Split(',').Contains(flag);
    public IReadOnlyList<string> FlagList => Flags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public bool IsUnsure => SuggestedAccountId == null || Confidence < 0.7m || HasFlag(LineFlags.CashWithdrawal);

    /// <summary>Lines for the journal: money in debits the bank, money out credits it.</summary>
    public IReadOnlyList<LineInput> ToJournalLines()
    {
        if (SuggestedAccountId is not Guid other) throw new DomainException("Choose a category first.");
        return Direction == Direction.In
            ? [new(BankAccountId, Amount, 0), new(other, 0, Amount)]
            : [new(other, Amount, 0), new(BankAccountId, 0, Amount)];
    }
}

/// <summary>Learned categorisation rule. Company rules map to an account code; personal rules to a category.</summary>
public class CategoryRule : Entity
{
    public Guid? CompanyId { get; set; }
    public Guid? UserId { get; set; }
    public string Pattern { get; set; } = "";
    public string Value { get; set; } = "";
}
