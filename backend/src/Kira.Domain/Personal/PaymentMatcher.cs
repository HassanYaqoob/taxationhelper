using Kira.Domain.Common;
using Kira.Domain.Entities;

namespace Kira.Domain.Personal;

public sealed record AccountMatch(PaymentAccount? Account, decimal Confidence, string Why);

/// <summary>Works out which of a person's accounts paid, from the last digits first, then the bank or e-wallet name.</summary>
public static class PaymentMatcher
{
    public static AccountMatch Match(IReadOnlyList<PaymentAccount> accounts, string? institution, string? last4, string? accountType)
    {
        var usable = accounts.Where(a => a.Kind != PaymentKind.Cash).ToList();
        var digits = Digits(last4);
        if (digits.Length >= 3)
        {
            var byDigits = usable.Where(a => a.Last4.Length > 0 && a.Last4.EndsWith(digits, StringComparison.Ordinal)).ToList();
            if (byDigits.Count == 1) return new(byDigits[0], 0.97m, $"••{digits} matches {byDigits[0].Name}");
        }

        var inst = (institution ?? "").Trim().ToLowerInvariant();
        if (inst.Length == 0) return new(null, 0, "Couldn't tell which account paid.");

        var byInst = usable.Where(a => SameInstitution(inst, a.Institution)).ToList();
        var kind = KindOf(accountType);
        if (byInst.Count > 1 && kind != null && byInst.Count(a => a.Kind == kind) == 1) byInst = byInst.Where(a => a.Kind == kind).ToList();
        return byInst.Count switch
        {
            1 => new(byInst[0], 0.82m, $"{institution} matches {byInst[0].Name}"),
            > 1 => new(byInst[0], 0.5m, $"You have several {institution} accounts. Please check which one paid."),
            _ => new(null, 0, $"Paid with {institution}, but no matching account is set up.")
        };
    }

    public static string Digits(string? s)
    {
        var d = new string((s ?? "").Where(char.IsDigit).ToArray());
        return d.Length > 4 ? d[^4..] : d;
    }

    private static bool SameInstitution(string read, string mine)
    {
        var m = mine.Trim().ToLowerInvariant();
        if (m.Length == 0) return false;
        if (read.Contains(m) || m.Contains(read)) return true;
        static bool Tng(string s) => s.Contains("touch") || s.Contains("tng");
        return Tng(read) && Tng(m);
    }

    private static PaymentKind? KindOf(string? t) => t switch
    {
        "bank" => PaymentKind.Bank,
        "credit_card" => PaymentKind.CreditCard,
        "debit_card" => PaymentKind.DebitCard,
        "ewallet" => PaymentKind.EWallet,
        _ => null
    };
}
