using LibMaterial.NET;
using NAudio.CoreAudioApi;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace FreqFreak
{
    public partial class AudioDevices : Window
    {
        private Color bgColor = Color.FromArgb(200, 26, 26, 26);
        public Dispatcher _audioDispatcher;
        public AudioDevices()
        {
            InitializeComponent();

            Loaded += (_, __) =>
            {
                var _hwnd = new WindowInteropHelper(this).Handle;
                //LibApply.Apply_Backdrop_Effect(HWnd: _hwnd, BackdropFlag: LibImport.DwmSystemBackdropTypeFlgs.DWMSBT_TRANSIENTWINDOW);
                //LibApply.Apply_Light_Theme(HWnd: _hwnd, Dark: false);
                var alpha = bgColor.A;
                var bgr = (uint)(bgColor.B | (bgColor.G << 8) | (bgColor.R << 16));
                LibApply.Apply_Custom_Acrylic(_hwnd, alpha: alpha, bgr: bgr);
            };

            Refresh();
        }
        private void Refresh()
        {
            OutputDevicesList.Items.Clear();
            InputDevicesList.Items.Clear();
            AudioAppsList.Items.Clear();
            var output = Visualizer.GetOutputDevices();
            var input = Visualizer.GetInputDevices();
            var apps = Visualizer.GetAudioApps();


            foreach (var op in output)
            {
                OutputDevicesList.Items.Add(op);
            }

            foreach (var ip in input)
            {
                InputDevicesList.Items.Add(ip);
            }

            foreach (var app in apps)
            {
                AudioAppsList.Items.Add(app);
            }

            if (Visualizer._audioDevice == null)
            {
                MMDeviceEnumerator e = new();
                var def = e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
                string selectedInput = def?.FriendlyName;
                var first = OutputDevicesList.Items.SourceCollection.Cast<string>().FirstOrDefault(x => x == selectedInput);
                if (first != null)
                {
                    OutputDevicesList.SelectedItem = first;
                }
            }

            if (Visualizer.isInput)
            {
                string selectedInput = Visualizer._audioDevice?.FriendlyName;
                var first = InputDevicesList.Items.SourceCollection.Cast<string>().FirstOrDefault(x => x == selectedInput);
                if (first != null)
                {
                    InputDevicesList.SelectedItem = first;
                }
            }
            else if (Visualizer.SelectedApp != "")
            {
                string selectedInput = Visualizer.SelectedApp;
                var first = AudioAppsList.Items.SourceCollection.Cast<string>().FirstOrDefault(x => x == selectedInput);
                if (first != null)
                {
                    AudioAppsList.SelectedItem = first;
                }
            }
            else
            {
                string selectedInput = Visualizer._audioDevice?.FriendlyName;
                var first = OutputDevicesList.Items.SourceCollection.Cast<string>().FirstOrDefault(x => x == selectedInput);
                if (first != null)
                {
                    OutputDevicesList.SelectedItem = first;
                }
            }

            CurrentDeviceText.Text = Visualizer._audioDevice == null ? "Current: Default Device" : $"Current: {Visualizer._audioDevice.FriendlyName}";
        }

        private void OutputDevicesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OutputDevicesList.SelectedItem == null) return;
            InputDevicesList.SelectedIndex = -1;
            AudioAppsList.SelectedIndex = -1;
            string item = (string)OutputDevicesList.SelectedItem;
            Task t = new Task(() =>
            {
                Visualizer.SelectedApp = "";
                Visualizer.UpdateSettings = true;
                Visualizer.isInput = false;
                Visualizer.SelectDevice(item);
                Visualizer._captureCTS.Cancel();
                Visualizer._captureCTS = new();
                var _captureThread = new Thread(() =>
                {
                    Visualizer.StartCapture(Visualizer._captureCTS.Token);
                });
                _captureThread.Start();

                _audioDispatcher.BeginInvoke(() =>
                {
                    CurrentDeviceText.Text = Visualizer._audioDevice == null ? "Error Setting Device" : $"Current: {Visualizer._audioDevice.FriendlyName}";
                });
            });
            t.Start();
        }

        private void InputDevicesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (InputDevicesList.SelectedItem == null) return;
            OutputDevicesList.SelectedIndex = -1;
            AudioAppsList.SelectedIndex = -1;
            string item = (string)InputDevicesList.SelectedItem;
            Task t = new Task(() =>
            {
                Visualizer.SelectedApp = "";
                Visualizer.UpdateSettings = true;
                Visualizer.isInput = true;
                Visualizer.SelectDevice(item);
                Visualizer._captureCTS.Cancel();
                Visualizer._captureCTS = new();
                var _captureThread = new Thread(() =>
                {
                    Visualizer.StartCapture(Visualizer._captureCTS.Token);
                });
                _captureThread.Start();

                _audioDispatcher.BeginInvoke(() =>
                {
                    CurrentDeviceText.Text = Visualizer._audioDevice == null ? "Error Setting Device!" : $"Current: {Visualizer._audioDevice.FriendlyName}";
                });
            });
            t.Start();
        }

        private void AudioAppsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AudioAppsList.SelectedItem == null) return;
            InputDevicesList.SelectedIndex = -1;
            OutputDevicesList.SelectedIndex = -1;
            var slct = (string)AudioAppsList.SelectedItem;
            Task t = new Task(() =>
            {
                Visualizer.SelectedApp = "";
                Visualizer.UpdateSettings = true;
                Visualizer.isInput = false;
                if (slct == "All") Visualizer.SelectedApp = "";
                else Visualizer.SelectedApp = slct;
                Visualizer._captureCTS.Cancel();
                Visualizer._captureCTS = new();
                var _captureThread = new Thread(() =>
                {
                    Visualizer.StartCapture(Visualizer._captureCTS.Token);
                });
                _captureThread.Start();

                _audioDispatcher.BeginInvoke(() =>
                {
                    CurrentDeviceText.Text = Visualizer.SelectedApp == null ? "Error Setting Device!" : $"Current: {Visualizer.SelectedApp}";
                });
            });
            t.Start();
        }

        // Window re-management
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            //dragHandler.EndDrag();
        }

        private Point _dragStart;
        private Rect _startRect;

        private void Resize_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Mouse.Capture(sender as IInputElement))
            {
                _dragStart = PointToScreen(e.GetPosition(this));
                _startRect = new Rect(Left, Top, ActualWidth, ActualHeight);
                MouseMove += OnResizeMouseMove;
                MouseLeftButtonUp += OnResizeMouseLeftButtonUp;
                WindowBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200));
            }
        }
        Color[] rainbow = new Color[]
                        {
                        Color.FromArgb(255, 255, 0, 255),    // A# - Purple
                        Color.FromArgb(255, 128, 0, 255),    // A  - Blue-Purple
                        Color.FromArgb(255, 0,   0, 255),    // G# - Blue
                        Color.FromArgb(255, 0, 128, 255),    // G  - Cyan-Blue
                        Color.FromArgb(255, 0, 255, 255),    // F# - Cyan
                        Color.FromArgb(255, 0, 255, 128),    // F  - Green-Cyan
                        Color.FromArgb(255,   0, 255, 0),    // E  - Green
                        Color.FromArgb(255, 128, 255, 0),    // D# - Yellow-Green
                        Color.FromArgb(255, 255, 255, 0),    // D  - Yellow
                        Color.FromArgb(255, 255, 128, 0),    // C# - Red-Orange
                        Color.FromArgb(255, 255,   0, 0),    // C  - Red
                        };
        private void OnResizeMouseMove(object? o, MouseEventArgs e)
        {
            this.Dispatcher.BeginInvoke(() =>
            {
                Point current = PointToScreen(e.GetPosition(this));
                Vector delta = current - _dragStart;

                // Which edge are we dragging?
                FrameworkElement fe = (FrameworkElement)Mouse.Captured;
                if (fe == null)
                {
                    return;
                }

                bool left = fe.Name.Contains("Left");
                bool right = fe.Name.Contains("Right");
                bool top = fe.Name.Contains("Top");
                bool bottom = fe.Name.Contains("Bottom");

                Rect r = _startRect;

                if (left) { r.X += delta.X; r.Width -= delta.X; }
                if (right) { r.Width += delta.X; }
                if (top) { r.Y += delta.Y; r.Height -= delta.Y; }
                if (bottom) { r.Height += delta.Y; }

                // Don't let it get negative
                if (r.Width > MinWidth)
                {
                    Left = r.X;
                    Width = r.Width;
                }
                else
                {
                    r.Width = MinWidth;
                    Left = r.X;
                    Width = r.Width;
                }

                if (r.Height > MinHeight) { Top = r.Y; Height = r.Height; }


                WindowBorder.BorderBrush = new SolidColorBrush(Visualizer.GetGradientColor(rainbow, ((1000000 - (r.Width * r.Height)) / 550000)));
            });
        }
        private void OnResizeMouseLeftButtonUp(object? o, MouseButtonEventArgs e)
        {
            MouseMove -= OnResizeMouseMove;
            MouseLeftButtonUp -= OnResizeMouseLeftButtonUp;
            Mouse.Capture(null);
            WindowBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 126, 126, 126));
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            Refresh();
        }
    }
}
