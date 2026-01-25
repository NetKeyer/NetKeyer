using Avalonia.Controls;
using NetKeyer.ViewModels;
using System;

namespace NetKeyer.Views
{
    public partial class CWMonitorWindow : Window
    {
        public CWMonitorWindow()
        {
            InitializeComponent();
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            // Prevent the window from closing, just hide it instead
            e.Cancel = true;
            Hide();
            
            // Notify the view model that the window was closed
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.OnCWMonitorWindowClosed();
            }
            
            base.OnClosing(e);
        }
    }
}
