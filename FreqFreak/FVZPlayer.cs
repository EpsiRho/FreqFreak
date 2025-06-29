using FFTVIS;
using FreqFreak;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.ComponentModel;
using System.Runtime.CompilerServices;

public class FVZPlayer : INotifyPropertyChanged
{
    private static FrequencyVisualizer? _fv;
    private static PlaybackTimer? _playbackTimer = new();
    private static AudioFileReader? _audioFile;
    private static OffsetSampleProvider? _OffsetAudioProvider;
    private static WaveOutEvent? _waveOut = new WaveOutEvent();
    private static CancellationTokenSource? _cts = new CancellationTokenSource();
    private static int _lastFrame = -1;
    private static int _barsWanted;
    private static double _MsCurOffset;
    private static float _vol = 0.5f;
    public static string fvzPath = @"";
    public static string audioPath = @"";
    public static int AudioDelayMs = 0;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public double PlayerVolume
    {
        get
        {
            if(_waveOut == null)
            {
                return _vol;
            }
            return _waveOut.Volume;
        }
        set
        {
            if (_waveOut == null)
            {
                _vol = (float)value;
                return;
            }
            if (value != _waveOut.Volume && value > 0.0f && value < 1.0f)
            {
                _waveOut.Volume = (float)value;
                _vol = (float)value;
                MainWindow.FVZPlayer.OnPropertyChanged();
            }
        }
    }
    public double PlaybackSeekMs;
    public bool isSeeking;
    public double PlaybackMs
    {
        get
        {
            if (isSeeking)
            {
                return PlaybackSeekMs;
            }
            if (_audioFile == null)
            {
                return PlaybackSeekMs;
            }

            var time = _playbackTimer.Position.TotalMilliseconds;
            PlaybackSeekMs = time;

            return time;
        }
        set
        {
            if (isSeeking)
            {
                if (value != PlaybackSeekMs)
                {
                    PlaybackSeekMs = value;
                }
            }
        }
    }
    public double PlaybackMsMax
    {
        get
        {
            if (_audioFile == null)
            {
                return 1.0f;
            }

            return _audioFile.TotalTime.TotalMilliseconds;
        }
    }
    public string PlaybackTime
    {
        get
        {
            if (isSeeking)
            {
                return $"{TimeSpan.FromMilliseconds(PlaybackSeekMs).ToString("hh\\:mm\\:ss\\.ff")}/{_audioFile.TotalTime.ToString("hh\\:mm\\:ss\\.ff")}";
            }
            if (_audioFile == null)
            {
                return "00:00.00";
            }

            return $"{_audioFile.CurrentTime.ToString("hh\\:mm\\:ss\\.ff")}/{_audioFile.TotalTime.ToString("hh\\:mm\\:ss\\.ff")}";
        }
    }

    public static void SetFV(FrequencyVisualizer fv)
    {
        _fv = fv;
        if(_waveOut == null)
        {
            _waveOut = new WaveOutEvent();
        }
        if(_waveOut.PlaybackState == PlaybackState.Stopped)
        {
            return;
        }
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        //_ = Task.Run(() => PumpFrames(_cts.Token));
    }
    public static void Seek(double ms, FrequencyVisualizer fv = null)
    {
        _cts?.Cancel();
        _waveOut?.Stop();
        if (_audioFile == null)
        {
            return;
        }
        _audioFile.Dispose();

        if (fv == null)
        {
            if (fvzPath == "")
            {
                return;
            }
            _fv = AudioDecoder.ReadFile(fvzPath);
        }
        else
        {
            _fv = fv;
        }
        _barsWanted = _fv.metadata.numBands;

        Visualizer.InstanceOptions._bars = _barsWanted;
        Visualizer.UpdateSettings = true;
        _audioFile = new AudioFileReader(audioPath);

        var trimmed = new OffsetSampleProvider(_audioFile)
        {
            SkipOver = TimeSpan.FromMilliseconds(ms)
        };

        _MsCurOffset = ms;

        _cts = new CancellationTokenSource();
        _waveOut.Init(trimmed);
        _waveOut.Volume = _vol;
        _waveOut.Play();

        _playbackTimer.Start();
        _playbackTimer.Set(TimeSpan.FromMilliseconds(_MsCurOffset));
    }
    public static double[] GetCurrentFrame()
    {
        try
        {
            if (_fv == null || _audioFile == null || _waveOut == null)
            {
                return null;
            }

            // Use compensated time for frame calculation
            int frameIndex = GetFrameIndexForTime(_playbackTimer.Position.TotalMilliseconds, _fv.metadata.frameRate);

            MainWindow.FVZPlayer.OnPropertyChanged("PlaybackMs");
            MainWindow.FVZPlayer.OnPropertyChanged("PlaybackMsMax");
            MainWindow.FVZPlayer.OnPropertyChanged("PlaybackTime");

            if (_fv!.frames.Length <= frameIndex || frameIndex < 0)
            {
                return null;
            }

            return (double[])_fv!.frames[frameIndex].Clone();
        }
        catch (Exception) { }
        return null;
    }

    public static void Start(string audioFile, FrequencyVisualizer fv = null)
    {
        _MsCurOffset = 0;
        if (audioFile == "")
        {
            return;
        }
        audioPath = audioFile;

        if (fv == null)
        {
            _fv = AudioDecoder.ReadFile(fvzPath);
        }
        else
        {
            _fv = fv;
        }
        _barsWanted = _fv.metadata.numBands;

        Visualizer.InstanceOptions._bars = _barsWanted;
        Visualizer.UpdateSettings = true;

        _audioFile = new AudioFileReader(audioPath);
        if (_waveOut == null)
        {
            _waveOut = new();
        }
        float vol = _waveOut.Volume;
        _waveOut.Volume = vol;
        if (_waveOut.PlaybackState != PlaybackState.Stopped)
        {
            _waveOut.Stop();
        }
        _waveOut.Init(_audioFile);
        _waveOut.Volume = _vol;
        _waveOut.Play();

        _playbackTimer.Reset();
        _playbackTimer.Start();

        _cts = new CancellationTokenSource();
    }
    public static void SeekPaused(double ms)
    {
        if (!_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }
        if (_waveOut.PlaybackState == PlaybackState.Playing)
        {
            _waveOut.Pause();
        }
        if(_waveOut.PlaybackState == PlaybackState.Stopped)
        {
            return;
        }

        double frameDurMs = 1000.0 / _fv.metadata.frameRate;
        int totalFrames = (int)_fv.metadata.totalFrames;
        long bytesPos = _waveOut.GetPosition(); // bytes already played
        double msPos = ms;

        // map time -> frame index
        int frameIndex = (int)(msPos / frameDurMs);
        if (frameIndex >= totalFrames) return;

        // enqueue
        if (frameIndex != _lastFrame)
        {
            var frameCopy = (double[])_fv!.frames[frameIndex].Clone();

            while (Visualizer._frameQueue.Count > 3)
                Visualizer._frameQueue.TryDequeue(out _);

            Visualizer._frameQueue.Enqueue(frameCopy);
            _lastFrame = frameIndex;
        }
        _playbackTimer.Set(TimeSpan.FromMilliseconds(ms));

        //await Task.Delay(1, token);
        MainWindow.FVZPlayer.OnPropertyChanged("PlaybackMs");
        MainWindow.FVZPlayer.OnPropertyChanged("PlaybackMsMax");
        MainWindow.FVZPlayer.OnPropertyChanged("PlaybackTime");
    }
    public static void Pause()
    {
        if (_waveOut == null)
            return;
        _waveOut.Pause();
        _playbackTimer.Stop();
    }
    public static void Resume()
    {
        if (_waveOut == null)
            return;
        _waveOut.Play();
        var time = _audioFile.CurrentTime == null ? 0 : _audioFile.CurrentTime.TotalMilliseconds;
        _playbackTimer.Start();

    }
    public static void Stop()
    {
        _cts?.Cancel();
        _waveOut?.Stop();
        _playbackTimer.Stop();
        _waveOut?.Dispose();
        _audioFile?.Dispose();

        _waveOut = null;
        _audioFile = null;
        _fv = null;
    }

    public static int GetFrameIndexForTime(double currentTimeMs, double frameRate)
    {
        double frameDurationMs = 1000.0 / frameRate;
        double exactFrame = currentTimeMs / frameDurationMs;

        int roundFrame = (int)Math.Round(exactFrame);

        int frameIndex = roundFrame;

        return frameIndex;
    }


}
