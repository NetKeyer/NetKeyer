using Avalonia.Controls;
using NetKeyer.ViewModels;

namespace NetKeyer.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        DataContext = new AboutWindowViewModel(this);
    }
}
