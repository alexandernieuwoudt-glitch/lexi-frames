using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace LexiFrames
{
    public sealed class ChartSeries
    {
        public string Name;
        public Color Color;
        public Func<SensorSample, double?> Value;
    }

    /// <summary>
    /// Lightweight line chart of sensor history: one y-axis, thin 2px lines, recessive grid,
    /// legend with live values, and a hover crosshair + tooltip.
    /// </summary>
    public sealed class HistoryChart : FrameworkElement
    {
        public string Title { get; set; } = "";
        public string Unit { get; set; } = "";
        /// <summary>Fixed top of the y-axis (e.g. 100 for %). Null = auto-fit to the data.</summary>
        public double? FixedMax { get; set; }
        public double RangeSeconds { get; set; } = 300;
        public List<ChartSeries> Series { get; } = new List<ChartSeries>();

        // Ink & surface (dark Options window)
        private static readonly Brush Surface   = Frozen(Color.FromRgb(0x1E, 0x1F, 0x22));
        private static readonly Brush InkMain   = Frozen(Color.FromRgb(0xE6, 0xE6, 0xE6));
        private static readonly Brush InkMuted  = Frozen(Color.FromRgb(0x9A, 0xA0, 0xA6));
        private static readonly Pen GridPen     = FrozenPen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), 1);
        private static readonly Pen CrossPen    = FrozenPen(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF), 1);
        private static readonly Brush TipBg     = Frozen(Color.FromRgb(0x2B, 0x2D, 0x31));
        private static readonly Pen TipBorder   = FrozenPen(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF), 1);
        private static readonly Typeface Face   = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface FaceB  = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        private SensorSample[] _data = Array.Empty<SensorSample>();
        private Point? _mouse;

        public HistoryChart()
        {
            SnapsToDevicePixels = true;
            MouseMove += (s, e) => { _mouse = e.GetPosition(this); InvalidateVisual(); };
            MouseLeave += (s, e) => { _mouse = null; InvalidateVisual(); };
        }

        public void SetData(SensorSample[] data)
        {
            _data = data ?? Array.Empty<SensorSample>();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 80 || h < 60) return;
            double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            // Transparent fill so the whole area receives mouse events
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

            // ---- Header: title + legend with latest values ----
            dc.DrawText(Text(Title, 13, InkMain, dip, bold: true), new Point(0, 0));
            SensorSample latest = _data.Length > 0 ? _data[_data.Length - 1] : null;
            double lx = w;
            for (int i = Series.Count - 1; i >= 0; i--)
            {
                var s = Series[i];
                double? v = latest != null ? s.Value(latest) : null;
                var ft = Text($"{s.Name}  {Fmt(v)}", 12, InkMain, dip);
                lx -= ft.Width;
                dc.DrawText(ft, new Point(lx, 1));
                lx -= 18;
                dc.DrawRoundedRectangle(new SolidColorBrush(s.Color), null, new Rect(lx, 8, 12, 3), 1.5, 1.5);
                lx -= 14;
            }

            // ---- Plot area ----
            var plot = new Rect(38, 26, Math.Max(10, w - 38 - 6), Math.Max(10, h - 26 - 20));
            DateTime now = DateTime.Now;
            DateTime t0 = now.AddSeconds(-RangeSeconds);
            var visible = _data.Where(d => d.Time >= t0).ToArray();

            // Y range
            double lo = 0, hi = FixedMax ?? 100;
            if (!FixedMax.HasValue)
            {
                var vals = visible.SelectMany(d => Series.Select(s => s.Value(d))).Where(v => v.HasValue).Select(v => v.Value).ToList();
                if (vals.Count > 0)
                {
                    lo = Math.Max(0, Math.Floor((vals.Min() - 5) / 10) * 10);
                    hi = Math.Ceiling((vals.Max() + 5) / 10) * 10;
                }
                else { lo = 0; hi = 100; }
                double span = Math.Max(20, Math.Ceiling((hi - lo) / 20) * 20);
                hi = lo + span;
            }

            // Grid + y labels (5 lines)
            for (int i = 0; i <= 4; i++)
            {
                double val = lo + (hi - lo) * i / 4;
                double y = Math.Round(plot.Bottom - plot.Height * i / 4) + 0.5;
                dc.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                var lbl = Text(val.ToString("0", CultureInfo.InvariantCulture), 10.5, InkMuted, dip);
                dc.DrawText(lbl, new Point(plot.Left - 6 - lbl.Width, y - lbl.Height / 2));
            }

            // X labels
            var left = Text(Ago(RangeSeconds), 10.5, InkMuted, dip);
            var mid = Text(Ago(RangeSeconds / 2), 10.5, InkMuted, dip);
            var right = Text("now", 10.5, InkMuted, dip);
            dc.DrawText(left, new Point(plot.Left, plot.Bottom + 4));
            dc.DrawText(mid, new Point(plot.Left + plot.Width / 2 - mid.Width / 2, plot.Bottom + 4));
            dc.DrawText(right, new Point(plot.Right - right.Width, plot.Bottom + 4));

            if (visible.Length < 2)
            {
                var msg = Text("Collecting data…", 12, InkMuted, dip);
                dc.DrawText(msg, new Point(plot.Left + plot.Width / 2 - msg.Width / 2, plot.Top + plot.Height / 2 - msg.Height / 2));
                return;
            }

            double X(DateTime t) => plot.Left + (t - t0).TotalSeconds / RangeSeconds * plot.Width;
            double Y(double v) => plot.Bottom - (Math.Clamp(v, lo, hi) - lo) / (hi - lo) * plot.Height;

            // ---- Lines (gaps where a reading is missing or > 3 s apart) ----
            dc.PushClip(new RectangleGeometry(new Rect(plot.Left, plot.Top - 2, plot.Width, plot.Height + 4)));
            foreach (var s in Series)
            {
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    bool open = false;
                    DateTime prevT = DateTime.MinValue;
                    foreach (var d in visible)
                    {
                        double? v = s.Value(d);
                        if (!v.HasValue || (open && (d.Time - prevT).TotalSeconds > 3)) { open = false; if (!v.HasValue) continue; }
                        var p = new Point(X(d.Time), Y(v.Value));
                        if (!open) { ctx.BeginFigure(p, false, false); open = true; }
                        else ctx.LineTo(p, true, true);
                        prevT = d.Time;
                    }
                }
                geo.Freeze();
                var pen = new Pen(new SolidColorBrush(s.Color), 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawGeometry(null, pen, geo);
            }
            dc.Pop();

            // ---- Hover crosshair + tooltip ----
            if (_mouse.HasValue && plot.Contains(_mouse.Value))
            {
                DateTime tm = t0.AddSeconds((_mouse.Value.X - plot.Left) / plot.Width * RangeSeconds);
                SensorSample near = visible.OrderBy(d => Math.Abs((d.Time - tm).TotalSeconds)).First();
                double cx = Math.Round(X(near.Time)) + 0.5;
                dc.DrawLine(CrossPen, new Point(cx, plot.Top), new Point(cx, plot.Bottom));

                foreach (var s in Series)
                {
                    double? v = s.Value(near);
                    if (!v.HasValue) continue;
                    var c = new Point(cx, Y(v.Value));
                    dc.DrawEllipse(Surface, null, c, 6, 6);                          // 2px surface ring
                    dc.DrawEllipse(new SolidColorBrush(s.Color), null, c, 4, 4);
                }

                // Tooltip
                var head = Text(Ago((now - near.Time).TotalSeconds, longForm: true), 11, InkMuted, dip);
                var rows = Series.Select(s => (s, Text($"{s.Name}", 12, InkMain, dip), Text(Fmt(s.Value(near)), 12, InkMain, dip, bold: true))).ToList();
                double nameW = rows.Max(r => r.Item2.Width), valW = rows.Max(r => r.Item3.Width);
                double tw = Math.Max(head.Width, 14 + nameW + 14 + valW) + 16;
                double th = 8 + head.Height + 2 + rows.Count * 18 + 6;
                double tx = cx + 12, ty = plot.Top + 4;
                if (tx + tw > w) tx = cx - 12 - tw;
                var box = new Rect(tx, ty, tw, th);
                dc.DrawRoundedRectangle(TipBg, TipBorder, box, 4, 4);
                dc.DrawText(head, new Point(tx + 8, ty + 6));
                double ry = ty + 8 + head.Height + 2;
                foreach (var (s, name, val) in rows)
                {
                    dc.DrawRoundedRectangle(new SolidColorBrush(s.Color), null, new Rect(tx + 8, ry + 7, 10, 3), 1.5, 1.5);
                    dc.DrawText(name, new Point(tx + 22, ry));
                    dc.DrawText(val, new Point(tx + tw - 8 - val.Width, ry));
                    ry += 18;
                }
            }
        }

        private string Fmt(double? v) => v.HasValue ? v.Value.ToString("0", CultureInfo.InvariantCulture) + Unit : "--";

        private static string Ago(double seconds, bool longForm = false)
        {
            if (longForm)
            {
                if (seconds < 1.5) return "now";
                if (seconds < 90) return $"{seconds:0}s ago";
                return $"{Math.Floor(seconds / 60):0}m {seconds % 60:00}s ago";
            }
            return seconds < 120 ? $"-{seconds:0}s" : $"-{seconds / 60:0.#}m";
        }

        private FormattedText Text(string s, double size, Brush b, double dip, bool bold = false) =>
            new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold ? FaceB : Face, size, b, dip);

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        private static Pen FrozenPen(Color c, double t) { var p = new Pen(Frozen(c), t); p.Freeze(); return p; }
    }
}
