using System.Globalization;

namespace TaskbarInfo;

public sealed record WaterReminderStatus(
    bool IsEnabled,
    bool IsDue,
    bool IsQuietHours,
    bool IsGoalReached,
    int CompletedToday,
    int DailyGoal,
    DateTime NextReminderAt,
    TimeSpan Remaining);

public static class WaterReminderSchedule
{
    private const int MinimumIntervalMinutes = 15;
    private const int MaximumIntervalMinutes = 240;
    private const int MinimumSnoozeMinutes = 5;
    private const int MaximumSnoozeMinutes = 60;
    private const int MinimumDailyGoal = 1;
    private const int MaximumDailyGoal = 24;

    public static void Normalize(AppSettings settings, DateTime now)
    {
        settings.WaterReminderDrinkHistory = WaterReminderHistory.Normalize(
            settings.WaterReminderDrinkHistory,
            now);
        settings.WaterReminderIntervalMinutes = Math.Clamp(
            settings.WaterReminderIntervalMinutes,
            MinimumIntervalMinutes,
            MaximumIntervalMinutes);
        settings.WaterReminderSnoozeMinutes = Math.Clamp(
            settings.WaterReminderSnoozeMinutes,
            MinimumSnoozeMinutes,
            MaximumSnoozeMinutes);
        settings.WaterReminderDailyGoal = Math.Clamp(
            settings.WaterReminderDailyGoal,
            MinimumDailyGoal,
            MaximumDailyGoal);

        string today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!string.Equals(settings.WaterReminderRecordDate, today, StringComparison.Ordinal))
        {
            settings.WaterReminderRecordDate = today;
            settings.WaterReminderCompletedToday = 0;
        }

        settings.WaterReminderCompletedToday = Math.Max(0, settings.WaterReminderCompletedToday);
        settings.WaterReminderLastCompletedAt ??= now;
        if (settings.WaterReminderSnoozedUntil.HasValue && settings.WaterReminderSnoozedUntil <= now)
        {
            settings.WaterReminderSnoozedUntil = null;
        }

        settings.WaterReminderQuietStart = NormalizeTime(settings.WaterReminderQuietStart, "22:00");
        settings.WaterReminderQuietEnd = NormalizeTime(settings.WaterReminderQuietEnd, "07:00");
    }

    public static WaterReminderStatus GetStatus(AppSettings settings, DateTime now)
    {
        Normalize(settings, now);
        bool isQuietHours = IsQuietHours(settings, now.TimeOfDay);
        bool isGoalReached = settings.WaterReminderCompletedToday >= settings.WaterReminderDailyGoal;
        DateTime nextReminder = settings.WaterReminderLastCompletedAt!.Value
            .AddMinutes(settings.WaterReminderIntervalMinutes);
        if (settings.WaterReminderSnoozedUntil.HasValue && settings.WaterReminderSnoozedUntil > nextReminder)
        {
            nextReminder = settings.WaterReminderSnoozedUntil.Value;
        }

        TimeSpan remaining = nextReminder > now ? nextReminder - now : TimeSpan.Zero;
        bool isDue = settings.EnableWaterReminder && !isQuietHours && !isGoalReached && now >= nextReminder;
        return new WaterReminderStatus(
            settings.EnableWaterReminder,
            isDue,
            isQuietHours,
            isGoalReached,
            settings.WaterReminderCompletedToday,
            settings.WaterReminderDailyGoal,
            nextReminder,
            remaining);
    }

    public static void RecordDrink(AppSettings settings, DateTime now)
    {
        Normalize(settings, now);
        settings.WaterReminderDrinkHistory.Add(now);
        settings.WaterReminderCompletedToday++;
        settings.WaterReminderLastCompletedAt = now;
        settings.WaterReminderSnoozedUntil = null;
    }

    public static void Snooze(AppSettings settings, DateTime now)
    {
        Normalize(settings, now);
        settings.WaterReminderSnoozedUntil = now.AddMinutes(settings.WaterReminderSnoozeMinutes);
    }

    private static bool IsQuietHours(AppSettings settings, TimeSpan now)
    {
        TimeSpan start = ParseTime(settings.WaterReminderQuietStart, new TimeSpan(22, 0, 0));
        TimeSpan end = ParseTime(settings.WaterReminderQuietEnd, new TimeSpan(7, 0, 0));
        if (start == end) return false;
        return start < end
            ? now >= start && now < end
            : now >= start || now < end;
    }

    private static string NormalizeTime(string? value, string fallback) =>
        TimeSpan.TryParseExact(value, "hh\\:mm", CultureInfo.InvariantCulture, out TimeSpan time) &&
        time >= TimeSpan.Zero && time < TimeSpan.FromDays(1)
            ? time.ToString("hh\\:mm", CultureInfo.InvariantCulture)
            : fallback;

    private static TimeSpan ParseTime(string? value, TimeSpan fallback) =>
        TimeSpan.TryParseExact(value, "hh\\:mm", CultureInfo.InvariantCulture, out TimeSpan time) &&
        time >= TimeSpan.Zero && time < TimeSpan.FromDays(1)
            ? time
            : fallback;
}
