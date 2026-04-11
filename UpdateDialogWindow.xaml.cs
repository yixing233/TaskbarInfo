using System;
using System.Windows;
namespace TaskbarInfo
{
    public partial class UpdateDialogWindow : Window
    {
        private string? _primaryUrl;

        private UpdateDialogWindow()
        {
            InitializeComponent();
            Icon = App.GetAppIcon();
        }

        public static bool? ShowForResult(Window owner, UpdateCheckResult result)
        {
            var window = new UpdateDialogWindow
            {
                Owner = owner
            };

            window.ApplyResult(result);
            return window.ShowDialog();
        }

        public static bool? ShowForError(Window owner, string message)
        {
            var window = new UpdateDialogWindow
            {
                Owner = owner
            };

            window.ApplyError(message);
            return window.ShowDialog();
        }

        private void ApplyResult(UpdateCheckResult result)
        {
            CurrentVersionText.Text = result.CurrentVersionDisplay;
            LatestVersionText.Text = result.NoReleasePublished ? "-" : result.LatestVersionDisplay;
            PublishedAtText.Text = result.PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "-";

            if (result.HasUpdate)
            {
                TitleText.Text = "发现新版本";
                SummaryText.Text = $"当前版本 {result.CurrentVersionDisplay}，发现了新的版本 {result.ReleaseTag}。";
                NotesTitleText.Text = "更新说明";
                NotesText.Text = string.IsNullOrWhiteSpace(result.ReleaseNotes) ? "这个版本暂时没有填写更新说明。" : result.ReleaseNotes.Trim();
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
                NotesTitleText.Text = "说明";
                NotesText.Text = "可以先在 GitHub Releases 中创建第一个发行版，之后应用就能正常检测更新了。";
                PrimaryButton.Visibility = Visibility.Collapsed;
                SetAccentVisual("#F4F5F7", "#4B5563", "i");
                return;
            }

            TitleText.Text = "已是最新版本";
            SummaryText.Text = $"当前运行的就是最新版本 {result.CurrentVersionDisplay}。";
            NotesTitleText.Text = "说明";
            NotesText.Text = "暂时没有检测到比当前版本更高的正式版。";
            PrimaryButton.Visibility = Visibility.Collapsed;
            SetAccentVisual("#EEF7EE", "#2E7D32", "✓");
        }

        private void ApplyError(string message)
        {
            TitleText.Text = "检查更新失败";
            SummaryText.Text = "这次没有成功拿到更新信息。";
            CurrentVersionText.Text = UpdateService.CurrentVersionDisplay;
            LatestVersionText.Text = "-";
            PublishedAtText.Text = "-";
            NotesTitleText.Text = "错误信息";
            NotesText.Text = string.IsNullOrWhiteSpace(message) ? "发生了未知错误。" : message.Trim();
            PrimaryButton.Visibility = Visibility.Collapsed;
            SetAccentVisual("#FFF4E5", "#C77700", "!");
        }

        private void SetAccentVisual(string background, string foreground, string glyph)
        {
            if (StatusGlyphText.Parent is System.Windows.Controls.Border badge)
            {
                badge.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(background)!;
            }

            StatusGlyphText.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(foreground)!;
            StatusGlyphText.Text = glyph;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
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
