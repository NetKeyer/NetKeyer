using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

        // Audio output device selection (empty string = use system default)
        public string SelectedAudioDeviceId { get; set; } = "";

        // MIDI note mappings
        public List<MidiNoteMapping> MidiNoteMappings { get; set; }

        // SmartLink settings
        public string SmartLinkClientId { get; set; }
        public bool RememberMeSmartLink { get; set; } = true;

        // Stored encrypted in the file (Base64)
        public string SmartLinkRefreshTokenEncrypted { get; set; }

        // Not serialized - only used in memory
        [System.Text.Json.Serialization.JsonIgnore]
        private string _smartLinkRefreshToken;

        [System.Text.Json.Serialization.JsonIgnore]
        public string SmartLinkRefreshToken
        {
            get => _smartLinkRefreshToken;
            set
            {
                _smartLinkRefreshToken = value;
                // Encrypt when setting
                if (!string.IsNullOrEmpty(value))
                {
                    SmartLinkRefreshTokenEncrypted = EncryptString(value);
                }
                else
                {
                    SmartLinkRefreshTokenEncrypted = null;
                }
            }
        }

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

        private static string EncryptString(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext))
                return null;

            try
            {
                byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

                // Use ProtectedData on Windows, AES with machine key on other platforms
                if (OperatingSystem.IsWindows())
                {
#pragma warning disable CA1416 // Validate platform compatibility
                    byte[] encryptedBytes = System.Security.Cryptography.ProtectedData.Protect(
                        plaintextBytes,
                        null, // No additional entropy
                        System.Security.Cryptography.DataProtectionScope.CurrentUser); // User-specific encryption
                    return Convert.ToBase64String(encryptedBytes);
#pragma warning restore CA1416
                }
                else
                {
                    // On non-Windows platforms, use AES encryption with machine-specific key
                    return EncryptWithAes(plaintextBytes);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to encrypt refresh token: {ex.Message}");
                return null;
            }
        }

        private static string DecryptString(string ciphertext)
        {
            if (string.IsNullOrEmpty(ciphertext))
                return null;

            try
            {
                // Use ProtectedData on Windows, AES with machine key on other platforms
                if (OperatingSystem.IsWindows())
                {
#pragma warning disable CA1416 // Validate platform compatibility
                    byte[] encryptedBytes = Convert.FromBase64String(ciphertext);
                    byte[] decryptedBytes = System.Security.Cryptography.ProtectedData.Unprotect(
                        encryptedBytes,
                        null, // No additional entropy
                        System.Security.Cryptography.DataProtectionScope.CurrentUser); // User-specific encryption
                    return Encoding.UTF8.GetString(decryptedBytes);
#pragma warning restore CA1416
                }
                else
                {
                    // On non-Windows platforms, use AES decryption with machine-specific key
                    return DecryptWithAes(ciphertext);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to decrypt refresh token: {ex.Message}");
                return null;
            }
        }

        // Derive a machine-specific key for AES encryption on non-Windows platforms
        private static byte[] GetMachineKey()
        {
            // Use machine name + user name as the basis for the key
            // This makes the key specific to this machine and user
            string keySource = $"{Environment.MachineName}_{Environment.UserName}_NetKeyer_Salt_v1";

            // Use SHA256 to derive a 256-bit key
            using (var sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(Encoding.UTF8.GetBytes(keySource));
            }
        }

        private static string EncryptWithAes(byte[] plaintextBytes)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = GetMachineKey();
                aes.GenerateIV(); // Generate random IV for each encryption

                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                using (var ms = new MemoryStream())
                {
                    // Write IV to the beginning of the output (needed for decryption)
                    ms.Write(aes.IV, 0, aes.IV.Length);

                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    {
                        cs.Write(plaintextBytes, 0, plaintextBytes.Length);
                        cs.FlushFinalBlock();
                    }

                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        private static string DecryptWithAes(string ciphertext)
        {
            byte[] ciphertextBytes = Convert.FromBase64String(ciphertext);

            using (var aes = Aes.Create())
            {
                aes.Key = GetMachineKey();

                // Extract IV from the beginning of the ciphertext
                byte[] iv = new byte[aes.IV.Length];
                Array.Copy(ciphertextBytes, 0, iv, 0, iv.Length);
                aes.IV = iv;

                using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                using (var ms = new MemoryStream(ciphertextBytes, iv.Length, ciphertextBytes.Length - iv.Length))
                using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                using (var resultStream = new MemoryStream())
                {
                    cs.CopyTo(resultStream);
                    return Encoding.UTF8.GetString(resultStream.ToArray());
                }
            }
        }

        public static UserSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();

                    // Decrypt the refresh token if present
                    if (!string.IsNullOrEmpty(settings.SmartLinkRefreshTokenEncrypted))
                    {
                        settings._smartLinkRefreshToken = DecryptString(settings.SmartLinkRefreshTokenEncrypted);
                    }

                    // Load default MIDI note mappings if not present
                    if (settings.MidiNoteMappings == null || settings.MidiNoteMappings.Count == 0)
                    {
                        settings.MidiNoteMappings = MidiNoteMapping.GetDefaultMappings();
                    }

                    return settings;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading settings: {ex.Message}");
            }

            var newSettings = new UserSettings();
            newSettings.MidiNoteMappings = MidiNoteMapping.GetDefaultMappings();
            return newSettings;
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
