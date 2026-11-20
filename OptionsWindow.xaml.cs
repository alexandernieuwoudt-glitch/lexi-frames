using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace LexiFrames
{
    public partial class OptionsWindow : Window
    {
        private readonly OverlaySettings _original;
        private OverlaySettings _s;
        private readonly Action<OverlaySettings> _apply;
        private bool _loading;
        private bool _saved;

        private readonly SensorHistory _history;
        private readonly DispatcherTimer _chartTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

        // Validated categorical colours (dark surface): CPU = blue, GPU = orange, FPS = violet
        private static readonly Color CpuColor = Color.FromRgb(0x39, 0x87, 0xE5);
        private static readonly Color GpuColor = Color.FromRgb(0xD9, 0x59, 0x26);
        private static readonly Color FpsColor = Color.FromRgb(0x90, 0x85, 0xE9);

        public OptionsWindow(OverlaySettings current, Action<OverlaySettings> apply, SensorHistory history, Action previewEffects = null)
        {
            InitializeComponent();
            _original = current.Clone();
            _s = current.Clone();
            _apply = apply;
            _history = history;
            PreviewFxBtn.Click += (s, e) => previewEffects?.Invoke();
            // Keep the window on screen; the Appearance tab scrolls if needed
            MaxHeight = SystemParameters.WorkArea.Height - 20;
            AppearanceScroll.MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 190);

            foreach (var f in Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n))
                FontBox.Items.Add(f);

            LoadControls();
            Wire();
            SetupCharts();
        }

        private void SetupCharts()
        {
            UsageChart.Title = "Usage";
            UsageChart.Unit = "%";
            UsageChart.FixedMax = 100;
            UsageChart.Series.Add(new ChartSeries { Name = "CPU", Color = CpuColor, Value = d => d.CpuLoad });
            UsageChart.Series.Add(new ChartSeries { Name = "GPU", Color = GpuColor, Value = d => d.GpuLoad });

            TempChart.Title = "Temperature";
            TempChart.Unit = "°C";
            TempChart.Series.Add(new ChartSeries { Name = "CPU", Color = CpuColor, Value = d => d.CpuTemp });
            TempChart.Series.Add(new ChartSeries { Name = "GPU", Color = GpuColor, Value = d => d.GpuTemp });

            FpsChart.Title = "FPS (app in front)";
            FpsChart.Series.Add(new ChartSeries { Name = "FPS", Color = FpsColor, Value = d => d.Fps });

            RangeBox.SelectionChanged += (s, e) => RefreshCharts();
            _chartTimer.Tick += (s, e) => { if (Tabs.SelectedIndex == 1) RefreshCharts(); };
            Tabs.SelectionChanged += (s, e) => { if (e.Source == Tabs) RefreshCharts(); };
            _chartTimer.Start();
            Closed += (s, e) => _chartTimer.Stop();
        }

        private void RefreshCharts()
        {
            if (UsageChart == null || _history == null) return;
            double range = 300;
            if (RangeBox.SelectedItem is ComboBoxItem item && double.TryParse(item.Tag as string, out double r)) range = r;

            var data = _history.Snapshot();
            foreach (var chart in new[] { UsageChart, TempChart, FpsChart })
            {
                chart.RangeSeconds = range;
                chart.SetData(data);
            }
        }

        private void LoadControls()
        {
            _loading = true;
            FontBox.SelectedItem = FontBox.Items.Contains(_s.FontFamily) ? _s.FontFamily : "Consolas";
            SizeSlider.Value = _s.FontSize;
            OpacitySlider.Value = Math.Round(_s.Opacity * 100);
            BoldBox.IsChecked = _s.Bold;
            ShadowBox.IsChecked = _s.TextShadow;
            ColorCodeBox.IsChecked = _s.ColorCoding;
            BgBox.IsChecked = _s.ShowBackground;
            BgOpacitySlider.Value = Math.Round(_s.BackgroundOpacity * 100);
            EffectsBox.IsChecked = _s.DynamicEffects;
            XSlider.Value = _s.PosX;
            YSlider.Value = _s.PosY;
            LayoutBox.SelectedItem = LayoutBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _s.Layout)
                                     ?? LayoutBox.Items[0];
            FpsBox.IsChecked = _s.ShowFps;
            CpuBox.IsChecked = _s.ShowCpu;
            GpuBox.IsChecked = _s.ShowGpu;
            TempsBox.IsChecked = _s.ShowTemps;
            UpdateSwatches();
            BgPanel.IsEnabled = _s.ShowBackground;
            _loading = false;
        }

        private void Wire()
        {
            FontBox.SelectionChanged += (s, e) => Changed();
            LayoutBox.SelectionChanged += (s, e) => Changed();
            foreach (var sl in new[] { SizeSlider, OpacitySlider, BgOpacitySlider, XSlider, YSlider })
                sl.ValueChanged += (s, e) => Changed();
            foreach (var cb in new[] { BoldBox, ShadowBox, ColorCodeBox, BgBox, FpsBox, CpuBox, GpuBox, TempsBox, EffectsBox })
            {
                cb.Checked += (s, e) => Changed();
                cb.Unchecked += (s, e) => Changed();
            }

            LabelColorBtn.Click += (s, e) => PickColor(c => _s.LabelColor = c, _s.LabelColor);
            ValueColorBtn.Click += (s, e) => PickColor(c => _s.ValueColor = c, _s.ValueColor);
            BgColorBtn.Click += (s, e) => PickColor(c => _s.BackgroundColor = c, _s.BackgroundColor);

            ResetBtn.Click += (s, e) => { _s = new OverlaySettings(); LoadControls(); _apply(_s); };
            CloseBtn.Click += (s, e) => { _s.Save(); _saved = true; Close(); };
            CancelBtn.Click += (s, e) => Close();
            Closed += (s, e) => { if (!_saved) _apply(_original); };
        }

        private void Changed()
        {
            if (_loading) return;
            _s.FontFamily = FontBox.SelectedItem as string ?? "Consolas";
            _s.FontSize = SizeSlider.Value;
            _s.Opacity = OpacitySlider.Value / 100.0;
            _s.Bold = BoldBox.IsChecked == true;
            _s.TextShadow = ShadowBox.IsChecked == true;
            _s.ColorCoding = ColorCodeBox.IsChecked == true;
            _s.ShowBackground = BgBox.IsChecked == true;
            _s.BackgroundOpacity = BgOpacitySlider.Value / 100.0;
            _s.DynamicEffects = EffectsBox.IsChecked == true;
            _s.PosX = XSlider.Value;
            _s.PosY = YSlider.Value;
            _s.Layout = (LayoutBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Vertical";
            _s.ShowFps = FpsBox.IsChecked == true;
            _s.ShowCpu = CpuBox.IsChecked == true;
            _s.ShowGpu = GpuBox.IsChecked == true;
            _s.ShowTemps = TempsBox.IsChecked == true;
            BgPanel.IsEnabled = _s.ShowBackground;
            _apply(_s);
        }

        private void Snap_Click(object sender, RoutedEventArgs e)
        {
            var parts = ((string)((Button)sender).Tag).Split(',');
            _loading = true;
            XSlider.Value = double.Parse(parts[0]);
            YSlider.Value = double.Parse(parts[1]);
            _loading = false;
            Changed();
        }

        private void PickColor(Action<string> set, string current)
        {
            var c = (Color)ColorConverter.ConvertFromString(current);
            using (var dlg = new Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B) })
            {
                if (dlg.ShowDialog() != Forms.DialogResult.OK) return;
                set($"#FF{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}");
            }
            UpdateSwatches();
            _apply(_s);
        }

        private void UpdateSwatches()
        {
            LabelColorBtn.Background = Brush(_s.LabelColor);
            ValueColorBtn.Background = Brush(_s.ValueColor);
            BgColorBtn.Background = Brush(_s.BackgroundColor);
        }

        private static SolidColorBrush Brush(string hex) =>
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }
}
