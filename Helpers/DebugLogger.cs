using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NetKeyer.Helpers;

/// <summary>
/// Centralized debug logging system controlled by the NETKEYER_DEBUG environment variable.
/// Supports comma-separated categories, 'all' keyword, and wildcard matching.
/// Logs to both console (where available) and a file in the NetKeyer application data directory.
///
/// Examples:
///   NETKEYER_DEBUG=all                     - Enable all categories
///   NETKEYER_DEBUG=keyer,midi              - Enable specific categories
///   NETKEYER_DEBUG=midi*                   - Enable all categories starting with 'midi'
///   NETKEYER_DEBUG=keyer,midi*,sidetone    - Mixed specific and wildcard patterns
///
/// Log file location:
///   Windows: %APPDATA%\NetKeyer\debug.log
///   Linux/macOS: ~/.config/NetKeyer/debug.log
/// </summary>
public static class DebugLogger
{
    private static readonly Lazy<DebugConfig> _config = new(() => new DebugConfig());
    private static readonly Lazy<FileLogger> _fileLogger = new(() => new FileLogger());
    private static bool _loggedStartupMessage = false;
    private static readonly object _startupLock = new();

    /// <summary>
    /// Gets the path to the debug log file.
    /// </summary>
    public static string LogFilePath => _fileLogger.Value.LogFilePath;

    /// <summary>
    /// Log a debug message if the specified category is enabled.
    /// </summary>
    /// <param name="category">The debug category (e.g., "keyer", "midi", "sidetone")</param>
    /// <param name="message">The message to log</param>
    public static void Log(string category, string message)
    {
        if (_config.Value.IsEnabled(category))
        {
            // Log the debug file location on first use
            lock (_startupLock)
            {
                if (!_loggedStartupMessage)
                {
                    _loggedStartupMessage = true;
                    var startupMsg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [system] Debug logging enabled. Log file: {LogFilePath}";
                    Console.WriteLine(startupMsg);
                    _fileLogger.Value.Write(startupMsg);
                }
            }

            var timestampedMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{category}] {message}";

            // Write to console (works on Linux/macOS, and in debuggers on Windows)
            Console.WriteLine(timestampedMessage);

            // Write to file (always works, especially important for Windows GUI apps)
            _fileLogger.Value.Write(timestampedMessage);
        }
    }

    private class DebugConfig
    {
        private readonly bool _allEnabled;
        private readonly HashSet<string> _exactCategories;
        private readonly List<string> _wildcardPrefixes;

        public DebugConfig()
        {
            _exactCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _wildcardPrefixes = new List<string>();
            _allEnabled = false;

            var debugVar = Environment.GetEnvironmentVariable("NETKEYER_DEBUG");
            if (string.IsNullOrWhiteSpace(debugVar))
            {
                return;
            }

            var categories = debugVar.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                     .Select(c => c.Trim())
                                     .Where(c => !string.IsNullOrEmpty(c));

            foreach (var category in categories)
            {
                if (category.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    _allEnabled = true;
                    return; // No need to process other categories if 'all' is enabled
                }
                else if (category.EndsWith('*'))
                {
                    // Wildcard pattern - store the prefix without the asterisk
                    var prefix = category[..^1];
                    if (!string.IsNullOrEmpty(prefix))
                    {
                        _wildcardPrefixes.Add(prefix);
                    }
                }
                else
                {
                    // Exact category match
                    _exactCategories.Add(category);
                }
            }
        }

        public bool IsEnabled(string category)
        {
            if (_allEnabled)
            {
                return true;
            }

            if (_exactCategories.Contains(category))
            {
                return true;
            }

            foreach (var prefix in _wildcardPrefixes)
            {
                if (category.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private class FileLogger
    {
        private readonly string _logFilePath;
        private readonly object _lock = new();
        private const long MaxLogFileSize = 10 * 1024 * 1024; // 10 MB

        public string LogFilePath => _logFilePath;

        public FileLogger()
        {
            try
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var appFolder = Path.Combine(appDataPath, "NetKeyer");
                Directory.CreateDirectory(appFolder);
                _logFilePath = Path.Combine(appFolder, "debug.log");

                // Rotate log file if it's too large
                RotateLogIfNeeded();
            }
            catch (Exception ex)
            {
                // Fallback to temp directory if we can't access app data folder
                var tempFolder = Path.GetTempPath();
                var appFolder = Path.Combine(tempFolder, "NetKeyer");
                Directory.CreateDirectory(appFolder);
                _logFilePath = Path.Combine(appFolder, "debug.log");

                Console.WriteLine($"Warning: Could not access application data folder, using temp directory for logs: {ex.Message}");
            }
        }

        public void Write(string message)
        {
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(_logFilePath, message + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                // If file logging fails, at least try to output to console
                Console.WriteLine($"Warning: Failed to write to log file: {ex.Message}");
            }
        }

        private void RotateLogIfNeeded()
        {
            try
            {
                if (File.Exists(_logFilePath))
                {
                    var fileInfo = new FileInfo(_logFilePath);
                    if (fileInfo.Length > MaxLogFileSize)
                    {
                        // Keep the old log as debug.log.old (overwrite previous .old file)
                        var oldLogPath = _logFilePath + ".old";
                        if (File.Exists(oldLogPath))
                        {
                            File.Delete(oldLogPath);
                        }
                        File.Move(_logFilePath, oldLogPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to rotate log file: {ex.Message}");
            }
        }
    }
}
