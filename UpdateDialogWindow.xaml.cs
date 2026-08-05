using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaColors = System.Windows.Media.Colors;
namespace TaskbarInfo
{
    public partial class UpdateDialogWindow : Window
    {
        private const byte AcrylicTintOpacity = 96;
        private string? _primaryUrl;
        private string _settingsWindowMaterial = "Mica";

        private UpdateDialogWindow()
        {
            InitializeComponent();
            Icon = App.GetAppIcon();
            SourceInitialized += (_, _) => ApplyWindowMaterial();
            ContentRendered += (_, _) => ApplyWindowMaterial();
        }

        public static bool? ShowForResult(Window owner, UpdateCheckResult result, string settingsWindowMaterial)
        {
            var window = new UpdateDialogWindow
            {
                Owner = owner
            };

            window.SetSettingsWindowMaterial(settingsWindowMaterial);
            window.ApplyResult(result);
            return window.ShowDialog();
        }

        public static bool? ShowForError(Window owner, string message, string settingsWindowMaterial)
        {
            var window = new UpdateDialogWindow
            {
                Owner = owner
            };

            window.SetSettingsWindowMaterial(settingsWindowMaterial);
            window.ApplyError(message);
            return window.ShowDialog();
        }

        private void ApplyResult(UpdateCheckResult result)
        {
            _primaryUrl = null;
            NotesPanel.Visibility = Visibility.Collapsed;
            PrimaryButton.Visibility = Visibility.Collapsed;
            CurrentVersionText.Text = result.CurrentVersionDisplay;
            LatestVersionText.Text = result.NoReleasePublished ? "-" : result.LatestVersionDisplay;
            PublishedAtText.Text = result.PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "-";

            if (result.HasUpdate)
            {
                TitleText.Text = "发现新版本";
                SummaryText.Text = $"当前版本 {result.CurrentVersionDisplay}，发现了新的版本 {result.ReleaseTag}。";
                ShowNotes("更新说明", string.IsNullOrWhiteSpace(result.ReleaseNotes)
                    ? "这个版本暂时没有填写更新说明。"
                    : result.ReleaseNotes.Trim());
                PrimaryButton.Visibility = Visibility.Visible;
                PrimaryButton.Content = "打开下载页";
                _primaryUrl = result.DownloadUrl;
                SetAccentVisual("#EAF4FF", "#0067C0", "!");
                return;
            }

            if (result.NoReleasePublished)
            {
                TitleText.Text = "暂无发行版";
                SummaryText.Text = "仓库里还没有发布正式版本。";
                ShowNotes("说明", "可以先在 GitHub Releases 中创建第一个发行版，之后应用就能正常检测更新了。");
                SetAccentVisual("#F4F5F7", "#4B5563", "i");
                return;
            }

            TitleText.Text = "已是最新版本";
            SummaryText.Text = $"当前运行的就是最新版本 {result.CurrentVersionDisplay}。";
            SetAccentVisual("#EEF7EE", "#2E7D32", "✓");
        }

        private void ApplyError(string message)
        {
            _primaryUrl = null;
            PrimaryButton.Visibility = Visibility.Collapsed;
            TitleText.Text = "检查更新失败";
            SummaryText.Text = "这次没有成功拿到更新信息。";
            CurrentVersionText.Text = UpdateService.CurrentVersionDisplay;
            LatestVersionText.Text = "-";
            PublishedAtText.Text = "-";
            ShowNotes("错误信息", string.IsNullOrWhiteSpace(message) ? "发生了未知错误。" : message.Trim());
            SetAccentVisual("#FFF4E5", "#C77700", "!");
        }

        private void ShowNotes(string title, string content)
        {
            NotesTitleText.Text = title;
            NotesText.Text = content;
            NotesPanel.Visibility = Visibility.Visible;
        }

        private void SetSettingsWindowMaterial(string settingsWindowMaterial)
        {
            _settingsWindowMaterial = settingsWindowMaterial;
            ApplyWindowMaterial();
        }

        private void ApplyWindowMaterial()
        {
            QuickTranslateWindowMaterial material = QuickTranslateWindowMaterialParser.Parse(_settingsWindowMaterial);
            RootSurface.Background = material switch
            {
                QuickTranslateWindowMaterial.Solid => new SolidColorBrush(MediaColor.FromRgb(245, 247, 250)),
                QuickTranslateWindowMaterial.Mica => new SolidColorBrush(MediaColor.FromArgb(112, 245, 247, 250)),
                _ => MediaBrushes.Transparent
            };
            Resources["CardBackground"] = material switch
            {
                QuickTranslateWindowMaterial.Acrylic => new SolidColorBrush(MediaColor.FromArgb(224, 255, 255, 255)),
                QuickTranslateWindowMaterial.Mica => new SolidColorBrush(MediaColor.FromArgb(228, 255, 255, 255)),
                _ => new SolidColorBrush(MediaColors.White)
            };

            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                int backdropType = material == QuickTranslateWindowMaterial.Mica
                    ? (int)UnmanagedMethods.DwmSystemBackdropType.DWMSBT_MAINWINDOW
                    : (int)UnmanagedMethods.DwmSystemBackdropType.DWMSBT_NONE;
                UnmanagedMethods.DwmSetWindowAttribute(
                    handle,
                    UnmanagedMethods.DWMWA_SYSTEMBACKDROP_TYPE,
                    ref backdropType,
                    sizeof(int));
            }

            if (material == QuickTranslateWindowMaterial.Acrylic)
            {
                ApplyAcrylicBackdrop(handle);
            }
            else
            {
                ClearAcrylicBackdrop(handle);
            }
        }

        private static void ApplyAcrylicBackdrop(IntPtr handle)
        {
            var accent = new UnmanagedMethods.AccentPolicy
            {
                AccentState = UnmanagedMethods.AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 0,
                GradientColor = ToAccentColor(MediaColor.FromArgb(AcrylicTintOpacity, 245, 247, 250)),
                AnimationId = 0
            };
            SetAccentPolicy(handle, accent);
        }

        private static void ClearAcrylicBackdrop(IntPtr handle) => SetAccentPolicy(handle, new UnmanagedMethods.AccentPolicy
        {
            AccentState = UnmanagedMethods.AccentState.ACCENT_DISABLED
        });

        private static void SetAccentPolicy(IntPtr handle, UnmanagedMethods.AccentPolicy accent)
        {
            int size = Marshal.SizeOf<UnmanagedMethods.AccentPolicy>();
            IntPtr data = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, data, false);
                var attribute = new UnmanagedMethods.WindowCompositionAttributeData
                {
                    Attribute = UnmanagedMethods.WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    Data = data,
                    SizeOfData = size
                };
                UnmanagedMethods.SetWindowCompositionAttribute(handle, ref attribute);
            }
            finally
            {
                Marshal.FreeHGlobal(data);
            }
        }

        private static int ToAccentColor(MediaColor color) =>
            unchecked((int)((uint)color.A << 24 | (uint)color.B << 16 | (uint)color.G << 8 | color.R));

        private void SetAccentVisual(string background, string foreground, string glyph)
        {
            StatusGlyphBadge.Background = (MediaBrush)new BrushConverter().ConvertFromString(background)!;

            StatusGlyphText.Foreground = (MediaBrush)new BrushConverter().ConvertFromString(foreground)!;
            StatusGlyphText.Text = glyph;
        }

        private void Primary_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_primaryUrl))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_primaryUrl) { UseShellExecute = true });
                }
                catch
                {
                }
            }

            DialogResult = true;
            Close();
        }
    }
}
