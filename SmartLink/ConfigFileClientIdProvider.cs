using System;

namespace NetKeyer.SmartLink
{
    /// <summary>
    /// Default implementation that reads the client_id from user configuration.
    /// For development and open-source use.
    /// </summary>
    public class ConfigFileClientIdProvider : IClientIdProvider
    {
        private readonly string _clientId;

        public ConfigFileClientIdProvider()
        {
            // Load client_id from user settings
            var settings = Models.UserSettings.Load();
            _clientId = settings.SmartLinkClientId;
        }

        public string GetClientId()
        {
            // Return null if not configured
            if (string.IsNullOrWhiteSpace(_clientId))
            {
                return null;
            }

            return _clientId;
        }
    }
}
