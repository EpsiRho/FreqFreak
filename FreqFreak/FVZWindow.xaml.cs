using FFTVIS;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace FreqFreak
{
    public partial class FVZWindow : Window
    {
        public static string CurrentSongPath = "";
        public static string CurrentFvzPath = "";
        public static int Playing = 0;
        public static AudioEncoder FVZEncoder;
        public static FrequencyVisualizer DecodedFV;
        DateTime StartTime = DateTime.Now;
        bool CanSeek = false;
        public FVZWindow()
        {
            InitializeComponent();
            DataContext = MainWindow.FVZPlayer;
            this.Closed += (s, e) =>
            {
                MainWindow.FVZWindowHandle.Hide();
                FVZPlayer.Stop();
                FVZEncoder = null;
                DecodedFV = null;
                MainWindow.FVZMode = false;
            };
            SetAccentColor();
            this.Loaded += FVZWindow_Loaded;
        }

        private void FVZWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SetAccentColor();
        }

        public void SetAccentColor()
        {
            var brush = new SolidColorBrush(MainWindow._TrayIconColor);
            FVZProgress.Foreground = brush;
            var track = (Track)PlaybackSlider.Template.FindName("PART_Track", PlaybackSlider);
            var track2 = (Track)PlaybackSlider.Template.FindName("PART_Track", VolumeSlider);


            if (track != null)
            {
                if (track.DecreaseRepeatButton != null)
                    track.DecreaseRepeatButton.Background = brush;
                track.DecreaseRepeatButton.Height = 5;

                if (track.IncreaseRepeatButton != null)
                    track.IncreaseRepeatButton.Background = new SolidColorBrush(Color.FromArgb(255, (byte)Math.Clamp(MainWindow._TrayIconColor.R - 55, 0, 255), (byte)Math.Clamp(MainWindow._TrayIconColor.G - 55, 0, 255), (byte)Math.Clamp(MainWindow._TrayIconColor.B - 55, 0, 255)));
                track.IncreaseRepeatButton.Height = 5;
            }

            if (track2 != null)
            {
                if (track2.DecreaseRepeatButton != null)
                    track2.DecreaseRepeatButton.Background = brush;
                track2.DecreaseRepeatButton.Height = 5;

                if (track2.IncreaseRepeatButton != null)
                    track2.IncreaseRepeatButton.Background = new SolidColorBrush(Color.FromArgb(255, (byte)Math.Clamp(MainWindow._TrayIconColor.R - 55, 0, 255), (byte)Math.Clamp(MainWindow._TrayIconColor.G - 55, 0, 255), (byte)Math.Clamp(MainWindow._TrayIconColor.B - 55, 0, 255)));
                track2.IncreaseRepeatButton.Height = 5;
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (Playing == 1)
            {
                FVZPlayer.Pause();
                Playing = 2;
                PlayButton.Content = "Play";
                return;
            }
            else if(Playing == 2)
            {
                FVZPlayer.Resume();
                Playing = 1;
                PlayButton.Content = "Pause";
                return;
            }

            if (!System.IO.Path.Exists(CurrentSongPath))
            {
                return;
            }
            if (DecodedFV == null)
            {
                return;
            }

            FVZPlayer.fvzPath = CurrentFvzPath;
            FVZPlayer.audioPath = CurrentSongPath;
            FVZPlayer.Start(CurrentSongPath, DecodedFV);
            Playing = 1;
            PlayButton.Content = "Pause";
        }

        private void LoadSongButton_Click(object sender, RoutedEventArgs e)
        {
            if(DecodedFV != null)
            {
                MessageBoxResult result = MessageBox.Show("If you load a new song the current FVZ Generation will be wiped, is that okay?", "Warning", MessageBoxButton.OKCancel);

                if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
            }
            PlayButton.IsEnabled = false;
            ExportFVZButton.IsEnabled = false;
            DecodedFV = null;
            CurrentSongPath = "";
            CurrentFvzPath = "";
            AudioFileText.Text = "Audio File: Loading";
            FVZFileText.Text = "FVZ File: None";
            PlayButton.Content = "Play";
            FVZPlayer.Stop();

            Playing = 0;
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Audio Files|*.mp3;*.wav;*.flac;*.ogg;*.aac|All Files|*.*",
                Title = "Select an Audio File"
            };

            var check = openFileDialog.ShowDialog();
            if (check == true)
            {
                CurrentSongPath = openFileDialog.FileName;
            }
            else
            {
                AudioFileText.Text = "Audio File: None";
                return;
            }

            AudioFileText.Text = $"Audio File: {System.IO.Path.GetFileName(CurrentSongPath)}";
            GenerateFVZButton.IsEnabled = true;
            if (CurrentSongPath != "" && CurrentFvzPath != "") 
            {
                PlayButton.IsEnabled = true;
            }
            else
            {
                PlayButton.IsEnabled = false;
            }
        }

        private void LoadFVZButton_Click(object sender, RoutedEventArgs e)
        {
            if (DecodedFV != null)
            {
                MessageBoxResult result = MessageBox.Show("The current FVZ Generation will be wiped, is that okay?", "Warning", MessageBoxButton.OKCancel);

                if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
            }

            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "FFT Visualizer|*.FVZ;*.fvz|All Files|*.*",
                Title = "Select an FVZ File"
            };

            CurrentFvzPath = "";
            FVZFileText.Text = "FVZ File: Loading";
            ExportFVZButton.IsEnabled = false;

            var check = openFileDialog.ShowDialog();
            if (check == true)
            {
                CurrentFvzPath = openFileDialog.FileName;
            }
            else
            {
                FVZFileText.Text = "FVZ File: None";
                return;
            }

            StartTime = DateTime.Now;
            FVZProgress.Visibility = Visibility.Visible;
            FVZFileText.Text = $"FVZ File: Decoding File...";
            Thread t = new Thread(() =>
            {
                try
                {
                    DecodedFV = AudioDecoder.ReadFile(CurrentFvzPath);
                }
                catch (Exception)
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show("Failed to load FVZ file. Make sure it is a valid FVZ file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                    CurrentFvzPath = "";
                    return;
                }

                Dispatcher.Invoke(() =>
                {
                    FVZFileText.Text = $"FVZ File: Decoded {System.IO.Path.GetFileName(CurrentFvzPath)} in {DateTime.Now - StartTime}";
                    if (CurrentSongPath != "" && CurrentFvzPath != "")
                    {
                        PlayButton.IsEnabled = true;
                    }
                    else
                    {
                        PlayButton.IsEnabled = false;
                    }
                    FVZProgress.Visibility = Visibility.Hidden;
                });
            });
            t.Start();
        }

        private void GenerateFVZButton_Click(object sender, RoutedEventArgs e)
        {
            if (DecodedFV != null)
            {
                MessageBoxResult result = MessageBox.Show("The current FVZ Generation will be wiped, is that okay?", "Warning", MessageBoxButton.OKCancel);

                if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
            }

            if (CurrentSongPath == "")
            {
                return;
            }
            bool check = int.TryParse(FPSInput.Text, out int fps);
            if (!check)
            {
                return;
            }

            CompressionType ct = CompressionType.Uncompressed;
            if (DeltaCheck.IsChecked.Value)
            {
                ct |= CompressionType.DeltaEncode;
            }

            if (ZstdCheck.IsChecked.Value)
            {
                ct |= CompressionType.Zstd;
            }

            if (!NoQuantCheck.IsChecked.Value)
            {
                ct |= CompressionType.Quantize;
            }

            DecodedFV = null;
            FVZEncoder = null;
            FVZEncoder = new AudioEncoder(Visualizer.InstanceOptions._bars,
                Visualizer.InstanceOptions._dbFloor, Visualizer.InstanceOptions._dbRange,
                Visualizer.InstanceOptions._fMin, Visualizer.InstanceOptions._fMax,
                Visualizer.InstanceOptions._smooth, (SpectrogramMapping)Visualizer.InstanceOptions._scaleMode,
                Visualizer.InstanceOptions._fftSize, fps, ct, Quant16Check.IsChecked.Value ? QuantizeLevel.Q16 : QuantizeLevel.Q8);

            FVZProgress.Visibility = Visibility.Visible;

            FVZFileText.Text = $"FVZ File: Loading Audio...";
            StartTime = DateTime.Now;

            Thread t = new Thread(GenerateFrames);
            t.Start();
        }
        
        private void GenerateFrames()
        {
            var chk = FVZEncoder.LoadAudio(CurrentSongPath);
            if (!chk)
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                FVZFileText.Text = $"FVZ File: Generating Frames...";
            });
            chk = FVZEncoder.GenerateFrames();
            if (!chk)
            {
                return;
            }

            DecodedFV = FVZEncoder.GetGeneratedFrames();
            if (DecodedFV == null)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("Failed to generate frames from the audio file.");
                });
                return;
            }

            Dispatcher.Invoke(() =>
            {
                FVZFileText.Text = $"FVZ File: Generated in {DateTime.Now - StartTime}";
                PlayButton.IsEnabled = true;
                ExportFVZButton.IsEnabled = true;
                FVZProgress.Visibility = Visibility.Hidden;
            });
            FVZPlayer.SetFV(DecodedFV);
        }

        private void ExportFVZButton_Click(object sender, RoutedEventArgs e)
        {
            //FVZPlayer.Fuck();
            //return;
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = "FFT Visualizer|*.fvz";
            dialog.Title = "Save FVZ File";
            string filename = System.IO.Path.GetFileNameWithoutExtension(CurrentSongPath);
            dialog.FileName = $"{filename}.fvz";

            var check = dialog.ShowDialog();
            if (check == true)
            {
                FVZProgress.Visibility = Visibility.Visible;
                CurrentFvzPath = dialog.FileName;
                FVZEncoder.SaveToFile(CurrentFvzPath);
                FVZProgress.Visibility = Visibility.Hidden;

                MessageBoxResult result = MessageBox.Show("File saved, open in File Explorer?", "Export Complete", MessageBoxButton.OKCancel);

                if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
                else
                {
                    try
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{CurrentFvzPath}\"");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to open File Explorer: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }

        }

        private void FPSInput_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void Check_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if(FVZEncoder == null)
                {
                    return;
                }
                CompressionType ct = CompressionType.Uncompressed;
                if (DeltaCheck.IsChecked.Value)
                {
                    ct |= CompressionType.DeltaEncode;
                }

                if (ZstdCheck.IsChecked.Value)
                {
                    ct |= CompressionType.Zstd;
                }

                if (!NoQuantCheck.IsChecked.Value)
                {
                    ct |= CompressionType.Quantize;
                }
                FVZEncoder.Compression = ct;
                FVZEncoder.QuantizeLevel = Quant16Check.IsChecked.Value ? QuantizeLevel.Q16 : QuantizeLevel.Q8;
            }
            catch (Exception)
            {

            }
        }

        
        private void PlaybackSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if(CanSeek)
            {
                //PlaybackTime.Text = $"{TimeSpan.FromMilliseconds(PlaybackSlider.Value).ToString("hh\\:mm\\:ss\\.ff")}";
                FVZPlayer.SeekPaused(PlaybackSlider.Value);
            }
        }

        private void PlaybackSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            MainWindow.FVZPlayer.isSeeking = false;
            FVZPlayer.Seek(PlaybackSlider.Value, DecodedFV);
            CanSeek = false;
            Playing = 1;
            PlayButton.Content = "Pause";
            //BindingOperations.SetBinding(PlaybackSlider, Slider.ValueProperty, new Binding("PlaybackMs"));
            //BindingOperations.SetBinding(PlaybackTime, TextBlock.TextProperty, new Binding("PlaybackTime"));
        }

        private void PlaybackSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            //BindingOperations.ClearBinding(PlaybackSlider, Slider.ValueProperty);
            //BindingOperations.ClearBinding(PlaybackTime, TextBlock.TextProperty);
            CanSeek = true;
            MainWindow.FVZPlayer.isSeeking = true;
        }

        private void AudioDelayInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                FVZPlayer.AudioDelayMs = int.Parse(AudioDelayInput.Text);
            }
            catch (Exception)
            {
                FVZPlayer.AudioDelayMs = 0; // Reset to 0 if parsing fails
            }
        }
    }
}
