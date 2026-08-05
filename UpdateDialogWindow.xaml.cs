using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
namespace TaskbarInfo
{
    public partial class UpdateDialogWindow : Window
    {
        private const byte AcrylicTintOpacity = 96;
        private readonly InAppUpdateDownloadService _downloadService = new();
        private string? _primaryUrl;
        private string _settingsWindowMaterial = "Mica";
        private string _applicationTheme = "System";
        private UpdatePackage? _package;
        private CancellationTokenSource? _downloadCancellation;

        private UpdateDialogWindow()
        {
            InitializeComponent();
            Icon = App.GetAppIcon();
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                CornerRadius = new CornerRadius(10),
                GlassFrameThickness = new Thickness(-1),
                ResizeBorderThickness = new Thickness(0)
            });
            SourceInitialized += (_, _) => ApplyWindowMaterial();
            ContentRendered += (_, _) => ApplyWindowMaterial();
        }

        public static bool? ShowForResult(
            Window owner,
            UpdateCheckResult result,
            string settingsWindowMaterial,
            string applicationTheme)
        {
            var window = new UpdateDialogWindow
            {
                Owner = owner
            };

            window.SetWindowAppearance(settingsWindowMaterial, applicationTheme);
            window.ApplyResult(result);
            return window.ShowDialog();
        }

        public static bool? ShowForError(
            Window owner,
            string message,
            string settingsWindowMaterial,
            string applicationTheme)
        {
            var window = new UpdateDialogWindow
            {
                Owner = owner
            };

            window.SetWindowAppearance(settingsWindowMaterial, applicationTheme);
            window.ApplyError(message);
            return window.ShowDialog();
        }

        private void ApplyResult(UpdateCheckResult result)
        {
            _primaryUrl = null;
            _package = null;
            NotesPanel.Visibility = Visibility.Collapsed;
            UpdateProgressPanel.Visibility = Visibility.Collapsed;
            PrimaryButton.Visibility = Visibility.Collapsed;
            PrimaryButton.IsEnabled = true;
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
                _package = result.Package;
                if (_package != null)
                {
                    PrimaryButton.Content = "下载并安装";
                }
                else
                {
                    PrimaryButton.Content = "打开发布页";
                    _primaryUrl = result.ReleasePageUrl;
                }
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
            _package = null;
            PrimaryButton.Visibility = Visibility.Collapsed;
            UpdateProgressPanel.Visibility = Visibility.Collapsed;
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

        private async Task DownloadAndInstallAsync()
        {
            UpdatePackage? package = _package;
            if (package == null || _downloadCancellation != null) return;

            using var cancellation = new CancellationTokenSource();
            _downloadCancellation = cancellation;
            UpdateProgressPanel.Visibility = Visibility.Visible;
            UpdateProgressBar.Value = 0;
            UpdateProgressText.Text = "正在下载更新… 0%";
            PrimaryButton.IsEnabled = false;
            PrimaryButton.Content = "下载中";

            try
            {
                var progress = new Progress<InAppUpdateDownloadProgress>(value =>
                {
                    UpdateProgressBar.Value = value.Fraction;
                    UpdateProgressText.Text = $"正在下载更新… {Math.Round(value.Fraction * 100):0}%";
                });
                string installerPath = await _downloadService.DownloadInstallerAsync(
                    package,
                    progress,
                    cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();

                UpdateProgressBar.Value = 1;
                UpdateProgressText.Text = "下载完成，正在启动安装程序…";
                var installer = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installerPath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(installerPath)
                });
                if (installer == null)
                {
                    throw new InvalidOperationException("无法启动更新安装程序。");
                }

                DialogResult = true;
                System.Windows.Application.Current.Shutdown();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                UpdateProgressPanel.Visibility = Visibility.Collapsed;
                UpdateProgressText.Text = string.Empty;
                PrimaryButton.Content = "重试下载";
                PrimaryButton.IsEnabled = true;
                SummaryText.Text = "更新下载已取消，可以稍后重新开始。";
            }
            catch (Exception ex)
            {
                UpdateProgressPanel.Visibility = Visibility.Collapsed;
                UpdateProgressText.Text = string.Empty;
                PrimaryButton.Content = "重试下载";
                PrimaryButton.IsEnabled = true;
                ShowNotes("下载失败", ex.Message);
            }
            finally
            {
                if (ReferenceEquals(_downloadCancellation, cancellation))
                {
                    _downloadCancellation = null;
                }
            }
        }

        private void SetWindowAppearance(string settingsWindowMaterial, string applicationTheme)
        {
            _settingsWindowMaterial = settingsWindowMaterial;
            _applicationTheme = applicationTheme;
            ApplyWindowMaterial();
        }

        private void ApplyWindowMaterial()
        {
            ResolvedApplicationTheme theme = ApplicationThemeParser.Resolve(_applicationTheme);
            QuickTranslateWindowMaterial material = QuickTranslateWindowMaterialParser.Parse(_settingsWindowMaterial);
            RootSurface.Background = material switch
            {
                QuickTranslateWindowMaterial.Solid => SolidBackground(theme),
                QuickTranslateWindowMaterial.Acrylic => MediaBrushes.Transparent,
                QuickTranslateWindowMaterial.Mica => MediaBrushes.Transparent,
                _ => SolidBackground(theme)
            };

            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                int cornerPreference = (int)UnmanagedMethods.DwmWindowCornerPreference.DWMWCP_ROUND;
                UnmanagedMethods.DwmSetWindowAttribute(
                    handle,
                    UnmanagedMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
                    ref cornerPreference,
                    sizeof(int));

                int backdropType = material == QuickTranslateWindowMaterial.Mica
                    ? (int)UnmanagedMethods.DwmSystemBackdropType.DWMSBT_TRANSIENTWINDOW
                    : (int)UnmanagedMethods.DwmSystemBackdropType.DWMSBT_NONE;
                UnmanagedMethods.DwmSetWindowAttribute(
                    handle,
                    UnmanagedMethods.DWMWA_SYSTEMBACKDROP_TYPE,
                    ref backdropType,
                    sizeof(int));
            }

            if (material == QuickTranslateWindowMaterial.Acrylic)
            {
                ApplyAcrylicBackdrop(handle, theme);
            }
            else
            {
                ClearAcrylicBackdrop(handle);
            }
        }

        private static SolidColorBrush SolidBackground(ResolvedApplicationTheme theme) =>
            theme == ResolvedApplicationTheme.Dark
                ? new SolidColorBrush(MediaColor.FromRgb(24, 32, 42))
                : new SolidColorBrush(MediaColor.FromRgb(245, 247, 250));

        private static void ApplyAcrylicBackdrop(IntPtr handle, ResolvedApplicationTheme theme)
        {
            var accent = new UnmanagedMethods.AccentPolicy
            {
                AccentState = UnmanagedMethods.AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 0,
                GradientColor = ToAccentColor(theme == ResolvedApplicationTheme.Dark
                    ? MediaColor.FromArgb(AcrylicTintOpacity, 24, 32, 42)
                    : MediaColor.FromArgb(AcrylicTintOpacity, 245, 247, 250)),
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

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            if (_downloadCancellation != null)
            {
                _downloadCancellation.Cancel();
                return;
            }

            DialogResult = false;
            Close();
        }

        private async void Primary_Click(object sender, RoutedEventArgs e)
        {
            if (_package != null)
            {
                await DownloadAndInstallAsync();
                return;
            }

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
