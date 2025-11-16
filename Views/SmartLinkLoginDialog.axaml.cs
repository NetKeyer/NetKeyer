using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace NetKeyer.Views
{
    public partial class SmartLinkLoginDialog : Window
    {
        private TextBox _usernameTextBox;
        private TextBox _passwordTextBox;
        private CheckBox _rememberMeCheckBox;
        private TextBlock _errorTextBlock;
        private TextBlock _statusTextBlock;
        private Button _loginButton;
        private Button _cancelButton;

        public string Username { get; private set; }
        public string Password { get; private set; }
        public bool RememberMe { get; private set; }
        public bool LoginSucceeded { get; private set; }

        public SmartLinkLoginDialog()
        {
            InitializeComponent();
            this.Opened += OnOpened;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            _usernameTextBox = this.FindControl<TextBox>("UsernameTextBox");
            _passwordTextBox = this.FindControl<TextBox>("PasswordTextBox");
            _rememberMeCheckBox = this.FindControl<CheckBox>("RememberMeCheckBox");
            _errorTextBlock = this.FindControl<TextBlock>("ErrorTextBlock");
            _statusTextBlock = this.FindControl<TextBlock>("StatusTextBlock");
            _loginButton = this.FindControl<Button>("LoginButton");
            _cancelButton = this.FindControl<Button>("CancelButton");
        }

        private void OnOpened(object sender, EventArgs e)
        {
            // Focus username textbox when dialog opens
            _usernameTextBox?.Focus();
        }

        public void SetRememberMe(bool rememberMe)
        {
            if (_rememberMeCheckBox != null)
            {
                _rememberMeCheckBox.IsChecked = rememberMe;
            }
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear previous error
            _errorTextBlock.IsVisible = false;
            _errorTextBlock.Text = "";

            // Validate inputs
            var username = _usernameTextBox?.Text?.Trim();
            var password = _passwordTextBox?.Text;

            if (string.IsNullOrEmpty(username))
            {
                ShowError("Please enter your email or username");
                _usernameTextBox?.Focus();
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                ShowError("Please enter your password");
                _passwordTextBox?.Focus();
                return;
            }

            // Store credentials, remember me state, and indicate success
            Username = username;
            Password = password;
            RememberMe = _rememberMeCheckBox?.IsChecked ?? false;
            LoginSucceeded = true;

            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            LoginSucceeded = false;
            Close();
        }

        private void ShowError(string message)
        {
            _errorTextBlock.Text = message;
            _errorTextBlock.IsVisible = true;
        }

        public void ShowStatus(string message)
        {
            _statusTextBlock.Text = message;
            _statusTextBlock.IsVisible = true;
        }

        public void SetButtonsEnabled(bool enabled)
        {
            _loginButton.IsEnabled = enabled;
            _cancelButton.IsEnabled = enabled;
            _usernameTextBox.IsEnabled = enabled;
            _passwordTextBox.IsEnabled = enabled;
        }
    }
}
