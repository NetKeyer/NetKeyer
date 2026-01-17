namespace NetKeyer.Services;

/// <summary>
/// Interface for localization service that provides access to localized strings.
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Gets a localized string by its key.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The localized string, or the key itself if not found.</returns>
    string GetString(string key);

    /// <summary>
    /// Gets a localized string by its key and formats it with the provided arguments.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <param name="args">The format arguments.</param>
    /// <returns>The formatted localized string, or the key itself if not found.</returns>
    string GetString(string key, params object[] args);
}
