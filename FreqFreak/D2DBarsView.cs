using System.Collections.Concurrent;
using System.Numerics;
using System.Windows;
using System.Windows.Media.Media3D;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.Wpf;

namespace FreqFreak
{
    public class FrequencyVorticeControl : DrawingSurface
    {
        private class Layer
        {
            public Layer(int barLen, double m)
            {
                max = m;
                Bars = new Vortice.Mathematics.Rect[barLen];
                BarsL = new Vortice.Mathematics.Rect[barLen];
                PeaksL = new Vortice.Mathematics.Rect[barLen];
                PeaksR = new Vortice.Mathematics.Rect[barLen];
                BarProperties = new (double, double, double)[barLen];
            }
            public Vortice.Mathematics.Rect[] Bars;
            public Vortice.Mathematics.Rect[] BarsL;
            public Vortice.Mathematics.Rect[] PeaksL;
            public Vortice.Mathematics.Rect[] PeaksR;
            public List<Vector2> leftPoints = new(); 
            public List<Vector2> rightPoints = new();
            public (double BarL, double BarR, double BarM)[] BarProperties; 
            public double max;
            public ID2D1Brush? barBrushM; 
            public ID2D1Brush? barBrushL;
            public ID2D1Brush? peakBrushM; 
            public ID2D1Brush? peakBrushL;
            public ID2D1Brush? lineBrushM; 
            public ID2D1Brush? lineBrushL;
        }

        private ConcurrentQueue<Layer> _layerQueue = new();
        private Layer _previousLayer;

        private ID2D1Device? _d2dDevice;
        private ID2D1DeviceContext? _d2dContext;
        private ID2D1Bitmap1? _targetBitmap;
        public System.Windows.Media.Color ClearColor { get; set; } = System.Windows.Media.Color.FromArgb(0, 0, 0, 0);
        private const double PI_OVER_TWO = Math.PI / 2;

        public FrequencyVorticeControl()
        {
            LoadContent += OnLoadContent;
            Draw += OnDraw;
            UnloadContent += OnUnloadContent;
        }
        private void OnLoadContent(object? sender, DrawingSurfaceEventArgs e)
        {
            // Ensure any old resources are disposed before creating new ones.
            DisposeD2DResources();

            // Setup Direct 2D device and context.
            using IDXGIDevice dxgiDevice = e.Device.QueryInterface<IDXGIDevice>();
            _d2dDevice = D2D1.D2D1CreateDevice(dxgiDevice);
            _d2dContext = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
            _d2dContext.UnitMode = UnitMode.Pixels;
            _d2dContext.AntialiasMode = AntialiasMode.PerPrimitive;

            // Create the target bitmap tied to the current color texture.
            CreateTargetBitmap();

            // Draw for the first time
            Invalidate();
        }

        private void OnUnloadContent(object? sender, DrawingSurfaceEventArgs e)
        {
            DisposeD2DResources();
        }

        private bool IsUnsafeBrush(ID2D1Brush brush)
        {
            // Disposed or never set
            if (brush is null || brush.NativePointer == IntPtr.Zero) return true;

            // The brush was created on a different factory
            var brushFactory = brush.Factory;
            var currentFactory = _d2dDevice?.Factory;
            if (brushFactory is null || currentFactory is null) return true;
            if (brushFactory.NativePointer != currentFactory.NativePointer) return true;

            return false;
        }
        private void SetBrushTransform(bool peak, int brushType, ID2D1Brush brush, Vortice.Mathematics.Rect rect, float maxH, float maxW, float val = 0)
        {
            if (peak)
            {
                if (brushType == 1) // horizontal: global gradient spanning entire chart width
                {
                    var transform = Matrix3x2.CreateScale(maxW, 1f) *
                                    Matrix3x2.CreateTranslation(0, 0);

                    brush.Transform = transform;
                }
                else if (brushType == 2) // vertical
                {
                    var transform = Matrix3x2.CreateScale(1f, maxH) *
                                    Matrix3x2.CreateTranslation(0, 0);

                    brush.Transform = transform;
                }
                else if (brushType == 3) // height
                {
                    float t = Math.Clamp(val / maxH, 0f, 1f);
                    float BIG = 1e6f;

                    var transform = Matrix3x2.CreateScale(BIG, 1f) *
                                    Matrix3x2.CreateTranslation(rect.Left - t * BIG, rect.Top);

                    brush.Transform = transform;
                }
            }
            else
            {
                if (brushType == 1) // horizontal: global gradient spanning entire chart width
                {
                    var transform = Matrix3x2.CreateScale(maxW, 1f) *
                                    Matrix3x2.CreateTranslation(0, 0);

                    brush.Transform = transform;
                }
                else if (brushType == 2) // vertical
                {
                    var transform = Matrix3x2.CreateScale(1f, rect.Height) *
                                    Matrix3x2.CreateTranslation(rect.Left, rect.Top);

                    brush.Transform = transform;
                }
                else if (brushType == 3) // height
                {
                    float t = Math.Clamp(rect.Height / maxH, 0f, 1f);
                    float BIG = 1e6f;

                    var transform = Matrix3x2.CreateScale(BIG, 1f) *
                                    Matrix3x2.CreateTranslation(rect.Left - t * BIG, rect.Top);

                    brush.Transform = transform;
                    //float t = Math.Clamp(rect.Height / maxH, 0f, 1f);
                    //float L = 1f;
                    //float BIG = 1e6f;

                    //var transform = Matrix3x2.CreateScale(BIG, 1f) *
                    //                Matrix3x2.CreateTranslation(rect.Left - t * BIG * L, rect.Top);

                    //brush.Transform = transform;
                }
            }
        }

        private void OnDraw(object? sender, DrawEventArgs e)
        {
            MainWindow.displayFpsMeter.Tick();
            if (NormalDragHandler.IsDragging)
            {
                return;
            }

            if (_d2dContext == null || _targetBitmap == null)
                return;

            // If the window changed size
            if (_targetBitmap.Size.Width != e.Surface.ActualWidth ||
                _targetBitmap.Size.Height != e.Surface.ActualHeight)
            {
                CreateTargetBitmap();
            }

            // Set the render target to the texture.
            _d2dContext.Target = _targetBitmap;

            // Begin drawing and clear the target 
            var clearColor = ToColor4(ClearColor);
            _d2dContext.AntialiasMode = AntialiasMode.Aliased;
            _d2dContext.BeginDraw();

            // Show current layer or if there is none, show the last layer
            Layer displayLayer;
            if (_layerQueue.Count() == 0)
            {
                displayLayer = _previousLayer;
            }
            else
            {
                _layerQueue.TryDequeue(out displayLayer);
                _previousLayer = displayLayer;
            }

            // If there is no layer there is no draw
            if (displayLayer == null)
            {
                _d2dContext.EndDraw();
                return;
            }

            _d2dContext.Clear(clearColor);

            // Rotate this bitch
            // More accurately:
            // - Find the content's center
            // - Move the content's center to 0,0
            // - Preform a D2D matrix rotation aka rotate the whole fucking GPU texture around it's 0,0 (you can't change the rotation point so I have to come to in instead of bringing it to me) 
            // - Now that it's rotated move it back to where it was
            // - Now the rectangle is likely clipping if it's rotated off-axis, so the window needs to be bigger. Window gets bigger in the right-down direction, leaving the content top-left and off-center
            // - Move the content into the center
            float w = (float)ActualWidth;
            float h = (float)ActualHeight;

            float rad = (float)(Visualizer.InstanceOptions._rotation * Math.PI / 180.0);

            float fw = Visualizer.InstanceOptions._bars * (Visualizer.InstanceOptions._barWidth + Visualizer.InstanceOptions._barGap);
            float fh = Visualizer.InstanceOptions._height;
            float wd = (w - fw) * 0.5f;
            float hd = (h - fh) * 0.5f;

            var contentCenter = new Vector2(fw * 0.5f, fh * 0.5f);

            var m = Matrix3x2.CreateTranslation(-contentCenter); // 0,0
            m *= Matrix3x2.CreateRotation(rad); // rotate
            m *= Matrix3x2.CreateTranslation(contentCenter); // old center
            m *= Matrix3x2.CreateTranslation(wd, hd); // new center

            _d2dContext.Transform = m;

            // Vars we'll need later
            float centerX = 0f;
            float centerY = 0f;
            bool rotateBars = false;
            bool stereo = Visualizer.InstanceOptions._channelMode == ChannelMode.Stereo;
            bool connectLines = !((Visualizer.InstanceOptions._visualizationMode == VisualizationMode.Center || Visualizer.InstanceOptions._visualizationMode == VisualizationMode.OuterCircle || Visualizer.InstanceOptions._visualizationMode == VisualizationMode.InnerCircle) && (Visualizer.InstanceOptions._visualizationMode != VisualizationMode.Bottom || Visualizer.InstanceOptions._visualizationMode != VisualizationMode.Top));
            bool lines = Visualizer.InstanceOptions._showLines;
            bool peaksLine = Visualizer.InstanceOptions._showPeaksLine;
            bool onlyPeaks = Visualizer.InstanceOptions._showOnlyPeaks;
            float thickness = Visualizer.InstanceOptions._lineThickness;
            var currentMode = Visualizer.InstanceOptions._visualizationMode;
            if (currentMode == VisualizationMode.OuterCircle ||
                currentMode == VisualizationMode.InnerCircle)
            {
                //var size = _targetBitmap.Size;
                //centerX = size.Width * 0.5f;
                //centerY = size.Height * 0.5f;
                centerX = contentCenter.X;
                centerY = contentCenter.Y;
                rotateBars = true;
            }
            int brushType = 0;
            if (Visualizer.InstanceOptions._barColorType == ColorMode.DualColorHorizontal || 
                Visualizer.InstanceOptions._barColorType == ColorMode.GradientHorizontal)
            {
                brushType = 1;
            }
            else if (Visualizer.InstanceOptions._barColorType == ColorMode.DualColorVertical ||
                Visualizer.InstanceOptions._barColorType == ColorMode.GradientVertical)
            {
                brushType = 2;
            }
            else if (Visualizer.InstanceOptions._barColorType == ColorMode.DualColorHeight ||
                Visualizer.InstanceOptions._barColorType == ColorMode.GradientHeight)
            {
                brushType = 3;
            }

            int peakBrushType = 0;
            if (Visualizer.InstanceOptions._peakColorType == ColorMode.Match)
            {
                peakBrushType = brushType;
            }
            else if (Visualizer.InstanceOptions._peakColorType == ColorMode.DualColorHorizontal || 
                Visualizer.InstanceOptions._peakColorType == ColorMode.GradientHorizontal)
            {
                peakBrushType = 1;
            }
            else if (Visualizer.InstanceOptions._peakColorType == ColorMode.DualColorVertical ||
                Visualizer.InstanceOptions._peakColorType == ColorMode.GradientVertical)
            {
                peakBrushType = 2;
            }
            else if (Visualizer.InstanceOptions._peakColorType == ColorMode.DualColorHeight ||
                Visualizer.InstanceOptions._peakColorType == ColorMode.GradientHeight)
            {
                peakBrushType = 3;
            }

            bool disposeBarLeft = false;
            bool disposePeakLeft = false;
            bool disposeLineLeft = false;
            bool disposeBarMono = false;
            bool disposePeakMono = false;
            bool disposeLineMono = false;

            // Render our bars if needed
            if (Visualizer.InstanceOptions._showBars && !onlyPeaks)
            {
                if (IsUnsafeBrush(displayLayer.barBrushM))
                {
                    _d2dContext.EndDraw();
                    return;
                }
                disposeBarMono = true;

                // Render each bar
                for (int i = 0; i < displayLayer.Bars.Length; i++)
                {
                    var rect = displayLayer.Bars[i];
                    var prop = displayLayer.BarProperties[i];

                    SetBrushTransform(false, brushType, displayLayer.barBrushM, rect, fh, fw);


                    // If we want to show circles:
                    if (rotateBars)
                    {
                        float barWidth = rect.Width;
                        float pivotX = rect.X + barWidth * 0.5f;
                        float pivotY;
                        if (currentMode == VisualizationMode.InnerCircle)
                        {
                            pivotY = rect.Y + rect.Height;
                        }
                        else
                        {
                            pivotY = rect.Y;
                        }
                        var pivot = new Vector2(pivotX, pivotY);
                        var diff = new Vector2(pivot.X - centerX, pivot.Y - centerY);
                        double angle = Math.Atan2(diff.Y, diff.X);
                        double rotation = angle - Math.PI * 0.5;


                        var originalTransform = _d2dContext.Transform;
                        var rotMatrix = Matrix3x2.CreateRotation((float)rotation, pivot);
                        _d2dContext.Transform = rotMatrix * originalTransform;
                        _d2dContext.FillRectangle(rect, displayLayer.barBrushM);
                        _d2dContext.Transform = originalTransform;

                        if (stereo)
                        {
                            if (IsUnsafeBrush(displayLayer.barBrushL))
                            {
                                _d2dContext.EndDraw();
                                return;
                            }
                            var rectL = displayLayer.BarsL[i];
                            SetBrushTransform(false, brushType, displayLayer.barBrushL, rectL, fh, fw);

                            var originalTransformL = _d2dContext.Transform;


                            float pivotYL = (currentMode == VisualizationMode.InnerCircle)
                                                ? rectL.Y + rectL.Height
                                                : rectL.Y;

                            var pivotMirror = new Vector2(centerX * 2f - pivot.X, pivotYL);

                            double rotationMirror = -angle + Math.PI * 0.5;

                            var rotMatrixL = Matrix3x2.CreateRotation((float)rotationMirror, pivotMirror);
                            _d2dContext.Transform = rotMatrixL * originalTransformL;
                            _d2dContext.FillRectangle(rectL, displayLayer.barBrushL);
                            _d2dContext.Transform = originalTransformL;
                            disposeBarLeft = true;
                        }

                    }
                    else // Otherwise we can just put the rect where it wants to be
                    {
                        _d2dContext.FillRectangle(rect, displayLayer.barBrushM);
                    }
                }
            }

            // Render the peak bars if needed
            if (Visualizer.InstanceOptions._showPeaks)
            {
                if (IsUnsafeBrush(displayLayer.peakBrushM))
                {
                    _d2dContext.EndDraw();
                    return;
                }
                disposePeakMono = true;
                for (int i = 0; i < displayLayer.Bars.Length; i++)
                {
                    var rect = displayLayer.PeaksR[i];
                    var prop = displayLayer.BarProperties[i];
                    var valM = MainWindow._peaks.Length > i ? MainWindow._peaks[i] : 0;
                    SetBrushTransform(true, peakBrushType, displayLayer.peakBrushM, rect, fh, fw, (float)valM);

                    // Same path as above, here we handle circle peaks
                    if (rotateBars)
                    {
                        float barWidth = rect.Width;
                        float pivotX = rect.X + barWidth * 0.5f;
                        float pivotY;
                        if (currentMode == VisualizationMode.InnerCircle)
                        {
                            pivotY = rect.Y + rect.Height;
                        }
                        else
                        {
                            pivotY = rect.Y;
                        }
                        var pivot = new Vector2(pivotX, pivotY);
                        var diff = new Vector2(pivot.X - centerX, pivot.Y - centerY);
                        double angle = Math.Atan2(diff.Y, diff.X);
                        double rotation = angle - Math.PI * 0.5; // rotate downward vector to radial direction


                        var originalTransform = _d2dContext.Transform;
                        var rotMatrix = Matrix3x2.CreateRotation((float)rotation, pivot);
                        _d2dContext.Transform = rotMatrix * originalTransform;
                        _d2dContext.FillRectangle(rect, displayLayer.peakBrushM);
                        _d2dContext.Transform = originalTransform;

                        if (stereo)
                        {
                            if (IsUnsafeBrush(displayLayer.peakBrushL))
                            {
                                _d2dContext.EndDraw();
                                return;
                            }

                            var rectL = displayLayer.PeaksL[i];
                            var valL = MainWindow._peaks.Length > i ? MainWindow._peaksRight[i] : 0;
                            SetBrushTransform(true, peakBrushType, displayLayer.peakBrushL, rectL, fh, fw, (float)valL);

                            var originalTransformL = _d2dContext.Transform;

                            float pivotXL = rectL.X + rectL.Width * 0.5f;
                            float pivotYL = (currentMode == VisualizationMode.InnerCircle)
                                                ? rectL.Y + rectL.Height
                                                : rectL.Y;

                            var pivotL = new Vector2(pivotXL, pivotYL);
                            var diffL = new Vector2(pivotL.X - centerX, pivotL.Y - centerY);

                            double angleL = Math.Atan2(diffL.Y, diffL.X);
                            double rotationL = angleL - Math.PI * 0.5;

                            var rotMatrixL = Matrix3x2.CreateRotation((float)rotationL, pivotL);
                            _d2dContext.Transform = rotMatrixL * originalTransformL;
                            _d2dContext.FillRectangle(rectL, displayLayer.peakBrushL);
                            _d2dContext.Transform = originalTransformL;
                            disposePeakLeft = true;
                        }
                    }
                    else // Otherwise we just place them down
                    {
                        _d2dContext.FillRectangle(rect, displayLayer.peakBrushM);
                        if (displayLayer.PeaksL[i] != null)
                        {
                            var rectL = displayLayer.PeaksL[i];

                            if (IsUnsafeBrush(displayLayer.peakBrushL))
                            {
                                _d2dContext.EndDraw();
                                return;
                            }
                            disposePeakLeft = true;
                            SetBrushTransform(true, peakBrushType, displayLayer.peakBrushL, rectL, fh, fw, (float)valM);

                            _d2dContext.FillRectangle(rectL, displayLayer.peakBrushL);
                        }
                    }
                }
            }

            // Draw the vis lines if needed
            if (lines && !onlyPeaks)
            {
                if (IsUnsafeBrush(displayLayer.lineBrushM))
                {
                    _d2dContext.EndDraw();
                    return;
                }
                disposeLineMono = true;

                var points = displayLayer.rightPoints;
                // We have points to draw
                if (points.Count > 0)
                {
                    var pointsL = displayLayer.leftPoints;
                    if (pointsL.Count() > 0) // If there are more points to draw, add them
                    {
                        pointsL.Reverse();
                        if (Visualizer.InstanceOptions._visualizationMode == VisualizationMode.Center)
                        {
                            var l = points.Last();
                            var l2 = pointsL.First();
                            points.Remove(l);
                            pointsL.Remove(l2);
                            l.X = (Visualizer.InstanceOptions._barWidth + Visualizer.InstanceOptions._barGap) * (Visualizer.InstanceOptions._bars) - Visualizer.InstanceOptions._barWidth;
                            l2.X = (Visualizer.InstanceOptions._barWidth + Visualizer.InstanceOptions._barGap) * (Visualizer.InstanceOptions._bars) - Visualizer.InstanceOptions._barWidth;
                            points.Add(l);
                            points.Add(l2);
                        }
                        points.AddRange(pointsL);
                        var geomL = BuildCatmullRomGeometry(points, connectLines);
                        _d2dContext.DrawGeometry(geomL, displayLayer.lineBrushM, thickness);
                        geomL.Dispose();
                    }
                    else // Otherwise just draw
                    {
                        var l = points.Last();
                        points.Remove(l);
                        l.X += (Visualizer.InstanceOptions._barWidth + Visualizer.InstanceOptions._barGap);
                        points.Add(l);
                        var geom = BuildCatmullRomGeometry(points, connectLines);
                        if (geom == null)
                        {
                            _d2dContext.EndDraw();
                            return;
                        }

                        _d2dContext.DrawGeometry(geom, displayLayer.lineBrushM, thickness);
                        geom.Dispose();
                    }
                }
            }

            // Draw the peak lines if needed
            if (peaksLine)
            {
                if (IsUnsafeBrush(displayLayer.lineBrushL))
                {
                    _d2dContext.EndDraw();
                    return;
                }

                disposeLineLeft = true;

                var points = displayLayer.PeaksR.Select(x => new Vector2(x.Left, x.Top)).ToList();
                var pointsL = displayLayer.PeaksL.Select(x => new Vector2(x.Left, x.Top)).ToList();
                if (points.Count > 0)
                {
                    if (displayLayer.PeaksL[0].Height != 0)
                    {
                        pointsL.Reverse();
                        if (Visualizer.InstanceOptions._visualizationMode == VisualizationMode.Center)
                        {
                            var l = points.Last();
                            var l2 = pointsL.First();
                            points.Remove(l);
                            pointsL.Remove(l2);
                            l.X = (Visualizer.InstanceOptions._barWidth + Visualizer.InstanceOptions._barGap) * (Visualizer.InstanceOptions._bars) - Visualizer.InstanceOptions._barWidth;
                            l2.X = (Visualizer.InstanceOptions._barWidth + Visualizer.InstanceOptions._barGap) * (Visualizer.InstanceOptions._bars) - Visualizer.InstanceOptions._barWidth;
                            points.Add(l);
                            points.Add(l2);
                        }
                        points.AddRange(pointsL);
                        var geomL = BuildCatmullRomGeometry(points, connectLines);
                        _d2dContext.DrawGeometry(geomL, displayLayer.lineBrushL, thickness);
                        geomL.Dispose();
                    }
                    else
                    {
                        var l = points.Last();
                        points.Remove(l);
                        l.X += (Visualizer.InstanceOptions._barWidth + Visualizer.InstanceOptions._barGap);
                        points.Add(l);
                        var geom = BuildCatmullRomGeometry(points, connectLines);
                        if (geom == null)
                        {
                            _d2dContext.EndDraw();
                            return;
                        }

                        _d2dContext.DrawGeometry(geom, displayLayer.lineBrushL, thickness);
                        geom.Dispose();
                    }
                }
            }

            // Finish drawing
            _d2dContext.EndDraw();
            if (disposeBarMono)
            {
                displayLayer.barBrushM.Dispose();
            }
            if (disposeBarLeft)
            {
                displayLayer.barBrushL.Dispose();
            }
            if (disposePeakMono)
            {
                displayLayer.peakBrushM.Dispose();
            }
            if (disposePeakLeft)
            {
                displayLayer.peakBrushL.Dispose();
            }
            if (disposeLineMono)
            {
                displayLayer.lineBrushM.Dispose();
            }
            if (disposeLineLeft)
            {
                displayLayer.lineBrushL.Dispose();
            }
        }

        private ID2D1PathGeometry BuildCatmullRomGeometry(List<Vector2> pts, bool open)
        {
            var geom = _d2dDevice.Factory.CreatePathGeometry();

            float barW = Visualizer.InstanceOptions._barWidth;
            const float barH = 4f;

            var centered = new List<Vector2>(pts.Count);
            centered.Add(new Vector2(pts[0].X, pts[0].Y));
            for (int i = 1; i < pts.Count - 1; i++)
            {
                centered.Add(new Vector2(pts[i].X + barW * 0.5f, pts[i].Y + barH * 0.5f));
            }
            centered.Add(new Vector2(pts[pts.Count - 1].X, pts[pts.Count - 1].Y));

            pts = centered;

            using (var sink = geom.Open())
            {
                sink.SetFillMode(Vortice.Direct2D1.FillMode.Winding);
                sink.BeginFigure(new Vector2(pts[0].X, pts[0].Y), FigureBegin.Hollow);

                for (int i = 0; i < pts.Count - 1; i++)
                {
                    Vector2 p0 = i > 0 ? pts[i - 1] : pts[i];
                    Vector2 p1 = pts[i];
                    Vector2 p2 = pts[i + 1];
                    Vector2 p3 = i < pts.Count - 2 ? pts[i + 2] : p2;

                    // Catmull-Rom to Bezier conversion
                    Vector2 c1 = new Vector2(
                        p1.X + (p2.X - p0.X) / 6,
                        p1.Y + (p2.Y - p0.Y) / 6);

                    Vector2 c2 = new Vector2(
                        p2.X - (p3.X - p1.X) / 6,
                        p2.Y - (p3.Y - p1.Y) / 6);

                    sink.AddBezier(new Vortice.Direct2D1.BezierSegment(c1, c2, p2));
                }
                FigureEnd end = open ? FigureEnd.Open : FigureEnd.Closed;
                sink.EndFigure(end);
                sink.Close();
            }
            var bounds = geom.GetBounds();
            return geom;
        }


        public void UpdatePlane(double[]? frameL, double[]? frameR)
        {
            if (Visualizer.UpdateSettings)
            {
                return;
            }

            if (frameL == null && frameR == null)
            {
                return;
            }

            if (_d2dContext == null)
            {
                return;
            }

            try
            {
                var layer = UpdateBars(frameL, frameR);
                if (layer == null)
                {
                    return;
                }

                if (Visualizer.InstanceOptions._showPeaks || Visualizer.InstanceOptions._showPeaksLine)
                {
                    layer = UpdatePeakRectangles(layer);

                    if (layer == null)
                    {
                        return;
                    }
                }

                _layerQueue.Enqueue(layer);

                // Invalidate the surface to trigger a redraw.
                Invalidate();
            }
            catch (Exception)
            {
                // Don't
            }

        }

        private Layer UpdateBars(double[] frame, double[] frameRight = null)
        {
            var opts = Visualizer.InstanceOptions;
            double height = opts._height;
            double min = opts._minHeight;
            double minHalf = min * 0.5;

            var pos = opts._visualizationMode;
            var channels = opts._channelMode;
            bool stereo = channels == ChannelMode.Stereo;

            float contentWidth = (opts._barWidth + opts._barGap) * opts._bars;
            float contentHeight = opts._height;
            float actualWidth = contentWidth;
            float actualHeight = contentHeight;


            double cx = actualWidth * 0.5;
            double cy = actualHeight * 0.5;
            double halfBar = opts._barWidth * 0.5;
            float barWidth = opts._barWidth;
            double barGap = opts._barGap;
            int barCount = opts._bars;

            double attack = opts._attackSpeed;
            double decay = opts._decaySpeed;
            double canvasHalfHeight = (pos == VisualizationMode.Center) ? actualHeight * 0.5 : 0.0;

            // Circle constants
            int doubledBars = barCount * 2;
            double combinedWidthGap = barWidth + barGap;
            double radiusStereo = stereo ? (combinedWidthGap * doubledBars) / (2 * Math.PI) : 0.0;
            double radiusMono = !stereo ? (combinedWidthGap * barCount) / (2 * Math.PI) : 0.0;
            double angleStepStereo = stereo ? (2 * Math.PI) / doubledBars : 0.0;
            double angleStepMono = !stereo ? (2 * Math.PI) / barCount : 0.0;
            double rotationOffset = opts._rotation;

            // Scale incoming data & find local max 
            int barLen = frame.Length;
            double localMax = 0.0;

            for (int i = 0; i < barLen; i++)
            {
                double valL = frame[i] *= height;
                if (valL > localMax) localMax = valL;

                if (stereo)
                {
                    if (frameRight.Length < i)
                    {
                        break;
                    }
                    double valR = frameRight[i] *= height;
                    if (valR > localMax) localMax = valR;
                }
            }

            if (barLen != MainWindow._peaks.Length)
            {
                return null;
            }

            var Layer = new Layer(barLen, localMax);

            if (Visualizer.InstanceOptions._showLines)
            {
                var lineBrush = CreateBrushForLinesLayer(false, actualWidth, (float)localMax, 0, actualHeight);
                Layer.lineBrushM = lineBrush;
            }
            else
            {

            }

            if (Visualizer.InstanceOptions._showPeaksLine)
            {
                var lineBrush = CreateBrushForLinesLayer(true, actualWidth, (float)localMax, 0, actualHeight);
                Layer.lineBrushL = lineBrush;
            }

            if (Visualizer.InstanceOptions._showBars && stereo && 
                (Visualizer.InstanceOptions._visualizationMode == VisualizationMode.OuterCircle || Visualizer.InstanceOptions._visualizationMode == VisualizationMode.InnerCircle))
            {
                Layer.barBrushM = CreateBrushForLayer(false, 0, barLen, actualWidth, (float)height, 0, localMax);
                Layer.barBrushL = CreateBrushForLayer(false, 0, barLen, actualWidth, (float)height, 0, localMax);
            }
            else if (Visualizer.InstanceOptions._showBars)
            {
                Layer.barBrushM = CreateBrushForLayer(false, 0, barLen, actualWidth, (float)height, 0, localMax);
            }

           

            // Update each bar 
            List<Point> linePoints = new List<Point>();
            List<Point> linePoints2 = new List<Point>();
            for (int i = 0; i < barLen; i++)
            {
                var newRect = new Vortice.Mathematics.Rect(0, 0, barWidth, 0);
                var newRectLeft = new Vortice.Mathematics.Rect(-1, -1, barWidth, 0);
                (double BarL, double BarR, double BarM) newBarProperties = (0.0, 0.0, 0.0);
                (double BarL, double BarR, double BarM) lastRect = _previousLayer != null && _previousLayer.BarProperties.Length > i ? _previousLayer.BarProperties[i] : (0.0, 0.0, 0.0);

                double current = 0.0;
                double currentLeft = 0.0;
                double currentRight = 0.0;

                if (stereo)
                {
                    double targetLeft = frameRight[i] + minHalf;
                    double targetRight = frame[i] + minHalf;

                    currentLeft = double.IsNaN(lastRect.BarL) ? 0.0 : lastRect.BarL;
                    currentRight = double.IsNaN(lastRect.BarR) ? 0.0 : lastRect.BarR;

                    double speedL = targetLeft > currentLeft ? attack : decay;
                    double speedR = targetRight > currentRight ? attack : decay;

                    currentLeft = Math.Clamp(currentLeft + (targetLeft - currentLeft) * speedL, 0.0, height);
                    currentRight = Math.Clamp(currentRight + (targetRight - currentRight) * speedR, 0.0, height);

                    current = (currentLeft + currentRight) * 0.5;
                    if (current < 1.0) current = 0.0;
                    if (currentLeft < 1.0) currentLeft = 0.0;
                    if (currentRight < 1.0) currentRight = 0.0;

                    newBarProperties.BarL = currentLeft;
                    newBarProperties.BarM = current;
                    newBarProperties.BarR = currentRight;
                }
                else
                {
                    double target = frame[i] + min;
                    current = double.IsNaN(lastRect.BarM) ? 0.0 : lastRect.BarM;

                    double speed = target > current ? attack : decay;
                    current = Math.Clamp(current + (target - current) * speed, 0.0, height);
                    if (current < 1.0) current = 0.0;

                    newRect.Height = (float)current;
                    newBarProperties.BarM = current;
                }

                // Positioning 
                switch (pos)
                {
                    case VisualizationMode.Bottom:
                        if (Visualizer.InstanceOptions._showLines)
                        {
                            Layer.rightPoints.Add(new Vector2((float)(barWidth + barGap) * i, (float)(height - current)));
                            if (MainWindow._peaks[i] < current) MainWindow._peaks[i] = current;
                            Layer.BarProperties[i] = newBarProperties;
                            if (!Visualizer.InstanceOptions._showBars)
                            {
                                continue;
                            }
                        }

                        newRect.Left = (i * (opts._barWidth + opts._barGap));
                        newRect.Top = (float)height - newRect.Height;
                        if (MainWindow._peaks[i] < current) MainWindow._peaks[i] = current;
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
                                Layer.leftPoints.Add(new Vector2((float)(barWidth + barGap) * i, (float)(canvasHalfHeight - (current * percentAbove))));
                                Layer.rightPoints.Add(new Vector2((float)(barWidth + barGap) * i, (float)(canvasHalfHeight + (current * percentBelow))));
                                if (MainWindow._peaks[i] < current) MainWindow._peaks[i] = current;
                                if (MainWindow._peaksRight[i] < currentRight) MainWindow._peaksRight[i] = currentRight;
                                Layer.BarProperties[i] = newBarProperties;
                                if (!Visualizer.InstanceOptions._showBars)
                                {
                                    continue;
                                }
                            }

                            newRect.Left = (i * (opts._barWidth + opts._barGap));
                            newRect.Top = (float)offsetDown;
                            newRect.Height = (float)current;

                            if (MainWindow._peaks[i] < currentRight) MainWindow._peaks[i] = currentRight;
                            if (MainWindow._peaksRight[i] < currentLeft) MainWindow._peaksRight[i] = currentLeft;
                        }
                        else
                        {
                            if (Visualizer.InstanceOptions._showLines)
                            {
                                Layer.leftPoints.Add(new Vector2((float)(barWidth + barGap) * i, (float)(canvasHalfHeight + (current * 0.5))));
                                Layer.rightPoints.Add(new Vector2((float)(barWidth + barGap) * i, (float)(canvasHalfHeight - (current * 0.5))));
                                if (MainWindow._peaks[i] < current) MainWindow._peaks[i] = current;
                                Layer.BarProperties[i] = newBarProperties;
                                if (!Visualizer.InstanceOptions._showBars)
                                {
                                    continue;
                                }
                            }

                            newRect.Left = (i * (opts._barWidth + opts._barGap));
                            newRect.Top = (float)(canvasHalfHeight - (current * 0.5f));

                            if (MainWindow._peaks[i] < current) MainWindow._peaks[i] = current;
                        }
                        break;

                    case VisualizationMode.Top:
                        if (Visualizer.InstanceOptions._showLines)
                        {
                            Layer.rightPoints.Add(new Vector2((float)(barWidth + barGap) * i, (float)current));
                            if (MainWindow._peaks[i] < current) MainWindow._peaks[i] = current;
                            Layer.BarProperties[i] = newBarProperties;
                            if (!Visualizer.InstanceOptions._showBars)
                            {
                                continue;
                            }
                        }

                        newRect.Left = (i * (opts._barWidth + opts._barGap));
                        newRect.Top = 0;

                        if (MainWindow._peaks[i] < current) MainWindow._peaks[i] = current;
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


                            newRectLeft.X = 0;
                            newRectLeft.Y = 0;

                            newRectLeft.Height = (float)currentLeft;
                            newRect.Height = (float)currentRight;

                            if (Visualizer.InstanceOptions._showLines)
                            {
                                Layer.leftPoints.Add(new Vector2((float)(x + sgn * cos * currentLeft), (float)(y + sgn * sin * currentLeft)));
                                Layer.rightPoints.Add(new Vector2((float)(xMirror + sgn * cosR * currentRight), (float)(y + sgn * sinR * currentRight)));
                                if (MainWindow._peaks[i] < current) MainWindow._peaks[i] = currentLeft;
                                if (MainWindow._peaksRight[i] < currentRight) MainWindow._peaksRight[i] = currentRight;
                                Layer.BarProperties[i] = newBarProperties;
                                if (!Visualizer.InstanceOptions._showBars)
                                {
                                    continue;
                                }
                            }

                            newRect.Left = (float)(x - halfBar);
                            newRectLeft.Left = (float)(xMirror - halfBar);

                            if (pos == VisualizationMode.OuterCircle)
                            {
                                newRect.Top = (float)y;
                                newRectLeft.Top = (float)y;

                                if (MainWindow._peaks[i] < currentLeft) MainWindow._peaks[i] = currentLeft;
                                if (MainWindow._peaksRight[i] < currentRight) MainWindow._peaksRight[i] = currentRight;
                            }
                            else  // InnerCircle
                            {
                                newRect.Top = (float)(y - currentRight);
                                newRectLeft.Top = (float)(y - currentLeft);

                                if (MainWindow._peaks[i] < currentLeft) MainWindow._peaks[i] = currentLeft;
                                if (MainWindow._peaksRight[i] < currentRight) MainWindow._peaksRight[i] = currentRight;
                            }


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
                                Layer.rightPoints.Add(new Vector2((float)(x + sgn * cos * current), (float)(y + sgn * sin * current)));
                                if (MainWindow._peaks[i] < current) MainWindow._peaks[i] = current;
                                if (MainWindow._peaksRight[i] < currentRight) MainWindow._peaksRight[i] = currentRight;
                                Layer.BarProperties[i] = newBarProperties;
                                if (!Visualizer.InstanceOptions._showBars)
                                {
                                    continue;
                                }
                            }

                            newRect.Left = (float)(x - halfBar);

                            if (pos == VisualizationMode.OuterCircle)
                            {
                                newRect.Top = (float)y;
                            }
                            else
                            {
                                newRect.Top = (float)(y - current);
                            }

                            if (MainWindow._peaks[i] < current) MainWindow._peaks[i] = current;
                            if (MainWindow._peaksRight[i] < currentRight) MainWindow._peaksRight[i] = currentRight;

                        }
                        break;
                }
                Layer.Bars[i] = newRect;
                Layer.BarsL[i] = newRectLeft;
                Layer.BarProperties[i] = newBarProperties;
            }


            return Layer;
        }
        private Layer UpdatePeakRectangles(Layer layer)
        {
            var opts = Visualizer.InstanceOptions;
            if (!opts._showPeaks && !opts._showPeaksLine)
            {
                return layer;
            }

            double min = opts._minHeight;
            var pos = opts._visualizationMode;
            var channels = opts._channelMode;
            bool stereo = channels == ChannelMode.Stereo;

            float barWidth = opts._barWidth;
            double barGap = opts._barGap;
            double halfBar = barWidth * 0.5;
            float stepX = (float)(barWidth + barGap);
            int barCount = opts._bars;
            float leftX = 0.0f;

            float contentWidth = (opts._barWidth + opts._barGap) * opts._bars;
            float contentHeight = opts._height;
            float actualWidth = contentWidth;
            float actualHeight = contentHeight;

            var barArr = MainWindow._peaks;
            var barArrL = MainWindow._peaksRight;

            if (barArr.Length != layer.Bars.Length)
            {
                return null;
            }

            int barLen = layer.BarsL.Length;

            if (Visualizer.InstanceOptions._showPeaks && stereo &&
               (Visualizer.InstanceOptions._visualizationMode == VisualizationMode.OuterCircle || Visualizer.InstanceOptions._visualizationMode == VisualizationMode.InnerCircle))
            {
                layer.peakBrushM = CreateBrushForPeaksLayer(0, barLen, actualWidth, contentHeight, 0, layer.max);
                layer.peakBrushL = CreateBrushForPeaksLayer(0, barLen, actualWidth, contentHeight, 0, layer.max);
            }
            else if (Visualizer.InstanceOptions._showPeaks && Visualizer.InstanceOptions._visualizationMode == VisualizationMode.Center)
            {
                layer.peakBrushM = CreateBrushForPeaksLayer(0, barLen, actualWidth, contentHeight, 0, layer.max);
                layer.peakBrushL = CreateBrushForPeaksLayer(0, barLen, actualWidth, contentHeight, 0, layer.max);
            }
            else if (Visualizer.InstanceOptions._showPeaks)
            {
                layer.peakBrushM = CreateBrushForPeaksLayer(0, barLen, actualWidth, contentHeight, 0, layer.max);
            }

            switch (pos)
            {
                // Bottom / Top
                case VisualizationMode.Bottom:
                case VisualizationMode.Top:
                    if (barArr == null) return layer;
                    bool useTop = pos == VisualizationMode.Top;

                    for (int i = 0; i < barArr.Length; i++, leftX += stepX)
                    {
                        var newRect = new Vortice.Mathematics.Rect(0, 0, barWidth, 4);

                        newRect.Left = leftX;
                        if (useTop)
                            newRect.Top = (float)barArr[i];
                        else
                            newRect.Top = actualHeight - (float)barArr[i];

                        layer.PeaksR[i] = newRect;

                    }
                    break;

                // Circle modes
                case VisualizationMode.OuterCircle:
                case VisualizationMode.InnerCircle:
                    double cx = actualWidth * 0.5;
                    double cy = actualHeight * 0.5;
                    bool outer = pos == VisualizationMode.OuterCircle;

                    if (stereo) // stereo: two peak arrays
                    {
                        if (barArr == null || barArrL == null) return layer;

                        int doubled = barCount * 2;
                        double radius = stepX * doubled / (2 * Math.PI);
                        double stepAng = (2 * Math.PI) / doubled;

                        for (int i = 0; i < barArr.Length; i++)
                        {
                            var newRectL = new Vortice.Mathematics.Rect(0, 0, barWidth, 4); // LEFT
                            var newRectR = new Vortice.Mathematics.Rect(0, 0, barWidth, 4); // RIGHT

                            double sgn = outer ? 1.0 : -1.0;

                            // L+R angles
                            double aL = (i + 0.5) * stepAng - Math.PI * 0.5;
                            double aR = -aL + Math.PI;

                            // baseline positions
                            double xBL = cx + radius * Math.Cos(aL);
                            double yBL = cy + radius * Math.Sin(aL);
                            double xBR = cx + radius * Math.Cos(aR);
                            double yBR = cy + radius * Math.Sin(aR);

                            // tip positions along radius by per-channel peak
                            double xTipL = xBL + sgn * Math.Cos(aL) * barArrL[i];
                            double yTipL = yBL + sgn * Math.Sin(aL) * barArrL[i];
                            double xTipR = xBR + sgn * Math.Cos(aR) * barArr[i];
                            double yTipR = yBR + sgn * Math.Sin(aR) * barArr[i];

                            // place rects with pivot at the tip
                            newRectL.Left = (float)(xTipL - halfBar);
                            newRectR.Left = (float)(xTipR - halfBar);
                            if (outer)
                            {
                                newRectL.Top = (float)yTipL;
                                newRectR.Top = (float)yTipR;
                            }
                            else
                            {
                                newRectL.Top = (float)(yTipL - 4.0);
                                newRectR.Top = (float)(yTipR - 4.0);
                            }

                            layer.PeaksL[i] = newRectL;
                            layer.PeaksR[i] = newRectR;

                        }
                    }
                    else // mono circle
                    {
                        if (barArr == null) return layer;

                        double radius = stepX * barCount / (2 * Math.PI);
                        double stepAng = (2 * Math.PI) / barCount;
                        double angOff = -Math.PI * 0.5;

                        for (int i = 0; i < barArr.Length; i++)
                        {
                            var newRect = new Vortice.Mathematics.Rect(0, 0, barWidth, 4);

                            double ang = i * stepAng - Math.PI * 0.5;
                            double x0 = cx + radius * Math.Cos(ang);
                            double y0 = cy + radius * Math.Sin(ang);
                            double sgn = outer ? 1.0 : -1.0;

                            double xTip = x0 + sgn * Math.Cos(ang) * barArr[i];
                            double yTip = y0 + sgn * Math.Sin(ang) * barArr[i];

                            newRect.Left = (float)(xTip - halfBar);
                            newRect.Top = outer ? (float)yTip : (float)(yTip - 4.0);

                            layer.PeaksR[i] = newRect;
                        }
                    }
                    break;
                case VisualizationMode.Center:
                    if (barArr == null || barArrL == null) return layer;

                    double halfCanvas = actualHeight * 0.5;

                    for (int i = 0; i < barArr.Length; i++, leftX += stepX)
                    {
                        var newRect = new Vortice.Mathematics.Rect(0, 0, barWidth, 4);
                        var newRectL = new Vortice.Mathematics.Rect(0, 0, barWidth, 4);

                        newRect.Left = (float)(leftX);
                        newRectL.Left = (float)(leftX);

                        if (stereo)
                        {
                            newRectL.Top = (float)(halfCanvas - (barArrL[i] * 0.5));
                            newRect.Top = (float)(halfCanvas + (barArr[i] * 0.5) - 2.0);

                        }
                        else
                        {
                            double peak = barArr[i] * 0.5;
                            newRectL.Top = (float)(halfCanvas - peak);
                            newRect.Top = (float)(halfCanvas + peak - 2.0);

                        }


                        layer.PeaksL[i] = newRectL;
                        layer.PeaksR[i] = newRect;
                    }
                    break;
            }

            return layer;
        }

        private void CreateTargetBitmap()
        {
            _targetBitmap?.Dispose();
            _targetBitmap = null;

            if (_d2dContext == null || ColorTexture == null)
                return;

            using IDXGISurface dxgiSurface = ColorTexture.QueryInterface<IDXGISurface>();

            var bitmapProperties = new BitmapProperties1(
                new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                96, 96,
                BitmapOptions.Target);

            _targetBitmap = _d2dContext!.CreateBitmapFromDxgiSurface(dxgiSurface, bitmapProperties);
        }

        private void DisposeD2DResources()
        {
            _targetBitmap?.Dispose();
            _targetBitmap = null;
            if (_layerQueue.Count > 0)
            {
                _layerQueue.Clear();
            }
            _d2dContext?.Dispose();
            _d2dContext = null;
            _d2dDevice?.Dispose();
            _d2dDevice = null;
        }

        private static Vortice.Mathematics.Color4 ToColor4(System.Windows.Media.Color color)
        {
            var d2d = new Color4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
            return d2d;
        }

        private ID2D1Brush CreateBrushForLinesLayer(bool peaks, float width, float height, float top, double max)
        {
            float ratio = 0;
            if (max > 0)
            {
                ratio = (float)(max / 0.7);
                ratio = Math.Clamp(ratio, 0f, 1f);
            }

            var clrMode = peaks ? Visualizer.InstanceOptions._peakColorType : Visualizer.InstanceOptions._barColorType;
            ColorMode type = ColorMode.SolidColor;
            if (peaks)
            {
                type = clrMode == ColorMode.Match ? Visualizer.InstanceOptions._barColorType : Visualizer.InstanceOptions._peakColorType;
            }
            else
            {
                type = clrMode;
            }
            var clr1 = peaks && clrMode != ColorMode.Match ? MainWindow._color3 : MainWindow._color1;
            var clr2 = peaks && clrMode != ColorMode.Match ? MainWindow._color4 : MainWindow._color2;
            var grdClrs = peaks && clrMode != ColorMode.Match ? MainWindow.colorPeakArrayGradient : MainWindow.colorArrayGradient;

            switch (type)
            {
                case ColorMode.SolidColor:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(clr1));
                case ColorMode.DualColorVertical:
                    var stopsV = new Vortice.Direct2D1.GradientStop[]
                    {
                        new Vortice.Direct2D1.GradientStop(0, ToColor4(clr1)),
                        new Vortice.Direct2D1.GradientStop(1, ToColor4(clr2))
                    };
                    using (var stopCollection = _d2dContext.CreateGradientStopCollection(stopsV))
                    {
                        var props = new LinearGradientBrushProperties
                        {
                            StartPoint = new System.Numerics.Vector2(0, top),
                            EndPoint = new System.Numerics.Vector2(0, (float)height + top)
                        };
                        return _d2dContext.CreateLinearGradientBrush(props, stopCollection);
                    }
                case ColorMode.DualColorHorizontal:
                    var stopsH = new Vortice.Direct2D1.GradientStop[]
                    {
                        new Vortice.Direct2D1.GradientStop(0, ToColor4(clr1)),
                        new Vortice.Direct2D1.GradientStop(1, ToColor4(clr2))
                    };
                    using (var stopCollection = _d2dContext.CreateGradientStopCollection(stopsH))
                    {
                        var props = new LinearGradientBrushProperties
                        {
                            StartPoint = new System.Numerics.Vector2(0, 0),
                            EndPoint = new System.Numerics.Vector2((float)width, (float)height)
                        };
                        return _d2dContext.CreateLinearGradientBrush(props, stopCollection);
                    }
                case ColorMode.DualColorHeight:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(Visualizer.GetGradientColor(
                            new[] { clr1, clr2 },
                            (double)height / max)));
                    break;
                case ColorMode.GradientVertical:
                    var stopsGV = new List<Vortice.Direct2D1.GradientStop>();
                    for (int i = 0; i < grdClrs.Length; i++)
                    {
                        float pos = (float)i / (grdClrs.Length - 1);
                        stopsGV.Add(new Vortice.Direct2D1.GradientStop(pos, ToColor4(grdClrs[i])));
                    }
                    using (var stopCollection = _d2dContext.CreateGradientStopCollection(stopsGV.ToArray()))
                    {
                        var props = new LinearGradientBrushProperties
                        {
                            StartPoint = new System.Numerics.Vector2(0, top),
                            EndPoint = new System.Numerics.Vector2(0, (float)height)
                        };
                        return _d2dContext.CreateLinearGradientBrush(props, stopCollection);
                    }
                case ColorMode.GradientHorizontal:
                    var stopsGH = new List<Vortice.Direct2D1.GradientStop>();
                    for (int i = 0; i < grdClrs.Length; i++)
                    {
                        float pos = (float)i / (grdClrs.Length - 1);
                        stopsGH.Add(new Vortice.Direct2D1.GradientStop(pos, ToColor4(grdClrs[i])));
                    }
                    using (var stopCollection = _d2dContext.CreateGradientStopCollection(stopsGH.ToArray()))
                    {
                        var props = new LinearGradientBrushProperties
                        {
                            StartPoint = new System.Numerics.Vector2(0, 0),
                            EndPoint = new System.Numerics.Vector2(width, (float)height)
                        };
                        return _d2dContext.CreateLinearGradientBrush(props, stopCollection);
                    }
                case ColorMode.GradientHeight:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(Visualizer.GetGradientColor(
                           grdClrs,
                           (double)height / max)));
                    break;
                case ColorMode.GradientPitch:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(
                        PitchDetector.GetPitchColor(MainWindow.PitchFreq, grdClrs)));
                case ColorMode.DualColorPitch:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(
                        PitchDetector.GetPitchColor(MainWindow.PitchFreq, new[] { clr1, clr2 })));
                case ColorMode.GradientFrequency:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(
                        Visualizer.GetGradientColor(
                            grdClrs,
                            (MainWindow.PitchFreq / 2200) - 0.03)));
                case ColorMode.DualColorFrequency:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(
                        Visualizer.GetGradientColor(
                            new[] { clr1, clr2 },
                            (MainWindow.PitchFreq / 2200) - 0.03)));

                default:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(MainWindow._color1));
            }
        }
        private ID2D1Brush CreateBrushForPeaksLayer(int index, int total, float width, float height, float top, double max)
        {
            float ratio = 0;
            if (max > 0)
            {
                ratio = (float)(max / 0.7);
                ratio = Math.Clamp(ratio, 0f, 1f);
            }

            var type = Visualizer.InstanceOptions._peakColorType == ColorMode.Match ? Visualizer.InstanceOptions._barColorType : Visualizer.InstanceOptions._peakColorType;
            var clr1 = Visualizer.InstanceOptions._peakColorType != ColorMode.Match ? MainWindow._color3 : MainWindow._color1;
            var clr2 = Visualizer.InstanceOptions._peakColorType != ColorMode.Match ? MainWindow._color4 : MainWindow._color2;
            var grdClrs = Visualizer.InstanceOptions._peakColorType != ColorMode.Match ? MainWindow.colorPeakArrayGradient : MainWindow.colorArrayGradient;

            switch (type)
            {
                case ColorMode.SolidColor:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(clr1));
                case ColorMode.DualColorVertical:
                    {
                        var stopsH = new Vortice.Direct2D1.GradientStop[]
                        {
                            new Vortice.Direct2D1.GradientStop(0, ToColor4(clr1)),
                            new Vortice.Direct2D1.GradientStop(1, ToColor4(clr2))
                        };
                        using (var stopCollection = _d2dContext.CreateGradientStopCollection(stopsH))
                        {
                            var props = new LinearGradientBrushProperties
                            {
                                StartPoint = new System.Numerics.Vector2(0, 0),
                                EndPoint = new System.Numerics.Vector2(0, 1)
                            };
                            return _d2dContext.CreateLinearGradientBrush(props, stopCollection);
                        }
                    }
                case ColorMode.DualColorHorizontal:
                    {
                        var stopsH = new Vortice.Direct2D1.GradientStop[]
                        {
                            new Vortice.Direct2D1.GradientStop(0, ToColor4(clr1)),
                            new Vortice.Direct2D1.GradientStop(1, ToColor4(clr2))
                        };
                        using (var stopCollection = _d2dContext.CreateGradientStopCollection(stopsH))
                        {
                            var props = new LinearGradientBrushProperties
                            {
                                StartPoint = new System.Numerics.Vector2(0, 0),
                                EndPoint = new System.Numerics.Vector2(1, 0)
                            };
                            return _d2dContext.CreateLinearGradientBrush(props, stopCollection);
                        }
                    }
                case ColorMode.DualColorHeight:
                    {
                        var stopsH = new Vortice.Direct2D1.GradientStop[]
                        {
                            new Vortice.Direct2D1.GradientStop(0, ToColor4(clr1)),
                            new Vortice.Direct2D1.GradientStop(1, ToColor4(clr2))
                        };
                        using (var stopCollection = _d2dContext.CreateGradientStopCollection(stopsH))
                        {
                            var props = new LinearGradientBrushProperties
                            {
                                StartPoint = new System.Numerics.Vector2(0, 0),
                                EndPoint = new System.Numerics.Vector2(1, 0)
                            };
                            return _d2dContext.CreateLinearGradientBrush(props, stopCollection);
                        }
                    }
                case ColorMode.GradientVertical:
                    { 
                        var stopsGV = new List<Vortice.Direct2D1.GradientStop>();
                        for (int i = 0; i < grdClrs.Length; i++)
                        {
                            float pos = (float)i / (grdClrs.Length - 1);
                            stopsGV.Add(new Vortice.Direct2D1.GradientStop(pos, ToColor4(grdClrs[i])));
                        }
                        using (var stopCollection = _d2dContext.CreateGradientStopCollection(stopsGV.ToArray()))
                        {
                            var props = new LinearGradientBrushProperties
                            {
                                StartPoint = new System.Numerics.Vector2(0, 0),
                                EndPoint = new System.Numerics.Vector2(0, 1)
                            };
                            return _d2dContext.CreateLinearGradientBrush(props, stopCollection);
                        }
                    }
                case ColorMode.GradientHorizontal:
                    {
                        var stopsGV = new List<Vortice.Direct2D1.GradientStop>();
                        for (int i = 0; i < grdClrs.Length; i++)
                        {
                            float pos = (float)i / (grdClrs.Length - 1);
                            stopsGV.Add(new Vortice.Direct2D1.GradientStop(pos, ToColor4(grdClrs[i])));
                        }
                        using (var stopCollection = _d2dContext.CreateGradientStopCollection(stopsGV.ToArray()))
                        {
                            var props = new LinearGradientBrushProperties
                            {
                                StartPoint = new System.Numerics.Vector2(0, 0),
                                EndPoint = new System.Numerics.Vector2(1, 0)
                            };
                            return _d2dContext.CreateLinearGradientBrush(props, stopCollection);
                        }
                    }
                case ColorMode.GradientHeight:
                    {
                        var stopsGV = new List<Vortice.Direct2D1.GradientStop>();
                        for (int i = 0; i < grdClrs.Length; i++)
                        {
                            float pos = (float)i / (grdClrs.Length - 1);
                            stopsGV.Add(new Vortice.Direct2D1.GradientStop(pos, ToColor4(grdClrs[i])));
                        }
                        using (var stopCollection = _d2dContext.CreateGradientStopCollection(stopsGV.ToArray()))
                        {
                            var props = new LinearGradientBrushProperties
                            {
                                StartPoint = new System.Numerics.Vector2(0, 0),
                                EndPoint = new System.Numerics.Vector2(1, 0)
                            };
                            return _d2dContext.CreateLinearGradientBrush(props, stopCollection);
                        }
                    }
                case ColorMode.GradientPitch:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(
                        PitchDetector.GetPitchColor(MainWindow.PitchFreq, grdClrs)));
                case ColorMode.DualColorPitch:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(
                        PitchDetector.GetPitchColor(MainWindow.PitchFreq, new[] { clr1, clr2 })));
                case ColorMode.GradientFrequency:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(
                        Visualizer.GetGradientColor(
                            grdClrs,
                            (MainWindow.PitchFreq / 2200) - 0.03)));
                case ColorMode.DualColorFrequency:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(
                        Visualizer.GetGradientColor(
                            new[] { clr1, clr2 },
                            (MainWindow.PitchFreq / 2200) - 0.03)));

                default:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(clr1));
            }
        }
        private ID2D1Brush CreateBrushForLayer(bool peaks, int index, int total, float width, float height, float top, double max)
        {
            float ratio = 0;
            if (max > 0)
            {
                ratio = (float)(max / 0.7);
                ratio = Math.Clamp(ratio, 0f, 1f);
            }

            var clrMode = peaks ? Visualizer.InstanceOptions._peakColorType : Visualizer.InstanceOptions._barColorType;
            var clr1 = peaks ? MainWindow._color3 : MainWindow._color1;
            var clr2 = peaks ? MainWindow._color4 : MainWindow._color2;
            var grdClrs = peaks ? MainWindow.colorPeakArrayGradient : MainWindow.colorArrayGradient;


            switch (clrMode)
            {
                case ColorMode.SolidColor:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(clr1));
                case ColorMode.DualColorVertical:
                    var stopsV = new Vortice.Direct2D1.GradientStop[]
                    {
                        new Vortice.Direct2D1.GradientStop(0, ToColor4(clr1)),
                        new Vortice.Direct2D1.GradientStop(1, ToColor4(clr2))
                    };
                    using (var stopCollection = _d2dContext.CreateGradientStopCollection(stopsV))
                    {
                        var props = new LinearGradientBrushProperties
                        {
                            StartPoint = new System.Numerics.Vector2(0, 0),
                            EndPoint = new System.Numerics.Vector2(0, 1)
                        };
                        return _d2dContext.CreateLinearGradientBrush(props, stopCollection);
                    }
                case ColorMode.DualColorHorizontal:
                    {
                        var stopsH = new Vortice.Direct2D1.GradientStop[]
                        {
                            new Vortice.Direct2D1.GradientStop(0, ToColor4(clr1)),
                            new Vortice.Direct2D1.GradientStop(1, ToColor4(clr2))
                        };
                        using (var stopCollection = _d2dContext.CreateGradientStopCollection(stopsH))
                        {
                            var props = new LinearGradientBrushProperties
                            {
                                StartPoint = new System.Numerics.Vector2(0, 0),
                                EndPoint = new System.Numerics.Vector2(1, 0)
                            };
                            return _d2dContext.CreateLinearGradientBrush(props, stopCollection);
                        }
                    }
                case ColorMode.DualColorHeight:
                    {
                        var stopsH = new Vortice.Direct2D1.GradientStop[]
                        {
                            new Vortice.Direct2D1.GradientStop(0, ToColor4(clr1)),
                            new Vortice.Direct2D1.GradientStop(1, ToColor4(clr2))
                        };
                        using (var stopCollection = _d2dContext.CreateGradientStopCollection(stopsH))
                        {
                            var props = new LinearGradientBrushProperties
                            {
                                StartPoint = new System.Numerics.Vector2(0, 0),
                                EndPoint = new System.Numerics.Vector2(1, 0)
                            };
                            return _d2dContext.CreateLinearGradientBrush(props, stopCollection);
                        }
                    }
                case ColorMode.GradientVertical:
                    var stopsGV = new List<Vortice.Direct2D1.GradientStop>();
                    for (int i = 0; i < grdClrs.Length; i++)
                    {
                        float pos = (float)i / (grdClrs.Length - 1);
                        stopsGV.Add(new Vortice.Direct2D1.GradientStop(pos, ToColor4(grdClrs[i])));
                    }
                    using (var stopCollection = _d2dContext.CreateGradientStopCollection(stopsGV.ToArray()))
                    {
                        var props = new LinearGradientBrushProperties
                        {
                            StartPoint = new System.Numerics.Vector2(0, 0),
                            EndPoint = new System.Numerics.Vector2(0, 1)
                        };
                        return _d2dContext.CreateLinearGradientBrush(props, stopCollection);
                    }
                case ColorMode.GradientHorizontal:
                    {
                        var stopsGH = new List<Vortice.Direct2D1.GradientStop>();
                        for (int i = 0; i < grdClrs.Length; i++)
                        {
                            float pos = (float)i / (grdClrs.Length - 1);
                            stopsGH.Add(new Vortice.Direct2D1.GradientStop(pos, ToColor4(grdClrs[i])));
                        }
                        using (var stopCollection = _d2dContext.CreateGradientStopCollection(stopsGH.ToArray()))
                        {
                            var props = new LinearGradientBrushProperties
                            {
                                StartPoint = new System.Numerics.Vector2(0, 0),
                                EndPoint = new System.Numerics.Vector2(1, 0)
                            };
                            return _d2dContext.CreateLinearGradientBrush(props, stopCollection);
                        }
                    }
                case ColorMode.GradientHeight:
                    {
                        var stopsGH = new List<Vortice.Direct2D1.GradientStop>();
                        for (int i = 0; i < grdClrs.Length; i++)
                        {
                            float pos = (float)i / (grdClrs.Length - 1);
                            stopsGH.Add(new Vortice.Direct2D1.GradientStop(pos, ToColor4(grdClrs[i])));
                        }
                        using (var stopCollection = _d2dContext.CreateGradientStopCollection(stopsGH.ToArray()))
                        {
                            var props = new LinearGradientBrushProperties
                            {
                                StartPoint = new System.Numerics.Vector2(0, 0),
                                EndPoint = new System.Numerics.Vector2(1, 0)
                            };
                            return _d2dContext.CreateLinearGradientBrush(props, stopCollection);
                        }
                    }
                case ColorMode.GradientPitch:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(
                        PitchDetector.GetPitchColor(MainWindow.PitchFreq, grdClrs)));
                case ColorMode.DualColorPitch:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(
                        PitchDetector.GetPitchColor(MainWindow.PitchFreq, new[] { clr1, clr2 })));
                case ColorMode.GradientFrequency:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(
                        Visualizer.GetGradientColor(
                            grdClrs,
                            (MainWindow.PitchFreq / 2200) - 0.03)));
                case ColorMode.DualColorFrequency:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(
                        Visualizer.GetGradientColor(
                            new[] { clr1, clr2 },
                            (MainWindow.PitchFreq / 2200) - 0.03)));

                default:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(MainWindow._color1));
            }
        }
    }
}