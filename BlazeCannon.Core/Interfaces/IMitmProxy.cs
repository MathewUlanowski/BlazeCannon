namespace BlazeCannon.Core.Interfaces;
using BlazeCannon.Core.Models;

public interface IMitmProxy
{
    event Action<BlazorMessage>? OnMessageIntercepted;
    event Action<SessionInfo>? OnSessionOpened;
    event Action<string>? OnSessionClosed;
    event Action? OnTrafficCleared;

    bool IsRunning { get; }
    IReadOnlyList<BlazorMessage> CapturedMessages { get; }
    int ActiveSessionCount { get; }
    Task ReplayMessageAsync(BlazorMessage message, CancellationToken ct = default);
    void RecordMessage(BlazorMessage message);
    void RegisterSession(string sessionId, System.Net.WebSockets.WebSocket targetWebSocket);
    void RegisterSession(string sessionId, System.Net.WebSockets.WebSocket targetWebSocket, string? host, string? hubPath, string? transport);
    void UnregisterSession(string sessionId);
    void ClearMessages();
}

public class SessionInfo
{
    public string SessionId { get; set; } = string.Empty;
    public string? Host { get; set; }
    public string? HubPath { get; set; }
    public string? Transport { get; set; }
}
