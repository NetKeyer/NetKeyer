using System.Runtime.InteropServices;
using Avalonia.Controls;
using NetKeyer.ViewModels;
using Res = NetKeyer.Resources.Strings.Resources;

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
        var fileMenu = new NativeMenuItem(Res.MacMenu_File);
        var fileSubMenu = new NativeMenu();

        var exitItem = new NativeMenuItem(Res.MacMenu_Exit);
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
        var settingsMenu = new NativeMenuItem(Res.MacMenu_Settings);
        var settingsSubMenu = new NativeMenu();

        var audioDeviceItem = new NativeMenuItem(Res.MacMenu_AudioDevice);
        audioDeviceItem.Click += (s, e) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.SelectAudioDeviceCommand?.Execute(null);
            }
        };
        settingsSubMenu.Add(audioDeviceItem);

        var midiNoteMappingItem = new NativeMenuItem(Res.MacMenu_MidiMapping);
        midiNoteMappingItem.Click += (s, e) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ConfigureMidiNotesCommand?.Execute(null);
            }
        };
        settingsSubMenu.Add(midiNoteMappingItem);

        settingsMenu.Menu = settingsSubMenu;

        // Help menu
        var helpMenu = new NativeMenuItem(Res.MacMenu_Help);
        var helpSubMenu = new NativeMenu();

        var documentationItem = new NativeMenuItem(Res.MacMenu_Documentation);
        documentationItem.Click += (s, e) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.OpenDocumentationCommand?.Execute(null);
            }
        };

        var aboutItem = new NativeMenuItem(Res.MacMenu_About);
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
