using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FreqFreak
{
    public enum ScaleMode { Normalized=0, Mel=1, Log10=2 }
    public enum ChannelMode { Mono=0, StereoLeft=1, StereoRight=2, Stereo=3 }
    public enum VisualizationMode { Bottom = 0, Center = 1, Top = 2, OuterCircle = 3, InnerCircle = 4, Oscilloscope = 5 }
    public enum ColorPreset { Rainbow = 0, BisexualLighting = 1, Flames = 2, Ocean = 3, Nature = 4, Space = 5 }
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
        public bool _showLines { get; set; }
        public bool _invertSpectrum { get; set; }
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
            _scaleMode = ScaleMode.Normalized;
            _barColorType = ColorMode.SolidColor;
            _peakColorType = ColorMode.Match;
            _peakDecay = 2;
            _peakHold = 30;
            _attackSpeed = 0.5;
            _decaySpeed = 0.3;
            _bassScale = 1;
            _bassShake = 1;
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
            if(peaks)
            {
                switch (preset)
                {
                    case ColorPreset.Rainbow:
                        _customPeakNoteGradientColors = new Color[]
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
                    case ColorPreset.BisexualLighting:
                        _customPeakNoteGradientColors = new Color[]
                        {
                        Color.FromArgb(255, 215, 0, 113),    // Pink
                        Color.FromArgb(255, 156, 78, 151),    // Light Purple
                        Color.FromArgb(255, 0, 53, 169),    // Dark Blue
                        };
                        break;
                    case ColorPreset.Ocean:
                        _customPeakNoteGradientColors = new Color[]
                        {
                        Color.FromArgb(255, 79, 66, 180),    // Ocean Blue
                        Color.FromArgb(255, 78, 91, 173),    // Liberty
                        Color.FromArgb(255, 76, 116, 166),    // Blue Yonder
                        Color.FromArgb(255, 75, 141, 160),    // Rackley
                        Color.FromArgb(255, 73, 166, 153),    // Keppel
                        Color.FromArgb(255, 72, 191, 146),    // Ocean Green
                        };
                        break;
                    case ColorPreset.Nature:
                        _customPeakNoteGradientColors = new Color[]
                        {
                        Color.FromArgb(255, 0, 52, 33),    // Dark Green
                        Color.FromArgb(255, 12, 70, 61),    // Blue-Green
                        Color.FromArgb(255, 9, 95, 84),    // Bangladesh Green
                        Color.FromArgb(255, 1, 124, 111),    // Pine Green
                        Color.FromArgb(255, 73, 166, 153),    // Paolo Veronese Green
                        };
                        break;
                    case ColorPreset.Flames:
                        _customPeakNoteGradientColors = new Color[]
                        {
                        Color.FromArgb(255, 191, 30, 51),    // Cardinal
                        Color.FromArgb(255, 220, 59, 24),    // Plochere's Vermilion
                        Color.FromArgb(255, 242, 100, 23),    // Halloween Orange
                        Color.FromArgb(255, 242, 143, 10),    // Tangerine
                        Color.FromArgb(255, 237, 182, 5),    // American Yellow
                        };
                        break;
                    case ColorPreset.Space:
                        _customPeakNoteGradientColors = new Color[]
                        {
                        Color.FromArgb(255, 20, 22, 24),    // Chinese Black
                        Color.FromArgb(255, 44, 49, 53),    // Gunmetal
                        Color.FromArgb(255, 130, 57, 130),    // Maximum Purple
                        Color.FromArgb(255, 103, 22, 110),    // Midnight
                        Color.FromArgb(255, 213, 232, 242),    // Gray
                        };
                        break;

                }
            }
            else
            {
                switch (preset)
                {
                    case ColorPreset.Rainbow:
                        _customNoteGradientColors = new Color[]
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
                    case ColorPreset.BisexualLighting:
                        _customNoteGradientColors = new Color[]
                        {
                        Color.FromArgb(255, 215, 0, 113),    // Pink
                        Color.FromArgb(255, 156, 78, 151),    // Light Purple
                        Color.FromArgb(255, 0, 53, 169),    // Dark Blue
                        };
                        break;
                    case ColorPreset.Ocean:
                        _customNoteGradientColors = new Color[]
                        {
                        Color.FromArgb(255, 79, 66, 180),    // Ocean Blue
                        Color.FromArgb(255, 78, 91, 173),    // Liberty
                        Color.FromArgb(255, 76, 116, 166),    // Blue Yonder
                        Color.FromArgb(255, 75, 141, 160),    // Rackley
                        Color.FromArgb(255, 73, 166, 153),    // Keppel
                        Color.FromArgb(255, 72, 191, 146),    // Ocean Green
                        };
                        break;
                    case ColorPreset.Nature:
                        _customNoteGradientColors = new Color[]
                        {
                        Color.FromArgb(255, 0, 52, 33),    // Dark Green
                        Color.FromArgb(255, 12, 70, 61),    // Blue-Green
                        Color.FromArgb(255, 9, 95, 84),    // Bangladesh Green
                        Color.FromArgb(255, 1, 124, 111),    // Pine Green
                        Color.FromArgb(255, 73, 166, 153),    // Paolo Veronese Green
                        };
                        break;
                    case ColorPreset.Flames:
                        _customNoteGradientColors = new Color[]
                        {
                        Color.FromArgb(255, 191, 30, 51),    // Cardinal
                        Color.FromArgb(255, 220, 59, 24),    // Plochere's Vermilion
                        Color.FromArgb(255, 242, 100, 23),    // Halloween Orange
                        Color.FromArgb(255, 242, 143, 10),    // Tangerine
                        Color.FromArgb(255, 237, 182, 5),    // American Yellow
                        };
                        break;
                    case ColorPreset.Space:
                        _customNoteGradientColors = new Color[]
                        {
                        Color.FromArgb(255, 20, 22, 24),    // Chinese Black
                        Color.FromArgb(255, 44, 49, 53),    // Gunmetal
                        Color.FromArgb(255, 130, 57, 130),    // Maximum Purple
                        Color.FromArgb(255, 103, 22, 110),    // Midnight
                        Color.FromArgb(255, 213, 232, 242),    // Gray
                        };
                        break;

                }
            }
           
        }
        public Settings()
        {

        }
    }
}
