using Kira.Domain.Common;

namespace Kira.Domain.Entities;

public class Account : Entity
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public AccountType Type { get; set; }
    public TaxTreatment Tax { get; set; } = TaxTreatment.Standard;
    public string? SystemKey { get; set; }
    /// <summary>Plain-language hint that helps the AI choose this account.</summary>
    public string Hint { get; set; } = "";
    /// <summary>For bank accounts: last 4 digits of the account number.</summary>
    public string Last4 { get; set; } = "";

    public bool IsBank => Tax == TaxTreatment.Bank;

    /// <summary>Balance in the account's natural direction (debit for assets and expenses, credit otherwise).</summary>
    public decimal Natural(decimal debit, decimal credit) =>
        Type is AccountType.Asset or AccountType.Expense ? debit - credit : credit - debit;
}

public sealed record LineInput(Guid AccountId, decimal Debit, decimal Credit);

/// <summary>
/// A posted, immutable journal entry. Lines can't change after posting; the only change allowed
/// is linking the entry to the reversal that cancels it.
/// </summary>
public class JournalEntry : Entity
{
    public Guid CompanyId { get; init; }
    public int Number { get; init; }
    public DateOnly Date { get; init; }
    public string Memo { get; init; } = "";
    public string Source { get; init; } = "manual";
    public Guid CreatedById { get; init; }
    public Guid? ReversesEntryId { get; init; }
    public Guid? ReversedByEntryId { get; set; }
    public List<JournalLine> Lines { get; init; } = [];

    public decimal Total => Lines.Sum(l => l.Debit);

    public static JournalEntry Create(Guid companyId, int number, DateOnly date, string memo, string source, Guid userId,
        IEnumerable<LineInput> lines, Guid? reverses = null)
    {
        var list = lines.Where(l => l.Debit != 0 || l.Credit != 0)
            .Select(l => new JournalLine { AccountId = l.AccountId, Debit = Math.Round(l.Debit, 2), Credit = Math.Round(l.Credit, 2) })
            .ToList();
        if (list.Count < 2) throw new DomainException("A journal entry needs at least two lines.");
        if (list.Any(l => l.Debit < 0 || l.Credit < 0 || (l.Debit > 0 && l.Credit > 0)))
            throw new DomainException("Each line must be either a debit or a credit, and positive.");
        var dr = list.Sum(l => l.Debit);
        var cr = list.Sum(l => l.Credit);
        if (dr != cr) throw new DomainException($"Entry doesn't balance: debits {dr:N2}, credits {cr:N2}.");

        memo = (memo ?? "").Trim();
        return new JournalEntry
        {
            CompanyId = companyId, Number = number, Date = date, Source = source, CreatedById = userId, ReversesEntryId = reverses,
            Memo = memo.Length > 300 ? memo[..300] : memo, Lines = list
        };
    }

    public JournalEntry CreateReversal(int number, DateOnly date, Guid userId)
    {
        if (ReversedByEntryId != null) throw new DomainException("This entry has already been reversed.");
        if (ReversesEntryId != null) throw new DomainException("A reversal can't itself be reversed. Post a new entry instead.");
        var r = Create(CompanyId, number, date, $"Reversal of #{Number}: {Memo}", "reversal", userId,
            Lines.Select(l => new LineInput(l.AccountId, l.Credit, l.Debit)), Id);
        ReversedByEntryId = r.Id;
        return r;
    }
}

public class JournalLine
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid JournalEntryId { get; init; }
    public Guid AccountId { get; init; }
    public Account? Account { get; init; }
    public decimal Debit { get; init; }
    public decimal Credit { get; init; }
}
