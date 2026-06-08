using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color; // Resolve ambiguity
using ColorConverter = System.Windows.Media.ColorConverter;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;

namespace TaskbarInfo
{
    public partial class ColorPickerWindow : Window
    {
        public Color SelectedColor { get; private set; }
        private bool _isUpdating = false;

        // HSV state [H: 0-360, S: 0-1, V: 0-1]
        private double _h = 0;
        private double _s = 0;
        private double _v = 1;
        private double _a = 255;

        // Dragging states
        private bool _isDraggingCanvas = false;
        private bool _isDraggingHue = false;

        public ColorPickerWindow(Color initialColor)
        {
            InitializeComponent();
            SelectedColor = initialColor;

            // Load presets
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

            // Sync layout positions once window is loaded and actual sizes are available
            this.Loaded += (s, e) => 
            {
                UpdateFromColor(initialColor);
            };

            // Re-sync marker coordinates on canvas resize (dpi scaling, window size changes etc.)
            ColorCanvas.SizeChanged += (s, e) => 
            {
                UpdateMarkersFromHsv();
            };

            // Set Icon
            this.Icon = App.GetAppIcon();
        }

        private void UpdateFromColor(Color c)
        {
            _isUpdating = true;
            SelectedColor = c;
            _a = c.A;

            // RGB to HSV Conversion
            RgbToHsv(c.R, c.G, c.B, out _h, out _s, out _v);

            // Update sliders
            SliderR.Value = c.R;
            SliderG.Value = c.G;
            SliderB.Value = c.B;
            SliderA.Value = c.A;

            // Update Numeric TextBoxes
            TextR.Text = c.R.ToString();
            TextG.Text = c.G.ToString();
            TextB.Text = c.B.ToString();
            TextA.Text = c.A.ToString();

            // Update Canvas indicators and backgrounds
            UpdateMarkersFromHsv();

            ColorPreview.Fill = new SolidColorBrush(c);
            HexInput.Text = c.ToString();

            _isUpdating = false;
        }

        // --- Core Color Conversions ---
        private void RgbToHsv(byte R, byte G, byte B, out double h, out double s, out double v)
        {
            double r = R / 255.0;
            double g = G / 255.0;
            double b = B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            v = max;
            s = (max == 0) ? 0 : delta / max;

            if (delta == 0)
            {
                h = 0;
            }
            else
            {
                if (max == r)
                {
                    h = 60 * (((g - b) / delta) % 6);
                }
                else if (max == g)
                {
                    h = 60 * (((b - r) / delta) + 2);
                }
                else
                {
                    h = 60 * (((r - g) / delta) + 4);
                }

                if (h < 0) h += 360;
            }
        }

        private Color HsvToRgb(double h, double s, double v, byte alpha)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs(((h / 60.0) % 2) - 1));
            double m = v - c;

            double r1 = 0, g1 = 0, b1 = 0;

            if (h >= 0 && h < 60)
            {
                r1 = c; g1 = x; b1 = 0;
            }
            else if (h >= 60 && h < 120)
            {
                r1 = x; g1 = c; b1 = 0;
            }
            else if (h >= 120 && h < 180)
            {
                r1 = 0; g1 = c; b1 = x;
            }
            else if (h >= 180 && h < 240)
            {
                r1 = 0; g1 = x; b1 = c;
            }
            else if (h >= 240 && h < 300)
            {
                r1 = x; g1 = 0; b1 = c;
            }
            else if (h >= 300 && h <= 360)
            {
                r1 = c; g1 = 0; b1 = x;
            }

            byte r = (byte)Math.Round((r1 + m) * 255.0);
            byte g = (byte)Math.Round((g1 + m) * 255.0);
            byte b = (byte)Math.Round((b1 + m) * 255.0);

            return Color.FromArgb(alpha, r, g, b);
        }

        // --- UI Marker Position Synchronization ---
        private void UpdateMarkersFromHsv()
        {
            if (ColorCanvas.ActualWidth <= 0 || ColorCanvas.ActualHeight <= 0) return;

            // 1. SB Canvas Pointer position
            double x = _s * ColorCanvas.ActualWidth;
            double y = (1.0 - _v) * ColorCanvas.ActualHeight;

            Canvas.SetLeft(ColorMarker, x);
            Canvas.SetTop(ColorMarker, y);

            // 2. Hue Slider pointer position
            double hY = (_h / 360.0) * HueSlider.ActualHeight;
            Canvas.SetLeft(HueMarker, 2);
            Canvas.SetTop(HueMarker, hY);

            // 3. Update the Solid base background of SB Canvas Grid with clean Color
            Color pureHueColor = HsvToRgb(_h, 1.0, 1.0, 255);
            HueBaseBorder.Background = new SolidColorBrush(pureHueColor);
        }

        // --- Custom Pick Area Mouse Events ---

        // Saturation/Brightness Dragging
        private void ColorCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isDraggingCanvas = true;
            ColorCanvas.CaptureMouse();
            UpdateColorFromCanvasMouse(e.GetPosition(ColorCanvas));
            e.Handled = true;
        }

        private void ColorCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isDraggingCanvas) return;
            UpdateColorFromCanvasMouse(e.GetPosition(ColorCanvas));
            e.Handled = true;
        }

        private void ColorCanvas_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isDraggingCanvas)
            {
                ColorCanvas.ReleaseMouseCapture();
                _isDraggingCanvas = false;
                e.Handled = true;
            }
        }

        private void UpdateColorFromCanvasMouse(Point p)
        {
            double w = ColorCanvas.ActualWidth;
            double h = ColorCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            // Bounds protection
            double x = Math.Max(0, Math.Min(p.X, w));
            double y = Math.Max(0, Math.Min(p.Y, h));

            _s = x / w;
            _v = 1.0 - (y / h);

            SyncHsvToRgbControls();
        }

        // Hue Slider Dragging
        private void HueSlider_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isDraggingHue = true;
            HueSlider.CaptureMouse();
            UpdateColorFromHueMouse(e.GetPosition(HueSlider));
            e.Handled = true;
        }

        private void HueSlider_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isDraggingHue) return;
            UpdateColorFromHueMouse(e.GetPosition(HueSlider));
            e.Handled = true;
        }

        private void HueSlider_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isDraggingHue)
            {
                HueSlider.ReleaseMouseCapture();
                _isDraggingHue = false;
                e.Handled = true;
            }
        }

        private void UpdateColorFromHueMouse(Point p)
        {
            double h = HueSlider.ActualHeight;
            if (h <= 0) return;

            double y = Math.Max(0, Math.Min(p.Y, h));
            _h = (y / h) * 360.0;
            if (_h > 360.0) _h = 360.0;

            SyncHsvToRgbControls();
        }

        // Synchronize HSV changes to RGB sliders, boxes and previews
        private void SyncHsvToRgbControls()
        {
            _isUpdating = true;

            Color newColor = HsvToRgb(_h, _s, _v, (byte)_a);
            SelectedColor = newColor;

            // Sliders
            SliderR.Value = newColor.R;
            SliderG.Value = newColor.G;
            SliderB.Value = newColor.B;
            SliderA.Value = newColor.A;

            // TextBoxes
            TextR.Text = newColor.R.ToString();
            TextG.Text = newColor.G.ToString();
            TextB.Text = newColor.B.ToString();
            TextA.Text = newColor.A.ToString();

            // Preview and Hex
            ColorPreview.Fill = new SolidColorBrush(newColor);
            HexInput.Text = newColor.ToString();

            UpdateMarkersFromHsv();

            _isUpdating = false;
        }

        // --- Horizontal Sliders Value Changed ---
        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdating) return;

            byte r = (byte)SliderR.Value;
            byte g = (byte)SliderG.Value;
            byte b = (byte)SliderB.Value;
            byte a = (byte)SliderA.Value;

            SelectedColor = Color.FromArgb(a, r, g, b);
            
            _isUpdating = true;
            
            // Sync Numbers
            TextR.Text = r.ToString();
            TextG.Text = g.ToString();
            TextB.Text = b.ToString();
            TextA.Text = a.ToString();

            _a = a;
            RgbToHsv(r, g, b, out _h, out _s, out _v);

            ColorPreview.Fill = new SolidColorBrush(SelectedColor);
            HexInput.Text = SelectedColor.ToString();
            UpdateMarkersFromHsv();

            _isUpdating = false;
        }

        // --- Manual Channel Number TextBoxes Input ---
        private void TextChannel_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating) return;

            var tb = sender as System.Windows.Controls.TextBox;
            if (tb == null) return;

            if (byte.TryParse(tb.Text, out byte val))
            {
                _isUpdating = true;

                // Sync corresponding slider
                if (tb == TextR) SliderR.Value = val;
                else if (tb == TextG) SliderG.Value = val;
                else if (tb == TextB) SliderB.Value = val;
                else if (tb == TextA) SliderA.Value = val;

                byte r = (byte)SliderR.Value;
                byte g = (byte)SliderG.Value;
                byte b = (byte)SliderB.Value;
                byte a = (byte)SliderA.Value;

                SelectedColor = Color.FromArgb(a, r, g, b);
                _a = a;
                RgbToHsv(r, g, b, out _h, out _s, out _v);

                ColorPreview.Fill = new SolidColorBrush(SelectedColor);
                HexInput.Text = SelectedColor.ToString();
                UpdateMarkersFromHsv();

                _isUpdating = false;
            }
        }

        // --- Hex TextBox Input ---
        private void HexInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating) return;

            try
            {
                var text = HexInput.Text;
                if (!text.StartsWith("#")) text = "#" + text;
                
                var c = (Color)ColorConverter.ConvertFromString(text);
                
                _isUpdating = true;
                
                SliderR.Value = c.R;
                SliderG.Value = c.G;
                SliderB.Value = c.B;
                SliderA.Value = c.A;

                TextR.Text = c.R.ToString();
                TextG.Text = c.G.ToString();
                TextB.Text = c.B.ToString();
                TextA.Text = c.A.ToString();

                SelectedColor = c;
                _a = c.A;
                RgbToHsv(c.R, c.G, c.B, out _h, out _s, out _v);

                ColorPreview.Fill = new SolidColorBrush(c);
                UpdateMarkersFromHsv();

                _isUpdating = false;
            }
            catch
            {
                // Invalid color, ignore
            }
        }

        // --- Dialog Actions ---
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
