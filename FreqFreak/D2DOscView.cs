using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.Wpf;

namespace FreqFreak
{
    public class OscilloscopeVorticeControl : DrawingSurface
    {
        private sealed record Layer(Vector2[] Points, ID2D1Brush Brush, DateTime Timestamp);
        private readonly List<Layer> _layers = new();

        // Direct2D resources
        private ID2D1Factory? _d2dFactory;
        private ID2D1Device? _d2dDevice;
        private ID2D1DeviceContext? _d2dContext;
        private ID2D1Bitmap1? _targetBitmap;

        public double FadeMilliseconds { get; set; } = 250.0;
        public int MaxLayers { get; set; } = 150;

        public System.Windows.Media.Color ClearColor { get; set; } = System.Windows.Media.Color.FromArgb(0,0,0,0);

        public OscilloscopeVorticeControl()
        {
            LoadContent += OnLoadContent;
            Draw += OnDraw;
            UnloadContent += OnUnloadContent;
        }

        private void OnLoadContent(object? sender, DrawingSurfaceEventArgs e)
        {
            DisposeD2DResources();

            _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.SingleThreaded);

            using IDXGIDevice dxgiDevice = e.Device.QueryInterface<IDXGIDevice>();
            _d2dDevice = D2D1.D2D1CreateDevice(dxgiDevice);
            _d2dContext = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
            _d2dContext.UnitMode = UnitMode.Pixels;
            _d2dContext.AntialiasMode = AntialiasMode.PerPrimitive;

            CreateTargetBitmap();

            Invalidate();
        }

        private void OnUnloadContent(object? sender, DrawingSurfaceEventArgs e)
        {
            DisposeD2DResources();
        }

        private void OnDraw(object? sender, DrawEventArgs e)
        {
            if (_d2dContext == null || _targetBitmap == null)
                return;

            if (_targetBitmap.Size.Width != e.Surface.ActualWidth ||
                _targetBitmap.Size.Height != e.Surface.ActualHeight)
            {
                CreateTargetBitmap();
            }

            _d2dContext.Target = _targetBitmap;

            // Remove expired layers based on the fade time.
            DateTime now = DateTime.Now;
            for (int i = _layers.Count - 1; i >= 0; i--)
            {
                double ageMs = (now - _layers[i].Timestamp).TotalMilliseconds;
                if (ageMs > FadeMilliseconds)
                {
                    _layers[i].Brush.Dispose();
                    _layers.RemoveAt(i);
                }
            }

            // Begin drawing
            var clearColor = ToColor4(ClearColor);
            _d2dContext.BeginDraw();
            _d2dContext.Clear(clearColor);

            const double jumpThreshold = 50.0;
            double jumpThresholdSq = jumpThreshold * jumpThreshold;

            // For each layer we take each point and draw a line from point to point.
            // Osc isn't FFTd so it's -1->1 in X and Y, with 0,0 in the center
            foreach (var layer in _layers)
            {
                double ageMs = (now - layer.Timestamp).TotalMilliseconds;
                float opacity = 1.0f;
                if (FadeMilliseconds > 0)
                {
                    float t = (float)(1.0 - (ageMs / FadeMilliseconds));
                    if (t < 0) t = 0;
                    opacity = t * t;
                }
                layer.Brush.Opacity = opacity;

                Vector2 prev = layer.Points[0];
                for (int i = 1; i < layer.Points.Length; i++)
                {
                    Vector2 curr = layer.Points[i];
                    float dx = curr.X - prev.X;
                    float dy = curr.Y - prev.Y;
                    if ((dx * dx + dy * dy) > jumpThresholdSq)
                    {
                        prev = curr;
                        continue;
                    }
                    _d2dContext.DrawLine(prev, curr, layer.Brush, Visualizer.InstanceOptions._lineThickness);
                    prev = curr;
                }
            }
            var res = _d2dContext.EndDraw();

        }

        public void UpdatePlane(double[]? frameL, double[]? frameR)
        {
            if (frameL == null || frameR == null || frameL.Length == 0 || frameR.Length == 0)
            {
                return;
            }

            if (_d2dContext == null)
            {
                return;
            }

            double maxL = frameL.Select(x => Math.Abs(x)).Max();
            double maxR = frameR.Select(x => Math.Abs(x)).Max();
            double maxAmp = Math.Max(maxL, maxR);

            Vector2[] points = new Vector2[frameL.Length];
            float width = _targetBitmap?.Size.Width ?? ColorTexture?.Description.Width ?? 100f;
            float height = _targetBitmap?.Size.Height ?? ColorTexture?.Description.Height ?? 100f;
            double cx = width * 0.5;
            double cy = height * 0.5;

            for (int i = 0; i < frameL.Length; i++)
            {
                float x = (float)(cx + frameL[i] * cx);
                float y = (float)(cy - frameR[i] * cy);
                points[i] = new Vector2(x, y);
            }

            ID2D1Brush brush = CreateBrushForLayer(maxAmp, width, height);

            _layers.Add(new Layer(points, brush, DateTime.Now));
            while (_layers.Count > MaxLayers)
            {
                var oldest = _layers[0];
                oldest.Brush.Dispose();
                _layers.RemoveAt(0);
            }

            // Invalidate the surface to trigger a redraw.
            Invalidate();
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
            if (_layers.Count > 0)
            {
                foreach (var layer in _layers)
                {
                    layer.Brush.Dispose();
                }
                _layers.Clear();
            }
            _d2dContext?.Dispose();
            _d2dContext = null;
            _d2dDevice?.Dispose();
            _d2dDevice = null;
            _d2dFactory?.Dispose();
            _d2dFactory = null;
        }

        private static Vortice.Mathematics.Color4 ToColor4(System.Windows.Media.Color color)
        {
            var d2d = new Color4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
            return d2d;
        }

        private ID2D1Brush CreateBrushForLayer(double max, float width, float height)
        {
            float ratio = 0;
            if (max > 0)
            {
                ratio = (float)(max / 0.7);
                ratio = Math.Clamp(ratio, 0f, 1f);
            }
            var grdClrs = MainWindow.colorArrayGradient;

            switch (Visualizer.InstanceOptions._barColorType)
            {
                case ColorMode.SolidColor:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(MainWindow._color1));
                case ColorMode.DualColorVertical:
                    var stopsV = new Vortice.Direct2D1.GradientStop[]
                    {
                        new Vortice.Direct2D1.GradientStop(0, ToColor4(MainWindow._color1)),
                        new Vortice.Direct2D1.GradientStop(1, ToColor4(MainWindow._color2))
                    };
                    using (var stopCollection = _d2dContext.CreateGradientStopCollection(stopsV))
                    {
                        var props = new LinearGradientBrushProperties
                        {
                            StartPoint = new System.Numerics.Vector2(0, 0),
                            EndPoint = new System.Numerics.Vector2(0, height)
                        };
                        return _d2dContext.CreateLinearGradientBrush(props, stopCollection);
                    }
                case ColorMode.DualColorHorizontal:
                    var stopsH = new Vortice.Direct2D1.GradientStop[]
                    {
                        new Vortice.Direct2D1.GradientStop(0, ToColor4(MainWindow._color1)),
                        new Vortice.Direct2D1.GradientStop(1, ToColor4(MainWindow._color2))
                    };
                    using (var stopCollection = _d2dContext.CreateGradientStopCollection(stopsH))
                    {
                        var props = new LinearGradientBrushProperties
                        {
                            StartPoint = new System.Numerics.Vector2(0, 0),
                            EndPoint = new System.Numerics.Vector2(width, 0)
                        };
                        return _d2dContext.CreateLinearGradientBrush(props, stopCollection);
                    }
                case ColorMode.DualColorHeight:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(Visualizer.GetGradientColor(
                            new[] { MainWindow._color1, MainWindow._color2 },
                            (double)max / 0.7)));
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
                            EndPoint = new System.Numerics.Vector2(0, height)
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
                            EndPoint = new System.Numerics.Vector2(width, 0)
                        };
                        return _d2dContext.CreateLinearGradientBrush(props, stopCollection);
                    }
                    break;
                case ColorMode.GradientHeight:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(Visualizer.GetGradientColor(
                           grdClrs,
                           (double)max / 0.7)));
                case ColorMode.GradientPitch: 
                    return _d2dContext.CreateSolidColorBrush(ToColor4(
                        PitchDetector.GetPitchColor(MainWindow.PitchFreq, grdClrs)));
                case ColorMode.DualColorPitch: 
                    return _d2dContext.CreateSolidColorBrush(ToColor4(
                        PitchDetector.GetPitchColor(MainWindow.PitchFreq, new[] { Visualizer.InstanceOptions._barColor1, Visualizer.InstanceOptions._barColor2 })));
                case ColorMode.GradientFrequency: 
                    return _d2dContext.CreateSolidColorBrush(ToColor4(
                        Visualizer.GetGradientColor(
                            grdClrs,
                            (MainWindow.PitchFreq / 2200) - 0.03)));
                case ColorMode.DualColorFrequency: 
                    return _d2dContext.CreateSolidColorBrush(ToColor4(
                        Visualizer.GetGradientColor(
                            new[] { Visualizer.InstanceOptions._barColor1, Visualizer.InstanceOptions._barColor2 },
                            (MainWindow.PitchFreq / 2200) - 0.03)));

                default:
                    return _d2dContext.CreateSolidColorBrush(ToColor4(MainWindow._color1));
            }
        }
    }
}
