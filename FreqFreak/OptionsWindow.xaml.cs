using H.NotifyIcon;
using H.NotifyIcon.Core;
using LibMaterial.NET;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FreqFreak
{
    public partial class OptionsWindow : Window
    {
        private bool AllowValueSet;
        private CancellationTokenSource _cts = new();
        private Color badClr = Color.FromArgb(255, 205, 10, 44);
        private Color goodClr = Color.FromArgb(0, 205, 10, 44);
        private Color bgColor = Color.FromArgb(200, 26, 26, 26);
        private Color pitchLockColor = Color.FromArgb(70, 0, 184, 255);
        private Color transparent = Color.FromArgb(0, 205, 10, 44);
        private Color transparentPitch = Color.FromArgb(0, 0, 184, 255);
        private DateTime lastPitchChange = DateTime.MinValue;
        private string lastPitch = "";
        public Dispatcher _optionsDispatcher;

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
                    try { 
                        if(_optionsDispatcher == null)
                        {
                            continue;
                        }
                        _optionsDispatcher.BeginInvoke(() =>
                        {
                            var fps = MainWindow.displayFpsMeter.RollingFps;
                            var fftps = Visualizer.fpsMeter.RollingFps;

                            double fpsStep = fps / 80;
                            double fftpsStep = fftps / 120;

                            var timeSinceLastChange = (DateTime.Now - lastPitchChange).TotalMilliseconds;
                            double pitchStep = timeSinceLastChange / 1000;
                            if(lastPitch != MainWindow.PitchText)
                            {
                                lastPitch = MainWindow.PitchText;
                                lastPitchChange = DateTime.Now;
                            }


                            var fpsClr = Visualizer.GetGradientColor(new Color[] { badClr, goodClr }, fpsStep);
                            var fftpsClr = Visualizer.GetGradientColor(new Color[] { badClr, goodClr }, fftpsStep);
                            var pitchClr = Visualizer.GetGradientColor(new Color[] { transparentPitch, pitchLockColor }, pitchStep);

                            if(ActualWidth < 440)
                            {
                                FPSMeter.Text = $"{fps.ToString("0000.0")}/s";
                                FFTPSMeter.Text = $"{fftps.ToString("0000.0")}/s";
                                PitchDisplay.Text = $"{MainWindow.PitchFreq.ToString("0000")}hz";
                            }
                            else
                            {
                                FPSMeter.Text = $"Render: {fps.ToString("00000.0")}/s";
                                FFTPSMeter.Text = $"Spectra: {fftps.ToString("00000.0")}/s";
                                PitchDisplay.Text = MainWindow.PitchText;
                            }

                            var brush = MainWindow.GetHorizontalGradientBrush(new Color[] { fpsClr, fpsClr, fftpsClr, fftpsClr, pitchClr });
                            FPSStatus.Background = brush;

                        });
                    }
                    catch (Exception)
                    {

                    }
                    Thread.Sleep(16);
                }
            });
            fpsThread.Start();

            InitializeComponent();

            if (!Visualizer._captureCTS.IsCancellationRequested)
            {
                ((Path)PlayPauseButton.Content).Data = Geometry.Parse("F1 M 6.25 2.5 L 7.5 2.5 L 7.5 17.5 L 6.25 17.5 Z M 13.75 2.5 L 13.75 17.5 L 12.5 17.5 L 12.5 2.5 Z ");
            }
            else
            {
                ((Path)PlayPauseButton.Content).Data = Geometry.Parse("F1 M 17.5 10 L 5 18.75 L 5 1.25 Z M 6.25 16.347656 L 15.322266 10 L 6.25 3.652344 Z ");
            }

            //base.on(EventArgs.Empty);

            Loaded += (_, __) =>
            {
                _optionsDispatcher.BeginInvoke(() =>
                {
                    var _hwnd = new WindowInteropHelper(this).Handle;
                    //LibApply.Apply_Backdrop_Effect(HWnd: _hwnd, BackdropFlag: LibImport.DwmSystemBackdropTypeFlgs.DWMSBT_TRANSIENTWINDOW);
                    //LibApply.Apply_Light_Theme(HWnd: _hwnd, Dark: false);
                    var alpha = bgColor.A;
                    var bgr = (uint)(bgColor.B | (bgColor.G << 8) | (bgColor.R << 16));
                    LibApply.Apply_Custom_Acrylic(_hwnd, alpha: alpha, bgr: bgr);
                });
            };

            ResetMenu();

            Visualizer.ShowBg = !PreviewInput.IsChecked.Value;
            Visualizer.ChangeBg = true;

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
            LineThicknessInput.Text = Visualizer.InstanceOptions._lineThickness.ToString();
            BassDampenInput.Text = Visualizer.InstanceOptions._bassDampening.ToString();
            AudioChannelInput.SelectedIndex = (int)Visualizer.InstanceOptions._channelMode;
            var curMode = Visualizer.InstanceOptions._visualizationMode.ToString();
            if (AudioChannelInput.Text == "Stereo")
            {
                if (curMode != "Center" && curMode != "Oscilloscope" && curMode != "InnerCircle" && curMode != "OuterCircle")
                {
                    PosInput.Text = "Center";
                }
                else
                {
                    PosInput.Text = curMode;
                }
            }
            else
            {
                if (curMode == "Oscilloscope")
                {
                    PosInput.Text = "Bottom";
                }
                else
                {
                    PosInput.Text = curMode;
                }
            }

            if(Visualizer.InstanceOptions._showPeaks && !Visualizer.InstanceOptions._showOnlyPeaks)
            {
                PeaksModeInput.Text = "Peak Bars";
            }
            else if (Visualizer.InstanceOptions._showPeaksLine && !Visualizer.InstanceOptions._showOnlyPeaks)
            {
                PeaksModeInput.Text = "Peak Line";
            }
            else if (Visualizer.InstanceOptions._showPeaks && Visualizer.InstanceOptions._showOnlyPeaks)
            {
                PeaksModeInput.Text = "Peak Bars Only";
            }
            else if (Visualizer.InstanceOptions._showPeaksLine && Visualizer.InstanceOptions._showOnlyPeaks)
            {
                PeaksModeInput.Text = "Peak Line Only";
            }
            else if (!Visualizer.InstanceOptions._showBars && !Visualizer.InstanceOptions._showPeaksLine)
            {
                PeaksModeInput.Text = "Off";
            }

            BarColorOne.SelectedColor = Visualizer.InstanceOptions._barColor1;
            BarColorTwo.SelectedColor = Visualizer.InstanceOptions._barColor2;
            PeakColor.SelectedColor = Visualizer.InstanceOptions._peakColor;
            PeakColorTwo.SelectedColor = Visualizer.InstanceOptions._peakColor2;
            TrayIconColor.SelectedColor = MainWindow._TrayIconColor;
            BarColorTypeInput.SelectedIndex = (int)Visualizer.InstanceOptions._barColorType;
            PeakColorTypeInput.SelectedIndex = Visualizer.InstanceOptions._peakColorType == ColorMode.Match ? 0 : ((int)Visualizer.InstanceOptions._peakColorType + 1);

            if (Visualizer.InstanceOptions._barColorType == ColorMode.GradientVertical || Visualizer.InstanceOptions._barColorType == ColorMode.GradientHorizontal
                || Visualizer.InstanceOptions._barColorType == ColorMode.GradientHeight || Visualizer.InstanceOptions._barColorType == ColorMode.GradientPitch || Visualizer.InstanceOptions._barColorType == ColorMode.GradientFrequency)
            {
                BarColorOne.Visibility = Visibility.Collapsed;
                BarColorTwo.Visibility = Visibility.Collapsed;
                ColorOneLabel.Visibility = Visibility.Collapsed;
                ColorTwoLabel.Visibility = Visibility.Collapsed;
                SwapButton.Visibility = Visibility.Collapsed;

                GradientEditButton.Visibility = Visibility.Visible;
            }
            else
            {
                BarColorOne.Visibility = Visibility.Visible;
                BarColorTwo.Visibility = Visibility.Visible;
                ColorOneLabel.Visibility = Visibility.Visible;
                ColorTwoLabel.Visibility = Visibility.Visible;
                SwapButton.Visibility = Visibility.Visible;

                GradientEditButton.Visibility = Visibility.Collapsed;
            }
            if (Visualizer.InstanceOptions._peakColorType == ColorMode.GradientVertical || Visualizer.InstanceOptions._peakColorType == ColorMode.GradientHorizontal
                || Visualizer.InstanceOptions._peakColorType == ColorMode.GradientHeight || Visualizer.InstanceOptions._peakColorType == ColorMode.GradientPitch || Visualizer.InstanceOptions._peakColorType == ColorMode.GradientFrequency)
            {
                PeakColor.Visibility = Visibility.Collapsed;
                PeakColorTwo.Visibility = Visibility.Collapsed;
                ColorThreeLabel.Visibility = Visibility.Collapsed;
                ColorFourLabel.Visibility = Visibility.Collapsed;
                PeakSwapButton.Visibility = Visibility.Collapsed;

                PeakGradientEditButton.Visibility = Visibility.Visible;
            }
            else if (Visualizer.InstanceOptions._peakColorType == ColorMode.Match)
            {
                PeakColor.Visibility = Visibility.Collapsed;
                PeakColorTwo.Visibility = Visibility.Collapsed;
                ColorThreeLabel.Visibility = Visibility.Collapsed;
                ColorFourLabel.Visibility = Visibility.Collapsed;
                PeakSwapButton.Visibility = Visibility.Collapsed;

                PeakGradientEditButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                PeakColor.Visibility = Visibility.Visible;
                PeakColorTwo.Visibility = Visibility.Visible;
                ColorThreeLabel.Visibility = Visibility.Visible;
                ColorFourLabel.Visibility = Visibility.Visible;
                PeakSwapButton.Visibility = Visibility.Visible;

                PeakGradientEditButton.Visibility = Visibility.Collapsed;
            }

            RotationInput.Text = Visualizer.InstanceOptions._rotation.ToString();
            PeakDecay.Text = Visualizer.InstanceOptions._peakDecay.ToString();
            PeakHold.Text = Visualizer.InstanceOptions._peakHold.ToString();
            ColorMoveSpeedInput.Text = Visualizer.InstanceOptions._ColorMoveSpeed.ToString();
            ColorChangeFreqInput.Text = Visualizer.InstanceOptions._ColorChangeFreqency.ToString();
            InvertSpectrum.IsChecked = Visualizer.InstanceOptions._invertSpectrum;
            ShowLinesInput.IsChecked = Visualizer.InstanceOptions._showLines;

            ScaleInput.Text = Visualizer.InstanceOptions._bassScale.ToString();
            ShakeInput.Text = Visualizer.InstanceOptions._bassShake.ToString();

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

            Visualizer.MainWin.Dispatcher.BeginInvoke(() =>
            {
                Visualizer.MainWin.OscView.Visibility = (Visualizer.InstanceOptions._visualizationMode == VisualizationMode.Oscilloscope) ? Visibility.Visible : Visibility.Collapsed;
                Visualizer.MainWin.VisCanvas.Visibility = (Visualizer.InstanceOptions._visualizationMode == VisualizationMode.Oscilloscope) ? Visibility.Collapsed : Visibility.Visible;
            });
              
        }
        private void SetValues()
        {
            // Set values (most of them anyway)
            if (!AllowValueSet) return;
            AllowValueSet = false;

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

            if (double.TryParse(ScaleInput.Text, out double bassScale))
            {
                Visualizer.InstanceOptions._bassScale = bassScale;
            }

            if (double.TryParse(ShakeInput.Text, out double bassShake))
            {
                Visualizer.InstanceOptions._bassShake = bassShake;
            }

            if (float.TryParse(LineThicknessInput.Text, out float lineThick))
            {
                Visualizer.InstanceOptions._lineThickness = lineThick;
            }

            if (float.TryParse(BassDampenInput.Text, out float bassDamp))
            {
                Visualizer.InstanceOptions._bassDampening = bassDamp;
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

            if(PosInput.SelectedItem != null)
            {
                string selectedMode = (string)((ComboBoxItem)(PosInput.SelectedItem)).Content;
                if (Enum.TryParse(typeof(VisualizationMode), selectedMode, out object result))
                {
                    Visualizer.InstanceOptions._visualizationMode = (VisualizationMode)result;

                    if(Visualizer.InstanceOptions._visualizationMode == VisualizationMode.Top || Visualizer.InstanceOptions._visualizationMode == VisualizationMode.Bottom)
                    {
                        Visualizer.InstanceOptions._channelMode = Visualizer.InstanceOptions._channelMode == ChannelMode.Stereo ? ChannelMode.Mono : Visualizer.InstanceOptions._channelMode;
                    }

                    Visualizer.MainWin.Dispatcher.BeginInvoke(() =>
                    {
                        Visualizer.MainWin.OscView.Visibility = (Visualizer.InstanceOptions._visualizationMode == VisualizationMode.Oscilloscope) ? Visibility.Visible : Visibility.Collapsed;
                        Visualizer.MainWin.VisCanvas.Visibility = (Visualizer.InstanceOptions._visualizationMode == VisualizationMode.Oscilloscope) ? Visibility.Collapsed : Visibility.Visible;
                    });
                    
                }
            }
            if (Visualizer.InstanceOptions._visualizationMode == VisualizationMode.Oscilloscope)
            {
                AudioChannelInput.SelectedIndex = 3;
            }

            string peakMode = (string)((ComboBoxItem)PeaksModeInput.SelectedItem).Content;

            if (peakMode == "Peak Bars")
            {
                Visualizer.InstanceOptions._showPeaks = true;
                Visualizer.InstanceOptions._showBars = Visualizer.InstanceOptions._showLines ? false : true;
                Visualizer.InstanceOptions._showPeaksLine = false;
                Visualizer.InstanceOptions._showOnlyPeaks = false;
            }
            else if (peakMode == "Peak Line")
            {
                Visualizer.InstanceOptions._showPeaks = false;
                Visualizer.InstanceOptions._showBars = Visualizer.InstanceOptions._showLines ? false : true; ;
                Visualizer.InstanceOptions._showPeaksLine = true;
                Visualizer.InstanceOptions._showOnlyPeaks = false;
            }
            else if (peakMode == "Peak Bars Only")
            {
                Visualizer.InstanceOptions._showPeaks = true;
                Visualizer.InstanceOptions._showBars = false;
                Visualizer.InstanceOptions._showPeaksLine = false;
                Visualizer.InstanceOptions._showOnlyPeaks = true;
            }
            else if (peakMode == "Peak Line Only")
            {
                Visualizer.InstanceOptions._showPeaks = false;
                Visualizer.InstanceOptions._showBars = false;
                Visualizer.InstanceOptions._showPeaksLine = true;
                Visualizer.InstanceOptions._showOnlyPeaks = true;
            }
            else if (peakMode == "Off")
            {
                Visualizer.InstanceOptions._showPeaks = false;
                Visualizer.InstanceOptions._showBars = Visualizer.InstanceOptions._showLines ? false : true;
                Visualizer.InstanceOptions._showPeaksLine = false;
                Visualizer.InstanceOptions._showOnlyPeaks = false;
            }

            var boclr = Color.FromArgb((byte)BarColorOne.Color.A, (byte)BarColorOne.Color.RGB_R, (byte)BarColorOne.Color.RGB_G, (byte)BarColorOne.Color.RGB_B);
            var btclr = Color.FromArgb((byte)BarColorTwo.Color.A, (byte)BarColorTwo.Color.RGB_R, (byte)BarColorTwo.Color.RGB_G, (byte)BarColorTwo.Color.RGB_B);
            var pclr = Color.FromArgb((byte)PeakColor.Color.A, (byte)PeakColor.Color.RGB_R, (byte)PeakColor.Color.RGB_G, (byte)PeakColor.Color.RGB_B);
            Visualizer.InstanceOptions._barColor1 = boclr;
            Visualizer.InstanceOptions._barColor2 = btclr;
            Visualizer.InstanceOptions._peakColor = pclr;

            Visualizer.UpdateSettings = true;
            Visualizer.UpdatePeaks = true;
            MainWindow.displayFpsMeter.ResetFpsCounters();
            Visualizer.fpsMeter.ResetFpsCounters();
            AllowValueSet = true;
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
            //Visualizer._captureCTS.Cancel();
            _optionsDispatcher.BeginInvoke(() =>
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
                //Visualizer._captureCTS = new CancellationTokenSource();
                //_ = Task.Run(() => Visualizer.StartCapture(Visualizer._captureCTS.Token), Visualizer._captureCTS.Token);
            });
        }

        private void SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SetValues();
        }

        private void BarColorTypeInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!AllowValueSet) return;
            int idx = BarColorTypeInput.SelectedIndex;
            int idx2 = PeakColorTypeInput.SelectedIndex - 1;
            Visualizer.InstanceOptions._barColorType = (ColorMode)idx;
            Visualizer.InstanceOptions._peakColorType = (ColorMode)idx2;

            if(((ComboBoxItem)PeakColorTypeInput.SelectedItem).Content as string == "Match Bars")
            {
                Visualizer.InstanceOptions._peakColorType = ColorMode.Match;
            }

            if (Visualizer.InstanceOptions._barColorType == ColorMode.GradientVertical || Visualizer.InstanceOptions._barColorType == ColorMode.GradientHorizontal
                || Visualizer.InstanceOptions._barColorType == ColorMode.GradientHeight || Visualizer.InstanceOptions._barColorType == ColorMode.GradientPitch || Visualizer.InstanceOptions._barColorType == ColorMode.GradientFrequency)
            {
                BarColorOne.Visibility = Visibility.Collapsed;
                BarColorTwo.Visibility = Visibility.Collapsed;
                ColorOneLabel.Visibility = Visibility.Collapsed;
                ColorTwoLabel.Visibility = Visibility.Collapsed;
                SwapButton.Visibility = Visibility.Collapsed;

                GradientEditButton.Visibility = Visibility.Visible;
            }
            else
            {
                BarColorOne.Visibility = Visibility.Visible;
                BarColorTwo.Visibility = Visibility.Visible;
                ColorOneLabel.Visibility = Visibility.Visible;
                ColorTwoLabel.Visibility = Visibility.Visible;
                SwapButton.Visibility = Visibility.Visible;

                GradientEditButton.Visibility = Visibility.Collapsed;
            }
            if (Visualizer.InstanceOptions._peakColorType == ColorMode.GradientVertical || Visualizer.InstanceOptions._peakColorType == ColorMode.GradientHorizontal
                || Visualizer.InstanceOptions._peakColorType == ColorMode.GradientHeight || Visualizer.InstanceOptions._peakColorType == ColorMode.GradientPitch || Visualizer.InstanceOptions._peakColorType == ColorMode.GradientFrequency)
            {
                PeakColor.Visibility = Visibility.Collapsed;
                PeakColorTwo.Visibility = Visibility.Collapsed;
                ColorThreeLabel.Visibility = Visibility.Collapsed;
                ColorFourLabel.Visibility = Visibility.Collapsed;
                PeakSwapButton.Visibility = Visibility.Collapsed;

                PeakGradientEditButton.Visibility = Visibility.Visible;
            }
            else if (Visualizer.InstanceOptions._peakColorType == ColorMode.Match)
            {
                PeakColor.Visibility = Visibility.Collapsed;
                PeakColorTwo.Visibility = Visibility.Collapsed;
                ColorThreeLabel.Visibility = Visibility.Collapsed;
                ColorFourLabel.Visibility = Visibility.Collapsed;
                PeakSwapButton.Visibility = Visibility.Collapsed;

                PeakGradientEditButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                PeakColor.Visibility = Visibility.Visible;
                PeakColorTwo.Visibility = Visibility.Visible;
                ColorThreeLabel.Visibility = Visibility.Visible;
                ColorFourLabel.Visibility = Visibility.Visible;
                PeakSwapButton.Visibility = Visibility.Visible;

                PeakGradientEditButton.Visibility = Visibility.Collapsed;
            }

            Visualizer.UpdateSettings = true;
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
            if (!AllowValueSet) return;
            if (LeftMovement.IsChecked.Value)
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
            if (!AllowValueSet) return;
            string selectedItem = (string)((ComboBoxItem)SpectrogramInput.SelectedItem).Content;

            if (Enum.TryParse(typeof(ScaleMode), selectedItem, out object result))
            {
                Visualizer.InstanceOptions._scaleMode = (ScaleMode)result;
            }
        }

        private void AudioDevicesButton_Click(object sender, RoutedEventArgs e)
        {
            Visualizer.MainWin.CreateNewAudioWindow();
        }

        private void InvertSpectrum_Click(object sender, RoutedEventArgs e)
        {
            if (!AllowValueSet) return;
            Visualizer.InstanceOptions._invertSpectrum = InvertSpectrum.IsChecked.Value;
        }

        private void PreviewInput_Click(object sender, RoutedEventArgs e)
        {
            if (!AllowValueSet) return;
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
            if(MainWindow.FVZWindowHandle != null)
            {
                MainWindow.FVZWindowHandle.SetAccentColor();
            }
            Visualizer.UpdateSettings = true;
            MainWindow.HueShiftIcon();
        }

        private void FVZButton_Click(object sender, RoutedEventArgs e)
        {
            Visualizer.MainWin.CreateNewFVZWindow();
        }

        private void OnTopInput_Click(object sender, RoutedEventArgs e)
        {
            if (!AllowValueSet) return;
            var check = OnTopInput.IsChecked.Value;
            Visualizer.MainWin.Dispatcher.BeginInvoke(() =>
            {
                Visualizer.MainWin.Topmost = check;
            });
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

        private void AudioChannelInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!AllowValueSet) return;
            Visualizer.InstanceOptions._channelMode = (ChannelMode)AudioChannelInput.SelectedIndex;
            var curMode = PosInput.Text;
            if (Visualizer.InstanceOptions._channelMode == ChannelMode.Stereo)
            {
                if (curMode != "Center" && curMode != "Oscilloscope" && curMode != "InnerCircle" && curMode != "OuterCircle")
                {
                    PosInput.Text = "Center";
                }
                else
                {
                    PosInput.Text = curMode;
                }
            }
            else
            {
                if (curMode == "Oscilloscope")
                {
                    PosInput.Text = "Bottom";
                }
                else
                {
                    PosInput.Text = curMode;
                }
            }
            Visualizer.UpdateSettings = true;
        }

        private void CenterlineInput_Click(object sender, RoutedEventArgs e)
        {
            var checkd = CenterlineInput.IsChecked.Value;
            Visualizer.MainWin.Dispatcher.BeginInvoke(() =>
            {
                Visualizer.MainWin.CenterLine.Visibility = checkd ? Visibility.Visible : Visibility.Collapsed;

            });
        }

        private void GradientEditButton_Click(object sender, RoutedEventArgs e)
        {
            Visualizer.MainWin.CreateNewGradientEditorWindow(false);
        }

        private void PeakGradientEditButton_Click(object sender, RoutedEventArgs e)
        {
            Visualizer.MainWin.CreateNewGradientEditorWindow(true);
        }

        private void Grid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double width = HeaderCutoutGrid.ActualWidth;
            double height = HeaderCutoutGrid.ActualHeight;
            double radius = 10;

            var outer = new RectangleGeometry(new Rect(0, 0, width, height));
            var inner = new RectangleGeometry(new Rect(10, 2, width - 20, 35), radius, radius);

            var combined = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
            HeaderCutoutGrid.Clip = combined;
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
            _optionsDispatcher.BeginInvoke(() =>
            {
                Point current = PointToScreen(e.GetPosition(this));
                Vector delta = current - _dragStart;

                // Which edge are we dragging?
                FrameworkElement fe = (FrameworkElement)Mouse.Captured;
                if(fe == null)
                {
                    return;
                }

                bool left = fe.Name.Contains("Left");
                bool right = fe.Name.Contains("Right");
                bool top = fe.Name.Contains("Top");
                bool bottom = fe.Name.Contains("Bottom");

                Rect r = _startRect;

                if (left) 
                { 
                    r.X += delta.X; 
                    r.Width = r.Width - delta.X >= MinWidth ? r.Width - delta.X : MinWidth; 
                }
                if (right) 
                { 
                    r.Width = r.Width + delta.X >= 0 ? r.Width + delta.X : 0;
                }
                if (top) 
                { 
                    r.Y += delta.Y;
                    r.Height = r.Height - delta.Y >= MinHeight ? r.Height - delta.Y : MinHeight;
                }
                if (bottom) 
                { 
                    r.Height = r.Height + delta.Y >= 0 ? r.Height + delta.Y : 0; 
                }

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

        private void NewInstance_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("FreqFreak.exe");
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (Visualizer._captureCTS.IsCancellationRequested)
            {
                Visualizer._captureCTS = new CancellationTokenSource();
                _ = Task.Run(() => Visualizer.StartCapture(Visualizer._captureCTS.Token), Visualizer._captureCTS.Token);
                Visualizer.MainWin.toggleVis.Text = "Pause Visualizer";
                ((Path)PlayPauseButton.Content).Data = Geometry.Parse("F1 M 6.25 2.5 L 7.5 2.5 L 7.5 17.5 L 6.25 17.5 Z M 13.75 2.5 L 13.75 17.5 L 12.5 17.5 L 12.5 2.5 Z ");
            }
            else
            {
                Visualizer._captureCTS.Cancel();
                Visualizer.MainWin.toggleVis.Text = "Resume Visualizer";
                ((Path)PlayPauseButton.Content).Data = Geometry.Parse("F1 M 17.5 10 L 5 18.75 L 5 1.25 Z M 6.25 16.347656 L 15.322266 10 L 6.25 3.652344 Z ");
            }
        }

        private void RecenterButton_Click(object sender, RoutedEventArgs e)
        {
            var midH = this.Top + (this.ActualHeight / 2);

            var midW = this.Left + (this.ActualWidth / 2);

            Visualizer.MainWin.Dispatcher.Invoke(() =>
            {
                var newMidH = midH - (Visualizer.MainWin.Height / 2);
                var newMidW = midW - (Visualizer.MainWin.Width / 2);
                Visualizer.MainWin.Left = newMidW;
                Visualizer.MainWin.Top = newMidH;
            });
        }

        private void PhotoButton_Click(object sender, RoutedEventArgs e)
        {
            Visualizer.MainWin.CreateNewPhotoCutoutWindow();
        }

        private void ShowLinesInput_Click(object sender, RoutedEventArgs e)
        {
            if (!AllowValueSet) return;
            Visualizer.InstanceOptions._showLines = ShowLinesInput.IsChecked.Value;
            Visualizer.InstanceOptions._showBars = !ShowLinesInput.IsChecked.Value;
        }
    }
}
