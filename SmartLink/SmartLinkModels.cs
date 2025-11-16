using System;

namespace NetKeyer.SmartLink
{
    /// <summary>
    /// Represents authentication tokens from Auth0
    /// </summary>
    public class SmartLinkTokens
    {
        public string IdToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTime ExpiresAt { get; set; }

        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    }

    /// <summary>
    /// Authentication state for SmartLink
    /// </summary>
    public enum SmartLinkAuthState
    {
        NotAuthenticated,
        Authenticating,
        Authenticated,
        Error
    }
}
