using CommunityToolkit.Mvvm.ComponentModel;

namespace NetKeyer.ViewModels
{
    public partial class AccessTokenDialogViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _accessToken = "";

        [ObservableProperty]
        private bool _useLocalJwtMinting = false;

        [ObservableProperty]
        private string _jwtKeyId = "";

        [ObservableProperty]
        private string _jwtKeySecret = "";

        [ObservableProperty]
        private string _jwtIssuer = "";

        [ObservableProperty]
        private string _jwtAudience = "";

        [ObservableProperty]
        private int _jwtTokenLifetimeMinutes = 30;

        public void SetAccessToken(string value)
        {
            AccessToken = value ?? "";
        }

        public void SetUseLocalJwtMinting(bool value)
        {
            UseLocalJwtMinting = value;
        }

        public void SetJwtKeyId(string value)
        {
            JwtKeyId = value ?? "";
        }

        public void SetJwtKeySecret(string value)
        {
            JwtKeySecret = value ?? "";
        }

        public void SetJwtIssuer(string value)
        {
            JwtIssuer = value ?? "";
        }

        public void SetJwtAudience(string value)
        {
            JwtAudience = value ?? "";
        }

        public void SetJwtTokenLifetimeMinutes(int value)
        {
            JwtTokenLifetimeMinutes = value;
        }

        public string GetAccessToken()
        {
            return AccessToken?.Trim() ?? "";
        }

        public bool GetUseLocalJwtMinting()
        {
            return UseLocalJwtMinting;
        }

        public string GetJwtKeyId()
        {
            return JwtKeyId?.Trim() ?? "";
        }

        public string GetJwtKeySecret()
        {
            return JwtKeySecret?.Trim() ?? "";
        }

        public string GetJwtIssuer()
        {
            return JwtIssuer?.Trim() ?? "";
        }

        public string GetJwtAudience()
        {
            return JwtAudience?.Trim() ?? "";
        }

        public int GetJwtTokenLifetimeMinutes()
        {
            if (JwtTokenLifetimeMinutes < 1)
            {
                return 1;
            }

            if (JwtTokenLifetimeMinutes > 1440)
            {
                return 1440;
            }

            return JwtTokenLifetimeMinutes;
        }
    }
}
