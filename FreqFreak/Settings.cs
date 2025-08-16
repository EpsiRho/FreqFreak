using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FreqFreak
{
    public enum ScaleMode { Normalized=0, Mel=1, Log10=2 }
    public enum ChannelMode { Mono=0, StereoLeft=1, StereoRight=2, Stereo=3 }
    public enum VisualizationMode { Bottom = 0, Center = 1, Top = 2, OuterCircle = 3, InnerCircle = 4, Oscilloscope = 5 }
    public enum ColorPreset { 
        Rainbow = 0, 
        Sakura = 1,
        Flames = 2, 
        Retro = 3, 
        OceanDepths = 4, 
        MidnightSurf = 5,
        Pastel = 6,
        Watermelon = 7,
        SynthwaveSunset = 8,
        GrassHills = 9,
        Blorange = 10,
        Coffee = 11,

    }
    /*
     *  <ComboBoxItem Content="Solid Color"/>
                            <ComboBoxItem Content="Dual Color Vertical"/>
                            <ComboBoxItem Content="Dual Color Horizontal"/>
                            <ComboBoxItem Content="Dual Color Height"/>
                            <ComboBoxItem Content="Dual Color Pitch"/>
                            <ComboBoxItem Content="Dual Color Frequency"/>
                            <ComboBoxItem Content="Gradient Vertical"/>
                            <ComboBoxItem Content="Gradient Horizontal"/>
                            <ComboBoxItem Content="Gradient Height"/>
                            <ComboBoxItem Content="Gradient Pitch"/>
                            <ComboBoxItem Content="Gradient Frequency"/>*/
    public enum ColorMode { SolidColor = 0, 
                            DualColorVertical = 1, DualColorHorizontal = 2, DualColorHeight = 3, DualColorPitch = 4, DualColorFrequency = 5, 
                            GradientVertical = 6, GradientHorizontal = 7, GradientHeight = 8, GradientPitch = 9, GradientFrequency = 10, Match = 11 }
    public class NoLayoutClipGrid : Grid
    {
        protected override Geometry GetLayoutClip(Size layoutSlotSize) => null;
    }     
    public class StupidTuple
    {
        public Guid id { get; set; }
        public CutoutWindow window { get; set; }
        public StupidTuple(Guid id, CutoutWindow window)
        {
            this.id = id;
            this.window = window;
        }
    }
    public class Settings
    {
        public int _fftSize;
        public int _height { get; set; }
        public int _bars { get; set; }
        public int _barGap { get; set; }
        public int _barWidth { get; set; }
        public double _dbFloor { get; set; }
        public double _dbRange { get; set; }
        public double _fMax { get; set; }
        public double _fMin { get; set; }
        public double _minHeight { get; set; }
        public int _smooth { get; set; }
        public VisualizationMode _visualizationMode { get; set; }
        public bool _showPeaks { get; set; }
        public bool _showOnlyPeaks { get; set; }
        public bool _showPeaksLine { get; set; }
        public bool _showLines { get; set; }
        public bool _showBars { get; set; }
        public bool _invertSpectrum { get; set; }
        public bool _detectPitch { get; set; }
        public int _rotateColor { get; set; }
        public double _peakDecay { get; set; }
        public double _peakHold { get; set; }
        public double _attackSpeed { get; set; }
        public double _decaySpeed { get; set; }
        public float _ColorMoveSpeed { get; set; }
        public double _ColorChangeFreqency { get; set; }
        public double _rotation { get; set; }
        public double _bassScale { get; set; }
        public double _bassShake { get; set; }
        public double _bassDampening { get; set; }
        public float _lineThickness { get; set; }
        public ChannelMode _channelMode { get; set; }
        public ScaleMode _scaleMode { get; set; }

        public ColorMode _barColorType { get; set; }
        public ColorMode _peakColorType { get; set; }
        public Color _barColor1 { get; set; }
        public Color _barColor2 { get; set; }
        public Color _peakColor { get; set; }
        public Color _peakColor2 { get; set; }
        public Color[] _customNoteGradientColors { get; set; }
        public Color[] _customPeakNoteGradientColors { get; set; }

        public void SetDefaults()
        {
            _fftSize = 8192;
            _height = 400;
            _bars = 200;
            _barGap = 0;
            _barWidth = 5;
            _dbFloor = -80;
            _dbRange = 90;
            _fMax = 20_000;
            _fMin = 20;
            _minHeight = 10;
            _smooth = 1;
            _visualizationMode = VisualizationMode.Center;
            _showPeaks = true;
            _showLines = false;
            _showPeaksLine = false;
            _showBars = true;
            _detectPitch = true;
            _scaleMode = ScaleMode.Normalized;
            _barColorType = ColorMode.SolidColor;
            _peakColorType = ColorMode.Match;
            _peakDecay = 2;
            _peakHold = 30;
            _attackSpeed = 0.5;
            _decaySpeed = 0.3;
            _bassScale = 1;
            _bassShake = 0;
            _bassDampening = 1.17;
            _lineThickness = 2;
            _invertSpectrum = false;
            _channelMode = ChannelMode.Mono;
            _barColor1 = Color.FromArgb(255, 0, 0, 0);
            _barColor2 = Color.FromArgb(255, 255, 255, 255);
            _peakColor = Color.FromArgb(255, 0, 0, 0);
            _peakColor2 = Color.FromArgb(255, 255, 255, 255);
            SetColorPreset();
            SetColorPreset(peaks:true);
        }
        public void SetColorPreset(ColorPreset preset = ColorPreset.Rainbow, bool peaks = false)
        {
            var clrs = new Color[] { };
            switch (preset)
            {
                case ColorPreset.Rainbow:
                    clrs = new Color[]
                    {
                        Color.FromArgb(255, 255,   0, 0),    // C  - Red
                        Color.FromArgb(255, 255, 128, 0),    // C# - Red-Orange
                        Color.FromArgb(255, 255, 255, 0),    // D  - Yellow
                        Color.FromArgb(255, 128, 255, 0),    // D# - Yellow-Green
                        Color.FromArgb(255,   0, 255, 0),    // E  - Green
                        Color.FromArgb(255, 0, 255, 128),    // F  - Green-Cyan
                        Color.FromArgb(255, 0, 255, 255),    // F# - Cyan
                        Color.FromArgb(255, 0, 128, 255),    // G  - Cyan-Blue
                        Color.FromArgb(255, 0,   0, 255),    // G# - Blue
                        Color.FromArgb(255, 128, 0, 255),    // A  - Blue-Purple
                        Color.FromArgb(255, 255, 0, 255),    // A# - Purple
                        Color.FromArgb(255, 255, 0, 128),    // B  - Purple-Red
                    };
                    break;
                case ColorPreset.Sakura:
                    clrs = new Color[]
                    {
                        (Color)ColorConverter.ConvertFromString("#FF5C8A"),
                        (Color)ColorConverter.ConvertFromString("#FF92C2"),
                        (Color)ColorConverter.ConvertFromString("#FFD1E3"),
                        (Color)ColorConverter.ConvertFromString("#A6E3E9"),
                        (Color)ColorConverter.ConvertFromString("#64DFDF"),
                    };
                    break;
                case ColorPreset.Flames:
                    clrs = new Color[]
                    {
                        (Color)ColorConverter.ConvertFromString("#FFDF0D2D"),
                        (Color)ColorConverter.ConvertFromString("#FFDC3B18"),
                        (Color)ColorConverter.ConvertFromString("#FFF26417"),
                        (Color)ColorConverter.ConvertFromString("#FFF28F0A"),
                        (Color)ColorConverter.ConvertFromString("#FFEDB605"),
                    };
                    break;
                case ColorPreset.Retro:
                    clrs = new Color[]
                    {
                        (Color)ColorConverter.ConvertFromString("#2B2D42"),
                        (Color)ColorConverter.ConvertFromString("#EF476F"),
                        (Color)ColorConverter.ConvertFromString("#FFD166"),
                        (Color)ColorConverter.ConvertFromString("#06D6A0"),
                        (Color)ColorConverter.ConvertFromString("#118AB2"),
                    };
                    break;
                case ColorPreset.OceanDepths:
                    clrs = new Color[]
                    {
                        (Color)ColorConverter.ConvertFromString("#FF002030"),
                        (Color)ColorConverter.ConvertFromString("#FF00243A"),
                        (Color)ColorConverter.ConvertFromString("#FF013A63"),
                        (Color)ColorConverter.ConvertFromString("#FF086FCE"),
                        (Color)ColorConverter.ConvertFromString("#FF39C6F4"),
                        (Color)ColorConverter.ConvertFromString("#FF65EBFF"),
                    };
                    break;
                case ColorPreset.MidnightSurf:
                    clrs = new Color[]
                    {
                        (Color)ColorConverter.ConvertFromString("#00F5D4"),
                        (Color)ColorConverter.ConvertFromString("#00BBF9"),
                        (Color)ColorConverter.ConvertFromString("#4EA8DE"),
                        (Color)ColorConverter.ConvertFromString("#6930C3"),
                        (Color)ColorConverter.ConvertFromString("#3A0CA3"),
                    };
                    break;
                case ColorPreset.Pastel:
                    clrs = new Color[]
                    {
                        (Color)ColorConverter.ConvertFromString("#F0F4F8"),
                        (Color)ColorConverter.ConvertFromString("#CFE8FF"),
                        (Color)ColorConverter.ConvertFromString("#B8E1DD"),
                        (Color)ColorConverter.ConvertFromString("#EAD6FF"),
                        (Color)ColorConverter.ConvertFromString("#8093F1"),
                    };
                    break;
                case ColorPreset.Watermelon:
                    clrs = new Color[]
                    {
                        (Color)ColorConverter.ConvertFromString("#FFD8003D"),
                        (Color)ColorConverter.ConvertFromString("#FFFB6F92"),
                        (Color)ColorConverter.ConvertFromString("#FFA7C957"),
                        (Color)ColorConverter.ConvertFromString("#FF538D22"),
                        (Color)ColorConverter.ConvertFromString("#FF007135"),
                    };
                    break;
                case ColorPreset.SynthwaveSunset:
                    clrs = new Color[]
                    {
                        (Color)ColorConverter.ConvertFromString("#2E1A47"),
                        (Color)ColorConverter.ConvertFromString("#7C3AED"),
                        (Color)ColorConverter.ConvertFromString("#F15BB5"),
                        (Color)ColorConverter.ConvertFromString("#FF9671"),
                        (Color)ColorConverter.ConvertFromString("#FFC75F"),
                    };
                    break;
                case ColorPreset.GrassHills:
                    clrs = new Color[]
                    {
                        (Color)ColorConverter.ConvertFromString("#FF0B2715"),
                        (Color)ColorConverter.ConvertFromString("#FF1E6A3A"),
                        (Color)ColorConverter.ConvertFromString("#FF42B46D"),
                        (Color)ColorConverter.ConvertFromString("#FFA4F1B6"),
                    };
                    break;
                case ColorPreset.Blorange:
                    clrs = new Color[]
                    {
                        (Color)ColorConverter.ConvertFromString("#FF264653"),
                        (Color)ColorConverter.ConvertFromString("#FF2A9D8F"),
                        (Color)ColorConverter.ConvertFromString("#FFE9C46A"),
                        (Color)ColorConverter.ConvertFromString("#FFF4A261"),
                        (Color)ColorConverter.ConvertFromString("#FFE76F51"),
                    };
                    break;
                case ColorPreset.Coffee:
                    clrs = new Color[]
                    {
                        (Color)ColorConverter.ConvertFromString("#FFF6E7D7"),
                        (Color)ColorConverter.ConvertFromString("#FFE6B8A2"),
                        (Color)ColorConverter.ConvertFromString("#FFC46A3B"),
                        (Color)ColorConverter.ConvertFromString("#FF8A5A44"),
                        (Color)ColorConverter.ConvertFromString("#FF2F2A2B"),
                    };
                    break;

            }
            if (peaks)
            {
                _customPeakNoteGradientColors = clrs;
            }
            else
            {
                _customNoteGradientColors = clrs;
            }
           
        }
        public Settings()
        {

        }
    }
}
