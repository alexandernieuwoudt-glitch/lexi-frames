using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Forms = System.Windows.Forms;

namespace LexiFrames
{
    public partial class MainWindow : Window
    {
        private readonly FpsMonitor _fps = new FpsMonitor();
        private readonly HardwareMonitor _hw = new HardwareMonitor();
        private readonly int _ownPid = Environment.ProcessId;
        private OverlaySettings _settings = OverlaySettings.Load();
        private OptionsWindow _options;
        private readonly SensorHistory _history = new SensorHistory();
        private Forms.NotifyIcon _tray;
        private Thread _worker;
        private volatile bool _running = true;
        private IntPtr _hwnd;
        private double? _lastFps;
        private readonly string _logPath = Array.IndexOf(Environment.GetCommandLineArgs(), "--log") > 0
            ? System.IO.Path.Combine(AppContext.BaseDirectory, "overlay-log.txt") : null;

        private const int HotkeyId = 0xF00D;

        private static readonly Brush Good = Freeze(new SolidColorBrush(Color.FromRgb(0x4C, 0xE0, 0x5A)));
        private static readonly Brush Warn = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xD0, 0x40)));
        private static readonly Brush Bad  = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x40)));

        public MainWindow()
        {
            InitializeComponent();
            ApplySettings(_settings);
            // Keep the overlay anchored (e.g. flush right) when its size changes as numbers update
            SizeChanged += (s, e) => ApplyPosition();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;

            // Click-through, never takes focus, hidden from Alt+Tab
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE,
                ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST);

            // Ctrl+Shift+O toggles the overlay
            HwndSource.FromHwnd(_hwnd).AddHook(WndProc);
            RegisterHotKey(_hwnd, HotkeyId, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, 0x4F /* O */);

            SetupTray();

            _fps.Start();
            try { _hw.Open(); } catch { }

            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "Sensor poller" };
            _worker.Start();
        }

        // ---------- Settings ----------

        public void ApplySettings(OverlaySettings s)
        {
            _settings = s;

            var font = new FontFamily(s.FontFamily);
            var weight = s.Bold ? FontWeights.Bold : FontWeights.Normal;
            var labelBrush = ParseBrush(s.LabelColor, Brushes.White);

            foreach (TextBlock tb in AllText())
            {
                tb.FontFamily = font;
                tb.FontSize = s.FontSize;
                tb.FontWeight = weight;
            }
            foreach (TextBlock tb in Labels())
                tb.Foreground = labelBrush;

            Counters.Effect = s.TextShadow
                ? new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 1, Color = Colors.Black }
                : null;

            if (s.ShowBackground)
            {
                var c = ParseColor(s.BackgroundColor, Color.FromRgb(0x10, 0x10, 0x10));
                c.A = (byte)Math.Round(Math.Clamp(s.BackgroundOpacity, 0, 1) * 255);
                Panel.Background = new SolidColorBrush(c);
            }
            else
            {
                Panel.Background = Brushes.Transparent;
            }

            Opacity = Math.Clamp(s.Opacity, 0.1, 1);

            ApplyLayout(s);
            Render(_lastFps);
            UpdateLayout();
            ApplyPosition();
        }

        /// <summary>Arranges the counters for the chosen layout style.</summary>
        private void ApplyLayout(OverlaySettings s)
        {
            Counters.RowDefinitions.Clear();
            Counters.ColumnDefinitions.Clear();

            bool list = s.Layout == "Vertical";
            Visibility Vis(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
            FpsLabel.Visibility = FpsText.Visibility = Vis(s.ShowFps);
            CpuLabel.Visibility = CpuLoadText.Visibility = Vis(s.ShowCpu);
            GpuLabel.Visibility = GpuLoadText.Visibility = Vis(s.ShowGpu);
            CpuTempText.Visibility = Vis(s.ShowCpu && s.ShowTemps);
            GpuTempText.Visibility = Vis(s.ShowGpu && s.ShowTemps);
            // Temperature labels only exist in the one-per-line list
            CpuTempLabel.Visibility = Vis(list && s.ShowCpu && s.ShowTemps);
            GpuTempLabel.Visibility = Vis(list && s.ShowGpu && s.ShowTemps);

            double fs = s.FontSize;
            // Fixed minimum widths stop the overlay from jiggling when numbers change length
            FpsText.MinWidth = fs * 1.75;
            CpuLoadText.MinWidth = GpuLoadText.MinWidth = fs * 2.3;
            CpuTempText.MinWidth = GpuTempText.MinWidth = fs * 2.9;
            var labels = Labels();
            foreach (var tb in AllText())
            {
                tb.VerticalAlignment = VerticalAlignment.Center;
                bool isLabel = Array.IndexOf(labels, tb) >= 0;
                // In the list, everything hugs the left; elsewhere numbers right-align into columns
                tb.HorizontalAlignment = isLabel || list ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
                if (!isLabel) tb.TextAlignment = list ? TextAlignment.Left : TextAlignment.Right;
                Grid.SetColumnSpan(tb, 1);
            }

            double labelGap = fs * 0.6, colGap = fs * 0.5;

            switch (s.Layout)
            {
                case "Horizontal":
                {
                    Counters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var order = new[] { FpsLabel, FpsText, CpuLabel, CpuLoadText, CpuTempText, GpuLabel, GpuLoadText, GpuTempText };
                    for (int i = 0; i < order.Length; i++)
                    {
                        Counters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        Place(order[i], 0, i, new Thickness(0));
                    }
                    double groupGap = fs * 1.2;
                    bool anyBefore = false;
                    foreach (var (label, values) in new[]
                    {
                        (FpsLabel, new[] { FpsText }),
                        (CpuLabel, new[] { CpuLoadText, CpuTempText }),
                        (GpuLabel, new[] { GpuLoadText, GpuTempText })
                    })
                    {
                        if (label.Visibility != Visibility.Visible) continue;
                        label.Margin = new Thickness(anyBefore ? groupGap : 0, 0, labelGap * 0.6, 0);
                        for (int i = 1; i < values.Length; i++) values[i].Margin = new Thickness(colGap, 0, 0, 0);
                        anyBefore = true;
                    }
                    break;
                }

                case "BigFps":
                {
                    for (int r = 0; r < 3; r++) Counters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    for (int c = 0; c < 3; c++) Counters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    FpsText.FontSize = fs * 2.4;
                    FpsText.MinWidth = 0;
                    FpsText.HorizontalAlignment = HorizontalAlignment.Left;
                    FpsLabel.FontSize = fs * 0.85;
                    Place(FpsText, 0, 0, new Thickness(0, 0, labelGap * 0.5, 0));
                    Grid.SetColumnSpan(FpsText, 2);
                    Place(FpsLabel, 0, 2, new Thickness(0, 0, 0, fs * 0.45));
                    FpsLabel.VerticalAlignment = VerticalAlignment.Bottom;
                    PlaceStatRows(1, labelGap, colGap);
                    break;
                }

                case "Compact":
                {
                    for (int r = 0; r < 3; r++) Counters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    for (int c = 0; c < 3; c++) Counters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    Place(FpsLabel, 0, 0, new Thickness(0, 0, labelGap, 0));
                    Place(FpsText, 0, 1, new Thickness(0));
                    PlaceStatRows(1, labelGap, colGap);
                    break;
                }

                default: // Vertical list: one counter per line, down the left
                {
                    var rows = new[]
                    {
                        (FpsLabel, FpsText), (CpuLabel, CpuLoadText), (CpuTempLabel, CpuTempText),
                        (GpuLabel, GpuLoadText), (GpuTempLabel, GpuTempText)
                    };
                    Counters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    Counters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    for (int r = 0; r < rows.Length; r++)
                    {
                        Counters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        Place(rows[r].Item1, r, 0, new Thickness(0, 0, labelGap, 0));
                        Place(rows[r].Item2, r, 1, new Thickness(0));
                    }
                    break;
                }
            }
        }

        private void PlaceStatRows(int firstRow, double labelGap, double colGap)
        {
            Place(CpuLabel, firstRow, 0, new Thickness(0, 0, labelGap, 0));
            Place(CpuLoadText, firstRow, 1, new Thickness(0));
            Place(CpuTempText, firstRow, 2, new Thickness(colGap, 0, 0, 0));
            Place(GpuLabel, firstRow + 1, 0, new Thickness(0, 0, labelGap, 0));
            Place(GpuLoadText, firstRow + 1, 1, new Thickness(0));
            Place(GpuTempText, firstRow + 1, 2, new Thickness(colGap, 0, 0, 0));
        }

        private static void Place(TextBlock tb, int row, int col, Thickness margin)
        {
            Grid.SetRow(tb, row);
            Grid.SetColumn(tb, col);
            tb.Margin = margin;
        }

        /// <summary>Positions the overlay by percentage so 100% reaches the right/bottom edge.</summary>
        private void ApplyPosition()
        {
            const double edge = 8;
            double sw = SystemParameters.PrimaryScreenWidth, sh = SystemParameters.PrimaryScreenHeight;
            double w = ActualWidth > 0 ? ActualWidth : DesiredSize.Width;
            double h = ActualHeight > 0 ? ActualHeight : DesiredSize.Height;
            double px = Math.Clamp(_settings.PosX, 0, 100) / 100, py = Math.Clamp(_settings.PosY, 0, 100) / 100;
            Left = Math.Round(edge + px * Math.Max(0, sw - w - 2 * edge));
            Top = Math.Round(edge + py * Math.Max(0, sh - h - 2 * edge));
        }

        private IEnumerable<TextBlock> AllText() => new[]
        {
            FpsLabel, FpsText, CpuLabel, CpuLoadText, CpuTempLabel, CpuTempText, GpuLabel, GpuLoadText, GpuTempLabel, GpuTempText
        };

        private TextBlock[] Labels() => new[] { FpsLabel, CpuLabel, CpuTempLabel, GpuLabel, GpuTempLabel };

        private void OpenOptions()
        {
            if (_options != null) { _options.Activate(); return; }
            _options = new OptionsWindow(_settings, s => ApplySettings(s.Clone()), _history, PreviewEffects);
            _options.Closed += (s, e) => _options = null;
            if (!IsVisible) Show();
            _options.Show();
            _options.Activate();
        }

        // ---------- Polling ----------

        private void WorkerLoop()
        {
            int tick = 0;
            while (_running)
            {
                Thread.Sleep(500);
                if (!_running) break;

                _fps.Sample();
                bool sensorTick = tick++ % 2 == 0;
                if (sensorTick)
                {
                    try { _hw.Update(); } catch { }
                }

                int pid = GetForegroundProcessId();
                double? fps = pid == _ownPid ? null : _fps.GetFps(pid);
                if (!fps.HasValue && pid > 0 && pid != _ownPid)
                {
                    // Browsers / Electron apps render in a child GPU process
                    foreach (int child in GetChildPids(pid))
                    {
                        double? c = _fps.GetFps(child);
                        if (c.HasValue && (!fps.HasValue || c > fps)) fps = c;
                    }
                }

                if (sensorTick)
                {
                    _history.Add(new SensorSample
                    {
                        Time = DateTime.Now,
                        CpuLoad = _hw.CpuLoad, CpuTemp = _hw.CpuTemp,
                        GpuLoad = _hw.GpuLoad, GpuTemp = _hw.GpuTemp,
                        Fps = fps
                    });
                }

                if (_logPath != null)
                {
                    try
                    {
                        System.IO.File.AppendAllText(_logPath,
                            $"{DateTime.Now:HH:mm:ss} fgPid={pid} fps={fps:0.0} cpu={_hw.CpuLoad:0}% {_hw.CpuTemp:0}C gpu={_hw.GpuLoad:0}% {_hw.GpuTemp:0}C etwError={_fps.Error} all={_fps.DebugSummary()}{Environment.NewLine}");
                    }
                    catch { }
                }

                Dispatcher.BeginInvoke(new Action(() => { _lastFps = fps; Render(fps); }));
            }
        }

        private void Render(double? fps)
        {
            var valueBrush = ParseBrush(_settings.ValueColor, Brushes.White);
            bool coded = _settings.ColorCoding;

            FpsText.Text = fps.HasValue ? Math.Round(fps.Value).ToString("0") : "--";
            FpsText.Foreground = coded && fps.HasValue ? FpsBrush(fps.Value) : valueBrush;

            float? cpuLoad = _hw.CpuLoad, gpuLoad = _hw.GpuLoad, cpuTemp = _hw.CpuTemp, gpuTemp = _hw.GpuTemp;

            // "Preview effects": step through all five levels with sample values
            double previewSec = (DateTime.Now - _previewStart).TotalSeconds;
            bool previewing = previewSec >= 0 && previewSec < PreviewSecondsPerLevel * 5;
            if (previewing)
            {
                int lvl = Math.Min(4, (int)(previewSec / PreviewSecondsPerLevel));
                cpuLoad = new float[] { 9, 31, 52, 71, 93 }[lvl];
                gpuLoad = new float[] { 6, 28, 49, 68, 98 }[lvl];
                cpuTemp = new float[] { 42, 55, 65, 75, 88 }[lvl];
                gpuTemp = new float[] { 39, 50, 61, 73, 84 }[lvl];
            }

            CpuLoadText.Text = Percent(cpuLoad);
            GpuLoadText.Text = Percent(gpuLoad);
            CpuTempText.Text = Temp(cpuTemp);
            GpuTempText.Text = Temp(gpuTemp);

            if (_fxCpuLoad == null)
            {
                _fxCpuLoad = new CounterEffect(CpuLoadText);
                _fxGpuLoad = new CounterEffect(GpuLoadText);
                _fxCpuTemp = new CounterEffect(CpuTempText);
                _fxGpuTemp = new CounterEffect(GpuTempText);
            }

            if (_settings.DynamicEffects || previewing)
            {
                Fx(_fxCpuLoad, cpuLoad, Heat.UsageSteps, false, valueBrush);
                Fx(_fxGpuLoad, gpuLoad, Heat.UsageSteps, false, valueBrush);
                Fx(_fxCpuTemp, cpuTemp, Heat.CpuTempSteps, true, valueBrush);
                Fx(_fxGpuTemp, gpuTemp, Heat.GpuTempSteps, true, valueBrush);
            }
            else
            {
                _fxCpuLoad.Clear(valueBrush);
                _fxGpuLoad.Clear(valueBrush);
                _fxCpuTemp.Clear(TempBrush(cpuTemp, valueBrush, coded));
                _fxGpuTemp.Clear(TempBrush(gpuTemp, valueBrush, coded));
            }

            // Games can push themselves above us; re-assert topmost every tick
            if (_hwnd != IntPtr.Zero && IsVisible)
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        private static string Percent(float? v) => v.HasValue ? $"{v.Value:0}%" : "--";

        private static string Temp(float? v) => v.HasValue ? $"{v.Value:0}°C" : "--";

        private static Brush TempBrush(float? v, Brush normal, bool coded) =>
            !coded || !v.HasValue || v.Value < 70 ? normal : v.Value < 85 ? Warn : Bad;

        private static void Fx(CounterEffect fx, float? v, double[] steps, bool pulseAtHigh, Brush normal)
        {
            if (v.HasValue) fx.Apply(Heat.Level(v.Value, steps), pulseAtHigh);
            else fx.Clear(normal);
        }

        /// <summary>Runs every effect level for a few seconds so it can be seen without heating the PC.</summary>
        public void PreviewEffects()
        {
            _previewStart = DateTime.Now;
            if (!IsVisible) Show();
            Render(_lastFps);
        }

        private const double PreviewSecondsPerLevel = 1.6;
        private DateTime _previewStart = DateTime.MinValue;
        private CounterEffect _fxCpuLoad, _fxGpuLoad, _fxCpuTemp, _fxGpuTemp;

        private static Brush FpsBrush(double fps) => fps >= 60 ? Good : fps >= 30 ? Warn : Bad;

        // ---------- Tray / hotkey ----------

        private void SetupTray()
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Options...", null, (s, a) => OpenOptions());
            menu.Items.Add("Show / hide overlay  (Ctrl+Shift+O)", null, (s, a) => ToggleVisible());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, a) => Close());

            System.Drawing.Icon icon = null;
            try { icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath); } catch { }

            _tray = new Forms.NotifyIcon
            {
                Icon = icon ?? System.Drawing.SystemIcons.Application,
                Text = "Lexi Frames",
                Visible = true,
                ContextMenuStrip = menu
            };
            _tray.DoubleClick += (s, a) => OpenOptions();
        }

        private void ToggleVisible()
        {
            if (IsVisible) Hide(); else Show();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                ToggleVisible();
                handled = true;
            }
            return IntPtr.Zero;
        }

        protected override void OnClosed(EventArgs e)
        {
            _running = false;
            _options?.Close();
            UnregisterHotKey(_hwnd, HotkeyId);
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            _fps.Dispose();
            _hw.Dispose();
            base.OnClosed(e);
        }

        // ---------- Helpers ----------

        private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        private static Color ParseColor(string hex, Color fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); } catch { return fallback; }
        }

        private static Brush ParseBrush(string hex, Brush fallback)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); } catch { return fallback; }
        }

        private static int GetForegroundProcessId()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return -1;
            GetWindowThreadProcessId(fg, out uint pid);
            return (int)pid;
        }

        private static List<int> GetChildPids(int parent)
        {
            var list = new List<int>();
            IntPtr snap = CreateToolhelp32Snapshot(0x2 /* TH32CS_SNAPPROCESS */, 0);
            if (snap == new IntPtr(-1)) return list;
            try
            {
                var pe = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
                if (Process32First(snap, ref pe))
                {
                    do { if (pe.th32ParentProcessID == parent) list.Add((int)pe.th32ProcessID); }
                    while (Process32Next(snap, ref pe));
                }
            }
            finally { CloseHandle(snap); }
            return list;
        }

        // ---------- Win32 ----------

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32
        {
            public uint dwSize, cntUsage, th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID, cntThreads, th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOPMOST = 0x00000008;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")] private static extern bool Process32First(IntPtr snap, ref PROCESSENTRY32 pe);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")] private static extern bool Process32Next(IntPtr snap, ref PROCESSENTRY32 pe);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    }
}
