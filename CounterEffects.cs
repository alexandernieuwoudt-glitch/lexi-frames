using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace LexiFrames
{
    /// <summary>
    /// Five-level "heat" scale for usage and temperature counters.
    ///
    /// Art direction:
    ///  - Colour runs a heat ramp: cool cyan -> green -> yellow -> orange -> red.
    ///  - Glow grows with the level (none at the bottom two levels, so calm stays clean).
    ///  - Colours blend smoothly (~0.45 s) so values never flicker between levels.
    ///  - Motion is reserved as a warning: only a High *temperature* pulses.
    ///    High usage is normal while gaming, so it glows but never moves.
    /// </summary>
    public static class Heat
    {
        public static readonly string[] Names = { "Low", "Low–Medium", "Medium", "Medium–High", "High" };

        public static readonly Color[] Colors =
        {
            Color.FromRgb(0x5A, 0xC8, 0xFA), // Low          - cool cyan
            Color.FromRgb(0x4C, 0xE0, 0x5A), // Low–Medium   - green
            Color.FromRgb(0xE8, 0xE0, 0x4A), // Medium       - yellow
            Color.FromRgb(0xFF, 0x9F, 0x40), // Medium–High  - orange
            Color.FromRgb(0xFF, 0x4D, 0x4D), // High         - red
        };

        // Thresholds (lower bound of levels 1..4)
        public static readonly double[] UsageSteps   = { 20, 40, 60, 80 };   // %
        public static readonly double[] CpuTempSteps = { 50, 60, 70, 80 };   // °C
        public static readonly double[] GpuTempSteps = { 45, 55, 68, 78 };   // °C

        public static int Level(double value, double[] steps)
        {
            int lvl = 0;
            while (lvl < steps.Length && value >= steps[lvl]) lvl++;
            return lvl;
        }
    }

    /// <summary>Applies the heat effect (colour, glow, pulse) to one counter.</summary>
    public sealed class CounterEffect
    {
        private static readonly double[] GlowBlur    = { 0, 0, 8, 12, 16 };
        private static readonly double[] GlowOpacity = { 0, 0, 0.75, 0.95, 1.0 };
        private static readonly Duration Blend = new Duration(TimeSpan.FromMilliseconds(450));

        private readonly TextBlock _tb;
        private readonly SolidColorBrush _brush = new SolidColorBrush(Colors.White);
        private readonly ScaleTransform _scale = new ScaleTransform(1, 1);
        private DropShadowEffect _glow;
        private int _level = -1;
        private bool _pulsing;

        public CounterEffect(TextBlock tb) { _tb = tb; }

        public void Apply(int level, bool pulseAtHigh)
        {
            if (level == _level && _tb.Foreground == _brush) return;
            _level = level;
            Color c = Heat.Colors[level];

            // Smooth colour blend from whatever the counter shows now
            if (_tb.Foreground != _brush)
            {
                _brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                _brush.Color = (_tb.Foreground as SolidColorBrush)?.Color ?? Colors.White;
                _tb.Foreground = _brush;
            }
            _brush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(c, Blend) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } });

            // Glow
            if (level < 2)
            {
                _tb.Effect = null;
            }
            else
            {
                if (_glow == null) _glow = new DropShadowEffect { ShadowDepth = 0 };
                _glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
                _glow.Color = c;
                _glow.BlurRadius = GlowBlur[level];
                _glow.Opacity = GlowOpacity[level];
                _tb.Effect = _glow;
            }

            // Pulse: warning only
            bool pulse = pulseAtHigh && level == 4;
            if (pulse && !_pulsing)
            {
                _tb.RenderTransformOrigin = new Point(0.5, 0.5);
                _tb.RenderTransform = _scale;
                var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
                var throb = new DoubleAnimation(1.0, 1.08, TimeSpan.FromMilliseconds(600))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
                _scale.BeginAnimation(ScaleTransform.ScaleXProperty, throb);
                _scale.BeginAnimation(ScaleTransform.ScaleYProperty, throb);
                _pulsing = true;
            }
            else if (!pulse && _pulsing)
            {
                StopPulse();
            }
            if (pulse)
            {
                _glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.35, 1.0, TimeSpan.FromMilliseconds(600))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            }
        }

        /// <summary>Removes all effects and restores a plain brush.</summary>
        public void Clear(Brush normal)
        {
            if (_level == -1 && _tb.Foreground != _brush) { _tb.Foreground = normal; return; }
            _level = -1;
            if (_pulsing) StopPulse();
            _glow?.BeginAnimation(DropShadowEffect.OpacityProperty, null);
            _tb.Effect = null;
            _tb.Foreground = normal;
        }

        private void StopPulse()
        {
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _scale.ScaleX = _scale.ScaleY = 1;
            _tb.RenderTransform = null;
            _pulsing = false;
        }
    }
}
