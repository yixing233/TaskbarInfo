using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color; // Resolve ambiguity
using ColorConverter = System.Windows.Media.ColorConverter;
using Button = System.Windows.Controls.Button;

namespace TaskbarInfo
{
    public partial class ColorPickerWindow : Window
    {
        public Color SelectedColor { get; private set; }
        private bool _isUpdating = false;

        public ColorPickerWindow(Color initialColor)
        {
            InitializeComponent();
            SelectedColor = initialColor;
            
            // Initialize Preset Colors
            var presets = new Color[] {
                Color.FromRgb(255, 255, 255), Color.FromRgb(0, 0, 0), 
                Color.FromRgb(244, 67, 54), Color.FromRgb(233, 30, 99),
                Color.FromRgb(156, 39, 176), Color.FromRgb(103, 58, 183),
                Color.FromRgb(63, 81, 181), Color.FromRgb(33, 150, 243),
                Color.FromRgb(0, 188, 212), Color.FromRgb(0, 150, 136),
                Color.FromRgb(76, 175, 80), Color.FromRgb(139, 195, 74),
                Color.FromRgb(255, 235, 59), Color.FromRgb(255, 193, 7),
                Color.FromRgb(255, 152, 0), Color.FromRgb(255, 87, 34),
                Color.FromRgb(121, 85, 72), Color.FromRgb(96, 125, 139)
            };

            foreach (var c in presets)
            {
                var btn = new Button
                {
                    Background = new SolidColorBrush(c),
                    Style = (Style)FindResource("ColorSwatchButtonStyle")
                };
                
                btn.Click += (s, e) => UpdateFromColor(c);
                PresetPanel.Children.Add(btn);
            }

            UpdateFromColor(initialColor);

             // Set Icon
             this.Icon = App.GetAppIcon();
        }

        private void UpdateFromColor(Color c)
        {
            _isUpdating = true;
            SelectedColor = c;
            
            SliderR.Value = c.R;
            SliderG.Value = c.G;
            SliderB.Value = c.B;
            SliderA.Value = c.A;
            
            ColorPreview.Fill = new SolidColorBrush(c);
            HexInput.Text = c.ToString();

            _isUpdating = false;
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdating) return;

            byte r = (byte)SliderR.Value;
            byte g = (byte)SliderG.Value;
            byte b = (byte)SliderB.Value;
            byte a = (byte)SliderA.Value;

            SelectedColor = Color.FromArgb(a, r, g, b);
            ColorPreview.Fill = new SolidColorBrush(SelectedColor);
            
            _isUpdating = true;
            HexInput.Text = SelectedColor.ToString();
            _isUpdating = false;
        }

        private void HexInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating) return;

            try
            {
                var text = HexInput.Text;
                if (!text.StartsWith("#")) text = "#" + text;
                
                var c = (Color)ColorConverter.ConvertFromString(text);
                
                _isUpdating = true; // Prevent loop back to hex input
                SliderR.Value = c.R;
                SliderG.Value = c.G;
                SliderB.Value = c.B;
                SliderA.Value = c.A;
                SelectedColor = c;
                ColorPreview.Fill = new SolidColorBrush(c);
                _isUpdating = false;
            }
            catch
            {
                // Invalid hex, ignore
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
