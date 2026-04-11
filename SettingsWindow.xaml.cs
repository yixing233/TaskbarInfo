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
        private bool _isUpdating = false;
        private List<string> _allFonts;

        private void OnFontSearch(object sender, TextChangedEventArgs e)
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
                    ComboFonts.IsDropDownOpen = true;
                    // ComboFonts.ScrollIntoView(match); // Method not found fix
                }
            }

            // Only update setting if it's a valid font
            if (_allFonts.Contains(filterText))
            {
                OnValueChanged();
            }
        }

        public SettingsWindow(AppSettings settings, Action previewCallback)
        {
            InitializeComponent();
            _settings = settings;
            _previewCallback = previewCallback;

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

            // Set current values
            _isUpdating = true;
            ComboFonts.Text = _settings.FontFamily;
            SliderSize.Value = _settings.FontSize;
            TextColorBox.Text = _settings.TextColor;
            BgColorBox.Text = _settings.BackgroundColor;
            CheckShadow.IsChecked = _settings.EnableShadow;
            CheckDoubleLine.IsChecked = _settings.IsDoubleLine;
            SliderWidth.Value = _settings.Width;
            SliderLyricOffset.Value = _settings.LyricOffsetSeconds;
            
            if (_settings.PositionMode == 1) RadioLeft.IsChecked = true;
            else RadioRight.IsChecked = true;
            
            SliderOffset.Value = _settings.OffsetX;

            SliderNextSizeDiff.Value = _settings.NextLyricFontSizeDiff;

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
            
            SliderSize.ValueChanged += (s, e) => OnValueChanged();
            SliderNextSizeDiff.ValueChanged += (s, e) => OnValueChanged(); // Add this
            ComboNextWeight.SelectionChanged += (s, e) => OnValueChanged(); // Add this
            TextColorBox.TextChanged += (s, e) => { UpdateColorPreview(); OnValueChanged(); };
            BgColorBox.TextChanged += (s, e) => { UpdateColorPreview(); OnValueChanged(); };
            CheckShadow.Checked += (s, e) => OnValueChanged();
            CheckShadow.Unchecked += (s, e) => OnValueChanged();
            CheckDoubleLine.Checked += (s, e) => OnValueChanged();
            CheckDoubleLine.Unchecked += (s, e) => OnValueChanged();
            SliderWidth.ValueChanged += (s, e) => OnValueChanged();
            SliderLyricOffset.ValueChanged += (s, e) => OnValueChanged();
            
            SliderOffset.ValueChanged += (s, e) => OnValueChanged();
            RadioRight.Checked += (s, e) => OnValueChanged();
            RadioLeft.Checked += (s, e) => OnValueChanged();

            // Set Icon
            this.Icon = App.GetAppIcon();
            
            // Ensure color preview updates after window is fully loaded
            this.Loaded += (s, e) => 
            {
                // Use Dispatcher to ensure UI is ready
                Dispatcher.BeginInvoke(new Action(() => 
                {
                    UpdateColorPreview();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            };
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
            _settings.EnableShadow = CheckShadow.IsChecked == true;
            _settings.EnableOutline = false; // Feature removed from UI
            _settings.Width = SliderWidth.Value;
            _settings.Width = SliderWidth.Value;
            _settings.IsDoubleLine = CheckDoubleLine.IsChecked == true;
            _settings.LyricOffsetSeconds = SliderLyricOffset.Value;
            
            _settings.PositionMode = (RadioLeft.IsChecked == true) ? 1 : 0;
            _settings.OffsetX = (int)SliderOffset.Value;
            
            _settings.NextLyricFontSizeDiff = SliderNextSizeDiff.Value;
            if (ComboNextWeight.SelectedItem is ComboBoxItem item)
            {
                _settings.NextLyricFontWeight = item.Tag?.ToString() ?? "Normal";
            }
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
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            UpdateSettingsObject();
            _settings.Save();
            
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
