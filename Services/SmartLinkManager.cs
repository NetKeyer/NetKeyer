using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flex.Smoothlake.FlexLib;
using NetKeyer.Models;
using NetKeyer.SmartLink;

namespace NetKeyer.Services;

public class SmartLinkStatusChangedEventArgs : EventArgs
{
    public string Status { get; set; }
    public bool IsAuthenticated { get; set; }
    public string ButtonText { get; set; }
}

public class WanRadiosDiscoveredEventArgs : EventArgs
{
    public List<Radio> Radios { get; set; }
}

public class WanConnectionReadyEventArgs : EventArgs
{
    public string WanConnectionHandle { get; set; }
    public string Serial { get; set; }
}

public class SmartLinkManager
{
    private readonly SmartLinkAuthService _smartLinkAuth;
    private WanServer _wanServer;
    private readonly ManualResetEvent _wanConnectionReadyEvent = new ManualResetEvent(false);
    private UserSettings _settings;
    private List<Radio> _cachedWanRadios = new List<Radio>();

    public bool IsAvailable { get; private set; }
    public bool IsAuthenticated => _smartLinkAuth?.AuthState == SmartLinkAuthState.Authenticated;
    public WanServer WanServer => _wanServer;

    public event EventHandler<SmartLinkStatusChangedEventArgs> StatusChanged;
    public event EventHandler<string> ErrorOccurred;
    public event EventHandler<WanRadiosDiscoveredEventArgs> WanRadiosDiscovered;
    public event EventHandler RegistrationInvalid;
    public event EventHandler<WanConnectionReadyEventArgs> WanRadioConnectReady;

    public SmartLinkManager(UserSettings settings)
    {
        _settings = settings;

        // Initialize SmartLink authentication service
        var clientIdProvider = new ConfigFileClientIdProvider();
        _smartLinkAuth = new SmartLinkAuthService(clientIdProvider);

        IsAvailable = _smartLinkAuth.IsAvailable;

        if (!IsAvailable)
        {
            RaiseStatusChanged("No client_id configured", false, "Login to SmartLink");
            return;
        }

        _smartLinkAuth.AuthStateChanged += SmartLinkAuth_AuthStateChanged;
        _smartLinkAuth.ErrorOccurred += SmartLinkAuth_ErrorOccurred;
    }

    public async Task<bool> TryRestoreSessionAsync()
    {
        if (string.IsNullOrEmpty(_settings.SmartLinkRefreshToken))
            return false;

        var success = await _smartLinkAuth.RestoreSessionAsync(_settings.SmartLinkRefreshToken);
        if (success)
        {
            await ConnectToServerAsync();
        }
        return success;
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        var success = await _smartLinkAuth.LoginAsync(username, password);
        if (success)
        {
            await ConnectToServerAsync();
        }
        return success;
    }

    public void Logout()
    {
        _smartLinkAuth?.Logout();
        _wanServer?.Disconnect();

        // Clear cached WAN radios
        _cachedWanRadios.Clear();

        // Clear saved refresh token
        _settings.SmartLinkRefreshToken = null;
        _settings.Save();
    }

    public async Task ConnectToServerAsync()
    {
        if (_wanServer == null)
        {
            _wanServer = new WanServer();
            WanServer.WanRadioRadioListRecieved += WanServer_WanRadioListReceived;
            _wanServer.WanApplicationRegistrationInvalid += WanServer_RegistrationInvalid;
            _wanServer.WanRadioConnectReady += WanServer_RadioConnectReady;
        }

        if (!_wanServer.IsConnected)
        {
            _wanServer.Connect();

            if (_wanServer.IsConnected)
            {
                var token = _smartLinkAuth.GetIdToken();
                var platform = Environment.OSVersion.Platform.ToString();
                _wanServer.SendRegisterApplicationMessageToServer("NetKeyer", platform, token);

                RaiseStatusChanged("Connected to SmartLink", IsAuthenticated, GetButtonText());
            }
            else
            {
                RaiseStatusChanged("Failed to connect to SmartLink server", IsAuthenticated, GetButtonText());
            }
        }

        await Task.CompletedTask;
    }

    public async Task<(bool Success, string WanConnectionHandle)> RequestWanConnectionAsync(string radioSerial, int timeoutMs = 10000)
    {
        if (_wanServer == null || !_wanServer.IsConnected)
        {
            return (false, null);
        }

        // Reset the event and subscribe to connect_ready event
        _wanConnectionReadyEvent.Reset();

        string capturedHandle = null;
        EventHandler<WanConnectionReadyEventArgs> handler = (s, e) =>
        {
            if (e.Serial == radioSerial)
            {
                capturedHandle = e.WanConnectionHandle;
                _wanConnectionReadyEvent.Set();
            }
        };

        WanRadioConnectReady += handler;

        try
        {
            // Request connection to this radio
            _wanServer.SendConnectMessageToRadio(radioSerial, HolePunchPort: 0);

            // Wait for connect_ready response (with timeout)
            if (!_wanConnectionReadyEvent.WaitOne(timeoutMs))
            {
                return (false, null);
            }

            return (true, capturedHandle);
        }
        finally
        {
            WanRadioConnectReady -= handler;
        }
    }

    public List<Radio> GetCachedWanRadios()
    {
        // Return a copy of the cached radios to avoid external modification
        return new List<Radio>(_cachedWanRadios);
    }

    private void SmartLinkAuth_AuthStateChanged(object sender, SmartLinkAuthState state)
    {
        string status;
        string buttonText;

        switch (state)
        {
            case SmartLinkAuthState.NotAuthenticated:
                status = "Not logged in";
                buttonText = "Login to SmartLink";
                break;
            case SmartLinkAuthState.Authenticating:
                status = "Authenticating...";
                buttonText = "Authenticating...";
                break;
            case SmartLinkAuthState.Authenticated:
                status = "Authenticated";
                buttonText = "Logout from SmartLink";

                // Save refresh token only if Remember Me is enabled
                if (_settings.RememberMeSmartLink)
                {
                    var refreshToken = _smartLinkAuth.GetRefreshToken();
                    if (!string.IsNullOrEmpty(refreshToken))
                    {
                        _settings.SmartLinkRefreshToken = refreshToken;
                        _settings.Save();
                    }
                }
                else
                {
                    // Clear any existing refresh token if Remember Me is disabled
                    _settings.SmartLinkRefreshToken = null;
                    _settings.Save();
                }
                break;
            case SmartLinkAuthState.Error:
                status = "Authentication error";
                buttonText = "Retry Login";
                break;
            default:
                status = "Unknown state";
                buttonText = "Login to SmartLink";
                break;
        }

        RaiseStatusChanged(status, state == SmartLinkAuthState.Authenticated, buttonText);
    }

    private void SmartLinkAuth_ErrorOccurred(object sender, string error)
    {
        Console.WriteLine($"SmartLink error: {error}");
        ErrorOccurred?.Invoke(this, error);
        RaiseStatusChanged($"Error: {error}", IsAuthenticated, GetButtonText());
    }

    private void WanServer_WanRadioListReceived(List<Radio> radios)
    {
        // Cache the WAN radios for later retrieval
        _cachedWanRadios = radios ?? new List<Radio>();

        WanRadiosDiscovered?.Invoke(this, new WanRadiosDiscoveredEventArgs { Radios = radios });
    }

    private void WanServer_RegistrationInvalid()
    {
        _smartLinkAuth?.Logout();

        // Clear saved refresh token
        _settings.SmartLinkRefreshToken = null;
        _settings.Save();

        // Disconnect from WAN server
        _wanServer?.Disconnect();

        RaiseStatusChanged("Registration invalid - please log in again", false, "Login to SmartLink");
        RegistrationInvalid?.Invoke(this, EventArgs.Empty);
    }

    private void WanServer_RadioConnectReady(string wan_connectionhandle, string serial)
    {
        WanRadioConnectReady?.Invoke(this, new WanConnectionReadyEventArgs
        {
            WanConnectionHandle = wan_connectionhandle,
            Serial = serial
        });
    }

    private void RaiseStatusChanged(string status, bool isAuthenticated, string buttonText)
    {
        StatusChanged?.Invoke(this, new SmartLinkStatusChangedEventArgs
        {
            Status = status,
            IsAuthenticated = isAuthenticated,
            ButtonText = buttonText
        });
    }

    private string GetButtonText()
    {
        return IsAuthenticated ? "Logout from SmartLink" : "Login to SmartLink";
    }

    public string GetIdToken()
    {
        return _smartLinkAuth?.GetIdToken();
    }
}
