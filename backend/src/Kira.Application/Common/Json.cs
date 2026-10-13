using System.Globalization;

namespace Kira.Application.Common;

/// <summary>Tolerant readers for JSON returned by the AI.</summary>
public static class Json
{
    public static string Str(this JsonElement e, string name, string fallback = "") =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() is { } s && s != "null" ? s : fallback,
                JsonValueKind.Null or JsonValueKind.Undefined => fallback,
                _ => v.ToString()
            }
            : fallback;

    public static decimal Dec(this JsonElement e, string name, decimal fallback = 0)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        return ParseMoney(v.ToString()) ?? fallback;
    }

    public static decimal? ParseMoney(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var clean = new string(s.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
        return decimal.TryParse(clean, NumberStyles.Number, CultureInfo.InvariantCulture, out var p) ? p : null;
    }

    public static DateOnly? Date(this JsonElement e, string name) =>
        DateOnly.TryParseExact(e.Str(name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static IEnumerable<JsonElement> Arr(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().ToList() : Enumerable.Empty<JsonElement>();

    public static JsonElement Obj(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;

    public static decimal Clamp01(decimal d) => Math.Max(0, Math.Min(1, d));

    public static string Cut(string? s, int max) => s == null ? "" : s.Length > max ? s[..max] : s;

    /// <summary>Pulls the first JSON object or array out of a model reply.</summary>
    public static JsonElement ParseReply(string text)
    {
        var t = text.Trim();
        var start = t.IndexOfAny(['{', '[']);
        var end = Math.Max(t.LastIndexOf('}'), t.LastIndexOf(']'));
        if (start < 0 || end < start) throw new DomainException("The AI reply couldn't be read. Try again.");
        try
        {
            using var doc = JsonDocument.Parse(t[start..(end + 1)]);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new DomainException("The AI reply was incomplete. Try again, or split a long statement into smaller files.");
        }
    }
}
