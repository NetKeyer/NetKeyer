using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace NetKeyer.Views
{
    public partial class SmartLinkLoginDialog : Window
    {
        private CheckBox _rememberMeCheckBox;
        private TextBlock _errorTextBlock;
        private TextBlock _statusTextBlock;
        private TextBlock _hintTextBlock;
        private ProgressBar _progressBar;
        private Button _cancelButton;

        private CancellationTokenSource _cts;
        private bool _completedSuccessfully;

        public bool RememberMe { get; private set; }
        public bool WasCancelled { get; private set; }
        public CancellationToken CancellationToken => _cts?.Token ?? CancellationToken.None;

        public SmartLinkLoginDialog()
        {
            InitializeComponent();
            _cts = new CancellationTokenSource();
            this.Closing += OnClosing;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            _rememberMeCheckBox = this.FindControl<CheckBox>("RememberMeCheckBox");
            _errorTextBlock = this.FindControl<TextBlock>("ErrorTextBlock");
            _statusTextBlock = this.FindControl<TextBlock>("StatusTextBlock");
            _hintTextBlock = this.FindControl<TextBlock>("HintTextBlock");
            _progressBar = this.FindControl<ProgressBar>("ProgressBar");
            _cancelButton = this.FindControl<Button>("CancelButton");
        }

        public void SetRememberMe(bool rememberMe)
        {
            if (_rememberMeCheckBox != null)
            {
                _rememberMeCheckBox.IsChecked = rememberMe;
            }
        }

        /// <summary>
        /// Updates the status text displayed in the dialog
        /// </summary>
        public void UpdateStatus(string message)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_statusTextBlock != null)
                {
                    _statusTextBlock.Text = message;
                }
            });
        }

        /// <summary>
        /// Shows an error message and hides the progress indicator
        /// </summary>
        public void ShowError(string message)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _errorTextBlock.Text = message;
                _errorTextBlock.IsVisible = true;
                _progressBar.IsVisible = false;
                _hintTextBlock.IsVisible = false;
                _statusTextBlock.Text = "Login failed";
            });
        }

        /// <summary>
        /// Indicates successful completion and closes the dialog
        /// </summary>
        public void CompleteSuccessfully()
        {
            Dispatcher.UIThread.Post(() =>
            {
                RememberMe = _rememberMeCheckBox?.IsChecked ?? false;
                WasCancelled = false;
                _completedSuccessfully = true;
                Close();
            });
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            WasCancelled = true;
            _cts?.Cancel();
            Close();
        }

        private void OnClosing(object sender, WindowClosingEventArgs e)
        {
            // If closing without explicit completion (e.g., via X button), treat as cancellation
            if (!_completedSuccessfully && !WasCancelled)
            {
                WasCancelled = true;
                _cts?.Cancel();
            }
            RememberMe = _rememberMeCheckBox?.IsChecked ?? false;
        }
    }
}
