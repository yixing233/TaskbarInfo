namespace TaskbarInfo;

public sealed record WaterReminderDailyCount(DateTime Date, int Count);

public static class WaterReminderHistory
{
    public const int RetentionDays = 90;

    public static List<DateTime> Normalize(IEnumerable<DateTime>? history, DateTime now)
    {
        DateTime earliest = now.Date.AddDays(-(RetentionDays - 1));
        return (history ?? Enumerable.Empty<DateTime>())
            .Where(timestamp => timestamp >= earliest && timestamp <= now)
            .OrderBy(timestamp => timestamp)
            .ToList();
    }

    public static IReadOnlyList<WaterReminderDailyCount> GetDailyCounts(
        IEnumerable<DateTime>? history,
        DateTime now,
        int days)
    {
        int count = Math.Clamp(days, 1, RetentionDays);
        DateTime firstDay = now.Date.AddDays(1 - count);
        var counts = (history ?? Enumerable.Empty<DateTime>())
            .Where(timestamp => timestamp >= firstDay && timestamp <= now)
            .GroupBy(timestamp => timestamp.Date)
            .ToDictionary(group => group.Key, group => group.Count());

        return Enumerable.Range(0, count)
            .Select(offset =>
            {
                DateTime date = firstDay.AddDays(offset);
                return new WaterReminderDailyCount(
                    date,
                    counts.TryGetValue(date, out int dailyCount) ? dailyCount : 0);
            })
            .ToList();
    }

    public static bool Remove(List<DateTime>? history, DateTime timestamp)
    {
        if (history == null) return false;

        int index = history.FindLastIndex(value => value == timestamp);
        if (index < 0) return false;

        history.RemoveAt(index);
        return true;
    }
}
