using System.Windows.Media;

namespace FreqFreak
{
    public enum ScaleMode { Normalized=0, Mel=1, Log10=2 }
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
        public int _Position { get; set; }
        public bool _showPeaks { get; set; }
        public bool _invertSpectrum { get; set; }
        public int _rotateColor { get; set; }
        public double _peakDecay { get; set; }
        public double _peakHold { get; set; }
        public double _attackSpeed { get; set; }
        public double _decaySpeed { get; set; }
        public float _ColorMoveSpeed { get; set; }
        public double _ColorChangeFreqency { get; set; }
        public ScaleMode _scaleMode { get; set; }

        public int _barColorType { get; set; }
        public int _peakColorType { get; set; } = 0;
        public Color _barColor1 { get; set; }
        public Color _barColor2 { get; set; }
        public Color _peakColor { get; set; }
        public Color _peakColor2 { get; set; }

        public void SetDefaults()
        {
            _fftSize = 8192;
            _height = 400;
            _bars = 100;
            _barGap = 0;
            _barWidth = 8;
            _dbFloor = -90;
            _dbRange = 90;
            _fMax = 20_000;
            _fMin = 20;
            _minHeight = 10;
            _smooth = 1;
            _Position = 0;
            _showPeaks = true;
            _scaleMode = ScaleMode.Normalized;
            _barColorType = 0;
            _peakColorType = 0;
            _peakDecay = 2;
            _peakHold = 60;
            _attackSpeed = 0.9;
            _decaySpeed = 0.9;
            _invertSpectrum = false;
            _barColor1 = Color.FromArgb(255, 0, 0, 0);
            _barColor2 = Color.FromArgb(255, 255, 255, 255);
            _peakColor = Color.FromArgb(255, 0, 0, 0);
            _peakColor2 = Color.FromArgb(255, 255, 255, 255);
        }
        public Settings()
        {

        }
    }
}
