using Newtonsoft.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FreqFreak
{
    public partial class OptionsWindow : Window
    {
        private bool AllowValueSet;
        private CancellationTokenSource _cts = new();
        private Color badClr = Color.FromArgb(255, 255, 0, 0);
        private Color goodClr = Color.FromArgb(255, 0, 255, 0);

        public OptionsWindow()
        {
            AllowValueSet = false;

            this.Closed += (sender, e) =>
            {
                _cts.Cancel();
                AllowValueSet = false;
                Visualizer.OptionsWindow = new OptionsWindow();
                Visualizer.ShowBg = false;
                Visualizer.ChangeBg = true;
            };

            var fpsThread = new Thread(() =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var fpsStats = MainWindow.displayFpsMeter.GetFpsStats();
                        var fftptStats = Visualizer.fpsMeter.GetFpsStats();
                        double fpsStep = fpsStats.avgFps / 400;
                        double fftpsStep = fftptStats.avgFps / 400;
                        var fpsClr = Visualizer.GetGradientColor(new Color[] { badClr, goodClr }, fpsStep);
                        var fftpsClr = Visualizer.GetGradientColor(new Color[] { badClr, goodClr }, fftpsStep);

                        FPSMeter.Text = $"FPS Avg: {fpsStats.avgFps.ToString("00000.0")}/s | FPS: {fpsStats.curFps.ToString("00000.0")}/s | : {fpsStats.ft.ToString("0.000")}ms";
                        FFTPSMeter.Text = $"FFTPS Avg: {fftptStats.avgFps.ToString("00000.0")}/s | FFTPS: {fftptStats.curFps.ToString("00000.0")}/s | : {fftptStats.ft.ToString("0.000")}ms";
                        FPSStatus.Fill = new SolidColorBrush(fpsClr);
                        FFTPSStatus.Fill = new SolidColorBrush(fftpsClr);
                    });
                    Thread.Sleep(16);
                }
            });
            fpsThread.Start();

            InitializeComponent();

            ResetMenu();

            AllowValueSet = true;
        }
        private void ResetMenu()
        {
            // Set the menu's values to the values stored in settings
            BarHeightInput.Text = Visualizer.InstanceOptions._height.ToString();
            NumBarInput.Text = Visualizer.InstanceOptions._bars.ToString();
            BarWidthInput.Text = Visualizer.InstanceOptions._barWidth.ToString();
            BarGapInput.Text = Visualizer.InstanceOptions._barGap.ToString();
            FloorInput.Text = Visualizer.InstanceOptions._dbFloor.ToString();
            FreqMinInput.Text = Visualizer.InstanceOptions._fMin.ToString();
            FreqMaxInput.Text = Visualizer.InstanceOptions._fMax.ToString();
            BarHeightMinInput.Text = Visualizer.InstanceOptions._minHeight.ToString();
            BinSmoothInput.Text = Visualizer.InstanceOptions._smooth.ToString();
            AttackSpeedInput.Text = Visualizer.InstanceOptions._attackSpeed.ToString();
            DecaySpeedInput.Text = Visualizer.InstanceOptions._decaySpeed.ToString();
            FFTResolutionInput.Text = Visualizer.InstanceOptions._fftSize.ToString();
            SpectrogramInput.Text = Visualizer.InstanceOptions._scaleMode.ToString();
            RangeInput.Text = Visualizer.InstanceOptions._dbRange.ToString();
            PosInput.SelectedIndex = Visualizer.InstanceOptions._Position;
            ShowPeaksInput.IsChecked = Visualizer.InstanceOptions._showPeaks;
            BarColorOne.SelectedColor = Visualizer.InstanceOptions._barColor1;
            BarColorTwo.SelectedColor = Visualizer.InstanceOptions._barColor2;
            PeakColor.SelectedColor = Visualizer.InstanceOptions._peakColor;
            PeakColorTwo.SelectedColor = Visualizer.InstanceOptions._peakColor2;
            TrayIconColor.SelectedColor = MainWindow._TrayIconColor;
            BarColorTypeInput.SelectedIndex = Visualizer.InstanceOptions._barColorType;
            RotationInput.Text = Visualizer.InstanceOptions._rotation.ToString();
            if(Visualizer.InstanceOptions._peakColorType == -1)
            {
                Visualizer.InstanceOptions._peakColorType = 0;
            }
            PeakColorTypeInput.SelectedIndex = Visualizer.InstanceOptions._peakColorType;
            PeakDecay.Text = Visualizer.InstanceOptions._peakDecay.ToString();
            PeakHold.Text = Visualizer.InstanceOptions._peakHold.ToString();
            ColorMoveSpeedInput.Text = Visualizer.InstanceOptions._ColorMoveSpeed.ToString();
            ColorChangeFreqInput.Text = Visualizer.InstanceOptions._ColorChangeFreqency.ToString();
            InvertSpectrum.IsChecked = Visualizer.InstanceOptions._invertSpectrum;

            if (Visualizer.InstanceOptions._rotateColor == 0)
            {
                NoMovement.IsChecked = true;
            }
            else if(Visualizer.InstanceOptions._rotateColor == 1)
            {
                LeftMovement.IsChecked = true;
            }
            else if (Visualizer.InstanceOptions._rotateColor == 2)
            {
                RightMovement.IsChecked = true;
            }
        }
        private void SetValues()
        {
            // Set values (most of them anyway)
            if (!AllowValueSet) return;

            if (int.TryParse(BarHeightInput.Text, out int height))
            {
                Visualizer.InstanceOptions._height = height;
            }

            if (int.TryParse(NumBarInput.Text, out int bars))
            {
                Visualizer.InstanceOptions._bars = bars;
            }

            Visualizer.InstanceOptions._invertSpectrum = InvertSpectrum.IsChecked.Value;

            if (double.TryParse(PeakDecay.Text, out double decay))
            {
                Visualizer.InstanceOptions._peakDecay = decay;
            }

            if (double.TryParse(PeakHold.Text, out double hold))
            {
                Visualizer.InstanceOptions._peakHold = hold;
            }

            if (double.TryParse(RotationInput.Text, out double rotatata))
            {
                Visualizer.InstanceOptions._rotation = rotatata;
            }

            if (int.TryParse(BarWidthInput.Text, out int barWidth))
            {
                Visualizer.InstanceOptions._barWidth = barWidth;
            }

            if (int.TryParse(BarGapInput.Text, out int barGap))
            {
                Visualizer.InstanceOptions._barGap = barGap;
            }

            if (double.TryParse(FloorInput.Text, out double dbFloor))
            {
                Visualizer.InstanceOptions._dbFloor = dbFloor;
            }

            if (float.TryParse(ColorMoveSpeedInput.Text, out float clrMove))
            {
                Visualizer.InstanceOptions._ColorMoveSpeed = clrMove;
            }
            if (double.TryParse(ColorChangeFreqInput.Text, out double clrChange))
            {
                Visualizer.InstanceOptions._ColorChangeFreqency = clrChange;
            }

            if (double.TryParse(FreqMinInput.Text, out double fMin))
            {
                Visualizer.InstanceOptions._fMin = fMin;
            }

            if (double.TryParse(FreqMaxInput.Text, out double fMax))
            {
                Visualizer.InstanceOptions._fMax = fMax;
            }

            if (double.TryParse(BarHeightMinInput.Text, out double minHeight))
            {
                Visualizer.InstanceOptions._minHeight = minHeight;
            }

            if (int.TryParse(BinSmoothInput.Text, out int smooth))
            {
                Visualizer.InstanceOptions._smooth = smooth;
            }

            if (double.TryParse(AttackSpeedInput.Text, out double barAttack))
            {
                Visualizer.InstanceOptions._attackSpeed = barAttack;
            }

            if (double.TryParse(DecaySpeedInput.Text, out double barDecay))
            {
                Visualizer.InstanceOptions._decaySpeed = barDecay;
            }

            int fftOut = Visualizer.InstanceOptions._fftSize;
            if (int.TryParse((string)((ComboBoxItem)FFTResolutionInput.SelectedItem).Content, out fftOut))
            {
                if (fftOut != Visualizer.InstanceOptions._fftSize)
                {
                    Visualizer.InstanceOptions._fftSize = fftOut;
                    Visualizer._captureCTS.Cancel();
                    Visualizer._captureCTS = new();
                    var _captureThread = new Thread(() =>
                    {
                        Visualizer.StartCapture(Visualizer._captureCTS.Token);
                    });
                    _captureThread.Start();
                }
            }

            if (double.TryParse(RangeInput.Text, out double dbRange))
            {
                Visualizer.InstanceOptions._dbRange = dbRange;
            }

            Visualizer.InstanceOptions._Position = PosInput.SelectedIndex;
            Visualizer.InstanceOptions._showPeaks = ShowPeaksInput.IsChecked.Value;

            var boclr = Color.FromArgb((byte)BarColorOne.Color.A, (byte)BarColorOne.Color.RGB_R, (byte)BarColorOne.Color.RGB_G, (byte)BarColorOne.Color.RGB_B);
            var btclr = Color.FromArgb((byte)BarColorTwo.Color.A, (byte)BarColorTwo.Color.RGB_R, (byte)BarColorTwo.Color.RGB_G, (byte)BarColorTwo.Color.RGB_B);
            var pclr = Color.FromArgb((byte)PeakColor.Color.A, (byte)PeakColor.Color.RGB_R, (byte)PeakColor.Color.RGB_G, (byte)PeakColor.Color.RGB_B);
            Visualizer.InstanceOptions._barColor1 = boclr;
            Visualizer.InstanceOptions._barColor2 = btclr;
            Visualizer.InstanceOptions._peakColor = pclr;

            Visualizer.UpdateSettings = true;
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            ResetMenu();
        }

        private void Input_TextChanged(object sender, TextChangedEventArgs e)
        {
            SetValues();
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                DefaultExt = ".json",
                AddExtension = true
            };
            if (saveFileDialog.ShowDialog() == true)
            {
                // Save the Visualizer.InstanceOptions to the selected file
                string json = JsonConvert.SerializeObject(Visualizer.InstanceOptions);
                System.IO.File.WriteAllText(saveFileDialog.FileName, json);
            }
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                DefaultExt = ".json",
                AddExtension = true
            };
            if (openFileDialog.ShowDialog() == true)
            {
                // Load the Visualizer.InstanceOptions from the selected file
                string json = System.IO.File.ReadAllText(openFileDialog.FileName);
                var options = JsonConvert.DeserializeObject<Settings>(json);
                if (options != null)
                {
                    AllowValueSet = false;
                    if (options._fftSize != Visualizer.InstanceOptions._fftSize)
                    {
                        Visualizer._captureCTS.Cancel();
                        Visualizer._captureCTS = new();
                        Visualizer.InstanceOptions._fftSize = options._fftSize;
                        var _captureThread = new Thread(() =>
                        {
                            Visualizer.StartCapture(Visualizer._captureCTS.Token);
                        });
                        _captureThread.Start();
                    }
                    Visualizer.InstanceOptions = options;
                    Visualizer.UpdateSettings = true;
                    ResetMenu();
                    AllowValueSet = true;
                }
                else
                {
                    MessageBox.Show("Failed to load settings from file.");
                }
            }
        }

        private void SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SetValues();
        }

        private void BarColorTypeInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = BarColorTypeInput.SelectedIndex;
            int idx2 = PeakColorTypeInput.SelectedIndex;
            Visualizer.InstanceOptions._barColorType = idx;
            Visualizer.InstanceOptions._peakColorType = idx2;
            
            Visualizer.UpdateSettings = true;
        }
        private void ShowPeaksInput_Click(object sender, RoutedEventArgs e)
        {
            Visualizer.InstanceOptions._showPeaks = ShowPeaksInput.IsChecked.Value;
        }

        private void BarColor_ColorChanged(object sender, RoutedEventArgs e)
        {
            if (!AllowValueSet)
            {
                return;
            }
            var boclr = Color.FromArgb((byte)BarColorOne.Color.A, (byte)BarColorOne.Color.RGB_R, (byte)BarColorOne.Color.RGB_G, (byte)BarColorOne.Color.RGB_B);
            var btclr = Color.FromArgb((byte)BarColorTwo.Color.A, (byte)BarColorTwo.Color.RGB_R, (byte)BarColorTwo.Color.RGB_G, (byte)BarColorTwo.Color.RGB_B);
            var pclr = Color.FromArgb((byte)PeakColor.Color.A, (byte)PeakColor.Color.RGB_R, (byte)PeakColor.Color.RGB_G, (byte)PeakColor.Color.RGB_B);
            var pclr2 = Color.FromArgb((byte)PeakColorTwo.Color.A, (byte)PeakColorTwo.Color.RGB_R, (byte)PeakColorTwo.Color.RGB_G, (byte)PeakColorTwo.Color.RGB_B);

            Visualizer.InstanceOptions._barColor1 = boclr;
            Visualizer.InstanceOptions._barColor2 = btclr;
            Visualizer.InstanceOptions._peakColor = pclr;
            Visualizer.InstanceOptions._peakColor2 = pclr2;
            Visualizer.UpdateSettings = true;
        }

        private void NoMovement_Click(object sender, RoutedEventArgs e)
        {
            if(LeftMovement.IsChecked.Value)
            {
                Visualizer.InstanceOptions._rotateColor = 1;
            }
            else if (RightMovement.IsChecked.Value)
            {
                Visualizer.InstanceOptions._rotateColor = 2;
            }
            else
            {
                Visualizer.InstanceOptions._rotateColor = 0;
            }
        }

        private void SpectrogramInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string selectedItem = (string)((ComboBoxItem)SpectrogramInput.SelectedItem).Content;

            if (Enum.TryParse(typeof(ScaleMode), selectedItem, out object result))
            {
                Visualizer.InstanceOptions._scaleMode = (ScaleMode)result;
            }
        }

        private void AudioDevicesButton_Click(object sender, RoutedEventArgs e)
        {
            if (Visualizer.AudioDevicesWindow == null)
            {
                Visualizer.AudioDevicesWindow = new();
                Visualizer.AudioDevicesWindow.Show();
            }
            else if (!Visualizer.AudioDevicesWindow.IsVisible)
            {
                Visualizer.AudioDevicesWindow = new();
                Visualizer.AudioDevicesWindow.Show();
            }
        }

        private void InvertSpectrum_Click(object sender, RoutedEventArgs e)
        {
            Visualizer.InstanceOptions._invertSpectrum = InvertSpectrum.IsChecked.Value;
        }

        private void PreviewInput_Click(object sender, RoutedEventArgs e)
        {
            Visualizer.ShowBg = !PreviewInput.IsChecked.Value;
            Visualizer.ChangeBg = true;
        }

        private void TrayIconColor_ColorChanged(object sender, RoutedEventArgs e)
        {
            if (!AllowValueSet)
            {
                return;
            }
            var ticlr = Color.FromArgb((byte)TrayIconColor.Color.A, (byte)TrayIconColor.Color.RGB_R, (byte)TrayIconColor.Color.RGB_G, (byte)TrayIconColor.Color.RGB_B);

            MainWindow._TrayIconColor = ticlr;
            MainWindow.FVZWindowHandle.SetAccentColor();
            Visualizer.UpdateSettings = true;
            MainWindow.HueShiftIcon();
        }

        private void FVZButton_Click(object sender, RoutedEventArgs e)
        {
            if (!MainWindow.FVZMode)
            {
                MainWindow.FVZWindowHandle = new FVZWindow();
                MainWindow.FVZWindowHandle.Show();
                MainWindow.FVZMode = true;
            }
            else
            {
                MainWindow.FVZWindowHandle.Hide();
                FVZPlayer.Stop();
                MainWindow.FVZMode = false;
            }
        }

        private void OnTopInput_Click(object sender, RoutedEventArgs e)
        {
            Visualizer.MainWin.Topmost = OnTopInput.IsChecked.Value;
        }

        private void SwapButton_Click(object sender, RoutedEventArgs e)
        {
            var temp = BarColorOne.SelectedColor;
            BarColorOne.SelectedColor = BarColorTwo.SelectedColor;
            BarColorTwo.SelectedColor = temp;
        }

        private void PeakSwapButton_Click(object sender, RoutedEventArgs e)
        {
            var temp = PeakColor.SelectedColor;
            PeakColor.SelectedColor = PeakColorTwo.SelectedColor;
            PeakColorTwo.SelectedColor = temp;
        }
    }
}
