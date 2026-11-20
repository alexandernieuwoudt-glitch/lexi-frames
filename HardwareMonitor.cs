using System;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace LexiFrames
{
    /// <summary>Reads CPU/GPU load and temperature via LibreHardwareMonitor.</summary>
    public sealed class HardwareMonitor : IDisposable
    {
        private readonly Computer _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };

        public float? CpuLoad { get; private set; }
        public float? CpuTemp { get; private set; }
        public float? GpuLoad { get; private set; }
        public float? GpuTemp { get; private set; }

        public void Open() => _computer.Open();

        public void Update()
        {
            float? cpuLoad = null, cpuTemp = null, gpuLoad = null, gpuTemp = null;
            int gpuRank = -1;

            foreach (IHardware hw in _computer.Hardware)
            {
                try
                {
                    hw.Update();
                    foreach (IHardware sub in hw.SubHardware) sub.Update();
                }
                catch { continue; }

                switch (hw.HardwareType)
                {
                    case HardwareType.Cpu:
                        cpuLoad = Find(hw, SensorType.Load, "CPU Total");
                        cpuTemp = Find(hw, SensorType.Temperature, "CPU Package")
                               ?? Find(hw, SensorType.Temperature, "Core Max")
                               ?? Find(hw, SensorType.Temperature, "Core Average")
                               ?? MaxOf(hw, SensorType.Temperature);
                        break;

                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel:
                        // Prefer a dedicated GPU over integrated graphics
                        int rank = hw.HardwareType == HardwareType.GpuIntel ? 0 : 1;
                        if (rank <= gpuRank) break;
                        gpuRank = rank;
                        gpuLoad = Find(hw, SensorType.Load, "GPU Core") ?? Find(hw, SensorType.Load, "D3D 3D");
                        gpuTemp = Find(hw, SensorType.Temperature, "GPU Core") ?? MaxOf(hw, SensorType.Temperature);
                        break;
                }
            }

            CpuLoad = cpuLoad; CpuTemp = cpuTemp; GpuLoad = gpuLoad; GpuTemp = gpuTemp;
        }

        private static float? Find(IHardware hw, SensorType type, string name)
        {
            var s = hw.Sensors.FirstOrDefault(x => x.SensorType == type &&
                        string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            return Valid(s?.Value);
        }

        private static float? MaxOf(IHardware hw, SensorType type)
        {
            float? best = null;
            foreach (var s in hw.Sensors.Where(x => x.SensorType == type))
            {
                var v = Valid(s.Value);
                if (v.HasValue && (!best.HasValue || v > best)) best = v;
            }
            return best;
        }

        private static float? Valid(float? v) =>
            v.HasValue && !float.IsNaN(v.Value) && v.Value > 0 ? v : null;

        public void Dispose()
        {
            try { _computer.Close(); } catch { }
        }
    }
}
