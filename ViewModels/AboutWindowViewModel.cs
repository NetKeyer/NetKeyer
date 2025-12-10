using System;
using System.Reflection;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using NetKeyer.Helpers;
using Velopack;

namespace NetKeyer.ViewModels;

public partial class AboutWindowViewModel : ViewModelBase
{
    private readonly Window _window;

    public string VersionText { get; }

    public AboutWindowViewModel(Window window)
    {
        _window = window;
        VersionText = GetVersionText();
    }

    private string GetVersionText()
    {
        try
        {
            // Try to get version from Velopack first
            var updateManager = new UpdateManager("https://github.com/NetKeyer/NetKeyer");
            if (updateManager.IsInstalled)
            {
                var currentVersion = updateManager.CurrentVersion;
                if (currentVersion != null)
                {
                    return $"Version {currentVersion}";
                }
            }
        }
        catch
        {
            // Velopack not available or error occurred, fall through to assembly version
        }

        // Fall back to assembly version
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            if (version != null)
            {
                return $"Version {version.Major}.{version.Minor}.{version.Build}";
            }
        }
        catch
        {
            // If all else fails
        }

        return "Version unknown";
    }

    [RelayCommand]
    private void OpenGitHub()
    {
        UrlHelper.OpenUrl("https://github.com/NetKeyer/NetKeyer");
    }

    [RelayCommand]
    private void Close()
    {
        _window?.Close();
    }
}
