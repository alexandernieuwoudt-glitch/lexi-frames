using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace LexiFrames
{
    /// <summary>
    /// Measures FPS per process from ETW present events (the same technique PresentMon uses).
    /// Nothing is injected into games, so it is safe with anti-cheat.
    ///
    /// ETW delivers events in batches (buffers are flushed about once a second), so FPS is
    /// computed from each frame's own timestamp rather than from when the event arrived.
    /// </summary>
    public sealed class FpsMonitor : IDisposable
    {
        private static readonly Guid DxgiProvider    = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9"); // Microsoft-Windows-DXGI
        private static readonly Guid D3D9Provider    = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9"); // Microsoft-Windows-D3D9
        private static readonly Guid DxgKrnlProvider = new Guid("802EC45A-1E99-4B83-9920-87C98277BA9D"); // Microsoft-Windows-DxgKrnl

        private const int DxgiPresentStart = 42;
        private const int D3D9PresentStart = 1;
        private const int KrnlPresentHistoryStart = 171;
        private const int KrnlPresentHistoryDetailedStart = 215;
        private const ulong KrnlPresentKeyword = 0x8000000;

        private const string SessionName = "LexiFrames_PresentSession";
        private const int Sources = 4;

        /// <summary>Averaging window for the FPS value.</summary>
        private const double WindowMs = 1000;
        /// <summary>How much history to keep per source.</summary>
        private const double KeepMs = 2500;
        /// <summary>A process with no frames for this long is treated as not rendering.</summary>
        private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(3);

        private sealed class ProcState
        {
            public readonly Queue<double>[] Frames = { new Queue<double>(), new Queue<double>(), new Queue<double>(), new Queue<double>() };
            public DateTime LastEventUtc;
        }

        private TraceEventSession _session;
        private Thread _thread;
        private readonly ConcurrentDictionary<int, ProcState> _procs = new ConcurrentDictionary<int, ProcState>();
        private readonly ConcurrentDictionary<int, double> _fps = new ConcurrentDictionary<int, double>();

        public string Error { get; private set; }

        public void Start()
        {
            try
            {
                _session = new TraceEventSession(SessionName) { StopOnDispose = true };
                _session.EnableProvider(DxgiProvider, TraceEventLevel.Verbose, ulong.MaxValue);
                _session.EnableProvider(D3D9Provider, TraceEventLevel.Verbose, ulong.MaxValue);
                _session.EnableProvider(DxgKrnlProvider, TraceEventLevel.Verbose, KrnlPresentKeyword);
                _session.Source.AllEvents += OnEvent;

                _thread = new Thread(() =>
                {
                    try { _session.Source.Process(); }
                    catch (Exception ex) { Error = ex.Message; }
                })
                { IsBackground = true, Name = "ETW present listener" };
                _thread.Start();
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
        }

        private void OnEvent(TraceEvent e)
        {
            int idx = -1;
            int id = (int)e.ID;
            Guid p = e.ProviderGuid;

            if (p == DxgiProvider) { if (id == DxgiPresentStart) idx = 0; }
            else if (p == D3D9Provider) { if (id == D3D9PresentStart) idx = 1; }
            else if (p == DxgKrnlProvider)
            {
                if (id == KrnlPresentHistoryStart) idx = 2;
                else if (id == KrnlPresentHistoryDetailedStart) idx = 3;
            }
            if (idx < 0) return;

            double t = e.TimeStampRelativeMSec; // exact time the frame was presented
            ProcState st = _procs.GetOrAdd(e.ProcessID, _ => new ProcState());
            lock (st)
            {
                var q = st.Frames[idx];
                q.Enqueue(t);
                while (q.Count > 0 && t - q.Peek() > KeepMs) q.Dequeue();
                st.LastEventUtc = DateTime.UtcNow;
            }
        }

        /// <summary>Recomputes FPS for every process. Call a few times per second.</summary>
        public void Sample()
        {
            DateTime now = DateTime.UtcNow;
            foreach (var kv in _procs)
            {
                ProcState st = kv.Value;
                double? fps;
                lock (st)
                {
                    if (now - st.LastEventUtc > StaleAfter)
                    {
                        _procs.TryRemove(kv.Key, out _);
                        _fps.TryRemove(kv.Key, out _);
                        continue;
                    }

                    // Prefer API-level events (one per frame); fall back to kernel events
                    // (covers OpenGL / Vulkan games that don't go through DXGI or D3D9).
                    fps = Best(st.Frames[0], st.Frames[1]) ?? Best(st.Frames[2], st.Frames[3]);
                }

                if (fps.HasValue) _fps[kv.Key] = fps.Value;
                else _fps.TryRemove(kv.Key, out _);
            }
        }

        private static double? Best(Queue<double> a, Queue<double> b)
        {
            double? fa = Rate(a), fb = Rate(b);
            if (!fa.HasValue) return fb;
            if (!fb.HasValue) return fa;
            return Math.Max(fa.Value, fb.Value);
        }

        /// <summary>Frames per second over the most recent window, using frame timestamps.</summary>
        private static double? Rate(Queue<double> q)
        {
            if (q.Count < 2) return null;

            double last = double.MinValue;
            foreach (double t in q) last = t;          // newest frame
            double from = last - WindowMs;

            double first = double.NaN;
            int count = 0;
            foreach (double t in q)
            {
                if (t < from) continue;
                if (double.IsNaN(first)) first = t;
                count++;
            }

            if (count >= 2 && last > first)
                return (count - 1) * 1000.0 / (last - first);  // frame intervals / elapsed time

            // Very low FPS (<2): use the two newest frames
            double prev = double.NaN, cur = double.NaN;
            foreach (double t in q) { prev = cur; cur = t; }
            return !double.IsNaN(prev) && cur > prev ? 1000.0 / (cur - prev) : (double?)null;
        }

        public string DebugSummary()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in _fps) sb.Append($"[{kv.Key}:{kv.Value:0.0}] ");
            return sb.ToString();
        }

        public double? GetFps(int processId)
        {
            return _fps.TryGetValue(processId, out double v) ? v : (double?)null;
        }

        public void Dispose()
        {
            try { _session?.Dispose(); } catch { }
        }
    }
}
