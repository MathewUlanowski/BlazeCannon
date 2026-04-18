using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using BlazeCannon.Core.Interfaces;
using BlazeCannon.Core.Models;
using Microsoft.Extensions.Logging;

namespace BlazeCannon.Proxy;

public class MitmProxyService : IMitmProxy
{
    private readonly ILogger<MitmProxyService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConcurrentDictionary<string, WebSocket> _activeSessions = new();
    private readonly ConcurrentDictionary<string, LongPollSessionInfo> _longPollSessions = new();
    private readonly List<BlazorMessage> _captured = new();
    private readonly object _lock = new();

    public event Action<BlazorMessage>? OnMessageIntercepted;
    public event Action<SessionInfo>? OnSessionOpened;
    public event Action<string>? OnSessionClosed;
    public event Action? OnTrafficCleared;
    public bool IsRunning => true; // Running as middleware, always active
    public int ActiveSessionCount => _activeSessions.Count + _longPollSessions.Count;

    public IReadOnlyList<BlazorMessage> CapturedMessages
    {
        get { lock (_lock) return _captured.ToList().AsReadOnly(); }
    }

    public MitmProxyService(ILogger<MitmProxyService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public void RecordMessage(BlazorMessage message)
    {
        lock (_lock) _captured.Add(message);
        OnMessageIntercepted?.Invoke(message);
    }

    public void RegisterSession(string sessionId, WebSocket targetWebSocket)
    {
        RegisterSession(sessionId, targetWebSocket, host: null, hubPath: null, transport: null);
    }

    public void RegisterSession(string sessionId, WebSocket targetWebSocket, string? host, string? hubPath, string? transport)
    {
        _activeSessions[sessionId] = targetWebSocket;
        _logger.LogInformation("MITM session registered: {SessionId} (active: {Count})", sessionId, ActiveSessionCount);
        OnSessionOpened?.Invoke(new SessionInfo
        {
            SessionId = sessionId,
            Host = host,
            HubPath = hubPath,
            Transport = transport
        });
    }

    /// <summary>
    /// Register (or refresh) a Blazor/SignalR long-poll session so replay can POST
    /// framed messages back to the target's <c>/_blazor?id=…</c> endpoint.
    /// Idempotent — repeated calls for the same id refresh <see cref="LongPollSessionInfo.LastSeenUtc"/>
    /// but only fire <see cref="OnSessionOpened"/> the first time, matching the WebSocket behaviour.
    /// </summary>
    public void RegisterLongPollSession(string sessionId, string targetBaseUrl, string hubPath, bool useMessagePack)
    {
        var now = DateTime.UtcNow;
        var isNew = false;

        _longPollSessions.AddOrUpdate(
            sessionId,
            _ =>
            {
                isNew = true;
                return new LongPollSessionInfo(targetBaseUrl, hubPath, useMessagePack, now);
            },
            (_, existing) => existing with { LastSeenUtc = now });

        if (isNew)
        {
            _logger.LogInformation(
                "MITM long-poll session registered: {SessionId} → {Base}{Path} (MessagePack={Mp}, active: {Count})",
                sessionId, targetBaseUrl, hubPath, useMessagePack, ActiveSessionCount);

            var host = Uri.TryCreate(targetBaseUrl, UriKind.Absolute, out var uri) ? uri.Authority : targetBaseUrl;
            OnSessionOpened?.Invoke(new SessionInfo
            {
                SessionId = sessionId,
                Host = host,
                HubPath = hubPath,
                Transport = "LongPolling"
            });
        }
    }

    public bool TryGetLongPollSession(string sessionId, out LongPollSessionInfo info)
        => _longPollSessions.TryGetValue(sessionId, out info!);

    public void UnregisterSession(string sessionId)
    {
        var hadWs = _activeSessions.TryRemove(sessionId, out _);
        var hadLp = _longPollSessions.TryRemove(sessionId, out _);
        if (hadWs || hadLp)
        {
            _logger.LogInformation("MITM session unregistered: {SessionId} (active: {Count})", sessionId, ActiveSessionCount);
            OnSessionClosed?.Invoke(sessionId);
        }
    }

    public void ClearMessages()
    {
        lock (_lock) _captured.Clear();
        OnTrafficCleared?.Invoke();
    }

    public async Task ReplayMessageAsync(BlazorMessage message, CancellationToken ct = default)
    {
        WebSocket? targetWs = null;

        // Try the original WS session first
        if (message.SessionId != null)
            _activeSessions.TryGetValue(message.SessionId, out targetWs);

        // Fall back to any active WS
        if (targetWs == null || targetWs.State != WebSocketState.Open)
        {
            targetWs = _activeSessions.Values.FirstOrDefault(ws => ws.State == WebSocketState.Open);
        }

        if (targetWs != null && targetWs.State == WebSocketState.Open)
        {
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

            _logger.LogInformation("Replayed {Direction} message over WebSocket: {Method}",
                message.Direction, message.HubMethod ?? message.MessageType.ToString());
            return;
        }

        // No WS available — try long-poll fallback.
        // Track the id to use on the wire separately from info: on the "any-open"
        // fallback path the target won't recognize message.SessionId, so we must
        // post with the key of the session we actually fell back to.
        LongPollSessionInfo? lp = null;
        string? lpSessionId = null;
        if (!string.IsNullOrEmpty(message.SessionId)
            && _longPollSessions.TryGetValue(message.SessionId!, out var exact))
        {
            lp = exact;
            lpSessionId = message.SessionId;
        }
        else
        {
            // Mirror the WS "any-open" fallback: pick an arbitrary long-poll session.
            foreach (var kvp in _longPollSessions)
            {
                lp = kvp.Value;
                lpSessionId = kvp.Key;
                break;
            }
        }

        if (lp == null)
        {
            _logger.LogWarning("No active session available for replay");
            throw new InvalidOperationException("No active sessions (WebSocket or long-poll) to replay to");
        }

        await ReplayOverLongPollAsync(message, lp.Value, lpSessionId!, ct);
    }

    private async Task ReplayOverLongPollAsync(BlazorMessage message, LongPollSessionInfo info, string sessionId, CancellationToken ct)
    {
        var url = $"{info.TargetBaseUrl}{info.HubPath}?id={Uri.EscapeDataString(sessionId)}";

        var http = _httpClientFactory.CreateClient("MitmProxy");
        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        // Ship the already-framed payload verbatim — the encoder has done the framing.
        if (message.RawBinaryPayload is { Length: > 0 })
        {
            var content = new ByteArrayContent(message.RawBinaryPayload);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            request.Content = content;
        }
        else
        {
            var content = new StringContent(message.RawPayload ?? string.Empty, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("text/plain") { CharSet = "utf-8" };
            request.Content = content;
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Long-poll replay transport error for session {SessionId}", sessionId);
            throw new InvalidOperationException($"Long-poll replay failed: {ex.Message}", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var reason = string.IsNullOrEmpty(response.ReasonPhrase) ? "(no reason)" : response.ReasonPhrase;
                _logger.LogWarning(
                    "Long-poll replay rejected: {Status} {Reason} (session {SessionId})",
                    (int)response.StatusCode, reason, sessionId);
                throw new InvalidOperationException(
                    $"Long-poll replay failed: {(int)response.StatusCode} {reason}");
            }
        }

        _logger.LogInformation(
            "Replayed {Direction} message over long-poll: {Method} → {Url}",
            message.Direction, message.HubMethod ?? message.MessageType.ToString(), url);
    }
}

/// <summary>
/// Bookkeeping for a Blazor/SignalR long-poll session so MITM replay can post
/// framed messages back to the real target instead of a WebSocket.
/// </summary>
public readonly record struct LongPollSessionInfo(
    string TargetBaseUrl,
    string HubPath,
    bool UseMessagePack,
    DateTime LastSeenUtc);
