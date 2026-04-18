using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlazeCannon.Api.Services;
using BlazeCannon.Core.Interfaces;
using BlazeCannon.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace BlazeCannon.Api.Controllers;

[ApiController]
[Route("api/traffic")]
public class TrafficController : ControllerBase
{
    private readonly IMitmProxy _proxy;

    public TrafficController(IMitmProxy proxy)
    {
        _proxy = proxy;
    }

    [HttpGet]
    public IActionResult Get(
        [FromQuery] string? direction,
        [FromQuery] string? type,
        [FromQuery] string? sessionId,
        [FromQuery] string? search,
        [FromQuery] int? limit)
    {
        var query = BuildQuery(direction, type, sessionId, search, limit, out var error);
        if (error is not null) return BadRequest(new { error });

        var filtered = TrafficFilter.Apply(_proxy.CapturedMessages, query!);
        return Ok(filtered);
    }

    [HttpDelete]
    public IActionResult Clear()
    {
        _proxy.ClearMessages();
        return NoContent();
    }

    [HttpGet("export")]
    public IActionResult Export(
        [FromQuery] string format = "json",
        [FromQuery] string? direction = null,
        [FromQuery] string? type = null,
        [FromQuery] string? sessionId = null,
        [FromQuery] string? search = null,
        [FromQuery] int? limit = null)
    {
        var query = BuildQuery(direction, type, sessionId, search, limit, out var error);
        if (error is not null) return BadRequest(new { error });

        var filtered = TrafficFilter.Apply(_proxy.CapturedMessages, query!);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        format = (format ?? "json").Trim().ToLowerInvariant();
        switch (format)
        {
            case "json":
            {
                var opts = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                    Converters = { new JsonStringEnumConverter() }
                };
                var bytes = JsonSerializer.SerializeToUtf8Bytes(filtered, opts);
                return File(bytes, "application/json", $"blazecannon-traffic-{timestamp}.json");
            }
            case "csv":
            {
                var csv = BuildCsv(filtered);
                var bytes = Encoding.UTF8.GetBytes(csv);
                return File(bytes, "text/csv", $"blazecannon-traffic-{timestamp}.csv");
            }
            default:
                return BadRequest(new { error = $"Unknown format '{format}'. Use 'json' or 'csv'." });
        }
    }

    private static TrafficQuery? BuildQuery(
        string? direction, string? type, string? sessionId, string? search, int? limit, out string? error)
    {
        error = null;
        var q = new TrafficQuery
        {
            SessionId = sessionId,
            Search = search,
            Limit = limit.GetValueOrDefault(500)
        };

        if (!string.IsNullOrEmpty(direction))
        {
            if (!Enum.TryParse<MessageDirection>(direction, ignoreCase: true, out var d))
            {
                error = $"Unknown direction '{direction}'. Expected ClientToServer or ServerToClient.";
                return null;
            }
            q.Direction = d;
        }

        if (!string.IsNullOrEmpty(type))
        {
            if (!Enum.TryParse<BlazorMessageType>(type, ignoreCase: true, out var t))
            {
                error = $"Unknown type '{type}'.";
                return null;
            }
            q.Type = t;
        }

        return q;
    }

    private static string BuildCsv(IEnumerable<BlazorMessage> messages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,Direction,Type,HubMethod,Host,HubPath,Transport,SessionId,InvocationId,SequenceId,PayloadLength,RawPayload");
        foreach (var m in messages)
        {
            int payloadLen = m.RawBinaryPayload?.Length ?? (m.RawPayload?.Length ?? 0);
            sb.Append(m.Timestamp.ToString("o", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(m.Direction).Append(',');
            sb.Append(m.MessageType).Append(',');
            sb.Append(Csv(m.HubMethod)).Append(',');
            sb.Append(Csv(m.Host)).Append(',');
            sb.Append(Csv(m.HubPath)).Append(',');
            sb.Append(Csv(m.Transport)).Append(',');
            sb.Append(Csv(m.SessionId)).Append(',');
            sb.Append(Csv(m.InvocationId)).Append(',');
            sb.Append(m.SequenceId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            sb.Append(payloadLen.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(Csv(m.RawPayload));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var needsQuote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
        if (!needsQuote) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
