using Kira.Domain.Common;
using Kira.Domain.Entities;
using Kira.Domain.Rules;

namespace Kira.Domain.Accounting;

public sealed record AccountBalance(Account Account, decimal Debit, decimal Credit)
{
    public decimal Natural => Account.Natural(Debit, Credit);
}

public sealed record TaxTierLine(string Label, decimal Amount);

public sealed record TaxEstimate(int YearOfAssessment, decimal Income, decimal Deductible, decimal Entertainment, decimal EntertainmentAddBack,
    decimal NonDeductible, decimal AssetsBought, decimal CapitalAllowancePct, decimal CapitalAllowance, decimal Adjusted, decimal Chargeable,
    IReadOnlyList<TaxTierLine> Tiers, decimal Tax, decimal Cp204Paid, decimal Balance, bool Sme);

/// <summary>
/// Estimated Malaysian company tax from the ledger. A planning estimate only: the tax agent prepares the real computation.
/// </summary>
public static class TaxCalculator
{
    public static TaxEstimate Estimate(TaxRule rule, bool sme, IEnumerable<AccountBalance> balances, decimal annualise = 1)
    {
        decimal income = 0, deductible = 0, entertainment = 0, nonDeductible = 0, capex = 0, paid = 0;
        foreach (var b in balances)
        {
            var v = b.Natural;
            var a = b.Account;
            if (a.Type == AccountType.Income) income += v;
            else if (a.Type == AccountType.Expense)
            {
                if (a.Tax == TaxTreatment.HalfDeductible) entertainment += v;
                else if (a.Tax == TaxTreatment.NonDeductible) nonDeductible += v;
                else deductible += v;
            }
            if (a.Tax == TaxTreatment.Capital) capex += b.Debit;
            if (a.SystemKey == SystemAccounts.TaxPrepaid) paid += b.Debit;
        }
        income *= annualise; deductible *= annualise; entertainment *= annualise; nonDeductible *= annualise;

        var addBack = Math.Round(entertainment * (100 - rule.EntertainmentDeductiblePct) / 100, 2);
        var adjusted = income - deductible - (entertainment - addBack);
        var capitalAllowance = Math.Round(capex * rule.FirstYearCapitalAllowancePct / 100, 2);
        var chargeable = Math.Max(0, adjusted - capitalAllowance);

        var tiers = new List<TaxTierLine>();
        decimal tax = 0;
        if (sme && rule.SmeTiers.Count > 0)
        {
            decimal previous = 0;
            foreach (var t in rule.SmeTiers)
            {
                var top = t.UpTo ?? decimal.MaxValue;
                var slice = Math.Max(0, Math.Min(chargeable, top) - previous);
                if (slice > 0)
                {
                    var amount = Math.Round(slice * t.Rate / 100, 2);
                    tax += amount;
                    tiers.Add(new TaxTierLine($"{t.Rate:0.##}% on RM {slice:N2}", amount));
                }
                previous = top;
                if (chargeable <= top) break;
            }
        }
        else
        {
            tax = Math.Round(chargeable * rule.StandardRate / 100, 2);
            tiers.Add(new TaxTierLine($"{rule.StandardRate:0.##}% on RM {chargeable:N2}", tax));
        }

        return new TaxEstimate(rule.YearOfAssessment, income, deductible, entertainment, addBack, nonDeductible, capex,
            rule.FirstYearCapitalAllowancePct, capitalAllowance, adjusted, chargeable, tiers, tax, paid, tax - paid, sme);
    }
}
