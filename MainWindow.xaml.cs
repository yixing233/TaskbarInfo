using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.Windows.Media;

using System.Windows.Media.Imaging;

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
        public MainWindow()
        {
            InitializeComponent();
            
            // Initialize references first
            _mainLyricControl = InfoText;
            _nextLyricControl = NextLyricText;
            
            // Then initialize TextBlock positions (ApplySettings no longer sets these)
            Canvas.SetTop(_mainLyricControl, 2);
            Canvas.SetLeft(_mainLyricControl, 0);
            Canvas.SetTop(_nextLyricControl, 0); // Will be set by logic based on mode
            Canvas.SetLeft(_nextLyricControl, 0);
        }

        private AppSettings _settings = new AppSettings();
        
        private MediaManager _mediaManager = new MediaManager();
        private LyricsEngine _lyricsEngine = new LyricsEngine();
        private string _currentArtist = "";
        private string _currentTitle = "";
        private bool _hasLyrics = false;
        private string _lastLyricText = "";
        private string _lastCurrentLyric = ""; // Track current lyric separately for animation
        
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
        
        private DispatcherTimer? _processMonitorTimer;

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

        private async void Window_Loaded(object sender, RoutedEventArgs e)
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
            _notifyIcon.Text = "LyricsX";
            
            // Handle Mouse Up to show WPF ContextMenu
            _notifyIcon.MouseUp += (s, args) => 
            {
                if (args.Button == System.Windows.Forms.MouseButtons.Right)
                {
                    // Activate window to ensure menu closes when clicking outside
                    var helper = new WindowInteropHelper(this);
                    UnmanagedMethods.SetForegroundWindow(helper.Handle);
                    
                    if (MainBorder.ContextMenu != null)
                    {
                        MainBorder.ContextMenu.IsOpen = true;
                    }
                }
            };
            
            // Old WinForms Menu removed
            // var trayMenu = new System.Windows.Forms.ContextMenuStrip(); 
            // ...

            // Load Settings
            _settings = AppSettings.Load();
            
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
            
            InjectIntoTaskbar();
            
            // Initialize Media Manager
            _mediaManager.FilterAppIds = _settings.IncludedAppIds; // Initialize Filter
            _mediaManager.MediaInfoChanged += MediaManager_MediaInfoChanged;
            _mediaManager.PlaybackPositionChanged += MediaManager_PlaybackPositionChanged;
            _mediaManager.PlaybackStatusChanged += MediaManager_PlaybackStatusChanged;
            _mediaManager.AppIdChanged += MediaManager_AppIdChanged;
            
            // Setup High-Frequency Smooth Sync Timer for Verse Color
            _lyricSyncTimer = new DispatcherTimer(DispatcherPriority.Render);
            _lyricSyncTimer.Interval = TimeSpan.FromMilliseconds(30); 
            _lyricSyncTimer.Tick += LyricSyncTimer_Tick;
            _lyricSyncTimer.Start();

            _mediaManager.Initialize();
            _isMediaPlaying = _mediaManager.IsPlaying;
        }

        private void LyricSyncTimer_Tick(object? sender, EventArgs e)
        {
            if (!_hasLyrics || !_isMediaPlaying) return;

            // Interpolate current position based on last system sync
            TimeSpan elapsedSinceSync = DateTime.Now - _lastSyncTime;
            TimeSpan currentEstimatedPosition = _lastMediaPosition + elapsedSinceSync;

            // Update UI with interpolated position
            UpdateLyricsUI(currentEstimatedPosition);
        }

        // ... existing methods ...

        private void Restart_Click(object sender, RoutedEventArgs e)
        {
            // Close notify icon to avoid ghost icon
            if (_notifyIcon != null)
            {
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
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            Application.Current.Shutdown();
        }

        private void ApplySettings()
        {
            // Sync Process Monitoring
            SetupProcessMonitor();
            
            // Sync Floating Window
            _floatingWindow?.ApplySettings();

            // Do NOT set this.Width/Height here, as it causes conflict with MoveWindow in InjectIntoTaskbar
            // this.Width = _settings.Width;
            
            // Adjust Height for Double Line logic (Internal elements only)
            double baseHeight = 30; // Default Taskbar Height approx
            double targetHeight = _settings.IsDoubleLine ? baseHeight * 2 : baseHeight;
            // this.Height = targetHeight; // Don't set Window Height, let MoveWindow handle it
            
            // Allow TextContainer to fill available space
            TextContainer.Height = double.NaN; 
            TextContainer.VerticalAlignment = VerticalAlignment.Stretch;
            
            InfoText.FontSize = _settings.FontSize;

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
               _mainLyricControl.FontWeight = FontWeights.SemiBold;

               if (_settings.IsDoubleLine)
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
                   // In single line mode, hide next lyric unless it's being used as ghost block
                   // Don't force Visibility here - UpdateSingleLineScroll manages it
                   // _nextLyricControl.Visibility = Visibility.Collapsed;
                   _mainLyricControl.TextWrapping = TextWrapping.NoWrap;
                   // Logic moved to UpdateVerticalCentering
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
        }

        private void UpdateVerticalCentering()
        {
            if (_settings.IsDoubleLine) return; // Managed by double line logic

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

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            // Backup
            AppSettings backup = _settings.Clone();

            // Callback for Preview
            Action onPreview = () => 
            {
                ApplySettings();
                InjectIntoTaskbar();
            };

            SettingsWindow win = new SettingsWindow(_settings, onPreview);
            
            if (win.ShowDialog() == true)
            {
                // Already applied via preview
                ApplySettings(); 
                InjectIntoTaskbar(); 
            }
            else
            {
                // Cancelled - Restore backup
                _settings = backup;
                ApplySettings();
                InjectIntoTaskbar();
            }
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
                IntPtr taskbarWnd = UnmanagedMethods.FindWindow("Shell_TrayWnd", null);
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

                if (_settings.PositionMode == 1)
                {
                    // === Left Mode ===
                    // Simple Offset from Left
                    xPos = _settings.OffsetX;
                }
                else
                {
                    // === Right Mode (Default) ===
                    // Relative to Tray
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
                }

                // Boundary Checks
                if (xPos < 0) xPos = 0;
                // if (xPos + width > tbWidth) xPos = tbWidth - width; // Optional constraint

                UnmanagedMethods.MoveWindow(hWnd, xPos, 0, width, height, true);
                
                // InfoText.Text = "Attached"; // Don't overwrite lyrics during update
                _currentX = xPos;
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
            if (status == Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
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
                if (_floatingWindow == null)
                {
                    _floatingWindow = new FloatingLyricsWindow();
                    _floatingWindow.Show();
                    // Apply CTX state explicitly if initialized late
                    _floatingWindow.SetClickThrough(_settings.FloatingLyricsClickThrough);
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





        private async void MediaManager_MediaInfoChanged(object? sender, string text)
        {
            // Parse Artist - Title from the simple text (assuming we formatted it "Artist - Title")
            // Actually, we should expose Artist/Title raw events to be cleaner, 
            // but for now let's rely on the formatted string or we can hackily parse.
            // Wait, MediaManager formats it. Better to modify MediaManager to pass object or parse here?
            // Let's modify MediaManager to pass a struct? Too much change?
            // Let's assume text format if it contains " - ".
            
            // Or better, let's keep it simple: Just trigger reload.
            // But we need artist/title for search.
            // Update: MediaManager only passes string. I should update MediaManager to expose _currentSession properties or pass a custom args.
            
            // TEMP FIX: Re-query session or make MediaManager event richer. 
            // Let's cheat: We only have the string. 
            // If Text is [Paused] ... remove it.
            
            string raw = text;
            var parts = raw.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
            
            string artist = parts.Length > 0 ? parts[0] : "";
            string title = parts.Length > 1 ? parts[1] : raw; // Fallback
            if (parts.Length > 1) title = string.Join(" - ", parts.Skip(1));


            if (artist != _currentArtist || title != _currentTitle)
            {
                _currentArtist = artist;
                _currentTitle = title;
                _hasLyrics = false;
                _lastLyricText = "";
                _lastCurrentLyric = "";
                
                // Update tooltip and menu with new song info
                Dispatcher.Invoke(() =>
                {
                    TooltipTitleText.Text = string.IsNullOrEmpty(title) ? "未知歌曲" : title;
                    TooltipArtistText.Text = string.IsNullOrEmpty(artist) ? "未知艺术家" : artist;
                    
                    var menuText = this.FindName("MenuSongInfoText") as TextBlock;
                    if (menuText != null)
                    {
                        string info = $"{TooltipTitleText.Text} - {TooltipArtistText.Text}";
                        menuText.Text = info;
                        menuText.ToolTip = info; // Add tooltip for full text
                    }
                    
                    UpdateTextWithScroll(text);
                });
                
                // Search lyrics
                _hasLyrics = await _lyricsEngine.SearchAndLoadLyricsAsync(artist, title);
            }

            else
            {
                 // Just update play state prefix if lyrics logic doesn't override
                 if (!_hasLyrics)
                 {
                     Dispatcher.Invoke(() => UpdateTextWithScroll(text));
                 }
            }
        }

        private void MediaManager_PlaybackPositionChanged(object? sender, TimeSpan position)
        {
            // Sync anchor point
            _lastMediaPosition = position;
            _lastSyncTime = DateTime.Now;

            if (_hasLyrics)
            {
                Dispatcher.Invoke(() => UpdateLyricsUI(position));
            }
        }

        private void UpdateLyricsUI(TimeSpan position)
        {
            if (!_hasLyrics) return;

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
                    else
                    {
                        // Interpolate/Update Progress Only
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
                    }
                    UpdateSingleLineProgress(_mainLyricControl, current, adjustedPosition);
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

            // Calculate Progress (0 to 1)
            double progress = _lyricsEngine.GetLineProgress(line, time);
            
            // Apply to Gradient
            var brush = target.Foreground as LinearGradientBrush;
            if (brush != null && brush.GradientStops.Count >= 2)
            {
                // Smooth transition: we use two stops at the same offset to create a sharp edge,
                // or slightly apart for a slight glow/soft edge.
                brush.GradientStops[0].Offset = progress;
                brush.GradientStops[1].Offset = Math.Min(1.0, progress + 0.05); // Small buffer for softness
            }

            // Floating Window Sync
            if (_settings.EnableFloatingLyrics && _floatingWindow != null && target == _mainLyricControl)
            {
                _floatingWindow.UpdateLyrics(line.Text);
                _floatingWindow.UpdateProgress(progress);
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
        
        private void UpdateTextWithScroll(string text) => UpdateSingleLineScroll(_mainLyricControl, text, 0, !_settings.IsDoubleLine);

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