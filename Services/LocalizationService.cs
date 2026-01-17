using System;
using System.Globalization;
using System.Resources;

namespace NetKeyer.Services;

/// <summary>
/// Localization service that provides access to localized strings using ResourceManager.
/// </summary>
public class LocalizationService : ILocalizationService
{
    private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
    private readonly ResourceManager _resourceManager;

    /// <summary>
    /// Gets the singleton instance of the LocalizationService.
    /// </summary>
    public static LocalizationService Instance => _instance.Value;

    public LocalizationService()
    {
        _resourceManager = NetKeyer.Resources.Strings.Resources.ResourceManager;
    }

    /// <inheritdoc/>
    public string GetString(string key)
    {
        try
        {
            var value = _resourceManager.GetString(key, CultureInfo.CurrentUICulture);
            return value ?? key;
        }
        catch
        {
            return key;
        }
    }

    /// <inheritdoc/>
    public string GetString(string key, params object[] args)
    {
        try
        {
            var value = _resourceManager.GetString(key, CultureInfo.CurrentUICulture);
            if (value == null)
                return key;

            return string.Format(CultureInfo.CurrentCulture, value, args);
        }
        catch
        {
            return key;
        }
    }
}
