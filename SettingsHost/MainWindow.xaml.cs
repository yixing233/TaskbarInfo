using System.Text.Json;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TaskbarInfo;
using Windows.Media.Control;

namespace LyricsX.Settings;

public sealed partial class MainWindow : Window
{
    private const string SharedSettingsAppliedEventName = "LyricsX.Settings.Apply";
    private static readonly string[] FontWeightOptions = ["常规", "细体", "半粗", "粗体"];
    private sealed record DisplayOption(string DeviceName, string Label);
    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr deviceContext, IntPtr monitorRect, IntPtr data);
    private const uint MonitorInfofPrimary = 0x00000001;
    private const int DefaultWidth = 920;
    private const int DefaultHeight = 640;
    private const int MinimumWidthDip = 700;
    private const int MinimumHeightDip = 540;
    private const int MaximumWidthDip = 1120;
    private const int MaximumHeightDip = 760;
    private const int GwlStyle = -16;
    private const long WsMaximizeBox = 0x00010000L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;
    private const uint WmNcLButtonDown = 0x00A1;
    private const uint HtCaption = 0x0002;
    private const uint WmGetMinMaxInfo = 0x0024;
    private static readonly UIntPtr WindowSizeSubclassId = new(1);

    private readonly SettingsDocument _settings;
    private readonly string _settingsPath;
    private readonly UpdateService _updateService = new();
    private readonly DispatcherQueueTimer _successInfoBarTimer;
    private readonly SubclassProc _windowSizeSubclassProc;
    private bool _didSave;
    private IntPtr _windowHandle;
    private bool _windowSizeSubclassInstalled;

    public MainWindow()
    {
        _windowSizeSubclassProc = WindowSizeSubclassProc;
        _settingsPath = ResolveSettingsPath();
        _settings = SettingsDocument.Load(_settingsPath);
        InitializeComponent();
        ApplyWindowIcon();
        _successInfoBarTimer = DispatcherQueue.CreateTimer();
        _successInfoBarTimer.Interval = TimeSpan.FromSeconds(3);
        _successInfoBarTimer.IsRepeating = false;
        _successInfoBarTimer.Tick += (_, _) => ErrorInfoBar.IsOpen = false;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ConfigureWindowChrome();
        InstallWindowSizeConstraints();
        ResizeToInitialSize();

        string initialPage = ResolveInitialPage();
        Navigate(initialPage);
        IEnumerable<NavigationViewItem> navigationItems = NavMenu.MenuItems
            .OfType<NavigationViewItem>()
            .SelectMany(item => item.MenuItems.OfType<NavigationViewItem>().Append(item));
        NavMenu.SelectedItem = navigationItems.FirstOrDefault(item => item.Tag as string == initialPage)
            ?? navigationItems.FirstOrDefault(item => item.Tag is string);
        Closed += MainWindow_Closed;
    }

    private void ApplyWindowIcon()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "LyricsX.ico");
        if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
    }

    private void ConfigureWindowChrome()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.SetBorderAndTitleBar(true, false);
        }

        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (_windowHandle == IntPtr.Zero) return;

        long style = GetWindowLongPtr(_windowHandle, GwlStyle).ToInt64();
        SetWindowLongPtr(_windowHandle, GwlStyle, new IntPtr(style & ~WsMaximizeBox));
        SetWindowPos(_windowHandle, IntPtr.Zero, 0, 0, 0, 0,
            SwpNoSize | SwpNoMove | SwpNoZOrder | SwpFrameChanged);
    }

    private void InstallWindowSizeConstraints()
    {
        if (_windowHandle == IntPtr.Zero) return;

        _windowSizeSubclassInstalled = SetWindowSubclass(
            _windowHandle,
            _windowSizeSubclassProc,
            WindowSizeSubclassId,
            UIntPtr.Zero);
    }

    private void ResizeToInitialSize()
    {
        Windows.Graphics.SizeInt32 minimum = GetMinimumSize();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            Math.Max(DefaultWidth, minimum.Width),
            Math.Max(DefaultHeight, minimum.Height)));
    }

    private Windows.Graphics.SizeInt32 GetMinimumSize()
    {
        WindowTrackSizeBounds bounds = GetWindowTrackSizeBounds();
        return new Windows.Graphics.SizeInt32(bounds.MinimumWidth, bounds.MinimumHeight);
    }

    private Windows.Graphics.SizeInt32 GetMaximumSize()
    {
        WindowTrackSizeBounds bounds = GetWindowTrackSizeBounds();
        return new Windows.Graphics.SizeInt32(bounds.MaximumWidth, bounds.MaximumHeight);
    }

    private WindowTrackSizeBounds GetWindowTrackSizeBounds()
    {
        uint dpi = _windowHandle == IntPtr.Zero ? 96 : GetDpiForWindow(_windowHandle);
        return WindowSizeConstraints.GetTrackSizeBounds(
            MinimumWidthDip,
            MinimumHeightDip,
            MaximumWidthDip,
            MaximumHeightDip,
            dpi);
    }

    private IntPtr WindowSizeSubclassProc(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr referenceData)
    {
        IntPtr result = DefSubclassProc(windowHandle, message, wParam, lParam);
        if (message != WmGetMinMaxInfo || lParam == IntPtr.Zero) return result;

        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        WindowTrackSizeBounds bounds = GetWindowTrackSizeBounds();
        minMaxInfo.MinimumTrackSize = new NativePoint(bounds.MinimumWidth, bounds.MinimumHeight);
        minMaxInfo.MaximumTrackSize = new NativePoint(bounds.MaximumWidth, bounds.MaximumHeight);
        Marshal.StructureToPtr(minMaxInfo, lParam, false);
        return result;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        IntPtr windowHandle,
        SubclassProc subclassProc,
        UIntPtr subclassId,
        UIntPtr referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        IntPtr windowHandle,
        SubclassProc subclassProc,
        UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    private delegate IntPtr SubclassProc(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr referenceData);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaximumSize;
        public NativePoint MaximumPosition;
        public NativePoint MinimumTrackSize;
        public NativePoint MaximumTrackSize;
    }

    private void NavMenu_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            Navigate(tag);
        }
    }

    private void TitleDragRegion_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(TitleDragRegion).Properties.IsLeftButtonPressed) return;

        IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (windowHandle == IntPtr.Zero) return;

        ReleaseCapture();
        SendMessage(windowHandle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Minimize();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Navigate(string tag)
    {
        ContentFrame.Content = tag switch
        {
            "Typography" => CreateTypographyPage(),
            "Visual" => CreateVisualPage(),
            "Floating" => CreateFloatingPage(),
            "Applications" => CreateApplicationsPage(),
            "DesktopWidget" => CreateDesktopWidgetPage(),
            "About" => CreateAboutPage(),
            _ => null
        };
    }

    private Page CreateTypographyPage()
    {
        var panel = NewPanel("字体与排版", "任务栏歌词的字体和显示方式。");
        panel.AddRow(LabeledFontPicker("字体", _settings.FontFamily, value => _settings.FontFamily = value));
        panel.AddRow(LabeledNumberBox("字体大小", _settings.FontSize, 8, 48, value => _settings.FontSize = value));
        panel.AddRow(LabeledComboBox("字体粗细", FontWeightOptions, ToChineseFontWeight(_settings.FontWeight), value => _settings.FontWeight = ToFontWeight(value)));
        panel.AddRow(LabeledNumberBox("下一句字号差", _settings.NextLyricFontSizeDiff, 0, 12, value => _settings.NextLyricFontSizeDiff = value));
        panel.AddRow(LabeledComboBox("下一句字体粗细", FontWeightOptions, ToChineseFontWeight(_settings.NextLyricFontWeight), value => _settings.NextLyricFontWeight = ToFontWeight(value)));
        panel.AddRow(LabeledToggle("显示双行歌词", _settings.IsDoubleLine, value => _settings.IsDoubleLine = value));
        panel.AddRow(LabeledNumberBox("任务栏歌词宽度", _settings.Width, 180, 1600, value => _settings.Width = value));
        DisplayOption[] displayOptions = GetDisplayOptions();
        DisplayOption selectedDisplay = displayOptions.FirstOrDefault(option =>
            string.Equals(option.DeviceName, _settings.TaskbarMonitorDeviceName, StringComparison.OrdinalIgnoreCase))
            ?? displayOptions[0];
        panel.AddRow(LabeledComboBox("显示屏", displayOptions.Select(option => option.Label).ToArray(), selectedDisplay.Label,
            label => _settings.TaskbarMonitorDeviceName = displayOptions.First(option => option.Label == label).DeviceName));
        panel.AddRow(LabeledNumberBox("任务栏右侧偏移", _settings.OffsetX, 0, 200, value => _settings.OffsetX = (int)value));
        panel.AddRow(LabeledNumberBox("歌词时间偏移（秒）", _settings.LyricOffsetSeconds, -10, 10, value => _settings.LyricOffsetSeconds = value));
        return Wrap(panel);
    }

    private static DisplayOption[] GetDisplayOptions()
    {
        try
        {
            var monitors = new List<(string DeviceName, int Width, int Height, bool IsPrimary)>();
            MonitorEnumProc collectMonitor = (monitor, _, _, _) =>
            {
                var monitorInfo = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
                if (GetMonitorInfo(monitor, ref monitorInfo))
                {
                    monitors.Add((
                        monitorInfo.DeviceName,
                        monitorInfo.Monitor.Right - monitorInfo.Monitor.Left,
                        monitorInfo.Monitor.Bottom - monitorInfo.Monitor.Top,
                        (monitorInfo.Flags & MonitorInfofPrimary) != 0));
                }

                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, collectMonitor, IntPtr.Zero);

            DisplayOption[] options = monitors
                .OrderByDescending(monitor => monitor.IsPrimary)
                .ThenBy(monitor => monitor.DeviceName, StringComparer.OrdinalIgnoreCase)
                .Select((monitor, index) => new DisplayOption(
                    monitor.DeviceName,
                    $"显示器 {index + 1}{(monitor.IsPrimary ? "（主显示器）" : "")} — {monitor.Width} × {monitor.Height}"))
                .ToArray();
            return options.Length > 0 ? options : [new DisplayOption("", "主显示器")];
        }
        catch
        {
            return [new DisplayOption("", "主显示器")];
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clipRect,
        MonitorEnumProc monitorEnumProc,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    private Page CreateVisualPage()
    {
        var panel = NewPanel("视觉效果", "颜色请使用 #RRGGBB 或 #AARRGGBB 格式。");
        panel.AddRow(LabeledColorPicker("歌词颜色", _settings.TextColor, value => _settings.TextColor = value));
        panel.AddRow(LabeledColorPicker("高亮颜色", _settings.ActiveTextColor, value => _settings.ActiveTextColor = value));
        panel.AddRow(LabeledColorPicker("任务栏背景", _settings.BackgroundColor, value => _settings.BackgroundColor = value));
        panel.AddRow(LabeledToggle("启用文字阴影", _settings.EnableShadow, value => _settings.EnableShadow = value));
        panel.AddRow(LabeledToggle("启用文字描边", _settings.EnableOutline, value => _settings.EnableOutline = value));
        return Wrap(panel);
    }

    private Page CreateFloatingPage()
    {
        var panel = NewPanel("悬浮歌词", "控制独立悬浮歌词窗口的外观和交互。");
        panel.AddRow(LabeledToggle("启用悬浮歌词", _settings.EnableFloatingLyrics, value => _settings.EnableFloatingLyrics = value));
        panel.AddRow(LabeledToggle("锁定位置", _settings.FloatingLyricsLocked, value => _settings.FloatingLyricsLocked = value));
        panel.AddRow(LabeledToggle("鼠标穿透", _settings.FloatingLyricsClickThrough, value => _settings.FloatingLyricsClickThrough = value));
        panel.AddRow(LabeledToggle("亚克力背景", _settings.FloatingLyricsUseAcrylic, value => _settings.FloatingLyricsUseAcrylic = value));
        panel.AddRow(LabeledToggle("启用文字阴影", _settings.FloatingLyricsEnableShadow, value => _settings.FloatingLyricsEnableShadow = value));
        panel.AddRow(LabeledFontPicker("字体", _settings.FloatingLyricsFontFamily, value => _settings.FloatingLyricsFontFamily = value));
        panel.AddRow(LabeledNumberBox("字体大小", _settings.FloatingLyricsFontSize, 10, 72, value => _settings.FloatingLyricsFontSize = value));
        panel.AddRow(LabeledComboBox("字体粗细", FontWeightOptions, ToChineseFontWeight(_settings.FloatingLyricsFontWeight), value => _settings.FloatingLyricsFontWeight = ToFontWeight(value)));
        panel.AddRow(LabeledNumberBox("窗口宽度", _settings.FloatingLyricsWidth ?? 360, 180, 1600, value => _settings.FloatingLyricsWidth = value));
        panel.AddRow(LabeledColorPicker("文字颜色", _settings.FloatingLyricsTextColor, value => _settings.FloatingLyricsTextColor = value));
        panel.AddRow(LabeledColorPicker("背景颜色", _settings.FloatingLyricsBackgroundColor, value => _settings.FloatingLyricsBackgroundColor = value));
        return Wrap(panel);
    }

    private Page CreateApplicationsPage()
    {
        var panel = NewPanel("应用筛选", "限制歌词的显示条件和媒体来源。");
        var selectedAppIds = new HashSet<string>(_settings.IncludedAppIds, StringComparer.OrdinalIgnoreCase);
        var appIdBox = new TextBox
        {
            Text = string.Join(",", selectedAppIds),
            MinWidth = 360
        };
        appIdBox.TextChanged += (_, _) =>
        {
            selectedAppIds.Clear();
            foreach (string appId in appIdBox.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                selectedAppIds.Add(appId);
            }
            _settings.IncludedAppIds = selectedAppIds.ToList();
        };

        var detectedApps = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
        var detectionStatus = new TextBlock
        {
            Opacity = 0.65,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        var detectButton = new Button { Content = "获取正在播放的应用", HorizontalAlignment = HorizontalAlignment.Left };
        detectButton.Click += async (_, _) =>
        {
            detectButton.IsEnabled = false;
            detectionStatus.Text = "正在读取系统媒体会话…";
            detectionStatus.Visibility = Visibility.Visible;
            detectedApps.Children.Clear();
            detectedApps.Visibility = Visibility.Collapsed;

            try
            {
                var sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                string[] appIds = sessionManager.GetSessions()
                    .Where(session => session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    .Select(session => session.SourceAppUserModelId)
                    .Where(appId => !string.IsNullOrWhiteSpace(appId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(appId => GetMediaApplicationDisplayName(appId), StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();

                if (appIds.Length == 0)
                {
                    detectionStatus.Text = "未检测到正在播放媒体的应用。请先在播放器中开始播放，再重新获取。";
                    return;
                }

                detectionStatus.Text = $"检测到 {appIds.Length} 个正在播放的应用；勾选后会加入应用标识筛选。";
                detectedApps.Visibility = Visibility.Visible;
                foreach (string appId in appIds)
                {
                    var checkBox = new CheckBox
                    {
                        IsChecked = selectedAppIds.Contains(appId),
                        Content = new StackPanel
                        {
                            Spacing = 1,
                            Children =
                            {
                                new TextBlock { Text = GetMediaApplicationDisplayName(appId) },
                                new TextBlock { Text = appId, Opacity = 0.65, FontSize = 12, TextWrapping = TextWrapping.Wrap }
                            }
                        }
                    };
                    checkBox.Checked += (_, _) => SetSelectedMediaApplication(selectedAppIds, appIdBox, appId, true);
                    checkBox.Unchecked += (_, _) => SetSelectedMediaApplication(selectedAppIds, appIdBox, appId, false);
                    detectedApps.Children.Add(checkBox);
                }
            }
            catch (Exception ex)
            {
                detectionStatus.Text = $"读取系统媒体会话失败：{ex.Message}";
            }
            finally
            {
                detectButton.IsEnabled = true;
            }
        };

        var detector = new StackPanel
        {
            Spacing = 8,
            Children = { detectButton, detectionStatus, detectedApps }
        };

        panel.AddRow(SectionHeader("显示条件"));
        panel.AddRow(LabeledToggle("仅在指定播放器运行时显示", _settings.RunOnlyWithMusicApp, value => _settings.RunOnlyWithMusicApp = value));
        panel.AddRow(LabeledTextBox("播放器进程名", _settings.MusicAppProcessNames, value => _settings.MusicAppProcessNames = value, "仅在开启上方选项时生效；使用英文逗号分隔。"));
        panel.AddRow(SectionHeader("媒体来源"));
        panel.AddRow(detector);
        panel.AddRow(Field("已选媒体应用", appIdBox, "可手动编辑应用标识；检测结果勾选后会自动写入。"));
        return Wrap(panel);
    }

    private Page CreateAboutPage()
    {
        var panel = NewPanel("关于", "查看版本信息、检查更新和项目来源。");
        var identity = new StackPanel { Spacing = 2 };
        identity.Children.Add(new TextBlock
        {
            Text = "LyricsX",
            FontSize = 28,
            FontWeight = FontWeights.SemiBold
        });
        identity.Children.Add(new TextBlock
        {
            Text = "Windows 桌面歌词与媒体组件",
            Opacity = 0.72
        });

        var version = new TextBlock
        {
            Text = $"当前版本  {UpdateService.CurrentVersionDisplay}",
            VerticalAlignment = VerticalAlignment.Center
        };
        var updateStatus = new TextBlock
        {
            Text = "尚未检查更新。",
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap
        };
        var releaseLink = new HyperlinkButton
        {
            Content = "查看新版本",
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(0)
        };
        var checkButton = new Button { Content = "检查更新" };
        checkButton.Click += async (_, _) =>
        {
            checkButton.IsEnabled = false;
            releaseLink.Visibility = Visibility.Collapsed;
            updateStatus.Text = "正在检查更新…";
            try
            {
                UpdateCheckResult result = await _updateService.CheckForUpdatesAsync();
                if (!result.Success)
                {
                    updateStatus.Text = $"检查更新失败：{result.ErrorMessage ?? "未知错误。"}";
                    return;
                }

                if (result.NoReleasePublished)
                {
                    updateStatus.Text = "暂未找到可用的正式版本。";
                }
                else if (result.HasUpdate)
                {
                    updateStatus.Text = $"发现新版本：{result.ReleaseName}（{result.LatestVersionDisplay}）。";
                    if (Uri.TryCreate(result.ReleasePageUrl, UriKind.Absolute, out Uri? releaseUri))
                    {
                        releaseLink.NavigateUri = releaseUri;
                        releaseLink.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    updateStatus.Text = $"当前已是最新版本（{UpdateService.CurrentVersionDisplay}）。";
                }
            }
            catch (TaskCanceledException)
            {
                updateStatus.Text = "检查更新超时，请稍后重试。";
            }
            catch (Exception exception)
            {
                updateStatus.Text = $"检查更新失败：{exception.Message}";
            }
            finally
            {
                checkButton.IsEnabled = true;
            }
        };

        var updatePanel = new StackPanel { Spacing = 8 };
        updatePanel.Children.Add(checkButton);
        updatePanel.Children.Add(updateStatus);
        updatePanel.Children.Add(releaseLink);

        var content = new Grid { ColumnSpacing = 28 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel { Spacing = 16 };
        left.Children.Add(identity);
        left.Children.Add(Field("版本", version, null));
        left.Children.Add(Field("更新", updatePanel, "通过 GitHub Releases 检查最新正式版本。"));

        var right = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(0, 8, 0, 0)
        };
        right.Children.Add(SectionHeader("相关来源"));
        right.Children.Add(CreateSourceLinks());
        right.Children.Add(LabeledToggle("启动时自动检查更新", _settings.AutoCheckUpdates, value => _settings.AutoCheckUpdates = value));

        Grid.SetColumn(right, 1);
        content.Children.Add(left);
        content.Children.Add(right);
        panel.AddRow(content);
        return Wrap(panel);
    }

    private static FrameworkElement CreateSourceLinks()
    {
        var links = new StackPanel { Spacing = 4 };
        links.Children.Add(CreateSourceLink("项目源码", UpdateService.RepositoryUrl));
        links.Children.Add(CreateSourceLink("发布页面", UpdateService.ReleasesUrl));
        links.Children.Add(CreateSourceLink("问题反馈", $"{UpdateService.RepositoryUrl}/issues"));
        return links;
    }

    private static HyperlinkButton CreateSourceLink(string label, string url)
    {
        return new HyperlinkButton
        {
            Content = label,
            NavigateUri = new Uri(url),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(0)
        };
    }

    private static TextBlock SectionHeader(string title)
    {
        return new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold
        };
    }

    private static Button InformationButton(string description)
    {
        var button = new Button
        {
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            Background = null,
            BorderBrush = null,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Content = new TextBlock
            {
                Text = "ⓘ",
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            Flyout = new Flyout
            {
                Content = new TextBlock
                {
                    Text = description,
                    MaxWidth = 360,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
        ToolTipService.SetToolTip(button, "说明");
        return button;
    }

    private static void SetSelectedMediaApplication(HashSet<string> selectedAppIds, TextBox appIdBox, string appId, bool isSelected)
    {
        if (isSelected) selectedAppIds.Add(appId);
        else selectedAppIds.Remove(appId);

        appIdBox.Text = string.Join(",", selectedAppIds.OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase));
    }

    private static string GetMediaApplicationDisplayName(string appId)
    {
        int packageSeparator = appId.IndexOf('!');
        if (packageSeparator > 0) return appId[..packageSeparator];

        return appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? appId[..^4]
            : appId;
    }

    private Page CreateDesktopWidgetPage()
    {
        var panel = NewPanel("桌面媒体组件", "组件外观沿用现有桌面组件实现，仅在此处配置。");
        panel.AddRow(LabeledToggle("启用桌面媒体组件", _settings.EnableDesktopWidget, value => _settings.EnableDesktopWidget = value));
        panel.AddRow(LabeledComboBox("主题", ["深色", "浅色"], _settings.DesktopWidgetTheme == 1 ? "浅色" : "深色", value => _settings.DesktopWidgetTheme = value == "浅色" ? 1 : 0));
        panel.AddRow(LabeledToggle("锁定组件位置", _settings.DesktopWidgetLocked, value => _settings.DesktopWidgetLocked = value));
        var reset = new Button { Content = "重置组件位置", HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) =>
        {
            _settings.DesktopWidgetLeft = 48;
            _settings.DesktopWidgetTop = 48;
            _settings.DesktopWidgetMonitorDeviceName = "";
            _settings.DesktopWidgetMonitorOffsetX = null;
            _settings.DesktopWidgetMonitorOffsetY = null;
        };
        panel.AddRow(reset);
        return Wrap(panel);
    }

    private static FormPanel NewPanel(string title, string subtitle)
    {
        var panel = new FormPanel();
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        header.Children.Add(new TextBlock
        {
            Text = title,
            Style = Application.Current.Resources["TitleTextBlockStyle"] as Style
        });
        if (!string.IsNullOrWhiteSpace(subtitle)) header.Children.Add(InformationButton(subtitle));
        panel.AddRow(header);
        return panel;
    }

    private static Page Wrap(FormPanel panel)
    {
        var viewer = new ScrollViewer
        {
            Padding = new Thickness(28, 16, 28, 12),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = panel
        };
        return new Page { Content = viewer };
    }

    private sealed class FormPanel : Grid
    {
        public FormPanel()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        public void AddRow(FrameworkElement child)
        {
            int rowIndex = RowDefinitions.Count;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            child.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (rowIndex > 0) child.Margin = new Thickness(0, 8, 0, 0);

            Grid.SetRow(child, rowIndex);
            Children.Add(child);
        }
    }

    private static FrameworkElement LabeledTextBox(string label, string initial, Action<string> update, string? description = null)
    {
        var box = new TextBox { Text = initial, MinWidth = 360 };
        box.TextChanged += (_, _) => update(box.Text);
        return Field(label, box, description);
    }

    private static FrameworkElement LabeledColorPicker(string label, string initial, Action<string> update)
    {
        const double SpectrumWidth = 280;
        const double SpectrumHeight = 176;
        const double SelectorDiameter = 16;

        var box = new TextBox { Text = initial, MinWidth = 360 };
        var checkerboard = new Grid();
        checkerboard.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        checkerboard.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        checkerboard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        checkerboard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int row = 0; row < 2; row++)
        {
            for (int column = 0; column < 2; column++)
            {
                var square = new Border
                {
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(
                            255,
                            (row + column) % 2 == 0 ? (byte)255 : (byte)208,
                            (row + column) % 2 == 0 ? (byte)255 : (byte)208,
                            (row + column) % 2 == 0 ? (byte)255 : (byte)208))
                };
                Grid.SetRow(square, row);
                Grid.SetColumn(square, column);
                checkerboard.Children.Add(square);
            }
        }

        var swatch = new Border
        {
            BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 128, 128, 128)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = checkerboard
        };
        var colorLayer = new Border { CornerRadius = new CornerRadius(3) };
        Grid.SetRowSpan(colorLayer, 2);
        Grid.SetColumnSpan(colorLayer, 2);
        checkerboard.Children.Add(colorLayer);

        Windows.UI.Color initialColor = TryParseColor(initial, out Windows.UI.Color parsedInitialColor)
            ? parsedInitialColor
            : Windows.UI.Color.FromArgb(255, 255, 255, 255);
        RgbToHsv(initialColor, out double hue, out double saturation, out double value);
        double alpha = initialColor.A;

        var spectrum = new Canvas
        {
            Width = SpectrumWidth,
            Height = SpectrumHeight,
            Background = CreateHueGradientBrush()
        };
        spectrum.Children.Add(new Border
        {
            Width = SpectrumWidth,
            Height = SpectrumHeight,
            Background = CreateSaturationGradientBrush(),
            IsHitTestVisible = false
        });
        var selectionIndicator = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = SelectorDiameter,
            Height = SelectorDiameter,
            StrokeThickness = 2,
            IsHitTestVisible = false
        };
        spectrum.Children.Add(selectionIndicator);

        var valueSlider = new Slider { Minimum = 0, Maximum = 100, Width = SpectrumWidth, Value = value * 100 };
        var alphaSlider = new Slider { Minimum = 0, Maximum = 255, Width = SpectrumWidth, Value = alpha };
        var pickerPanel = new StackPanel { Width = SpectrumWidth, Spacing = 2 };
        pickerPanel.Children.Add(new Border
        {
            BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = spectrum
        });
        pickerPanel.Children.Add(new TextBlock { Text = "亮度", FontSize = 12, Opacity = 0.7 });
        pickerPanel.Children.Add(valueSlider);
        pickerPanel.Children.Add(new TextBlock { Text = "不透明度", FontSize = 12, Opacity = 0.7 });
        pickerPanel.Children.Add(alphaSlider);
        var pickerButton = new Button
        {
            Width = 48,
            Height = 36,
            Padding = new Thickness(2),
            Background = null,
            BorderBrush = null,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Content = swatch,
            Flyout = new Flyout { Content = pickerPanel }
        };
        var editor = new Grid { MinWidth = 416, ColumnSpacing = 8 };
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(box, 0);
        Grid.SetColumn(pickerButton, 1);
        editor.Children.Add(box);
        editor.Children.Add(pickerButton);

        void UpdateSwatch(Windows.UI.Color color) =>
            colorLayer.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);

        void PositionSelectionIndicator()
        {
            double left = hue / 360d * (SpectrumWidth - SelectorDiameter);
            double top = (1 - saturation) * (SpectrumHeight - SelectorDiameter);
            Canvas.SetLeft(selectionIndicator, left);
            Canvas.SetTop(selectionIndicator, top);
            selectionIndicator.Stroke = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                value > 0.65 && saturation < 0.3
                    ? Windows.UI.Color.FromArgb(255, 20, 20, 20)
                    : Windows.UI.Color.FromArgb(255, 255, 255, 255));
        }

        bool synchronizing = false;
        void ApplyColor(Windows.UI.Color color, bool updateText)
        {
            RgbToHsv(color, out hue, out saturation, out value);
            alpha = color.A;
            synchronizing = true;
            valueSlider.Value = value * 100;
            alphaSlider.Value = alpha;
            synchronizing = false;
            UpdateSwatch(color);
            PositionSelectionIndicator();

            if (!updateText) return;

            string colorText = FormatColor(color);
            if (!string.Equals(box.Text, colorText, StringComparison.Ordinal)) box.Text = colorText;
            else update(colorText);
        }

        void CommitPickerColor() => ApplyColor(HsvToRgb(hue, saturation, value, (byte)Math.Round(alpha)), true);

        void UpdateSpectrumSelection(Windows.Foundation.Point position)
        {
            double left = Math.Clamp(position.X - SelectorDiameter / 2, 0, SpectrumWidth - SelectorDiameter);
            double top = Math.Clamp(position.Y - SelectorDiameter / 2, 0, SpectrumHeight - SelectorDiameter);
            hue = left / (SpectrumWidth - SelectorDiameter) * 360;
            saturation = 1 - top / (SpectrumHeight - SelectorDiameter);
            CommitPickerColor();
        }

        bool selectingSpectrum = false;
        spectrum.PointerPressed += (_, args) =>
        {
            selectingSpectrum = true;
            spectrum.CapturePointer(args.Pointer);
            UpdateSpectrumSelection(args.GetCurrentPoint(spectrum).Position);
        };
        spectrum.PointerMoved += (_, args) =>
        {
            if (selectingSpectrum) UpdateSpectrumSelection(args.GetCurrentPoint(spectrum).Position);
        };
        spectrum.PointerReleased += (_, args) =>
        {
            selectingSpectrum = false;
            spectrum.ReleasePointerCapture(args.Pointer);
        };
        spectrum.PointerCanceled += (_, args) =>
        {
            selectingSpectrum = false;
            spectrum.ReleasePointerCapture(args.Pointer);
        };

        box.TextChanged += (_, _) =>
        {
            update(box.Text);
            if (!TryParseColor(box.Text, out Windows.UI.Color color)) return;

            ApplyColor(color, false);
        };
        valueSlider.ValueChanged += (_, _) =>
        {
            if (synchronizing) return;
            value = valueSlider.Value / 100;
            CommitPickerColor();
        };
        alphaSlider.ValueChanged += (_, _) =>
        {
            if (synchronizing) return;
            alpha = alphaSlider.Value;
            CommitPickerColor();
        };

        ApplyColor(initialColor, false);

        return Field(label, editor, null);
    }

    private static FrameworkElement LabeledFontPicker(string label, string initial, Action<string> update)
    {
        var box = new AutoSuggestBox
        {
            Text = initial,
            PlaceholderText = "输入字体名称搜索",
            MinWidth = 360,
            MaxSuggestionListHeight = 320
        };

        void UpdateSuggestions(string query)
        {
            string[] suggestions = GetFontSuggestions(query);
            box.ItemsSource = suggestions;
            box.IsSuggestionListOpen = suggestions.Length > 0 && !string.IsNullOrWhiteSpace(query);
        }

        box.TextChanged += (_, args) =>
        {
            update(box.Text);
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                UpdateSuggestions(box.Text);
            }
        };
        box.SuggestionChosen += (_, args) =>
        {
            string fontName = args.SelectedItem as string ?? box.Text;
            box.Text = fontName;
            update(fontName);
        };
        box.QuerySubmitted += (_, args) =>
        {
            string fontName = args.ChosenSuggestion as string ?? box.Text;
            box.Text = fontName;
            update(fontName);
        };

        return Field(label, box, "输入以搜索本机已安装字体，也可直接输入字体名称。");
    }

    private static string[] GetFontSuggestions(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        try
        {
            using var fonts = new InstalledFontCollection();
            return fonts.Families
                .Select(font => font.Name)
                .Where(name => name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .Take(40)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static string ToChineseFontWeight(string fontWeight) => fontWeight switch
    {
        "Light" => "细体",
        "SemiBold" => "半粗",
        "Bold" => "粗体",
        _ => "常规"
    };

    private static string ToFontWeight(string fontWeight) => fontWeight switch
    {
        "细体" => "Light",
        "半粗" => "SemiBold",
        "粗体" => "Bold",
        _ => "Normal"
    };

    private static FrameworkElement LabeledNumberBox(string label, double initial, double minimum, double maximum, Action<double> update)
    {
        var box = new NumberBox { Value = initial, Minimum = minimum, Maximum = maximum, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden, MinWidth = 180 };
        box.ValueChanged += (_, _) => { if (!double.IsNaN(box.Value)) update(box.Value); };
        return Field(label, box, null);
    }

    private static FrameworkElement LabeledComboBox(string label, IReadOnlyList<string> options, string initial, Action<string> update)
    {
        var box = new ComboBox { ItemsSource = options, SelectedItem = options.Contains(initial) ? initial : options[0], MinWidth = 180 };
        box.SelectionChanged += (_, _) => update(box.SelectedItem as string ?? options[0]);
        return Field(label, box, null);
    }

    private static FrameworkElement LabeledToggle(string label, bool initial, Action<bool> update)
    {
        var row = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var title = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        var toggle = new ToggleSwitch
        {
            IsOn = initial,
            MinWidth = 0,
            OnContent = null,
            OffContent = null,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        toggle.Toggled += (_, _) => update(toggle.IsOn);
        row.Children.Add(title);
        row.Children.Add(toggle);
        var host = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = row
        };
        return host;
    }

    private static FrameworkElement Field(string label, FrameworkElement control, string? description)
    {
        var panel = new StackPanel { Spacing = 4 };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        header.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        if (!string.IsNullOrWhiteSpace(description)) header.Children.Add(InformationButton(description));
        panel.Children.Add(header);
        panel.Children.Add(control);
        return panel;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        ApplySettings();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!ApplySettings()) return;

        _didSave = true;
        Close();
    }

    private bool ApplySettings()
    {
        if (!IsValidColor(_settings.TextColor) || !IsValidColor(_settings.ActiveTextColor) || !IsValidColor(_settings.BackgroundColor) || !IsValidColor(_settings.FloatingLyricsTextColor) || !IsValidColor(_settings.FloatingLyricsBackgroundColor))
        {
            _successInfoBarTimer.Stop();
            ErrorInfoBar.Severity = InfoBarSeverity.Error;
            ErrorInfoBar.Message = "颜色格式无效，请使用 #RRGGBB 或 #AARRGGBB。";
            ErrorInfoBar.IsOpen = true;
            return false;
        }

        try
        {
            _settings.Save(_settingsPath);
            NotifySettingsApplied();
            ErrorInfoBar.Severity = InfoBarSeverity.Success;
            ErrorInfoBar.Message = "设置已应用。";
            ErrorInfoBar.IsOpen = true;
            _successInfoBarTimer.Stop();
            _successInfoBarTimer.Start();
            return true;
        }
        catch (Exception exception)
        {
            _successInfoBarTimer.Stop();
            ErrorInfoBar.Severity = InfoBarSeverity.Error;
            ErrorInfoBar.Message = "设置保存失败: " + exception.Message;
            ErrorInfoBar.IsOpen = true;
            return false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_windowSizeSubclassInstalled)
        {
            RemoveWindowSubclass(_windowHandle, _windowSizeSubclassProc, WindowSizeSubclassId);
            _windowSizeSubclassInstalled = false;
        }

        Environment.ExitCode = _didSave ? 0 : 1;
    }

    private static bool IsValidColor(string value) => TryParseColor(value, out _);

    private static bool TryParseColor(string value, out Windows.UI.Color color)
    {
        string hex = value.Trim().TrimStart('#');
        if (hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
        {
            color = Windows.UI.Color.FromArgb(
                255,
                (byte)(rgb >> 16),
                (byte)(rgb >> 8),
                (byte)rgb);
            return true;
        }

        if (hex.Length == 8 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint argb))
        {
            color = Windows.UI.Color.FromArgb(
                (byte)(argb >> 24),
                (byte)(argb >> 16),
                (byte)(argb >> 8),
                (byte)argb);
            return true;
        }

        color = default;
        return false;
    }

    private static string FormatColor(Windows.UI.Color color) => color.A == byte.MaxValue
        ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
        : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static Microsoft.UI.Xaml.Media.LinearGradientBrush CreateHueGradientBrush()
    {
        var brush = new Microsoft.UI.Xaml.Media.LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 0)
        };
        brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Offset = 0, Color = Windows.UI.Color.FromArgb(255, 255, 0, 0) });
        brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Offset = 1d / 6, Color = Windows.UI.Color.FromArgb(255, 255, 255, 0) });
        brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Offset = 2d / 6, Color = Windows.UI.Color.FromArgb(255, 0, 255, 0) });
        brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Offset = 3d / 6, Color = Windows.UI.Color.FromArgb(255, 0, 255, 255) });
        brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Offset = 4d / 6, Color = Windows.UI.Color.FromArgb(255, 0, 0, 255) });
        brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Offset = 5d / 6, Color = Windows.UI.Color.FromArgb(255, 255, 0, 255) });
        brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Offset = 1, Color = Windows.UI.Color.FromArgb(255, 255, 0, 0) });
        return brush;
    }

    private static Microsoft.UI.Xaml.Media.LinearGradientBrush CreateSaturationGradientBrush()
    {
        var brush = new Microsoft.UI.Xaml.Media.LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 1)
        };
        brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Offset = 0, Color = Windows.UI.Color.FromArgb(0, 255, 255, 255) });
        brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Offset = 1, Color = Windows.UI.Color.FromArgb(255, 255, 255, 255) });
        return brush;
    }

    private static void RgbToHsv(Windows.UI.Color color, out double hue, out double saturation, out double value)
    {
        double red = color.R / 255d;
        double green = color.G / 255d;
        double blue = color.B / 255d;
        double maximum = Math.Max(red, Math.Max(green, blue));
        double minimum = Math.Min(red, Math.Min(green, blue));
        double delta = maximum - minimum;

        value = maximum;
        saturation = maximum == 0 ? 0 : delta / maximum;
        if (delta == 0)
        {
            hue = 0;
            return;
        }

        hue = maximum == red
            ? 60 * ((green - blue) / delta % 6)
            : maximum == green
                ? 60 * ((blue - red) / delta + 2)
                : 60 * ((red - green) / delta + 4);
        if (hue < 0) hue += 360;
    }

    private static Windows.UI.Color HsvToRgb(double hue, double saturation, double value, byte alpha)
    {
        double chroma = value * saturation;
        double hueSegment = hue / 60;
        double secondary = chroma * (1 - Math.Abs(hueSegment % 2 - 1));
        double match = value - chroma;
        (double red, double green, double blue) = hueSegment switch
        {
            < 1 => (chroma, secondary, 0d),
            < 2 => (secondary, chroma, 0d),
            < 3 => (0d, chroma, secondary),
            < 4 => (0d, secondary, chroma),
            < 5 => (secondary, 0d, chroma),
            _ => (chroma, 0d, secondary)
        };

        return Windows.UI.Color.FromArgb(
            alpha,
            (byte)Math.Round((red + match) * 255),
            (byte)Math.Round((green + match) * 255),
            (byte)Math.Round((blue + match) * 255));
    }

    private static string ResolveSettingsPath()
    {
        string? argument = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(path => !path.StartsWith("-", StringComparison.Ordinal));
        return string.IsNullOrWhiteSpace(argument)
            ? Path.Combine(AppContext.BaseDirectory, "settings.json")
            : Path.GetFullPath(argument);
    }

    private static string ResolveInitialPage()
    {
        string? argument = Environment.GetCommandLineArgs()
            .Skip(1)
            .FirstOrDefault(value => value.StartsWith("--page=", StringComparison.OrdinalIgnoreCase));
        return argument?[7..] switch
        {
            "1" => "Visual",
            "2" => "Floating",
            "3" => "Applications",
            "4" => "DesktopWidget",
            "5" => "About",
            _ => "Typography"
        };
    }

    private static void NotifySettingsApplied()
    {
        string? eventName = Environment.GetCommandLineArgs()
            .Skip(1)
            .FirstOrDefault(value => value.StartsWith("--apply-event=", StringComparison.OrdinalIgnoreCase))?[14..];
        SignalSettingsAppliedEvent(eventName);
        if (!string.Equals(eventName, SharedSettingsAppliedEventName, StringComparison.Ordinal))
        {
            SignalSettingsAppliedEvent(SharedSettingsAppliedEventName);
        }
    }

    private static void SignalSettingsAppliedEvent(string? eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName)) return;

        try
        {
            using var settingsApplied = EventWaitHandle.OpenExisting(eventName);
            settingsApplied.Set();
        }
        catch
        {
            // The main window may already have exited.
        }
    }
}

public sealed class SettingsDocument
{
    public double Width { get; set; } = 400;
    public double FontSize { get; set; } = 12;
    public string FontFamily { get; set; } = "Microsoft YaHei";
    public string TextColor { get; set; } = "#FFFFFF";
    public string ActiveTextColor { get; set; } = "#FF33BBFF";
    public string BackgroundColor { get; set; } = "#33000000";
    public bool EnableShadow { get; set; }
    public string FontWeight { get; set; } = "SemiBold";
    public bool EnableOutline { get; set; }
    public int OffsetX { get; set; } = 10;
    public string TaskbarMonitorDeviceName { get; set; } = "";
    public bool IsDoubleLine { get; set; } = true;
    public double LyricOffsetSeconds { get; set; }
    public List<string> IncludedAppIds { get; set; } = [];
    public bool EnableFloatingLyrics { get; set; }
    public bool FloatingLyricsLocked { get; set; }
    public bool FloatingLyricsClickThrough { get; set; }
    public string FloatingLyricsFontFamily { get; set; } = "Microsoft YaHei";
    public double FloatingLyricsFontSize { get; set; } = 20;
    public string FloatingLyricsFontWeight { get; set; } = "Bold";
    public string FloatingLyricsTextColor { get; set; } = "#FF1F2937";
    public string FloatingLyricsBackgroundColor { get; set; } = "#FFFFFFFF";
    public bool FloatingLyricsUseAcrylic { get; set; }
    public bool FloatingLyricsEnableShadow { get; set; } = true;
    public double? FloatingLyricsLeft { get; set; }
    public double? FloatingLyricsTop { get; set; }
    public double? FloatingLyricsWidth { get; set; }
    public bool EnableDesktopWidget { get; set; }
    public int DesktopWidgetTheme { get; set; }
    public double DesktopWidgetLeft { get; set; } = 48;
    public double DesktopWidgetTop { get; set; } = 48;
    public string DesktopWidgetMonitorDeviceName { get; set; } = "";
    public double? DesktopWidgetMonitorOffsetX { get; set; }
    public double? DesktopWidgetMonitorOffsetY { get; set; }
    public bool DesktopWidgetLocked { get; set; }
    public double NextLyricFontSizeDiff { get; set; } = 2;
    public string NextLyricFontWeight { get; set; } = "Normal";
    public bool RunOnlyWithMusicApp { get; set; }
    public string MusicAppProcessNames { get; set; } = "QQMusic,cloudmusic,Spotify,YesPlayMusic,Foobar2000";
    public bool AutoCheckUpdates { get; set; } = true;

    public static SettingsDocument Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new SettingsDocument();

            SettingsDocument settings = JsonSerializer.Deserialize<SettingsDocument>(File.ReadAllText(path)) ?? new SettingsDocument();
            settings.IncludedAppIds ??= [];
            return settings;
        }
        catch
        {
            return new SettingsDocument();
        }
    }

    public void Save(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
