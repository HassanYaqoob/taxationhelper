using Kira.Domain.Common;

namespace Kira.Domain.Accounting;

/// <summary>Starter chart of accounts for a Malaysian Sdn. Bhd. Bank accounts are added separately at setup.</summary>
public static class DefaultChart
{
    public sealed record Item(string Code, string Name, AccountType Type, TaxTreatment Tax = TaxTreatment.Standard, string? SystemKey = null, string Hint = "");

    public static readonly IReadOnlyList<Item> Accounts =
    [
        new("1050", "Petty cash", AccountType.Asset, SystemKey: SystemAccounts.PettyCash, Hint: "Cash withdrawn for small business spending"),
        new("1100", "Accounts receivable", AccountType.Asset, SystemKey: SystemAccounts.Receivable, Hint: "Customer paying an invoice already issued"),
        new("1200", "Computer equipment", AccountType.Asset, TaxTreatment.Capital, Hint: "Laptops, phones, servers, monitors"),
        new("1210", "Furniture & fittings", AccountType.Asset, TaxTreatment.Capital, Hint: "Desks, chairs, renovation"),
        new("1220", "Motor vehicles", AccountType.Asset, TaxTreatment.Capital, Hint: "Company car or van purchase"),
        new("1400", "Amount due from director", AccountType.Asset, SystemKey: SystemAccounts.DueFromDirector, Hint: "Director drawings or personal spending paid by the company"),
        new("1500", "Tax recoverable (CP204 paid)", AccountType.Asset, SystemKey: SystemAccounts.TaxPrepaid, Hint: "Company income tax instalments paid to LHDN / HASiL"),
        new("2000", "Accounts payable", AccountType.Liability, SystemKey: SystemAccounts.Payable, Hint: "Paying a supplier bill already recorded"),
        new("2200", "Amount due to director", AccountType.Liability, SystemKey: SystemAccounts.DueToDirector, Hint: "Director lending money to the company"),
        new("2400", "Bank loan / financing", AccountType.Liability, Hint: "Loan disbursement or principal repayment"),
        new("2500", "SST payable", AccountType.Liability, Hint: "SST paid to Customs (JKDM)"),
        new("3000", "Share capital", AccountType.Equity, Hint: "Money put in for new shares"),
        new("3100", "Retained earnings", AccountType.Equity, SystemKey: SystemAccounts.RetainedEarnings),
        new("4000", "Sales revenue", AccountType.Income, Hint: "Payment from a customer for services or goods sold"),
        new("4100", "Other income", AccountType.Income, Hint: "Interest, hibah, rebates, grants"),
        new("5000", "Cost of sales", AccountType.Expense, Hint: "Subcontractors, freelancers and direct project costs"),
        new("6000", "Salaries & wages", AccountType.Expense, Hint: "Staff salaries, bonuses, allowances"),
        new("6010", "EPF, SOCSO & EIS", AccountType.Expense, Hint: "KWSP, PERKESO, EIS, HRD Corp levy"),
        new("6100", "Rent", AccountType.Expense, Hint: "Office or shop rental"),
        new("6110", "Utilities & internet", AccountType.Expense, Hint: "TNB, Air Selangor, Indah Water, Unifi, TIME, Maxis, CelcomDigi"),
        new("6120", "Software & cloud", AccountType.Expense, Hint: "Google Workspace, Microsoft 365, AWS, Azure, SaaS"),
        new("6130", "Professional fees", AccountType.Expense, Hint: "Accountant, audit, tax agent, company secretary, legal, SSM fees"),
        new("6140", "Advertising & marketing", AccountType.Expense, Hint: "Meta, Google, TikTok and LinkedIn ads, printing"),
        new("6150", "Travel & transport", AccountType.Expense, Hint: "Grab, toll, petrol, parking, flights, hotels, courier"),
        new("6160", "Entertainment", AccountType.Expense, TaxTreatment.HalfDeductible, Hint: "Client meals and entertaining"),
        new("6170", "Office & general", AccountType.Expense, Hint: "Stationery, pantry, small office items"),
        new("6180", "Bank charges", AccountType.Expense, Hint: "Service charges, transfer, cheque and card fees"),
        new("6190", "Repairs & maintenance", AccountType.Expense),
        new("6200", "Insurance", AccountType.Expense),
        new("6900", "Fines & penalties", AccountType.Expense, TaxTreatment.NonDeductible, Hint: "Compounds, summons, late penalties (not deductible)")
    ];
}
