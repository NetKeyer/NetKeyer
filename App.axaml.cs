using System;
using System.Globalization;
using System.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using NetKeyer.Models;
using NetKeyer.ViewModels;
using NetKeyer.Views;

namespace NetKeyer;

public partial class App : Application
{
    public override void Initialize()
    {
        // Apply saved language setting before loading XAML
        ApplySavedLanguage();

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplySavedLanguage()
    {
        try
        {
            var settings = UserSettings.Load();
            if (!string.IsNullOrEmpty(settings.Language))
            {
                var culture = new CultureInfo(settings.Language);
                Thread.CurrentThread.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
            }
        }
        catch (Exception ex)
        {
            // If language setting is invalid, just use system default
            Console.WriteLine($"Failed to apply saved language setting: {ex.Message}");
        }
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
