using System;
using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;

namespace TaskbarInfo
{
    public partial class FloatingLyricsWindow : Window
    {
        private AppSettings _settings;

        public FloatingLyricsWindow()
        {
            InitializeComponent();
            _settings = AppSettings.Load();
            ApplySettings();
            LockPositionMenuItem.IsChecked = _settings.FloatingLyricsLocked;
            this.Icon = App.GetAppIcon();
        }

        public void ApplySettings()
        {
            try
            {
                var mainColor = (Color)ColorConverter.ConvertFromString(_settings.TextColor);
                var activeColor = (Color)ColorConverter.ConvertFromString(_settings.ActiveTextColor);
                
                if (LyricText.Foreground is LinearGradientBrush brush && brush.GradientStops.Count >= 2)
                {
                    brush.GradientStops[0].Color = activeColor;
                    brush.GradientStops[1].Color = mainColor;
                }
                
                LyricText.FontFamily = new FontFamily(_settings.FontFamily);
                LyricText.FontSize = _settings.FontSize + 8; // Floating window usually larger
            }
            catch { }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            SetClickThrough(_settings.FloatingLyricsClickThrough);
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
                this.DragMove();
        }

        private void LockPosition_Click(object sender, RoutedEventArgs e)
        {
            _settings.FloatingLyricsLocked = LockPositionMenuItem.IsChecked;
            _settings.Save();
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
