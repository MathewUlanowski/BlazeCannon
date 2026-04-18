namespace BlazeCannon.Core.Interfaces;
using BlazeCannon.Core.Models;

public interface ITrafficProxy : IAsyncDisposable
{
    event Action<BlazorMessage>? OnMessageIntercepted;
    Task ConnectAsync(TargetConfig config, CancellationToken ct = default);
    Task DisconnectAsync();
    Task SendToServerAsync(string rawMessage, CancellationToken ct = default);
    bool IsConnected { get; }
    IReadOnlyList<BlazorMessage> CapturedMessages { get; }
}
