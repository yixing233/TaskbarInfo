namespace TaskbarInfo;

public static class TaskbarWaterReminderLayout
{
    public const int DefaultOffsetFromTray = 72;

    public static int GetLeftFromTray(int taskbarWidth, int trayLeft, int? savedOffset)
    {
        if (taskbarWidth <= 0) return 0;

        int maxLeft = Math.Max(0, taskbarWidth - TaskbarWaterReminderWindow.WidthInPixels);
        int offset = Math.Max(0, savedOffset ?? DefaultOffsetFromTray);
        int candidate = trayLeft - TaskbarWaterReminderWindow.WidthInPixels - offset;
        return Math.Clamp(candidate, 0, maxLeft);
    }

    public static int GetOffsetForLeft(int trayLeft, int left) =>
        Math.Max(0, trayLeft - TaskbarWaterReminderWindow.WidthInPixels - left);
}
