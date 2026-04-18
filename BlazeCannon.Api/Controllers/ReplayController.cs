using System.Text.Json;
using BlazeCannon.Api.Services;
using BlazeCannon.Core.Interfaces;
using BlazeCannon.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace BlazeCannon.Api.Controllers;

[ApiController]
[Route("api/replay")]
public class ReplayController : ControllerBase
{
    private readonly IMitmProxy _proxy;
    private readonly IProtocolEncoder _encoder;
    private readonly ReplayStagingService _staging;
    private readonly ILogger<ReplayController> _logger;

    public ReplayController(
        IMitmProxy proxy,
        IProtocolEncoder encoder,
        ReplayStagingService staging,
        ILogger<ReplayController> logger)
    {
        _proxy = proxy;
        _encoder = encoder;
        _staging = staging;
        _logger = logger;
    }

    [HttpGet("staged")]
    public IActionResult GetStaged() => Ok(_staging.Staged);

    [HttpPost("stage")]
    public IActionResult Stage([FromBody] BlazorMessage message)
    {
        if (message is null) return BadRequest(new { error = "Message body required." });
        _staging.Stage(message);
        return Ok(_staging.Staged);
    }

    [HttpDelete("staged/{index:int}")]
    public IActionResult RemoveStaged(int index)
    {
        if (!_staging.Remove(index))
            return NotFound(new { error = $"No staged message at index {index}." });
        return NoContent();
    }

    [HttpDelete("staged")]
    public IActionResult ClearStaged()
    {
        _staging.Clear();
        return NoContent();
    }

    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] BlazorMessage message, CancellationToken ct)
    {
        if (message is null) return BadRequest(new { error = "Message body required." });

        try
        {
            await _proxy.ReplayMessageAsync(message, ct);
            return Ok(new { sentAt = DateTime.UtcNow, error = (string?)null });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Replay failed");
            return Ok(new { sentAt = (DateTime?)null, error = ex.Message });
        }
    }

    /// <summary>
    /// Encode a decoded Invocation (hub method + arguments) into the appropriate
    /// SignalR wire format (JSON-text or MessagePack) and replay it to the active session.
    /// </summary>
    [HttpPost("encode-and-send")]
    public async Task<IActionResult> EncodeAndSend(
        [FromBody] EncodeAndSendRequest? request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new { error = "Request body required." });

        // Scope guardrail: v0.5.0 supports Invocation only.
        if (!string.Equals(request.MessageType, "Invocation", StringComparison.Ordinal))
            return BadRequest(new
            {
                error = $"Unsupported messageType '{request.MessageType}'. Only 'Invocation' is supported in this release."
            });

        if (string.IsNullOrWhiteSpace(request.HubMethod))
            return BadRequest(new { error = "hubMethod is required." });

        if (request.Arguments.ValueKind != JsonValueKind.Array)
            return BadRequest(new { error = "arguments must be a JSON array." });

        if (_proxy.ActiveSessionCount == 0)
            return Conflict(new { error = "no active sessions" });

        // Materialize JsonElement args into an object?[] the encoder accepts.
        var args = new List<object?>();
        foreach (var el in request.Arguments.EnumerateArray())
            args.Add(el);

        byte[]? binary = null;
        string? text = null;

        try
        {
            if (request.UseMessagePack)
                binary = _encoder.EncodeInvocationMessagePack(request.HubMethod!, request.InvocationId, args);
            else
                text = _encoder.EncodeInvocationText(request.HubMethod!, request.InvocationId, args);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or JsonException)
        {
            _logger.LogWarning(ex, "Encode failed for hubMethod={Method}", request.HubMethod);
            return BadRequest(new { error = $"Failed to encode message: {ex.Message}" });
        }

        var message = new BlazorMessage
        {
            Direction = MessageDirection.ClientToServer,
            MessageType = BlazorMessageType.Invocation,
            HubMethod = request.HubMethod,
            InvocationId = request.InvocationId,
            SessionId = request.SessionId,
            RawBinaryPayload = binary,
            RawPayload = text ?? string.Empty,
            Timestamp = DateTime.UtcNow,
        };

        try
        {
            await _proxy.ReplayMessageAsync(message, ct);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Replay failed — no active session");
            return Conflict(new { error = "no active sessions" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "encode-and-send replay failed");
            return StatusCode(500, new { error = ex.Message });
        }

        int byteLength = binary?.Length ?? System.Text.Encoding.UTF8.GetByteCount(text ?? string.Empty);

        return Ok(new EncodeAndSendResponse
        {
            SentAt = DateTime.UtcNow,
            ByteLength = byteLength,
            WireFormatBase64 = binary is not null ? Convert.ToBase64String(binary) : null,
            RawText = text,
        });
    }
}

/// <summary>
/// Request body for POST /api/replay/encode-and-send.
/// </summary>
public class EncodeAndSendRequest
{
    public string MessageType { get; set; } = "Invocation";
    public string? HubMethod { get; set; }
    public string? InvocationId { get; set; }
    /// <summary>
    /// Raw JSON element — must be a JSON array.
    /// </summary>
    public JsonElement Arguments { get; set; }
    public bool UseMessagePack { get; set; }
    public string? SessionId { get; set; }
}

public class EncodeAndSendResponse
{
    public DateTime SentAt { get; set; }
    public int ByteLength { get; set; }
    public string? WireFormatBase64 { get; set; }
    public string? RawText { get; set; }
}
