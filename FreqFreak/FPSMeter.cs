using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FreqFreak
{
    public class FPSMeter
    {
        readonly Stopwatch sw = Stopwatch.StartNew();
        readonly ConcurrentQueue<double> frameTimes = new();

        int framesThisSecond;
        long lastSecondMark;
        object fpsLock = new object();
        public double RollingFps
        {
            get
            {
                double[] arr = new double[60];
                arr = frameTimes.ToArray();
                if (arr.Length == 0)
                {
                    return 0;
                }
                return 1000.0 / arr.Average();
            }
        }

        public void Tick()
        {
            double dt = sw.Elapsed.TotalMilliseconds;
            sw.Restart();

            frameTimes.Enqueue(dt);
            if (frameTimes.Count > 30) frameTimes.TryDequeue(out _);

            framesThisSecond++;
        }
        public void ResetFpsCounters()
        {
            lock (fpsLock)
            {
                framesThisSecond = 0;
                lastSecondMark = 0;
                frameTimes.Clear();
                sw.Restart();
            }
        }
    }
}
