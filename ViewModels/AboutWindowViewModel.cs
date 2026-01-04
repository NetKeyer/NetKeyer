using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetKeyer.Helpers;
using Velopack;
using Velopack.Sources;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace NetKeyer.ViewModels;

public partial class AboutWindowViewModel : ViewModelBase
{
    private readonly Window _window;

    public string VersionText { get; }

    [ObservableProperty]
    private string _updateStatus = "";

    [ObservableProperty]
    private bool _isCheckingForUpdates = false;

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
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            IsCheckingForUpdates = true;
            UpdateStatus = "Checking for updates...";

            DebugLogger.Log("update", "=== Manual update check started ===");
            DebugLogger.Log("update", $"Current time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            DebugLogger.Log("update", "Creating GithubSource...");
            var source = new GithubSource("https://github.com/NetKeyer/NetKeyer", null, false);
            DebugLogger.Log("update", "GithubSource created successfully");

            DebugLogger.Log("update", "Creating UpdateManager...");
            var mgr = new UpdateManager(source);
            DebugLogger.Log("update", $"UpdateManager initialized");
            DebugLogger.Log("update", $"  IsInstalled: {mgr.IsInstalled}");
            DebugLogger.Log("update", $"  AppId: {mgr.AppId}");
            DebugLogger.Log("update", $"  UpdateUrl: https://github.com/NetKeyer/NetKeyer");

            if (!mgr.IsInstalled)
            {
                DebugLogger.Log("update", "App not installed via Velopack - running in development mode");
                UpdateStatus = "App is not installed via Velopack (running in development mode)";
                return;
            }

            DebugLogger.Log("update", $"Current version: {mgr.CurrentVersion}");

            // Log system information
            DebugLogger.Log("update", $"Operating System: {Environment.OSVersion}");
            DebugLogger.Log("update", $"64-bit OS: {Environment.Is64BitOperatingSystem}");
            DebugLogger.Log("update", $"64-bit Process: {Environment.Is64BitProcess}");

            DebugLogger.Log("update", "Calling CheckForUpdatesAsync() - this will query GitHub releases...");
            DebugLogger.Log("update", $"GitHub URL: https://github.com/NetKeyer/NetKeyer");
            DebugLogger.Log("update", $"Thread ID: {System.Threading.Thread.CurrentThread.ManagedThreadId}");

            // Add a 30-second timeout to prevent indefinite hanging
            var checkTask = mgr.CheckForUpdatesAsync();
            DebugLogger.Log("update", "CheckForUpdatesAsync task started, waiting for completion...");

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
            var completedTask = await Task.WhenAny(checkTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                DebugLogger.Log("update", "CheckForUpdatesAsync() timed out after 30 seconds");
                DebugLogger.Log("update", $"Task status: {checkTask.Status}");
                DebugLogger.Log("update", $"Task is completed: {checkTask.IsCompleted}");
                DebugLogger.Log("update", $"Task is faulted: {checkTask.IsFaulted}");
                throw new TimeoutException("Update check timed out after 30 seconds. Please check your internet connection and try again.");
            }

            DebugLogger.Log("update", "CheckForUpdatesAsync task completed within timeout");
            var updateInfo = await checkTask;

            DebugLogger.Log("update", $"CheckForUpdatesAsync() completed");
            DebugLogger.Log("update", $"  updateInfo is null: {updateInfo == null}");

            if (updateInfo == null)
            {
                DebugLogger.Log("update", "No updates available - already on latest version");
                UpdateStatus = "You are running the latest version!";
                return;
            }

            // Log detailed update information
            DebugLogger.Log("update", "=== UPDATE FOUND ===");
            DebugLogger.Log("update", $"Target version: {updateInfo.TargetFullRelease?.Version}");

            if (updateInfo.TargetFullRelease != null)
            {
                var release = updateInfo.TargetFullRelease;
                DebugLogger.Log("update", $"  FileName: {release.FileName}");
                DebugLogger.Log("update", $"  SHA256: {release.SHA256}");
                DebugLogger.Log("update", $"  Size: {release.Size} bytes");
                DebugLogger.Log("update", $"  Type: {release.Type}");
            }

            UpdateStatus = $"Update available: {updateInfo.TargetFullRelease?.Version}";

            DebugLogger.Log("update", "Downloading update...");
            UpdateStatus = "Downloading update...";

            await mgr.DownloadUpdatesAsync(updateInfo, progress =>
            {
                DebugLogger.Log("update", $"Download progress: {progress}%");
                UpdateStatus = $"Downloading update... {progress}%";
            });

            DebugLogger.Log("update", "Download complete!");

            // Ask user if they want to restart
            var messageBox = MessageBoxManager.GetMessageBoxStandard(
                "Update Ready",
                $"Version {updateInfo.TargetFullRelease?.Version} has been downloaded.\n\nWould you like to restart and install it now?",
                ButtonEnum.YesNo);

            var result = await messageBox.ShowWindowDialogAsync(_window);

            if (result == ButtonResult.Yes)
            {
                DebugLogger.Log("update", "User confirmed restart - applying update...");
                UpdateStatus = "Restarting to apply update...";

                // Apply update and restart
                mgr.ApplyUpdatesAndRestart(updateInfo);
            }
            else
            {
                DebugLogger.Log("update", "User postponed update installation");
                UpdateStatus = "Update downloaded - restart app to install";
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log("update", $"=== UPDATE CHECK FAILED ===");
            DebugLogger.Log("update", $"Exception type: {ex.GetType().FullName}");
            DebugLogger.Log("update", $"Message: {ex.Message}");
            DebugLogger.Log("update", $"Stack trace:");
            DebugLogger.Log("update", ex.StackTrace ?? "(no stack trace)");

            if (ex.InnerException != null)
            {
                DebugLogger.Log("update", $"Inner exception type: {ex.InnerException.GetType().FullName}");
                DebugLogger.Log("update", $"Inner message: {ex.InnerException.Message}");
                DebugLogger.Log("update", $"Inner stack trace:");
                DebugLogger.Log("update", ex.InnerException.StackTrace ?? "(no stack trace)");
            }

            UpdateStatus = $"Update check failed: {ex.Message}";

            var errorBox = MessageBoxManager.GetMessageBoxStandard(
                "Update Check Failed",
                $"Failed to check for updates:\n\n{ex.Message}\n\nCheck the debug log for more details.",
                ButtonEnum.Ok);

            await errorBox.ShowWindowDialogAsync(_window);
        }
        finally
        {
            IsCheckingForUpdates = false;
            DebugLogger.Log("update", "=== Update check completed ===");
        }
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
