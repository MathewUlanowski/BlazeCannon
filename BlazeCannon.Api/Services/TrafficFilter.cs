using BlazeCannon.Core.Models;

namespace BlazeCannon.Api.Services;

public class TrafficQuery
{
    public MessageDirection? Direction { get; set; }
    public BlazorMessageType? Type { get; set; }
    public string? SessionId { get; set; }
    public string? Search { get; set; }
    public int Limit { get; set; } = 500;
}

public static class TrafficFilter
{
    public static IReadOnlyList<BlazorMessage> Apply(IReadOnlyList<BlazorMessage> source, TrafficQuery q)
    {
        IEnumerable<BlazorMessage> seq = source;

        if (q.Direction is { } dir)
            seq = seq.Where(m => m.Direction == dir);

        if (q.Type is { } type)
            seq = seq.Where(m => m.MessageType == type);

        if (!string.IsNullOrEmpty(q.SessionId))
            seq = seq.Where(m => string.Equals(m.SessionId, q.SessionId, StringComparison.Ordinal));

        if (!string.IsNullOrEmpty(q.Search))
        {
            var needle = q.Search;
            seq = seq.Where(m =>
                (m.RawPayload?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (m.HubMethod?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (m.Host?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (m.HubPath?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var list = seq.ToList();
        if (q.Limit > 0 && list.Count > q.Limit)
        {
            // take the last N
            list = list.Skip(list.Count - q.Limit).ToList();
        }
        return list;
    }
}
