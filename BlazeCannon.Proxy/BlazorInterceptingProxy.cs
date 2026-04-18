using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BlazeCannon.Core.Interfaces;
using BlazeCannon.Core.Models;
using BlazeCannon.Protocol;
using Microsoft.Extensions.Logging;

namespace BlazeCannon.Proxy;

public class BlazorInterceptingProxy : ITrafficProxy
{
    private readonly IProtocolDecoder _decoder;
    private readonly ILogger<BlazorInterceptingProxy> _logger;
    private readonly BlazorProtocolEncoder _encoder = new();
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _receiveCts;
    private readonly List<BlazorMessage> _capturedMessages = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public event Action<BlazorMessage>? OnMessageIntercepted;
    public bool IsConnected => _webSocket?.State == WebSocketState.Open;
    public IReadOnlyList<BlazorMessage> CapturedMessages => _capturedMessages.AsReadOnly();

    public BlazorInterceptingProxy(IProtocolDecoder decoder, ILogger<BlazorInterceptingProxy> logger)
    {
        _decoder = decoder;
        _logger = logger;
    }

    public async Task ConnectAsync(TargetConfig config, CancellationToken ct = default)
    {
        // Step 1: Negotiate
        var negotiateUrl = $"{config.BaseUrl}{config.BlazorHubPath}/negotiate?negotiateVersion=1";
        using var httpClient = new HttpClient();
        httpClient.Timeout = config.ConnectionTimeout;

        foreach (var header in config.CustomHeaders)
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        if (!string.IsNullOrEmpty(config.AuthCookie))
            httpClient.DefaultRequestHeaders.Add("Cookie", config.AuthCookie);

        _logger.LogInformation("Negotiating with {Url}", negotiateUrl);
        var negotiateResponse = await httpClient.PostAsync(negotiateUrl, null, ct);
        negotiateResponse.EnsureSuccessStatusCode();

        var negotiateJson = await negotiateResponse.Content.ReadAsStringAsync(ct);
        _logger.LogDebug("Negotiate response: {Response}", negotiateJson);

        using var negotiateDoc = JsonDocument.Parse(negotiateJson);
        var connectionToken = negotiateDoc.RootElement.GetProperty("connectionToken").GetString()
            ?? throw new InvalidOperationException("No connectionToken in negotiate response");

        // Step 2: WebSocket connect
        var wsScheme = config.BaseUrl.StartsWith("https") ? "wss" : "ws";
        var baseHost = config.BaseUrl.Replace("https://", "").Replace("http://", "");
        var wsUrl = $"{wsScheme}://{baseHost}{config.BlazorHubPath}?id={connectionToken}";

        _webSocket = new ClientWebSocket();
        foreach (var header in config.CustomHeaders)
            _webSocket.Options.SetRequestHeader(header.Key, header.Value);
        if (!string.IsNullOrEmpty(config.AuthCookie))
            _webSocket.Options.SetRequestHeader("Cookie", config.AuthCookie);

        _logger.LogInformation("Connecting WebSocket to {Url}", wsUrl);
        await _webSocket.ConnectAsync(new Uri(wsUrl), ct);
        _logger.LogInformation("WebSocket connected");

        // Step 3: SignalR handshake
        var encoder = new BlazorProtocolEncoder();
        var handshake = encoder.EncodeHandshake();
        await SendRawAsync(handshake, ct);

        // Read handshake response
        var response = await ReceiveRawAsync(ct);
        var handshakeMsg = new BlazorMessage
        {
            Direction = MessageDirection.ServerToClient,
            MessageType = BlazorMessageType.Handshake,
            RawPayload = response
        };
        RecordMessage(handshakeMsg);

        // Step 4: Start receive loop
        _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = Task.Run(() => ReceiveLoop(_receiveCts.Token), _receiveCts.Token);
    }

    public async Task SendToServerAsync(string rawMessage, CancellationToken ct = default)
    {
        if (_webSocket?.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket is not connected");

        // Decode and record outgoing message
        foreach (var msg in _decoder.DecodeMessages(rawMessage, MessageDirection.ClientToServer))
            RecordMessage(msg);

        await SendRawLockedAsync(rawMessage, ct);
    }

    public async Task DisconnectAsync()
    {
        _receiveCts?.Cancel();
        if (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during WebSocket close");
            }
        }
        _webSocket?.Dispose();
        _webSocket = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        GC.SuppressFinalize(this);
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[1024 * 64];
        var messageBuffer = new StringBuilder();

        while (!ct.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
        {
            try
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Server closed WebSocket connection");
                    break;
                }

                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                messageBuffer.Append(text);

                if (result.EndOfMessage)
                {
                    var fullMessage = messageBuffer.ToString();
                    messageBuffer.Clear();

                    foreach (var msg in _decoder.DecodeMessages(fullMessage, MessageDirection.ServerToClient))
                    {
                        RecordMessage(msg);
                        await HandleAutoResponseAsync(msg, ct);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (WebSocketException ex)
            {
                _logger.LogError(ex, "WebSocket receive error");
                break;
            }
        }
    }

    /// <summary>
    /// Blazor Server requires the client to acknowledge render batches via OnRenderCompleted
    /// and respond to pings. Without this, the server times out and closes the circuit.
    /// </summary>
    private async Task HandleAutoResponseAsync(BlazorMessage msg, CancellationToken ct)
    {
        try
        {
            if (msg.MessageType == BlazorMessageType.RenderBatch)
            {
                // Extract batch ID from the invocation arguments
                // JS.RenderBatch args: [batchId, base64RenderBatchData]
                long batchId = 0;
                if (msg.DecodedArguments is { Length: > 0 })
                {
                    batchId = msg.DecodedArguments[0] switch
                    {
                        long l => l,
                        int i => i,
                        string s when long.TryParse(s, out var parsed) => parsed,
                        JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt64(),
                        _ => 0
                    };
                }

                // Also try parsing from raw payload if arguments didn't work
                if (batchId == 0)
                {
                    batchId = ExtractBatchIdFromRaw(msg.RawPayload);
                }

                var ack = _encoder.EncodeOnRenderCompleted(batchId, null);
                _logger.LogDebug("Auto-acknowledging render batch {BatchId}", batchId);
                await SendRawLockedAsync(ack, ct);
            }
            else if (msg.MessageType == BlazorMessageType.Ping)
            {
                // Respond to server pings to keep the connection alive
                var pong = JsonSerializer.Serialize(new { type = 6 }) + "\x1e";
                await SendRawLockedAsync(pong, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send auto-response for {Type}", msg.MessageType);
        }
    }

    private static long ExtractBatchIdFromRaw(string rawPayload)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawPayload.TrimEnd('\x1e'));
            if (doc.RootElement.TryGetProperty("arguments", out var args) &&
                args.ValueKind == JsonValueKind.Array &&
                args.GetArrayLength() > 0)
            {
                return args[0].GetInt64();
            }
        }
        catch { }
        return 0;
    }

    private async Task SendRawLockedAsync(string message, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            if (_webSocket?.State == WebSocketState.Open)
                await SendRawAsync(message, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private void RecordMessage(BlazorMessage message)
    {
        _capturedMessages.Add(message);
        OnMessageIntercepted?.Invoke(message);
    }

    private async Task SendRawAsync(string message, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await _webSocket!.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private async Task<string> ReceiveRawAsync(CancellationToken ct)
    {
        var buffer = new byte[1024 * 16];
        var sb = new StringBuilder();
        WebSocketReceiveResult result;
        do
        {
            result = await _webSocket!.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        } while (!result.EndOfMessage);
        return sb.ToString();
    }
}
