using Kira.Domain.Accounting;
using Kira.Domain.Common;
using Kira.Domain.Entities;
using Kira.Domain.Rules;

namespace Kira.UnitTests;

public class TaxTests
{
    static Account A(AccountType type, TaxTreatment tax = TaxTreatment.Standard, string? key = null) => new() { Type = type, Tax = tax, SystemKey = key };

    [Fact]
    public void Sme_tiers_and_adjustments()
    {
        var balances = new[]
        {
            new AccountBalance(A(AccountType.Income), 0, 200_000),
            new AccountBalance(A(AccountType.Expense), 20_000, 0),
            new AccountBalance(A(AccountType.Expense, TaxTreatment.HalfDeductible), 2_000, 0),
            new AccountBalance(A(AccountType.Expense, TaxTreatment.NonDeductible), 500, 0),
            new AccountBalance(A(AccountType.Asset, TaxTreatment.Capital), 10_000, 0),
            new AccountBalance(A(AccountType.Asset, key: SystemAccounts.TaxPrepaid), 5_000, 0)
        };
        var t = TaxCalculator.Estimate(TaxRule.Default(2026), sme: true, balances);

        Assert.Equal(1_000, t.EntertainmentAddBack);              // half of entertainment is added back
        Assert.Equal(200_000 - 20_000 - 1_000, t.Adjusted);       // penalties are not deducted
        Assert.Equal(4_000, t.CapitalAllowance);                  // 40% of RM10,000
        Assert.Equal(175_000, t.Chargeable);
        Assert.Equal(150_000 * 0.15m + 25_000 * 0.17m, t.Tax);    // 22,500 + 4,250
        Assert.Equal(5_000, t.Cp204Paid);
        Assert.Equal(t.Tax - 5_000, t.Balance);
    }

    [Fact]
    public void Loss_means_no_tax()
    {
        var t = TaxCalculator.Estimate(TaxRule.Default(2026), true,
            [new AccountBalance(A(AccountType.Income), 0, 1_000), new AccountBalance(A(AccountType.Expense), 5_000, 0)]);
        Assert.Equal(0, t.Chargeable);
        Assert.Equal(0, t.Tax);
    }

    [Fact]
    public void Non_sme_uses_standard_rate()
    {
        var t = TaxCalculator.Estimate(TaxRule.Default(2026), false, [new AccountBalance(A(AccountType.Income), 0, 100_000)]);
        Assert.Equal(24_000, t.Tax);
    }

    [Fact]
    public void Rules_pick_latest_year_not_after_requested()
    {
        var rules = new KiraRules { Tax = [new TaxRule { YearOfAssessment = 2025, StandardRate = 25 }, new TaxRule { YearOfAssessment = 2027, StandardRate = 23 }] };
        Assert.Equal(25, rules.TaxFor(2026).StandardRate);
        Assert.Equal(23, rules.TaxFor(2028).StandardRate);
    }
}
