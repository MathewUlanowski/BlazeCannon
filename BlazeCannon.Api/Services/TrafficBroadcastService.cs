using BlazeCannon.Api.Hubs;
using BlazeCannon.Core.Interfaces;
using BlazeCannon.Core.Models;
using Microsoft.AspNetCore.SignalR;

namespace BlazeCannon.Api.Services;

/// <summary>
/// Wires IMitmProxy and ReplayStagingService events onto the TrafficHub so
/// Angular clients get realtime updates. Hosted service so it's alive for the
/// lifetime of the app regardless of whether any hub client is connected.
/// </summary>
public class TrafficBroadcastService : IHostedService
{
    private readonly IMitmProxy _proxy;
    private readonly ReplayStagingService _staging;
    private readonly IHubContext<TrafficHub> _hub;
    private readonly ILogger<TrafficBroadcastService> _logger;

    private Action<BlazorMessage>? _onMessageIntercepted;
    private Action<SessionInfo>? _onSessionOpened;
    private Action<string>? _onSessionClosed;
    private Action? _onTrafficCleared;
    private Action? _onStageChanged;

    public TrafficBroadcastService(
        IMitmProxy proxy,
        ReplayStagingService staging,
        IHubContext<TrafficHub> hub,
        ILogger<TrafficBroadcastService> logger)
    {
        _proxy = proxy;
        _staging = staging;
        _hub = hub;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _onMessageIntercepted = msg => Fire("MessageIntercepted", msg);
        _onSessionOpened = info => Fire("SessionOpened", info);
        _onSessionClosed = id => Fire("SessionClosed", new { sessionId = id });
        _onTrafficCleared = () => Fire("TrafficCleared");
        _onStageChanged = () => Fire("StageChanged");

        _proxy.OnMessageIntercepted += _onMessageIntercepted;
        _proxy.OnSessionOpened += _onSessionOpened;
        _proxy.OnSessionClosed += _onSessionClosed;
        _proxy.OnTrafficCleared += _onTrafficCleared;
        _staging.OnStageChanged += _onStageChanged;

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_onMessageIntercepted != null) _proxy.OnMessageIntercepted -= _onMessageIntercepted;
        if (_onSessionOpened != null) _proxy.OnSessionOpened -= _onSessionOpened;
        if (_onSessionClosed != null) _proxy.OnSessionClosed -= _onSessionClosed;
        if (_onTrafficCleared != null) _proxy.OnTrafficCleared -= _onTrafficCleared;
        if (_onStageChanged != null) _staging.OnStageChanged -= _onStageChanged;
        return Task.CompletedTask;
    }

    private void Fire(string method, object? payload = null)
    {
        // Fire-and-forget; hub send failures should not break event producers.
        _ = Task.Run(async () =>
        {
            try
            {
                if (payload is null)
                    await _hub.Clients.All.SendAsync(method);
                else
                    await _hub.Clients.All.SendAsync(method, payload);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hub broadcast failed: {Method}", method);
            }
        });
    }
}
