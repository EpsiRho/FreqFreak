using System.Windows;
using System.Windows.Controls;

namespace FreqFreak
{
    public partial class AudioDevices : Window
    {
        public AudioDevices()
        {
            InitializeComponent();
            var output = Visualizer.GetOutputDevices();
            var input = Visualizer.GetInputDevices();
            var apps = Visualizer.GetAudioApps();

            foreach (var op in output)
            {
                OutputDevicesList.Items.Add(op);
            }

            foreach (var ip in input)
            {
                InputDevicesList.Items.Add(ip);
            }

            AudioAppsList.Items.Add("All");
            foreach (var app in apps)
            {
                AudioAppsList.Items.Add(app);
            }
            CurrentDeviceText.Text = Visualizer._audioDevice == null ? "Current: Default Device" : $"Current: {Visualizer._audioDevice.FriendlyName}";
        }

        private void OutputDevicesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OutputDevicesList.SelectedItem == null) return;
            Visualizer.SelectedApp = "";
            InputDevicesList.SelectedIndex = -1;
            AudioAppsList.SelectedIndex = -1;
            Visualizer.UpdateSettings = true;
            Visualizer.isInput = false;
            Visualizer.SelectDevice((string)OutputDevicesList.SelectedItem);
            Visualizer._captureCTS.Cancel();
            Visualizer._captureCTS = new();
            var _captureThread = new Thread(() =>
            {
                Visualizer.StartCapture(Visualizer._captureCTS.Token);
            });
            _captureThread.Start();
            OutputDevicesList.SelectedIndex = -1;
            CurrentDeviceText.Text = Visualizer._audioDevice == null ? "Error Setting Device" : $"Current: {Visualizer._audioDevice.FriendlyName}";
        }

        private void InputDevicesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (InputDevicesList.SelectedItem == null) return;
            Visualizer.SelectedApp = "";
            OutputDevicesList.SelectedIndex = -1;
            AudioAppsList.SelectedIndex = -1;
            Visualizer.UpdateSettings = true;
            Visualizer.isInput = true;
            Visualizer.SelectDevice((string)InputDevicesList.SelectedItem);
            Visualizer._captureCTS.Cancel();
            Visualizer._captureCTS = new();
            var _captureThread = new Thread(() =>
            {
                Visualizer.StartCapture(Visualizer._captureCTS.Token);
            });
            _captureThread.Start();
            InputDevicesList.SelectedIndex = -1;
            CurrentDeviceText.Text = Visualizer._audioDevice == null ? "Error Setting Device!" : $"Current: {Visualizer._audioDevice.FriendlyName}";
        }

        private void AudioAppsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AudioAppsList.SelectedItem == null) return;
            InputDevicesList.SelectedIndex = -1;
            OutputDevicesList.SelectedIndex = -1;
            Visualizer.UpdateSettings = true;
            Visualizer.isInput = false;
            var slct = (string)AudioAppsList.SelectedItem;
            if (slct == "All") Visualizer.SelectedApp = "";
            else Visualizer.SelectedApp = (string)AudioAppsList.SelectedItem;
            Visualizer._captureCTS.Cancel();

            Visualizer._captureCTS = new();
            var _captureThread = new Thread(() =>
            {
                Visualizer.StartCapture(Visualizer._captureCTS.Token);
            });
            _captureThread.Start();
            AudioAppsList.SelectedIndex = -1;
            CurrentDeviceText.Text = Visualizer.SelectedApp == null ? "Error Setting Device!" : $"Current: {Visualizer.SelectedApp}";
        }
    }
}
