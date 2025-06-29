using H.NotifyIcon.Core;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FreqFreak
{
    public partial class MainWindow : Window, IDisposable
    {
        // The code for this app is a quite messy and it could be nice to split it up a little more sometime
        // But it works as is and I do not feel like breaking everything trying to rearange it
        //
        // Structure Notes:
        // MainWindow is the main UI and entry point for the app.
        // It handles the main rendering loop for bars and peaks as well as loops for color animation.
        // Because of this lots of color helpers are in here, alongside funcs for adding, removing, and adjusting bars as needed for the visualization.
        // The tray icon is also added and managed from within here.
        //
        // Visualizer is a static class that handles the audio capture and frame generation.
        // The visualizer has it's own loop that continuously asks the WASAPI for audio and then builds frames off of it.
        // Every time we are done showing a frame the MainWindow asks the Visualizer if it's got new frames for us to show, and then dequeues that frame and displays it
        //
        // FVZPlayer is the class that handles playing audio files when using the FVZ Tool.
        // It also stores the FrequencyVisualizer object and exposes a way to get it's current frame.
        // The actual FV object is decoded / generated in FVZWindow.xaml.cs, in the corroponding button's action
        // The audio file is also loaded from there.
        // 
        // The OptionsWindow just fasilitates a way to change settings within Visualizer.InstanceOptions, or other global / static properties used by the app.
        // It also contains the FPS Meter, and buttons for opening the other windows
        //
        // Lastly, the AudioDevices window shows the user's active output/input devices + apps actively playing audio.
        // Selecting them there sets the variables used by the Visualizer to capture audio from the correct source.



        private readonly CancellationTokenSource _cts = new();
        private static readonly CancellationTokenSource _clrCts = new();
        public static FPSMeter displayFpsMeter = new();
        public static Color _TrayIconColor { get; set; }
        public static bool FVZMode { get; set; }
        public static FVZWindow FVZWindowHandle { get; set; } = new FVZWindow();
        public static FVZPlayer FVZPlayer { get; set; } = new FVZPlayer();

        private Rectangle[] _bars = Array.Empty<Rectangle>();
        private Rectangle[]? _peakBars; // peaks for top & bottom modes
        private Rectangle[]? _peakBarsLow; // centered low peaks
        private Rectangle[]? _peakBarsHigh; // centered high peaks

        private double[] _peaks = Array.Empty<double>();
        private readonly object _peakLock = new(); 
        private readonly Task _peakDecayTask;
        private readonly Task _ColorMoveTask;

        private static readonly Random _rng = new();

        private IntPtr _hwnd;
        private static TrayIconWithContextMenu? _trayIcon;
        private static System.Drawing.Icon? _icon;
        private static SolidColorBrush _color1 = new(); // Bars 1
        private static SolidColorBrush _color2 = new(); // Bars 2
        private static SolidColorBrush _color3 = new(); // Peaks 1
        private static SolidColorBrush _color4 = new(); // Peaks 2
        private static LinearGradientBrush _gradient = new();
        private static LinearGradientBrush _peakGradient = new();
        private static bool _failure = false;

        double max = 0; // The maximum amplitude seen recently (decreases with DecaySpeed). Used for gradient by height

        // Window + App setup
        public MainWindow()
        {
            Visualizer.MainWin = this;
            Visualizer.InstanceOptions.SetDefaults();
            InitializeComponent();

            Loaded += (_, __) => _hwnd = new WindowInteropHelper(this).Handle;
            MouseLeftButtonDown += (_, __) => DragMove();

            Random rand = new(DateTime.Now.TimeOfDay.Nanoseconds);

            _TrayIconColor = Color.FromRgb((byte)rand.Next(100, 255), (byte)rand.Next(100, 255), (byte)rand.Next(100, 255));

            ConfigureWindow();
            CreateTrayIcon();

            Visualizer.OptionsWindow = new OptionsWindow();
            Visualizer.OptionsWindow.Show();

            // initial geometry
            ResizeBars();

            // Hook UI thread render loop
            CompositionTarget.Rendering += OnRender;

            // Background audio capture task
            _ = Task.Run(() => Visualizer.StartCapture(Visualizer._captureCTS.Token), Visualizer._captureCTS.Token);
            
            // Background fvz player task
            //_ = Task.Run(() => FVZPlayer.Start(), Visualizer._captureCTS.Token);

            // Peak decay task
            _peakDecayTask = Task.Run(PeakDecayLoop, _cts.Token);

            _ColorMoveTask = Task.Run(ManageColorMove, _clrCts.Token);
        }
        private void ConfigureWindow()
        {
            Title = "Visualizer Overlay";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
        }

        // TrayIcon Management
        public static void HueShiftIcon()
        {
            _icon.Dispose();
            using var iconStream = GetStream("FreqIcon.ico");
            _icon = new System.Drawing.Icon(iconStream);
            var TrayIconBMP = _icon.ToBitmap();

            System.Drawing.Color sourceColor = TrayIconBMP.GetPixel(12, 3);
            float sourceHue = sourceColor.GetHue();
            System.Drawing.Color targetColor = System.Drawing.Color.FromArgb(_TrayIconColor.A, _TrayIconColor.R, _TrayIconColor.G, _TrayIconColor.B);
            float targetHue = targetColor.GetHue();

            float hueShift = (targetHue - sourceHue + 360) % 360;


            for (int y = 0; y < TrayIconBMP.Height; y++)
            {
                for (int x = 0; x < TrayIconBMP.Width; x++)
                {
                    System.Drawing.Color original = TrayIconBMP.GetPixel(x, y);
                    System.Drawing.Color shifted = ShiftHue(original, hueShift);
                    TrayIconBMP.SetPixel(x, y, shifted);
                }
            }
            try
            {
                var customIcon = System.Drawing.Icon.FromHandle(TrayIconBMP.GetHicon());
                _trayIcon.UpdateIcon(customIcon.Handle);
            }
            catch (Exception)
            {
                _trayIcon?.Dispose();
                _failure = true;
            }
        }
        private void CreateTrayIcon()
        {
            using var iconStream = GetStream("FreqIcon.ico");
            _icon = new System.Drawing.Icon(iconStream);

            var TrayIconBMP = _icon.ToBitmap();

            System.Drawing.Color sourceColor = TrayIconBMP.GetPixel(12, 3);
            float sourceHue = sourceColor.GetHue();
            System.Drawing.Color targetColor = System.Drawing.Color.FromArgb(_TrayIconColor.A, _TrayIconColor.R, _TrayIconColor.G, _TrayIconColor.B);
            float targetHue = targetColor.GetHue();

            float hueShift = (targetHue - sourceHue + 360) % 360;


            for (int y = 0; y < TrayIconBMP.Height; y++)
            {
                for (int x = 0; x < TrayIconBMP.Width; x++)
                {
                    System.Drawing.Color original = TrayIconBMP.GetPixel(x, y);
                    System.Drawing.Color shifted = ShiftHue(original, hueShift);
                    TrayIconBMP.SetPixel(x, y, shifted);
                }
            }
            var customIcon = System.Drawing.Icon.FromHandle(TrayIconBMP.GetHicon());

            _trayIcon = new TrayIconWithContextMenu
            {
                Icon = customIcon.Handle,
                ToolTip = "FreqFreak",
                ContextMenu = new PopupMenu
                {
                    Items =
                    {
                        new PopupMenuItem("Settings", (_, _) => Dispatcher.Invoke(() =>
                        {
                            if (Visualizer.OptionsWindow == null || !Visualizer.OptionsWindow.IsVisible)
                                (Visualizer.OptionsWindow ??= new OptionsWindow()).Show();
                            Visualizer.ShowBg = true;
                            Visualizer.ChangeBg = true;
                        })),
                        new PopupMenuItem("Exit", (_, _) =>{
                            _trayIcon.Dispose();
                            Visualizer._captureCTS.Cancel();
                            _cts.Cancel();
                            _clrCts.Cancel();
                            Environment.Exit(0);
                            })
                    }
                }
            };
            _trayIcon.UpdateName(GenerateRandomString());

            var id = TrayIcon.CreateUniqueGuidFromString("FreqFreak");

            _trayIcon.Create();
        }
        public static Stream GetStream(string fileName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resource = assembly.GetManifestResourceNames()
                                   .SingleOrDefault(n => n.EndsWith($".{fileName}", StringComparison.OrdinalIgnoreCase));
            return resource != null
                ? assembly.GetManifestResourceStream(resource)!
                : throw new ArgumentException($"Embedded resource '{fileName}' not found.");
        }

        // Color helper funcs
        // Color is stupid, you'd think the system color objects would have better support for moving them around
        public static Color ColorFromHSV(double hue, double saturation, double value, byte alpha = 255)
        {
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            double f = hue / 60 - Math.Floor(hue / 60);

            value = value * 255;
            byte v = Convert.ToByte(value);
            byte p = Convert.ToByte(value * (1 - saturation));
            byte q = Convert.ToByte(value * (1 - f * saturation));
            byte t = Convert.ToByte(value * (1 - (1 - f) * saturation));

            switch (hi)
            {
                case 0: return Color.FromArgb(alpha, v, t, p);
                case 1: return Color.FromArgb(alpha, q, v, p);
                case 2: return Color.FromArgb(alpha, p, v, t);
                case 3: return Color.FromArgb(alpha, p, q, v);
                case 4: return Color.FromArgb(alpha, t, p, v);
                default: return Color.FromArgb(alpha, v, p, q);
            }
        }
        public static System.Drawing.Color ShiftHue(System.Drawing.Color color, float hueShift)
        {
            // Convert to HSV

            RGBtoHSV(color, out double hue, out double sat, out double val);

            // Shift hue
            hue = (hue + hueShift) % 360;
            if (hue < 0) hue += 360;

            // Convert back to Color
            var clr = ColorFromHSV(hue, sat, val, color.A);
            return System.Drawing.Color.FromArgb(clr.A, clr.R, clr.G, clr.B);
        }
        Color LerpColor(Color firstFloat, Color secondFloat, float by)
        {
            return firstFloat * (1 - by) + secondFloat * by;
        }
        private static LinearGradientBrush GetVerticalGradientBrush(Color clr1, Color clr2)
        {
            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            gradient.GradientStops.Add(new GradientStop(clr1, 0));
            gradient.GradientStops.Add(new GradientStop(clr2, 1));
            return gradient;
        }

        public static Color GetRandomColor() => Color.FromArgb(255, (byte)_rng.Next(256), (byte)_rng.Next(256), (byte)_rng.Next(256));
        public static void RGBtoHSV(System.Drawing.Color color, out double hue, out double saturation, out double value)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));

            hue = color.GetHue();
            value = max;

            saturation = (max == 0) ? 0 : (max - min) / max;
        }

        // Decays the peak bars at the decay speed
        private void PeakDecayLoop()
        {
            var token = _cts.Token;
            (double pos, int canFall)[] peakHold = new (double, int canFall)[_peaks.Length];
            while (!token.IsCancellationRequested)
            {
                if(peakHold.Length != _peaks.Length)
                {
                    peakHold = new (double, int canFall)[_peaks.Length];
                }
                double decay = Visualizer.InstanceOptions._peakDecay;
                double curMax = 0;
                lock (_peakLock)
                {
                    for (int i = 0; i < _peaks.Length; i++)
                    {
                        if (peakHold[i].pos >= _peaks[i])
                        {
                            peakHold[i].canFall++;
                            if(peakHold[i].canFall >= Visualizer.InstanceOptions._peakHold)
                            {
                                _peaks[i] -= decay;
                                peakHold[i].pos = _peaks[i];
                            }
                        }
                        else
                        {
                            peakHold[i].pos = _peaks[i];
                            peakHold[i].canFall = 0;
                        }
                        //_peaks[i] -= decay;

                        if(curMax < _peaks[i])
                        {
                            curMax = _peaks[i];
                        }
                    }
                }
                max = curMax;
                Thread.Sleep(16);
                if (_failure)
                {
                    CreateTrayIcon();
                    _failure = false;
                }
            }
        }

        // Render funcs
        private void OnRender(object? sender, EventArgs e)
        {
            displayFpsMeter.StartFpsCounter();
            if (_cts.IsCancellationRequested) return;

            UpdateBackgroundIfNeeded();

            // Get frame data efficiently, avoid allocating unless necessary
            List<double>? frame = null;
            if (FVZMode)
            {
                var frameArr = FVZPlayer.GetCurrentFrame();
                if (frameArr != null)
                    frame = new List<double>(frameArr);
            }
            else
            {
                frame = Visualizer.GetFrame();
            }

            if (frame == null) return;

            if (Visualizer.InstanceOptions._invertSpectrum)
                frame.Reverse();

            if (frame.Count != _bars.Length || Visualizer.UpdateSettings)
                ResizeBars();

            try
            {
                UpdateBars(frame);
                //DecayPeaks();
                UpdatePeakRectangles();
            }
            catch (Exception)
            {
                // This is almost always because a setting has changed mid frame, and errors will be resolved by next run
            }
            displayFpsMeter.StopFpsCounter();
        }

        private void UpdateBars(List<double> frame)
        {
            FPSMeter.Text = $"{displayFpsMeter.GetFpsStats()}\n{Visualizer.fpsMeter.GetFpsStats()}";
            var opts = Visualizer.InstanceOptions;
            double min = opts._minHeight;
            int pos = opts._Position;
            double cx = Width / 2;
            double cy = Height / 2;
            var barWidth = opts._barWidth;
            var barCount = opts._bars;
            var barGap = opts._barGap;
            double radius = ((barWidth + barGap) * barCount) / (2 * Math.PI);

            // Take frames from 0-1 to 0-Bar Height
            double localMax = 0;
            int barLen = Math.Min(frame.Count, _bars.Length);

            for (int i = 0; i < barLen; i++)
            {
                frame[i] *= opts._height;
                if (frame[i] > localMax) localMax = frame[i];
            }

            // Clamp frame if bigger than window
            // This really only happens when playing an FVZ file and ajusting settings before starting a new generation
            if (frame.Count > _bars.Length)
                frame.RemoveRange(_bars.Length, frame.Count - _bars.Length);

            // Draw/update each bar
            for (int i = 0; i < barLen; i++)
            {
                var value = frame[i] + min;
                var rect = _bars[i];

                double current = rect.Height;

                // Get attack/decay speed depending on direction and dampen
                double speed = (value > current) ? opts._attackSpeed : opts._decaySpeed;
                current += (value - current) * speed;

                // Clamp value
                if (current < 0) current = 0;
                if (current > opts._height) current = opts._height;

                rect.Height = current;

                // Where go
                switch (pos)
                {
                    case 0: // Bottom
                        Canvas.SetBottom(rect, 0);
                        Canvas.SetTop(rect, double.NaN);
                        break;
                    case 1: // Center
                        Canvas.SetBottom(rect, (VisCanvas.ActualHeight / 2) - (current / 2));
                        Canvas.SetTop(rect, double.NaN);
                        break;
                    case 2: // Top
                        Canvas.SetTop(rect, 0);
                        Canvas.SetBottom(rect, double.NaN);
                        break;
                    case 3: // Outer Circle
                    case 4: // Inner Circle
                            // Unroll repeated code using variables for both circle types
                        double angleOffset = -Math.PI / 2;
                        double angle = i * (2 * Math.PI / barCount) + angleOffset;
                        double x = cx + radius * Math.Cos(angle);
                        double y = cy + radius * Math.Sin(angle);
                        Canvas.SetLeft(rect, x - barWidth / 2);
                        if (pos == 3)
                        {
                            Canvas.SetTop(rect, y);
                            rect.RenderTransform = new RotateTransform(angle * 180 / Math.PI - 90, barWidth / 2, 0);
                        }
                        else
                        {
                            Canvas.SetTop(rect, y - current);
                            rect.RenderTransform = new RotateTransform(angle * 180 / Math.PI - 90, barWidth / 2, current);
                        }
                        break;
                }

                SetBarColour(rect, i, barLen, current, localMax);

                // Peak tracking
                if (_peaks[i] < current) _peaks[i] = current;
            }

            // Store new max
            max = localMax;
        }

        private void UpdatePeakRectangles()
        {
            var opts = Visualizer.InstanceOptions;
            if (!opts._showPeaks)
            {
                RemovePeakBars();
                return;
            }

            ShowPeakBars();

            double min = opts._minHeight;
            int pos = opts._Position;
            double cx = Width / 2;
            double cy = Height / 2;
            var barWidth = opts._barWidth;
            var barCount = opts._bars;
            var barGap = opts._barGap;
            double radius = ((barWidth + barGap) * barCount) / (2 * Math.PI);

            switch (pos)
            {
                case 0: // Bottom
                case 2: // Top
                case 3: // Outer Circle
                case 4: // Inner Circle
                    var useTop = (pos == 2);
                    var isCircle = (pos == 3 || pos == 4);
                    var barArr = _peakBars;
                    if (barArr == null) return;
                    for (int i = 0; i < barArr.Length; i++)
                    {
                        var peak = barArr[i];

                        SetBarColour(peak, i, barCount, _peaks[i], max, true);
                        
                        // Normal calcs
                        if (!isCircle)
                        {
                            Canvas.SetLeft(peak, i * (barWidth + barGap) + 1);
                            if (useTop)
                                Canvas.SetTop(peak, _peaks[i]);
                            else
                                Canvas.SetBottom(peak, _peaks[i]);
                        }
                        else // Round boy calcs
                        {
                            double angleOffset = -Math.PI / 2;
                            double angle = i * (2 * Math.PI / barCount) + angleOffset;
                            double x = cx + radius * Math.Cos(angle);
                            double y = cy + radius * Math.Sin(angle);
                            Canvas.SetLeft(peak, x - barWidth / 2);
                            if (pos == 3)
                            {
                                Canvas.SetTop(peak, y + _peaks[i]);
                                peak.RenderTransform = new RotateTransform(angle * 180 / Math.PI - 90, barWidth / 2, -_peaks[i]);
                            }
                            else // pos == 4
                            {
                                Canvas.SetTop(peak, y - _peaks[i]);
                                peak.RenderTransform = new RotateTransform(angle * 180 / Math.PI - 90, barWidth / 2, _peaks[i]);
                            }
                        }
                    }
                    break;

                case 1: // Centered (two peaks per bar)
                    if (_peakBarsLow == null || _peakBarsHigh == null) return;
                    for (int i = 0; i < _peakBarsLow.Length; i++)
                    {
                        var pLow = _peakBarsLow[i];
                        var pHigh = _peakBarsHigh[i];

                        SetBarColour(pLow, i, barCount, _peaks[i], max, true);
                        SetBarColour(pHigh, i, barCount, _peaks[i], max, true, true);

                        double left = i * (barWidth + barGap) + 1;
                        Canvas.SetLeft(pLow, left);
                        Canvas.SetLeft(pHigh, left);

                        Canvas.SetBottom(pLow, (VisCanvas.ActualHeight / 2) - (_peaks[i] / 2) - (min / 2) + (min / 2));
                        Canvas.SetBottom(pHigh, (VisCanvas.ActualHeight / 2) + (_peaks[i] / 2) + (min / 2) - (min / 2) - 2);
                    }
                    break;
            }
        }
        private void CreateBars()
        {
            var opts = Visualizer.InstanceOptions;
            int count = opts._bars;

            _bars = new Rectangle[count];
            var old = _peaks;
            _peaks = new double[count];
            Array.Copy(old, _peaks, Math.Min(old.Length, count));

            VisCanvas.Children.Clear();

            for (int i = 0; i < count; i++)
            {
                var rect = new Rectangle
                {
                    Width = opts._barWidth,
                    Height = opts._minHeight,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(rect, i * (opts._barWidth + opts._barGap) + 1);
                VisCanvas.Children.Add(rect);
                _bars[i] = rect;
            }
        }
        private void ShowPeakBars()
        {
            var opts = Visualizer.InstanceOptions;
            int count = opts._bars;

            if (!opts._showPeaks)
            {
                RemovePeakBars();
                return;
            }

            var peakColour = _color3;

            switch (opts._Position)
            {
                case 0: // bottom
                    if (_peakBars?.Length == count) return; 
                    RemovePeakBars();
                    _peakBars = new Rectangle[count];
                    for (int i = 0; i < count; i++)
                    {
                        var peak = new Rectangle
                        {
                            Width = opts._barWidth,
                            Height = 3,
                            Fill = peakColour,
                            IsHitTestVisible = false
                        };
                        VisCanvas.Children.Add(peak);
                        _peakBars[i] = peak;
                    }
                    break;
                case 2: // top
                    if (_peakBars?.Length == count) return; 
                    RemovePeakBars();
                    _peakBars = new Rectangle[count];
                    for (int i = 0; i < count; i++)
                    {
                        var peak = new Rectangle
                        {
                            Width = opts._barWidth,
                            Height = 3,
                            Fill = peakColour,
                            IsHitTestVisible = false
                        };
                        VisCanvas.Children.Add(peak);
                        _peakBars[i] = peak;
                    }
                    break;

                case 1: // centered (needs two peaks per bar)
                    if (_peakBarsLow?.Length == count && _peakBarsHigh?.Length == count) return;
                    RemovePeakBars();
                    _peakBarsLow = new Rectangle[count];
                    _peakBarsHigh = new Rectangle[count];
                    for (int i = 0; i < count; i++)
                    {
                        var pLow = new Rectangle
                        {
                            Width = opts._barWidth,
                            Height = 3,
                            Fill = peakColour,
                            IsHitTestVisible = false
                        };
                        var pHigh = new Rectangle
                        {
                            Width = opts._barWidth,
                            Height = 3,
                            Fill = peakColour,
                            IsHitTestVisible = false
                        };
                        VisCanvas.Children.Add(pLow);
                        VisCanvas.Children.Add(pHigh);
                        _peakBarsLow[i] = pLow;
                        _peakBarsHigh[i] = pHigh;
                    }
                    break;
                case 3: // Outer Circle 
                    if (_peakBars?.Length == count) return;
                    RemovePeakBars();
                    _peakBars = new Rectangle[count];
                    for (int i = 0; i < count; i++)
                    {
                        var peak = new Rectangle
                        {
                            Width = opts._barWidth,
                            Height = 2,
                            Fill = peakColour,
                            IsHitTestVisible = false
                        };
                        VisCanvas.Children.Add(peak);
                        _peakBars[i] = peak;
                    }
                    break;
                case 4: // Inner Circle
                    if (_peakBars?.Length == count) return;
                    RemovePeakBars();
                    _peakBars = new Rectangle[count];
                    for (int i = 0; i < count; i++)
                    {
                        var peak = new Rectangle
                        {
                            Width = opts._barWidth,
                            Height = 2,
                            Fill = peakColour,
                            IsHitTestVisible = false
                        };
                        VisCanvas.Children.Add(peak);
                        _peakBars[i] = peak;
                    }
                    break;
            }
        }
        private void RemovePeakBars()
        {
            RemoveRectArray(_peakBars);
            RemoveRectArray(_peakBarsLow);
            RemoveRectArray(_peakBarsHigh);
            _peakBars = _peakBarsLow = _peakBarsHigh = null;
        }
        private void RemoveRectArray(Rectangle[]? arr)
        {
            if (arr == null) return;
            foreach (var r in arr)
                VisCanvas.Children.Remove(r);
        }
        private void ResizeBars()
        {
            try
            {
                Visualizer.UpdateSettings = false;
                var opts = Visualizer.InstanceOptions;

                _color1 = new SolidColorBrush(opts._barColor1);
                _color2 = new SolidColorBrush(opts._barColor2);
                _color3 = new SolidColorBrush(opts._peakColor);
                _color4 = new SolidColorBrush(opts._peakColor2);
                _gradient = GetVerticalGradientBrush(_color1.Color, _color2.Color);
                _peakGradient = GetVerticalGradientBrush(_color3.Color, _color4.Color);

                double barWidth = opts._barWidth;
                int barCount = opts._bars;
                double barGap = opts._barGap;

                // Calculate for position 3 or 4 circles
                double radius = ((barWidth + barGap) * barCount) / (2 * Math.PI);

                switch (opts._Position)
                {
                    case 3:
                        Height = radius + opts._height * 2;
                        Width = radius + opts._height * 2;
                        break;
                    case 4:
                        Height = radius + opts._height;
                        Width = radius + opts._height;
                        break;
                    default:
                        Height = opts._height;
                        Width = barCount * (barWidth + barGap) + 2;
                        break;
                }

                RemovePeakBars();
                VisCanvas.Children.Clear();
                CreateBars();
                ShowPeakBars();
            }
            catch (Exception)
            {

            }
        }

        public void ManageColorMove()
        {
            dTimeWatch.Start();
            cfWatch.Start();
            bool wait = false;
            int state = 0;


            while (!_clrCts.Token.IsCancellationRequested)
            {
                var opts = Visualizer.InstanceOptions;
                double deltaTime = GetDeltaTime();
                int rotateMode = opts._rotateColor;
                // HSV color movement
                
                if (rotateMode == 0) // No Movement Needed
                {
                    if (opts._ColorChangeFreqency > 0 && cfWatch.ElapsedMilliseconds > opts._ColorChangeFreqency)
                    {
                        wait = false;
                        Dispatcher.Invoke(() =>
                        {
                            // Cache color values to avoid repeated property access
                            var colorArr = new[] { _color1.Color, _color2.Color, _color3.Color, _color4.Color };
                            var hsvArr = new (double h, double s, double v)[4];

                            for (int i = 0; i < 4; i++)
                            {
                                var c = colorArr[i];
                                System.Drawing.Color sysColor = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
                                RGBtoHSV(sysColor, out double h, out double s, out double v);
                                h += opts._ColorMoveSpeed;
                                if (h >= 360) h -= 360;
                                hsvArr[i] = (h, s, v);
                            }

                            _color1 = new SolidColorBrush(ColorFromHSV(hsvArr[0].h, hsvArr[0].s, hsvArr[0].v, _color1.Color.A));
                            _color2 = new SolidColorBrush(ColorFromHSV(hsvArr[1].h, hsvArr[1].s, hsvArr[1].v, _color2.Color.A));
                            _color3 = new SolidColorBrush(ColorFromHSV(hsvArr[2].h, hsvArr[2].s, hsvArr[2].v, _color3.Color.A));
                            _color4 = new SolidColorBrush(ColorFromHSV(hsvArr[3].h, hsvArr[3].s, hsvArr[3].v, _color4.Color.A));
                            _gradient = GetVerticalGradientBrush(_color1.Color, _color2.Color);
                            _peakGradient = GetVerticalGradientBrush(_color3.Color, _color4.Color);
                        });
                        cfWatch.Restart();

                    }
                    else
                    {

                        if (wait) continue;
                        wait = true;
                        Dispatcher.Invoke(() =>
                        {
                            _color1 = new SolidColorBrush(opts._barColor1);
                            _color2 = new SolidColorBrush(opts._barColor2);
                            _color3 = new SolidColorBrush(opts._peakColor);
                            _color4 = new SolidColorBrush(opts._peakColor2);
                            _gradient = GetVerticalGradientBrush(_color1.Color, _color2.Color);
                            _peakGradient = GetVerticalGradientBrush(_color3.Color, _color4.Color);
                        });
                    }
                }
                else
                {
                    wait = false;
                    _progress += (float)(opts._ColorMoveSpeed * deltaTime);
                    if (_progress >= 1f)
                    {
                        _progress = 0f;
                        state = (state + 1) & 0x3; // Weird bit manipulation trick to flip back to 0 from 3
                    }

                    // Bar and peak color transitions (left or right)
                    Color c1, c2, c3, c4;
                    var b1 = opts._barColor1;
                    var b2 = opts._barColor2;
                    var p1 = opts._peakColor;
                    var p2 = opts._peakColor2;

                    // Compact state tables (for readability, state handling is unchanged)
                    if (rotateMode == 1)
                    {
                        switch (state)
                        {
                            case 0: c1 = LerpColor(b1, b2, _progress); c2 = b2; break;
                            case 1: c1 = b2; c2 = LerpColor(b2, b1, _progress); break;
                            case 2: c1 = LerpColor(b2, b1, _progress); c2 = b1; break;
                            case 3: c1 = b1; c2 = LerpColor(b1, b2, _progress); break;
                            default: c1 = b1; c2 = b2; break;
                        }
                        switch (state)
                        {
                            case 0: c3 = LerpColor(p1, p2, _progress); c4 = p2; break;
                            case 1: c3 = p2; c4 = LerpColor(p2, p1, _progress); break;
                            case 2: c3 = LerpColor(p2, p1, _progress); c4 = p1; break;
                            case 3: c3 = p1; c4 = LerpColor(p1, p2, _progress); break;
                            default: c3 = p1; c4 = p2; break;
                        }
                    }
                    else // rotateMode == 2
                    {
                        switch (state)
                        {
                            case 0: c2 = LerpColor(b2, b1, _progress); c1 = b1; break;
                            case 1: c2 = b1; c1 = LerpColor(b1, b2, _progress); break;
                            case 2: c2 = LerpColor(b1, b2, _progress); c1 = b2; break;
                            case 3: c2 = b2; c1 = LerpColor(b2, b1, _progress); break;
                            default: c1 = b1; c2 = b2; break;
                        }
                        switch (state)
                        {
                            case 0: c4 = LerpColor(p2, p1, _progress); c3 = p1; break;
                            case 1: c4 = p1; c3 = LerpColor(p1, p2, _progress); break;
                            case 2: c4 = LerpColor(p1, p2, _progress); c3 = p2; break;
                            case 3: c4 = p2; c3 = LerpColor(p2, p1, _progress); break;
                            default: c3 = p1; c4 = p2; break;
                        }
                    }

                    Dispatcher.Invoke(() =>
                    {
                        _color1 = new SolidColorBrush(c1);
                        _color2 = new SolidColorBrush(c2);
                        _color3 = new SolidColorBrush(c3);
                        _color4 = new SolidColorBrush(c4);
                        _gradient = GetVerticalGradientBrush(_color1.Color, _color2.Color);
                        _peakGradient = GetVerticalGradientBrush(_color3.Color, _color4.Color);
                    });
                }
                Thread.Sleep(16); // 60 fps
            }
        }
        private void SetBarColour(Rectangle rect, int index, int total, double height, double max, bool peak = false, bool top = false)
        {
            if (peak)
            {
                switch (Visualizer.InstanceOptions._peakColorType)
                {
                    case 0: // Match Bars
                        switch (Visualizer.InstanceOptions._barColorType)
                        {
                            case 0: // Solid
                                rect.Fill = _color1;
                                break;

                            case 1: // Vertical gradient
                                if (Visualizer.InstanceOptions._Position == 0 || Visualizer.InstanceOptions._Position == 4) // Bottom
                                {
                                    rect.Fill = _color1;
                                }
                                else if (Visualizer.InstanceOptions._Position == 2 || Visualizer.InstanceOptions._Position == 3) // Top
                                {
                                    rect.Fill = _color2;
                                }
                                else // Center
                                {
                                    if (top)
                                    {
                                        rect.Fill = _color1;
                                    }
                                    else
                                    {
                                        rect.Fill = _color2;
                                    }
                                }
                                break;

                            case 2: // Horizontal gradient
                                rect.Fill = new SolidColorBrush(
                                    Visualizer.GetGradientColor(
                                        new[] { _color1.Color, _color2.Color },
                                        (double)index / total));
                                break;

                            case 3: // Height gradient
                                rect.Fill = new SolidColorBrush(
                                    Visualizer.GetGradientColor(
                                        new[] { _color1.Color, _color2.Color },
                                        (double)height / max));
                                break;
                        }
                        break;
                    case 1: // Solid
                        rect.Fill = _color3;
                        break;

                    case 2: // Vertical gradient
                        rect.Fill = _peakGradient;
                        break;

                    case 3: // Horizontal gradient
                        rect.Fill = new SolidColorBrush(
                            Visualizer.GetGradientColor(
                                new[] { _color3.Color, _color4.Color },
                                (double)index / total));
                        break;

                    case 4: // Height gradient
                        rect.Fill = new SolidColorBrush(
                            Visualizer.GetGradientColor(
                                new[] { _color3.Color, _color4.Color },
                                (double)height / max));
                        break;
                }
            }
            else
            {
                switch (Visualizer.InstanceOptions._barColorType)
                {
                    case 0: // Solid
                        rect.Fill = _color1;
                        break;

                    case 1: // Vertical gradient
                        rect.Fill = _gradient;
                        break;

                    case 2: // Horizontal gradient
                        rect.Fill = new SolidColorBrush(
                            Visualizer.GetGradientColor(
                                new[] { _color1.Color, _color2.Color },
                                (double)index / total));
                        break;

                    case 3: // Height gradient
                        rect.Fill = new SolidColorBrush(
                            Visualizer.GetGradientColor(
                                new[] { _color1.Color, _color2.Color },
                                (double)height / max));
                        break;
                }
            }
        }

        // Delta time helpers for moving colors at the rate specified
        float _progress = 0;
        static Stopwatch dTimeWatch = new Stopwatch();
        static Stopwatch cfWatch = new Stopwatch();
        static double lastElapsed = 0;
        public static double GetDeltaTime(bool markFrame = true)
        {
            double currentElapsed = dTimeWatch.Elapsed.TotalSeconds;
            double deltaTime = currentElapsed - lastElapsed;
            if (markFrame)
            {
                lastElapsed = currentElapsed;
            }
            return deltaTime;
        }
        
        // Extra funcs
        private void UpdateBackgroundIfNeeded()
        {
            if (!Visualizer.ChangeBg) return;

            bool optionsVisible = Visualizer.ShowBg;
            Background = new SolidColorBrush(Color.FromArgb(optionsVisible ? (byte)40 : (byte)0, 255, 255, 255));
            IsHitTestVisible = optionsVisible;
            ClickThrough.Toggle(_hwnd, !optionsVisible);
            Visualizer.ChangeBg = false;
        }
        public static string GenerateRandomString()
        {
            Random random = new Random();
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            char[] stringChars = new char[4];

            for (int i = 0; i < 4; i++)
            {
                stringChars[i] = chars[random.Next(chars.Length)];
            }

            string code = new string(stringChars);

            return code;
        }
        public void Dispose()
        {
            _cts.Cancel();
            _trayIcon?.Dispose();
            _icon?.Dispose();
        }
    }
}
