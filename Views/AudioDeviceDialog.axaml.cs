using Avalonia.Controls;
using Avalonia.Interactivity;
using NetKeyer.ViewModels;

namespace NetKeyer.Views
{
    public partial class AudioDeviceDialog : Window
    {
        private AudioDeviceDialogViewModel _viewModel;

        public bool DeviceChanged { get; private set; }
        public string SelectedDeviceId { get; private set; }
        public bool AggressiveLowLatency { get; private set; }
        public bool KeepAudioDeviceAwake { get; private set; }

        public AudioDeviceDialog()
        {
            InitializeComponent();
            _viewModel = new AudioDeviceDialogViewModel();
            DataContext = _viewModel;
        }

        public void SetCurrentDevice(string deviceId)
        {
            _viewModel.SetSelectedDeviceId(deviceId);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedDeviceId = _viewModel.GetSelectedDeviceId();
            AggressiveLowLatency = _viewModel.GetAggressiveLowLatency();
            KeepAudioDeviceAwake = _viewModel.GetKeepAudioDeviceAwake();
            DeviceChanged = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DeviceChanged = false;
            Close();
        }
    }
}
