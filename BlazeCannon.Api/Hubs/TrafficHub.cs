using Microsoft.AspNetCore.SignalR;

namespace BlazeCannon.Api.Hubs;

/// <summary>
/// Real-time broadcast hub for captured traffic, sessions, and staging.
/// Clients connect at /hubs/traffic. The hub itself has no server-invoked
/// methods — broadcasting happens from TrafficBroadcastService via
/// IHubContext&lt;TrafficHub&gt;.
/// </summary>
public class TrafficHub : Hub
{
}
