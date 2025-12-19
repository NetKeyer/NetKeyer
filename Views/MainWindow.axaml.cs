using System.Runtime.InteropServices;
using Avalonia.Controls;
using NetKeyer.ViewModels;

namespace NetKeyer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        // Set up native macOS menu bar
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            SetupMacOsNativeMenu();
        }
    }
    
    private void SetupMacOsNativeMenu()
    {
        // Create the native menu for macOS
        var nativeMenu = new NativeMenu();
        
        // File menu
        var fileMenu = new NativeMenuItem("File");
        var fileSubMenu = new NativeMenu();
        
        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (s, e) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ExitCommand?.Execute(null);
            }
        };
        fileSubMenu.Add(exitItem);
        fileMenu.Menu = fileSubMenu;

        // Settings menu
        var settingsMenu = new NativeMenuItem("Settings");
        var settingsSubMenu = new NativeMenu();

        var audioDeviceItem = new NativeMenuItem("Audio Output Device...");
        audioDeviceItem.Click += (s, e) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.SelectAudioDeviceCommand?.Execute(null);
            }
        };
        settingsSubMenu.Add(audioDeviceItem);
        settingsMenu.Menu = settingsSubMenu;

        // Help menu
        var helpMenu = new NativeMenuItem("Help");
        var helpSubMenu = new NativeMenu();
        
        var documentationItem = new NativeMenuItem("Documentation");
        documentationItem.Click += (s, e) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.OpenDocumentationCommand?.Execute(null);
            }
        };
        
        var aboutItem = new NativeMenuItem("About NetKeyer...");
        aboutItem.Click += (s, e) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ShowAboutCommand?.Execute(null);
            }
        };
        
        helpSubMenu.Add(documentationItem);
        helpSubMenu.Add(aboutItem);
        helpMenu.Menu = helpSubMenu;
        
        // Add menus to the native menu bar
        nativeMenu.Add(fileMenu);
        nativeMenu.Add(settingsMenu);
        nativeMenu.Add(helpMenu);
        
        // Set the native menu for this window
        NativeMenu.SetMenu(this, nativeMenu);
    }
}