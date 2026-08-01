using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Button = System.Windows.Controls.Button;

namespace TaskbarInfo
{
    public partial class DesktopWidgetWindow : System.Windows.Controls.UserControl, IDisposable
    {
        private readonly DesktopHostService _hostService = new();
        private HwndSource? _source;
        private AppSettings _settings;
        private TextBlock _activeLyric;
        private TextBlock _inactiveLyric;
        private TranslateTransform _activeLyricTransform;
        private TranslateTransform _inactiveLyricTransform;
        private string _currentLyric = "";
        private TimeSpan _duration;
        private bool _isDragging;
        private UnmanagedMethods.POINT _dragStartPointer;
        private double _dragStartLeft;
        private double _dragStartTop;
        private System.Windows.Forms.Screen? _dragScreen;
        private bool _dragMoveScheduled;
        private bool _displaySettingsSubscribed;
        private double _windowDpiScaleX = 1;
        private double _windowDpiScaleY = 1;
        private readonly DispatcherTimer _fallbackInputTimer;
        private bool _fallbackLeftWasDown;
        private bool _fallbackOwnsGesture;
        private Button? _fallbackPressedButton;

        public event Action? CloseRequested;
        public event Action? SettingsRequested;
        public event Func<Task>? PreviousTrackRequested;
        public event Func<Task>? PlayPauseRequested;
        public event Func<Task>? NextTrackRequested;

        public DesktopWidgetWindow(AppSettings settings)
        {
            _settings = settings;
            InitializeComponent();
            _activeLyric = LyricPrimary;
            _inactiveLyric = LyricSecondary;
            _activeLyricTransform = LyricPrimaryTransform;
            _inactiveLyricTransform = LyricSecondaryTransform;
            LockPositionMenuItem.IsChecked = settings.DesktopWidgetLocked;
            ApplySettings(settings);
            ResetAlbumArt();
            Loaded += DesktopWidgetWindow_Loaded;
            SizeChanged += (_, _) => ApplyRoundedWindowRegion();
            SubscribeDisplaySettings();
            _fallbackInputTimer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _fallbackInputTimer.Tick += FallbackInputTimer_Tick;
        }

        public IntPtr WindowHandle => _source?.Handle ?? IntPtr.Zero;
        public IntPtr DesktopHostHandle => _hostService.DesktopHostHandle;

        public void Show()
        {
            SubscribeDisplaySettings();
            _fallbackInputTimer.Start();
            if (_source != null &&
                _source.Handle != IntPtr.Zero &&
                UnmanagedMethods.IsWindow(_source.Handle))
            {
                Visibility = Visibility.Visible;
                EnsureDesktopAttachment();
                return;
            }

            if (_source != null)
            {
                _source.DpiChanged -= Source_DpiChanged;
                _source.Dispose();
            }
            _source = null;

            IntPtr host = DesktopHostService.FindDesktopInputHost();
            if (host == IntPtr.Zero) return;

            WidgetPlacement placement = CalculatePlacement(null, true);
            _windowDpiScaleX = placement.DpiScaleX;
            _windowDpiScaleY = placement.DpiScaleY;
            if (!DesktopHostService.TryScreenToHostClient(
                    host,
                    placement.X,
                    placement.Y,
                    out var clientPoint)) return;
            _settings.DesktopWidgetLeft = placement.X;
            _settings.DesktopWidgetTop = placement.Y;
            UpdateMonitorPlacement(placement);

            var parameters = new HwndSourceParameters(
                "LyricsX Desktop Widget",
                placement.Width,
                placement.Height)
            {
                ParentWindow = host,
                TreatAsInputRoot = true,
                WindowStyle = UnmanagedMethods.WS_CHILD |
                              UnmanagedMethods.WS_VISIBLE |
                              UnmanagedMethods.WS_CLIPSIBLINGS,
                ExtendedWindowStyle = UnmanagedMethods.WS_EX_TOOLWINDOW | UnmanagedMethods.WS_EX_NOACTIVATE,
                PositionX = clientPoint.X,
                PositionY = clientPoint.Y
            };

            _source = new HwndSource(parameters)
            {
                RootVisual = this
            };
            ApplyCompositionBackground(
                DesktopWidgetThemePalette.Get(_settings.DesktopWidgetTheme).WindowBackground);
            _source.DpiChanged += Source_DpiChanged;
            Visibility = Visibility.Visible;
            EnsureDesktopAttachment();
        }

        public void Close()
        {
            if (_source != null)
            {
                _source.DpiChanged -= Source_DpiChanged;
                _source.Dispose();
            }
            _source = null;
            Visibility = Visibility.Collapsed;
            _fallbackInputTimer.Stop();
            ResetFallbackInput();
            UnsubscribeDisplaySettings();
        }

        public void Dispose()
        {
            Close();
        }

        private void DesktopWidgetWindow_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureDesktopAttachment();
        }

        private void Source_DpiChanged(object sender, HwndDpiChangedEventArgs e)
        {
            _windowDpiScaleX = e.NewDpi.DpiScaleX;
            _windowDpiScaleY = e.NewDpi.DpiScaleY;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyRoundedWindowRegion();
            }), DispatcherPriority.Loaded);
        }

        private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                MoveToSavedPosition(null, true, true);
                ApplyRoundedWindowRegion();
                _settings.Save();
            }), DispatcherPriority.Loaded);
        }

        private void SubscribeDisplaySettings()
        {
            if (_displaySettingsSubscribed) return;
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            _displaySettingsSubscribed = true;
        }

        private void UnsubscribeDisplaySettings()
        {
            if (!_displaySettingsSubscribed) return;
            SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            _displaySettingsSubscribed = false;
        }

        public bool EnsureDesktopAttachment()
        {
            IntPtr handle = WindowHandle;
            if (!_hostService.EnsureAttached(handle)) return false;
            MoveToSavedPosition();
            ApplyRoundedWindowRegion();
            return true;
        }

        public bool RefreshDesktopAttachment()
        {
            if (_source == null ||
                WindowHandle == IntPtr.Zero ||
                !UnmanagedMethods.IsWindow(WindowHandle))
            {
                Show();
                return WindowHandle != IntPtr.Zero &&
                       UnmanagedMethods.IsWindow(WindowHandle);
            }

            IntPtr previousHost = DesktopHostHandle;
            if (!_hostService.EnsureAttached(WindowHandle)) return false;

            if (previousHost != DesktopHostHandle)
            {
                MoveToSavedPosition();
                ApplyRoundedWindowRegion();
            }
            return true;
        }

        public void ApplySettings(AppSettings settings)
        {
            _settings = settings;
            ApplyTheme(settings.DesktopWidgetTheme);
            LockPositionMenuItem.IsChecked = settings.DesktopWidgetLocked;
            if (IsLoaded) MoveToSavedPosition();
        }

        public void UpdateTrack(MediaTrackInfo track)
        {
            RunOnUi(() =>
            {
                TrackTitleText.Text = track.HasTrack ? track.Title : "等待播放";
                TrackArtistText.Text = track.HasTrack
                    ? (string.IsNullOrWhiteSpace(track.Artist) ? "未知艺术家" : track.Artist)
                    : "LyricsX";
                _duration = track.DurationMs is > 0
                    ? TimeSpan.FromMilliseconds(track.DurationMs.Value)
                    : TimeSpan.Zero;
                TotalTimeRun.Text = DesktopWidgetFormatting.FormatTime(_duration);
                UpdatePlayback(TimeSpan.Zero, _duration);

                if (track.AlbumArtBytes is { Length: > 0 })
                {
                    try
                    {
                        using var stream = new MemoryStream(track.AlbumArtBytes, writable: false);
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        AlbumArtBrush.ImageSource = bitmap;
                    }
                    catch
                    {
                        ResetAlbumArt();
                    }
                }
                else
                {
                    ResetAlbumArt();
                }
            });
        }

        public void UpdatePlayback(TimeSpan position, TimeSpan duration)
        {
            RunOnUi(() =>
            {
                if (duration > TimeSpan.Zero) _duration = duration;
                if (position < TimeSpan.Zero) position = TimeSpan.Zero;
                if (_duration > TimeSpan.Zero && position > _duration) position = _duration;
                PlaybackProgress.Value = _duration.TotalMilliseconds > 0
                    ? Math.Clamp(position.TotalMilliseconds / _duration.TotalMilliseconds, 0, 1)
                    : 0;
                ElapsedTimeRun.Text = DesktopWidgetFormatting.FormatTime(position);
                TotalTimeRun.Text = DesktopWidgetFormatting.FormatTime(_duration);
            });
        }

        public void SetPlaybackState(bool isPlaying)
        {
            RunOnUi(() =>
            {
                PlayPauseButton.Content = isPlaying ? "\uf04c" : "\uf04b";
                PlayPauseButton.ToolTip = isPlaying ? "暂停" : "播放";
            });
        }

        public void UpdateLyrics(string text)
        {
            text = string.IsNullOrWhiteSpace(text) ? "暂无歌词" : text.Trim();
            RunOnUi(() => AnimateLyricChange(text));
        }

        private void AnimateLyricChange(string text)
        {
            if (string.Equals(_currentLyric, text, StringComparison.Ordinal)) return;
            _currentLyric = text;

            if (string.IsNullOrEmpty(_activeLyric.Text) || _activeLyric.Text == "歌词将在这里显示")
            {
                _activeLyric.Text = text;
                return;
            }

            const double distance = 54;
            var duration = TimeSpan.FromMilliseconds(320);
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

            _inactiveLyric.Text = text;
            _inactiveLyric.Visibility = Visibility.Visible;
            _inactiveLyricTransform.BeginAnimation(TranslateTransform.YProperty, null);
            _activeLyricTransform.BeginAnimation(TranslateTransform.YProperty, null);
            _inactiveLyricTransform.Y = distance;
            _activeLyricTransform.Y = 0;

            var oldAnimation = new DoubleAnimation(0, -distance, duration) { EasingFunction = easing };
            var newAnimation = new DoubleAnimation(distance, 0, duration) { EasingFunction = easing };
            var outgoing = _activeLyric;
            var outgoingTransform = _activeLyricTransform;

            newAnimation.Completed += (_, _) =>
            {
                outgoing.Visibility = Visibility.Collapsed;
                outgoingTransform.BeginAnimation(TranslateTransform.YProperty, null);
                outgoingTransform.Y = 0;
            };

            _activeLyricTransform.BeginAnimation(TranslateTransform.YProperty, oldAnimation);
            _inactiveLyricTransform.BeginAnimation(TranslateTransform.YProperty, newAnimation);

            (_activeLyric, _inactiveLyric) = (_inactiveLyric, _activeLyric);
            (_activeLyricTransform, _inactiveLyricTransform) = (_inactiveLyricTransform, _activeLyricTransform);
        }

        private async void PreviousTrack_Click(object sender, RoutedEventArgs e)
        {
            if (PreviousTrackRequested != null) await PreviousTrackRequested();
        }

        private async void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (PlayPauseRequested != null) await PlayPauseRequested();
        }

        private async void NextTrack_Click(object sender, RoutedEventArgs e)
        {
            if (NextTrackRequested != null) await NextTrackRequested();
        }

        private void LockPosition_Click(object sender, RoutedEventArgs e)
        {
            _settings.DesktopWidgetLocked = LockPositionMenuItem.IsChecked;
            _settings.Save();
        }

        private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
        private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

        private void WidgetRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_settings.DesktopWidgetLocked || IsFromButton(e.OriginalSource as DependencyObject)) return;
            // Dragging is sampled from the native cursor timer so it uses one
            // coordinate space on every monitor, even when Explorer owns input.
            e.Handled = true;
        }

        private void WidgetRoot_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_fallbackOwnsGesture) return;
            if (!_isDragging || !UnmanagedMethods.GetCursorPos(out var pointer)) return;
            UpdateDrag(pointer);
        }

        private void WidgetRoot_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_fallbackOwnsGesture) return;
            if (!_isDragging) return;
            FinishDrag(WidgetRoot.IsMouseCaptured);
            e.Handled = true;
        }

        private void FallbackInputTimer_Tick(object? sender, EventArgs e)
        {
            IntPtr handle = WindowHandle;
            if (handle == IntPtr.Zero ||
                !UnmanagedMethods.GetCursorPos(out var pointer) ||
                !UnmanagedMethods.GetWindowRect(handle, out var bounds))
            {
                ResetFallbackInput();
                return;
            }

            bool leftDown = (UnmanagedMethods.GetAsyncKeyState(UnmanagedMethods.VK_LBUTTON) & 0x8000) != 0;
            bool inside = pointer.X >= bounds.Left && pointer.X < bounds.Right &&
                          pointer.Y >= bounds.Top && pointer.Y < bounds.Bottom;
            bool nativeHit = inside && UnmanagedMethods.WindowFromPoint(pointer) == handle;

            if (leftDown && !_fallbackLeftWasDown && inside && !_isDragging)
            {
                Button? hitButton = HitTestButton(pointer, bounds);
                if (hitButton == null && !_settings.DesktopWidgetLocked)
                {
                    _fallbackOwnsGesture = true;
                    StartDrag(pointer, false);
                }
                else if (!nativeHit && hitButton != null)
                {
                    _fallbackOwnsGesture = true;
                    _fallbackPressedButton = hitButton;
                }
            }

            if (leftDown && _fallbackOwnsGesture && _isDragging)
            {
                UpdateDrag(pointer);
            }

            if (!leftDown && _fallbackLeftWasDown && _fallbackOwnsGesture)
            {
                Button? releasedOver = inside ? HitTestButton(pointer, bounds) : null;
                Button? pressedButton = _fallbackPressedButton;
                if (_isDragging)
                {
                    FinishDrag(false);
                }
                else if (pressedButton != null && releasedOver == pressedButton)
                {
                    pressedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                ResetFallbackInput();
            }

            _fallbackLeftWasDown = leftDown;
        }

        private Button? HitTestButton(UnmanagedMethods.POINT pointer, UnmanagedMethods.RECT bounds)
        {
            int pixelWidth = Math.Max(1, bounds.Right - bounds.Left);
            int pixelHeight = Math.Max(1, bounds.Bottom - bounds.Top);
            double width = ActualWidth > 0 ? ActualWidth : DesktopWidgetLayout.Width;
            double height = ActualHeight > 0 ? ActualHeight : DesktopWidgetLayout.Height;
            var point = new System.Windows.Point(
                (pointer.X - bounds.Left) * width / pixelWidth,
                (pointer.Y - bounds.Top) * height / pixelHeight);
            DependencyObject? hit = InputHitTest(point) as DependencyObject;
            while (hit != null)
            {
                if (hit is Button button) return button;
                hit = VisualTreeHelper.GetParent(hit);
            }
            return null;
        }

        private void StartDrag(UnmanagedMethods.POINT pointer, bool captureMouse)
        {
            _dragStartPointer = pointer;
            _isDragging = true;
            _dragScreen = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point(pointer.X, pointer.Y));
            if (UnmanagedMethods.GetWindowRect(WindowHandle, out var bounds))
            {
                _dragStartLeft = bounds.Left;
                _dragStartTop = bounds.Top;
                _settings.DesktopWidgetLeft = bounds.Left;
                _settings.DesktopWidgetTop = bounds.Top;
            }
            else
            {
                _dragStartLeft = _settings.DesktopWidgetLeft;
                _dragStartTop = _settings.DesktopWidgetTop;
            }
            if (captureMouse) WidgetRoot.CaptureMouse();
        }

        private void UpdateDrag(UnmanagedMethods.POINT pointer)
        {
            _settings.DesktopWidgetLeft = _dragStartLeft + pointer.X - _dragStartPointer.X;
            _settings.DesktopWidgetTop = _dragStartTop + pointer.Y - _dragStartPointer.Y;
            _dragScreen = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point(pointer.X, pointer.Y));
            ScheduleDragMove();
        }

        private void FinishDrag(bool releaseMouseCapture)
        {
            _isDragging = false;
            _dragMoveScheduled = false;
            if (releaseMouseCapture) WidgetRoot.ReleaseMouseCapture();
            MoveToSavedPosition(_dragScreen, true);
            _dragScreen = null;
            _settings.Save();
        }

        private void ResetFallbackInput()
        {
            _fallbackLeftWasDown = false;
            _fallbackOwnsGesture = false;
            _fallbackPressedButton = null;
        }

        private void ScheduleDragMove()
        {
            if (_dragMoveScheduled) return;
            _dragMoveScheduled = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _dragMoveScheduled = false;
                if (_isDragging)
                {
                    MoveToSavedPosition(_dragScreen, false, true);
                }
            }), DispatcherPriority.Render);
        }

        private void MoveToSavedPosition(
            System.Windows.Forms.Screen? preferredScreen = null,
            bool clampToWorkArea = true,
            bool positionOnly = false)
        {
            if (WindowHandle == IntPtr.Zero) return;
            WidgetPlacement placement = CalculatePlacement(preferredScreen, clampToWorkArea);
            _settings.DesktopWidgetLeft = placement.X;
            _settings.DesktopWidgetTop = placement.Y;

            bool moved = _hostService.Move(
                WindowHandle,
                placement.X,
                placement.Y,
                placement.Width,
                placement.Height,
                positionOnly);
            if (moved && UnmanagedMethods.GetWindowRect(WindowHandle, out var actualBounds))
            {
                _settings.DesktopWidgetLeft = actualBounds.Left;
                _settings.DesktopWidgetTop = actualBounds.Top;
                if (clampToWorkArea)
                {
                    UpdateMonitorPlacement(new WidgetPlacement(
                        actualBounds.Left,
                        actualBounds.Top,
                        actualBounds.Right - actualBounds.Left,
                        actualBounds.Bottom - actualBounds.Top,
                        placement.Screen,
                        placement.DpiScaleX,
                        placement.DpiScaleY));
                }
            }
        }

        private WidgetPlacement CalculatePlacement(
            System.Windows.Forms.Screen? preferredScreen,
            bool clampToWorkArea)
        {
            int targetX = (int)Math.Round(_settings.DesktopWidgetLeft);
            int targetY = (int)Math.Round(_settings.DesktopWidgetTop);
            var targetPoint = new System.Drawing.Point(targetX, targetY);
            var savedScreen = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(screen =>
                string.Equals(
                    screen.DeviceName,
                    _settings.DesktopWidgetMonitorDeviceName,
                    StringComparison.OrdinalIgnoreCase));
            var screen = preferredScreen ?? savedScreen ?? System.Windows.Forms.Screen.FromPoint(targetPoint);
            if (preferredScreen == null &&
                savedScreen != null &&
                _settings.DesktopWidgetMonitorOffsetX.HasValue &&
                _settings.DesktopWidgetMonitorOffsetY.HasValue)
            {
                targetX = savedScreen.WorkingArea.Left +
                          (int)Math.Round(_settings.DesktopWidgetMonitorOffsetX.Value);
                targetY = savedScreen.WorkingArea.Top +
                          (int)Math.Round(_settings.DesktopWidgetMonitorOffsetY.Value);
            }
            var monitorDpi = DesktopHostService.GetDpiScaleForPoint(
                screen.Bounds.Left + screen.Bounds.Width / 2,
                screen.Bounds.Top + screen.Bounds.Height / 2);
            double dpiScaleX = _source == null ? monitorDpi.X : _windowDpiScaleX;
            double dpiScaleY = _source == null ? monitorDpi.Y : _windowDpiScaleY;
            DesktopWidgetPixelSize size = DesktopWidgetLayout.GetPixelSize(dpiScaleX, dpiScaleY);

            if (!clampToWorkArea)
            {
                return new WidgetPlacement(
                    targetX,
                    targetY,
                    size.Width,
                    size.Height,
                    screen,
                    dpiScaleX,
                    dpiScaleY);
            }

            var workArea = screen.WorkingArea;
            DesktopWidgetPosition position = DesktopWidgetLayout.ClampToWorkArea(
                targetX,
                targetY,
                size.Width,
                size.Height,
                workArea.Left,
                workArea.Top,
                workArea.Right,
                workArea.Bottom);
            return new WidgetPlacement(
                position.X,
                position.Y,
                size.Width,
                size.Height,
                screen,
                dpiScaleX,
                dpiScaleY);
        }

        private void UpdateMonitorPlacement(WidgetPlacement placement)
        {
            var workArea = placement.Screen.WorkingArea;
            _settings.DesktopWidgetMonitorDeviceName = placement.Screen.DeviceName;
            _settings.DesktopWidgetMonitorOffsetX = placement.X - workArea.Left;
            _settings.DesktopWidgetMonitorOffsetY = placement.Y - workArea.Top;
        }

        private void ApplyRoundedWindowRegion()
        {
            IntPtr handle = WindowHandle;
            if (handle == IntPtr.Zero ||
                !UnmanagedMethods.GetWindowRect(handle, out var bounds)) return;

            int width = Math.Max(1, bounds.Right - bounds.Left);
            int height = Math.Max(1, bounds.Bottom - bounds.Top);
            var dpi = DesktopHostService.GetDpiScaleForPoint(
                bounds.Left + width / 2,
                bounds.Top + height / 2);
            int diameter = Math.Max(2, (int)Math.Round(32 * dpi.X));
            IntPtr region = UnmanagedMethods.CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
            if (region == IntPtr.Zero) return;

            if (UnmanagedMethods.SetWindowRgn(handle, region, true) == 0)
            {
                UnmanagedMethods.DeleteObject(region);
            }
        }

        private void ResetAlbumArt()
        {
            AlbumArtBrush.ImageSource = App.GetAppIcon();
            AlbumArtBrush.Stretch = Stretch.UniformToFill;
        }

        private void ApplyTheme(DesktopWidgetTheme theme)
        {
            var palette = DesktopWidgetThemePalette.Get(theme);
            SetThemeBrush("DesktopWidgetWindowBackgroundBrush", palette.WindowBackground);
            SetThemeBrush("DesktopWidgetCardBackgroundBrush", palette.CardBackground);
            SetThemeBrush("DesktopWidgetCardBorderBrush", palette.CardBorder);
            SetThemeBrush("DesktopWidgetPrimaryTextBrush", palette.PrimaryText);
            SetThemeBrush("DesktopWidgetSecondaryTextBrush", palette.SecondaryText);
            SetThemeBrush("DesktopWidgetTimeTextBrush", palette.SecondaryText);
            SetThemeBrush("DesktopWidgetLyricTextBrush", palette.LyricText);
            SetThemeBrush("DesktopWidgetControlForegroundBrush", palette.ControlForeground);
            SetThemeBrush("DesktopWidgetControlHoverBrush", palette.ControlHover);
            SetThemeBrush("DesktopWidgetControlPressedBrush", palette.ControlPressed);
            SetThemeBrush("DesktopWidgetProgressTrackBrush", palette.ProgressTrack);
            SetThemeBrush("DesktopWidgetAccentBrush", palette.Accent);
            Resources["DesktopWidgetAccentColor"] = ParseThemeColor(palette.Accent);
            ApplyCompositionBackground(palette.WindowBackground);
        }

        private void SetThemeBrush(string key, string color)
        {
            Resources[key] = new SolidColorBrush(ParseThemeColor(color));
        }

        private static Color ParseThemeColor(string color)
        {
            return (Color)ColorConverter.ConvertFromString(color);
        }

        private void ApplyCompositionBackground(string windowBackground)
        {
            if (_source?.CompositionTarget == null) return;

            Color color = ParseThemeColor(windowBackground);
            _source.CompositionTarget.BackgroundColor = Color.FromRgb(color.R, color.G, color.B);
        }

        private void RunOnUi(Action action)
        {
            if (CheckAccess()) action();
            else Dispatcher.Invoke(action);
        }

        private static bool IsFromButton(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is System.Windows.Controls.Button) return true;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        private readonly record struct WidgetPlacement(
            int X,
            int Y,
            int Width,
            int Height,
            System.Windows.Forms.Screen Screen,
            double DpiScaleX,
            double DpiScaleY);
    }
}
