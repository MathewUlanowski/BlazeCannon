using System.Net.WebSockets;
using System.Text;
using BlazeCannon.Core.Interfaces;
using BlazeCannon.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BlazeCannon.Proxy;

/// <summary>
/// HTTP forward proxy. Configure your browser to proxy through this port.
/// All HTTP traffic and WebSocket frames are captured and decoded.
/// </summary>
public class MitmProxyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMitmProxy _mitmProxy;
    private readonly IProtocolDecoder _decoder;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MitmProxyMiddleware> _logger;

    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Transfer-Encoding", "TE",
        "Trailer", "Proxy-Authorization", "Proxy-Authenticate",
        "Proxy-Connection"
    };

    public MitmProxyMiddleware(
        RequestDelegate next,
        IMitmProxy mitmProxy,
        IProtocolDecoder decoder,
        IHttpClientFactory httpClientFactory,
        ILogger<MitmProxyMiddleware> logger)
    {
        _next = next;
        _mitmProxy = mitmProxy;
        _decoder = decoder;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Derive the target from the request itself (forward proxy style)
        // Browser sends: GET http://target:5000/path HTTP/1.1
        // Kestrel gives us Host = target:5000, Path = /path
        var targetHost = context.Request.Host.ToString();

        if (string.IsNullOrEmpty(targetHost))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("BlazeCannon Forward Proxy: No Host header");
            return;
        }

        var scheme = context.Request.Scheme ?? "http";
        var targetBaseUrl = $"{scheme}://{targetHost}";

        if (context.WebSockets.IsWebSocketRequest)
        {
            await HandleWebSocketProxy(context, targetBaseUrl);
            return;
        }

        // Some browsers/proxies send the upgrade headers but Kestrel doesn't flag it.
        // Log so we know when detection misses.
        if (HasWebSocketUpgradeHeaders(context.Request))
        {
            _logger.LogWarning(
                "WS upgrade headers present but IsWebSocketRequest=false. Path={Path} Connection='{Conn}' Upgrade='{Upg}'",
                context.Request.Path,
                context.Request.Headers.Connection.ToString(),
                context.Request.Headers["Upgrade"].ToString());
        }

        await HandleHttpProxy(context, targetBaseUrl);
    }

    private static bool HasWebSocketUpgradeHeaders(HttpRequest req)
    {
        if (!req.Headers.TryGetValue("Upgrade", out var upg)) return false;
        return upg.Any(v => v != null && v.Contains("websocket", StringComparison.OrdinalIgnoreCase));
    }

    private async Task HandleHttpProxy(HttpContext context, string targetBaseUrl)
    {
        var targetUri = $"{targetBaseUrl}{context.Request.Path}{context.Request.QueryString}";
        _logger.LogDebug("Proxy {Method} -> {Target}", context.Request.Method, targetUri);

        var httpClient = _httpClientFactory.CreateClient("MitmProxy");

        var requestMessage = new HttpRequestMessage
        {
            Method = new HttpMethod(context.Request.Method),
            RequestUri = new Uri(targetUri)
        };

        // Copy request headers
        foreach (var header in context.Request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key)) continue;
            if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
            requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
        requestMessage.Headers.Host = context.Request.Host.ToString();

        // Buffer request body — needed so Blazor long-poll payloads can be decoded
        byte[]? requestBodyBytes = null;
        if (context.Request.ContentLength > 0 || context.Request.ContentType != null)
        {
            var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body);
            requestBodyBytes = body.ToArray();
            body.Position = 0;
            requestMessage.Content = new StreamContent(body);
            if (context.Request.ContentType != null)
                requestMessage.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
        }

        // SignalR long-poll transport: /_blazor?id=<connectionId> (negotiate excluded)
        var path = context.Request.Path.Value ?? string.Empty;
        var isBlazorLongPoll = path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase)
                               && !path.EndsWith("/negotiate", StringComparison.OrdinalIgnoreCase);
        var sessionId = context.Request.Query["id"].ToString();
        if (string.IsNullOrEmpty(sessionId)) sessionId = "lp-" + context.Connection.Id;
        var host = context.Request.Host.ToString();

        if (isBlazorLongPoll && requestBodyBytes is { Length: > 0 })
        {
            DecodeLongPollBody(requestBodyBytes, MessageDirection.ClientToServer, sessionId, host, path);
        }

        try
        {
            var response = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);

            context.Response.StatusCode = (int)response.StatusCode;

            foreach (var header in response.Headers)
            {
                if (HopByHopHeaders.Contains(header.Key)) continue;
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
            foreach (var header in response.Content.Headers)
            {
                if (HopByHopHeaders.Contains(header.Key)) continue;
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
            context.Response.Headers.Remove("transfer-encoding");

            if (isBlazorLongPoll)
            {
                var respBytes = await response.Content.ReadAsByteArrayAsync();
                if (respBytes.Length > 0)
                    DecodeLongPollBody(respBytes, MessageDirection.ServerToClient, sessionId, host, path);
                await context.Response.Body.WriteAsync(respBytes);
            }
            else
            {
                await response.Content.CopyToAsync(context.Response.Body);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Proxy failed: {Target}", targetUri);
            context.Response.StatusCode = 502;
            await context.Response.WriteAsync($"BlazeCannon Proxy: Cannot reach {targetUri} — {ex.Message}");
        }
    }

    // SignalR long-poll bodies carry the same framed messages as WS frames.
    // Content-Type is unreliable (typically text/plain regardless of payload),
    // so try MessagePack first (dominant in Blazor Server) then JSON.
    private void DecodeLongPollBody(byte[] data, MessageDirection direction, string sessionId, string host, string hubPath)
    {
        List<BlazorMessage>? messages = null;
        bool wasBinary = false;

        try
        {
            var binResult = _decoder.DecodeMessagePackMessages(data, direction).ToList();
            if (binResult.Count > 0) { messages = binResult; wasBinary = true; }
        }
        catch { }

        if (messages == null)
        {
            try
            {
                var textResult = _decoder.DecodeMessages(Encoding.UTF8.GetString(data), direction).ToList();
                if (textResult.Count > 0) messages = textResult;
            }
            catch { }
        }

        if (messages is { Count: > 0 })
        {
            foreach (var msg in messages)
            {
                msg.SessionId = sessionId;
                msg.Host = host;
                msg.HubPath = hubPath;
                msg.Transport = "LongPolling";
                if (wasBinary) msg.RawBinaryPayload = data;
                _mitmProxy.RecordMessage(msg);
            }
            return;
        }

        _mitmProxy.RecordMessage(new BlazorMessage
        {
            Direction = direction,
            SessionId = sessionId,
            Host = host,
            HubPath = hubPath,
            Transport = "LongPolling",
            MessageType = BlazorMessageType.Unknown,
            RawPayload = $"(long-poll body {data.Length} bytes — undecodable)",
            RawBinaryPayload = data
        });
    }

    private async Task HandleWebSocketProxy(HttpContext context, string targetBaseUrl)
    {
        var sessionId = Guid.NewGuid().ToString("N")[..8];
        var wsScheme = targetBaseUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
        var targetHost = new Uri(targetBaseUrl).Authority;
        var hubPath = context.Request.Path.Value ?? string.Empty;
        var targetWsUrl = $"{wsScheme}://{targetHost}{context.Request.Path}{context.Request.QueryString}";

        _logger.LogInformation("WebSocket proxy {Session}: {Target}", sessionId, targetWsUrl);

        var browserWs = await context.WebSockets.AcceptWebSocketAsync();
        var targetWs = new ClientWebSocket();

        // Forward headers
        foreach (var header in context.Request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key)) continue;
            if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
            if (header.Key.StartsWith("Sec-WebSocket", StringComparison.OrdinalIgnoreCase)) continue;
            try { targetWs.Options.SetRequestHeader(header.Key, header.Value!); }
            catch { }
        }

        try
        {
            await targetWs.ConnectAsync(new Uri(targetWsUrl), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebSocket connect failed: {Url}", targetWsUrl);
            await browserWs.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "Target unreachable", CancellationToken.None);
            return;
        }

        _mitmProxy.RegisterSession(sessionId, targetWs, targetHost, hubPath, "WebSocket");

        var cts = new CancellationTokenSource();
        var b2s = RelayLoop(browserWs, targetWs, MessageDirection.ClientToServer, sessionId, targetHost, hubPath, cts);
        var s2b = RelayLoop(targetWs, browserWs, MessageDirection.ServerToClient, sessionId, targetHost, hubPath, cts);

        await Task.WhenAny(b2s, s2b);
        cts.Cancel();

        _mitmProxy.UnregisterSession(sessionId);
        await CloseWs(browserWs);
        await CloseWs(targetWs);
        _logger.LogInformation("WebSocket proxy {Session} ended", sessionId);
    }

    private async Task RelayLoop(WebSocket source, WebSocket dest,
        MessageDirection direction, string sessionId, string host, string hubPath, CancellationTokenSource cts)
    {
        var buffer = new byte[64 * 1024];
        var msgBuf = new MemoryStream();

        try
        {
            while (!cts.IsCancellationRequested && source.State == WebSocketState.Open && dest.State == WebSocketState.Open)
            {
                var result = await source.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                msgBuf.Write(buffer, 0, result.Count);

                if (result.EndOfMessage)
                {
                    var data = msgBuf.ToArray();
                    msgBuf.SetLength(0);

                    Decode(data, result.MessageType, direction, sessionId, host, hubPath);
                    await dest.SendAsync(data, result.MessageType, true, cts.Token);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }

    private void Decode(byte[] data, WebSocketMessageType frameType, MessageDirection direction, string sessionId, string host, string hubPath)
    {
        try
        {
            IEnumerable<BlazorMessage> messages = frameType == WebSocketMessageType.Binary
                ? _decoder.DecodeMessagePackMessages(data, direction)
                : _decoder.DecodeMessages(Encoding.UTF8.GetString(data), direction);

            foreach (var msg in messages)
            {
                msg.SessionId = sessionId;
                msg.Host = host;
                msg.HubPath = hubPath;
                msg.Transport = "WebSocket";
                if (frameType == WebSocketMessageType.Binary) msg.RawBinaryPayload = data;
                _mitmProxy.RecordMessage(msg);
            }
        }
        catch
        {
            _mitmProxy.RecordMessage(new BlazorMessage
            {
                Direction = direction,
                SessionId = sessionId,
                Host = host,
                HubPath = hubPath,
                Transport = "WebSocket",
                MessageType = BlazorMessageType.Unknown,
                RawPayload = frameType == WebSocketMessageType.Binary
                    ? $"(binary {data.Length} bytes)" : Encoding.UTF8.GetString(data),
                RawBinaryPayload = frameType == WebSocketMessageType.Binary ? data : null
            });
        }
    }

    private static async Task CloseWs(WebSocket ws)
    {
        try
        {
            if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null,
                    new CancellationTokenSource(5000).Token);
        }
        catch { }
    }
}
