using Kira.Domain.Common;

namespace Kira.Domain.Entities;

// Personal records are always owned by one user and never linked to a company.

public class PaymentAccount : Entity
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public PaymentKind Kind { get; set; }
    public string Institution { get; set; } = "";
    /// <summary>Last 4 digits of the account or card (phone digits for e-wallets). Used to match screenshots.</summary>
    public string Last4 { get; set; } = "";
    public decimal OpeningBalance { get; set; }
    public int SortOrder { get; set; }
}

public class PersonalTransaction : Entity
{
    public Guid UserId { get; set; }
    public Guid? PaymentAccountId { get; set; }
    public DateOnly Date { get; set; }
    public string Description { get; set; } = "";
    public Direction Direction { get; set; }
    public decimal Amount { get; set; }
    public string Category { get; set; } = "Other";
    public decimal Confidence { get; set; } = 1;
    public string Reason { get; set; } = "";
    public bool Reviewed { get; set; } = true;
    /// <summary>Money moving between the person's own accounts; not income or spending.</summary>
    public bool IsTransfer { get; set; }
    public TransactionSource Source { get; set; } = TransactionSource.Manual;
    public Guid? ReceiptId { get; set; }
    /// <summary>True once a statement line has confirmed a receipt-created transaction.</summary>
    public bool StatementMatched { get; set; }
    public string ImportBatch { get; set; } = "";

    /// <summary>A statement line and a receipt are the same payment when account and amount match within 3 days.</summary>
    public bool IsSamePayment(Guid? accountId, decimal amount, DateOnly date) =>
        PaymentAccountId == accountId && Amount == amount && Direction == Direction.Out && Math.Abs(Date.DayNumber - date.DayNumber) <= 3;
}

public class Receipt : Entity
{
    public Guid UserId { get; set; }
    public ReceiptStatus Status { get; set; } = ReceiptStatus.Review;
    public string Merchant { get; set; } = "";
    public DateOnly Date { get; set; }
    public decimal Total { get; set; }
    public decimal Sst { get; set; }
    public string ReceiptNo { get; set; } = "";
    public string ItemsJson { get; set; } = "[]";
    public string Category { get; set; } = "Other";
    public decimal Confidence { get; set; }
    public string Reason { get; set; } = "";
    /// <summary>Payment method printed on the receipt, e.g. "VISA ••2201".</summary>
    public string PrintedPayment { get; set; } = "";

    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unknown;
    public Guid? PaymentAccountId { get; set; }
    public PaymentSource PaymentSource { get; set; } = PaymentSource.None;
    public string PaymentNote { get; set; } = "";
    public decimal PaymentConfidence { get; set; }

    public string? ReliefCode { get; set; }
    public string? SuggestedReliefCode { get; set; }
    public decimal ReliefConfidence { get; set; }
    public string ReliefReason { get; set; } = "";
    public bool ReliefDismissed { get; set; }

    public Guid? ImageFileId { get; set; }
    public Guid? ScreenshotFileId { get; set; }
    /// <summary>What the AI read from the payment screenshot, as JSON.</summary>
    public string ScreenshotJson { get; set; } = "";

    public Guid? TransactionId { get; set; }
}
