using Kira.Application.Common;
using Microsoft.Extensions.Configuration;

namespace Kira.Infrastructure.Time;

/// <summary>Business dates in the configured time zone (default Asia/Kuala_Lumpur), whatever the server's own zone is.</summary>
public sealed class MalaysiaClock(TimeProvider time, IConfiguration config) : IClock
{
    readonly TimeZoneInfo zone = Resolve(config["App:TimeZone"] ?? "Asia/Kuala_Lumpur");

    public DateTime UtcNow => time.GetUtcNow().UtcDateTime;
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(UtcNow, zone));

    static TimeZoneInfo Resolve(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("MYT", TimeSpan.FromHours(8), "Malaysia Time", "Malaysia Time");
        }
    }
}
