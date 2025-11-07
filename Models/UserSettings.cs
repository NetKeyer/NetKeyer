using System;
using System.IO;
using System.Text.Json;

namespace NetKeyer.Models
{
    public class UserSettings
    {
        public string SelectedRadioSerial { get; set; }
        public string SelectedGuiClientStation { get; set; }
        public string SelectedSerialPort { get; set; }
        public string SelectedMidiDevice { get; set; }
        public string InputType { get; set; } = "Serial";
        public string HaliKeyVersion { get; set; } = "HaliKey v1";

        private static string SettingsFilePath
        {
            get
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var appFolder = Path.Combine(appDataPath, "NetKeyer");
                Directory.CreateDirectory(appFolder);
                return Path.Combine(appFolder, "settings.json");
            }
        }

        public static UserSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    return JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading settings: {ex.Message}");
            }

            return new UserSettings();
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving settings: {ex.Message}");
            }
        }
    }
}
