namespace BlazeCannon.Core.Models;

public class RenderBatchInfo
{
    public List<BlazorComponent> Components { get; set; } = new();
    public List<BlazorEventHandler> EventHandlers { get; set; } = new();
    public List<string> StringTable { get; set; } = new();
}

public class BlazorComponent
{
    public int ComponentId { get; set; }
    public string ComponentType { get; set; } = string.Empty;
    public List<BlazorElement> Elements { get; set; } = new();
}

public class BlazorElement
{
    public string TagName { get; set; } = string.Empty;
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Value { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = new();
    public ulong? EventHandlerId { get; set; }
}

public class BlazorEventHandler
{
    public ulong EventHandlerId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public int ComponentId { get; set; }
}
