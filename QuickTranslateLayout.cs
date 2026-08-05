using System.Drawing;

namespace TaskbarInfo;

public static class QuickTranslateLayout
{
    private const int Gap = 8;

    public static QuickTranslatePlacement GetPlacement(
        Rectangle buttonBounds,
        Rectangle taskbarBounds,
        Rectangle screenBounds,
        Rectangle workArea,
        int popupWidth,
        int popupHeight)
    {
        int width = Math.Max(1, popupWidth);
        int height = Math.Max(1, popupHeight);
        bool isVerticalTaskbar = taskbarBounds.Height > taskbarBounds.Width;
        int left;
        int top;

        if (isVerticalTaskbar)
        {
            bool isLeftTaskbar = taskbarBounds.Right <= workArea.Left ||
                (taskbarBounds.Left < screenBounds.Left + screenBounds.Width / 2 &&
                 taskbarBounds.Left <= workArea.Left);
            left = isLeftTaskbar
                ? taskbarBounds.Right + Gap
                : taskbarBounds.Left - width - Gap;
            top = buttonBounds.Top + (buttonBounds.Height - height) / 2;
        }
        else
        {
            bool isTopTaskbar = taskbarBounds.Bottom <= workArea.Top ||
                (taskbarBounds.Top < screenBounds.Top + screenBounds.Height / 2 &&
                 taskbarBounds.Top <= workArea.Top);
            left = buttonBounds.Left + (buttonBounds.Width - width) / 2;
            top = isTopTaskbar
                ? taskbarBounds.Bottom + Gap
                : taskbarBounds.Top - height - Gap;
        }

        return new QuickTranslatePlacement(
            Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - width)),
            Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height)));
    }
}

public readonly record struct QuickTranslatePlacement(int Left, int Top);
