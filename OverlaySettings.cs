using System;
using System.IO;
using System.Text.Json;

namespace LexiFrames
{
    /// <summary>User-editable look of the overlay. Saved to %AppData%\LexiFrames\settings.json.</summary>
    public sealed class OverlaySettings
    {
        // Text
        public string FontFamily { get; set; } = "Consolas";
        public double FontSize { get; set; } = 15;
        public bool Bold { get; set; } = true;
        public string LabelColor { get; set; } = "#FFFFFFFF";
        public string ValueColor { get; set; } = "#FFFFFFFF";
        public bool ColorCoding { get; set; } = true;   // green/yellow/red FPS & temps
        public bool TextShadow { get; set; } = true;    // keeps text readable without a background
        public double Opacity { get; set; } = 1.0;

        // Colour / glow / pulse counters by usage & temperature level (see CounterEffects.cs)
        public bool DynamicEffects { get; set; } = false;

        // Background (off by default = minimal look)
        public bool ShowBackground { get; set; } = false;
        public string BackgroundColor { get; set; } = "#FF101010";
        public double BackgroundOpacity { get; set; } = 0.6;

        // Layout: "Vertical" (stacked list), "Horizontal" (one row), "BigFps" (large FPS + stats below)
        public string Layout { get; set; } = "Vertical";

        // Position as a percentage of the screen: 0 = left/top edge, 100 = right/bottom edge
        public double PosX { get; set; } = 0;
        public double PosY { get; set; } = 0;

        // Which counters are shown
        public bool ShowFps { get; set; } = true;
        public bool ShowCpu { get; set; } = true;
        public bool ShowGpu { get; set; } = true;
        public bool ShowTemps { get; set; } = true;

        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LexiFrames");
        private static readonly string FilePath = Path.Combine(Dir, "settings.json");

        public static OverlaySettings Load()
        {
            try
            {
                // Carry over settings saved under the program's old name (AliFPS)
                string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AliFPS", "settings.json");
                string path = File.Exists(FilePath) ? FilePath : File.Exists(legacy) ? legacy : null;
                if (path != null)
                    return JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(path)) ?? new OverlaySettings();
            }
            catch { }
            return new OverlaySettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public OverlaySettings Clone() =>
            JsonSerializer.Deserialize<OverlaySettings>(JsonSerializer.Serialize(this));
    }
}
