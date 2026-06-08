using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;

namespace TaskbarInfo
{
    public partial class FloatingLyricsWindow : Window
    {
        private AppSettings _settings;
        public bool IsAcrylicMode { get; }
        public event Action? CloseRequested;
        public event Action? SettingsRequested;

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
            ApplySettings();
            LockPositionMenuItem.IsChecked = _settings.FloatingLyricsLocked;
            this.Icon = App.GetAppIcon();
            this.Loaded += (_, _) => ApplySavedPosition();
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
                
                if (LyricText.Foreground is LinearGradientBrush brush && brush.GradientStops.Count >= 2)
                {
                    brush.GradientStops[0].Color = activeColor;
                    brush.GradientStops[1].Color = mainColor;
                }

                ApplyAcrylicBackdrop(_settings.FloatingLyricsUseAcrylic, backgroundColor);
                Background = System.Windows.Media.Brushes.Transparent;
                FloatingBackground.Background = new SolidColorBrush(GetDisplayBackgroundColor(backgroundColor));
                FloatingBackground.Opacity = 1.0;
                
                LyricText.FontFamily = new FontFamily(_settings.FloatingLyricsFontFamily);
                LyricText.FontSize = _settings.FloatingLyricsFontSize;
                
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
            }
            catch { }
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

                int cornerPreference = enable
                    ? (int)UnmanagedMethods.DwmWindowCornerPreference.DWMWCP_ROUND
                    : (int)UnmanagedMethods.DwmWindowCornerPreference.DWMWCP_DONOTROUND;
                UnmanagedMethods.DwmSetWindowAttribute(
                    hwnd,
                    UnmanagedMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
                    ref cornerPreference,
                    sizeof(int));

                int backdropType = enable
                    ? (int)UnmanagedMethods.DwmSystemBackdropType.DWMSBT_NONE
                    : (int)UnmanagedMethods.DwmSystemBackdropType.DWMSBT_NONE;

                UnmanagedMethods.DwmSetWindowAttribute(
                    hwnd,
                    UnmanagedMethods.DWMWA_SYSTEMBACKDROP_TYPE,
                    ref backdropType,
                    sizeof(int));

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
                LyricText.Text = lyrics;
            }
            else
            {
                Dispatcher.Invoke(() => UpdateLyrics(lyrics));
            }
        }
        public void UpdateProgress(double progress)
        {
            if (CheckAccess())
            {
                if (LyricText.Foreground is LinearGradientBrush brush && brush.GradientStops.Count >= 2)
                {
                    brush.GradientStops[0].Offset = progress;
                    brush.GradientStops[1].Offset = Math.Min(1.0, progress + 0.05);
                }
            }
            else
            {
                Dispatcher.Invoke(() => UpdateProgress(progress));
            }
        }
    }
}
