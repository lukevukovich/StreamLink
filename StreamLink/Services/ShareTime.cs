using System.Globalization;

namespace StreamLink.Services;

public static class ShareTime
{
    public static string Utc(DateTime utc)
    {
        var value = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var date = value.Year == DateTime.UtcNow.Year ? "MMM d" : "MMM d, yyyy";
        return value.ToString($"h:mm tt, {date} 'UTC'", CultureInfo.InvariantCulture);
    }

    public static string Remaining(DateTime utc, DateTime? nowUtc = null, bool includeSuffix = true)
    {
        var now = DateTime.SpecifyKind(nowUtc ?? DateTime.UtcNow, DateTimeKind.Utc);
        var remaining = DateTime.SpecifyKind(utc, DateTimeKind.Utc) - now;
        if (remaining <= TimeSpan.Zero) return "Expired";
        var minutes = (int)Math.Ceiling(remaining.TotalMinutes);
        if (minutes < 60) return Format($"{minutes}m", includeSuffix);
        if (minutes < 1440)
        {
            var hours = minutes / 60;
            var minutesAfterHours = minutes % 60;
            return Format(minutesAfterHours == 0
                ? $"{hours}h"
                : $"{hours}h {minutesAfterHours}m", includeSuffix);
        }

        var days = minutes / 1440;
        var remainingMinutes = minutes % 1440;
        var remainingHours = remainingMinutes / 60;
        var minutesAfterDayHours = remainingMinutes % 60;
        var dayLabel = days == 1 ? "day" : "days";
        if (remainingHours == 0)
            return Format(minutesAfterDayHours == 0
                ? $"{days} {dayLabel}"
                : $"{days} {dayLabel} {minutesAfterDayHours}m", includeSuffix);
        return Format(minutesAfterDayHours == 0
            ? $"{days} {dayLabel} {remainingHours}h"
            : $"{days} {dayLabel} {remainingHours}h {minutesAfterDayHours}m", includeSuffix);
    }

    private static string Format(string value, bool includeSuffix) =>
        includeSuffix ? $"{value} remaining" : value;
}
