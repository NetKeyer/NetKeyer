using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace NetKeyer.SmartLink
{
    /// <summary>
    /// Minimal HTTP server to receive OAuth callback on localhost
    /// </summary>
    public class OAuthCallbackServer : IDisposable
    {
        private const int CALLBACK_PORT = 43539;
        private const string CALLBACK_PATH = "/netkeyer/auth_callback";

        private HttpListener _listener;
        private bool _disposed;

        public static string RedirectUri => $"http://localhost:{CALLBACK_PORT}{CALLBACK_PATH}";

        /// <summary>
        /// Starts the callback server on the fixed port
        /// </summary>
        public void Start()
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{CALLBACK_PORT}{CALLBACK_PATH}/");
                _listener.Start();
            }
            catch (HttpListenerException)
            {
                _listener?.Close();
                _listener = null;
                throw new OAuthException("port_unavailable",
                    $"Port {CALLBACK_PORT} is already in use. Please close other applications that may be using this port and try again.");
            }
        }

        /// <summary>
        /// Waits for the OAuth callback and returns the authorization code
        /// </summary>
        public async Task<string> WaitForCallbackAsync(string expectedState, CancellationToken cancellationToken)
        {
            if (_listener == null || !_listener.IsListening)
                throw new InvalidOperationException("Server not started");

            try
            {
                // Wait for incoming request with cancellation support
                var getContextTask = _listener.GetContextAsync();

                using (cancellationToken.Register(() => _listener.Stop()))
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await getContextTask;
                    }
                    catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }
                    catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }

                    var request = context.Request;
                    var response = context.Response;

                    try
                    {
                        // Parse query parameters
                        var queryParams = ParseQueryString(request.Url?.Query);

                        // Check for error response
                        if (queryParams.TryGetValue("error", out var error))
                        {
                            queryParams.TryGetValue("error_description", out var errorDescription);
                            await SendHtmlResponseAsync(response, false, errorDescription ?? error);
                            throw new OAuthException(error, errorDescription ?? "Authentication failed");
                        }

                        // Validate state parameter
                        if (!queryParams.TryGetValue("state", out var state) || state != expectedState)
                        {
                            await SendHtmlResponseAsync(response, false, "Invalid state parameter - possible CSRF attack");
                            throw new OAuthException("invalid_state", "State parameter mismatch");
                        }

                        // Get authorization code
                        if (!queryParams.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
                        {
                            await SendHtmlResponseAsync(response, false, "No authorization code received");
                            throw new OAuthException("missing_code", "No authorization code in callback");
                        }

                        // Success - send response to browser
                        await SendHtmlResponseAsync(response, true, null);
                        return code;
                    }
                    finally
                    {
                        response.Close();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (OAuthException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new OAuthException("callback_error", ex.Message);
            }
        }

        private static Dictionary<string, string> ParseQueryString(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(query))
                return result;

            // Remove leading '?'
            if (query.StartsWith("?"))
                query = query.Substring(1);

            var pairs = query.Split('&');
            foreach (var pair in pairs)
            {
                var parts = pair.Split(new[] { '=' }, 2);
                if (parts.Length == 2)
                {
                    var key = HttpUtility.UrlDecode(parts[0]);
                    var value = HttpUtility.UrlDecode(parts[1]);
                    result[key] = value;
                }
            }

            return result;
        }

        private static async Task SendHtmlResponseAsync(HttpListenerResponse response, bool success, string errorMessage)
        {
            string html;
            if (success)
            {
                html = @"<!DOCTYPE html>
<html>
<head>
    <title>Login Successful</title>
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
               display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0;
               background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); }
        .container { text-align: center; background: white; padding: 40px 60px; border-radius: 12px; box-shadow: 0 10px 40px rgba(0,0,0,0.2); }
        h1 { color: #22c55e; margin-bottom: 10px; }
        p { color: #666; margin-top: 0; }
        .checkmark { font-size: 64px; margin-bottom: 20px; }
    </style>
</head>
<body>
    <div class='container'>
        <div class='checkmark'>&#10004;</div>
        <h1>Login Successful!</h1>
        <p>You can close this browser tab and return to NetKeyer.</p>
    </div>
</body>
</html>";
            }
            else
            {
                var escapedError = WebUtility.HtmlEncode(errorMessage ?? "Unknown error");
                html = $@"<!DOCTYPE html>
<html>
<head>
    <title>Login Failed</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
               display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0;
               background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); }}
        .container {{ text-align: center; background: white; padding: 40px 60px; border-radius: 12px; box-shadow: 0 10px 40px rgba(0,0,0,0.2); }}
        h1 {{ color: #ef4444; margin-bottom: 10px; }}
        p {{ color: #666; margin-top: 0; }}
        .error {{ font-size: 64px; margin-bottom: 20px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='error'>&#10060;</div>
        <h1>Login Failed</h1>
        <p>{escapedError}</p>
        <p>Please close this tab and try again in NetKeyer.</p>
    </div>
</body>
</html>";
            }

            var buffer = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            response.StatusCode = success ? 200 : 400;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        public void Stop()
        {
            if (_listener != null && _listener.IsListening)
            {
                _listener.Stop();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Stop();
                _listener?.Close();
                _listener = null;
            }
        }
    }
}
