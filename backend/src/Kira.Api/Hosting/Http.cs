using Kira.Application.Features.Auth;

namespace Kira.Api.Hosting;

public sealed class UploadOptions
{
    public const string Section = "Uploads";
    public int MaxFileMb { get; set; } = 25;
}

public static class Http
{
    public const string RefreshCookie = "kira_rt";
    public const string RefreshCookiePath = "/api/v1/auth";

    /// <summary>Reads an uploaded file into memory, rejecting it if it's too large.</summary>
    public static async Task<FileUpload> ReadAsync(this IFormFile? file, int maxMb, CancellationToken ct)
    {
        if (file == null || file.Length == 0) throw new DomainException("Choose a file to upload.");
        if (file.Length > maxMb * 1024L * 1024L) throw new DomainException($"The file is too large (max {maxMb} MB).");
        using var ms = new MemoryStream((int)file.Length);
        await file.CopyToAsync(ms, ct);
        return new FileUpload(file.FileName, file.ContentType ?? "", ms.ToArray());
    }

    public static FileContentResult ToFile(this Application.Common.FileResult f) =>
        new(f.Data, f.ContentType) { FileDownloadName = f.FileName };

    public static void SetRefreshCookie(this HttpResponse res, Session s, bool secure) =>
        res.Cookies.Append(RefreshCookie, s.RefreshToken, new CookieOptions
        {
            HttpOnly = true, Secure = secure, SameSite = SameSiteMode.Strict, Path = RefreshCookiePath,
            Expires = s.RefreshExpiresAt, IsEssential = true
        });

    public static void ClearRefreshCookie(this HttpResponse res, bool secure) =>
        res.Cookies.Delete(RefreshCookie, new CookieOptions { HttpOnly = true, Secure = secure, SameSite = SameSiteMode.Strict, Path = RefreshCookiePath });
}

public static class RatePolicies
{
    public const string Auth = "auth";
    public const string Ai = "ai";
}
