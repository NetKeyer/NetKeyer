using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace NetKeyer.Services.Rendezvous;

public sealed class RendezvousHostRegistrationOptions
{
    public string ServerUrl { get; set; } = "";
    public string HostId { get; set; } = "";
    public int MaxClients { get; set; } = 5;
    public Dictionary<string, object> Metadata { get; set; } = new();
    public Func<string, int, string, Task> OnUseRelayAsync { get; set; }
    public Func<string, int, Task<RendezvousPortMapResult>> OnRequestPortMapAsync { get; set; }
}

public sealed class RendezvousPortMapResult
{
    public bool Success { get; set; }
    public string PublicIp { get; set; } = "";
    public int PublicPort { get; set; }
}

public sealed class RendezvousClientConnectOptions
{
    public string ServerUrl { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string HostId { get; set; } = "";
}

public sealed class RendezvousHostListRequestOptions
{
    public string ServerUrl { get; set; } = "";
    public string ClientId { get; set; } = "";
}

public sealed class RendezvousHostSummary
{
    public string HostId { get; set; } = "";
    public string Name { get; set; } = "";
    public string PublicIp { get; set; } = "";
    public int PublicPort { get; set; }
    public int CurrentClients { get; set; }
    public int MaxClients { get; set; }

    public override string ToString()
    {
        string displayHostId = string.IsNullOrWhiteSpace(HostId) ? "unknown" : HostId.Trim();
        string displayName = string.IsNullOrWhiteSpace(Name) ? displayHostId : Name.Trim();
        return $"'{displayHostId}' - {displayName} ({CurrentClients}/{MaxClients})";
    }
}

public sealed class RendezvousResolvedEndpoint
{
    public string HostPublicIp { get; set; } = "";
    public int HostPublicPort { get; set; }
    public string SessionId { get; set; } = "";
}

public sealed class RendezvousHostRegistrationSession : IAsyncDisposable
{
    private readonly Func<ValueTask> _disposeAsync;

    public RendezvousHostRegistrationSession(Func<ValueTask> disposeAsync)
    {
        _disposeAsync = disposeAsync;
    }

    public ValueTask DisposeAsync() => _disposeAsync();
}

public sealed class RendezvousClientConnectionSession : IAsyncDisposable
{
    private readonly Func<bool, System.Threading.CancellationToken, System.Threading.Tasks.Task> _reportPunchResultAsync;
    private readonly Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<bool>> _requestPortMapAsync;
    private readonly Func<ValueTask> _disposeAsync;

    public RendezvousClientConnectionSession(
        RendezvousResolvedEndpoint endpoint,
        string clientId,
        string hostId,
        Func<bool, System.Threading.CancellationToken, System.Threading.Tasks.Task> reportPunchResultAsync,
        Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<bool>> requestPortMapAsync,
        Func<ValueTask> disposeAsync)
    {
        Endpoint = endpoint;
        ClientId = clientId;
        HostId = hostId;
        _reportPunchResultAsync = reportPunchResultAsync;
        _requestPortMapAsync = requestPortMapAsync;
        _disposeAsync = disposeAsync;
    }

    public RendezvousResolvedEndpoint Endpoint { get; internal set; }
    public string ClientId { get; }
    public string HostId { get; }
    internal ClientWebSocket ControlSocket { get; set; }
    internal CancellationTokenSource ControlMonitorCts { get; set; }
    internal Task ControlMonitorTask { get; set; }
    internal bool ControlMonitorStarted { get; set; }
    public string RelayHost { get; internal set; } = "";
    public int RelayPort { get; internal set; }
    public bool RelayRequested { get; internal set; }

    public bool HasRelayEndpoint => RelayRequested && !string.IsNullOrWhiteSpace(RelayHost) && RelayPort > 0;

    public System.Threading.Tasks.Task ReportPunchResultAsync(bool success, System.Threading.CancellationToken ct)
        => _reportPunchResultAsync(success, ct);

    public System.Threading.Tasks.Task<bool> RequestPortMapAsync(System.Threading.CancellationToken ct)
        => _requestPortMapAsync(ct);

    public ValueTask DisposeAsync() => _disposeAsync();
}
