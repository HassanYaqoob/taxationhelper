using Kira.Domain.Common;
using Kira.Domain.Entities;

namespace Kira.UnitTests;

public class LedgerTests
{
    static readonly Guid Company = Guid.NewGuid(), Bank = Guid.NewGuid(), Rent = Guid.NewGuid(), User = Guid.NewGuid();
    static readonly DateOnly Day = new(2026, 9, 1);

    [Fact]
    public void Balanced_entry_is_created()
    {
        var e = JournalEntry.Create(Company, 1, Day, "Rent", "bank", User, [new(Rent, 4500, 0), new(Bank, 0, 4500)]);
        Assert.Equal(2, e.Lines.Count);
        Assert.Equal(4500, e.Total);
    }

    [Fact]
    public void Unbalanced_entry_is_rejected()
    {
        var ex = Assert.Throws<DomainException>(() =>
            JournalEntry.Create(Company, 1, Day, "Rent", "bank", User, [new(Rent, 4500, 0), new(Bank, 0, 4400)]));
        Assert.Contains("doesn't balance", ex.Message);
    }

    [Fact]
    public void Line_cannot_be_both_debit_and_credit()
    {
        Assert.Throws<DomainException>(() =>
            JournalEntry.Create(Company, 1, Day, "Bad", "manual", User, [new(Rent, 10, 10), new(Bank, 0, 0)]));
    }

    [Fact]
    public void Reversal_swaps_debits_and_credits_and_links_both_entries()
    {
        var e = JournalEntry.Create(Company, 1, Day, "Rent", "bank", User, [new(Rent, 4500, 0), new(Bank, 0, 4500)]);
        var r = e.CreateReversal(2, Day.AddDays(3), User);
        Assert.Equal(e.Id, r.ReversesEntryId);
        Assert.Equal(r.Id, e.ReversedByEntryId);
        Assert.Equal(4500, r.Lines.Single(l => l.AccountId == Rent).Credit);
        Assert.Throws<DomainException>(() => e.CreateReversal(3, Day, User));
        Assert.Throws<DomainException>(() => r.CreateReversal(3, Day, User));
    }

    [Fact]
    public void Bank_line_money_in_debits_the_bank()
    {
        var line = new BankLine { BankAccountId = Bank, SuggestedAccountId = Rent, Direction = Direction.In, Amount = 100 };
        var lines = line.ToJournalLines();
        Assert.Equal(100, lines.Single(l => l.AccountId == Bank).Debit);
    }

    [Theory]
    [InlineData(9, 2026, 1, "2026-01-01")]
    [InlineData(3, 2026, 7, "2025-07-01")]
    [InlineData(7, 2026, 7, "2026-07-01")]
    public void Financial_year_start(int month, int year, int fyStartMonth, string expected)
    {
        var c = new Company { FyStartMonth = fyStartMonth };
        Assert.Equal(DateOnly.Parse(expected), c.FyStart(new DateOnly(year, month, 15)));
    }
}
