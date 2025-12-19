using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetKeyer.Audio;
using NetKeyer.Models;

namespace NetKeyer.ViewModels
{
    public partial class AudioDeviceDialogViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<AudioDeviceInfo> _audioDevices = new();

        [ObservableProperty]
        private AudioDeviceInfo _selectedAudioDevice;

        public AudioDeviceDialogViewModel()
        {
            RefreshAudioDevices();
        }

        public void SetSelectedDeviceId(string deviceId)
        {
            SelectedAudioDevice = AudioDevices.FirstOrDefault(d => d.DeviceId == deviceId)
                                  ?? AudioDevices.FirstOrDefault(d => string.IsNullOrEmpty(d.DeviceId));
        }

        public string GetSelectedDeviceId()
        {
            return SelectedAudioDevice?.DeviceId ?? "";
        }

        [RelayCommand]
        private void RefreshAudioDevices()
        {
            var previousSelection = SelectedAudioDevice?.DeviceId ?? "";

            AudioDevices.Clear();

            try
            {
                // Use platform-aware enumeration from factory
                var devices = SidetoneGeneratorFactory.EnumerateDevices();

                foreach (var (deviceId, name) in devices)
                {
                    AudioDevices.Add(new AudioDeviceInfo { DeviceId = deviceId, Name = name });
                }

                // Restore previous selection
                SetSelectedDeviceId(previousSelection);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error refreshing audio devices: {ex.Message}");

                // Ensure at least System Default is available
                if (AudioDevices.Count == 0)
                {
                    AudioDevices.Add(new AudioDeviceInfo { DeviceId = "", Name = "System Default" });
                }
                SelectedAudioDevice = AudioDevices[0];
            }
        }
    }
}
