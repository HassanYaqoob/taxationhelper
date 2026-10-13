using System.Text.RegularExpressions;

namespace Kira.Domain.Accounting;

/// <summary>Reduces a bank description to a stable merchant key, e.g. "PEMBAYARAN JOMPAY TNB 2209" → "tnb".</summary>
public static partial class MerchantKey
{
    private static readonly HashSet<string> Stop =
    [
        "ibg", "duitnow", "trf", "transfer", "fpx", "to", "fr", "from", "cr", "dr", "pos", "purchase", "payment",
        "jompay", "bill", "sdn", "bhd", "the", "and", "debit", "credit", "card", "inv", "ref", "acc", "own",
        "bayaran", "pembayaran", "pindahan", "dana", "ke", "dari", "qr", "mbb", "cimb", "instant", "online", "ib"
    ];

    [GeneratedRegex("[^a-z\\s]")] private static partial Regex NonLetters();

    public static string From(string? description)
    {
        var cleaned = NonLetters().Replace((description ?? "").ToLowerInvariant(), " ");
        return string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1 && !Stop.Contains(w)).Take(3));
    }
}
