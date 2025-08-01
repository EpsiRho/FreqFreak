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

namespace FreqFreak
{
    /// <summary>
    /// Interaction logic for CutoutWindow.xaml
    /// </summary>
    public partial class CutoutWindow : Window
    {
        public CancellationTokenSource cts = new CancellationTokenSource();
        private Thread EffectsThread;
        public double WidthCache = 400;
        public double HeightCache = 400;
        public double BassScale = 1.0;
        public double BassShake = 0.0;
        public double Rotation = 0;
        public bool DraggableCache = true;
        public Guid WindowID = Guid.NewGuid();
        public IntPtr _hwnd = -1;
        public BitmapImage CutoutImage;
        public NormalDragHandler dragHandler;
        public CutoutWindow(BitmapImage img)
        {
            dragHandler = new(this);
            InitializeComponent();
            MouseLeftButtonDown += (s, e) =>
            {
                //var offset = e.GetPosition(this);
                //DragWorkaround.StartDragging(this, offset);
                dragHandler.BeginDrag(e);
            };
            MouseLeftButtonUp += (_, __) =>
            {
                dragHandler.EndDrag();
                if(Visualizer.PhotoCutoutWindow != null)
                {
                    Visualizer.PhotoCutoutWindow.UpdateSettings(WindowID, Left, Top, WidthCache, HeightCache, BassScale, BassShake, Rotation, Topmost, DraggableCache);
                }
            };

            Closed += (_, __) =>
            {
                cts.Cancel();
                Visualizer.PhotoCutoutWindow.ClearOutWindow(WindowID);
            };

            Loaded += (_, __) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                EffectsThread = new Thread(() =>
                {
                    RenderEffects();
                });
                EffectsThread.Start();

                CutoutImage = img;
                MainImage.Source = img;
                MainImage.Width = 400;
                MainImage.Height = 400;
                WidthCache = 400;
                HeightCache = 400;
                MainImage.Stretch = Stretch.Uniform;
            };

            double left = 0;
            double top = 0;
            (left, top) = Visualizer.MainWin.GetWindowPosition(this, Dispatcher, 400, 400);
            Left = left;
            Top = top;
        }
        public void UpdateSettings(double x, double y, double w, double h, double bScale, double bShake, double r, bool t, bool d)
        {
            WidthCache = w;
            HeightCache = h;
            BassScale = bScale;
            BassShake = bShake;
            DraggableCache = d;
            Dispatcher.Invoke(() =>
            {
                Left = x;
                Top = y;
                Width = w * BassScale;
                Height = h * BassScale;
                MainImage.Width = w;
                MainImage.Height = h;
                MainGrid.Width = w * BassScale;
                MainGrid.Height = h * BassScale;
                Rotation = r;
                Topmost = t;
                MainGrid.IsHitTestVisible = d;
                IsHitTestVisible = d;
                ClickThrough.Toggle(_hwnd, !d);
            });
        }


        public void RenderEffects()
        { 
            while (!cts.Token.IsCancellationRequested)
            {
                double angleDegrees = Rotation;
                double angleRadians = angleDegrees * Math.PI / 180;

                double originalWidth = WidthCache;
                double originalHeight = HeightCache;

                double newWidth = Math.Abs(originalWidth * Math.Cos(angleRadians)) + Math.Abs(originalHeight * Math.Sin(angleRadians));
                double newHeight = Math.Abs(originalWidth * Math.Sin(angleRadians)) + Math.Abs(originalHeight * Math.Cos(angleRadians));

                //var normalized = BassAmplitude / Visualizer.InstanceOptions._height;
                var scale = 1 + (MainWindow.BassAmplitude * (BassScale - 1));

                Dispatcher.Invoke(() =>
                {
                    Width = newWidth * BassScale;
                    Height = newHeight * BassScale;
                    MainGridRotation.Angle = angleDegrees;
                    MainGridScale.ScaleX = scale;
                    MainGridScale.ScaleY = scale;
                });
            }
        }
    }
}
