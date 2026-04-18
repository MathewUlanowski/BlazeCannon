using BlazeCannon.Core.Interfaces;
using BlazeCannon.Core.Models;
using BlazeCannon.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace BlazeCannon.Browser;

public class PlaywrightBrowserEngine : IBrowserEngine
{
    private readonly IProtocolDecoder _decoder;
    private readonly ILogger<PlaywrightBrowserEngine> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;

    public event Action<BlazorMessage>? OnWebSocketMessage;
    public bool IsRunning => _browser?.IsConnected == true;

    public PlaywrightBrowserEngine(IProtocolDecoder decoder, ILogger<PlaywrightBrowserEngine> logger)
    {
        _decoder = decoder;
        _logger = logger;
    }

    public async Task LaunchAsync(CancellationToken ct = default)
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = new[] { "--disable-web-security", "--no-sandbox" }
        });

        var context = await _browser.NewContextAsync();
        _page = await context.NewPageAsync();

        // Intercept WebSocket frames for protocol analysis (both JSON and MessagePack)
        _page.WebSocket += (_, ws) =>
        {
            _logger.LogInformation("WebSocket opened: {Url}", ws.Url);

            ws.FrameSent += (_, frame) =>
            {
                try
                {
                    if (frame.Binary != null && frame.Binary.Length > 0)
                    {
                        foreach (var msg in _decoder.DecodeMessagePackMessages(frame.Binary, MessageDirection.ClientToServer))
                            OnWebSocketMessage?.Invoke(msg);
                    }
                    else if (frame.Text != null)
                    {
                        foreach (var msg in _decoder.DecodeMessages(frame.Text, MessageDirection.ClientToServer))
                            OnWebSocketMessage?.Invoke(msg);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to decode sent frame");
                }
            };

            ws.FrameReceived += (_, frame) =>
            {
                try
                {
                    if (frame.Binary != null && frame.Binary.Length > 0)
                    {
                        foreach (var msg in _decoder.DecodeMessagePackMessages(frame.Binary, MessageDirection.ServerToClient))
                            OnWebSocketMessage?.Invoke(msg);
                    }
                    else if (frame.Text != null)
                    {
                        foreach (var msg in _decoder.DecodeMessages(frame.Text, MessageDirection.ServerToClient))
                            OnWebSocketMessage?.Invoke(msg);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to decode received frame");
                }
            };
        };

        _logger.LogInformation("Browser launched");
    }

    public async Task NavigateAsync(string url, CancellationToken ct = default)
    {
        if (_page == null) throw new InvalidOperationException("Browser not launched");
        await _page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        _logger.LogInformation("Navigated to {Url}", url);
    }

    public async Task<List<BlazorElement>> DiscoverFieldsAsync(CancellationToken ct = default)
    {
        if (_page == null) throw new InvalidOperationException("Browser not launched");

        var elements = new List<BlazorElement>();
        var inputs = await _page.QuerySelectorAllAsync("input, textarea, select, button[type='submit']");

        foreach (var input in inputs)
        {
            var element = new BlazorElement
            {
                TagName = await input.EvaluateAsync<string>("el => el.tagName.toLowerCase()"),
                Id = await input.GetAttributeAsync("id"),
                Name = await input.GetAttributeAsync("name"),
                Type = await input.GetAttributeAsync("type"),
                Value = await input.GetAttributeAsync("value"),
            };
            elements.Add(element);
        }

        _logger.LogInformation("Discovered {Count} form elements", elements.Count);
        return elements;
    }

    public async Task FillFieldAsync(string selector, string value, CancellationToken ct = default)
    {
        if (_page == null) throw new InvalidOperationException("Browser not launched");
        await _page.FillAsync(selector, value);
    }

    public async Task ClickAsync(string selector, CancellationToken ct = default)
    {
        if (_page == null) throw new InvalidOperationException("Browser not launched");
        await _page.ClickAsync(selector);
    }

    public async Task<byte[]> ScreenshotAsync(CancellationToken ct = default)
    {
        if (_page == null) throw new InvalidOperationException("Browser not launched");
        return await _page.ScreenshotAsync(new PageScreenshotOptions { FullPage = true });
    }

    public async Task<string> GetPageContentAsync(CancellationToken ct = default)
    {
        if (_page == null) throw new InvalidOperationException("Browser not launched");
        return await _page.ContentAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser != null) await _browser.CloseAsync();
        _playwright?.Dispose();
        GC.SuppressFinalize(this);
    }
}
