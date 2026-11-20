using System;
using System.Collections.Generic;

namespace LexiFrames
{
    public sealed class SensorSample
    {
        public DateTime Time;
        public float? CpuLoad, CpuTemp, GpuLoad, GpuTemp;
        public double? Fps;
    }

    /// <summary>Keeps the last 30 minutes of readings (one per second) in memory.</summary>
    public sealed class SensorHistory
    {
        public const int Capacity = 30 * 60;
        private readonly Queue<SensorSample> _samples = new Queue<SensorSample>(Capacity + 1);
        private readonly object _lock = new object();

        public void Add(SensorSample s)
        {
            lock (_lock)
            {
                _samples.Enqueue(s);
                while (_samples.Count > Capacity) _samples.Dequeue();
            }
        }

        public SensorSample[] Snapshot()
        {
            lock (_lock) return _samples.ToArray();
        }
    }
}
