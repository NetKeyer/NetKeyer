using Avalonia.Controls;
using Avalonia.Interactivity;
using NetKeyer.ViewModels;

namespace NetKeyer.Views
{
    public partial class AccessTokenDialog : Window
    {
        private readonly AccessTokenDialogViewModel _viewModel;

        public bool TokenSaved { get; private set; }
        public string AccessToken { get; private set; } = string.Empty;
        public bool UseLocalJwtMinting { get; private set; }
        public string JwtKeyId { get; private set; } = string.Empty;
        public string JwtKeySecret { get; private set; } = string.Empty;
        public string JwtIssuer { get; private set; } = string.Empty;
        public string JwtAudience { get; private set; } = string.Empty;
        public int JwtTokenLifetimeMinutes { get; private set; } = 30;

        public AccessTokenDialog()
        {
            InitializeComponent();
            _viewModel = new AccessTokenDialogViewModel();
            DataContext = _viewModel;
        }

        public void SetCurrentSettings(
            string token,
            bool useLocalJwtMinting,
            string jwtKeyId,
            string jwtKeySecret,
            string jwtIssuer,
            string jwtAudience,
            int jwtTokenLifetimeMinutes)
        {
            _viewModel.SetAccessToken(token ?? string.Empty);
            _viewModel.SetUseLocalJwtMinting(useLocalJwtMinting);
            _viewModel.SetJwtKeyId(jwtKeyId ?? string.Empty);
            _viewModel.SetJwtKeySecret(jwtKeySecret ?? string.Empty);
            _viewModel.SetJwtIssuer(jwtIssuer ?? string.Empty);
            _viewModel.SetJwtAudience(jwtAudience ?? string.Empty);
            _viewModel.SetJwtTokenLifetimeMinutes(jwtTokenLifetimeMinutes);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            AccessToken = _viewModel.GetAccessToken();
            UseLocalJwtMinting = _viewModel.GetUseLocalJwtMinting();
            JwtKeyId = _viewModel.GetJwtKeyId();
            JwtKeySecret = _viewModel.GetJwtKeySecret();
            JwtIssuer = _viewModel.GetJwtIssuer();
            JwtAudience = _viewModel.GetJwtAudience();
            JwtTokenLifetimeMinutes = _viewModel.GetJwtTokenLifetimeMinutes();
            TokenSaved = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            TokenSaved = false;
            Close();
        }
    }
}
