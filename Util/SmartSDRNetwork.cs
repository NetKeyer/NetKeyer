using Flex.UiWpfFramework.Mvvm;
using Netify;

namespace Util;

public class SmartSDRNetwork : ObservableObject, INetworkObserver
{
    private bool _isInternetAvailable;
    private readonly NetworkStatusNotifier _networkStatusNotifier = new();

    public SmartSDRNetwork()
    {
        _networkStatusNotifier.AddObserver(this);
        IsInternetAvailable = _networkStatusNotifier.CheckNow() == ConnectivityStatus.Connected;
        _networkStatusNotifier.Start();
    }

    public bool IsInternetAvailable
    {
        get => _isInternetAvailable;
        set
        {
            if (_isInternetAvailable == value)
            {
                return;
            }

            _isInternetAvailable = value;
            RaisePropertyChanged(nameof(IsInternetAvailable));
        }
    }

    public void ConnectivityChanged(ConnectivityStatus status)
    {
        IsInternetAvailable = status == ConnectivityStatus.Connected;
    }
}
