using System;
using System.Text;

namespace TaskbarInfo
{
    /// <summary>
    /// Hosts the media widget inside the active desktop list view.
    /// </summary>
    public sealed class DesktopHostService
    {
        private IntPtr _desktopHost;
        private IntPtr _desktopView;

        public IntPtr DesktopHostHandle => _desktopHost;

        public bool EnsureAttached(IntPtr widgetHandle)
        {
            if (widgetHandle == IntPtr.Zero || !UnmanagedMethods.IsWindow(widgetHandle))
            {
                return false;
            }

            IntPtr desktopView = UnmanagedMethods.IsWindow(_desktopView)
                ? _desktopView
                : FindDesktopView();
            if (desktopView == IntPtr.Zero || !UnmanagedMethods.IsWindow(desktopView))
            {
                _desktopHost = IntPtr.Zero;
                _desktopView = IntPtr.Zero;
                return false;
            }

            IntPtr desktopHost = FindDesktopInputHost(desktopView);
            if (desktopHost == IntPtr.Zero || !UnmanagedMethods.IsWindow(desktopHost))
            {
                _desktopHost = IntPtr.Zero;
                _desktopView = IntPtr.Zero;
                return false;
            }

            _desktopView = desktopView;
            _desktopHost = desktopHost;
            bool alreadyAttached = UnmanagedMethods.GetParent(widgetHandle) == desktopHost;
            if (!alreadyAttached)
            {
                ApplyDesktopWindowStyles(widgetHandle);
                UnmanagedMethods.SetParent(widgetHandle, desktopHost);
            }
            return UnmanagedMethods.GetParent(widgetHandle) == desktopHost;
        }

        public bool Move(
            IntPtr widgetHandle,
            int screenX,
            int screenY,
            int width,
            int height,
            bool positionOnly = false)
        {
            if (UnmanagedMethods.GetParent(widgetHandle) != _desktopHost &&
                !EnsureAttached(widgetHandle)) return false;

            if (!TryScreenToHostClient(
                    _desktopHost,
                    screenX,
                    screenY,
                    out var clientPoint)) return false;

            return UnmanagedMethods.SetWindowPos(
                widgetHandle,
                UnmanagedMethods.HWND_TOP,
                clientPoint.X,
                clientPoint.Y,
                positionOnly ? 0 : Math.Max(1, width),
                positionOnly ? 0 : Math.Max(1, height),
                UnmanagedMethods.SWP_NOACTIVATE |
                UnmanagedMethods.SWP_NOOWNERZORDER |
                UnmanagedMethods.SWP_NOZORDER |
                UnmanagedMethods.SWP_SHOWWINDOW |
                (positionOnly
                    ? UnmanagedMethods.SWP_NOSIZE
                    : 0));
        }

        public static IntPtr FindDesktopView()
        {
            IntPtr progman = UnmanagedMethods.FindWindow("Progman", "Program Manager");
            IntPtr desktopView = progman == IntPtr.Zero
                ? IntPtr.Zero
                : UnmanagedMethods.FindWindowEx(
                    progman,
                    IntPtr.Zero,
                    "SHELLDLL_DefView",
                    null);
            if (desktopView != IntPtr.Zero)
            {
                return desktopView;
            }

            IntPtr found = IntPtr.Zero;
            UnmanagedMethods.EnumWindows((window, _) =>
            {
                IntPtr child = UnmanagedMethods.FindWindowEx(
                    window,
                    IntPtr.Zero,
                    "SHELLDLL_DefView",
                    null);
                if (child == IntPtr.Zero) return true;

                found = child;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        public static IntPtr FindDesktopHost()
        {
            IntPtr desktopView = FindDesktopView();
            return desktopView == IntPtr.Zero
                ? IntPtr.Zero
                : UnmanagedMethods.GetParent(desktopView);
        }

        public static IntPtr FindDesktopInputHost()
        {
            return FindDesktopInputHost(FindDesktopView());
        }

        private static IntPtr FindDesktopInputHost(IntPtr desktopView)
        {
            if (desktopView == IntPtr.Zero) return IntPtr.Zero;

            IntPtr listView = UnmanagedMethods.FindWindowEx(
                desktopView,
                IntPtr.Zero,
                "SysListView32",
                null);
            return listView != IntPtr.Zero ? listView : desktopView;
        }

        public static string GetWindowClassName(IntPtr window)
        {
            if (window == IntPtr.Zero) return "";
            var name = new StringBuilder(128);
            return UnmanagedMethods.GetClassName(window, name, name.Capacity) > 0
                ? name.ToString()
                : "";
        }

        public static (double X, double Y) GetDpiScaleForPoint(int screenX, int screenY)
        {
            try
            {
                IntPtr monitor = UnmanagedMethods.MonitorFromPoint(
                    new UnmanagedMethods.POINT { X = screenX, Y = screenY },
                    UnmanagedMethods.MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero &&
                    UnmanagedMethods.GetDpiForMonitor(monitor, 0, out uint dpiX, out uint dpiY) == 0)
                {
                    return (dpiX / 96d, dpiY / 96d);
                }
            }
            catch
            {
            }

            return (1, 1);
        }

        public static bool TryScreenToHostClient(
            IntPtr host,
            int screenX,
            int screenY,
            out UnmanagedMethods.POINT clientPoint)
        {
            clientPoint = default;
            if (host == IntPtr.Zero ||
                !UnmanagedMethods.GetWindowRect(host, out var hostBounds))
            {
                return false;
            }

            // ScreenToClient applies cross-process DPI virtualization when the
            // Explorer host spans monitors. SetWindowPos expects coordinates in
            // the host's own virtual-desktop space, which is represented by its
            // window rectangle instead.
            clientPoint.X = screenX - hostBounds.Left;
            clientPoint.Y = screenY - hostBounds.Top;
            return true;
        }

        private static void ApplyDesktopWindowStyles(IntPtr widgetHandle)
        {
            long style = UnmanagedMethods.GetWindowLongPtr(
                widgetHandle,
                UnmanagedMethods.GWL_STYLE).ToInt64();
            style &= ~UnmanagedMethods.WS_OVERLAPPEDWINDOW;
            style &= ~UnmanagedMethods.WS_POPUP;
            style |= UnmanagedMethods.WS_CHILD |
                     UnmanagedMethods.WS_VISIBLE |
                     UnmanagedMethods.WS_CLIPSIBLINGS;
            if (UnmanagedMethods.GetWindowLongPtr(
                    widgetHandle,
                    UnmanagedMethods.GWL_STYLE).ToInt64() != style)
            {
                UnmanagedMethods.SetWindowLongPtr(
                    widgetHandle,
                    UnmanagedMethods.GWL_STYLE,
                    new IntPtr(style));
            }

            long exStyle = UnmanagedMethods.GetWindowLongPtr(
                widgetHandle,
                UnmanagedMethods.GWL_EXSTYLE).ToInt64();
            exStyle &= ~UnmanagedMethods.WS_EX_TRANSPARENT;
            exStyle &= ~UnmanagedMethods.WS_EX_APPWINDOW;
            exStyle |= UnmanagedMethods.WS_EX_TOOLWINDOW |
                       UnmanagedMethods.WS_EX_NOACTIVATE;
            if (UnmanagedMethods.GetWindowLongPtr(
                    widgetHandle,
                    UnmanagedMethods.GWL_EXSTYLE).ToInt64() != exStyle)
            {
                UnmanagedMethods.SetWindowLongPtr(
                    widgetHandle,
                    UnmanagedMethods.GWL_EXSTYLE,
                    new IntPtr(exStyle));
            }
        }

    }
}
