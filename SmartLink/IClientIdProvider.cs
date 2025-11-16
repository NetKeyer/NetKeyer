namespace NetKeyer.SmartLink
{
    /// <summary>
    /// Provides the client_id needed for SmartLink authentication.
    /// </summary>
    public interface IClientIdProvider
    {
        /// <summary>
        /// Gets the client_id for Auth0 authentication.
        /// </summary>
        /// <returns>The client_id if available, null otherwise.</returns>
        string GetClientId();
    }
}
