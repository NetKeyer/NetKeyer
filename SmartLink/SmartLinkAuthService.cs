using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Auth0.AuthenticationApi;
using Auth0.AuthenticationApi.Models;
using NetKeyer.Helpers;

namespace NetKeyer.SmartLink
{
    /// <summary>
    /// Handles Auth0 authentication for SmartLink using PKCE authorization code flow
    /// </summary>
    public class SmartLinkAuthService : IDisposable
    {
        private const string AUTH0_DOMAIN = "frtest.auth0.com";
        private const string AUTH_SCOPE = "openid offline_access email given_name family_name picture";

        private readonly IClientIdProvider _clientIdProvider;
        private readonly AuthenticationApiClient _authClient;
        private SmartLinkTokens _tokens;
        private CancellationTokenSource _loginCts;
        private OAuthCallbackServer _callbackServer;

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
        /// Authenticates using PKCE authorization code flow (browser-based)
        /// </summary>
        public async Task<bool> LoginAsync(CancellationToken cancellationToken = default)
        {
            var clientId = _clientIdProvider.GetClientId();
            if (string.IsNullOrEmpty(clientId))
            {
                ErrorOccurred?.Invoke(this, "Client ID not available");
                return false;
            }

            // Create linked cancellation token
            _loginCts?.Dispose();
            _loginCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var linkedToken = _loginCts.Token;

            try
            {
                SetAuthState(SmartLinkAuthState.Authenticating);

                // Generate PKCE code verifier and challenge
                var codeVerifier = GenerateCodeVerifier();
                var codeChallenge = GenerateCodeChallenge(codeVerifier);

                // Generate state parameter for CSRF protection
                var state = GenerateState();

                // Start callback server
                _callbackServer?.Dispose();
                _callbackServer = new OAuthCallbackServer();
                _callbackServer.Start();

                // Build authorization URL
                var authUrl = BuildAuthorizationUrl(clientId, OAuthCallbackServer.RedirectUri, codeChallenge, state);

                // Open browser for user authentication
                UrlHelper.OpenUrl(authUrl);

                // Wait for callback with authorization code
                var authorizationCode = await _callbackServer.WaitForCallbackAsync(state, linkedToken);

                // Exchange authorization code for tokens
                var request = new AuthorizationCodePkceTokenRequest
                {
                    ClientId = clientId,
                    Code = authorizationCode,
                    CodeVerifier = codeVerifier,
                    RedirectUri = OAuthCallbackServer.RedirectUri
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
            catch (OperationCanceledException)
            {
                SetAuthState(SmartLinkAuthState.NotAuthenticated);
                return false;
            }
            catch (OAuthException ex)
            {
                ErrorOccurred?.Invoke(this, $"Login failed: {ex.Message}");
                SetAuthState(SmartLinkAuthState.Error);
                return false;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Login failed: {ex.Message}");
                SetAuthState(SmartLinkAuthState.Error);
                return false;
            }
            finally
            {
                _callbackServer?.Dispose();
                _callbackServer = null;
            }
        }

        /// <summary>
        /// Cancels an in-progress login
        /// </summary>
        public void CancelLogin()
        {
            _loginCts?.Cancel();
            _callbackServer?.Stop();
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

        #region PKCE Helpers

        /// <summary>
        /// Generates a cryptographically random code verifier (32 bytes, base64url encoded)
        /// </summary>
        private static string GenerateCodeVerifier()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Base64UrlEncode(bytes);
        }

        /// <summary>
        /// Generates code challenge from verifier (SHA256 hash, base64url encoded)
        /// </summary>
        private static string GenerateCodeChallenge(string codeVerifier)
        {
            using (var sha256 = SHA256.Create())
            {
                var challengeBytes = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
                return Base64UrlEncode(challengeBytes);
            }
        }

        /// <summary>
        /// Generates a random state parameter for CSRF protection
        /// </summary>
        private static string GenerateState()
        {
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Base64UrlEncode(bytes);
        }

        /// <summary>
        /// Base64url encoding per RFC 4648
        /// </summary>
        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        /// <summary>
        /// Builds the Auth0 authorization URL for PKCE flow
        /// </summary>
        private static string BuildAuthorizationUrl(string clientId, string redirectUri, string codeChallenge, string state)
        {
            var sb = new StringBuilder();
            sb.Append($"https://{AUTH0_DOMAIN}/authorize?");
            sb.Append($"response_type=code");
            sb.Append($"&client_id={Uri.EscapeDataString(clientId)}");
            sb.Append($"&redirect_uri={Uri.EscapeDataString(redirectUri)}");
            sb.Append($"&scope={Uri.EscapeDataString(AUTH_SCOPE)}");
            sb.Append($"&code_challenge={Uri.EscapeDataString(codeChallenge)}");
            sb.Append($"&code_challenge_method=S256");
            sb.Append($"&state={Uri.EscapeDataString(state)}");
            return sb.ToString();
        }

        #endregion

        public void Dispose()
        {
            _loginCts?.Dispose();
            _callbackServer?.Dispose();
            _authClient?.Dispose();
        }
    }
}
