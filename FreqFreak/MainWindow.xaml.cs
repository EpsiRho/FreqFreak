using H.NotifyIcon.Core;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.ConstrainedExecution;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;

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



        public CancellationTokenSource _cts = new();
        public CancellationTokenSource _clrCts = new();
        public static FPSMeter displayFpsMeter = new();
        public static Color _TrayIconColor { get; set; }
        public static bool FVZMode { get; set; }
        public static FVZWindow FVZWindowHandle { get; set; }
        public static FVZPlayer FVZPlayer { get; set; } = new FVZPlayer();

        private Rectangle[] _bars = Array.Empty<Rectangle>();
        private Rectangle[]? _peakBars; // peaks for top & bottom modes
        private Rectangle[]? _peakBarsLow; // centered low peaks
        private Rectangle[]? _peakBarsHigh; // centered high peaks

        private double[] _peaks = Array.Empty<double>();
        private double[] _peaksRight = Array.Empty<double>();
        private object _peakLock = new(); 
        private object _oscLock = new(); 
        private Task _peakDecayTask;
        private Task _ColorMoveTask;

        private static readonly Random _rng = new();

        public IntPtr _hwnd = -1;
        public static TrayIconWithContextMenu? _trayIcon;
        private static System.Drawing.Icon? _icon;
        public static SolidColorBrush _color1 = new(); // Bars 1
        public static SolidColorBrush _color2 = new(); // Bars 2
        public static SolidColorBrush _color3 = new(); // Peaks 1
        public static SolidColorBrush _color4 = new(); // Peaks 2
        public static Color[] colorArrayGradient = new Color[12]; // Rainbow / Custom gradients(?)
        public static Color[] colorPeakArrayGradient = new Color[12]; // Rainbow / Custom gradients(?)
        public static LinearGradientBrush _colorGradientBrush = new(); // Rainbow gradient brush for vertical
        public static LinearGradientBrush _colorPeaksGradientBrush = new(); // Rainbow gradient brush for vertical
        public static LinearGradientBrush _gradient = new();
        public static LinearGradientBrush _peakGradient = new();
        public static double PitchFreq = 0;
        public static double BassAmplitude = 0;
        public static string PitchText = "None";
        private static bool _failure = false;
        public static bool _lineSwiitch = false;
        public NormalDragHandler dragHandler;
        public PopupMenuItem toggleVis = new PopupMenuItem();

        double max = 0; // The maximum amplitude seen recently (decreases with DecaySpeed). Used for gradient by height

        // Window + App setup
        public MainWindow()
        {
            dragHandler = new(this);
            Visualizer.MainWin = this;
            Visualizer.InstanceOptions.SetDefaults();
            InitializeComponent();

            Loaded += (_, __) => _hwnd = new WindowInteropHelper(this).Handle;
            MouseLeftButtonDown += (s, e) => 
            {
                //var offset = e.GetPosition(this);
                //DragWorkaround.StartDragging(this, offset);
                dragHandler.BeginDrag(e);
            };
            MouseLeftButtonUp += (_, __) => 
            {
                dragHandler.EndDrag();
            };

            Random rand = new(DateTime.Now.TimeOfDay.Nanoseconds);

            _TrayIconColor = GenerateTimeBasedColor();
            Visualizer.InstanceOptions._barColor1 = _TrayIconColor;

            ConfigureWindow();
            CreateTrayIcon();

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

            new Thread(() =>
            {
                while (true)
                {
                    if (_peakDecayTask == null || _peakDecayTask.IsCompleted)
                    {
                        _peakDecayTask = Task.Run(PeakDecayLoop, _cts.Token);
                    }
                    if (_peakDecayTask == null || _peakDecayTask.IsCompleted)
                    {
                        _ColorMoveTask = Task.Run(ManageColorMove, _clrCts.Token);
                    }
                    Thread.Sleep(1000); // check every second
                }
            }).Start();

            this.KeyDown += MainWindow_KeyDown;

            this.Loaded += (_, __) =>
            {
                CreateNewOptionsWindow();
            };
        }

        private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if(e.Key == System.Windows.Input.Key.Up)
            {
                this.Top--;
            }
            else if (e.Key == System.Windows.Input.Key.Down)
            {
                this.Top++;
            }
            else if (e.Key == System.Windows.Input.Key.Left)
            {
                this.Left--;
            }
            else if (e.Key == System.Windows.Input.Key.Right)
            {
                this.Left++;
            }
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

        public void CreateNewOptionsWindow()
        {
            if (Visualizer.OptionsWindow != null)
            {
                // If it's already open bring it to the front
                Visualizer.OptionsWindow._optionsDispatcher.Invoke(() => {
                    Visualizer.OptionsWindow.Activate();
                    Visualizer.OptionsWindow.Focus();
                });
                return;
            }
            double left = 0;
            double top = 0;
            (left, top) = GetWindowPosition(this, Dispatcher, 490, 890);
            Thread t = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

                Visualizer.OptionsWindow = new OptionsWindow();
                Visualizer.OptionsWindow.Owner = null;
                Visualizer.OptionsWindow._optionsDispatcher = Visualizer.OptionsWindow.Dispatcher;
                Visualizer.OptionsWindow._optionsDispatcher.Invoke(() =>
                {
                    Visualizer.OptionsWindow.Left = left;
                    Visualizer.OptionsWindow.Top = top;
                });

                Visualizer.OptionsWindow.Closed += (s, e) =>
                {
                    Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    Visualizer.OptionsWindow._optionsDispatcher = null;
                    Visualizer.OptionsWindow = null;
                };

                Visualizer.OptionsWindow.Show();

                Dispatcher.Run();
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        public void CreateNewAudioWindow()
        {
            if (Visualizer.AudioDevicesWindow != null)
            {
                // If it's already open bring it to the front
                Visualizer.AudioDevicesWindow._audioDispatcher.Invoke(() => {
                    Visualizer.AudioDevicesWindow.Activate();
                    Visualizer.AudioDevicesWindow.Focus();
                });
                return;
            }

            double left = 0;
            double top = 0;
            if (Visualizer.OptionsWindow != null)
            {
                (left, top) = GetWindowPosition(Visualizer.OptionsWindow, Visualizer.OptionsWindow._optionsDispatcher, 400, 750);
            }
            else
            {
                (left, top) = GetWindowPosition(this, Dispatcher, 400, 750);
            }

            Thread t = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

                Visualizer.AudioDevicesWindow = new AudioDevices();
                Visualizer.AudioDevicesWindow.Owner = null;
                Visualizer.AudioDevicesWindow._audioDispatcher = Visualizer.AudioDevicesWindow.Dispatcher;
                Visualizer.AudioDevicesWindow._audioDispatcher.Invoke(() =>
                {
                    Visualizer.AudioDevicesWindow.Left = left;
                    Visualizer.AudioDevicesWindow.Top = top;
                });

                Visualizer.AudioDevicesWindow.Closed += (s, e) =>
                {
                    Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    Visualizer.AudioDevicesWindow._audioDispatcher = null;
                    Visualizer.AudioDevicesWindow = null;
                };

                Visualizer.AudioDevicesWindow.Show();

                Dispatcher.Run();
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }
        public void CreateNewFVZWindow()
        {
            if (FVZWindowHandle != null)
            {
                // FVZ Mode is a toggle
                FVZWindowHandle._fvzDispatcher.Invoke(() =>
                {
                    FVZWindowHandle.Hide();
                    FVZPlayer.Stop();
                });
                FVZMode = false;
                FVZWindowHandle = null;
                return;
            }

            FVZMode = true;
            double left = 0;
            double top = 0;
            if (Visualizer.OptionsWindow != null)
            {
                (left, top) = GetWindowPosition(Visualizer.OptionsWindow, Visualizer.OptionsWindow._optionsDispatcher, 800, 480);
            }
            else
            {
                (left, top) = GetWindowPosition(this, Dispatcher, 800, 480);
            }

            Thread t = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

                FVZWindowHandle = new FVZWindow();
                FVZWindowHandle.Owner = null;
                FVZWindowHandle._fvzDispatcher = FVZWindowHandle.Dispatcher;
                FVZWindowHandle._fvzDispatcher.Invoke(() =>
                {
                    FVZWindowHandle.Left = left;
                    FVZWindowHandle.Top = top;
                });

                FVZWindowHandle.Closed += (s, e) =>
                {
                    Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    FVZWindowHandle._fvzDispatcher = null;
                    FVZWindowHandle = null;
                };

                FVZWindowHandle.Show();

                Dispatcher.Run();
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }
        public void CreateNewGradientEditorWindow(bool peaks)
        {
            if (Visualizer.EditorWindow != null)
            {
                // If it's already open bring it to the front
                Visualizer.EditorWindow.PeakEditing = peaks;
                Visualizer.EditorWindow.Dispatcher.Invoke(() => {
                    Visualizer.EditorWindow.Activate();
                    Visualizer.EditorWindow.Focus();
                });
                return;
            }

            double left = 0;
            double top = 0;
            if (Visualizer.OptionsWindow != null)
            {
                (left, top) = GetWindowPosition(Visualizer.OptionsWindow, Visualizer.OptionsWindow._optionsDispatcher, 450, 450);
            }
            else
            {
                (left, top) = GetWindowPosition(this, Dispatcher, 450, 450);
            }

            Thread t = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

                Visualizer.EditorWindow = new GradientEditor();
                Visualizer.EditorWindow.PeakEditing = peaks;
                Visualizer.EditorWindow.Owner = null;
                Visualizer.EditorWindow.Dispatcher.Invoke(() =>
                {
                    Visualizer.EditorWindow.Left = left;
                    Visualizer.EditorWindow.Top = top;
                });

                Visualizer.EditorWindow.Closed += (s, e) =>
                {
                    Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    Visualizer.EditorWindow = null;
                };

                Visualizer.EditorWindow.Show();

                Dispatcher.Run();
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        public void CreateNewPhotoCutoutWindow()
        {
            if (Visualizer.PhotoCutoutWindow != null)
            {
                // If it's already open bring it to the front
                Visualizer.PhotoCutoutWindow.Dispatcher.Invoke(() => {
                    Visualizer.PhotoCutoutWindow.Activate();
                    Visualizer.PhotoCutoutWindow.Focus();
                });
                return;
            }

            double left = 0;
            double top = 0;
            if (Visualizer.OptionsWindow != null)
            {
                (left, top) = GetWindowPosition(Visualizer.OptionsWindow, Visualizer.OptionsWindow._optionsDispatcher, 450, 450);
            }
            else
            {
                (left, top) = GetWindowPosition(this, Dispatcher, 450, 450);
            }

            Thread t = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

                Visualizer.PhotoCutoutWindow = new PhotoCutout();
                Visualizer.PhotoCutoutWindow.Owner = null;
                Visualizer.PhotoCutoutWindow.Dispatcher.Invoke(() =>
                {
                    Visualizer.PhotoCutoutWindow.Left = left;
                    Visualizer.PhotoCutoutWindow.Top = top;
                });

                Visualizer.PhotoCutoutWindow.Closed += (s, e) =>
                {
                    Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    Visualizer.PhotoCutoutWindow = null;
                };

                Visualizer.PhotoCutoutWindow.Show();

                Dispatcher.Run();
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        public (double left, double top) GetWindowPosition(Window window, Dispatcher dispatcher, double newWindowWidth, double newWindowHeight)
        {
            double left = -1;
            double top = -1;

            List<Rect> rects = new();
            Dispatcher.Invoke(() =>
            {
                var rect = new Rect(this.Left, this.Top, this.ActualWidth, this.ActualHeight);
                rects.Add(rect);
            });
            if(Visualizer.OptionsWindow != null)
            {
                Visualizer.OptionsWindow._optionsDispatcher.Invoke(() =>
                {
                    var rect = new Rect(Visualizer.OptionsWindow.Left, Visualizer.OptionsWindow.Top, Visualizer.OptionsWindow.ActualWidth, Visualizer.OptionsWindow.ActualHeight);
                    rects.Add(rect);
                });
            }
            if (Visualizer.EditorWindow != null)
            {
                Visualizer.EditorWindow.Dispatcher.Invoke(() =>
                {
                    var rect = new Rect(Visualizer.EditorWindow.Left, Visualizer.EditorWindow.Top, Visualizer.EditorWindow.ActualWidth, Visualizer.EditorWindow.ActualHeight);
                    rects.Add(rect);
                });
            }
            if (Visualizer.AudioDevicesWindow != null)
            {
                Visualizer.AudioDevicesWindow.Dispatcher.Invoke(() =>
                {
                    var rect = new Rect(Visualizer.AudioDevicesWindow.Left, Visualizer.AudioDevicesWindow.Top, Visualizer.AudioDevicesWindow.ActualWidth, Visualizer.AudioDevicesWindow.ActualHeight);
                    rects.Add(rect);
                });
            }
            if (FVZWindowHandle != null)
            {
                FVZWindowHandle.Dispatcher.Invoke(() =>
                {
                    var rect = new Rect(FVZWindowHandle.Left, FVZWindowHandle.Top, FVZWindowHandle.ActualWidth, FVZWindowHandle.ActualHeight);
                    rects.Add(rect);
                });
            }

            double newMidH = 0;
            double newMidW = 0;

            dispatcher.Invoke(() =>
            {
                var screen = MonitorSizeHandlercs.GetCurrentMonitorSize(window);
                // Check Left
                var midH = window.Top + (window.ActualHeight / 2);
                newMidH = midH - (newWindowHeight / 2);

                var midW = window.Left + (window.ActualWidth / 2);
                newMidW = midW - (newWindowWidth / 2);

                var l = window.Left - newWindowWidth - 10;
                if (l > 0 && !DoesRectOverlapAny(rects, new Rect(l, newMidH, newWindowWidth, newWindowHeight)))
                {
                    left = l;
                    top = newMidH;
                    return;
                }

                // Check Right
                var r = window.Left + window.ActualWidth + 10;
                if (r + newWindowWidth < screen.w && !DoesRectOverlapAny(rects, new Rect(r, newMidH, newWindowWidth, newWindowHeight)))
                {
                    left = r;
                    top = newMidH;
                    return;
                }

                // Check Top
                var t = window.Top - newWindowHeight - 10;
                if (t > 0 && !DoesRectOverlapAny(rects, new Rect(newMidW, t, newWindowWidth, newWindowHeight)))
                {
                    left = newMidW;
                    top = t;
                    return;
                }

                // Check Top
                var b = window.Top + window.ActualHeight + 10;
                if (b + newWindowHeight < screen.h && !DoesRectOverlapAny(rects, new Rect(newMidW, b, newWindowWidth, newWindowHeight)))
                {
                    left = newMidW;
                    top = b;
                    return;
                }
            });


            if(left == -1 || top == -1)
            {
                left = newMidW >= 0 ? newMidW : 0;
                top = newMidH >= 0 ? newMidH : 0;
            }

            return (left, top);
        }

        public static bool DoesRectOverlapAny(List<Rect> rectList, Rect target)
        {
            foreach (var rect in rectList)
            {
                if (rect.IntersectsWith(target))
                {
                    return true;
                }
            }
            return false;
        }

        // TrayIcon Management
        public static Color GenerateTimeBasedColor()
        {
            long ticks = DateTime.UtcNow.Ticks;

            // Use a hash to break predictable patterns
            int hash = ticks.GetHashCode();

            // Normalize hash to range [0, 360)
            float hue = Math.Abs(hash % 360);

            // Convert HSV to RGB
            return ColorFromHSV(hue, 0.85f, 0.95f); // high saturation and value for vibrancy
        }
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

            toggleVis = new PopupMenuItem("Pause Visualizer", (_, _) =>
            {
                if (Visualizer._captureCTS.IsCancellationRequested)
                {
                    Visualizer._captureCTS = new CancellationTokenSource();
                    _ = Task.Run(() => Visualizer.StartCapture(Visualizer._captureCTS.Token), Visualizer._captureCTS.Token);
                    toggleVis.Text = "Pause Visualizer";
                }
                else
                {
                    Visualizer._captureCTS.Cancel();
                    toggleVis.Text = "Resume Visualizer";
                }
            });

            _trayIcon = new TrayIconWithContextMenu
            {
                Icon = customIcon.Handle,
                ToolTip = "FreqFreak",
                ContextMenu = new PopupMenu
                {
                    Items =
                    {
                        new PopupMenuItem("View Settings Panel", (_, _) => Dispatcher.Invoke(() =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                CreateNewOptionsWindow();
                            });
                        })),
                        new PopupMenuSeparator(),
                        toggleVis,
                        new PopupMenuItem("Open New Instance", (_, _) =>{
                            Process.Start("FreqFreak.exe");
                        }),
                        new PopupMenuSeparator(),
                        new PopupMenuItem("Toggle Peaks", (_, _) =>{
                            Visualizer.InstanceOptions._showPeaks = !Visualizer.InstanceOptions._showPeaks;
                        }),
                        new PopupMenuItem("Import Config File", (_, _) =>{
                            Dispatcher.Invoke(() =>
                            {
                                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                                {
                                    Filter = "JSON files (*.json)|*.json",
                                    DefaultExt = ".json",
                                    AddExtension = true
                                };
                                if (openFileDialog.ShowDialog() == true)
                                {
                                    // Load the Visualizer.InstanceOptions from the selected file
                                    string json = System.IO.File.ReadAllText(openFileDialog.FileName);
                                    var options = JsonConvert.DeserializeObject<Settings>(json);
                                    if (options != null)
                                    {
                                        if (options._fftSize != Visualizer.InstanceOptions._fftSize)
                                        {
                                            Visualizer._captureCTS.Cancel();
                                            Visualizer._captureCTS = new();
                                            Visualizer.InstanceOptions._fftSize = options._fftSize;
                                            var _captureThread = new Thread(() =>
                                            {
                                                Visualizer.StartCapture(Visualizer._captureCTS.Token);
                                            });
                                            _captureThread.Start();
                                        }
                                        Visualizer.InstanceOptions = options;
                                        Visualizer.UpdateSettings = true;
                                    }
                                    else
                                    {
                                        MessageBox.Show("Failed to load settings from file.");
                                    }
                                }
                            });
                        }),
                        new PopupMenuItem("View Audio Input Switcher", (_, _) =>{
                            CreateNewAudioWindow();
                        }),
                        new PopupMenuSeparator(),
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

            _trayIcon.Removed += (_, _) =>
            {
                _failure = true;
            };

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
        public static Color LerpColor(Color firstFloat, Color secondFloat, float by)
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
        public static LinearGradientBrush GetVerticalGradientBrush(Color[] clrs)
        {
            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };

            if (clrs == null)
            {
                gradient.GradientStops.Add(new GradientStop(Color.FromArgb(255,255,0,0), 0));
                gradient.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 255, 0), 0.8));
                return gradient;
            }

            for(double i = 0; i < clrs.Length; i++)
            {
                gradient.GradientStops.Add(new GradientStop(clrs[(int)i], (double)(i / clrs.Length)));

            }
            return gradient;
        }
        public static LinearGradientBrush GetHorizontalGradientBrush(Color[] clrs)
        {
            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0)
            };
            for (double i = 0; i < clrs.Length; i++)
            {
                gradient.GradientStops.Add(new GradientStop(clrs[(int)i], (double)(i / clrs.Length)));

            }
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
            (double pos, int canFall)[] peakRightHold = new (double, int canFall)[_peaksRight.Length];
            while (!token.IsCancellationRequested)
            {
                try 
                {
                    if (peakHold.Length != _peaks.Length)
                    {
                        peakHold = new (double, int canFall)[_peaks.Length];
                    }
                    if (peakRightHold.Length != _peaks.Length)
                    {
                        peakRightHold = new (double, int canFall)[_peaksRight.Length];
                    }
                    double decay = Visualizer.InstanceOptions._peakDecay;
                    double curMax = 0;
                    lock (_peakLock)
                    {
                        for (int i = 0; i < _peaks.Length; i++)
                        {
                            // Left

                            if (peakHold[i].pos >= _peaks[i])
                            {
                                peakHold[i].canFall++;
                                if (peakHold[i].canFall >= Visualizer.InstanceOptions._peakHold)
                                {
                                    var newVal = _peaks[i] - decay;
                                    _peaks[i] = newVal > Visualizer.InstanceOptions._minHeight ? newVal : Visualizer.InstanceOptions._minHeight;
                                    peakHold[i].pos = _peaks[i];
                                }
                            }
                            else
                            {
                                peakHold[i].pos = _peaks[i];
                                peakHold[i].canFall = 0;
                            }
                            //_peaks[i] -= decay;

                            if (curMax < _peaks[i])
                            {
                                curMax = _peaks[i];
                            }

                            if (_peaksRight.Length == i)
                            {
                                return;
                            }


                            // Right

                            if (peakRightHold[i].pos >= _peaksRight[i])
                            {
                                peakRightHold[i].canFall++;
                                if (peakRightHold[i].canFall >= Visualizer.InstanceOptions._peakHold)
                                {
                                    var newVal = _peaksRight[i] - decay;
                                    _peaksRight[i] = newVal > Visualizer.InstanceOptions._minHeight ? newVal : Visualizer.InstanceOptions._minHeight;
                                    peakRightHold[i].pos = _peaksRight[i];
                                }
                            }
                            else
                            {
                                peakRightHold[i].pos = _peaksRight[i];
                                peakRightHold[i].canFall = 0;
                            }
                            //_peaks[i] -= decay;

                            if (curMax < _peaksRight[i])
                            {
                                curMax = _peaksRight[i];
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
                catch (Exception)
                {

                }
            }
        }

        // Render funcs
        Random rand = new Random();
        private (double X, double Y) GetRandomXY()
        {
            var direction = rand.Next(8);
            double movementX = 0;
            double movementY = 0;

            switch (direction)
            {
                case 0: // Up
                    movementY = Visualizer.InstanceOptions._bassShake * (BassAmplitude);
                    break;
                case 1: // Up Right
                    movementX = Visualizer.InstanceOptions._bassShake * (BassAmplitude);
                    movementY = Visualizer.InstanceOptions._bassShake * (BassAmplitude);
                    break;
                case 2: // Right
                    movementX = Visualizer.InstanceOptions._bassShake * (BassAmplitude);
                    break;
                case 3: // Down Right
                    movementX = Visualizer.InstanceOptions._bassShake * (BassAmplitude);
                    movementY = Visualizer.InstanceOptions._bassShake * (BassAmplitude);
                    break;
                case 4: // Down
                    movementY = Visualizer.InstanceOptions._bassShake * (BassAmplitude);
                    break;
                case 5: // Down Left
                    movementX = Visualizer.InstanceOptions._bassShake * (BassAmplitude);
                    movementY = Visualizer.InstanceOptions._bassShake * (BassAmplitude);
                    break;
                case 6: // Left
                    movementX = Visualizer.InstanceOptions._bassShake * (BassAmplitude);
                    break;
                case 7: // Left Up
                    movementX = Visualizer.InstanceOptions._bassShake * BassAmplitude;
                    movementY = Visualizer.InstanceOptions._bassShake * BassAmplitude;
                    break;

            }

            return (movementX, movementY);
        }
        private void OnRender(object? sender, EventArgs e)
        {
            displayFpsMeter.Tick();
            if(NormalDragHandler.IsDragging)
            {
                return;
            }
            if (_cts.IsCancellationRequested) return;

            UpdateBackgroundIfNeeded();

            double[]? frame = null;
            double[]? frameR = null;

            if (FVZMode)
            {
                frame = FVZPlayer.GetCurrentFrame();
            }
            else
            {
                if (Visualizer.InstanceOptions._visualizationMode != VisualizationMode.Oscilloscope)
                {
                    if (Visualizer.InstanceOptions._channelMode == ChannelMode.Stereo)
                    {
                        (frame, frameR) = Visualizer.GetFrameStereo();
                    }
                    else
                    {
                        frame = Visualizer.GetFrame();
                    }
                }
                else
                {
                    var fftSize = Visualizer._oscilloscopeBuffer.Count;
                    frame = new double[fftSize];
                    frameR = new double[fftSize];

                    for (int i = 0; i < fftSize; i++)
                    {
                        var sample = Visualizer._oscilloscopeBuffer.Dequeue(); // Remove used samples
                        frame[i] = sample.L;
                        frameR[i] = sample.R;
                    }
                    Visualizer._oscilloscopeBuffer.Clear();
                }
            }

            if (Visualizer.InstanceOptions._visualizationMode != VisualizationMode.Oscilloscope)
            {
                if (frame == null) return;

                if (Visualizer.InstanceOptions._invertSpectrum)
                {
                    Array.Reverse(frame);
                    if (frameR != null)
                    {
                        Array.Reverse(frameR);
                    }
                }
            }

            if (Visualizer.UpdateSettings)
            {
                ResizeBars();
            }

            var binEdges = Visualizer.GetFrameEdges();
            if (binEdges != null)
            {
                if (Visualizer.InstanceOptions._invertSpectrum)
                {
                    var arr = _peaks.Reverse().ToArray();
                    var arr2 = _peaksRight.Reverse().ToArray();
                    var binCenters = PitchDetector.CalculateBinCenters(binEdges);
                    var pitchInfo = PitchDetector.DetectPitch(arr, binCenters);
                    PitchText = pitchInfo.note;
                    PitchFreq = pitchInfo.frequency;

                    var BassAmplitudeL = PitchDetector.DetectBassAmplitude(arr, binCenters, Visualizer.InstanceOptions._height);
                    var BassAmplitudeR = PitchDetector.DetectBassAmplitude(arr2, binCenters, Visualizer.InstanceOptions._height);
                    BassAmplitude = (BassAmplitudeL + BassAmplitudeR) / 2;
                }
                else
                {
                    var binCenters = PitchDetector.CalculateBinCenters(binEdges);
                    var pitchInfo = PitchDetector.DetectPitch(_peaks, binCenters);
                    PitchText = pitchInfo.note;
                    PitchFreq = pitchInfo.frequency;

                    var BassAmplitudeL = PitchDetector.DetectBassAmplitude(_peaks, binCenters, Visualizer.InstanceOptions._height);
                    var BassAmplitudeR = PitchDetector.DetectBassAmplitude(_peaksRight, binCenters, Visualizer.InstanceOptions._height);
                    BassAmplitude = (BassAmplitudeL + BassAmplitudeR) / 2;
                }
                
            }

            try
            {
                if (Visualizer.InstanceOptions._visualizationMode == VisualizationMode.Oscilloscope)
                {
                    if(frame == null || frameR == null) return;
                    OscView.UpdatePlane(frame, frameR);
                }
                else
                {
                    UpdateBars(frame, frameR);
                    UpdatePeakRectangles();
                }
                CenterLine.Fill = _color3;
                UpdateRotation();
            }
            catch (Exception)
            {

            }
        }
        private void UpdateRotation()
        {
            double angleDegrees = Visualizer.InstanceOptions._rotation; 
            double angleRadians = angleDegrees * Math.PI / 180;

            double barWidth = Visualizer.InstanceOptions._barWidth;
            int barCount = Visualizer.InstanceOptions._bars;
            double barGap = Visualizer.InstanceOptions._barGap;
            if (Visualizer.InstanceOptions._channelMode == ChannelMode.Stereo && (Visualizer.InstanceOptions._visualizationMode == VisualizationMode.OuterCircle || Visualizer.InstanceOptions._visualizationMode == VisualizationMode.InnerCircle))
            {
                barCount = barCount * 2;
            }

            double originalWidth = (barWidth * barCount) + (barGap * barCount);
            double originalHeight = Visualizer.InstanceOptions._height;

            double newWidth = Math.Abs(originalWidth * Math.Cos(angleRadians)) + Math.Abs(originalHeight * Math.Sin(angleRadians));
            double newHeight = Math.Abs(originalWidth * Math.Sin(angleRadians)) + Math.Abs(originalHeight * Math.Cos(angleRadians));

            // Calculate for position 3 or 4 circles
            double radius = ((barWidth + barGap) * barCount) / (2 * Math.PI);
            double totalRadius = radius + (Visualizer.InstanceOptions._height);

            //var normalized = BassAmplitude / Visualizer.InstanceOptions._height;
            var scale = 1 + (BassAmplitude * (Visualizer.InstanceOptions._bassScale - 1));

            switch (Visualizer.InstanceOptions._visualizationMode)
            {
                case VisualizationMode.OuterCircle:
                    MainGrid.Width = (totalRadius * 2) * Visualizer.InstanceOptions._bassScale;
                    MainGrid.Height = (totalRadius * 2) * Visualizer.InstanceOptions._bassScale;
                    Width = MainGrid.Width;
                    Height = MainGrid.Height ;
                    MainGridRotation.Angle = angleDegrees;
                    MainGridScale.ScaleX = scale;
                    MainGridScale.ScaleY = scale;
                    VisCanvas.Width = MainGrid.Width;
                    VisCanvas.Height = MainGrid.Height;
                    (MainGridTranslation.X, MainGridTranslation.Y) = GetRandomXY();
                    break;
                case VisualizationMode.InnerCircle:
                    MainGrid.Width = (radius * 2) * Visualizer.InstanceOptions._bassScale;
                    MainGrid.Height = (radius * 2) * Visualizer.InstanceOptions._bassScale;
                    Width = MainGrid.Width;
                    Height = MainGrid.Height;
                    MainGridRotation.Angle = angleDegrees;
                    MainGridScale.ScaleX = scale;
                    MainGridScale.ScaleY = scale;
                    VisCanvas.Width = MainGrid.Width;
                    VisCanvas.Height = MainGrid.Height;
                    break;
                case VisualizationMode.Oscilloscope:
                    MainGrid.Width = barCount * Visualizer.InstanceOptions._bassScale;
                    MainGrid.Height = originalHeight * Visualizer.InstanceOptions._bassScale;
                    Width = barCount * Visualizer.InstanceOptions._bassScale;
                    Height = originalHeight * Visualizer.InstanceOptions._bassScale;
                    MainGridRotation.Angle = angleDegrees;
                    MainGridScale.ScaleX = scale;
                    MainGridScale.ScaleY = scale;
                    OscView.Width = MainGrid.Width;
                    OscView.Height = MainGrid.Height;
                    break;
                default:
                    MainGrid.Width = newWidth * scale;
                    MainGrid.Height = newHeight * scale;
                    Width = newWidth * Visualizer.InstanceOptions._bassScale;
                    Height = newHeight * Visualizer.InstanceOptions._bassScale;
                    MainGridRotation.Angle = angleDegrees;
                    MainGridScale.ScaleX = scale;
                    MainGridScale.ScaleY = scale;
                    VisCanvas.Width = originalWidth;
                    VisCanvas.Height = originalHeight;
                    break;
            }
        }

        private void UpdateBars(double[] frame, double[] frameRight = null)
        {
            var opts = Visualizer.InstanceOptions;
            double height = opts._height;
            double min = opts._minHeight;
            double minHalf = min * 0.5;

            var pos = opts._visualizationMode;
            var channels = opts._channelMode;
            bool stereo = channels == ChannelMode.Stereo;

            double cx = VisCanvas.ActualWidth * 0.5;
            double cy = VisCanvas.ActualHeight * 0.5;
            double halfBar = opts._barWidth * 0.5;
            double barWidth = opts._barWidth;
            double barGap = opts._barGap;
            int barCount = opts._bars;

            double attack = opts._attackSpeed;
            double decay = opts._decaySpeed;
            double canvasHalfHeight = (pos == VisualizationMode.Center) ? VisCanvas.ActualHeight * 0.5 : 0.0;

            // Circle constants
            int doubledBars = barCount * 2;
            double combinedWidthGap = barWidth + barGap;
            double radiusStereo = stereo ? (combinedWidthGap * doubledBars) / (2 * Math.PI) : 0.0;
            double radiusMono = !stereo ? (combinedWidthGap * barCount) / (2 * Math.PI) : 0.0;
            double angleStepStereo = stereo ? (2 * Math.PI) / doubledBars : 0.0;
            double angleStepMono = !stereo ? (2 * Math.PI) / barCount : 0.0;
            double rotationOffset = opts._rotation;

            // Scale incoming data & find local max 
            int barLen = Math.Min(frame.Length, _bars.Length);
            double localMax = 0.0;

            for (int i = 0; i < barLen; i++)
            {
                double valL = frame[i] *= height;
                if (valL > localMax) localMax = valL;

                if (stereo)
                {
                    double valR = frameRight[i] *= height;
                    if (valR > localMax) localMax = valR;
                }
            }

            // Trim overflow frames (can occur after settings changes or in fvz mode if the fvz file bars != set display bars)
            if (frame.Length > _bars.Length)
            {
                Array.Resize(ref frame, _bars.Length);
            }

            if (_lineSwiitch)
            {
                _lineSwiitch = false;
                CreateBars();
                ShowPeakBars();
            }

            // Update each bar 
            List<Point> linePoints = new List<Point>();
            List<Point> linePoints2 = new List<Point>();
            for (int i = 0; i < barLen; i++)
            {
                Rectangle rect = _bars[i];

                double current = 0.0;
                double currentLeft = 0.0;
                double currentRight = 0.0;

                if (stereo)
                {
                    double targetLeft = frameRight[i] + minHalf;
                    double targetRight = frame[i] + minHalf;

                    currentLeft = double.IsNaN(rect.StrokeMiterLimit) ? 0.0 : rect.StrokeMiterLimit;
                    currentRight = double.IsNaN(rect.StrokeDashOffset) ? 0.0 : rect.StrokeDashOffset;

                    double speedL = targetLeft > currentLeft ? attack : decay;
                    double speedR = targetRight > currentRight ? attack : decay;

                    currentLeft = Math.Clamp(currentLeft + (targetLeft - currentLeft) * speedL, 0.0, height);
                    currentRight = Math.Clamp(currentRight + (targetRight - currentRight) * speedR, 0.0, height);

                    current = (currentLeft + currentRight) * 0.5;
                    if (current < 1.0) current = 0.0;
                    if (currentLeft < 1.0) currentLeft = 0.0;
                    if (currentRight < 1.0) currentRight = 0.0;

                    rect.StrokeMiterLimit = currentLeft;
                    rect.Height = current;
                    rect.StrokeDashOffset = currentRight;
                }
                else
                {
                    double target = frame[i] + min;
                    current = double.IsNaN(rect.Height) ? 0.0 : rect.Height;

                    double speed = target > current ? attack : decay;
                    current = Math.Clamp(current + (target - current) * speed, 0.0, height);
                    if (current < 1.0) current = 0.0;

                    rect.Height = current;
                }

                // Positioning 
                switch (pos)
                {
                    case VisualizationMode.Bottom:
                        if (Visualizer.InstanceOptions._showLines)
                        {
                            linePoints.Add(new Point((barWidth + barGap) * i, height - current));
                            if (_peaks[i] < current) _peaks[i] = current;
                            continue;
                        }

                        Canvas.SetBottom(rect, 0.0);
                        Canvas.SetTop(rect, double.NaN);
                        if (_peaks[i] < current) _peaks[i] = current;
                        SetBarColour(rect, i, barLen, current, localMax);
                        break;

                    case VisualizationMode.Center:
                        if (stereo)
                        {
                            double percentBelow = currentLeft / (currentLeft + currentRight + double.Epsilon);
                            double percentAbove = currentRight / (currentLeft + currentRight + double.Epsilon);
                            double leftH = current * percentBelow; 
                            double rightH = current * percentAbove;

                            double offsetDown = canvasHalfHeight - leftH;

                            if (Visualizer.InstanceOptions._showLines)
                            {
                                linePoints.Add(new Point((barWidth + barGap) * i, canvasHalfHeight - (current * percentAbove)));
                                linePoints2.Add(new Point((barWidth + barGap) * i, canvasHalfHeight + (current * percentBelow)));
                                if (_peaks[i] < current) _peaks[i] = current;
                                if (_peaksRight[i] < currentRight) _peaksRight[i] = currentRight;
                                continue;
                            }

                            Canvas.SetBottom(rect, offsetDown);
                            Canvas.SetTop(rect, double.NaN);

                            if (_peaks[i] < currentRight) _peaks[i] = currentRight;
                            if (_peaksRight[i] < currentLeft) _peaksRight[i] = currentLeft;
                            SetBarColour(rect, i, barLen, current, localMax);
                        }
                        else
                        {
                            if (Visualizer.InstanceOptions._showLines)
                            {
                                linePoints.Add(new Point((barWidth + barGap) * i, canvasHalfHeight + (current * 0.5)));
                                linePoints2.Add(new Point((barWidth + barGap) * i, canvasHalfHeight - (current * 0.5)));
                                if (_peaks[i] < current) _peaks[i] = current;
                                continue;
                            }
                            Canvas.SetBottom(rect, canvasHalfHeight - (current * 0.5));
                            Canvas.SetTop(rect, double.NaN);
                            if (_peaks[i] < current) _peaks[i] = current;
                            SetBarColour(rect, i, barLen, current, localMax);
                        }
                        break;

                    case VisualizationMode.Top:
                        if (Visualizer.InstanceOptions._showLines)
                        {
                            linePoints.Add(new Point((barWidth + barGap) * i, current));
                            if (_peaks[i] < current) _peaks[i] = current;
                            continue;
                        }
                        Canvas.SetTop(rect, 0.0);
                        Canvas.SetBottom(rect, double.NaN);
                        if (_peaks[i] < current) _peaks[i] = current;
                        SetBarColour(rect, i, barLen, current, localMax);
                        break;

                    case VisualizationMode.OuterCircle:
                    case VisualizationMode.InnerCircle:

                        if (stereo)
                        {
                            double angle = ((i + 0.5) * angleStepStereo) - PI_OVER_TWO;
                            double cos = Math.Cos(angle);
                            double sin = Math.Sin(angle);
                            double cosR = -cos;
                            double sinR = sin;

                            double x = cx + radiusStereo * cos;
                            double y = cy + radiusStereo * sin;
                            double xMirror = (cx * 2.0) - x;
                            double sgn = (pos == VisualizationMode.OuterCircle) ? 1.0 : -1.0;


                            Rectangle rectMirror = _bars[_bars.Length - 1 - i];

                            rect.Height = currentLeft;
                            rectMirror.Height = currentRight;

                            if (Visualizer.InstanceOptions._showLines)
                            {
                                linePoints.Add(new Point(x + sgn * cos * currentLeft, y + sgn * sin * currentLeft)); // Needs to be current Height of left bars
                                linePoints2.Add(new Point(xMirror + sgn * cosR * currentRight, y + sgn * sinR * currentRight)); // Needs to be current Height of right bars
                                if (_peaks[i] < current) _peaks[i] = currentLeft;
                                if (_peaksRight[i] < currentRight) _peaksRight[i] = currentRight;
                                continue;
                            }

                            Canvas.SetLeft(rect, x - halfBar);
                            Canvas.SetLeft(rectMirror, xMirror - halfBar);

                            if (pos == VisualizationMode.OuterCircle)
                            {
                                Canvas.SetTop(rect, y);
                                rect.RenderTransform = new RotateTransform(angle * 180.0 / Math.PI - 90.0, halfBar, 0.0);

                                Canvas.SetTop(rectMirror, y);
                                rectMirror.RenderTransform = new RotateTransform((-angle + Math.PI) * 180.0 / Math.PI - 90.0, halfBar, 0.0);
                                if (_peaks[i] < currentLeft) _peaks[i] = currentLeft;
                                if (_peaksRight[i] < currentRight) _peaksRight[i] = currentRight;
                            }
                            else  // InnerCircle
                            {
                                Canvas.SetTop(rect, y - currentLeft);
                                rect.RenderTransform = new RotateTransform(angle * 180.0 / Math.PI - 90.0, halfBar, currentLeft);

                                Canvas.SetTop(rectMirror, y - currentRight);
                                rectMirror.RenderTransform = new RotateTransform((-angle + Math.PI) * 180.0 / Math.PI - 90.0, halfBar, currentRight);

                                if (_peaks[i] < currentLeft) _peaks[i] = currentLeft;
                                if (_peaksRight[i] < currentRight) _peaksRight[i] = currentRight;
                            }

                            SetBarColour(rect, i, barLen, currentLeft, localMax);
                            SetBarColour(rectMirror, i, barLen, currentRight, localMax);

                        }
                        else  // Mono circular
                        {
                            double angle = (i * angleStepMono - PI_OVER_TWO) + rotationOffset;
                            double cos = Math.Cos(angle);
                            double sin = Math.Sin(angle);
                            double x = cx + radiusMono * cos;
                            double y = cy + radiusMono * sin;

                            double sgn = (pos == VisualizationMode.OuterCircle) ? 1.0 : -1.0;

                            if (Visualizer.InstanceOptions._showLines)
                            {
                                linePoints.Add(new Point(x + sgn * cos * current, y + sgn * sin * current)); // Needs to be current Height of left bars
                                if (_peaks[i] < current) _peaks[i] = current;
                                if (_peaksRight[i] < currentRight) _peaksRight[i] = currentRight;
                                continue;
                            }

                            Canvas.SetLeft(rect, x - halfBar);

                            if (pos == VisualizationMode.OuterCircle)
                            {
                                Canvas.SetTop(rect, y);
                                rect.RenderTransform = new RotateTransform(angle * 180.0 / Math.PI - 90.0, halfBar, 0.0);
                            }
                            else
                            {
                                Canvas.SetTop(rect, y - current);
                                rect.RenderTransform = new RotateTransform(angle * 180.0 / Math.PI - 90.0, halfBar, current);
                            }

                            if (_peaks[i] < current) _peaks[i] = current;
                            SetBarColour(rect, i, barLen, current, localMax);
                        }
                        break;
                }

                // Color update 
            }
            if (Visualizer.InstanceOptions._showLines)
            {
                VisCanvas.Children.Clear();
                switch (pos)
                {
                    case VisualizationMode.Bottom:
                    case VisualizationMode.Top:
                        DrawSmoothCurve(linePoints);
                        break;
                    case VisualizationMode.OuterCircle:
                    case VisualizationMode.InnerCircle:
                        if(linePoints2.Count() > 0)
                        {
                            linePoints2.Reverse();
                            linePoints.AddRange(linePoints2);
                            linePoints.Add(linePoints.First());
                            DrawSmoothCurve(linePoints);
                        }
                        else
                        {
                            linePoints.Add(linePoints.First());
                            DrawSmoothCurve(linePoints);
                        }
                        break;

                    case VisualizationMode.Center:
                        //DrawSmoothCurve(linePoints);
                        //DrawSmoothCurve(linePoints2);
                        linePoints2.Reverse();
                        linePoints.AddRange(linePoints2);
                        DrawSmoothCurve(linePoints);
                        break;
                }
            }

            //max = localMax;
        }
        public void DrawSmoothCurve(List<Point> points)
        {
            if (points.Count < 2)
                return;

            var pathFigure = new PathFigure { StartPoint = points[0] };
            var segments = new PathSegmentCollection();

            for (int i = 0; i < points.Count - 1; i++)
            {
                Point p0 = i > 0 ? points[i - 1] : points[i];
                Point p1 = points[i];
                Point p2 = points[i + 1];
                Point p3 = i < points.Count - 2 ? points[i + 2] : p2;

                // Catmull-Rom to Bezier conversion
                Point cp1 = new Point(
                    p1.X + (p2.X - p0.X) / 6,
                    p1.Y + (p2.Y - p0.Y) / 6);

                Point cp2 = new Point(
                    p2.X - (p3.X - p1.X) / 6,
                    p2.Y - (p3.Y - p1.Y) / 6);

                segments.Add(new BezierSegment(cp1, cp2, p2, true));
            }

            pathFigure.Segments = segments;
            var geometry = new PathGeometry(new[] { pathFigure });

            Brush brush = null;
            switch (Visualizer.InstanceOptions._barColorType)
            {
                case ColorMode.SolidColor:
                    brush = _color1;
                    break;

                case ColorMode.DualColorVertical:
                    brush = _gradient;
                    break;

                case ColorMode.DualColorHorizontal:
                    brush = GetHorizontalGradientBrush(new[] { _color1.Color, _color2.Color });
                    break;

                case ColorMode.DualColorHeight:
                    brush = new SolidColorBrush(
                        Visualizer.GetGradientColor(new[] { _color1.Color, _color2.Color }, (double)max / Visualizer.InstanceOptions._height));
                    break;
                case ColorMode.GradientVertical:
                    brush = _colorGradientBrush;
                    break;
                case ColorMode.GradientHorizontal:
                    brush = GetHorizontalGradientBrush(colorArrayGradient);
                    break;
                case ColorMode.GradientHeight:
                    brush = new SolidColorBrush(
                        Visualizer.GetGradientColor(colorArrayGradient, (double)max / Visualizer.InstanceOptions._height));
                    break;
                case ColorMode.GradientPitch: // Peak rainbow
                    brush = new SolidColorBrush(
                        PitchDetector.GetPitchColor(PitchFreq, Visualizer.InstanceOptions._customNoteGradientColors));
                    break;
                case ColorMode.DualColorPitch: // Peak gradient
                    brush = new SolidColorBrush(
                        PitchDetector.GetPitchColor(PitchFreq, new[] { Visualizer.InstanceOptions._barColor1, Visualizer.InstanceOptions._barColor2 }));
                    break;
                case ColorMode.GradientFrequency: // Frequency rainbow
                    brush = new SolidColorBrush(
                        Visualizer.GetGradientColor(
                            colorArrayGradient,
                            (PitchFreq / 2200) - 0.03)); ;
                    break;
                case ColorMode.DualColorFrequency: // Frequency gradient
                    brush = new SolidColorBrush(
                        Visualizer.GetGradientColor(
                            new[] { Visualizer.InstanceOptions._barColor1, Visualizer.InstanceOptions._barColor2 },
                            (PitchFreq / 2200) - 0.03)); ;
                    break;
            }

            var path = new System.Windows.Shapes.Path
            {
                Stroke = brush,
                StrokeThickness = 2,
                Data = geometry
            };
            
            VisCanvas.Children.Add(path);
        }

        private static RotateTransform EnsureRotateTransform(Rectangle r)
        {
            if (r.RenderTransform is RotateTransform rt) return rt;
            rt = new RotateTransform();
            r.RenderTransform = rt;
            return rt;
        }
        private const double DEG = 180.0 / Math.PI;
        private const double PI_OVER_TWO = Math.PI / 2;
        private void UpdatePeakRectangles()
        {
            var opts = Visualizer.InstanceOptions;
            if (!opts._showPeaks || Visualizer.UpdatePeaks)
            {
                Visualizer.UpdatePeaks = false;
                if (_peakBars == null && _peakBarsLow == null && _peakBarsHigh == null) return;
                RemovePeakBars();
                return;
            }
            if (_peakBars == null && _peakBarsLow == null && _peakBarsHigh == null) ShowPeakBars();

            double min = opts._minHeight;
            var pos = opts._visualizationMode;
            var channels = opts._channelMode;
            bool stereo = channels == ChannelMode.Stereo;

            double barWidth = opts._barWidth;
            double barGap = opts._barGap;
            double halfBar = barWidth * 0.5;
            double stepX = barWidth + barGap;
            int barCount = opts._bars;
            double leftX = 1.0;

            Rectangle[] barArr = _peakBars;

            switch (pos)
            {
                // Bottom / Top
                case VisualizationMode.Bottom:
                case VisualizationMode.Top:
                        if (barArr == null) return;
                        bool useTop = pos == VisualizationMode.Top;

                        for (int i = 0; i < barArr.Length; i++, leftX += stepX)
                        {
                            var peak = barArr[i];

                            SetBarColour(peak, i, barCount, _peaks[i], max, true);

                            Canvas.SetLeft(peak, leftX);
                            if (useTop)
                                Canvas.SetTop(peak, _peaks[i]);
                            else
                                Canvas.SetBottom(peak, _peaks[i]);
                        }
                        break;

                // Circle modes
                case VisualizationMode.OuterCircle:
                case VisualizationMode.InnerCircle:
                        double cx = ActualWidth * 0.5;
                        double cy = ActualHeight * 0.5;
                        bool outer = pos == VisualizationMode.OuterCircle;

                        if (stereo) // stereo: two peak arrays
                        {
                            if (_peakBarsLow == null || _peakBarsHigh == null) return;

                            int doubled = barCount * 2;
                            double radius = stepX * doubled / (2 * Math.PI);
                            double stepAng = (2 * Math.PI) / doubled;

                            for (int i = 0; i < _peakBarsLow.Length; i++)
                            {
                                var pLow = _peakBarsLow[i];
                                var pHigh = _peakBarsHigh[i];

                                var rotLow = EnsureRotateTransform(pLow);
                                var rotHigh = EnsureRotateTransform(pHigh);

                                SetBarColour(pLow, i, barCount, _peaks[i], max, true);
                                SetBarColour(pHigh, i, barCount, _peaksRight[i], max, true);

                                // cache polar -> Cartesian once per bar
                                if (pLow.Tag == null)
                                {
                                    double a = (i + 0.5) * stepAng - Math.PI * 0.5;
                                    double x1 = cx + radius * Math.Cos(a);
                                    double y1 = cy + radius * Math.Sin(a);
                                    pLow.Tag = (x1, y1, a, cx, cy);
                                }
                                var (x, y, ang, cxOld, cyOld) = ((double, double, double, double, double))pLow.Tag;

                                if(cx != cxOld || cy != cyOld)
                                {
                                    // recalculate polar -> Cartesian if center has changed
                                    x = cx + radius * Math.Cos(ang);
                                    y = cy + radius * Math.Sin(ang);
                                    pLow.Tag = (x, y, ang, cx, cy);
                                }

                                double angM = ang + Math.PI; // mirror angle
                                double xm = cx + radius * Math.Cos(angM); // mirror x

                                Canvas.SetLeft(pLow, x - halfBar);
                                Canvas.SetLeft(pHigh, xm - halfBar);

                                if (outer)
                                {
                                    Canvas.SetTop(pLow, y + _peaks[i]);
                                    rotLow.Angle = ang * DEG - 90.0;
                                    rotLow.CenterX = halfBar;
                                    rotLow.CenterY = -_peaks[i];

                                    Canvas.SetTop(pHigh, y + _peaksRight[i]);
                                    rotHigh.Angle = -angM * DEG - 90.0;
                                    rotHigh.CenterX = halfBar;
                                    rotHigh.CenterY = -_peaksRight[i];
                                }
                                else // InnerCircle
                                {
                                    Canvas.SetTop(pLow, y - _peaks[i]);
                                    rotLow.Angle = ang * DEG - 90.0;
                                    rotLow.CenterX = halfBar;
                                    rotLow.CenterY = _peaks[i];

                                    Canvas.SetTop(pHigh, y - _peaksRight[i]);
                                    rotHigh.Angle = -angM * DEG - 90.0;
                                    rotHigh.CenterX = halfBar;
                                    rotHigh.CenterY = _peaksRight[i];
                                }
                            }
                        }
                        else // mono circle
                        {
                            if (barArr == null) return;

                            double radius = stepX * barCount / (2 * Math.PI);
                            double stepAng = (2 * Math.PI) / barCount;
                            double angOff = -Math.PI * 0.5;

                            for (int i = 0; i < barArr.Length; i++)
                            {
                                var peak = barArr[i];
                                SetBarColour(peak, i, barCount, _peaks[i], max, true);

                                double ang = i * stepAng + angOff;
                                double x = cx + radius * Math.Cos(ang);
                                double y = cy + radius * Math.Sin(ang);

                                Canvas.SetLeft(peak, x - halfBar);

                                if (outer)
                                {
                                    Canvas.SetTop(peak, y + _peaks[i]);
                                    peak.RenderTransform = new RotateTransform(ang * DEG - 90.0, halfBar, -_peaks[i]);
                                }
                                else // InnerCircle
                                {
                                    Canvas.SetTop(peak, y - _peaks[i]);
                                    peak.RenderTransform = new RotateTransform(ang * DEG - 90.0, halfBar, _peaks[i]);
                                }
                            }
                        }
                        break;
                // Center mode 
                case VisualizationMode.Center:
                        if (_peakBarsLow == null || _peakBarsHigh == null) return;

                        double halfCanvas = VisCanvas.ActualHeight * 0.5;

                        for (int i = 0; i < _peakBarsLow.Length; i++, leftX += stepX)
                        {
                            var pLow = _peakBarsLow[i];
                            var pHigh = _peakBarsHigh[i];

                            Canvas.SetLeft(pLow, leftX);
                            Canvas.SetLeft(pHigh, leftX);

                            if (stereo)
                            {
                                Canvas.SetBottom(pLow, halfCanvas - (_peaksRight[i] * 0.5));
                                Canvas.SetBottom(pHigh, halfCanvas + (_peaks[i] * 0.5) - 2.0);
                            }
                            else
                            {
                                double peak = _peaks[i] * 0.5;
                                Canvas.SetBottom(pLow, halfCanvas - peak);
                                Canvas.SetBottom(pHigh, halfCanvas + peak - 2.0);
                            }

                            SetBarColour(pLow, i, barCount, _peaks[i], max, true);
                            SetBarColour(pHigh, i, barCount, _peaks[i], max, true, true);
                        }
                        break;
            }
        }

        private void CreateBars()
        {
            var opts = Visualizer.InstanceOptions;
            int count = opts._bars;

            if(opts._channelMode == ChannelMode.Stereo && (opts._visualizationMode == VisualizationMode.OuterCircle || opts._visualizationMode == VisualizationMode.InnerCircle))
            {
                _bars = new Rectangle[count * 2];
                _peaks = new double[count];
                VisCanvas.Children.Clear();

                for (int i = 0; i < count * 2; i++)
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
            else
            {
                _bars = new Rectangle[count];
                _peaks = new double[count];
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

            var old = _peaks;
            _peaksRight = new double[count];
            _peaksRight = new double[count];
            Array.Copy(old, _peaks, Math.Min(old.Length, count));
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

            switch (opts._visualizationMode)
            {
                case VisualizationMode.Bottom: // bottom
                    //if (_peakBars?.Length == count) return; 
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
                case VisualizationMode.Top: // top
                    //if (_peakBars?.Length == count) return; 
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

                case VisualizationMode.Center: // centered (needs two peaks per bar)
                    //if (_peakBarsLow?.Length == count && _peakBarsHigh?.Length == count) return;
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
                case VisualizationMode.OuterCircle: // Outer Circle 
                    RemovePeakBars();
                    if (opts._channelMode == ChannelMode.Stereo)
                    {
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
                    }
                    else
                    {
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
                    }
                    break;
                case VisualizationMode.InnerCircle: // Inner Circle
                    RemovePeakBars();
                    if (opts._channelMode == ChannelMode.Stereo)
                    {
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
                    }
                    else
                    {
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
                _colorGradientBrush = GetVerticalGradientBrush(Visualizer.InstanceOptions._customNoteGradientColors);
                _colorPeaksGradientBrush = GetVerticalGradientBrush(Visualizer.InstanceOptions._customPeakNoteGradientColors);
                colorArrayGradient = Visualizer.InstanceOptions._customNoteGradientColors;
                colorPeakArrayGradient = Visualizer.InstanceOptions._customPeakNoteGradientColors;

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
                
                if (rotateMode == 0 || opts._ColorChangeFreqency == 0 || opts._ColorMoveSpeed == 0) // No Movement Needed
                {
                    if (opts._ColorChangeFreqency > 0 && cfWatch.ElapsedMilliseconds > opts._ColorChangeFreqency)
                    {
                        wait = false;
                        Dispatcher.Invoke(() =>
                        {
                            var colorArr = new[] { _color1.Color, _color2.Color, _color3.Color, _color4.Color };
                            var hsvArr = new (double h, double s, double v)[4];
                            if(colorArrayGradient == null || colorPeakArrayGradient == null)
                            {
                                return;
                            }
                            var clrGradient = new Color[colorArrayGradient.Length];
                            var clrPeaksGradient = new Color[colorPeakArrayGradient.Length];

                            for (int i = 0; i < 4; i++)
                            {
                                var c = colorArr[i];
                                System.Drawing.Color sysColor = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
                                RGBtoHSV(sysColor, out double h, out double s, out double v);
                                h += opts._ColorMoveSpeed;
                                if (h >= 360) h -= 360;
                                hsvArr[i] = (h, s, v);
                            }

                            if (opts._barColorType == ColorMode.GradientVertical || opts._barColorType == ColorMode.GradientHorizontal
                                || opts._barColorType == ColorMode.GradientHeight || opts._barColorType == ColorMode.GradientPitch || opts._barColorType == ColorMode.GradientFrequency)
                            {
                                for (int i = 0; i < colorArrayGradient.Length; i++)
                                {
                                    var c = colorArrayGradient[i];
                                    System.Drawing.Color sysColor = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
                                    RGBtoHSV(sysColor, out double h, out double s, out double v);
                                    h += opts._ColorMoveSpeed;
                                    if (h >= 360) h -= 360;
                                    var clr = ColorFromHSV(h, s, v, c.A);
                                    clrGradient[i] = clr;
                                }
                                colorArrayGradient = clrGradient;
                                _colorGradientBrush = GetVerticalGradientBrush(clrGradient);
                            }

                            if (opts._peakColorType == ColorMode.GradientVertical || opts._peakColorType == ColorMode.GradientHorizontal
                                || opts._peakColorType == ColorMode.GradientHeight || opts._peakColorType == ColorMode.GradientPitch || opts._peakColorType == ColorMode.GradientFrequency)
                            {
                                for (int i = 0; i < colorPeakArrayGradient.Length; i++)
                                {
                                    var c = colorPeakArrayGradient[i];
                                    System.Drawing.Color sysColor = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
                                    RGBtoHSV(sysColor, out double h, out double s, out double v);
                                    h += opts._ColorMoveSpeed;
                                    if (h >= 360) h -= 360;
                                    var clr = ColorFromHSV(h, s, v, c.A);
                                    clrPeaksGradient[i] = clr;
                                }
                                colorPeakArrayGradient = clrPeaksGradient;
                                _colorPeaksGradientBrush = GetVerticalGradientBrush(clrPeaksGradient);
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
                            colorArrayGradient = Visualizer.InstanceOptions._customNoteGradientColors;
                            colorPeakArrayGradient = Visualizer.InstanceOptions._customPeakNoteGradientColors;
                            _gradient = GetVerticalGradientBrush(_color1.Color, _color2.Color);
                            _peakGradient = GetVerticalGradientBrush(_color3.Color, _color4.Color);
                            _colorGradientBrush = GetVerticalGradientBrush(Visualizer.InstanceOptions._customNoteGradientColors);
                            _colorPeaksGradientBrush = GetVerticalGradientBrush(Visualizer.InstanceOptions._customPeakNoteGradientColors);
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
                    var clrGradient = new Color[colorArrayGradient.Length];
                    var clrPeaksGradient = new Color[colorPeakArrayGradient.Length];

                    // Compact state tables
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

                        if (opts._barColorType == ColorMode.GradientVertical || opts._barColorType == ColorMode.GradientHorizontal
                                || opts._barColorType == ColorMode.GradientHeight || opts._barColorType == ColorMode.GradientPitch || opts._barColorType == ColorMode.GradientFrequency)
                        {
                            for (int i = 0; i < colorArrayGradient.Length - 1; i++)
                            {
                                var c = colorArrayGradient[i];
                                System.Drawing.Color sysColor = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
                                RGBtoHSV(sysColor, out double h, out double s, out double v);
                                h -= opts._ColorMoveSpeed;
                                if (h >= 360) h += 360;
                                var clr = ColorFromHSV(h, s, v, c.A);
                                clrGradient[i] = clr;
                            }
                            colorArrayGradient = clrGradient;
                            _colorGradientBrush = GetVerticalGradientBrush(clrGradient);
                        }

                        if (opts._peakColorType == ColorMode.GradientVertical || opts._peakColorType == ColorMode.GradientHorizontal
                            || opts._peakColorType == ColorMode.GradientHeight || opts._peakColorType == ColorMode.GradientPitch || opts._peakColorType == ColorMode.GradientFrequency)
                        {
                            for (int i = 0; i < colorPeakArrayGradient.Length; i++)
                            {
                                var c = colorPeakArrayGradient[i];
                                System.Drawing.Color sysColor = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
                                RGBtoHSV(sysColor, out double h, out double s, out double v);
                                h -= opts._ColorMoveSpeed;
                                if (h >= 360) h += 360;
                                var clr = ColorFromHSV(h, s, v, c.A);
                                clrPeaksGradient[i] = clr;
                            }
                            colorPeakArrayGradient = clrPeaksGradient;
                            _colorPeaksGradientBrush = GetVerticalGradientBrush(clrPeaksGradient);
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
                        if (opts._barColorType == ColorMode.GradientVertical || opts._barColorType == ColorMode.GradientHorizontal
                                || opts._barColorType == ColorMode.GradientHeight || opts._barColorType == ColorMode.GradientPitch || opts._barColorType == ColorMode.GradientFrequency)
                        {
                            for (int i = 0; i < colorArrayGradient.Length; i++)
                            {
                                var c = colorArrayGradient[i];
                                System.Drawing.Color sysColor = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
                                RGBtoHSV(sysColor, out double h, out double s, out double v);
                                h += opts._ColorMoveSpeed;
                                if (h >= 360) h -= 360;
                                var clr = ColorFromHSV(h, s, v, c.A);
                                clrGradient[i] = clr;
                            }
                            Dispatcher.Invoke(() =>
                            {
                                colorArrayGradient = clrGradient;
                                _colorGradientBrush = GetVerticalGradientBrush(clrGradient);
                            });
                        }

                        if (opts._peakColorType == ColorMode.GradientVertical || opts._peakColorType == ColorMode.GradientHorizontal
                            || opts._peakColorType == ColorMode.GradientHeight || opts._peakColorType == ColorMode.GradientPitch || opts._peakColorType == ColorMode.GradientFrequency)
                        {
                            for (int i = 0; i < colorPeakArrayGradient.Length; i++)
                            {
                                var c = colorPeakArrayGradient[i];
                                System.Drawing.Color sysColor = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
                                RGBtoHSV(sysColor, out double h, out double s, out double v);
                                h += opts._ColorMoveSpeed;
                                if (h >= 360) h -= 360;
                                var clr = ColorFromHSV(h, s, v, c.A);
                                clrPeaksGradient[i] = clr;
                            }
                            Dispatcher.Invoke(() =>
                            {
                                colorPeakArrayGradient = clrPeaksGradient;
                                _colorPeaksGradientBrush = GetVerticalGradientBrush(clrPeaksGradient);
                            });
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
                    case ColorMode.Match: // Match Bars
                        switch (Visualizer.InstanceOptions._barColorType)
                        {
                            case ColorMode.SolidColor: // Solid
                                rect.Fill = _color1;
                                break;

                            case ColorMode.DualColorVertical: // Vertical gradient
                                if (Visualizer.InstanceOptions._visualizationMode == VisualizationMode.Bottom || Visualizer.InstanceOptions._visualizationMode == VisualizationMode.InnerCircle)
                                {
                                    rect.Fill = _color1;
                                }
                                else if (Visualizer.InstanceOptions._visualizationMode == VisualizationMode.Top || Visualizer.InstanceOptions._visualizationMode == VisualizationMode.OuterCircle) 
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

                            case ColorMode.DualColorHorizontal: // Horizontal gradient
                                rect.Fill = new SolidColorBrush(
                                    Visualizer.GetGradientColor(
                                        new[] { _color1.Color, _color2.Color },
                                        (double)index / total));
                                break;

                            case ColorMode.DualColorHeight: // Height gradient
                                rect.Fill = new SolidColorBrush(
                                    Visualizer.GetGradientColor(
                                        new[] { _color1.Color, _color2.Color },
                                        (double)height / max));
                                break;
                            case ColorMode.GradientVertical:
                                rect.Fill = _colorGradientBrush;
                                break;
                            case ColorMode.GradientHorizontal:
                                rect.Fill = new SolidColorBrush(
                                    Visualizer.GetGradientColor(
                                        colorArrayGradient,
                                        (double)index / total));
                                break;
                            case ColorMode.GradientHeight:
                                rect.Fill = new SolidColorBrush(
                                    Visualizer.GetGradientColor(
                                        colorArrayGradient,
                                        (double)height / max));
                                break;
                            case ColorMode.GradientPitch: // Peak rainbow
                                rect.Fill = new SolidColorBrush(
                                    PitchDetector.GetPitchColor(PitchFreq, Visualizer.InstanceOptions._customNoteGradientColors));
                                break;
                            case ColorMode.DualColorPitch: // Peak gradient
                                rect.Fill = new SolidColorBrush(
                                    PitchDetector.GetPitchColor(PitchFreq, new[] { Visualizer.InstanceOptions._barColor1, Visualizer.InstanceOptions._barColor2 }));
                                break;
                            case ColorMode.GradientFrequency: // Frequency rainbow
                                rect.Fill = new SolidColorBrush(
                                    Visualizer.GetGradientColor(
                                        colorArrayGradient,
                                        (PitchFreq / 2200) - 0.03)); ;
                                break;
                            case ColorMode.DualColorFrequency: // Frequency gradient
                                rect.Fill = new SolidColorBrush(
                                    Visualizer.GetGradientColor(
                                        new[] { Visualizer.InstanceOptions._barColor1, Visualizer.InstanceOptions._barColor2 },
                                        (PitchFreq / 2200) - 0.03)); ;
                                break;
                        }
                        break;
                    case ColorMode.SolidColor: // Solid
                        rect.Fill = _color3;
                        break;

                    case ColorMode.DualColorVertical: // Vertical gradient
                        rect.Fill = _peakGradient;
                        break;

                    case ColorMode.DualColorHorizontal: // Horizontal gradient
                        rect.Fill = new SolidColorBrush(
                            Visualizer.GetGradientColor(
                                new[] { _color3.Color, _color4.Color },
                                (double)index / total));
                        break;

                    case ColorMode.DualColorHeight: // Height gradient
                        rect.Fill = new SolidColorBrush(
                            Visualizer.GetGradientColor(
                                new[] { _color3.Color, _color4.Color },
                                (double)height / max));
                        break;
                    case ColorMode.GradientVertical:
                        rect.Fill = _colorPeaksGradientBrush;
                        break;
                    case ColorMode.GradientHorizontal:
                        rect.Fill = new SolidColorBrush(
                            Visualizer.GetGradientColor(
                                colorPeakArrayGradient,
                                (double)index / total));
                        break;
                    case ColorMode.GradientHeight:
                        rect.Fill = new SolidColorBrush(
                            Visualizer.GetGradientColor(
                                colorPeakArrayGradient,
                                (double)height / max));
                        break;
                    case ColorMode.GradientPitch: // Peak rainbow
                        rect.Fill = new SolidColorBrush(
                            PitchDetector.GetPitchColor(PitchFreq, Visualizer.InstanceOptions._customPeakNoteGradientColors));
                        break;
                    case ColorMode.DualColorPitch: // Peak gradient
                        rect.Fill = new SolidColorBrush(
                            PitchDetector.GetPitchColor(PitchFreq, new[] { _color3.Color, _color4.Color }));
                        break;
                    case ColorMode.GradientFrequency: // Frequency rainbow
                        rect.Fill = new SolidColorBrush(
                            Visualizer.GetGradientColor(
                                colorPeakArrayGradient,
                                (PitchFreq / 2200) - 0.03)); ;
                        break;
                    case ColorMode.DualColorFrequency: // Frequency gradient
                        rect.Fill = new SolidColorBrush(
                            Visualizer.GetGradientColor(
                                new[] { _color3.Color, _color4.Color },
                                (PitchFreq / 2200) - 0.03)); ;
                        break;
                }
            }
            else
            {
                switch (Visualizer.InstanceOptions._barColorType)
                {
                    case ColorMode.SolidColor: 
                        rect.Fill = _color1;
                        break;

                    case ColorMode.DualColorVertical: 
                        rect.Fill = _gradient;
                        break;

                    case ColorMode.DualColorHorizontal: 
                        rect.Fill = new SolidColorBrush(
                            Visualizer.GetGradientColor(
                                new[] { _color1.Color, _color2.Color },
                                (double)index / total));
                        break;

                    case ColorMode.DualColorHeight: 
                        rect.Fill = new SolidColorBrush(
                            Visualizer.GetGradientColor(
                                new[] { _color1.Color, _color2.Color },
                                (double)height / max));
                        break;
                    case ColorMode.GradientVertical: 
                        rect.Fill = _colorGradientBrush;
                        break;
                    case ColorMode.GradientHorizontal: 
                        rect.Fill = new SolidColorBrush(
                            Visualizer.GetGradientColor(
                                colorArrayGradient,
                                (double)index / total));
                        break;
                    case ColorMode.GradientHeight: 
                        rect.Fill = new SolidColorBrush(
                            Visualizer.GetGradientColor(
                                colorArrayGradient,
                                (double)height / max));
                        break;
                    case ColorMode.GradientPitch: // Peak rainbow
                        rect.Fill = new SolidColorBrush(
                            PitchDetector.GetPitchColor(PitchFreq, Visualizer.InstanceOptions._customNoteGradientColors));
                        break;
                    case ColorMode.DualColorPitch: // Peak gradient
                        rect.Fill = new SolidColorBrush(
                            PitchDetector.GetPitchColor(PitchFreq, new[] {Visualizer.InstanceOptions._barColor1, Visualizer.InstanceOptions._barColor2 }));
                        break;
                    case ColorMode.GradientFrequency: // Frequency rainbow
                        rect.Fill = new SolidColorBrush(
                            Visualizer.GetGradientColor(
                                colorArrayGradient,
                                (PitchFreq / 2200) - 0.03));;
                        break;
                    case ColorMode.DualColorFrequency: // Frequency gradient
                        rect.Fill = new SolidColorBrush(
                            Visualizer.GetGradientColor(
                                new[] { Visualizer.InstanceOptions._barColor1, Visualizer.InstanceOptions._barColor2 },
                                (PitchFreq / 2200) - 0.03));;
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
            double currentElapsedMs = dTimeWatch.Elapsed.TotalMilliseconds;
            double deltaTime = (currentElapsedMs - lastElapsed) / 1000.0;
            if (markFrame)
            {
                lastElapsed = currentElapsedMs;
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
