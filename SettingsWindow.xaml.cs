using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace TaskbarInfo
{
    public partial class SettingsWindow : Window
    {
        private AppSettings _settings;
        private Action _previewCallback;
        private MediaManager _mediaManager;
        private bool _isUpdating = false;
        private List<string> _allFonts;

        public class AppItem
        {
            public string AppId { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public bool IsSelected { get; set; }
        }

        private void OnFontSearch(object sender, TextChangedEventArgs e)
        {
            HandleFontSearch(ComboFonts, e);
        }

        private void OnFloatingFontSearch(object sender, TextChangedEventArgs e)
        {
            HandleFontSearch(ComboFloatingFonts, e);
        }

        private void HandleFontSearch(System.Windows.Controls.ComboBox comboBox, TextChangedEventArgs e)
        {
            if (_isUpdating) return;
            
            var textBox = e.OriginalSource as System.Windows.Controls.TextBox;
            if (textBox == null) return;
            
            // Prevent handling when not user-initiated (e.g. init)
            if (!textBox.IsKeyboardFocused) return;

            string filterText = textBox.Text;
            
            // Scroll to the first match instead of filtering
            if (!string.IsNullOrWhiteSpace(filterText))
            {
                var match = _allFonts.FirstOrDefault(f => f.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null)
                {
                    // Do NOT set SelectedItem to avoid overwriting user input
                    comboBox.IsDropDownOpen = true;
                    // comboBox.ScrollIntoView(match); // Method not found fix
                }
            }

            // Only update setting if it's a valid font
            if (_allFonts.Contains(filterText))
            {
                OnValueChanged();
            }
        }

        public SettingsWindow(AppSettings settings, Action previewCallback, MediaManager mediaManager, int initialNavIndex = 0)
        {
            InitializeComponent();
            _settings = settings;
            _previewCallback = previewCallback;
            _mediaManager = mediaManager;
            NavMenu.SelectedIndex = initialNavIndex;

            // Load Fonts with Chinese names support
            _allFonts = new List<string>();
            var zhLang = System.Windows.Markup.XmlLanguage.GetLanguage("zh-cn");
            
            foreach (var fontFamily in Fonts.SystemFontFamilies)
            {
                string name = fontFamily.Source;
                if (fontFamily.FamilyNames.ContainsKey(zhLang))
                {
                    name = fontFamily.FamilyNames[zhLang];
                }
                _allFonts.Add(name);
            }
            _allFonts.Sort();
            ComboFonts.ItemsSource = _allFonts;
            ComboFloatingFonts.ItemsSource = _allFonts;

            // Set current values
            _isUpdating = true;
            ComboFonts.Text = _settings.FontFamily;
            SliderSize.Value = _settings.FontSize;
            TextColorBox.Text = _settings.TextColor;
            BgColorBox.Text = _settings.BackgroundColor;
            CheckShadow.IsChecked = _settings.EnableShadow;
            CheckDoubleLine.IsChecked = _settings.IsDoubleLine;
            SliderWidth.Value = _settings.Width;
            CheckAutoUpdate.IsChecked = _settings.AutoCheckUpdates;
            TxtLyricOffset.Text = _settings.LyricOffsetSeconds.ToString("F1");
            CheckRunOnly.IsChecked = _settings.RunOnlyWithMusicApp;
            LoadApps();

            SliderNextSizeDiff.Value = _settings.NextLyricFontSizeDiff;
            ComboFloatingFonts.Text = _settings.FloatingLyricsFontFamily;
            SliderFloatingSize.Value = _settings.FloatingLyricsFontSize;
            FloatingBgColorBox.Text = _settings.FloatingLyricsBackgroundColor;
            CheckFloatingShadow.IsChecked = _settings.FloatingLyricsEnableShadow;
            CheckFloatingAcrylic.IsChecked = _settings.FloatingLyricsUseAcrylic;
            CheckDesktopWidget.IsChecked = _settings.EnableDesktopWidget;
            DesktopWidgetDarkThemeRadio.IsChecked = _settings.DesktopWidgetTheme == DesktopWidgetTheme.Dark;
            DesktopWidgetLightThemeRadio.IsChecked = _settings.DesktopWidgetTheme == DesktopWidgetTheme.Light;
            CheckDesktopWidgetLocked.IsChecked = _settings.DesktopWidgetLocked;

            // Initialize main font weight selector
            foreach (ComboBoxItem item in ComboFontWeight.Items)
            {
                if (item.Tag?.ToString() == _settings.FontWeight)
                {
                    ComboFontWeight.SelectedItem = item;
                    break;
                }
            }
            if (ComboFontWeight.SelectedItem == null)
            {
               foreach (ComboBoxItem item in ComboFontWeight.Items)
               {
                   if (item.Tag?.ToString() == "SemiBold") 
                   {
                       ComboFontWeight.SelectedItem = item;
                       break;
                   }
               }
            }

            // Initialize floating lyrics font weight selector
            foreach (ComboBoxItem item in ComboFloatingWeight.Items)
            {
                if (item.Tag?.ToString() == _settings.FloatingLyricsFontWeight)
                {
                    ComboFloatingWeight.SelectedItem = item;
                    break;
                }
            }
            if (ComboFloatingWeight.SelectedItem == null)
            {
               foreach (ComboBoxItem item in ComboFloatingWeight.Items)
               {
                   if (item.Tag?.ToString() == "Bold") 
                   {
                       ComboFloatingWeight.SelectedItem = item;
                       break;
                   }
               }
            }

            // Initialize next font weight selector
            foreach (ComboBoxItem item in ComboNextWeight.Items)
            {
                if (item.Tag?.ToString() == _settings.NextLyricFontWeight)
                {
                    ComboNextWeight.SelectedItem = item;
                    break;
                }
            }
            // Fallback if not found (e.g. settings file has old value or empty)
            if (ComboNextWeight.SelectedItem == null)
            {
               foreach (ComboBoxItem item in ComboNextWeight.Items)
               {
                   if (item.Tag?.ToString() == "Normal") 
                   {
                       ComboNextWeight.SelectedItem = item;
                       break;
                   }
               }
            }

            _isUpdating = false;
            
            VersionText.Text = $"当前版本 {UpdateService.CurrentVersionDisplay}";
            UpdateColorPreview();

            // Attach Events
            ComboFonts.SelectionChanged += (s, e) => {
                if (ComboFonts.SelectedItem != null)
                {
                    OnValueChanged();
                }
            };
            
            // Handle Font Search/Filter
            ComboFonts.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, 
                new TextChangedEventHandler(OnFontSearch));
            ComboFloatingFonts.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler(OnFloatingFontSearch));
            
            // Auto-open dropdown on focus, unless clicking the key/toggle
            ComboFonts.GotFocus += (s, e) => { 
                try 
                {
                    var element = System.Windows.Input.Mouse.DirectlyOver as DependencyObject;
                    while (element != null && element != ComboFonts)
                    {
                        if (element is System.Windows.Controls.Primitives.ToggleButton) return;
                        element = VisualTreeHelper.GetParent(element);
                    }
                    if (!ComboFonts.IsDropDownOpen) ComboFonts.IsDropDownOpen = true; 
                }
                catch { /* Ignore visual tree errors */ }
            };
            ComboFloatingFonts.GotFocus += (s, e) => { 
                try 
                {
                    var element = System.Windows.Input.Mouse.DirectlyOver as DependencyObject;
                    while (element != null && element != ComboFloatingFonts)
                    {
                        if (element is System.Windows.Controls.Primitives.ToggleButton) return;
                        element = VisualTreeHelper.GetParent(element);
                    }
                    if (!ComboFloatingFonts.IsDropDownOpen) ComboFloatingFonts.IsDropDownOpen = true; 
                }
                catch { /* Ignore visual tree errors */ }
            };
            
            SliderSize.ValueChanged += (s, e) => OnValueChanged();
            ComboFontWeight.SelectionChanged += (s, e) => OnValueChanged(); // Main font weight
            SliderNextSizeDiff.ValueChanged += (s, e) => OnValueChanged(); 
            ComboNextWeight.SelectionChanged += (s, e) => OnValueChanged(); 
            TextColorBox.TextChanged += (s, e) => { UpdateColorPreview(); UpdateValidationState(); OnValueChanged(); };
            BgColorBox.TextChanged += (s, e) => { UpdateColorPreview(); UpdateValidationState(); OnValueChanged(); };
            ComboFloatingFonts.SelectionChanged += (s, e) => {
                if (ComboFloatingFonts.SelectedItem != null)
                {
                    OnValueChanged();
                }
            };
            SliderFloatingSize.ValueChanged += (s, e) => OnValueChanged();
            ComboFloatingWeight.SelectionChanged += (s, e) => OnValueChanged();
            FloatingBgColorBox.TextChanged += (s, e) => { UpdateColorPreview(); UpdateValidationState(); OnValueChanged(); };
            CheckFloatingShadow.Checked += (s, e) => OnValueChanged();
            CheckFloatingShadow.Unchecked += (s, e) => OnValueChanged();
            CheckFloatingAcrylic.Checked += (s, e) => OnValueChanged();
            CheckFloatingAcrylic.Unchecked += (s, e) => OnValueChanged();
            CheckShadow.Checked += (s, e) => OnValueChanged();
            CheckShadow.Unchecked += (s, e) => OnValueChanged();
            CheckDoubleLine.Checked += (s, e) => OnValueChanged();
            CheckDoubleLine.Unchecked += (s, e) => OnValueChanged();
            SliderWidth.ValueChanged += (s, e) => OnValueChanged();
            CheckAutoUpdate.Checked += (s, e) => OnValueChanged();
            CheckAutoUpdate.Unchecked += (s, e) => OnValueChanged();
            CheckDesktopWidget.Checked += (s, e) => OnValueChanged();
            CheckDesktopWidget.Unchecked += (s, e) => OnValueChanged();
            CheckDesktopWidgetLocked.Checked += (s, e) => OnValueChanged();
            CheckDesktopWidgetLocked.Unchecked += (s, e) => OnValueChanged();
            DesktopWidgetDarkThemeRadio.Checked += (s, e) => OnValueChanged();
            DesktopWidgetLightThemeRadio.Checked += (s, e) => OnValueChanged();

            TxtLyricOffset.TextChanged += (s, e) => {
                if (_isUpdating) return;
                if (double.TryParse(TxtLyricOffset.Text, out double val))
                {
                    if (val < -10.0) val = -10.0;
                    if (val > 10.0) val = 10.0;
                    _settings.LyricOffsetSeconds = val;
                    OnValueChanged();
                }
            };
            // Set Icon
            this.Icon = App.GetAppIcon();
            
            // Ensure color preview updates after window is fully loaded
            this.Loaded += (s, e) => 
            {
                // Use Dispatcher to ensure UI is ready
                Dispatcher.BeginInvoke(new Action(() => 
                {
                    UpdateColorPreview();
                    UpdateValidationState();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            };
        }

        private void BtnDecOffset_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(TxtLyricOffset.Text, out double val))
            {
                val -= 0.1;
                if (val < -10.0) val = -10.0;
                TxtLyricOffset.Text = val.ToString("F1");
            }
            else
            {
                TxtLyricOffset.Text = "0.0";
            }
        }

        private void BtnIncOffset_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(TxtLyricOffset.Text, out double val))
            {
                val += 0.1;
                if (val > 10.0) val = 10.0;
                TxtLyricOffset.Text = val.ToString("F1");
            }
            else
            {
                TxtLyricOffset.Text = "0.0";
            }
        }

        private void PickColor_Click(object sender, RoutedEventArgs e)
        {
            Color initialColor = Colors.White; // Default
            try 
            {
                 initialColor = (Color)ColorConverter.ConvertFromString(TextColorBox.Text);
            }
            catch {}

            var picker = new ColorPickerWindow(initialColor);
            picker.Owner = this; // Center over settings window
            
            if (picker.ShowDialog() == true)
            {
                var c = picker.SelectedColor;
                TextColorBox.Text = c.ToString();
            }
        }

        private void LoadApps()
        {
            var savedIds = new HashSet<string>(_settings.IncludedAppIds);
            var runningIds = _mediaManager.GetCurrentSourceIds();
            var allIds = new HashSet<string>(savedIds);

            foreach (var id in runningIds)
            {
                allIds.Add(id);
            }

            var list = allIds
                .Select(id => new AppItem
                {
                    AppId = id,
                    DisplayName = id.Contains("!") ? id.Split('!')[0] : id.Replace(".exe", ""),
                    IsSelected = savedIds.Contains(id)
                })
                .OrderBy(item => item.DisplayName)
                .ToList();

            AppList.ItemsSource = list;
        }

        private void RefreshApps_Click(object sender, RoutedEventArgs e)
        {
            LoadApps();
        }

        private void PickBgColor_Click(object sender, RoutedEventArgs e)
        {
            Color initialColor = Colors.Transparent; // Default
            try 
            {
                 initialColor = (Color)ColorConverter.ConvertFromString(BgColorBox.Text);
            }
            catch {}

            var picker = new ColorPickerWindow(initialColor);
            picker.Owner = this; // Center over settings window
            
            if (picker.ShowDialog() == true)
            {
                var c = picker.SelectedColor;
                BgColorBox.Text = c.ToString();
            }
        }

        private void PickFloatingBgColor_Click(object sender, RoutedEventArgs e)
        {
            Color initialColor = Colors.White;
            try 
            {
                 initialColor = (Color)ColorConverter.ConvertFromString(FloatingBgColorBox.Text);
            }
            catch {}

            var picker = new ColorPickerWindow(initialColor);
            picker.Owner = this;
            
            if (picker.ShowDialog() == true)
            {
                var c = picker.SelectedColor;
                FloatingBgColorBox.Text = c.ToString();
            }
        }

        private void ResetDesktopWidgetPosition_Click(object sender, RoutedEventArgs e)
        {
            _settings.DesktopWidgetLeft = 48;
            _settings.DesktopWidgetTop = 48;
            _settings.DesktopWidgetMonitorDeviceName = "";
            _settings.DesktopWidgetMonitorOffsetX = null;
            _settings.DesktopWidgetMonitorOffsetY = null;
            _previewCallback?.Invoke();
        }

        private void OnValueChanged()
        {
            if (_isUpdating) return;
            
            UpdateSettingsObject();
            _previewCallback?.Invoke();
        }

        private void UpdateSettingsObject()
        {
            _settings.FontFamily = ComboFonts.Text; // Use Text for editable combo
            _settings.FontSize = SliderSize.Value;
            _settings.TextColor = TextColorBox.Text;
            _settings.BackgroundColor = BgColorBox.Text;
            _settings.FloatingLyricsFontFamily = ComboFloatingFonts.Text;
            _settings.FloatingLyricsFontSize = SliderFloatingSize.Value;
            _settings.FloatingLyricsBackgroundColor = FloatingBgColorBox.Text;
            _settings.FloatingLyricsEnableShadow = CheckFloatingShadow.IsChecked == true;
            _settings.FloatingLyricsUseAcrylic = CheckFloatingAcrylic.IsChecked == true;
            _settings.EnableDesktopWidget = CheckDesktopWidget.IsChecked == true;
            _settings.DesktopWidgetTheme = DesktopWidgetLightThemeRadio.IsChecked == true
                ? DesktopWidgetTheme.Light
                : DesktopWidgetTheme.Dark;
            _settings.DesktopWidgetLocked = CheckDesktopWidgetLocked.IsChecked == true;
            _settings.EnableShadow = CheckShadow.IsChecked == true;
            _settings.EnableOutline = false; // Feature removed from UI
            _settings.Width = SliderWidth.Value;
            _settings.IsDoubleLine = CheckDoubleLine.IsChecked == true;
            _settings.AutoCheckUpdates = CheckAutoUpdate.IsChecked == true;
            if (double.TryParse(TxtLyricOffset.Text, out double offsetVal))
            {
                if (offsetVal < -10.0) offsetVal = -10.0;
                if (offsetVal > 10.0) offsetVal = 10.0;
                _settings.LyricOffsetSeconds = offsetVal;
            }
            UpdateAppFilterSettings();
            
            if (ComboFontWeight.SelectedItem is ComboBoxItem mainWeightItem)
            {
                _settings.FontWeight = mainWeightItem.Tag?.ToString() ?? "SemiBold";
            }

            if (ComboFloatingWeight.SelectedItem is ComboBoxItem floatingWeightItem)
            {
                _settings.FloatingLyricsFontWeight = floatingWeightItem.Tag?.ToString() ?? "Bold";
            }

            _settings.NextLyricFontSizeDiff = SliderNextSizeDiff.Value;
            if (ComboNextWeight.SelectedItem is ComboBoxItem item)
            {
                _settings.NextLyricFontWeight = item.Tag?.ToString() ?? "Normal";
            }
        }

        private void UpdateAppFilterSettings()
        {
            if (AppList.ItemsSource is not List<AppItem> list) return;

            _settings.IncludedAppIds.Clear();
            var processNames = new List<string>();

            foreach (var item in list)
            {
                if (!item.IsSelected) continue;

                _settings.IncludedAppIds.Add(item.AppId);
                processNames.Add(item.DisplayName);
            }

            _settings.RunOnlyWithMusicApp = CheckRunOnly.IsChecked == true;
            _settings.MusicAppProcessNames = string.Join(",", processNames);
        }

        private void UpdateColorPreview()
        {
            // Ensure controls are initialized
            if (ColorPreview == null || TextColorBox == null) return;
            
            try
            {
                var colorText = TextColorBox.Text;
                if (string.IsNullOrWhiteSpace(colorText))
                {
                    ColorPreview.Background = new SolidColorBrush(Colors.White);
                }
                else 
                {
                    var color = (Color)ColorConverter.ConvertFromString(colorText);
                    ColorPreview.Background = new SolidColorBrush(color);
                }
            }
            catch
            {
                // Invalid color - use white as fallback
                ColorPreview.Background = new SolidColorBrush(Colors.White);
            }

            if (BgColorPreview == null || BgColorBox == null) return;

            try
            {
                var colorText = BgColorBox.Text;
                if (string.IsNullOrWhiteSpace(colorText))
                {
                    BgColorPreview.Background = new SolidColorBrush(Colors.White);
                }
                else 
                {
                    var color = (Color)ColorConverter.ConvertFromString(colorText);
                    BgColorPreview.Background = new SolidColorBrush(color);
                }
            }
            catch
            {
                BgColorPreview.Background = new SolidColorBrush(Colors.White);
            }

            if (FloatingBgColorPreview == null || FloatingBgColorBox == null) return;

            try
            {
                var colorText = FloatingBgColorBox.Text;
                if (string.IsNullOrWhiteSpace(colorText))
                {
                    FloatingBgColorPreview.Background = new SolidColorBrush(Colors.White);
                }
                else
                {
                    var color = (Color)ColorConverter.ConvertFromString(colorText);
                    FloatingBgColorPreview.Background = new SolidColorBrush(color);
                }
            }
            catch
            {
                FloatingBgColorPreview.Background = new SolidColorBrush(Colors.White);
            }

            UpdateDesktopWidgetThemePreview();
        }

        private void UpdateDesktopWidgetThemePreview()
        {
            if (DesktopWidgetPreviewCard == null) return;

            var palette = DesktopWidgetThemePalette.Get(_settings.DesktopWidgetTheme);
            DesktopWidgetPreviewCard.Background = CreateBrush(palette.CardBackground);
            DesktopWidgetPreviewCard.BorderBrush = CreateBrush(palette.CardBorder);
            DesktopWidgetPreviewTitle.Foreground = CreateBrush(palette.PrimaryText);
            DesktopWidgetPreviewArtist.Foreground = CreateBrush(palette.SecondaryText);
            DesktopWidgetPreviewLyric.Foreground = CreateBrush(palette.LyricText);
            DesktopWidgetPreviewProgressTrack.Background = CreateBrush(palette.ProgressTrack);
            DesktopWidgetPreviewProgressFill.Background = CreateBrush(palette.Accent);
        }

        private static SolidColorBrush CreateBrush(string color)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }

        private bool UpdateValidationState()
        {
            var invalidFields = new List<string>();

            SetColorFieldState(TextColorBox, IsValidColor(TextColorBox.Text), "文本颜色", invalidFields);
            SetColorFieldState(BgColorBox, IsValidColor(BgColorBox.Text), "背景颜色", invalidFields);
            SetColorFieldState(FloatingBgColorBox, IsValidColor(FloatingBgColorBox.Text), "悬浮背景颜色", invalidFields);

            bool isValid = invalidFields.Count == 0;
            BtnSave.IsEnabled = isValid;
            SettingsStatusText.Text = isValid ? "" : "颜色格式无效: " + string.Join("、", invalidFields);
            return isValid;
        }

        private static bool IsValidColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            try
            {
                ColorConverter.ConvertFromString(value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void SetColorFieldState(System.Windows.Controls.TextBox textBox, bool isValid, string displayName, List<string> invalidFields)
        {
            if (isValid)
            {
                textBox.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
                textBox.ToolTip = null;
                return;
            }

            textBox.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
            textBox.ToolTip = "请输入 #RRGGBB、#AARRGGBB 或系统颜色名称";
            invalidFields.Add(displayName);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!UpdateValidationState())
            {
                return;
            }

            UpdateSettingsObject();
            if (!_settings.Save(out var errorMessage))
            {
                SettingsStatusText.Text = "保存失败: " + errorMessage;
                System.Windows.MessageBox.Show(this, "设置保存失败:\n" + errorMessage, "LyricsX", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            this.DialogResult = true;
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
