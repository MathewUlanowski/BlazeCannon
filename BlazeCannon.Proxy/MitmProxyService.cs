using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using BlazeCannon.Core.Interfaces;
using BlazeCannon.Core.Models;
using Microsoft.Extensions.Logging;

namespace BlazeCannon.Proxy;

public class MitmProxyService : IMitmProxy
{
    private readonly ILogger<MitmProxyService> _logger;
    private readonly ConcurrentDictionary<string, WebSocket> _activeSessions = new();
    private readonly List<BlazorMessage> _captured = new();
    private readonly object _lock = new();

    public event Action<BlazorMessage>? OnMessageIntercepted;
    public bool IsRunning => true; // Running as middleware, always active
    public int ActiveSessionCount => _activeSessions.Count;

    public IReadOnlyList<BlazorMessage> CapturedMessages
    {
        get { lock (_lock) return _captured.ToList().AsReadOnly(); }
    }

    public MitmProxyService(ILogger<MitmProxyService> logger)
    {
        _logger = logger;
    }

    public void RecordMessage(BlazorMessage message)
    {
        lock (_lock) _captured.Add(message);
        OnMessageIntercepted?.Invoke(message);
    }

    public void RegisterSession(string sessionId, WebSocket targetWebSocket)
    {
        _activeSessions[sessionId] = targetWebSocket;
        _logger.LogInformation("MITM session registered: {SessionId} (active: {Count})", sessionId, _activeSessions.Count);
    }

    public void UnregisterSession(string sessionId)
    {
        _activeSessions.TryRemove(sessionId, out _);
        _logger.LogInformation("MITM session unregistered: {SessionId} (active: {Count})", sessionId, _activeSessions.Count);
    }

    public void ClearMessages()
    {
        lock (_lock) _captured.Clear();
    }

    public async Task ReplayMessageAsync(BlazorMessage message, CancellationToken ct = default)
    {
        WebSocket? targetWs = null;

        // Try the original session first
        if (message.SessionId != null)
            _activeSessions.TryGetValue(message.SessionId, out targetWs);

        // Fall back to any active session
        if (targetWs == null || targetWs.State != WebSocketState.Open)
        {
            targetWs = _activeSessions.Values.FirstOrDefault(ws => ws.State == WebSocketState.Open);
        }

        if (targetWs == null || targetWs.State != WebSocketState.Open)
        {
            _logger.LogWarning("No active session available for replay");
            throw new InvalidOperationException("No active WebSocket session to replay to");
        }

        // Replay binary or text
        if (message.RawBinaryPayload != null)
        {
            await targetWs.SendAsync(
                message.RawBinaryPayload,
                WebSocketMessageType.Binary, true, ct);
        }
        else
        {
            var bytes = Encoding.UTF8.GetBytes(message.RawPayload);
            await targetWs.SendAsync(
                bytes,
                WebSocketMessageType.Text, true, ct);
        }

        _logger.LogInformation("Replayed {Direction} message: {Method}",
            message.Direction, message.HubMethod ?? message.MessageType.ToString());
    }
}
