using System;
using System.Collections.Generic;
using System.Linq;

namespace NetKeyer.Helpers;

/// <summary>
/// Centralized debug logging system controlled by the NETKEYER_DEBUG environment variable.
/// Supports comma-separated categories, 'all' keyword, and wildcard matching.
///
/// Examples:
///   NETKEYER_DEBUG=all                     - Enable all categories
///   NETKEYER_DEBUG=keyer,midi              - Enable specific categories
///   NETKEYER_DEBUG=midi*                   - Enable all categories starting with 'midi'
///   NETKEYER_DEBUG=keyer,midi*,sidetone    - Mixed specific and wildcard patterns
/// </summary>
public static class DebugLogger
{
    private static readonly Lazy<DebugConfig> _config = new(() => new DebugConfig());

    /// <summary>
    /// Log a debug message if the specified category is enabled.
    /// </summary>
    /// <param name="category">The debug category (e.g., "keyer", "midi", "sidetone")</param>
    /// <param name="message">The message to log</param>
    public static void Log(string category, string message)
    {
        if (_config.Value.IsEnabled(category))
        {
            Console.WriteLine(message);
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
}
