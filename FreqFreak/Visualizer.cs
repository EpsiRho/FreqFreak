using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using FftSharp;
using NAudio.CoreAudioApi;
using NAudio.Dmo;
using NAudio.Wave;

namespace FreqFreak
{
    public static class Visualizer
    {
        public static FPSMeter fpsMeter = new();
        public static MMDevice _audioDevice;
        public static Settings InstanceOptions = new();
        public static CancellationTokenSource _captureCTS = new();
        public static CancellationTokenSource _fftCTS = new();
        public static MainWindow MainWin = null;
        public static OptionsWindow OptionsWindow;
        public static AudioDevices AudioDevicesWindow;
        public static bool ChangeBg;
        public static bool ShowBg;
        public static bool UpdateSettings;
        public static List<string> GetOutputDevices()
        {
            var devices = new MMDeviceEnumerator()
                .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .ToDictionary(d => d.FriendlyName, d => d);
            return devices.Keys.ToList();
        }
        public static List<string> GetInputDevices()
        {
            var devices = new MMDeviceEnumerator()
                .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .ToDictionary(d => d.FriendlyName, d => d);
            return devices.Keys.ToList();
        }
        public static List<string> GetAudioApps()
        {
            // Get default device
            using var devEnum = new MMDeviceEnumerator();
            using var devicesC = devEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);

            var sessionsC = devicesC.AudioSessionManager.Sessions;

            var list = new List<string>();
            for (int i = 0; i < sessionsC.Count; i++)
            {
                var s = sessionsC[i];

                float peak = s.AudioMeterInformation.MasterPeakValue;

                uint pid = s.GetProcessID; // exposed by NAudio wrapper

                string name;
                try
                {
                    var p = Process.GetProcessById((int)pid);
                    name = !string.IsNullOrWhiteSpace(s.DisplayName)
                            ? s.DisplayName
                            : (string.IsNullOrWhiteSpace(p.MainWindowTitle)
                                    ? p.ProcessName
                                    : p.MainWindowTitle);
                }
                catch
                {
                    name = $"PID {pid}";
                }

                list.Add($"{pid} - {name}");
            }

            return list;
        }
        public static void SelectDevice(string name)
        {
            var devices = new MMDeviceEnumerator()
                .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .ToDictionary(d => d.FriendlyName, d => d);

            foreach (var choice in devices)
            {
                if (choice.Key == name)
                {
                    _audioDevice = choice.Value;
                    break;
                }
            }

            var indevices = new MMDeviceEnumerator()
                .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .ToDictionary(d => d.FriendlyName, d => d);

            foreach (var choice in indevices)
            {
                if (choice.Key == name)
                {
                    _audioDevice = choice.Value;
                    break;
                }
            }
        }

        // Capture + Processing Windows WASAPI Loopback
        public static WasapiLoopbackCapture _capture;  
        public static WaveFormat _CaptureFormat;       
        public static WasapiCapture _inputCapture;     
        public static ConcurrentQueue<double[]> _frameQueue = new(); // Frame Queue, each frame of frequency ranges to show

        public static CircularBuffer<double> _samples = new CircularBuffer<double>(1);                                     // Samples obtained from WASAPI
        public static System.Numerics.Complex[] _spectrum = new System.Numerics.Complex[InstanceOptions._fftSize];  // Frequency Spectrumn, unbinned
        public static double[] _magnitudes = new double[InstanceOptions._fftSize / 2];                              // Sample magnitudes
        public static FftSharp.Windows.Hanning _window = new();                                                     // Windowing object from FftSharp

        public static double _maxMagnitude = 0;
        public static double _minMagnitude = 0;

        public static string SelectedApp = "";
        public static bool isInput = false;
        public static Thread FFTThread = null;

        // Call this to start showing the visualizer
        public static async void StartCapture(CancellationToken token)
        {
            if (InstanceOptions == null)
            {
                InstanceOptions = new Settings();
            }
            _samples = new CircularBuffer<double>(InstanceOptions._fftSize);

            // Checking if this is an input device or an output device
            if (!isInput)
            {
                var devEnum = new MMDeviceEnumerator(); // Audio Device enumerator

                // If no selected audio device setup with the default
                if (_audioDevice == null)
                {
                    _capture = new WasapiLoopbackCapture();
                    _CaptureFormat = _capture.WaveFormat;
                }
                else // Otherwise we need to setup a new capture using the selected device
                {
                    try
                    {
                        string fuic = _audioDevice.FriendlyName;
                        MMDevice output = string.IsNullOrWhiteSpace(_audioDevice.FriendlyName)
                            ? devEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console)
                            : devEnum.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                                      .First(d => d.FriendlyName == _audioDevice.FriendlyName);
                        _capture = new WasapiLoopbackCapture(output);
                        _CaptureFormat = _capture.WaveFormat;
                    }
                    catch (Exception)
                    {
                        _audioDevice = null;
                        _capture = new WasapiLoopbackCapture();
                        _CaptureFormat = _capture.WaveFormat;
                    }

                }
            }
            else // Same thing as above, except for Microphone inputs (DataFlow.Capture vs DataFlow.Render)
            {
                var devEnum = new MMDeviceEnumerator();
                MMDevice mic = string.IsNullOrWhiteSpace(_audioDevice.FriendlyName)
                    ? devEnum.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console)
                    : devEnum.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                              .First(d => d.FriendlyName == _audioDevice.FriendlyName);
                _inputCapture = new WasapiCapture(mic);
                _CaptureFormat = _inputCapture.WaveFormat;
            }

            _fftCTS.Cancel();
            _fftCTS = new CancellationTokenSource();
            FFTThread = new Thread(() => { ComputeFFT(); });
            FFTThread.Start();

            // Selected app variable used to allow users to select per-process audio 
            // This functionality as of this publication is NOT in NAudio, see "WASAPI Woes" section below.
            if (SelectedApp != "")
            {
                int pid = int.Parse(SelectedApp.Split(" - ")[0]);
                var cap = await WasapiCapture.CreateForProcessCaptureAsync(pid, true);
                _CaptureFormat = cap.WaveFormat;

                cap.DataAvailable += CaptureOnDataAvailable;
                cap.StartRecording();

                while (!token.IsCancellationRequested) { }
                cap.StopRecording();
            }
            else if (!isInput) // Output device 
            {
                _capture.DataAvailable += CaptureOnDataAvailable; // Hook Capture function to event that's called when data is ready
                _capture.StartRecording(); // Start Recording
                while (!token.IsCancellationRequested) { }
                _capture.StopRecording();
            }
            else // Input device
            {
                _inputCapture.DataAvailable += CaptureOnDataAvailable; // Hook Capture function to event that's called when data is ready
                _inputCapture.StartRecording(); // Start Recording
                while (!token.IsCancellationRequested) { }
                _inputCapture.StopRecording();
            }
        }

        private static CircularBuffer<byte> tempBuffer = new CircularBuffer<byte>(1);
        // Audio Capture hook, called whenever _capture has data availible
        private static void CaptureOnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (MainWindow.FVZMode) return;

            try
            {
                var fmt = _CaptureFormat;

                // Calculate buffer size
                int samplesNeeded = InstanceOptions._fftSize / fmt.Channels;
                int bytesPerSample = fmt.BitsPerSample / 8;
                int bufferSizeInBytes = samplesNeeded * fmt.Channels * bytesPerSample;

                if (tempBuffer.Capacity != bufferSizeInBytes)
                {
                    tempBuffer = new CircularBuffer<byte>(bufferSizeInBytes);
                }

                // Add new data to buffer
                for (int i = 0; i < e.BytesRecorded; i++)
                {
                    tempBuffer.PushBack(e.Buffer[i]);
                }

                // Check if we have enough data
                if (tempBuffer.Count() < bufferSizeInBytes)
                {
                    return;
                }

                // Extract bytes from the ring buffer
                byte[] buffer = new byte[bufferSizeInBytes];
                int idx = 0;
                foreach (var b in tempBuffer)
                {
                    buffer[idx++] = b;
                    if (idx >= bufferSizeInBytes) break;
                }

                // Process bytes into samples
                if (fmt.Encoding == WaveFormatEncoding.IeeeFloat ||
                    (fmt.Encoding == WaveFormatEncoding.Extensible &&
                     ((WaveFormatExtensible)fmt).SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT))
                {
                    ProcessFloatData(buffer, bufferSizeInBytes, fmt, samplesNeeded);
                }
                else if (fmt.Encoding == WaveFormatEncoding.Pcm && fmt.BitsPerSample == 16)
                {
                    ProcessPcm16Data(buffer, bufferSizeInBytes, fmt, samplesNeeded);
                }
            }
            catch (Exception ex)
            {

            }
        }
        private static void ProcessFloatData(byte[] buffer, int bytesRecorded, WaveFormat fmt, int needed)
        {
            ReadOnlySpan<float> span = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, bytesRecorded));
            int channels = fmt.Channels;

            if (channels == 2)
            {
                // Stereo
                for (int n = 0; n < needed; n++)
                {
                    int i = n * 2;
                    _samples.PushBack((span[i] + span[i + 1]) * 0.5f);
                }
            }
            else
            {
                // Mono
                for (int n = 0; n < needed - 1; n++)
                {
                    _samples[n] = span[n * channels];
                    _samples.PushBack(span[n * channels]);
                }
            }
        }
        private static void ProcessPcm16Data(byte[] buffer, int bytesRecorded, WaveFormat fmt, int needed)
        {
            // Same byte cast but to shorts instead
            ReadOnlySpan<short> span = MemoryMarshal.Cast<byte, short>(buffer.AsSpan(0, bytesRecorded));
            int channels = fmt.Channels;

            const float scale = 1.0f / 32768f;

            if (channels == 2)
            {
                // Stereo
                for (int n = 0; n < needed; n++)
                {
                    int i = n * 2;
                    _samples.PushBack(((span[i] + span[i + 1]) * 0.5f) * scale);
                }
            }
            else
            {
                // Mono
                for (int n = 0; n < needed; n++)
                {
                    _samples.PushBack(span[n * channels] * scale);
                }
            }
        }

        private static void ComputeFFT()
        {
            int sampleRate = _CaptureFormat.SampleRate;
            while (!_fftCTS.IsCancellationRequested)
            {
                fpsMeter.StartFpsCounter();
                if (!_samples.IsFull || _samples.IsEmpty || MainWindow.FVZMode)
                {
                    fpsMeter.StopFpsCounter();
                    continue;
                }
                try
                {
                    // Apply window function using FFTSharp
                    var slice = _samples.ToArray();
                    _window.ApplyInPlace(slice);

                    // FFT processing using FFTSharp
                    _spectrum = FFT.Forward(slice);
                    _magnitudes = FFT.Magnitude(_spectrum);

                    // Build frame with current scale mode
                    BuildFrame(_magnitudes, sampleRate);
                }
                catch (Exception)
                {

                }
                fpsMeter.StopFpsCounter();
            }
        }

        // Frame Builder entry, routes to one of the builders below
        public static void BuildFrame(double[] mags, int sampleRate)
        {
            switch (InstanceOptions._scaleMode)
            {
                case ScaleMode.Mel:
                    BuildFrameMel(mags, sampleRate);
                    break;
                case ScaleMode.Normalized:
                    BuildFrameNormalized(mags, sampleRate);
                    break;
                default:
                    BuildFrameLogNormalized(mags, sampleRate);
                    break;
            }
        }


        // Frame Builder Part 1, Logarithmic
        private static void BuildFrameNormalized(double[] mags, int sampleRate)
        {
            // Preparing Variables
            double fMin = InstanceOptions._fMin;
            double fMax = InstanceOptions._fMax == -1 ? sampleRate / 2.0 : InstanceOptions._fMax;
            int rows = InstanceOptions._bars;
            var power = new double[rows]; // Stores Accumulated Power sums for each bar
            var binCnt = new double[rows]; // Stores count of FFT bins -> Visualizer bins

            // Logarithmic edges 
            // Human hearing is said to be logarithmic, so we compensate by making the spectrum of frequencies take up more space as you get higher up. 
            // This means at the low end a bar might be 20-40, but higher up will be 1600-3200. By contrast Linear would take the same ammount of fft bins into each visualizer bar, 20-40 at the bottom and 1600-1620 at the top.
            double logMin = Math.Log10(fMin);
            double logMax = Math.Log10(fMax);
            double logStep = (logMax - logMin) / rows;

            // Pre-compute bar edges 
            // Here we are calculating the boundaries between bars, what frequencies do they start/stop at
            // Edges contains the left edge of boundary (the start freq) at i, and the right edge at i+1
            double[] edges = new double[rows + 1];
            for (int r = 0; r <= rows; r++)
            {
                double t = r / (double)rows;
                double easedT = TriEase(t); 
                double logF = logMin + easedT * (logMax - logMin);
                edges[r] = Math.Pow(10, logF);
            }

            // Loop over each FFT bin and for each:
            //  - Find which left and right edge it is between
            //  - Distribute it's power if it sits between/on an edge
            for (int bin = 1; bin < mags.Length; bin++)
            {
                double f = bin * sampleRate / (double)InstanceOptions._fftSize;
                if (f < edges[0] || f >= edges[^1]) continue;

                // Locate left edge index k so that edges[k] <= f < edges[k+1]
                int k = Array.BinarySearch(edges, f);
                if (k < 0) k = ~k - 1; // BinarySearch peculiarity

                double l = edges[k];
                double rEdge = edges[k + 1];
                double t = (f - l) / (rEdge - l); // 0->1 position between the two edges

                // distribute energy into the two adjacent bars
                double e = mags[bin] * mags[bin]; // energy to distribute

                power[k] += e * (1 - t); // left bar distribution
                binCnt[k] += (1 - t);

                if (k + 1 < rows) // right bar distribution (still inside range)
                {
                    power[k + 1] += e * t;
                    binCnt[k + 1] += t;
                }
            }


            // Clamp in range + Normalize
            // - Normalize the power by the ammount of FFT bins that contributed to a specific bar.
            // - Convert Amplitude to RMS to show the "average energy" of the bins that were added into a bar
            // - Clamp in the range of _dbRange where 0 is _dbFloor and 1 is _dbFloor + _dbRange
            var frame = new double[rows];
            for (int r = 0; r < rows; r++)
            {
                if (binCnt[r] == 0) { frame[r] = 0; continue; }

                // power -> amplitude -> dB
                double rms = Math.Sqrt(power[r] / binCnt[r]) * Math.Sqrt(binCnt[r]);
                double db = 20 * Math.Log10(rms + 1e-20);

                // Normalize the DB within the range
                double topDb = InstanceOptions._dbFloor + InstanceOptions._dbRange;
                double dbNorm = Math.Clamp((db - InstanceOptions._dbFloor) / InstanceOptions._dbRange, 0, 1);

                // Apply gain compensation as frequency increases (so high ends don't get washed)
                double t = r / (double)(rows - 1);
                //double comp = double.Lerp(1.2, 1.1, t);
                //dbNorm /= comp;

                // Use Soft Gate to gate out noise and an exponential smoothstep to smooth out the missing cliff
                frame[r] = Math.Clamp(ApplySoftGate(dbNorm, t), 0, 1);

            }

            // At this point we have values usable in our visualizer and format, 0.0->1.0 where 0 is no sound in(/around) that frequency and 1 is very loud sound in that frequency (clamped max, but can technically go "out of bounds")

            // Last part here is smoothing, we take each bar and "blur" it to the bars on either side
            var smoothed = new double[rows];
            for (int r = 0; r < rows; r++)
            {
                double sum = 0;
                int cnt = 0;
                // For -Smoothness to +Smoothness (2 smooth would be -2 -> 2)
                for (int s = -InstanceOptions._smooth; s <= InstanceOptions._smooth; s++)
                {
                    // If row +/- smooth is not negative and not more than the rows we have
                    if (r + s >= 0 && r + s < rows)
                    {
                        // Take that row at add it to our sum (and increase our total)
                        sum += frame[r + s];
                        cnt++;
                    }
                }

                // Avg the sum
                smoothed[r] = (sum / cnt);
            }

            // Now this frame is done and ready to be shown by the visualizer, so it's chucked into a ConcurrentQueue
            // I also check to make sure the UI isn't lagging too far behind by not letting the buffer get overfilled
            _frameQueue.Enqueue(smoothed);
            while (_frameQueue.Count > 13) _frameQueue.TryDequeue(out _);
        }
        static double TriEase(double t, double lowMid = 0.3, double highMid = 0.7)
        {
            lowMid = 0.40;
            highMid = 0.95;
            var transitionWidth = 0.02; // Smoothness value between low / mid / high

            if (t <= 0.0) return 0.0;
            if (t >= 1.0) return 1.0;

            if (t < lowMid - transitionWidth)
            {
                // Pure low compression
                double x = t / lowMid;
                return 0.5 * Math.Pow(x, 0.5);
            }
            else if (t < lowMid + transitionWidth)
            {
                // Smooth transition from low to mid
                double t1 = lowMid - transitionWidth;
                double t2 = lowMid + transitionWidth;

                // Values at transition points
                double v1 = 0.5 * Math.Pow(t1 / lowMid, 0.5);
                double v2 = 0.5 + ((t2 - lowMid) / (highMid - lowMid)) * 0.4;

                // Derivatives at transition points  
                double d1 = 0.5 * 0.5 * Math.Pow(t1 / lowMid, -0.5) / lowMid;
                double d2 = 0.4 / (highMid - lowMid);

                return CubicHermite(t, t1, v1, d1, t2, v2, d2);
            }
            else if (t < highMid - transitionWidth)
            {
                // Pure mid stretch
                double x = (t - lowMid) / (highMid - lowMid);
                return 0.5 + x * 0.4;
            }
            else if (t < highMid + transitionWidth)
            {
                // Smooth transition from mid to high
                double t1 = highMid - transitionWidth;
                double t2 = highMid + transitionWidth;

                // Values at transition points
                double v1 = 0.5 + ((t1 - lowMid) / (highMid - lowMid)) * 0.4;
                double v2 = 0.9 + 0.1 * Math.Pow((t2 - highMid) / (1 - highMid), 0.9);

                // Derivatives at transition points
                double d1 = 0.4 / (highMid - lowMid);
                double d2 = 0.1 * 0.9 * Math.Pow((t2 - highMid) / (1 - highMid), -0.1) / (1 - highMid);

                return CubicHermite(t, t1, v1, d1, t2, v2, d2);
            }
            else
            {
                // Pure high compression  
                double x = (t - highMid) / (1 - highMid);
                return 0.9 + 0.1 * Math.Pow(x, 0.9);
            }
        }
        static double CubicHermite(double t, double t0, double p0, double m0, double t1, double p1, double m1)
        {
            double dt = t1 - t0;
            double h = (t - t0) / dt;
            double h2 = h * h;
            double h3 = h2 * h;

            double h00 = 2 * h3 - 3 * h2 + 1;
            double h10 = h3 - 2 * h2 + h;
            double h01 = -2 * h3 + 3 * h2;
            double h11 = h3 - h2;

            return h00 * p0 + h10 * dt * m0 + h01 * p1 + h11 * dt * m1;
        }

        static double ApplySoftGate(double x, double t)
        {
            double center = 0.4;
            double steep = 15.0;
            return 1.0 / (1.0 + Math.Exp(-steep * (x - center)));
        }


        // Frame Builder Part 2, Melectric Boogaloo
        private static void BuildFrameMel(double[] mags, int sampleRate)
        {
            double fMin = InstanceOptions._fMin;
            double fMax = InstanceOptions._fMax == -1 ? sampleRate / 2.0 : InstanceOptions._fMax;

            // Mel Funcs
            static double Mel(double hz) => 2595.0 * Math.Log10(1.0 + hz / 700.0);
            static double InvMel(double mel) => 700.0 * (Math.Pow(10.0, mel / 2595.0) - 1.0);

            int rows = InstanceOptions._bars;
            double melMin = Mel(fMin);
            double melMax = Mel(fMax);

            // One extra point on either side so we have enough rows for triangular filters
            double[] melEdges = new double[rows + 2];
            double melStep = (melMax - melMin) / (rows + 1);
            for (int i = 0; i < melEdges.Length; i++)
            {
                melEdges[i] = InvMel(melMin + i * melStep);
            }

            // Accumulate power through the filters
            var power = new double[rows];
            var binCnt = new int[rows]; // used for RMS later

            for (int bin = 1; bin < mags.Length; bin++)
            {
                double freq = bin * sampleRate / (double)InstanceOptions._fftSize;
                if (freq < fMin || freq >= fMax) continue;

                // Which two edges sandwich this bin?
                int k = Array.FindLastIndex(melEdges, e => e <= freq);
                if (k <= 0 || k >= melEdges.Length - 1) continue;

                double left = melEdges[k - 1];
                double center = melEdges[k];
                double right = melEdges[k + 1];

                // triangular weight for this bin in the Mel filter "k-1"
                double weight = freq < center
                                ? (freq - left) / (center - left)
                                : (right - freq) / (right - center);

                if (weight < 0) continue; // outside current triangle

                int row = k - 1; // rows correspond to triangles
                power[row] += (mags[bin] * mags[bin]) * weight;
                binCnt[row] += 1; // for later RMS normalisation
            }

            // Convert to dB-normalised “height”
            var frame = new double[rows];
            for (int r = 0; r < rows; r++)
            {
                if (binCnt[r] == 0) { frame[r] = 0; continue; }

                double rms = Math.Sqrt(power[r] / binCnt[r]) * Math.Sqrt(binCnt[r]);
                double db = 20 * Math.Log10(rms + 1e-20);
                double dbNorm = Math.Clamp((db - InstanceOptions._dbFloor) / InstanceOptions._dbRange, 0, 1);


                frame[r] = dbNorm;
            }

            // Octave smoothing, less needed here than with log10
            var smoothed = new double[rows];
            for (int r = 0; r < rows; r++)
            {
                double sum = 0; int cnt = 0;
                for (int s = -InstanceOptions._smooth; s <= InstanceOptions._smooth; s++)
                    if (r + s >= 0 && r + s < rows) { sum += frame[r + s]; cnt++; }
                smoothed[r] = (sum / cnt);
            }

            _frameQueue.Enqueue(smoothed);
            while (_frameQueue.Count > 3) _frameQueue.TryDequeue(out _);
        }


        // Frame Builder Part 3, Log10
        private static void BuildFrameLogNormalized(double[] mags, int sampleRate)
        {
            // Preparing Variables
            double fMin = InstanceOptions._fMin;
            double fMax = InstanceOptions._fMax == -1 ? sampleRate / 2.0 : InstanceOptions._fMax;
            int rows = InstanceOptions._bars;
            var power = new double[rows]; // Stores Accumulated Power sums for each bar
            var binCnt = new double[rows]; // Stores count of FFT bins -> Visualizer bins

            // Logarithmic edges 
            // Human hearing is said to be logarithmic, so we compensate by making the spectrum of frequencies take up more space as you get higher up. 
            // This means at the low end a bar might be 20-40, but higher up will be 1600-3200. By contrast Linear would take the same ammount of fft bins into each visualizer bar, 20-40 at the bottom and 1600-1620 at the top.
            double logMin = Math.Log10(fMin);
            double logMax = Math.Log10(fMax);
            double logStep = (logMax - logMin) / rows;

            // Pre-compute bar edges 
            // Here we are calculating the boundaries between bars, what frequencies do they start/stop at
            // Edges contains the left edge of boundary (the start freq) at i, and the right edge at i+1
            double[] edges = new double[rows + 1];
            for (int r = 0; r <= rows; r++)
            {
                edges[r] = Math.Pow(10, logMin + r * logStep);
            }

            // Loop over each FFT bin and for each:
            //  - Find which left and right edge it is between
            //  - Distribute it's power if it sits between/on an edge
            for (int bin = 1; bin < mags.Length; bin++)
            {
                double f = bin * sampleRate / (double)InstanceOptions._fftSize;
                if (f < edges[0] || f >= edges[^1]) continue;

                // Locate left edge index k so that edges[k] <= f < edges[k+1]
                int k = Array.BinarySearch(edges, f);
                if (k < 0) k = ~k - 1; // BinarySearch peculiarity

                double l = edges[k];
                double rEdge = edges[k + 1];
                double t = (f - l) / (rEdge - l); // 0->1 position between the two edges

                // distribute energy into the two adjacent bars
                double e = mags[bin] * mags[bin]; // energy to distribute

                power[k] += e * (1 - t); // left bar distribution
                binCnt[k] += (1 - t);

                if (k + 1 < rows) // right bar distribution (still inside range)
                {
                    power[k + 1] += e * t;
                    binCnt[k + 1] += t;
                }
            }


            // Clamp in range + power -> RMS
            // - Convert Amplitude to RMS to show the "average energy" of the bins that were added into a bar
            // - Clamp in the range of _dbRange where 0 is _dbFloor and 1 is _dbFloor + _dbRange
            var frame = new double[rows];
            for (int r = 0; r < rows; r++)
            {
                if (binCnt[r] == 0) { frame[r] = 0; continue; }

                // power -> amplitude -> dB
                double rms = Math.Sqrt(power[r] / binCnt[r]) * Math.Sqrt(binCnt[r]);
                double db = 20 * Math.Log10(rms + 1e-20);

                double topDb = InstanceOptions._dbFloor + InstanceOptions._dbRange;
                double dbNorm = Math.Clamp((db - InstanceOptions._dbFloor) / InstanceOptions._dbRange, 0, 1);
                frame[r] = dbNorm;
            }

            // At this point we have values usable in our visualizer and format, 0.0->1.0 where 0 is no sound in(/around) that frequency and 1 is very loud sound in that frequency (clamped max, but can technically go "out of bounds")

            // Last part here is smoothing, we take each bar and "blur" it to the bars on either side
            var smoothed = new double[rows];
            for (int r = 0; r < rows; r++)
            {
                double sum = 0;
                int cnt = 0;
                // For -Smoothness to +Smoothness (2 smooth would be -2 -> 2)
                for (int s = -InstanceOptions._smooth; s <= InstanceOptions._smooth; s++)
                {
                    // If row +/- smooth is not negative and not more than the rows we have
                    if (r + s >= 0 && r + s < rows)
                    {
                        // Take that row at add it to our sum (and increase our total)
                        sum += frame[r + s];
                        cnt++;
                    }
                }

                // Avg the sum
                smoothed[r] = (sum / cnt);
            }

            // Now this frame is done and ready to be shown by the visualizer, so it's chucked into a ConcurrentQueue
            // I also check to make sure the UI isn't lagging too far behind by not letting the buffer get overfilled
            _frameQueue.Enqueue(smoothed);
            while (_frameQueue.Count > 13) _frameQueue.TryDequeue(out _);
        }



        public static List<double> GetFrame()
        {
            if (!_frameQueue.TryDequeue(out var frame))
            {
                return null;
            }

            try
            {
                return new List<double>(frame);
            }
            catch (Exception)
            {
                _frameQueue.Clear();
                return null;
            }
        }
        public static Color GetGradientColor(Color[] colors, double step)
        {
            if (colors == null || colors.Length < 2)
                throw new ArgumentException("At least two colors are required to create a gradient.");

            if (step < 0.0 || step > 1.0)
                step = Math.Clamp(step, 0.0, 1.0); // Handle out of range more gracefully

            // More efficient calculation
            int segmentCount = colors.Length - 1;
            double scaledStep = step * segmentCount;
            int lowerIndex = (int)Math.Floor(scaledStep);
            int upperIndex = Math.Min(lowerIndex + 1, colors.Length - 1);

            double localStep = scaledStep - lowerIndex;

            // Get the two colors to blend
            Color lowerColor = colors[lowerIndex];
            Color upperColor = colors[upperIndex];

            // More efficient blending using byte arithmetic
            byte a = (byte)(lowerColor.A + (upperColor.A - lowerColor.A) * localStep);
            byte r = (byte)(lowerColor.R + (upperColor.R - lowerColor.R) * localStep);
            byte g = (byte)(lowerColor.G + (upperColor.G - lowerColor.G) * localStep);
            byte b = (byte)(lowerColor.B + (upperColor.B - lowerColor.B) * localStep);

            return Color.FromArgb(a, r, g, b);
        }
    }
}