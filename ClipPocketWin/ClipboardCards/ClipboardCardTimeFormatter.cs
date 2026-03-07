using System;

namespace ClipPocketWin;

internal static class ClipboardCardTimeFormatter
{
    public static string GetRelativeTimestampLabel(DateTimeOffset timestamp)
    {
        TimeSpan elapsed = DateTimeOffset.UtcNow - timestamp;
        if (elapsed <= TimeSpan.FromSeconds(2))
        {
            return "Now";
        }

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return $"{(int)elapsed.TotalSeconds} sec";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            int minutes = (int)elapsed.TotalMinutes;
            int seconds = elapsed.Seconds;
            return $"{minutes} min, {seconds} sec";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            int hours = (int)elapsed.TotalHours;
            int minutes = elapsed.Minutes;
            return $"{hours} hr, {minutes} min";
        }

        int days = (int)elapsed.TotalDays;
        int hoursRemainder = elapsed.Hours;
        return $"{days} d, {hoursRemainder} hr";
    }
}
