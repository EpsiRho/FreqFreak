using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FreqFreak
{
    public class FPSMeter
    {
        // Tracking FPS
        private Stopwatch _fpsStopwatch = new Stopwatch();
        private Queue<double> _frameTimes = new Queue<double>(30); // Store recent frame times
        private double _currentFps = 0;
        private double _averageFps = 0;
        public int _frameCount = 0;
        private long _lastSecondTick = 0;
        private int _framesThisSecond = 0;
        private object _fpsLock = new object();

        // External FPS tracking properties
        public double CurrentFps => _currentFps;
        public double AverageFps => _averageFps;
        public double FrameTime { get; private set; } = 0; // Last frame time in ms
        public (double curFps, double avgFps, double ft) GetFpsStats()
        {
            lock (_fpsLock)
            {
                return (_currentFps, _averageFps, FrameTime);
            }
        }
        public void ResetFpsCounters()
        {
            lock (_fpsLock)
            {
                _frameTimes.Clear();
                _frameCount = 0;
                _framesThisSecond = 0;
                _currentFps = 0;
                _averageFps = 0;
                _lastSecondTick = Environment.TickCount;
            }
        }
        public void StartFpsCounter()
        {
            _fpsStopwatch.Restart();
        }
        public double StopFpsCounter()
        {
            _fpsStopwatch.Stop();

            lock (_fpsLock)
            {
                double frameTimeMs = _fpsStopwatch.Elapsed.TotalMilliseconds;
                FrameTime = frameTimeMs;

                // Add to rolling window of frame times (keep last 30 frames)
                _frameTimes.Enqueue(frameTimeMs);
                if (_frameTimes.Count > 30)
                    _frameTimes.Dequeue();

                // Calculate average frame time
                double avgFrameTime = _frameTimes.Average();

                // Calculate instantaneous FPS from current frame
                _currentFps = 1000.0 / frameTimeMs;

                // Calculate average FPS from rolling window
                _averageFps = 1000.0 / avgFrameTime;

                // Track FPS per second for more stable reporting
                _frameCount++;
                _framesThisSecond++;

                long currentTick = Environment.TickCount;
                if (currentTick - _lastSecondTick >= 1000)
                {
                    // A second has passed, update the FPS count
                    _currentFps = _framesThisSecond;
                    _framesThisSecond = 0;
                    _lastSecondTick = currentTick;
                }

                return frameTimeMs;
            }
        }
    }
}
