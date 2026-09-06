using System;
using System.Runtime.InteropServices;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using NetKeyer.ViewModels;

namespace NetKeyer.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnMainWindowClosing;
        DataContextChanged += OnMainWindowDataContextChanged;
        Opened += OnWindowOpened;
        
        // Set up native macOS menu bar
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            SetupMacOsNativeMenu();
        }
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        ApplyWindowSizingMode();

        // Ensure startup autosize is based on finalized first-pass layout.
        Dispatcher.UIThread.Post(ApplyWindowSizingMode, DispatcherPriority.Loaded);
    }

    private void OnMainWindowDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;

        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        ApplyWindowSizingMode();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage)
            || e.PropertyName == nameof(MainWindowViewModel.RemoteMode))
        {
            ApplyWindowSizingMode();
        }
    }

    private void ApplyWindowSizingMode()
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 0;
        MinHeight = 0;
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (vm.IsExiting)
        {
            return;
        }

        e.Cancel = true;
        vm.ExitCommand?.Execute(null);
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

        var midiNoteMappingItem = new NativeMenuItem("MIDI Note Mapping...");
        midiNoteMappingItem.Click += (s, e) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ConfigureMidiNotesCommand?.Execute(null);
            }
        };
        settingsSubMenu.Add(midiNoteMappingItem);

        var accessTokenItem = new NativeMenuItem("Access Token...");
        accessTokenItem.Click += (s, e) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ConfigureAccessTokenCommand?.Execute(null);
            }
        };
        settingsSubMenu.Add(accessTokenItem);

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