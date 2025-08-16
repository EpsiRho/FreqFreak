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

        public static double[] _peaks = Array.Empty<double>();
        public static double[] _peaksRight = Array.Empty<double>();
        private object _peakLock = new(); 
        private Task _peakDecayTask;
        private Task _ColorMoveTask;

        private static readonly Random _rng = new();

        public IntPtr _hwnd = -1;
        public static TrayIconWithContextMenu? _trayIcon;
        private static System.Drawing.Icon? _icon;
        public static Color _color1 = new(); // Bars 1
        public static Color _color2 = new(); // Bars 2
        public static Color _color3 = new(); // Peaks 1
        public static Color _color4 = new(); // Peaks 2
        public static Color[] colorArrayGradient = new Color[12]; // Rainbow / Custom gradients(?)
        public static Color[] colorPeakArrayGradient = new Color[12]; // Rainbow / Custom gradients(?)
        //public static LinearGradientBrush _colorGradientBrush = new(); // Rainbow gradient brush for vertical
        //public static LinearGradientBrush _colorPeaksGradientBrush = new(); // Rainbow gradient brush for vertical
        //public static LinearGradientBrush _gradient = new();
        //public static LinearGradientBrush _peakGradient = new();
        public static double PitchFreq = 0;
        public static double BassAmplitude = 0;
        public static string PitchText = "None";
        private static bool _failure = false;
        public static bool _lineSwiitch = false;
        public NormalDragHandler dragHandler;
        public PopupMenuItem toggleVis = new PopupMenuItem();


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
                    if (_ColorMoveTask == null || _ColorMoveTask.IsCompleted)
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


        // Tool Window Creators
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
                Visualizer.AudioDevicesWindow.Dispatcher.Invoke(() => {
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
                Visualizer.AudioDevicesWindow.Dispatcher.Invoke(() =>
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

        // Window Position Helpers
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

                    //max = curMax;
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
        public static Rect CalculateBoundingBox(Point[] corners, float angleDegrees)
        {
            // Rotate all corners
            Point[] rotatedCorners = new Point[4];
            for (int i = 0; i < corners.Length; i++)
            {
                rotatedCorners[i] = RotatePoint((float)corners[i].X, (float)corners[i].Y, angleDegrees);
            }

            // Find min and max x and y
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            foreach (var corner in rotatedCorners)
            {
                minX = (float)Math.Min(minX, corner.X);
                maxX = (float)Math.Max(maxX, corner.X);
                minY = (float)Math.Min(minY, corner.Y);
                maxY = (float)Math.Max(maxY, corner.Y);
            }

            float boundingWidth = maxX - minX;
            float boundingHeight = maxY - minY;

            return new Rect
            {
                Width = boundingWidth,
                Height = boundingHeight,
                X = minX,
                Y = minY
            };
        }
        public static Point RotatePoint(float x, float y, float angleDegrees)
        {
            double rad = angleDegrees * Math.PI / 180.0;
            float cos = (float)Math.Cos(rad);
            float sin = (float)Math.Sin(rad);

            // Apply rotation matrix
            // [x'] = [cos θ  -sin θ] [x]
            // [y'] = [sin θ   cos θ] [y]
            float newX = x * cos - y * sin;
            float newY = x * sin + y * cos;

            return new Point(newX, newY);
        }


        // Render Pipe
        private void OnRender(object? sender, EventArgs e)
        {
            if (NormalDragHandler.IsDragging)
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

            if (Visualizer.InstanceOptions._detectPitch)
            {
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
                        try
                        {
                            var arr = _peaks.ToArray();
                            var arr2 = _peaksRight.ToArray();
                            var binCenters = PitchDetector.CalculateBinCenters(binEdges);
                            var pitchInfo = PitchDetector.DetectPitch(arr, binCenters);
                            PitchText = pitchInfo.note;
                            PitchFreq = pitchInfo.frequency;

                            var BassAmplitudeL = PitchDetector.DetectBassAmplitude(arr, binCenters, Visualizer.InstanceOptions._height);
                            var BassAmplitudeR = PitchDetector.DetectBassAmplitude(arr2, binCenters, Visualizer.InstanceOptions._height);
                            BassAmplitude = (BassAmplitudeL + BassAmplitudeR) / 2;
                        }
                        catch (Exception)
                        {

                        }
                    }

                }
            }

            try
            {
                if (Visualizer.InstanceOptions._visualizationMode != VisualizationMode.Oscilloscope)
                {
                    if (Visualizer.UpdateSettings)
                    {
                        return;
                    }
                    VisCanvas.UpdatePlane(frame, frameR);
                }

                CenterLine.Fill = new SolidColorBrush(_color3);
                UpdateRotation();
            }
            catch (Exception)
            {

            }
        }
        private void ResizeBars()
        {
            try
            {
                Visualizer.UpdateSettings = false;
                var opts = Visualizer.InstanceOptions;

                _color1 = opts._barColor1;
                _color2 = opts._barColor2;
                _color3 = opts._peakColor;
                _color4 = opts._peakColor2;
                //_gradient = GetVerticalGradientBrush(_color1.Color, _color2.Color);
                //_peakGradient = GetVerticalGradientBrush(_color3.Color, _color4.Color);
                //_colorGradientBrush = GetVerticalGradientBrush(Visualizer.InstanceOptions._customNoteGradientColors);
                //_colorPeaksGradientBrush = GetVerticalGradientBrush(Visualizer.InstanceOptions._customPeakNoteGradientColors);
                colorArrayGradient = Visualizer.InstanceOptions._customNoteGradientColors;
                colorPeakArrayGradient = Visualizer.InstanceOptions._customPeakNoteGradientColors;

                CreateBars();
            }
            catch (Exception)
            {

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
                //VisCanvas.Children.Clear();

                for (int i = 0; i < count * 2; i++)
                {
                    var rect = new Rectangle
                    {
                        Width = opts._barWidth,
                        Height = opts._minHeight,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(rect, i * (opts._barWidth + opts._barGap) + 1);
                    //VisCanvas.Children.Add(rect);
                    _bars[i] = rect;
                }
            }
            else
            {
                _bars = new Rectangle[count];
                _peaks = new double[count];
                //VisCanvas.Children.Clear();

                for (int i = 0; i < count; i++)
                {
                    var rect = new Rectangle
                    {
                        Width = opts._barWidth,
                        Height = opts._minHeight,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(rect, i * (opts._barWidth + opts._barGap) + 1);
                    //VisCanvas.Children.Add(rect);
                    _bars[i] = rect;
                }
            }

            var old = _peaks;
            _peaksRight = new double[count];
            _peaksRight = new double[count];
            Array.Copy(old, _peaks, Math.Min(old.Length, count));
        }
        private void UpdateRotation()
        {
            float angleDegrees = (float)Visualizer.InstanceOptions._rotation;
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

            var boundBox = CalculateBoundingBox(new Point[]
            {
                new Point(0,0),
                new Point(originalWidth,0),
                new Point(0,originalHeight),
                new Point(originalWidth,originalHeight),
            }, angleDegrees);

            newWidth = boundBox.Width;
            newHeight = boundBox.Height;

            switch (Visualizer.InstanceOptions._visualizationMode)
            {
                case VisualizationMode.OuterCircle:
                    MainGrid.Width = (totalRadius * 2) * Visualizer.InstanceOptions._bassScale;
                    MainGrid.Height = (totalRadius * 2) * Visualizer.InstanceOptions._bassScale;
                    Width = MainGrid.Width;
                    Height = MainGrid.Height;
                    //MainGridRotation.Angle = angleDegrees;
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
                    //MainGridRotation.Angle = angleDegrees;
                    MainGridScale.ScaleX = scale;
                    MainGridScale.ScaleY = scale;
                    VisCanvas.Width = MainGrid.Width;
                    VisCanvas.Height = MainGrid.Height;
                    (MainGridTranslation.X, MainGridTranslation.Y) = GetRandomXY();
                    break;
                case VisualizationMode.Oscilloscope:
                    MainGrid.Width = barCount * Visualizer.InstanceOptions._bassScale;
                    MainGrid.Height = originalHeight * Visualizer.InstanceOptions._bassScale;
                    Width = barCount * Visualizer.InstanceOptions._bassScale;
                    Height = originalHeight * Visualizer.InstanceOptions._bassScale;
                    //MainGridRotation.Angle = angleDegrees;
                    MainGridScale.ScaleX = scale;
                    MainGridScale.ScaleY = scale;
                    OscView.Width = MainGrid.Width;
                    OscView.Height = MainGrid.Height;
                    (MainGridTranslation.X, MainGridTranslation.Y) = GetRandomXY();
                    break;
                default:
                    MainGrid.Width = newWidth * scale;
                    MainGrid.Height = newHeight * scale;
                    Width = newWidth * Visualizer.InstanceOptions._bassScale;
                    Height = newHeight * Visualizer.InstanceOptions._bassScale;
                    //MainGridRotation.Angle = angleDegrees;
                    MainGridScale.ScaleX = scale;
                    MainGridScale.ScaleY = scale;
                    VisCanvas.Width = newWidth;
                    VisCanvas.Height = newHeight;
                    (MainGridTranslation.X, MainGridTranslation.Y) = GetRandomXY();
                    break;
            }
        }


        // Color Animation + Delta time helpers for moving colors at the rate specified
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
                            var colorArr = new[] { _color1, _color2, _color3, _color4 };
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
                                //_colorGradientBrush = GetVerticalGradientBrush(clrGradient);
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
                                //_colorPeaksGradientBrush = GetVerticalGradientBrush(clrPeaksGradient);
                            }

                            _color1 = ColorFromHSV(hsvArr[0].h, hsvArr[0].s, hsvArr[0].v, _color1.A);
                            _color2 = ColorFromHSV(hsvArr[1].h, hsvArr[1].s, hsvArr[1].v, _color2.A);
                            _color3 = ColorFromHSV(hsvArr[2].h, hsvArr[2].s, hsvArr[2].v, _color3.A);
                            _color4 = ColorFromHSV(hsvArr[3].h, hsvArr[3].s, hsvArr[3].v, _color4.A);
                            //_gradient = GetVerticalGradientBrush(_color1.Color, _color2.Color);
                            //_peakGradient = GetVerticalGradientBrush(_color3.Color, _color4.Color);
                        });
                        cfWatch.Restart();

                    }
                    else
                    {

                        if (wait) continue;
                        wait = true;
                        Dispatcher.Invoke(() =>
                        {
                            _color1 = opts._barColor1;
                            _color2 = opts._barColor2;
                            _color3 = opts._peakColor;
                            _color4 = opts._peakColor2;
                            colorArrayGradient = Visualizer.InstanceOptions._customNoteGradientColors;
                            colorPeakArrayGradient = Visualizer.InstanceOptions._customPeakNoteGradientColors;
                            //_gradient = GetVerticalGradientBrush(_color1.Color, _color2.Color);
                            //_peakGradient = GetVerticalGradientBrush(_color3.Color, _color4.Color);
                            //_colorGradientBrush = GetVerticalGradientBrush(Visualizer.InstanceOptions._customNoteGradientColors);
                            //_colorPeaksGradientBrush = GetVerticalGradientBrush(Visualizer.InstanceOptions._customPeakNoteGradientColors);
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
                            //_colorGradientBrush = GetVerticalGradientBrush(clrGradient);
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
                            //_colorPeaksGradientBrush = GetVerticalGradientBrush(clrPeaksGradient);
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
                                //_colorGradientBrush = GetVerticalGradientBrush(clrGradient);
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
                                //_colorPeaksGradientBrush = GetVerticalGradientBrush(clrPeaksGradient);
                            });
                        }
                    }

                    Dispatcher.Invoke(() =>
                    {
                        _color1 = c1;
                        _color2 = c2;
                        _color3 = c3;
                        _color4 = c4;
                        //_gradient = GetVerticalGradientBrush(_color1.Color, _color2.Color);
                        //_peakGradient = GetVerticalGradientBrush(_color3.Color, _color4.Color);
                    });
                }
                Thread.Sleep(16); // 60 fps
            }
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
