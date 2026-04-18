namespace BlazeCannon.Core.Interfaces;
using BlazeCannon.Core.Models;

public interface IMitmProxy
{
    event Action<BlazorMessage>? OnMessageIntercepted;
    bool IsRunning { get; }
    IReadOnlyList<BlazorMessage> CapturedMessages { get; }
    int ActiveSessionCount { get; }
    Task ReplayMessageAsync(BlazorMessage message, CancellationToken ct = default);
    void RecordMessage(BlazorMessage message);
    void RegisterSession(string sessionId, System.Net.WebSockets.WebSocket targetWebSocket);
    void UnregisterSession(string sessionId);
    void ClearMessages();
}
