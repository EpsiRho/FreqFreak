namespace FreqFreak
{
    using System;

    public class PlaybackTimer
    {
        private TimeSpan _current;
        private DateTime? _startTime;
        private bool _running;

        public PlaybackTimer()
        {
            Reset();
        }

        // Start the timer
        public void Start()
        {
            if (!_running)
            {
                _startTime = DateTime.UtcNow;
                _running = true;
            }
        }

        // Stop the timer
        public void Stop()
        {
            if (_running)
            {
                _current = GetElapsed();
                _startTime = null;
                _running = false;
            }
        }

        // Reset the timer
        public void Reset()
        {
            _current = TimeSpan.Zero;
            _startTime = null;
            _running = false;
        }

        // Set the timer to a specific position (for seeking)
        public void Set(TimeSpan time)
        {
            _current = time;
            if (_running)
            {
                _startTime = DateTime.UtcNow;
            }
        }

        // Get the current elapsed time
        public TimeSpan Position
        {
            get
            {
                return GetElapsed();
            }
        }

        // Helper: get the current elapsed time
        private TimeSpan GetElapsed()
        {
            if (_running && _startTime.HasValue)
            {
                return _current + (DateTime.UtcNow - _startTime.Value);
            }
            else
            {
                return _current;
            }
        }

        // For convenience
        public bool IsRunning => _running;
    }

}
