namespace BlazeCannon.Core.Interfaces;
using BlazeCannon.Core.Models;

public interface IBrowserEngine : IAsyncDisposable
{
    event Action<BlazorMessage>? OnWebSocketMessage;
    Task LaunchAsync(CancellationToken ct = default);
    Task NavigateAsync(string url, CancellationToken ct = default);
    Task<List<BlazorElement>> DiscoverFieldsAsync(CancellationToken ct = default);
    Task FillFieldAsync(string selector, string value, CancellationToken ct = default);
    Task ClickAsync(string selector, CancellationToken ct = default);
    Task<byte[]> ScreenshotAsync(CancellationToken ct = default);
    Task<string> GetPageContentAsync(CancellationToken ct = default);
    bool IsRunning { get; }
}
