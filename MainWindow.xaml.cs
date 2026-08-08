using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.Windows.Media;

using System.Windows.Media.Imaging;
using System.Windows.Input;

// Aliases to avoid ambiguity with System.Windows.Forms/System.Drawing
using Application = System.Windows.Application;
using Point = System.Windows.Point;
using FontFamily = System.Windows.Media.FontFamily;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace TaskbarInfo
{
    public partial class MainWindow : Window
    {
        private const string SharedSettingsAppliedEventName = "TaskbarInfo.Settings.Apply";
        private const int QuickTranslateHotkeyId = 0x4C58;
        private const int QuickTranslateSettingsPage = 6;
        private const int TaskbarPerformanceSettingsPage = 7;
        private const int WaterReminderSettingsPage = 8;
        private static readonly uint SettingsNavigateMessage =
            UnmanagedMethods.RegisterWindowMessage("TaskbarInfo.Settings.Navigate");

        public MainWindow()
        {
            InitializeComponent();
            Closed += (_, _) =>
            {
                StopSharedSettingsApplyNotification();
                StopSettingsUpdateRequestNotification();
                _taskbarPerformanceWindow?.Dispose();
                _taskbarTranslateButtonWindow?.Dispose();
                _taskbarWaterReminderWindow?.Dispose();
                _waterReminderPopup?.Dispose();
                _waterReminderTimer?.Stop();
                CloseQuickTranslatePopup();
                CloseSettingsHost();
                UnregisterQuickTranslateHotkey();
            };
            
            // Initialize references first
            _mainLyricControl = InfoText;
            _nextLyricControl = NextLyricText;
            
            // Then initialize TextBlock positions (ApplySettings no longer sets these)
            Canvas.SetTop(_mainLyricControl, 2);
            Canvas.SetLeft(_mainLyricControl, 0);
            Canvas.SetTop(_nextLyricControl, 0); // Will be set by logic based on mode
            Canvas.SetLeft(_nextLyricControl, 0);
        }

        private bool _isDraggingHandle = false;
        private Point _dragStartMouseScreenPos;
        private int _dragStartOffsetX;

        private AppSettings _settings = new AppSettings();
        
        private MediaManager _mediaManager = new MediaManager();
        private LyricsEngine _lyricsEngine = new LyricsEngine();
        private string _currentArtist = "";
        private string _currentTitle = "";
        private string _currentTrackKey = "";
        private MediaTrackInfo _currentTrackInfo = new();
        private bool _hasLyrics = false;
        private string _lastLyricText = "";
        private string _lastCurrentLyric = ""; // Track current lyric separately for animation
        private bool _isShowingStatusText = true;
        private CancellationTokenSource? _lyricsSearchCts;
        private int _lyricsSearchVersion;
        
        // Sync & Interpolation logic
        private DispatcherTimer? _lyricSyncTimer;
        private TimeSpan _lastMediaPosition = TimeSpan.Zero;
        private DateTime _lastSyncTime = DateTime.Now;
        private bool _isMediaPlaying = false;
        
        // Swappable controls references
        private TextBlock _mainLyricControl;
        private TextBlock _nextLyricControl;

        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private FloatingLyricsWindow? _floatingWindow;
        private DesktopWidgetWindow? _desktopWidget;
        private DispatcherTimer? _desktopHostTimer;
        private readonly UpdateService _updateService = new UpdateService();
        private UpdateCheckResult? _pendingUpdateResult;
        private bool _isCheckingUpdates;
        
        private DispatcherTimer? _processMonitorTimer;
        private TaskbarPerformanceWindow? _taskbarPerformanceWindow;
        private TaskbarTranslateButtonWindow? _taskbarTranslateButtonWindow;
        private TaskbarWaterReminderWindow? _taskbarWaterReminderWindow;
        private WaterReminderPopupWindow? _waterReminderPopup;
        private DispatcherTimer? _waterReminderTimer;
        private bool _waterReminderWasDue;
        private QuickTranslatePopupWindow? _quickTranslatePopup;
        private System.Diagnostics.Process? _settingsProcess;
        private HwndSource? _mainWindowSource;
        private bool _quickTranslateHotkeyRegistered;
        private EventWaitHandle? _sharedSettingsAppliedEvent;
        private RegisteredWaitHandle? _sharedSettingsAppliedWait;
        private EventWaitHandle? _settingsUpdateRequestedEvent;
        private RegisteredWaitHandle? _settingsUpdateRequestedWait;
        private string? _settingsUpdateRequestEventName;

        private void SetupProcessMonitor()
        {
            if (_processMonitorTimer == null)
            {
                _processMonitorTimer = new DispatcherTimer();
                _processMonitorTimer.Interval = TimeSpan.FromSeconds(3);
                _processMonitorTimer.Tick += ProcessMonitor_Tick;
            }

            if (_settings.RunOnlyWithMusicApp)
            {
                _processMonitorTimer.Start();
                ProcessMonitor_Tick(null, null); // Check immediately
            }
            else
            {
                _processMonitorTimer.Stop();
                if (this.Visibility != Visibility.Visible)
                {
                     this.Show(); 
                }
            }
        }

        private void ProcessMonitor_Tick(object? sender, EventArgs? e)
        {
            if (!_settings.RunOnlyWithMusicApp) return;

            string[] targets = _settings.MusicAppProcessNames.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            bool found = false;

            var processes = System.Diagnostics.Process.GetProcesses();
            foreach (var p in processes)
            {
                foreach (var t in targets)
                {
                    if (p.ProcessName.Equals(t.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
                if (found) break;
            }

            if (found)
            {
                SetTrayText("TaskbarInfo");

                if (this.Visibility != Visibility.Visible)
                {
                    this.Show();
                }
                if (_settings.EnableFloatingLyrics && _floatingWindow != null && _floatingWindow.Visibility != Visibility.Visible)
                {
                    _floatingWindow.Show();
                }
            }
            else
            {
                SetTrayText("TaskbarInfo - 已隐藏，等待播放器运行");
                // Hide everything
                if (this.Visibility == Visibility.Visible)
                {
                    this.Hide();
                }

                if (_floatingWindow != null && _floatingWindow.Visibility == Visibility.Visible)
                {
                    _floatingWindow.Hide();
                }
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Init Tray Icon
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            try
            {
                // Tray Icon
                try
                {
                    var trayUri = new Uri("pack://application:,,,/src/icons/托盘图标.png");
                    var info = Application.GetResourceStream(trayUri);
                    if (info != null)
                    {
                        using (var stream = info.Stream)
                        using (var bitmap = new System.Drawing.Bitmap(stream))
                        {
                            _notifyIcon.Icon = System.Drawing.Icon.FromHandle(bitmap.GetHicon());
                        }
                    }
                    else
                    {
                        _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
                    }
                }
                catch
                {
                    _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
                }

                // Window Icon
                this.Icon = App.GetAppIcon();
            }
            catch
            {
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }
            _notifyIcon.Visible = true;
            _notifyIcon.Visible = true;
            _notifyIcon.Text = "TaskbarInfo";
            _notifyIcon.BalloonTipClicked += NotifyIcon_BalloonTipClicked;
            
            // Handle Mouse Up to show WPF ContextMenu
            _notifyIcon.MouseUp += (s, args) => 
            {
                if (args.Button == System.Windows.Forms.MouseButtons.Right)
                {
                    // Activate window to ensure menu closes when clicking outside
                    var helper = new WindowInteropHelper(this);
                    UnmanagedMethods.SetForegroundWindow(helper.Handle);
                    
                    if (FindResource("TrayContextMenu") is ContextMenu trayMenu)
                    {
                        trayMenu.PlacementTarget = MainBorder;
                        trayMenu.IsOpen = true;
                    }
                }
            };
            
            // Old WinForms Menu removed
            // var trayMenu = new System.Windows.Forms.ContextMenuStrip(); 
            // ...

            // Load Settings
            _settings = AppSettings.Load();
            StartSharedSettingsApplyNotification();
            StartSettingsUpdateRequestNotification();
            
            // Apply visual settings
            ApplySettings();
            
            // Sync Menu State (will trigger Checked/Unchecked events which handle window creation)
            if (FloatingLyricsMenuItem != null)
                FloatingLyricsMenuItem.IsChecked = _settings.EnableFloatingLyrics;

            if (FloatingLyricsCtxItem != null)
            {
                FloatingLyricsCtxItem.IsChecked = _settings.FloatingLyricsClickThrough;
                // Visibility controlled by ManageFloatingWindow
            }
            
            // Re-calc vertical center on resize
            TextContainer.SizeChanged += (s, e) => UpdateVerticalCentering();
            
            // Ensure floating window is correct state (in case menu item was null or event didn't fire)
            ManageFloatingWindow();
            ManageDesktopWidget();
            
            InjectIntoTaskbar();
            ManageTaskbarPerformance();
            ManageTaskbarTranslateButton();
            
            // Initialize Media Manager
            _mediaManager.FilterAppIds = _settings.IncludedAppIds; // Initialize Filter
            _mediaManager.MediaInfoChanged += MediaManager_MediaInfoChanged;
            _mediaManager.PlaybackPositionChanged += MediaManager_PlaybackPositionChanged;
            _mediaManager.PlaybackStatusChanged += MediaManager_PlaybackStatusChanged;
            _mediaManager.AppIdChanged += MediaManager_AppIdChanged;
            
            // Setup High-Frequency Smooth Sync Timer for Verse Color
            _lyricSyncTimer = new DispatcherTimer(DispatcherPriority.Render);
            _lyricSyncTimer.Interval = TimeSpan.FromMilliseconds(100);
            _lyricSyncTimer.Tick += LyricSyncTimer_Tick;
            _lyricSyncTimer.Start();

            _mediaManager.Initialize();
            _isMediaPlaying = _mediaManager.IsPlaying;

            if (_settings.AutoCheckUpdates)
            {
                _ = CheckForUpdatesAsync(isStartupCheck: true);
            }

            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(PrewarmSettingsHost));
        }

        private void NotifyIcon_BalloonTipClicked(object? sender, EventArgs e)
        {
            if (_pendingUpdateResult != null)
            {
                ShowUpdateDialog(_pendingUpdateResult);
            }
        }

        private void LyricSyncTimer_Tick(object? sender, EventArgs e)
        {
            if (!_hasLyrics || !_isMediaPlaying) return;

            // Interpolate current position based on last system sync
            TimeSpan elapsedSinceSync = DateTime.Now - _lastSyncTime;
            TimeSpan currentEstimatedPosition = _lastMediaPosition + elapsedSinceSync;

            // Update UI with interpolated position
            UpdateLyricsUI(currentEstimatedPosition);
            _desktopWidget?.UpdatePlayback(currentEstimatedPosition, GetCurrentTrackDuration());
        }

        // ... existing methods ...

        private void Restart_Click(object sender, RoutedEventArgs e)
        {
            // Close notify icon to avoid ghost icon
            if (_notifyIcon != null)
            {
                _notifyIcon.BalloonTipClicked -= NotifyIcon_BalloonTipClicked;
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            
            // Start new instance
            var path = Environment.ProcessPath;
            if (path != null)
            {
                 // Use ShellExecute=true if needed, or simply start process
                 // With .NET Core/5+, Process.Start(string) uses ShellExecute=true by default only on Windows? No.
                 // Actually Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); is safer for exe execution from context like this.
                 try 
                 {
                     System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                 }
                 catch 
                 {
                     // Fallback
                     try { System.Diagnostics.Process.Start(path); } catch { }
                 }
            }
            
            Application.Current.Shutdown();
        }

        private void Exit_Click(object? sender, RoutedEventArgs? e)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.BalloonTipClicked -= NotifyIcon_BalloonTipClicked;
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            Application.Current.Shutdown();
        }

        private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesAsync(isStartupCheck: false);
        }

        private async Task CheckForUpdatesAsync(bool isStartupCheck)
        {
            if (_isCheckingUpdates)
            {
                return;
            }

            _isCheckingUpdates = true;

            try
            {
                var result = await _updateService.CheckForUpdatesAsync();

                if (!result.Success)
                {
                    if (!isStartupCheck)
                    {
                        UpdateDialogWindow.ShowForError(this, result.ErrorMessage ?? "发生了未知错误。", _settings.SettingsWindowMaterial, _settings.ApplicationTheme);
                    }
                    return;
                }

                if (result.NoReleasePublished)
                {
                    if (!isStartupCheck)
                    {
                        ShowUpdateDialog(result);
                    }
                    return;
                }

                if (!result.HasUpdate)
                {
                    if (!isStartupCheck)
                    {
                        ShowUpdateDialog(result);
                    }
                    return;
                }

                _pendingUpdateResult = result;

                if (isStartupCheck)
                {
                    _notifyIcon?.ShowBalloonTip(
                        5000,
                        "TaskbarInfo 有新版本",
                        $"当前 {result.CurrentVersionDisplay}，最新 {result.LatestVersionDisplay}。点击此通知可下载并安装。",
                        System.Windows.Forms.ToolTipIcon.Info);
                    return;
                }

                ShowUpdateDialog(result);
            }
            catch (Exception ex)
            {
                if (!isStartupCheck)
                {
                    UpdateDialogWindow.ShowForError(this, ex.Message, _settings.SettingsWindowMaterial, _settings.ApplicationTheme);
                }
            }
            finally
            {
                _isCheckingUpdates = false;
            }
        }

        private void ShowUpdateDialog(UpdateCheckResult result)
        {
            UpdateDialogWindow.ShowForResult(
                this,
                result,
                _settings.SettingsWindowMaterial,
                _settings.ApplicationTheme);
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
            }
        }

        private void SetTrayText(string text)
        {
            if (_notifyIcon == null) return;
            _notifyIcon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }

        private void ShowTrayWarning(string message)
        {
            if (_notifyIcon == null) return;
            _notifyIcon.ShowBalloonTip(3000, "TaskbarInfo", message, System.Windows.Forms.ToolTipIcon.Warning);
        }

        private void ApplySettings()
        {
            ApplyApplicationTheme();

            // Sync Process Monitoring
            SetupProcessMonitor();
            ManageTaskbarPerformance();
            ManageTaskbarTranslateButton();
            ManageWaterReminder();
            ConfigureQuickTranslateHotkey();
            
            // Sync Floating Window
            _floatingWindow?.ApplySettings(_settings);
            ManageDesktopWidget();
            _desktopWidget?.ApplySettings(_settings);

            // Do NOT set this.Width/Height here, as it causes conflict with MoveWindow in InjectIntoTaskbar
            // this.Width = _settings.Width;
            
            // Adjust Height for Double Line logic (Internal elements only)
            double baseHeight = 30; // Default Taskbar Height approx
            bool isActuallyDoubleLine = _settings.IsDoubleLine && _hasLyrics;
            double targetHeight = isActuallyDoubleLine ? baseHeight * 2 : baseHeight;
            // this.Height = targetHeight; // Don't set Window Height, let MoveWindow handle it
            
            // Allow TextContainer to fill available space
            TextContainer.Height = double.NaN; 
            TextContainer.VerticalAlignment = VerticalAlignment.Stretch;
            
            _mainLyricControl.FontSize = _settings.FontSize;

            // Apply Background Color
            try
            {
                var bgColor = (Color)ColorConverter.ConvertFromString(_settings.BackgroundColor);
                // Fix: If fully transparent, set Alpha to 1 to stay hit-testable
                if (bgColor.A == 0) bgColor.A = 1; 
                MainBorder.Background = new SolidColorBrush(bgColor);
            }
            catch
            {
                MainBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33000000"));
            }
            
            try 
            {
               var fontFamily = new FontFamily(_settings.FontFamily);
               var mainColor = (Color)ColorConverter.ConvertFromString(_settings.TextColor);
               var activeColor = (Color)ColorConverter.ConvertFromString(_settings.ActiveTextColor);
               
               // Apply colors to the brush defined in XAML instead of replacing it
               if (_mainLyricControl.Foreground is LinearGradientBrush mainBrush && mainBrush.GradientStops.Count >= 2)
               {
                   mainBrush.GradientStops[0].Color = activeColor;
                   mainBrush.GradientStops[1].Color = mainColor;
               }

               _mainLyricControl.FontFamily = fontFamily;
               _mainLyricControl.Opacity = 1.0; 

               try
               {
                   if (!string.IsNullOrEmpty(_settings.FontWeight))
                   {
                       var weightStr = _settings.FontWeight.Split(' ')[0];
                       var converter = new FontWeightConverter();
                       var obj = converter.ConvertFromString(weightStr);
                       _mainLyricControl.FontWeight = (obj as FontWeight?) ?? FontWeights.SemiBold;
                   }
                   else
                   {
                       _mainLyricControl.FontWeight = FontWeights.SemiBold;
                   }
               }
               catch
               {
                   _mainLyricControl.FontWeight = FontWeights.SemiBold;
               }

                if (isActuallyDoubleLine)
                {
                    _nextLyricControl.FontFamily = fontFamily;
                    
                    if (_nextLyricControl.Foreground is LinearGradientBrush nextBrush && nextBrush.GradientStops.Count >= 2)
                    {
                        nextBrush.GradientStops[0].Color = activeColor;
                        nextBrush.GradientStops[1].Color = mainColor;
                    }

                    _nextLyricControl.FontSize = Math.Max(9, _settings.FontSize - _settings.NextLyricFontSizeDiff); 
                    _nextLyricControl.Opacity = 0.7; // Use Opacity property for dimming
                    
                    try
                     {
                         if (!string.IsNullOrEmpty(_settings.NextLyricFontWeight))
                         {
                             var weightStr = _settings.NextLyricFontWeight.Split(' ')[0];
                             var converter = new FontWeightConverter();
                             var obj = converter.ConvertFromString(weightStr);
                             _nextLyricControl.FontWeight = (obj as FontWeight?) ?? FontWeights.Normal;
                         }
                         else
                         {
                              _nextLyricControl.FontWeight = FontWeights.Normal;
                         }
                     }
                    catch
                    {
                        _nextLyricControl.FontWeight = FontWeights.Normal;
                    }

                    _nextLyricControl.Visibility = Visibility.Visible;
                    
                    _mainLyricControl.TextWrapping = TextWrapping.NoWrap; 
                    _mainLyricControl.Height = double.NaN; 
                    // Set positions for double line mode
                    Canvas.SetTop(_mainLyricControl, 2);
                    Canvas.SetTop(_nextLyricControl, _settings.FontSize + 4);
                    
                    _nextLyricControl.TextWrapping = TextWrapping.NoWrap; 
                    // Don't set Canvas.SetTop here - AnimateDoubleLyricTransition and UpdateSingleLineScroll control this
                    // Canvas.SetTop(_nextLyricControl, _settings.FontSize + 8); 
                }
                else
                {
                    _nextLyricControl.Visibility = Visibility.Collapsed; // 显式隐藏第二行
                    _mainLyricControl.TextWrapping = TextWrapping.NoWrap;
                    UpdateVerticalCentering();
                }
            }
            catch {}

            // Effects
            _mainLyricControl.Effect = null;
            _nextLyricControl.Effect = null;

            if (_settings.EnableShadow || _settings.EnableOutline)
            {
                 var dropShadow = new System.Windows.Media.Effects.DropShadowEffect();
                 dropShadow.Color = Colors.Black;
                 
                 if (_settings.EnableOutline)
                 {
                     dropShadow.BlurRadius = 2;
                     dropShadow.ShadowDepth = 0;
                     dropShadow.Opacity = 1;
                 }
                 else
                 {
                     dropShadow.BlurRadius = 4;
                     dropShadow.ShadowDepth = 2;
                     dropShadow.Opacity = 0.8;
                 }
                 _mainLyricControl.Effect = dropShadow;
                 // Apply effect to next line too
                 _nextLyricControl.Effect = dropShadow;
            }

            if (_isShowingStatusText)
            {
                ApplyStatusTextLayout();
            }
        }

        private void UpdateVerticalCentering()
        {
            bool isActuallyDoubleLine = _settings.IsDoubleLine && _hasLyrics;
            if (isActuallyDoubleLine) return; // Managed by double line logic

            double containerH = TextContainer.ActualHeight;
            if (containerH <= 0 || double.IsNaN(containerH)) containerH = 30; // Fallback

            // Estimate Text Height based on FontSize (approx 1.3-1.4 em usually)
            // Or use ActualHeight if available? TextBlock ActualHeight updates late.
            // Let's use FontSize * 1.4 for safety or measure a sample.
            // Using FontSize * 1.35 is standard for Segoe UI.
            double textH = _settings.FontSize * 1.35;
            
            double top = (containerH - textH) / 2;
            
            Canvas.SetTop(_mainLyricControl, top);
        }

        private void ApplyStatusTextLayout()
        {
            _mainLyricControl.BeginAnimation(Canvas.LeftProperty, null);
            _nextLyricControl.BeginAnimation(Canvas.LeftProperty, null);
            _mainLyricControl.BeginAnimation(Canvas.TopProperty, null);
            _nextLyricControl.BeginAnimation(Canvas.TopProperty, null);

            Canvas.SetLeft(_mainLyricControl, 0);
            Canvas.SetLeft(_nextLyricControl, 0);

            _nextLyricControl.Text = string.Empty;
            _nextLyricControl.Visibility = Visibility.Collapsed;
            _nextLyricControl.Opacity = 0;

            _mainLyricControl.Visibility = Visibility.Visible;
            _mainLyricControl.Opacity = 1.0;
            _mainLyricControl.FontFamily = new FontFamily(_settings.FontFamily);
            _mainLyricControl.FontSize = _settings.FontSize;
            _mainLyricControl.FontWeight = GetConfiguredFontWeight(
                _settings.FontWeight,
                FontWeights.SemiBold);

            // Status text should always render as a single centered line,
            // regardless of whether double-line lyric mode is enabled.
            double containerH = TextContainer.ActualHeight;
            if (containerH <= 0 || double.IsNaN(containerH))
            {
                containerH = 30;
            }

            double textH = _settings.FontSize * 1.35;
            double top = (containerH - textH) / 2;
            Canvas.SetTop(_mainLyricControl, top);
        }

        private static FontWeight GetConfiguredFontWeight(string? value, FontWeight fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;

            try
            {
                var converter = new FontWeightConverter();
                var converted = converter.ConvertFromString(value.Split(' ')[0]);
                return (converted as FontWeight?) ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            OpenSettings(0);
        }

        private void ShowQuickTranslate()
        {
            if (_quickTranslatePopup?.IsVisible == true)
            {
                CloseQuickTranslatePopup();
                return;
            }

            if (!TryGetQuickTranslateLaunchOptions(out QuickTranslateLaunchOptions options))
            {
                ShowTrayWarning("无法获取任务栏位置，快捷翻译未打开。");
                return;
            }

            try
            {
                var popup = new QuickTranslatePopupWindow(_settings);
                _quickTranslatePopup = popup;
                popup.Closed += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ReferenceEquals(_quickTranslatePopup, popup))
                    {
                        _quickTranslatePopup = null;
                    }
                }));
                popup.ShowAt(options);
            }
            catch (Exception exception)
            {
                _quickTranslatePopup = null;
                ShowTrayWarning("快捷翻译窗口启动失败: " + exception.Message);
            }
        }

        private bool TryGetQuickTranslateLaunchOptions(out QuickTranslateLaunchOptions options)
        {
            options = null!;
            IntPtr taskbarHandle = TaskbarMonitorLocator.FindTaskbarWindow(_settings.TaskbarTranslateButtonMonitorDeviceName);
            if (taskbarHandle == IntPtr.Zero ||
                !UnmanagedMethods.GetWindowRect(taskbarHandle, out UnmanagedMethods.RECT taskbarRect))
            {
                return false;
            }

            System.Drawing.Rectangle taskbarBounds = System.Drawing.Rectangle.FromLTRB(
                taskbarRect.Left, taskbarRect.Top, taskbarRect.Right, taskbarRect.Bottom);
            System.Drawing.Rectangle buttonBounds;
            if (_taskbarTranslateButtonWindow?.TryGetScreenBounds(out buttonBounds) != true)
            {
                buttonBounds = taskbarBounds;
            }

            var screen = System.Windows.Forms.Screen.FromHandle(taskbarHandle);
            options = new QuickTranslateLaunchOptions(
                buttonBounds,
                taskbarBounds,
                screen.Bounds,
                screen.WorkingArea);
            return true;
        }

        private void CloseQuickTranslatePopup()
        {
            QuickTranslatePopupWindow? popup = _quickTranslatePopup;
            _quickTranslatePopup = null;
            try
            {
                popup?.Close();
            }
            catch
            {
            }
        }

        private void ConfigureQuickTranslateHotkey()
        {
            var source = PresentationSource.FromVisual(this) as HwndSource;
            if (source == null) return;

            if (_mainWindowSource != source)
            {
                _mainWindowSource?.RemoveHook(MainWindowMessageHook);
                _mainWindowSource = source;
                _mainWindowSource.AddHook(MainWindowMessageHook);
            }

            UnregisterQuickTranslateHotkey();
            string configuredHotkey = _settings.QuickTranslateHotkey?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(configuredHotkey)) return;

            if (!QuickTranslateHotkey.TryParse(configuredHotkey, out QuickTranslateHotkey hotkey))
            {
                ShowTrayWarning("快捷翻译快捷键格式无效，请使用 Ctrl+Alt+T 这类格式。");
                return;
            }

            IntPtr handle = new WindowInteropHelper(this).Handle;
            _quickTranslateHotkeyRegistered = handle != IntPtr.Zero &&
                UnmanagedMethods.RegisterHotKey(
                    handle,
                    QuickTranslateHotkeyId,
                    hotkey.Modifiers | UnmanagedMethods.MOD_NOREPEAT,
                    hotkey.VirtualKey);
            if (!_quickTranslateHotkeyRegistered)
            {
                ShowTrayWarning("快捷翻译快捷键已被其他程序占用。");
            }
        }

        private void UnregisterQuickTranslateHotkey()
        {
            if (!_quickTranslateHotkeyRegistered) return;

            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                UnmanagedMethods.UnregisterHotKey(handle, QuickTranslateHotkeyId);
            }
            _quickTranslateHotkeyRegistered = false;
        }

        private IntPtr MainWindowMessageHook(
            IntPtr handle,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message == UnmanagedMethods.WM_HOTKEY && wParam.ToInt32() == QuickTranslateHotkeyId)
            {
                handled = true;
                Dispatcher.BeginInvoke(new Action(ShowQuickTranslate));
            }
            return IntPtr.Zero;
        }

        private void OpenSettings(int initialNavIndex)
        {
            if (TryActivateSettingsHost(initialNavIndex)) return;

            EventWaitHandle? settingsAppliedEvent = null;
            RegisteredWaitHandle? settingsAppliedWait = null;
            int settingsApplied = 0;

            try
            {
                string settingsHost = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "SettingsHost",
                    "TaskbarInfo.Settings.exe");
                if (!System.IO.File.Exists(settingsHost))
                {
                    System.Windows.MessageBox.Show("设置窗口组件未找到，请重新生成开发版本。", "TaskbarInfo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string applyEventName = $"TaskbarInfo.Settings.{Environment.ProcessId}.{Guid.NewGuid():N}";
                settingsAppliedEvent = new EventWaitHandle(false, EventResetMode.AutoReset, applyEventName);
                settingsAppliedWait = ThreadPool.RegisterWaitForSingleObject(
                    settingsAppliedEvent,
                    (_, _) =>
                    {
                        Interlocked.Exchange(ref settingsApplied, 1);
                        Dispatcher.BeginInvoke(new Action(ReloadSettingsFromHost));
                    },
                    null,
                    Timeout.Infinite,
                    false);

                var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = settingsHost,
                    Arguments = $"\"{AppSettings.SettingsPath}\" --page={initialNavIndex} --apply-event=\"{applyEventName}\" --update-event=\"{_settingsUpdateRequestEventName}\"",
                    UseShellExecute = false
                });
                if (process == null)
                {
                    DisposeSettingsApplyNotification(settingsAppliedEvent, settingsAppliedWait);
                    return;
                }

                _settingsProcess = process;
                int processId = process.Id;
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (process.ExitCode == 0 && Interlocked.CompareExchange(ref settingsApplied, 0, 0) == 0)
                        {
                            ReloadSettingsFromHost();
                        }
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    DisposeSettingsApplyNotification(settingsAppliedEvent, settingsAppliedWait);
                    if (_settingsProcess?.Id == processId)
                    {
                        _settingsProcess = null;
                    }
                    try { process.Dispose(); } catch (InvalidOperationException) { }
                }));
            }
            catch (Exception exception)
            {
                DisposeSettingsApplyNotification(settingsAppliedEvent, settingsAppliedWait);
                System.Windows.MessageBox.Show($"无法打开设置窗口：{exception.Message}", "TaskbarInfo", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PrewarmSettingsHost()
        {
            if (_settingsProcess != null) return;

            string settingsHost = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "SettingsHost",
                "TaskbarInfo.Settings.exe");
            if (!System.IO.File.Exists(settingsHost)) return;

            try
            {
                var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = settingsHost,
                    Arguments = $"\"{AppSettings.SettingsPath}\" --keep-alive --hidden --parent-pid={Environment.ProcessId} --update-event=\"{_settingsUpdateRequestEventName}\"",
                    WorkingDirectory = System.IO.Path.GetDirectoryName(settingsHost),
                    UseShellExecute = false
                });
                if (process == null) return;

                _settingsProcess = process;
                int processId = process.Id;
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_settingsProcess?.Id != processId) return;
                    _settingsProcess.Dispose();
                    _settingsProcess = null;
                }));
            }
            catch
            {
                // Opening settings remains available through the normal on-demand path.
            }
        }

        private bool TryActivateSettingsHost(int initialNavIndex)
        {
            System.Diagnostics.Process? process = _settingsProcess;
            if (process == null) return false;

            try
            {
                if (process.HasExited)
                {
                    process.Dispose();
                    _settingsProcess = null;
                    return false;
                }

                process.Refresh();
                IntPtr windowHandle = process.MainWindowHandle;
                if (windowHandle == IntPtr.Zero)
                {
                    process.WaitForInputIdle(1500);
                    process.Refresh();
                    windowHandle = process.MainWindowHandle;
                }
                if (windowHandle == IntPtr.Zero)
                {
                    windowHandle = FindTopLevelWindowForProcess(process.Id);
                }
                if (windowHandle != IntPtr.Zero)
                {
                    if (SettingsNavigateMessage != 0)
                    {
                        UnmanagedMethods.PostMessage(
                            windowHandle,
                            SettingsNavigateMessage,
                            new IntPtr(initialNavIndex),
                            IntPtr.Zero);
                    }
                    UnmanagedMethods.ShowWindow(windowHandle, UnmanagedMethods.SW_RESTORE);
                    UnmanagedMethods.SetForegroundWindow(windowHandle);
                }
                return true;
            }
            catch
            {
                process.Dispose();
                _settingsProcess = null;
                return false;
            }
        }

        private static IntPtr FindTopLevelWindowForProcess(int processId)
        {
            IntPtr windowHandle = IntPtr.Zero;
            UnmanagedMethods.EnumWindows((candidate, _) =>
            {
                UnmanagedMethods.GetWindowThreadProcessId(candidate, out uint candidateProcessId);
                if (candidateProcessId != (uint)processId ||
                    UnmanagedMethods.GetParent(candidate) != IntPtr.Zero ||
                    !UnmanagedMethods.IsWindow(candidate))
                {
                    return true;
                }

                windowHandle = candidate;
                return false;
            }, IntPtr.Zero);
            return windowHandle;
        }

        private void CloseSettingsHost()
        {
            System.Diagnostics.Process? process = _settingsProcess;
            _settingsProcess = null;
            if (process == null) return;

            try
            {
                if (!process.HasExited) process.CloseMainWindow();
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        private void ReloadSettingsFromHost()
        {
            CloseQuickTranslatePopup();
            _settings = AppSettings.Load();
            ApplyApplicationTheme();
            _mediaManager.FilterAppIds = _settings.IncludedAppIds;
            _mediaManager.RefreshSession();
            ManageFloatingWindow();
            ApplySettings();
            InjectIntoTaskbar();
        }

        private void NotifySettingsApplied()
        {
            try
            {
                using var settingsApplied = EventWaitHandle.OpenExisting(SharedSettingsAppliedEventName);
                settingsApplied.Set();
            }
            catch
            {
                // Main window may have exited.
            }
        }

        private void ApplyApplicationTheme()
        {
            WpfThemeService.Apply(
                Application.Current,
                ApplicationThemeParser.Resolve(_settings.ApplicationTheme));
        }

        internal void RefreshSystemTheme()
        {
            if (ApplicationThemeParser.Parse(_settings.ApplicationTheme) != ApplicationThemePreference.System)
            {
                return;
            }

            ApplyApplicationTheme();
            ApplySettings();
        }

        private void StartSharedSettingsApplyNotification()
        {
            _sharedSettingsAppliedEvent = new EventWaitHandle(
                false,
                EventResetMode.AutoReset,
                SharedSettingsAppliedEventName);
            _sharedSettingsAppliedWait = ThreadPool.RegisterWaitForSingleObject(
                _sharedSettingsAppliedEvent,
                (_, _) => Dispatcher.BeginInvoke(new Action(ReloadSettingsFromHost)),
                null,
                Timeout.Infinite,
                false);
        }

        private void StopSharedSettingsApplyNotification()
        {
            _sharedSettingsAppliedWait?.Unregister(null);
            _sharedSettingsAppliedWait = null;
            _sharedSettingsAppliedEvent?.Dispose();
            _sharedSettingsAppliedEvent = null;
        }

        private void StartSettingsUpdateRequestNotification()
        {
            if (_settingsUpdateRequestedEvent != null) return;

            _settingsUpdateRequestEventName = $"TaskbarInfo.UpdateRequest.{Environment.ProcessId}";
            _settingsUpdateRequestedEvent = new EventWaitHandle(
                false,
                EventResetMode.AutoReset,
                _settingsUpdateRequestEventName);
            _settingsUpdateRequestedWait = ThreadPool.RegisterWaitForSingleObject(
                _settingsUpdateRequestedEvent,
                (_, _) => Dispatcher.BeginInvoke(new Action(() => _ = CheckForUpdatesAsync(isStartupCheck: false))),
                null,
                Timeout.Infinite,
                false);
        }

        private void StopSettingsUpdateRequestNotification()
        {
            _settingsUpdateRequestedWait?.Unregister(null);
            _settingsUpdateRequestedWait = null;
            _settingsUpdateRequestedEvent?.Dispose();
            _settingsUpdateRequestedEvent = null;
            _settingsUpdateRequestEventName = null;
        }

        private static void DisposeSettingsApplyNotification(EventWaitHandle? settingsAppliedEvent, RegisteredWaitHandle? settingsAppliedWait)
        {
            settingsAppliedWait?.Unregister(null);
            settingsAppliedEvent?.Dispose();
        }

        private void ManageTaskbarPerformance()
        {
            List<string> selectedMetrics = TaskbarPerformanceMetricCatalog.Normalize(_settings.TaskbarPerformanceMetrics);
            bool enabled = _settings.EnableTaskbarPerformanceMonitor && selectedMetrics.Count > 0;
            if (!enabled)
            {
                _taskbarPerformanceWindow?.Dispose();
                _taskbarPerformanceWindow = null;
                return;
            }

            if (_taskbarPerformanceWindow == null)
            {
                _taskbarPerformanceWindow = new TaskbarPerformanceWindow();
                _taskbarPerformanceWindow.SettingsRequested += (_, _) => OpenSettings(TaskbarPerformanceSettingsPage);
            }
            _taskbarPerformanceWindow.ApplySettings(
                _settings,
                GetPerformanceAnchorLeft());
        }

        private void ManageTaskbarTranslateButton()
        {
            if (!_settings.EnableTaskbarTranslateButton)
            {
                CloseQuickTranslatePopup();
                _taskbarTranslateButtonWindow?.Dispose();
                _taskbarTranslateButtonWindow = null;
                return;
            }

            if (_taskbarTranslateButtonWindow == null)
            {
                _taskbarTranslateButtonWindow = new TaskbarTranslateButtonWindow();
                _taskbarTranslateButtonWindow.TranslateRequested += (_, _) => ShowQuickTranslate();
                _taskbarTranslateButtonWindow.SettingsRequested += (_, _) => OpenSettings(QuickTranslateSettingsPage);
            }

            _taskbarTranslateButtonWindow.ApplySettings(_settings);
        }

        private void ManageWaterReminder()
        {
            if (!_settings.EnableWaterReminder)
            {
                _waterReminderTimer?.Stop();
                _waterReminderWasDue = false;
                _waterReminderPopup?.Dispose();
                _waterReminderPopup = null;
                _taskbarWaterReminderWindow?.Dispose();
                _taskbarWaterReminderWindow = null;
                return;
            }

            if (_taskbarWaterReminderWindow == null)
            {
                _taskbarWaterReminderWindow = new TaskbarWaterReminderWindow();
                _taskbarWaterReminderWindow.DrinkRequested += (_, _) => RecordWaterDrink();
                _taskbarWaterReminderWindow.SnoozeRequested += (_, _) => SnoozeWaterReminder();
                _taskbarWaterReminderWindow.SettingsRequested += (_, _) => OpenSettings(WaterReminderSettingsPage);
            }

            _taskbarWaterReminderWindow.ApplySettings(_settings);
            if (_waterReminderTimer == null)
            {
                _waterReminderTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromSeconds(30)
                };
                _waterReminderTimer.Tick += (_, _) => UpdateWaterReminder();
            }

            _waterReminderTimer.Start();
            UpdateWaterReminder();
        }

        private void UpdateWaterReminder()
        {
            if (!_settings.EnableWaterReminder || _taskbarWaterReminderWindow == null) return;

            string recordDate = _settings.WaterReminderRecordDate;
            int completedToday = _settings.WaterReminderCompletedToday;
            DateTime? lastCompletedAt = _settings.WaterReminderLastCompletedAt;
            DateTime? snoozedUntil = _settings.WaterReminderSnoozedUntil;
            WaterReminderStatus status = WaterReminderSchedule.GetStatus(_settings, DateTime.Now);
            if (recordDate != _settings.WaterReminderRecordDate ||
                completedToday != _settings.WaterReminderCompletedToday ||
                lastCompletedAt != _settings.WaterReminderLastCompletedAt ||
                snoozedUntil != _settings.WaterReminderSnoozedUntil)
            {
                _settings.Save();
            }

            _taskbarWaterReminderWindow.Update(status);
            _waterReminderPopup?.ApplyTheme(ApplicationThemeParser.Resolve(_settings.ApplicationTheme));
            bool shouldNotify = status.IsDue && !_waterReminderWasDue;
            if (shouldNotify && _settings.WaterReminderHideInFullscreen && IsFullscreenAppRunning())
            {
                // Defer while a fullscreen app is active; retried on the next tick.
                return;
            }
            _waterReminderWasDue = status.IsDue;
            if (!shouldNotify) return;

            ShowWaterReminderPopup(status);
            if (_settings.WaterReminderShowSystemNotification && _notifyIcon != null)
            {
                _notifyIcon.ShowBalloonTip(
                    3000,
                    "喝水助手",
                    "该喝水了。",
                    System.Windows.Forms.ToolTipIcon.Info);
            }
        }

        private void ShowWaterReminderPopup(WaterReminderStatus status)
        {
            if (_taskbarWaterReminderWindow == null) return;

            if (_waterReminderPopup == null)
            {
                _waterReminderPopup = new WaterReminderPopupWindow();
                _waterReminderPopup.DrinkRequested += (_, _) => RecordWaterDrink();
                _waterReminderPopup.SnoozeRequested += (_, _) => SnoozeWaterReminder();
            }

            _waterReminderPopup.ApplyTheme(ApplicationThemeParser.Resolve(_settings.ApplicationTheme));
            _waterReminderPopup.ShowAbove(_taskbarWaterReminderWindow, status);
        }

        private bool IsFullscreenAppRunning()
        {
            IntPtr foreground = UnmanagedMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;

            // The desktop icon list (SysListView32) and taskbar buttons are child windows of
            // Progman/WorkerW/Shell_TrayWnd. Resolve to the top-level window first so that
            // focusing the desktop is not mistaken for a fullscreen app.
            IntPtr root = UnmanagedMethods.GetAncestor(foreground, UnmanagedMethods.GA_ROOT);
            if (root == IntPtr.Zero) root = foreground;

            UnmanagedMethods.GetWindowThreadProcessId(root, out uint processId);
            if (processId == Environment.ProcessId) return false;

            IntPtr desktopWindow = UnmanagedMethods.GetDesktopWindow();
            IntPtr shellWindow = UnmanagedMethods.GetShellWindow();
            if (root == desktopWindow || root == shellWindow) return false;

            System.Text.StringBuilder className = new(256);
            if (UnmanagedMethods.GetClassName(root, className, className.Capacity) > 0)
            {
                string name = className.ToString();
                if (name == "Progman" || name == "WorkerW" || name == "Shell_TrayWnd" || name == "Shell_SecondaryTrayWnd")
                {
                    return false;
                }
            }

            if (!UnmanagedMethods.IsWindowVisible(root)) return false;
            if (UnmanagedMethods.IsIconic(root)) return false;
            if (!UnmanagedMethods.GetWindowRect(root, out UnmanagedMethods.RECT windowRect)) return false;

            int width = windowRect.Right - windowRect.Left;
            int height = windowRect.Bottom - windowRect.Top;
            if (width <= 0 || height <= 0) return false;

            // Use the monitor that contains the foreground window.
            UnmanagedMethods.POINT center = new()
            {
                X = windowRect.Left + width / 2,
                Y = windowRect.Top + height / 2
            };
            IntPtr monitor = UnmanagedMethods.MonitorFromPoint(center, UnmanagedMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return false;

            UnmanagedMethods.MONITORINFO mi = new() { cbSize = (uint)Marshal.SizeOf<UnmanagedMethods.MONITORINFO>() };
            if (!UnmanagedMethods.GetMonitorInfo(monitor, ref mi)) return false;

            // Fullscreen: the window covers the entire monitor, including the taskbar area.
            const int tolerance = 2;
            return windowRect.Left <= mi.rcMonitor.Left + tolerance &&
                   windowRect.Top <= mi.rcMonitor.Top + tolerance &&
                   windowRect.Right >= mi.rcMonitor.Right - tolerance &&
                   windowRect.Bottom >= mi.rcMonitor.Bottom - tolerance;
        }

        private void RecordWaterDrink()
        {
            WaterReminderSchedule.RecordDrink(_settings, DateTime.Now);
            _settings.Save();
            _waterReminderPopup?.Hide();
            _waterReminderWasDue = false;
            UpdateWaterReminder();
            _taskbarWaterReminderWindow?.ShowDrinkRecordedFeedback();
        }

        private void SnoozeWaterReminder()
        {
            WaterReminderSchedule.Snooze(_settings, DateTime.Now);
            _settings.Save();
            _waterReminderPopup?.Hide();
            _waterReminderWasDue = false;
            UpdateWaterReminder();
        }

        private int GetPerformanceAnchorLeft()
        {
            return _currentX;
        }

        // ... (Media Events kept as is, not shown here to avoid huge replacement) ...

        
        private void InjectIntoTaskbar()
        {
            try 
            {
                var helper = new WindowInteropHelper(this);
                IntPtr hWnd = helper.Handle;
                if (hWnd == IntPtr.Zero) return;

                // 1. Find the Taskbar
                IntPtr taskbarWnd = TaskbarMonitorLocator.FindTaskbarWindow(_settings.TaskbarMonitorDeviceName);
                if (taskbarWnd == IntPtr.Zero) return;

                // Check if we are already child
                IntPtr currentParent = UnmanagedMethods.GetParent(hWnd);
                if (currentParent != taskbarWnd)
                {
                    // 2. Modify Window Style to be a Child window
                    int style = UnmanagedMethods.GetWindowLong(hWnd, UnmanagedMethods.GWL_STYLE);
                    style = (style & ~(-2147483648)); // Remove Popup
                    style |= UnmanagedMethods.WS_CHILD; 
                    style |= UnmanagedMethods.WS_VISIBLE;

                    UnmanagedMethods.SetWindowLong(hWnd, UnmanagedMethods.GWL_STYLE, style);

                    // 3. Set the Taskbar as the parent
                    IntPtr result = UnmanagedMethods.SetParent(hWnd, taskbarWnd);
                    if (result == IntPtr.Zero)
                    {
                        // Log?
                        // int err = Marshal.GetLastWin32Error();
                        return;
                    }
                }

                // 4. Position the window
                UnmanagedMethods.RECT rectTaskbar;
                UnmanagedMethods.GetWindowRect(taskbarWnd, out rectTaskbar);
                int tbWidth = rectTaskbar.Right - rectTaskbar.Left;
                int tbHeight = rectTaskbar.Bottom - rectTaskbar.Top;
                
                int width = (int)_settings.Width;
                int height = tbHeight;
                int xPos = 0;

                // Always anchor against the tray area; drag handle updates OffsetX.
                IntPtr trayNotifyWnd = UnmanagedMethods.FindWindowEx(taskbarWnd, IntPtr.Zero, "TrayNotifyWnd", null);
                if (trayNotifyWnd != IntPtr.Zero)
                {
                    UnmanagedMethods.RECT rectTray;
                    UnmanagedMethods.GetWindowRect(trayNotifyWnd, out rectTray);
                    
                    // Tray Left (Screen) - Taskbar Left (Screen) - My Width - Offset
                    xPos = (rectTray.Left - rectTaskbar.Left) - width - _settings.OffsetX;
                }
                else
                {
                    // Fallback
                    xPos = tbWidth - width - _settings.OffsetX - 10;
                }

                // Boundary Checks
                if (xPos < 0) xPos = 0;
                // if (xPos + width > tbWidth) xPos = tbWidth - width; // Optional constraint

                UnmanagedMethods.MoveWindow(hWnd, xPos, 0, width, height, true);
                
                // InfoText.Text = "Attached"; // Don't overwrite lyrics during update
                _currentX = xPos;
                _taskbarPerformanceWindow?.UpdateLyricsPosition(GetPerformanceAnchorLeft());
            }
            catch (Exception)
            {
                // Silent fail to avoid crash loop
            }
        }

        private int _currentX;

        // Playback status update
        private void MediaManager_PlaybackStatusChanged(object? sender, Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus status)
        {
            _isMediaPlaying = (status == Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
            Dispatcher.Invoke(() => UpdatePlayPauseButton(status));
        }

        private void UpdatePlayPauseButton(Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus status)
        {
            bool isPlaying = status == Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            _floatingWindow?.SetPlaybackState(isPlaying);
            _desktopWidget?.SetPlaybackState(isPlaying);

            if (isPlaying)
            {
                // Currently playing - show pause icon
                PlayPauseButton.Content = "\uf04c"; // Pause icon (FontAwesome)
                PlayPauseButton.ToolTip = "暂停";
            }
            else
            {
                // Paused or stopped - show play icon
                PlayPauseButton.Content = "\uf04b"; // Play icon (FontAwesome)
                PlayPauseButton.ToolTip = "播放";
            }
        }

        // Media control event handlers
        private async void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            await _mediaManager.PlayPauseAsync();
        }

        private async void NextTrack_Click(object sender, RoutedEventArgs e)
        {
            await _mediaManager.NextTrackAsync();
        }

        private async void PreviousTrack_Click(object sender, RoutedEventArgs e)
        {
            await _mediaManager.PreviousTrackAsync();
        }

        private void OpenAppFilter_Click(object sender, RoutedEventArgs e)
        {
             var win = new AppFilterWindow(_settings, _mediaManager);
             if (win.ShowDialog() == true)
             {
                 _mediaManager.FilterAppIds = _settings.IncludedAppIds; // Update Filter
                 _mediaManager.RefreshSession(); // Apply immediately
             }
        }

        private void FloatingLyrics_Checked(object sender, RoutedEventArgs e)
        {
            _settings.EnableFloatingLyrics = true;
            _settings.Save();
            ManageFloatingWindow();
        }

        private void FloatingLyrics_Unchecked(object sender, RoutedEventArgs e)
        {
            _settings.EnableFloatingLyrics = false;
            _settings.Save();
            ManageFloatingWindow();
        }

        private void ManageFloatingWindow()
        {
            if (_settings.EnableFloatingLyrics)
            {
                if (_floatingWindow != null && _floatingWindow.IsAcrylicMode != _settings.FloatingLyricsUseAcrylic)
                {
                    _floatingWindow.CloseRequested -= FloatingWindow_CloseRequested;
                    _floatingWindow.SettingsRequested -= FloatingWindow_SettingsRequested;
                    _floatingWindow.PreviousTrackRequested -= FloatingWindow_PreviousTrackRequested;
                    _floatingWindow.PlayPauseRequested -= FloatingWindow_PlayPauseRequested;
                    _floatingWindow.NextTrackRequested -= FloatingWindow_NextTrackRequested;
                    _floatingWindow.Close();
                    _floatingWindow = null;
                }

                if (_floatingWindow == null)
                {
                    _floatingWindow = new FloatingLyricsWindow(_settings);
                    _floatingWindow.CloseRequested += FloatingWindow_CloseRequested;
                    _floatingWindow.SettingsRequested += FloatingWindow_SettingsRequested;
                    _floatingWindow.PreviousTrackRequested += FloatingWindow_PreviousTrackRequested;
                    _floatingWindow.PlayPauseRequested += FloatingWindow_PlayPauseRequested;
                    _floatingWindow.NextTrackRequested += FloatingWindow_NextTrackRequested;
                    _floatingWindow.Show();
                    // Apply CTX state explicitly if initialized late
                    _floatingWindow.SetClickThrough(_settings.FloatingLyricsClickThrough);
                    _floatingWindow.SetPlaybackState(_isMediaPlaying);
                }
                else
                {
                    _floatingWindow.Show();
                    // Ensure state
                    _floatingWindow.SetClickThrough(_settings.FloatingLyricsClickThrough);
                }
                
                if (FloatingLyricsCtxItem != null)
                {
                     FloatingLyricsCtxItem.IsEnabled = true;
                     FloatingLyricsCtxItem.Visibility = Visibility.Visible;
                }

                // Push current text if available
                if (!string.IsNullOrEmpty(_lastLyricText)) 
                {
                    string textToShow = _lastLyricText;
                    if (textToShow.Contains("|"))
                    {
                        textToShow = textToShow.Split('|')[0];
                    }
                    _floatingWindow.UpdateLyrics(textToShow);
                }
            }
            else
            {
                if (FloatingLyricsCtxItem != null)
                {
                     FloatingLyricsCtxItem.IsEnabled = false;
                     FloatingLyricsCtxItem.Visibility = Visibility.Collapsed;
                }
                _floatingWindow?.Hide();
            }
        }

        private void FloatingWindow_CloseRequested()
        {
            _settings.EnableFloatingLyrics = false;
            _settings.Save();

            if (FloatingLyricsMenuItem != null)
            {
                FloatingLyricsMenuItem.IsChecked = false;
            }

            ManageFloatingWindow();
        }

        private void FloatingWindow_SettingsRequested()
        {
            OpenSettings(2);
        }

        private Task FloatingWindow_PreviousTrackRequested() => _mediaManager.PreviousTrackAsync();

        private Task FloatingWindow_PlayPauseRequested() => _mediaManager.PlayPauseAsync();

        private Task FloatingWindow_NextTrackRequested() => _mediaManager.NextTrackAsync();

        private void ManageDesktopWidget()
        {
            if (_settings.EnableDesktopWidget)
            {
                if (_desktopWidget == null)
                {
                    _desktopWidget = new DesktopWidgetWindow(_settings);
                    _desktopWidget.CloseRequested += DesktopWidget_CloseRequested;
                    _desktopWidget.SettingsRequested += DesktopWidget_SettingsRequested;
                    _desktopWidget.PreviousTrackRequested += DesktopWidget_PreviousTrackRequested;
                    _desktopWidget.PlayPauseRequested += DesktopWidget_PlayPauseRequested;
                    _desktopWidget.NextTrackRequested += DesktopWidget_NextTrackRequested;
                    _desktopWidget.Show();
                    _desktopWidget.UpdateTrack(_currentTrackInfo);
                    _desktopWidget.SetPlaybackState(_isMediaPlaying);
                    _desktopWidget.UpdatePlayback(_lastMediaPosition, GetCurrentTrackDuration());

                    string initialLyric = !string.IsNullOrWhiteSpace(_lastCurrentLyric)
                        ? _lastCurrentLyric
                        : (_currentTrackInfo.HasTrack ? _currentTrackInfo.DisplayText : "等待播放");
                    _desktopWidget.UpdateLyrics(initialLyric);
                }
                else
                {
                    _desktopWidget.ApplySettings(_settings);
                    if (!_desktopWidget.IsVisible) _desktopWidget.Show();
                    _desktopWidget.EnsureDesktopAttachment();
                }

                EnsureDesktopHostTimer();
            }
            else
            {
                _desktopHostTimer?.Stop();
                if (_desktopWidget != null)
                {
                    _desktopWidget.CloseRequested -= DesktopWidget_CloseRequested;
                    _desktopWidget.SettingsRequested -= DesktopWidget_SettingsRequested;
                    _desktopWidget.PreviousTrackRequested -= DesktopWidget_PreviousTrackRequested;
                    _desktopWidget.PlayPauseRequested -= DesktopWidget_PlayPauseRequested;
                    _desktopWidget.NextTrackRequested -= DesktopWidget_NextTrackRequested;
                    _desktopWidget.Close();
                    _desktopWidget = null;
                }
            }
        }

        private void EnsureDesktopHostTimer()
        {
            if (_desktopHostTimer == null)
            {
                _desktopHostTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };
                _desktopHostTimer.Tick += (_, _) => _desktopWidget?.RefreshDesktopAttachment();
            }
            _desktopHostTimer.Start();
        }

        private void DesktopWidget_CloseRequested()
        {
            _settings.EnableDesktopWidget = false;
            _settings.Save();
            ManageDesktopWidget();
        }

        private void DesktopWidget_SettingsRequested() => OpenSettings(4);
        private Task DesktopWidget_PreviousTrackRequested() => _mediaManager.PreviousTrackAsync();
        private Task DesktopWidget_PlayPauseRequested() => _mediaManager.PlayPauseAsync();
        private Task DesktopWidget_NextTrackRequested() => _mediaManager.NextTrackAsync();

        private TimeSpan GetCurrentTrackDuration()
        {
            return _currentTrackInfo.DurationMs is > 0
                ? TimeSpan.FromMilliseconds(_currentTrackInfo.DurationMs.Value)
                : TimeSpan.Zero;
        }

        private void FloatingLyricsCtx_Checked(object sender, RoutedEventArgs e)
        {
            _settings.FloatingLyricsClickThrough = true;
            _settings.Save();
            if (_floatingWindow != null) _floatingWindow.SetClickThrough(true);
        }

        private void FloatingLyricsCtx_Unchecked(object sender, RoutedEventArgs e)
        {
             _settings.FloatingLyricsClickThrough = false;
             _settings.Save();
             if (_floatingWindow != null) _floatingWindow.SetClickThrough(false);
        }





        private async void MediaManager_MediaInfoChanged(object? sender, MediaTrackInfo track)
        {
            _currentTrackInfo = track;
            Dispatcher.Invoke(() => _desktopWidget?.UpdateTrack(track));
            string artist = track.Artist.Trim();
            string title = track.Title.Trim();
            string displayText = track.DisplayText;

            if (!track.HasTrack)
            {
                _lyricsSearchCts?.Cancel();
                Interlocked.Increment(ref _lyricsSearchVersion);
                _currentArtist = "";
                _currentTitle = "";
                _currentTrackKey = "";
                _hasLyrics = false;
                _lastLyricText = "";
                _lastCurrentLyric = "";

                Dispatcher.Invoke(() =>
                {
                    TooltipTitleText.Text = "未知歌曲";
                    TooltipArtistText.Text = "未知艺术家";
                    MenuSongInfoText.Text = displayText;
                    MenuSongInfoText.ToolTip = displayText;
                    ShowStatusText(displayText);
                    _floatingWindow?.UpdateLyrics(displayText);
                });
                return;
            }

            string trackKey = $"{track.SourceAppId}\n{title}\n{artist}\n{track.Album}";
            if (string.Equals(trackKey, _currentTrackKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _currentTrackKey = trackKey;
            _currentArtist = artist;
            _currentTitle = title;
            _hasLyrics = false;
            _lastLyricText = "";
            _lastCurrentLyric = "";

            _lyricsSearchCts?.Cancel();
            _lyricsSearchCts?.Dispose();
            var searchCts = new CancellationTokenSource();
            _lyricsSearchCts = searchCts;
            int searchVersion = Interlocked.Increment(ref _lyricsSearchVersion);

            Dispatcher.Invoke(() =>
            {
                TooltipTitleText.Text = title;
                TooltipArtistText.Text = string.IsNullOrEmpty(artist) ? "未知艺术家" : artist;
                MenuSongInfoText.Text = displayText;
                MenuSongInfoText.ToolTip = displayText;
                ShowStatusText("正在搜索歌词…");
                _floatingWindow?.UpdateLyrics("正在搜索歌词…");
            });

            var result = await _lyricsEngine.SearchAndLoadLyricsAsync(track, searchCts.Token);
            if (searchCts.IsCancellationRequested || searchVersion != _lyricsSearchVersion)
            {
                return;
            }

            _hasLyrics = result.IsSuccess;
            Dispatcher.Invoke(() =>
            {
                if (_hasLyrics)
                {
                    ShowStatusText(displayText);
                    ApplySettings();
                    UpdateLyricsUI(_lastMediaPosition);
                }
                else
                {
                    string statusText = string.IsNullOrEmpty(result.StatusText)
                        ? "未找到歌词"
                        : result.StatusText;
                    ShowStatusText(statusText);
                    _floatingWindow?.UpdateLyrics(statusText);
                    ApplySettings();
                }
            });
        }

        private void MediaManager_PlaybackPositionChanged(object? sender, TimeSpan position)
        {
            // Sync anchor point
            _lastMediaPosition = position;
            _lastSyncTime = DateTime.Now;
            _desktopWidget?.UpdatePlayback(position, GetCurrentTrackDuration());

        }

        private void UpdateLyricsUI(TimeSpan position)
        {
            if (!_hasLyrics) return;

            _isShowingStatusText = false;

            var adjustedPosition = position - TimeSpan.FromSeconds(_settings.LyricOffsetSeconds);

            if (_settings.IsDoubleLine)
            {
                var (current, next) = _lyricsEngine.GetLyricsForTime(adjustedPosition);
                
                if (current != null)
                {
                    // Update Content
                    if (current.Text != _lastCurrentLyric)
                    {
                        _lastCurrentLyric = current.Text;
                        _lastLyricText = $"{current.Text}|{(next?.Text ?? "")}";
                        
                        UpdateSingleLineProgress(_mainLyricControl, current, adjustedPosition);
                        
                        _nextLyricControl.Text = next?.Text ?? "";
                        _nextLyricControl.Visibility = string.IsNullOrEmpty(_nextLyricControl.Text) ? Visibility.Collapsed : Visibility.Visible;
                    }
                    else if (current.HasSyllables)
                    {
                        UpdateSingleLineProgress(_mainLyricControl, current, adjustedPosition);
                    }
                }
            }
            else
            {
                // Single Line Mode
                var (current, _) = _lyricsEngine.GetLyricsForTime(adjustedPosition);
                if (current != null)
                {
                    if (current.Text != _lastLyricText)
                    {
                        _lastLyricText = current.Text;
                        UpdateSingleLineProgress(_mainLyricControl, current, adjustedPosition);
                    }
                    else if (current.HasSyllables)
                    {
                        UpdateSingleLineProgress(_mainLyricControl, current, adjustedPosition);
                    }
                }
            }
        }

        private void UpdateSingleLineProgress(TextBlock target, LyricsEngine.LyricLine line, TimeSpan time)
        {
            // Update Text if different
            if (target.Text != line.Text)
            {
                target.Text = line.Text;
                // Auto scroll if long - Disable infinite loop in double line mode to avoid control collision
                UpdateSingleLineScroll(target, line.Text, (line.EndMs - line.StartMs) / 1000.0, !_settings.IsDoubleLine);
            }

            double progress = _lyricsEngine.GetLineProgress(line, time);
            var brush = target.Foreground as LinearGradientBrush;
            if (brush != null && brush.GradientStops.Count >= 2)
            {
                brush.GradientStops[0].Offset = progress;
                brush.GradientStops[1].Offset = Math.Min(1.0, progress + 0.05);
            }

            if (_settings.EnableFloatingLyrics && _floatingWindow != null && target == _mainLyricControl)
            {
                _floatingWindow.UpdateLyrics(line.Text);
                _floatingWindow.UpdateProgress(progress, line.HasSyllables);
            }

            if (target == _mainLyricControl)
            {
                _desktopWidget?.UpdateLyrics(line.Text);
            }
        }

        private void AnimateDoubleLyricTransition(string newCurrent, double currentDur, string newNext, double nextDur)
        {
             // Immediate update for floating window
             if (_settings.EnableFloatingLyrics && _floatingWindow != null)
                 _floatingWindow.UpdateLyrics(newCurrent);

             // Resolve Next Weight
             FontWeight nextWeight = FontWeights.Normal;
             try 
             {
                  if (!string.IsNullOrEmpty(_settings.NextLyricFontWeight))
                  {
                      var weightStr = _settings.NextLyricFontWeight.Split(' ')[0];
                      var converter = new FontWeightConverter();
                      var obj = converter.ConvertFromString(weightStr);
                      nextWeight = (obj as FontWeight?) ?? FontWeights.Normal;
                  }
             }
             catch {}

             double nextFontSize = Math.Max(9, _settings.FontSize - _settings.NextLyricFontSizeDiff);

             // 1. Swap References Logic
             var oldMain = _mainLyricControl;
             var oldNext = _nextLyricControl;

             // Promoted: Used to be Next (Bottom), now Main (Top)
             _mainLyricControl = oldNext; 
             // Demoted: Used to be Main (Top), now Next (Bottom)
             _nextLyricControl = oldMain; 

             double topPos = 2;
             double bottomPos = _settings.FontSize + 4; // Use updated offset
             
             // 2. Animate NEW Main (Moving Bottom -> Top)
             // Reset animations first
             _mainLyricControl.BeginAnimation(Canvas.LeftProperty, null);
             Canvas.SetLeft(_mainLyricControl, 0);
             _mainLyricControl.BeginAnimation(Canvas.TopProperty, null);
             _mainLyricControl.BeginAnimation(TextBlock.FontSizeProperty, null);
             _mainLyricControl.BeginAnimation(TextBlock.OpacityProperty, null);
             
             _mainLyricControl.Visibility = Visibility.Visible;
             _mainLyricControl.Text = newCurrent;
             
             // Transition Weight: From NextWeight to MainWeight (SemiBold)
             _mainLyricControl.FontWeight = FontWeights.SemiBold; 

             // Animations
             var moveUp = new DoubleAnimation(bottomPos, topPos, TimeSpan.FromMilliseconds(400)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
             var growFont = new DoubleAnimation(nextFontSize, _settings.FontSize, TimeSpan.FromMilliseconds(400)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
             var fadeIn = new DoubleAnimation(0.7, 1.0, TimeSpan.FromMilliseconds(400)); 

             _mainLyricControl.BeginAnimation(Canvas.TopProperty, moveUp);
             _mainLyricControl.BeginAnimation(TextBlock.FontSizeProperty, growFont);
             _mainLyricControl.BeginAnimation(TextBlock.OpacityProperty, fadeIn);

             // 3. Animate NEW Next (Old Main leaving Top)
             _nextLyricControl.BeginAnimation(Canvas.LeftProperty, null);
             Canvas.SetLeft(_nextLyricControl, 0);
             _nextLyricControl.BeginAnimation(Canvas.TopProperty, null);
             _nextLyricControl.BeginAnimation(TextBlock.FontSizeProperty, null);
             _nextLyricControl.BeginAnimation(TextBlock.OpacityProperty, null);
             
             _nextLyricControl.Visibility = Visibility.Visible;

             // Exiting animation: Fade out 
             var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200));
             
             // We need to capture variables for closure
             var targetNextWeight = nextWeight;
             var targetNextFontSize = nextFontSize;

             fadeOut.Completed += (s, e) => 
             {
                 // Recycle for New Next
                 _nextLyricControl.BeginAnimation(Canvas.TopProperty, null); 
                 _nextLyricControl.Text = newNext;
                 _nextLyricControl.Visibility = Visibility.Visible;
                 
                 Canvas.SetTop(_nextLyricControl, bottomPos);
                 _nextLyricControl.FontSize = targetNextFontSize;
                 _nextLyricControl.FontWeight = targetNextWeight; 
                 
                 // Fade In New Next
                 var nextFadeIn = new DoubleAnimation(0.0, 0.7, TimeSpan.FromMilliseconds(200));
                 _nextLyricControl.BeginAnimation(TextBlock.OpacityProperty, nextFadeIn);
             };

             _nextLyricControl.BeginAnimation(TextBlock.OpacityProperty, fadeOut);
             
             // 4. Trigger Scroll for Main after animation
             var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
             timer.Tick += (s, e) =>
             {
                 timer.Stop();
                 // Finalize State 
                 _mainLyricControl.BeginAnimation(Canvas.TopProperty, null);
                 Canvas.SetTop(_mainLyricControl, topPos);
                 _mainLyricControl.BeginAnimation(TextBlock.FontSizeProperty, null);
                 _mainLyricControl.FontSize = _settings.FontSize;
                 _mainLyricControl.BeginAnimation(TextBlock.OpacityProperty, null);
                 _mainLyricControl.Opacity = 1.0;
                 _mainLyricControl.Visibility = Visibility.Visible;
                 
                 UpdateSingleLineScroll(_mainLyricControl, newCurrent, currentDur);
             };
             timer.Start();
        }
        
        private void ShowStatusText(string text)
        {
            _isShowingStatusText = true;
            ApplyStatusTextLayout();
            UpdateSingleLineScroll(_mainLyricControl, text, 0, true);
            _desktopWidget?.UpdateLyrics(text);
        }

        private void UpdateSingleLineScroll(TextBlock target, string text, double durationSeconds, bool isInfinite = false)
        {
             target.BeginAnimation(Canvas.LeftProperty, null);
             Canvas.SetLeft(target, 0);
             target.Visibility = Visibility.Visible;
             
             _nextLyricControl.BeginAnimation(Canvas.LeftProperty, null);

             target.UpdateLayout(); 

             double textWidth = target.ActualWidth;
             double containerWidth = TextContainer.ActualWidth;

             if (textWidth > containerWidth)
             {
                 double gap = 50; 
                 if (isInfinite)
                 {
                     _nextLyricControl.Text = text;
                     _nextLyricControl.Visibility = Visibility.Visible;
                     _nextLyricControl.FontSize = target.FontSize; 
                     _nextLyricControl.FontWeight = target.FontWeight; 
                     _nextLyricControl.Opacity = target.Opacity;
                     _nextLyricControl.Foreground = target.Foreground;
                     
                     Canvas.SetTop(_nextLyricControl, Canvas.GetTop(target));
                     _nextLyricControl.UpdateLayout();
                     
                     double totalDistance = textWidth + gap;
                     double duration = totalDistance / 30.0;

                     DoubleAnimation animMain = new DoubleAnimation(0, -totalDistance, new Duration(TimeSpan.FromSeconds(duration))) { RepeatBehavior = RepeatBehavior.Forever };
                     DoubleAnimation animNext = new DoubleAnimation(totalDistance, 0, new Duration(TimeSpan.FromSeconds(duration))) { RepeatBehavior = RepeatBehavior.Forever };

                     target.BeginAnimation(Canvas.LeftProperty, animMain);
                     _nextLyricControl.BeginAnimation(Canvas.LeftProperty, animNext);
                 }
                 else
                 {
                     double delay = 1.0;
                     double scrollTime = Math.Max(2.0, durationSeconds - 2.0);
                     DoubleAnimation animation = new DoubleAnimation(0, -(textWidth - containerWidth + 20), new Duration(TimeSpan.FromSeconds(scrollTime))) { BeginTime = TimeSpan.FromSeconds(delay) };
                     target.BeginAnimation(Canvas.LeftProperty, animation);
                 }
             }
             else
             {
                 if (isInfinite) _nextLyricControl.Visibility = Visibility.Collapsed;
             }
        }
        
        private void UpdateInfo()
        {
             // Fallback or Trigger manual update
        }

        private void MediaManager_AppIdChanged(object? sender, string appId)
        {
            Dispatcher.Invoke(() => UpdateAppIcon(appId));
        }

        private void UpdateAppIcon(string appId)
        {
            if (string.IsNullOrEmpty(appId))
            {
                AppIcon.Visibility = Visibility.Collapsed;
                return;
            }

            ImageSource? iconSource = null;

            // 1. Try if it's a direct path
            if (appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(appId))
            {
                iconSource = GetIconFromPath(appId);
            }
            
            // 2. Fallback: Try to find a running process that matches the ID or common names
            if (iconSource == null)
            {
                string processName = appId;
                
                // Cleanup ID if it looks like a UWP ID or complex string
                if (processName.Contains("!"))
                {
                   // processName = processName.Split('!')[0]; // Simple attempt
                }
                
                // Clean up extension
                if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    processName = System.IO.Path.GetFileNameWithoutExtension(processName);

                iconSource = GetIconFromProcessName(processName);
                
                // Specific fix for "Spotify" which might be named simply "Spotify" in ID but "Spotify.exe" in process
                if (iconSource == null && appId.ToLower().Contains("spotify"))
                     iconSource = GetIconFromProcessName("Spotify");
                     
                else if (iconSource == null && appId.ToLower().Contains("cloudmusic")) // NetEase
                     iconSource = GetIconFromProcessName("cloudmusic"); // Or cloudmusic.exe
                     
                 else if (iconSource == null && appId.ToLower().Contains("qqmusic")) // QQ Music
                     iconSource = GetIconFromProcessName("QQMusic");
            }

            if (iconSource != null)
            {
                AppIcon.Source = iconSource;
                AppIcon.Visibility = Visibility.Visible;
            }
            else
            {
                AppIcon.Visibility = Visibility.Collapsed;
            }
        }

        private ImageSource? GetIconFromProcessName(string processName)
        {
            try
            {
                var processes = System.Diagnostics.Process.GetProcessesByName(processName);
                if (processes.Length > 0)
                {
                    try 
                    {
                        var path = processes[0].MainModule?.FileName;
                        if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                        {
                            return GetIconFromPath(path);
                        }
                    }
                    catch { } // MainModule might deny access
                }
            }
            catch {}
            return null;
        }

        private ImageSource? GetIconFromPath(string path)
        {
            try
            {
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    if (icon != null)
                    {
                        return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                    }
                }
            }
            catch {}
            return null;
        }



        private void DragHandle_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var el = sender as UIElement;
            if (el != null)
            {
                _isDraggingHandle = true;
                el.CaptureMouse();
                try
                {
                    _dragStartMouseScreenPos = PointToScreen(e.GetPosition(this));
                }
                catch
                {
                    _dragStartMouseScreenPos = e.GetPosition(null);
                }
                _dragStartOffsetX = _settings.OffsetX;
                e.Handled = true;
            }
        }

        private void DragHandle_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isDraggingHandle) return;

            Point currentMouseScreenPos;
            try
            {
                currentMouseScreenPos = PointToScreen(e.GetPosition(this));
            }
            catch
            {
                currentMouseScreenPos = e.GetPosition(null);
            }

            double deltaX = currentMouseScreenPos.X - _dragStartMouseScreenPos.X;

            int newOffsetX = _dragStartOffsetX - (int)Math.Round(deltaX);

            // 限制 OffsetX 不能为负
            if (newOffsetX < 0) newOffsetX = 0;

            if (_settings.OffsetX != newOffsetX)
            {
                _settings.OffsetX = newOffsetX;
                InjectIntoTaskbar();
            }
            e.Handled = true;
        }

        private void DragHandle_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isDraggingHandle)
            {
                var el = sender as UIElement;
                if (el != null)
                {
                    el.ReleaseMouseCapture();
                }
                _isDraggingHandle = false;
                
                // 拖拽结束时保存设置
                _settings.Save();
                e.Handled = true;
            }
        }

        private void AppIcon_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                string appId = _mediaManager.CurrentAppAppUserModelId;
                if (!string.IsNullOrEmpty(appId))
                {
                    TryActivateApp(appId);
                }
            }
        }

        private void TryActivateApp(string appId)
        {
             // 1. Try generic process finding logic
             string processName = appId;
             if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                processName = System.IO.Path.GetFileNameWithoutExtension(processName);

             bool success = TryActivateAppByName(processName);

             // 2. Fallbacks for common apps
             if (!success)
             {
                 if (appId.ToLower().Contains("spotify")) TryActivateAppByName("Spotify");
                 else if (appId.ToLower().Contains("cloudmusic")) TryActivateAppByName("cloudmusic");
                 else if (appId.ToLower().Contains("qqmusic")) TryActivateAppByName("QQMusic");
             }
        }

        private bool TryActivateAppByName(string processName)
        {
            var pids = new System.Collections.Generic.HashSet<uint>();
            try
            {
                var processes = System.Diagnostics.Process.GetProcessesByName(processName);
                if (processes.Length == 0) return false;

                foreach (var p in processes)
                {
                    pids.Add((uint)p.Id);
                    // Standard check
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        if (UnmanagedMethods.IsWindowVisible(p.MainWindowHandle))
                        {
                            IntPtr hwnd = p.MainWindowHandle;
                             // If minimized, restore
                            UnmanagedMethods.ShowWindow(hwnd, 9); // SW_RESTORE
                            UnmanagedMethods.SetForegroundWindow(hwnd);
                            return true;
                        }
                    }
                }
                
                // Advanced Search
                IntPtr bestCandidate = IntPtr.Zero;

                UnmanagedMethods.EnumWindows(delegate(IntPtr hwnd, IntPtr lParam)
                {
                    uint pid;
                    UnmanagedMethods.GetWindowThreadProcessId(hwnd, out pid);
                    
                    if (pids.Contains(pid))
                    {
                        // Must have a title to be a candidate for a "Main Window"
                        int len = UnmanagedMethods.GetWindowTextLength(hwnd);
                        if (len > 0)
                        {
                            bool isVisible = UnmanagedMethods.IsWindowVisible(hwnd);
                            
                            // If we found a visible window with a title, it's a strong candidate
                            if (isVisible)
                            {
                                bestCandidate = hwnd;
                                return false; // Stop searching, we found a good one
                            }
                            
                            // Only update if we haven't found a candidate yet
                            if (bestCandidate == IntPtr.Zero)
                            {
                                bestCandidate = hwnd;
                            }
                        }
                    }
                    return true; // Continue
                }, IntPtr.Zero);
                
                if (bestCandidate != IntPtr.Zero)
                {
                    UnmanagedMethods.ShowWindow(bestCandidate, 9); // Force Restore
                    UnmanagedMethods.SetForegroundWindow(bestCandidate);
                    return true;
                }
            }
            catch
            { 
                 // Troubleshooting
                 // System.Windows.MessageBox.Show("Error: " + ex.Message);
            }
            return false;
        }
    }
}
