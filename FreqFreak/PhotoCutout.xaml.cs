using LibMaterial.NET;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FreqFreak
{
    /// <summary>
    /// Interaction logic for PhotoCutout.xaml
    /// </summary>
    public partial class PhotoCutout : Window
    {
        private Color bgColor = Color.FromArgb(200, 26, 26, 26);
        private bool AllowSet = true;
        public PhotoCutout()
        {
            InitializeComponent();
            Loaded += (_, __) =>
            {
                var _hwnd = new WindowInteropHelper(this).Handle;
                var alpha = bgColor.A;
                var bgr = (uint)(bgColor.B | (bgColor.G << 8) | (bgColor.R << 16));
                LibApply.Apply_Custom_Acrylic(_hwnd, alpha: alpha, bgr: bgr);
            };

            this.Closed += (sender, e) =>
            {
                Visualizer.PhotoCutoutWindow = null;
            };

            foreach(var window in Visualizer.CutoutWindows)
            {
                if (window != null)
                {
                    CreateListItem(window.window.WindowID, window.window.CutoutImage);
                }
            }
        }

        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
                DefaultExt = ".png",
                AddExtension = true
            };
            if (openFileDialog.ShowDialog(this) != true) return;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(openFileDialog.FileName);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();

            var cw = await SpawnCutoutAsync(bmp);
            Visualizer.CutoutWindows.Add(new StupidTuple(cw.WindowID, cw));

            CreateListItem(cw.WindowID, bmp);
            CutoutList.SelectedIndex = CutoutList.Items.Count - 1;
        }
        private static Task<CutoutWindow> SpawnCutoutAsync(BitmapImage bmp)
        {
            var tcs = new TaskCompletionSource<CutoutWindow>(TaskCreationOptions.RunContinuationsAsynchronously);

            var t = new Thread(() =>
            {
                try
                {
                    var disp = Dispatcher.CurrentDispatcher;

                    var cw = new CutoutWindow(bmp);
                    cw.Closed += (_, __) => disp.BeginInvokeShutdown(DispatcherPriority.Background);

                    cw.Show();
                    tcs.TrySetResult(cw);

                    Dispatcher.Run();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();

            return tcs.Task;
        }
        public void UpdateSettings(Guid id, double x, double y, double w, double h, double bScale, double r, bool t, bool d)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (CutoutList.SelectedItem != null)
                {
                    Guid selectedGuid = (Guid)(CutoutList.SelectedItem as ListViewItem).Tag;
                    if(selectedGuid == id)
                    {
                        UpdateSettings(x, y, w, h, bScale, r, t, d);
                    }
                }
            });
        }

        public void UpdateSettings(double x, double y, double w, double h, double bScale, double r, bool t, bool d)
        {
            Dispatcher.Invoke(() =>
            {
                XPosInput.Text = x.ToString();
                YPosInput.Text = y.ToString();
                WidthInput.Text = w.ToString();
                HeightInput.Text = h.ToString();
                BassScaleInput.Text = bScale.ToString();
                RotationInput.Text = r.ToString();
                AlwaysOnTopInput.IsChecked = t;
                Draggable.IsChecked = d;
            });
        }

        private void CreateListItem(Guid id, BitmapImage bmp)
        {
            var lvi = new ListViewItem();

            lvi.Tag = id;

            var sp = new StackPanel();
            sp.Orientation = Orientation.Horizontal;
            sp.Children.Add(new Image
            {
                Source = bmp,
                Width = 150,
                Height = 150,
                Stretch = Stretch.Uniform
            });

            lvi.Content = sp;

            CutoutList.Items.Add(lvi);
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var item = (ListViewItem)CutoutList.SelectedItem;
            if (item == null)
            {
                return;
            }
            var guid = (Guid)item.Tag;

            var window = Visualizer.CutoutWindows.Find(cw => cw.id == guid);
            window?.window.Dispatcher.BeginInvoke(() =>
            {
                window.window.Close();
            });
            ClearOutWindow(guid);
        }
        public void ClearOutWindow(Guid guid)
        {
            Dispatcher.Invoke(() =>
            {
                Visualizer.CutoutWindows.RemoveAll(cw => cw.id == guid);
                CutoutList.Items.Clear();
                foreach (var window in Visualizer.CutoutWindows)
                {
                    if (window != null)
                    {
                        CreateListItem(window.id, window.window.CutoutImage);
                    }
                }
                CutoutList.SelectedIndex = CutoutList.Items.Count - 1;
                UpdateValues();
            });
        }

        private void UpdateValues()
        {
            if (!AllowSet)
            {
                return;
            }
            var item = (ListViewItem)CutoutList.SelectedItem;
            if (item == null)
            {
                return;
            }
            var guid = (Guid)item.Tag;

            double x = double.TryParse(XPosInput.Text, out var xVal) ? xVal : 0;
            double y = double.TryParse(YPosInput.Text, out var yVal) ? yVal : 0;
            double w = double.TryParse(WidthInput.Text, out var wVal) ? wVal : 0;
            double h = double.TryParse(HeightInput.Text, out var hVal) ? hVal : 0;
            double bScale = double.TryParse(BassScaleInput.Text, out var bScaleVal) ? bScaleVal : 1.0;
            double r = double.TryParse(RotationInput.Text, out var rVal) ? rVal : 0.0;
            bool t = AlwaysOnTopInput.IsChecked ?? false;
            bool d = Draggable.IsChecked ?? false;
            Visualizer.CutoutWindows.Find(cw => cw.id == guid)?.window.UpdateSettings(x, y, w, h, bScale, r, t, d);
        }

        private void Input_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateValues();
        }

        // Window re-management
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            //var offset = e.GetPosition(this);
            //DragWorkaround.StartDragging(this, offset);
            //dragHandler.BeginDrag(e);
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

        private void AlwaysOnTopInput_Checked(object sender, RoutedEventArgs e)
        {
            UpdateValues();
        }

        private void Draggable_Checked(object sender, RoutedEventArgs e)
        {
            UpdateValues();
        }

        private void Draggable_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateValues();
        }

        private void CutoutList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            AllowSet = false;
            var item = (ListViewItem)CutoutList.SelectedItem;
            if(item == null)
            {
                UpdateSettings(0, 0, 0, 0, 0, 0, false, false);
                InputGrid.IsEnabled = false;
                return;
            }
            var guid = (Guid)item.Tag;
            var window = Visualizer.CutoutWindows.Find(cw => cw.id == guid);

            if(window != null)
            {
                double x = 0;
                double y = 0;
                double w = 0;
                double h = 0;
                double bScale = 0;
                double bShake = 0;
                double r = 0;
                bool t = false;
                bool d = false;
                window.window.Dispatcher.Invoke(() =>
                {
                    x = window.window.Left;
                    y = window.window.Top;
                    w = window.window.WidthCache;
                    h = window.window.HeightCache;
                    bScale = window.window.BassScale;
                    r = window.window.Rotation;
                    t = window.window.Topmost;
                    d = window.window.DraggableCache;
                });
                UpdateSettings(x, y, w, h, bScale, r, t, d);
            }
            AllowSet = true;
            InputGrid.IsEnabled = true;
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            HelpPopup.IsOpen = true;
        }

        private void AlwaysOnTopInput_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateValues();
        }
    }
}
