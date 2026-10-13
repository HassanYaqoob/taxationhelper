namespace Kira.Domain.Common;

public enum AccountType { Asset, Liability, Equity, Income, Expense }

/// <summary>How an account is treated for Malaysian company tax. Bank marks the company's own bank accounts.</summary>
public enum TaxTreatment { Standard, Bank, Capital, HalfDeductible, NonDeductible }

/// <summary>Owner can change everything. Accountant (tax agent) is read-only.</summary>
public enum MemberRole { Owner, Accountant }

public enum Direction { In, Out }
public enum BankLineStatus { Pending, Posted, Ignored }
public enum PaymentKind { Bank, CreditCard, DebitCard, EWallet, Cash }
public enum PaymentStatus { Unknown, Cash, Account, NotPaid }
public enum PaymentSource { None, Manual, Receipt, Screenshot }
public enum ReceiptStatus { Review, Saved, Error }
public enum TransactionSource { Manual, Statement, Receipt }
public enum ReminderRepeat { Once, Monthly, Yearly }

/// <summary>Accounts the system finds by purpose rather than by code.</summary>
public static class SystemAccounts
{
    public const string PettyCash = "PETTY_CASH";
    public const string Receivable = "AR";
    public const string Payable = "AP";
    public const string DueToDirector = "DUE_TO_DIRECTOR";
    public const string DueFromDirector = "DUE_FROM_DIRECTOR";
    public const string TaxPrepaid = "TAX_PREPAID";
    public const string RetainedEarnings = "RETAINED";
}

/// <summary>Flags the AI can put on a bank line.</summary>
public static class LineFlags
{
    public const string CashWithdrawal = "cash_withdrawal";
    public const string ForeignPayee = "foreign_payee";
    public const string OwnTransfer = "own_transfer";
}

public static class PersonalCategories
{
    public const string Transfers = "Transfers";
    public static readonly IReadOnlyList<string> All =
    [
        "Income", "Groceries", "Dining", "Transport", "Fuel", "Utilities", "Housing", "Entertainment",
        "Shopping", "Health", "Education", "Insurance", "Family", Transfers, "Other"
    ];
}
