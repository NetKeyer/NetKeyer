using System;
using System.IdentityModel.Tokens.Jwt;
using System.Threading.Tasks;
using Auth0.AuthenticationApi;
using Auth0.AuthenticationApi.Models;

namespace NetKeyer.SmartLink
{
    /// <summary>
    /// Handles Auth0 authentication for SmartLink using Resource Owner Password Credentials flow
    /// </summary>
    public class SmartLinkAuthService : IDisposable
    {
        private const string AUTH0_DOMAIN = "frtest.auth0.com";

        private readonly IClientIdProvider _clientIdProvider;
        private readonly AuthenticationApiClient _authClient;
        private SmartLinkTokens _tokens;

        public event EventHandler<SmartLinkAuthState> AuthStateChanged;
        public event EventHandler<string> ErrorOccurred;

        public SmartLinkAuthState AuthState { get; private set; } = SmartLinkAuthState.NotAuthenticated;
        public bool IsAvailable => _clientIdProvider.GetClientId() != null;

        public SmartLinkAuthService(IClientIdProvider clientIdProvider)
        {
            _clientIdProvider = clientIdProvider ?? throw new ArgumentNullException(nameof(clientIdProvider));
            _authClient = new AuthenticationApiClient(AUTH0_DOMAIN);
        }

        /// <summary>
        /// Authenticates using username and password (Resource Owner Password Credentials flow)
        /// </summary>
        public async Task<bool> LoginAsync(string username, string password)
        {
            var clientId = _clientIdProvider.GetClientId();
            if (string.IsNullOrEmpty(clientId))
            {
                ErrorOccurred?.Invoke(this, "Client ID not available");
                return false;
            }

            try
            {
                SetAuthState(SmartLinkAuthState.Authenticating);

                var request = new ResourceOwnerTokenRequest
                {
                    ClientId = clientId,
                    Username = username,
                    Password = password,
                    Scope = "openid offline_access email given_name family_name picture",
                    Realm = "Username-Password-Authentication" // Auth0 default database connection
                };

                var response = await _authClient.GetTokenAsync(request);

                // Parse JWT to get expiration
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(response.IdToken);
                var exp = jwtToken.ValidTo;

                _tokens = new SmartLinkTokens
                {
                    IdToken = response.IdToken,
                    RefreshToken = response.RefreshToken,
                    ExpiresAt = exp
                };

                SetAuthState(SmartLinkAuthState.Authenticated);
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Login failed: {ex.Message}");
                SetAuthState(SmartLinkAuthState.Error);
                return false;
            }
        }

        /// <summary>
        /// Restores session from a saved refresh token
        /// </summary>
        public async Task<bool> RestoreSessionAsync(string refreshToken)
        {
            var clientId = _clientIdProvider.GetClientId();
            if (string.IsNullOrEmpty(clientId))
            {
                return false;
            }

            try
            {
                SetAuthState(SmartLinkAuthState.Authenticating);

                var request = new RefreshTokenRequest
                {
                    ClientId = clientId,
                    RefreshToken = refreshToken
                };

                var response = await _authClient.GetTokenAsync(request);

                // Parse JWT to get expiration
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(response.IdToken);
                var exp = jwtToken.ValidTo;

                _tokens = new SmartLinkTokens
                {
                    IdToken = response.IdToken,
                    RefreshToken = refreshToken, // Keep the original refresh token
                    ExpiresAt = exp
                };

                SetAuthState(SmartLinkAuthState.Authenticated);
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Failed to restore session: {ex.Message}");
                SetAuthState(SmartLinkAuthState.NotAuthenticated);
                return false;
            }
        }

        /// <summary>
        /// Gets the current ID token (JWT) for SmartLink server authentication
        /// </summary>
        public string GetIdToken()
        {
            return _tokens?.IdToken;
        }

        /// <summary>
        /// Gets the current refresh token for persistence
        /// </summary>
        public string GetRefreshToken()
        {
            return _tokens?.RefreshToken;
        }

        /// <summary>
        /// Checks if the current token is expired
        /// </summary>
        public bool IsTokenExpired()
        {
            return _tokens?.IsExpired ?? true;
        }

        /// <summary>
        /// Clears the current authentication
        /// </summary>
        public void Logout()
        {
            _tokens = null;
            SetAuthState(SmartLinkAuthState.NotAuthenticated);
        }

        private void SetAuthState(SmartLinkAuthState state)
        {
            if (AuthState != state)
            {
                AuthState = state;
                AuthStateChanged?.Invoke(this, state);
            }
        }

        public void Dispose()
        {
            _authClient?.Dispose();
        }
    }
}
