using ColorPicker;
using LibMaterial.NET;
using NAudio.SoundFont;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Xaml;
using System.Xml;

namespace FreqFreak
{
    public partial class GradientEditor : Window
    {
        public bool PeakEditing = false;
        private Color bgColor = Color.FromArgb(200, 26, 26, 26);
        private Style styleCache = null;
        public GradientEditor()
        {
            InitializeComponent();
            styleCache = (Style)this.FindResource("DefaultColorPickerStyle");
            var preset = Visualizer.InstanceOptions._customNoteGradientColors;
            foreach (var color in preset)
            {
                ListViewItem item = new ListViewItem();
                CreateItem(item, color);
                ColorsList.Items.Add(item);
            }
            ColorsTextInput.Text = string.Join(", ", preset.Select(color => color.ToString()));

            GradientDisplay.Fill = MainWindow.GetHorizontalGradientBrush(Visualizer.InstanceOptions._customNoteGradientColors);
            this.Activated += (s, e) =>
            {
                this.WindowState = WindowState.Normal;
                ColorsList.Items.Clear();
                TitleBarText.Text = PeakEditing ? "Gradient Editor - Peaks" : "Gradient Editor - Bars";
                if (!PeakEditing)
                {
                    var preset = Visualizer.InstanceOptions._customNoteGradientColors;
                    foreach (var color in preset)
                    {
                        ListViewItem item = new ListViewItem();
                        CreateItem(item, color);
                        ColorsList.Items.Add(item);
                    }
                    GradientDisplay.Fill = MainWindow.GetHorizontalGradientBrush(Visualizer.InstanceOptions._customNoteGradientColors);
                }
                else
                {
                    var preset = Visualizer.InstanceOptions._customPeakNoteGradientColors;
                    foreach (var color in preset)
                    {
                        ListViewItem item = new ListViewItem();
                        CreateItem(item, color);
                        ColorsList.Items.Add(item);
                    }
                    GradientDisplay.Fill = MainWindow.GetHorizontalGradientBrush(Visualizer.InstanceOptions._customPeakNoteGradientColors);
                }
            };
            Loaded += (_, __) =>
            {
                var _hwnd = new WindowInteropHelper(this).Handle;
                var alpha = bgColor.A;
                var bgr = (uint)(bgColor.B | (bgColor.G << 8) | (bgColor.R << 16));
                LibApply.Apply_Custom_Acrylic(_hwnd, alpha: alpha, bgr: bgr);
            };
        }

        private void Grid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double width = HeaderCutoutGrid.ActualWidth;
            double height = HeaderCutoutGrid.ActualHeight;
            double radius = 5;

            var outer = new RectangleGeometry(new Rect(0, 0, width, height));
            var inner = new RectangleGeometry(new Rect(10, 2, width - 20, 25), radius, radius);

            var combined = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
            HeaderCutoutGrid.Clip = combined;
        }

        private void AddColorButton_Click(object sender, RoutedEventArgs e)
        {
            var clr = Color.FromArgb(255, 255, 255, 255);
            ListViewItem item = new ListViewItem();
            CreateItem(item, clr);
            ColorsList.Items.Add(item);
            UpdateInstanceColors();
        }
        private void UpdateInstanceColors()
        {
            var clrs = ColorsList.Items.SourceCollection.Cast<ListViewItem>()
                .Select(item => (item.Background as SolidColorBrush)?.Color)
                .Where(color => color.HasValue)
                .Select(color => color.Value)
                .ToArray();
            if (PeakEditing)
            {
                Visualizer.InstanceOptions._customPeakNoteGradientColors = clrs;
                GradientDisplay.Fill = MainWindow.GetHorizontalGradientBrush(Visualizer.InstanceOptions._customPeakNoteGradientColors);
            }
            else
            {
                Visualizer.InstanceOptions._customNoteGradientColors = clrs;
                GradientDisplay.Fill = MainWindow.GetHorizontalGradientBrush(Visualizer.InstanceOptions._customNoteGradientColors);
            }
            
            Visualizer.UpdateSettings = true;
        }
        private void CreateItem(ListViewItem lvi, Color baseColor)
        {
            // Set Padding
            lvi.Padding = new Thickness(10);
            lvi.Width = 120;

            // Normal (unselected) background
            var normalBrush = new SolidColorBrush(baseColor) { Opacity = 0.20 };
            lvi.Background = normalBrush;

            var clrpckr = new PortableColorPicker();
            clrpckr.SelectedColor = baseColor;
            clrpckr.Height = 20;
            clrpckr.Style = styleCache;
            clrpckr.Focusable = false;
            clrpckr.IsHitTestVisible = false;

            lvi.Content = clrpckr;
            lvi.Focusable = false;
            lvi.MouseDoubleClick += (s, e) =>
            {
                Popup popup = FindChildPopup(clrpckr);
                if (popup != null)
                {
                    popup.StaysOpen = true;
                    popup.IsOpen = true;
                }
            };
            lvi.PreviewMouseDown += (s, e) =>
            {
                ColorsList.SelectedItem = lvi;
            };
            clrpckr.ColorChanged += (s, e) => 
            {
                var normalBrush = new SolidColorBrush(clrpckr.SelectedColor) { Opacity = 0.20 };
                lvi.Background = normalBrush;
                UpdateInstanceColors();
            };
        }
        private static Popup FindChildPopup(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is Popup p) return p;
                var nested = FindChildPopup(child);
                if (nested != null) return nested;
            }
            return null;
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (ColorsList.SelectedItem == null)
            {
                return;
            }
            var idx = ColorsList.SelectedIndex;
            ColorsList.Items.Remove(ColorsList.SelectedItem);
            ColorsList.SelectedIndex = idx;
            UpdateInstanceColors();
        }

        private void MoveDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (ColorsList.SelectedItem == null)
            {
                return;
            }
            var temp = ColorsList.SelectedItem;
            var tempIdx = ColorsList.SelectedIndex;

            if(tempIdx == ColorsList.Items.Count - 1 || tempIdx < 0)
            {
                return; 
            }

            ColorsList.Items.Remove(temp);
            ColorsList.Items.Insert(tempIdx + 1, temp);
            ColorsList.SelectedIndex = tempIdx + 1;

            UpdateInstanceColors();
        }

        private void MoveUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (ColorsList.SelectedItem == null)
            {
                return;
            }
            var temp = ColorsList.SelectedItem;
            var tempIdx = ColorsList.SelectedIndex;

            if (tempIdx == 0 || tempIdx < 0)
            {
                return;
            }

            ColorsList.Items.Remove(temp);
            ColorsList.Items.Insert(tempIdx - 1, temp);
            ColorsList.SelectedIndex = tempIdx - 1;

            UpdateInstanceColors();
        }

        private void PresetSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var idx = PresetSelector.SelectedIndex;
            if(idx == -1)
            {
                return;
            }

            ColorsList.Items.Clear();
            Visualizer.InstanceOptions.SetColorPreset((ColorPreset)idx, PeakEditing);
            if (!PeakEditing)
            {
                var preset = Visualizer.InstanceOptions._customNoteGradientColors;
                foreach (var color in preset)
                {
                    ListViewItem item = new ListViewItem();
                    CreateItem(item, color);
                    ColorsList.Items.Add(item);
                }
            }
            else
            {
                var preset = Visualizer.InstanceOptions._customPeakNoteGradientColors;
                foreach (var color in preset)
                {
                    ListViewItem item = new ListViewItem();
                    CreateItem(item, color);
                    ColorsList.Items.Add(item);
                }
            }

            PresetSelector.SelectedIndex = -1;

            UpdateInstanceColors();
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (ColorsList.SelectedItem == null)
            {
                return;
            }
            var clrBrush = ((ListViewItem)ColorsList.SelectedItem).Background as SolidColorBrush;
            ListViewItem item = new ListViewItem();
            CreateItem(item, clrBrush.Color);
            ColorsList.Items.Insert(ColorsList.SelectedIndex, item);
            UpdateInstanceColors();
        }

        private void ReverseButton_Click(object sender, RoutedEventArgs e)
        {
            List<ListViewItem> temp = ColorsList.Items.SourceCollection.Cast<ListViewItem>().ToList();
            temp.Reverse();
            ColorsList.Items.Clear();
            foreach(var item in temp)
            {
                ColorsList.Items.Add(item);
            }

            UpdateInstanceColors();
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

        // Resize helper
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
           Dispatcher.BeginInvoke(() =>
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

        public static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parent = VisualTreeHelper.GetParent(child);

            while (parent != null)
            {
                if (parent is T typedParent)
                    return typedParent;

                parent = VisualTreeHelper.GetParent(parent);
            }

            return null;
        }

        private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            foreach(var lvi in ColorsList.Items)
            {
                Popup popup = FindChildPopup((DependencyObject)lvi);
                if (popup != null)
                {
                    popup.StaysOpen = false;
                    popup.IsOpen = false;
                }
            }
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            HelpPopup.IsOpen = true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {

        }

        private void ColorsTextInput_MouseEnter(object sender, MouseEventArgs e)
        {
            ColorsTextInput.Text = string.Join(", ", ColorsList.Items
                .SourceCollection.Cast<ListViewItem>()
                .Select(item => (item.Background as SolidColorBrush)?.Color)
                .Where(color => color.HasValue)
                .Select(color => color.Value.ToString()));
            //ColorsTextInput.Height = 90;
        }

        private void ColorsTextInput_MouseLeave(object sender, MouseEventArgs e)
        {
            ColorsTextInput.Text = string.Join(", ", ColorsList.Items
                .SourceCollection.Cast<ListViewItem>()
                .Select(item => (item.Background as SolidColorBrush)?.Color)
                .Where(color => color.HasValue)
                .Select(color => color.Value.ToString()));
        }

        private void ColorsTextInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            var hexCodes = ColorsTextInput.Text.Split(new[] { ",", "\n", " " }, StringSplitOptions.TrimEntries);
            ColorsList.Items.Clear();
            foreach (var hex in hexCodes)
            {
                if (string.IsNullOrWhiteSpace(hex)) continue;
                try
                {
                    Color color = (Color)ColorConverter.ConvertFromString(hex);
                    ListViewItem item = new ListViewItem();
                    CreateItem(item, color);
                    ColorsList.Items.Add(item);
                }
                catch (FormatException)
                {
                    return;
                }
            }
            UpdateInstanceColors();
        }
    }
}
