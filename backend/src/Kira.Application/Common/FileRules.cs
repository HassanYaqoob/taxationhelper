using System.Security.Cryptography;

namespace Kira.Application.Common;

/// <summary>Validates uploads by their content (magic bytes), not just the file name.</summary>
public static class FileRules
{
    public const string Pdf = "application/pdf";
    public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string ExcelContentType = Xlsx;

    public static string Detect(FileUpload f)
    {
        var d = f.Data;
        bool Starts(params byte[] sig) => d.Length >= sig.Length && sig.Select((b, i) => d[i] == b).All(x => x);
        if (Starts(0x25, 0x50, 0x44, 0x46)) return Pdf;                                   // %PDF
        if (Starts(0x89, 0x50, 0x4E, 0x47)) return "image/png";
        if (Starts(0xFF, 0xD8, 0xFF)) return "image/jpeg";
        if (d.Length > 12 && Starts(0x52, 0x49, 0x46, 0x46) && d[8] == 0x57 && d[9] == 0x45 && d[10] == 0x42 && d[11] == 0x50) return "image/webp";
        if (Starts(0x47, 0x49, 0x46, 0x38)) return "image/gif";
        if (Starts(0x50, 0x4B, 0x03, 0x04) && f.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) return Xlsx;
        if (LooksLikeText(d))
            return f.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? "text/csv" : "text/plain";
        return "application/octet-stream";
    }

    public static bool IsImage(string type) => type is "image/png" or "image/jpeg" or "image/webp" or "image/gif";

    /// <summary>Checks the real type is one of the allowed ones and returns it.</summary>
    public static string Require(FileUpload f, params string[] allowed)
    {
        if (f.Data.Length == 0) throw new DomainException("The file is empty.");
        var type = Detect(f);
        if (!allowed.Contains(type) && !(allowed.Contains("image/*") && IsImage(type)))
            throw new DomainException("This type of file isn't supported here.");
        return type;
    }

    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    public static string SafeName(string name)
    {
        var n = Path.GetFileName(name ?? "file");
        n = new string(n.Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c != '"').ToArray()).Trim();
        if (n.Length == 0) n = "file";
        return n.Length > 180 ? n[^180..] : n;
    }

    private static bool LooksLikeText(byte[] d)
    {
        var n = Math.Min(d.Length, 4096);
        for (var i = 0; i < n; i++) if (d[i] == 0) return false;
        return true;
    }
}
