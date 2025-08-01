using System;
using System.Windows.Media;
using System.Windows;

namespace FreqFreak
{
    public class OscilloscopeControl : FrameworkElement
    {
        private readonly Queue<DrawingVisual> _visualPool = new();
        private readonly List<(DrawingVisual Visual, DateTime Timestamp)> _layers = new();
        private readonly VisualCollection _children;

        // Pre-allocate reusable objects
        public Pen _pen;
        private readonly StreamGeometry _geometry = new();

        public OscilloscopeControl()
        {
            _children = new VisualCollection(this);
            _pen = new Pen(new SolidColorBrush(Visualizer.InstanceOptions._barColor1), 1);

            // Pre-populate visual pool
            for (int i = 0; i < 10; i++)
            {
                _visualPool.Enqueue(new DrawingVisual());
            }
        }

        protected override int VisualChildrenCount => _children.Count;
        protected override Visual GetVisualChild(int index) => _children[index];


        private DrawingVisual GetVisual()
        {
            return _visualPool.Count > 0 ? _visualPool.Dequeue() : new DrawingVisual();
        }

        private void ReturnVisual(DrawingVisual visual)
        {
            visual.Opacity = 1.0; // Reset
            _visualPool.Enqueue(visual);
        }
        private void UpdateColor(double max)
        {
            switch (Visualizer.InstanceOptions._barColorType)
            {
                case ColorMode.SolidColor:
                    _pen.Brush = MainWindow._color1;
                    break;

                case ColorMode.DualColorVertical:
                    _pen.Brush = MainWindow._gradient;
                    break;

                case ColorMode.DualColorHorizontal:
                    _pen.Brush = new LinearGradientBrush(MainWindow._color1.Color, MainWindow._color2.Color, 0);
                    break;

                case ColorMode.DualColorHeight:
                    _pen.Brush = new SolidColorBrush(Visualizer.GetGradientColor(
                            new[] { MainWindow._color1.Color, MainWindow._color2.Color },
                            (double)max / 0.7));
                    break;
                case ColorMode.GradientVertical:
                    _pen.Brush = MainWindow._colorGradientBrush;
                    break;
                case ColorMode.GradientHorizontal:
                    _pen.Brush = MainWindow.GetHorizontalGradientBrush(MainWindow.colorArrayGradient);
                    break;
                case ColorMode.GradientHeight:
                    _pen.Brush = new SolidColorBrush(Visualizer.GetGradientColor(MainWindow.colorArrayGradient,
                            (double)max / 0.7));
                    break;
                case ColorMode.GradientPitch: // Peak rainbow
                    _pen.Brush = new SolidColorBrush(
                        PitchDetector.GetPitchColor(MainWindow.PitchFreq, Visualizer.InstanceOptions._customNoteGradientColors));
                    break;
                case ColorMode.DualColorPitch: // Peak gradient
                    _pen.Brush = new SolidColorBrush(
                        PitchDetector.GetPitchColor(MainWindow.PitchFreq, new[] { Visualizer.InstanceOptions._barColor1, Visualizer.InstanceOptions._barColor2 }));
                    break;
                case ColorMode.GradientFrequency: // Frequency rainbow
                    _pen.Brush = new SolidColorBrush(
                        Visualizer.GetGradientColor(
                            MainWindow.colorArrayGradient,
                            (MainWindow.PitchFreq / 2200) - 0.03)); ;
                    break;
                case ColorMode.DualColorFrequency: // Frequency gradient
                    _pen.Brush = new SolidColorBrush(
                        Visualizer.GetGradientColor(
                            new[] { Visualizer.InstanceOptions._barColor1, Visualizer.InstanceOptions._barColor2 },
                            (MainWindow.PitchFreq / 2200) - 0.03)); ;
                    break;
            }
        }

        public void UpdatePlane(double[] frameL, double[] frameR)
        {
            if (frameL == null && frameR == null)
            {
                return;
            }
            else if (frameL.Length == 0 || frameR.Length == 0)
            {
                return;
            }

            var maxL = frameL.Select(x => Math.Abs(x)).Max();
            var maxR = frameR.Select(x => Math.Abs(x)).Max();
            var max = maxL > maxR ? maxL : maxR;

            UpdateColor(max);

            _pen.DashCap = PenLineCap.Round;
            _pen.Thickness = 1;
            _pen.EndLineCap = PenLineCap.Round;
            _pen.StartLineCap = PenLineCap.Round;

            var now = DateTime.Now;
            var fadeMs = 250; // Fade time

            // Clean up old layers
            for (int i = _layers.Count - 1; i >= 0; i--)
            {
                var age = (now - _layers[i].Timestamp).TotalMilliseconds;
                if (age > fadeMs)
                {
                    _children.Remove(_layers[i].Visual);
                    ReturnVisual(_layers[i].Visual);
                    _layers.RemoveAt(i);
                }
                else if (i > 150)
                {
                    _children.Remove(_layers[i].Visual);
                    ReturnVisual(_layers[i].Visual);
                    _layers.RemoveAt(i);
                }
                else
                {
                    var opacity = Math.Pow(1.0 - (age / fadeMs), 2); // Exponential fade
                    _layers[i].Visual.Opacity = opacity;
                }
            }

            const double JUMP_THRESHOLD = 50; // When to break a line and draw nothing before starting at a new point

            var visual = GetVisual();
            using (var dc = visual.RenderOpen())
            {
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    if (frameL.Length > 0)
                    {
                        var centerX = ActualWidth * 0.5;
                        var centerY = ActualHeight * 0.5;

                        var prevX = centerX + (frameL[0] * centerX); // Center + deflection
                        var prevY = centerY - (frameR[0] * centerY); // (Y inverted)

                        ctx.BeginFigure(new Point(prevX, prevY), false, false);

                        for (int i = 1; i < frameL.Length; i++)
                        {
                            var x = centerX + (frameL[i] * centerX);
                            var y = centerY - (frameR[i] * centerY);

                            var distance = Math.Sqrt(Math.Pow(x - prevX, 2) + Math.Pow(y - prevY, 2));

                            if (distance > JUMP_THRESHOLD)
                            {
                                ctx.BeginFigure(new Point(x, y), false, false);
                            }
                            else
                            {
                                ctx.LineTo(new Point(x, y), true, false);
                            }

                            prevX = x;
                            prevY = y;
                        }
                    }
                }
                geometry.Freeze();
                dc.DrawGeometry(null, _pen, geometry);
            }

            _layers.Add((visual, now));
            _children.Add(visual);
        }
    }
}
