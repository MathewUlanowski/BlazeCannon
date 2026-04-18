using System.Text.Json;
using BlazeCannon.Core.Models;

namespace BlazeCannon.Proxy;

public class TrafficRecorder
{
    private readonly List<BlazorMessage> _messages = new();
    private bool _isRecording = true;

    public IReadOnlyList<BlazorMessage> Messages => _messages.AsReadOnly();
    public bool IsRecording => _isRecording;

    public void Record(BlazorMessage message)
    {
        if (_isRecording) _messages.Add(message);
    }

    public void Pause() => _isRecording = false;
    public void Resume() => _isRecording = true;
    public void Clear() => _messages.Clear();

    public IEnumerable<BlazorMessage> Filter(
        MessageDirection? direction = null,
        BlazorMessageType? type = null,
        DateTime? from = null,
        DateTime? to = null,
        string? payloadSearch = null)
    {
        var query = _messages.AsEnumerable();
        if (direction.HasValue) query = query.Where(m => m.Direction == direction.Value);
        if (type.HasValue) query = query.Where(m => m.MessageType == type.Value);
        if (from.HasValue) query = query.Where(m => m.Timestamp >= from.Value);
        if (to.HasValue) query = query.Where(m => m.Timestamp <= to.Value);
        if (!string.IsNullOrEmpty(payloadSearch))
            query = query.Where(m => m.RawPayload.Contains(payloadSearch, StringComparison.OrdinalIgnoreCase));
        return query;
    }

    public string ExportToJson()
    {
        return JsonSerializer.Serialize(_messages, new JsonSerializerOptions { WriteIndented = true });
    }

    public void ImportFromJson(string json)
    {
        var messages = JsonSerializer.Deserialize<List<BlazorMessage>>(json);
        if (messages != null) _messages.AddRange(messages);
    }
}
