using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;

namespace TaskbarInfo
{
    public partial class FloatingLyricsWindow : Window
    {
        private static bool SupportsWindows11DwmAttributes =>
            OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

        private AppSettings _settings;
        private readonly TranslateTransform _marqueeTransform = new();
        private readonly FloatingLyricsResizeCoordinator _resizeCoordinator = new();
        private int _placementLayoutVersion;
        private Color _floatingMainTextColor = Colors.White;
        private Color _floatingActiveTextColor = Color.FromRgb(0x33, 0xBB, 0xFF);
        private bool _hasTimedLyricProgress;
        private double _lyricProgress;
        public bool IsAcrylicMode { get; }
        public event Action? CloseRequested;
        public event Action? SettingsRequested;
        public event Func<Task>? PreviousTrackRequested;
        public event Func<Task>? PlayPauseRequested;
        public event Func<Task>? NextTrackRequested;

        public FloatingLyricsWindow(AppSettings settings)
        {
            IsAcrylicMode = settings.FloatingLyricsUseAcrylic;
            AllowsTransparency = !IsAcrylicMode;
            InitializeComponent();
            if (IsAcrylicMode)
            {
                WindowChrome.SetWindowChrome(this, new WindowChrome
                {
                    GlassFrameThickness = new Thickness(-1),
                    CaptionHeight = 0,
                    ResizeBorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(0)
                });
            }
            _settings = settings;
            MarqueePanel.RenderTransform = _marqueeTransform;
            ApplySettings();
            LockPositionMenuItem.IsChecked = _settings.FloatingLyricsLocked;
            this.Icon = App.GetAppIcon();
            this.Loaded += FloatingLyricsWindow_Loaded;
            this.ContentRendered += FloatingLyricsWindow_ContentRendered;
        }

        private void FloatingLyricsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplySavedPosition();
            SchedulePlacementLayoutRefresh();
        }

        private void FloatingLyricsWindow_ContentRendered(object? sender, EventArgs e)
        {
            // Transparent/chromeless HWNDs can complete their first render with a backing surface
            // sized in physical pixels as if they were DIPs. Re-assert the explicit logical width
            // after the first composition pass instead of waiting for a mouse-driven native move.
            SchedulePlacementLayoutRefresh();
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            SchedulePlacementLayoutRefresh();
        }

        private void SchedulePlacementLayoutRefresh()
        {
            int version = ++_placementLayoutVersion;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (version != _placementLayoutVersion) return;

                if (_settings.FloatingLyricsWidth is double savedWidth)
                {
                    SizeToContent = SizeToContent.Height;
                    Width = Clamp(savedWidth, MinWidth, GetMaximumBubbleWidth());
                    ResetViewportToContentWidth();
                }

                InvalidateMeasure();
                InvalidateArrange();
                FloatingBackground.InvalidateVisual();
                UpdateLayout();
                ConfigureMarquee();
                SynchronizeNativeWindowSize();
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        private void SynchronizeNativeWindowSize()
        {
            if (!IsLoaded) return;

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            double logicalWidth = !double.IsNaN(Width) ? Width : ActualWidth;
            double logicalHeight = ActualHeight > 0 ? ActualHeight : MinHeight;
            if (logicalWidth <= 0 || logicalHeight <= 0) return;

            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            int pixelWidth = Math.Max(1, (int)Math.Round(logicalWidth * dpi.DpiScaleX));
            int pixelHeight = Math.Max(1, (int)Math.Round(logicalHeight * dpi.DpiScaleY));
            UnmanagedMethods.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                0,
                0,
                pixelWidth,
                pixelHeight,
                UnmanagedMethods.SWP_NOMOVE |
                UnmanagedMethods.SWP_NOZORDER |
                UnmanagedMethods.SWP_NOACTIVATE |
                UnmanagedMethods.SWP_NOOWNERZORDER |
                UnmanagedMethods.SWP_FRAMECHANGED);
        }

        public void ApplySettings(AppSettings settings)
        {
            _settings = settings;
            ApplySettings();
        }

        public void ApplySettings()
        {
            try
            {
                var mainColor = (Color)ColorConverter.ConvertFromString(_settings.FloatingLyricsTextColor);
                var activeColor = (Color)ColorConverter.ConvertFromString(_settings.ActiveTextColor);
                var backgroundColor = (Color)ColorConverter.ConvertFromString(_settings.FloatingLyricsBackgroundColor);

                _floatingMainTextColor = mainColor;
                _floatingActiveTextColor = activeColor;
                ApplyLyricProgressBrush();

                ApplyAcrylicBackdrop(_settings.FloatingLyricsUseAcrylic, backgroundColor);
                Background = System.Windows.Media.Brushes.Transparent;
                FloatingBackground.Background = new SolidColorBrush(GetDisplayBackgroundColor(backgroundColor));
                FloatingBackground.Opacity = 1.0;
                
                LyricText.FontFamily = new FontFamily(_settings.FloatingLyricsFontFamily);
                LyricText.FontSize = _settings.FloatingLyricsFontSize;
                ApplyResponsiveBubbleMetrics(_settings.FloatingLyricsFontSize);
                UpdateFontSizeMenuState();
                
                try
                {
                    if (!string.IsNullOrEmpty(_settings.FloatingLyricsFontWeight))
                    {
                        var weightStr = _settings.FloatingLyricsFontWeight.Split(' ')[0];
                        var converter = new FontWeightConverter();
                        var obj = converter.ConvertFromString(weightStr);
                        LyricText.FontWeight = (obj as FontWeight?) ?? FontWeights.Bold;
                    }
                    else
                    {
                        LyricText.FontWeight = FontWeights.Bold;
                    }
                }
                catch
                {
                    LyricText.FontWeight = FontWeights.Bold;
                }

                LyricText.Effect = null;
                if (_settings.FloatingLyricsEnableShadow && _settings.FloatingLyricsUseAcrylic)
                {
                    LyricText.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Colors.Black,
                        BlurRadius = 4,
                        ShadowDepth = 2,
                        Opacity = 0.8
                    };
                }

                ScheduleMarqueeUpdate();
            }
            catch { }
        }

        private void ApplyResponsiveBubbleMetrics(double fontSize)
        {
            const double defaultFontSize = 20;
            double scale = fontSize / defaultFontSize;
            double horizontalPadding = Math.Clamp(12 * scale, 7, 24);
            double verticalPadding = Math.Clamp(12 * scale, 6, 24);
            double minimumViewportWidth = Math.Clamp(176 * scale, 104, 320);
            double lineHeight = Math.Ceiling(fontSize * 1.25);

            FloatingBackground.Padding = new Thickness(horizontalPadding, verticalPadding,
                horizontalPadding, verticalPadding);
            FloatingBackground.CornerRadius = new CornerRadius(Math.Clamp(10 * scale, 7, 18));
            LyricViewport.MinWidth = minimumViewportWidth;
            LyricText.LineHeight = lineHeight;

            MinWidth = Math.Ceiling(minimumViewportWidth + horizontalPadding * 2);
            MinHeight = Math.Ceiling(lineHeight + verticalPadding * 2);
            SizeToContent = SizeToContent.Height;

            if (_settings.FloatingLyricsWidth is double savedWidth)
            {
                Width = Clamp(savedWidth, MinWidth, GetMaximumBubbleWidth());
            }
            else if (double.IsNaN(Width))
            {
                Width = MinWidth;
            }
        }

        private double GetMaximumBubbleWidth()
        {
            return Math.Max(MinWidth, SystemParameters.WorkArea.Width * 0.9);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplySettings();
            MakeCompositionTargetTransparent();
            if (!IsAcrylicMode)
            {
                DisableDwmEffects();
            }
            SetClickThrough(_settings.FloatingLyricsClickThrough);
        }

        private void MakeCompositionTargetTransparent()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                var source = HwndSource.FromHwnd(hwnd);
                if (source?.CompositionTarget != null)
                {
                    source.CompositionTarget.BackgroundColor = Colors.Transparent;
                }
            }
            catch { }
        }

        private void DisableDwmEffects()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                if (SupportsWindows11DwmAttributes)
                {
                    int cornerPreference = (int)UnmanagedMethods.DwmWindowCornerPreference.DWMWCP_DONOTROUND;
                    UnmanagedMethods.DwmSetWindowAttribute(
                        hwnd,
                        UnmanagedMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
                        ref cornerPreference,
                        sizeof(int));

                    int backdropType = (int)UnmanagedMethods.DwmSystemBackdropType.DWMSBT_NONE;
                    UnmanagedMethods.DwmSetWindowAttribute(
                        hwnd,
                        UnmanagedMethods.DWMWA_SYSTEMBACKDROP_TYPE,
                        ref backdropType,
                        sizeof(int));
                }

                ApplyAcrylicBackdrop(false, Colors.Transparent);
            }
            catch { }
        }

        private Color GetDisplayBackgroundColor(Color backgroundColor)
        {
            if (_settings.FloatingLyricsUseAcrylic)
            {
                return GetAcrylicTintColor(backgroundColor, 26);
            }

            return backgroundColor;
        }

        private void ApplyAcrylicBackdrop(bool enable, Color tintColor)
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                if (SupportsWindows11DwmAttributes)
                {
                    int cornerPreference = enable
                        ? (int)UnmanagedMethods.DwmWindowCornerPreference.DWMWCP_ROUND
                        : (int)UnmanagedMethods.DwmWindowCornerPreference.DWMWCP_DONOTROUND;
                    UnmanagedMethods.DwmSetWindowAttribute(
                        hwnd,
                        UnmanagedMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
                        ref cornerPreference,
                        sizeof(int));

                    int backdropType = (int)UnmanagedMethods.DwmSystemBackdropType.DWMSBT_NONE;
                    UnmanagedMethods.DwmSetWindowAttribute(
                        hwnd,
                        UnmanagedMethods.DWMWA_SYSTEMBACKDROP_TYPE,
                        ref backdropType,
                        sizeof(int));
                }

                var accent = new UnmanagedMethods.AccentPolicy
                {
                    AccentState = enable
                        ? UnmanagedMethods.AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND
                        : UnmanagedMethods.AccentState.ACCENT_ENABLE_TRANSPARENTGRADIENT,
                    AccentFlags = enable ? 0 : 0x13,
                    GradientColor = enable ? ToAccentColor(tintColor, 64) : 0,
                    AnimationId = 0
                };

                int size = Marshal.SizeOf(accent);
                IntPtr ptr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(accent, ptr, false);
                    var data = new UnmanagedMethods.WindowCompositionAttributeData
                    {
                        Attribute = UnmanagedMethods.WindowCompositionAttribute.WCA_ACCENT_POLICY,
                        Data = ptr,
                        SizeOfData = size
                    };

                    UnmanagedMethods.SetWindowCompositionAttribute(hwnd, ref data);
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }

            }
            catch { }
        }

        private static int ToAccentColor(Color color, byte opacity)
        {
            return unchecked((int)((uint)opacity << 24 | (uint)color.B << 16 | (uint)color.G << 8 | color.R));
        }

        private static Color GetAcrylicTintColor(Color color, byte opacity)
        {
            return Color.FromArgb(opacity, color.R, color.G, color.B);
        }

        public void SetClickThrough(bool enable)
        {
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                IntPtr hWnd = helper.Handle;
                
                int exStyle = UnmanagedMethods.GetWindowLong(hWnd, UnmanagedMethods.GWL_EXSTYLE);
                
                if (enable)
                {
                    exStyle |= UnmanagedMethods.WS_EX_TRANSPARENT;
                }
                else
                {
                    exStyle &= ~UnmanagedMethods.WS_EX_TRANSPARENT;
                }
                
                UnmanagedMethods.SetWindowLong(hWnd, UnmanagedMethods.GWL_EXSTYLE, exStyle);
            }
            catch {}
        }

        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (WidthResizeThumb.IsMouseOver || WidthResizeThumb.IsMouseCaptureWithin)
            {
                return;
            }

            if (_settings.FloatingLyricsLocked) return;

            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                this.DragMove();
                SaveCurrentPosition();
            }
        }

        private void ApplySavedPosition()
        {
            if (_settings.FloatingLyricsLeft == null || _settings.FloatingLyricsTop == null) return;

            Left = Clamp(_settings.FloatingLyricsLeft.Value, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - ActualWidth);
            Top = Clamp(_settings.FloatingLyricsTop.Value, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - ActualHeight);
        }

        private void SaveCurrentPosition()
        {
            _settings.FloatingLyricsLeft = Left;
            _settings.FloatingLyricsTop = Top;
            _settings.Save();
        }

        private static double Clamp(double value, double min, double max)
        {
            if (max < min) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private void LockPosition_Click(object sender, RoutedEventArgs e)
        {
            _settings.FloatingLyricsLocked = LockPositionMenuItem.IsChecked;
            _settings.Save();
        }

        private void WidthResizeThumb_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;

            if (e.ClickCount >= 2)
            {
                ResetBubbleWidthToAutomatic();
                return;
            }

            double currentWidth = ActualWidth > 0 ? ActualWidth : MinWidth;
            SizeToContent = SizeToContent.Height;
            Width = currentWidth;
            MaxWidth = GetMaximumBubbleWidth();

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            _resizeCoordinator.BeginNativeWidthResize();
            StopMarquee();
            try
            {
                UnmanagedMethods.ReleaseCapture();
                UnmanagedMethods.SendMessage(
                    hwnd,
                    UnmanagedMethods.WM_SYSCOMMAND,
                    (IntPtr)(UnmanagedMethods.SC_SIZE | UnmanagedMethods.WMSZ_RIGHT),
                    IntPtr.Zero);
            }
            finally
            {
                _resizeCoordinator.EndNativeWidthResize();
                double finalWidth = Clamp(ActualWidth, MinWidth, GetMaximumBubbleWidth());
                Width = finalWidth;
                _settings.FloatingLyricsWidth = finalWidth;
                ResetViewportToContentWidth();
                ScheduleMarqueeUpdate();
                _settings.Save();
            }
        }

        private void ResetViewportToContentWidth()
        {
            // The Border already subtracts its padding when arranging the viewport. Keeping the
            // viewport on Auto avoids mixing the Window's outer width with its DPI-dependent
            // client width, which previously left the right side one resize-border short.
            LyricViewport.Width = double.NaN;
        }

        private void ResetBubbleWidthToAutomatic()
        {
            _settings.FloatingLyricsWidth = null;
            MaxWidth = double.PositiveInfinity;
            SizeToContent = SizeToContent.Height;
            Width = MinWidth;
            ScheduleMarqueeUpdate();
            _settings.Save();
        }

        private async void PreviousTrack_Click(object sender, RoutedEventArgs e)
        {
            CloseContextMenu();
            if (PreviousTrackRequested != null) await PreviousTrackRequested();
        }

        private async void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            CloseContextMenu();
            if (PlayPauseRequested != null) await PlayPauseRequested();
        }

        private async void NextTrack_Click(object sender, RoutedEventArgs e)
        {
            CloseContextMenu();
            if (NextTrackRequested != null) await NextTrackRequested();
        }

        private void CloseContextMenu()
        {
            if (FloatingBackground.ContextMenu != null)
            {
                FloatingBackground.ContextMenu.IsOpen = false;
            }
        }

        private void DecreaseFontSize_Click(object sender, RoutedEventArgs e)
        {
            ChangeFontSize(-2);
        }

        private void ResetFontSize_Click(object sender, RoutedEventArgs e)
        {
            _settings.FloatingLyricsFontSize = 20;
            ApplySettings();
            _settings.Save();
        }

        private void IncreaseFontSize_Click(object sender, RoutedEventArgs e)
        {
            ChangeFontSize(2);
        }

        private void ChangeFontSize(double delta)
        {
            _settings.FloatingLyricsFontSize = Math.Clamp(_settings.FloatingLyricsFontSize + delta, 12, 64);
            ApplySettings();
            _settings.Save();
        }

        private void UpdateFontSizeMenuState()
        {
            double fontSize = _settings.FloatingLyricsFontSize;
            FloatingFontSizeText.Text = $"{fontSize:F0} px";
            DecreaseFontSizeMenuItem.IsEnabled = fontSize > 12;
            IncreaseFontSizeMenuItem.IsEnabled = fontSize < 64;
        }

        public void SetPlaybackState(bool isPlaying)
        {
            if (!CheckAccess())
            {
                Dispatcher.Invoke(() => SetPlaybackState(isPlaying));
                return;
            }

            FloatingPlayPauseButton.ToolTip = isPlaying ? "暂停" : "播放";
            FloatingPlayPauseIcon.Text = isPlaying ? "\uf04c" : "\uf04b";
        }

        private void CloseFloatingLyrics_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke();
        }

        private void OpenFloatingSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsRequested?.Invoke();
        }

        public void UpdateLyrics(string lyrics)
        {
            if (CheckAccess())
            {
                if (string.Equals(LyricText.Text, lyrics, StringComparison.Ordinal)) return;

                LyricText.Text = lyrics;
                _hasTimedLyricProgress = false;
                _lyricProgress = 0;
                ApplyLyricProgressBrush();
                ScheduleMarqueeUpdate();
            }
            else
            {
                Dispatcher.Invoke(() => UpdateLyrics(lyrics));
            }
        }

        private void ScheduleMarqueeUpdate()
        {
            int version = _resizeCoordinator.ScheduleMarqueeUpdate();
            StopMarquee();

            if (_resizeCoordinator.IsNativeWidthResizing)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_resizeCoordinator.CanApplyMarqueeUpdate(version)) return;
                ConfigureMarquee();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void ConfigureMarquee()
        {
            if (_resizeCoordinator.IsNativeWidthResizing) return;

            StopMarquee();
            LyricTextDuplicate.Visibility = Visibility.Collapsed;
            Canvas.SetLeft(MarqueePanel, 0);
            Canvas.SetTop(MarqueePanel, 0);
            MarqueePanel.ClearValue(FrameworkElement.WidthProperty);
            LyricText.ClearValue(FrameworkElement.WidthProperty);
            LyricTextDuplicate.ClearValue(FrameworkElement.WidthProperty);
            LyricText.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));

            // Reserve a small layout-rounding guard for glyph overhang. More importantly, assign
            // the unbounded measurement back to both TextBlocks so a later viewport measure cannot
            // truncate their visual to only the initially visible section.
            double textWidth = FloatingLyricsLayout.GetTextRenderWidth(LyricText.DesiredSize.Width);
            double textHeight = Math.Ceiling(LyricText.DesiredSize.Height);
            double maxViewportWidth = Math.Min(680, Math.Max(320, SystemParameters.WorkArea.Width * 0.6));
            double requestedViewportWidth;

            if (_settings.FloatingLyricsWidth.HasValue)
            {
                Width = Clamp(_settings.FloatingLyricsWidth.Value, MinWidth, GetMaximumBubbleWidth());
                requestedViewportWidth = Math.Max(LyricViewport.MinWidth,
                    Width - FloatingBackground.Padding.Left - FloatingBackground.Padding.Right);
            }
            else
            {
                requestedViewportWidth = Math.Max(LyricViewport.MinWidth, Math.Min(textWidth, maxViewportWidth));
                Width = FloatingLyricsLayout.GetBubbleWidth(
                    requestedViewportWidth,
                    FloatingBackground.Padding.Left,
                    FloatingBackground.Padding.Right,
                    MinWidth,
                    GetMaximumBubbleWidth());
            }

            SizeToContent = SizeToContent.Height;
            LyricViewport.Width = double.NaN;
            LyricViewport.Height = Math.Max(LyricText.LineHeight, textHeight);
            LyricText.Width = textWidth;
            LyricTextDuplicate.Width = textWidth;
            UpdateLayout();

            double viewportWidth = LyricViewport.ActualWidth;
            if (viewportWidth <= 0)
            {
                viewportWidth = requestedViewportWidth;
            }

            if (textWidth <= viewportWidth + 1)
            {
                MarqueePanel.Width = textWidth;
                Canvas.SetLeft(MarqueePanel, Math.Max(0, (viewportWidth - textWidth) / 2));
                Canvas.SetTop(MarqueePanel, Math.Max(0, (LyricViewport.ActualHeight - textHeight) / 2));
                MarqueePanel.UpdateLayout();
                return;
            }

            LyricTextDuplicate.Visibility = Visibility.Visible;

            double distance = textWidth + FloatingLyricsLayout.MarqueeGap;
            MarqueePanel.Width = FloatingLyricsLayout.GetMarqueePanelWidth(textWidth);
            Canvas.SetLeft(MarqueePanel, 0);
            Canvas.SetTop(MarqueePanel, Math.Max(0, (LyricViewport.ActualHeight - textHeight) / 2));
            MarqueePanel.UpdateLayout();
            double durationSeconds = Math.Max(4, distance / 42.0);
            var animation = new DoubleAnimation
            {
                From = 0,
                To = -distance,
                BeginTime = TimeSpan.FromSeconds(1),
                Duration = TimeSpan.FromSeconds(durationSeconds),
                RepeatBehavior = RepeatBehavior.Forever
            };

            _marqueeTransform.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        private void StopMarquee()
        {
            _marqueeTransform.BeginAnimation(TranslateTransform.XProperty, null);
            _marqueeTransform.X = 0;
            LyricTextDuplicate.Visibility = Visibility.Collapsed;
        }

        public void UpdateProgress(double progress, bool hasTimedProgress = true)
        {
            if (CheckAccess())
            {
                _hasTimedLyricProgress = hasTimedProgress;
                _lyricProgress = Math.Clamp(progress, 0, 1);
                ApplyLyricProgressBrush();
            }
            else
            {
                Dispatcher.Invoke(() => UpdateProgress(progress, hasTimedProgress));
            }
        }

        private void ApplyLyricProgressBrush()
        {
            if (LyricText.Foreground is not LinearGradientBrush brush || brush.GradientStops.Count < 2)
            {
                return;
            }

            var activeStop = brush.GradientStops[0];
            var mainStop = brush.GradientStops[1];
            if (FloatingLyricsLayout.ShouldUseActiveColor(_hasTimedLyricProgress))
            {
                activeStop.Color = _floatingActiveTextColor;
                mainStop.Color = _floatingMainTextColor;
                activeStop.Offset = _lyricProgress;
                mainStop.Offset = _lyricProgress;
            }
            else
            {
                // Plain LRC lines have no per-word timing. Render the whole line in the normal
                // color rather than leaving a synthetic colored strip on the first character.
                activeStop.Color = _floatingMainTextColor;
                mainStop.Color = _floatingMainTextColor;
                activeStop.Offset = 0;
                mainStop.Offset = 0;
            }
        }
    }
}
