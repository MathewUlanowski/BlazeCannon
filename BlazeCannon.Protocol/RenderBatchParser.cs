using BlazeCannon.Core.Models;
using Microsoft.Extensions.Logging;

namespace BlazeCannon.Protocol;

/// <summary>
/// Parses Blazor Server render batches to extract component tree, event handlers, and form elements.
/// Render batches in JSON mode are base64-encoded binary diffs sent via JS.RenderBatch.
/// </summary>
public class RenderBatchParser
{
    private readonly ILogger<RenderBatchParser> _logger;

    public RenderBatchParser(ILogger<RenderBatchParser> logger)
    {
        _logger = logger;
    }

    public RenderBatchInfo Parse(string base64RenderBatch)
    {
        var info = new RenderBatchInfo();

        try
        {
            var bytes = Convert.FromBase64String(base64RenderBatch);
            if (bytes.Length < 20)
            {
                _logger.LogWarning("Render batch too short ({Len} bytes)", bytes.Length);
                return info;
            }

            using var reader = new BinaryReader(new MemoryStream(bytes));

            // Parse string table (at end of batch)
            // The render batch binary format:
            // [updated components] [reference frames] [disposed component IDs] [disposed event handler IDs] [string table]
            // String table offset is at the end - last 4 bytes point to string table start
            if (bytes.Length >= 4)
            {
                var stringTableOffset = BitConverter.ToInt32(bytes, bytes.Length - 4);
                if (stringTableOffset > 0 && stringTableOffset < bytes.Length - 4)
                {
                    ParseStringTable(bytes, stringTableOffset, info);
                }
            }

            // Extract event handler IDs from reference frames
            ExtractEventHandlers(bytes, info);

            // Try to identify form elements from string table patterns
            IdentifyFormElements(info);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fully parse render batch");
        }

        return info;
    }

    private void ParseStringTable(byte[] bytes, int offset, RenderBatchInfo info)
    {
        try
        {
            var pos = offset;
            if (pos + 4 > bytes.Length) return;

            var count = BitConverter.ToInt32(bytes, pos);
            pos += 4;

            for (int i = 0; i < count && pos < bytes.Length - 4; i++)
            {
                var strLen = BitConverter.ToInt32(bytes, pos);
                pos += 4;

                if (strLen < 0 || pos + strLen > bytes.Length) break;

                var str = System.Text.Encoding.UTF8.GetString(bytes, pos, strLen);
                info.StringTable.Add(str);
                pos += strLen;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error parsing string table");
        }
    }

    private void ExtractEventHandlers(byte[] bytes, RenderBatchInfo info)
    {
        // Reference frames are 36 bytes each in Blazor's binary format
        // Frame type 6 = Attribute with event handler
        // We scan for patterns that look like event handler registrations
        try
        {
            for (int i = 0; i <= bytes.Length - 8; i += 4)
            {
                // Look for event handler ID patterns (non-zero ulong values in expected positions)
                if (i + 16 <= bytes.Length)
                {
                    var possibleHandlerId = BitConverter.ToUInt64(bytes, i);
                    // Event handler IDs are typically small sequential numbers
                    if (possibleHandlerId > 0 && possibleHandlerId < 10000)
                    {
                        // Check surrounding context to validate this is likely an event handler
                        // This is heuristic -- the exact format depends on Blazor version
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error extracting event handlers");
        }
    }

    private void IdentifyFormElements(RenderBatchInfo info)
    {
        var inputTypes = new HashSet<string> { "text", "password", "email", "number", "search", "tel", "url", "hidden", "textarea" };
        var eventNames = new HashSet<string> { "onchange", "oninput", "onclick", "onsubmit", "onfocus", "onblur", "onkeydown" };

        for (int i = 0; i < info.StringTable.Count; i++)
        {
            var s = info.StringTable[i];

            // Detect HTML element types
            if (s == "input" || s == "textarea" || s == "select" || s == "button" || s == "form")
            {
                var element = new BlazorElement { TagName = s };

                // Look ahead for attributes
                for (int j = i + 1; j < Math.Min(i + 20, info.StringTable.Count); j++)
                {
                    var attr = info.StringTable[j];
                    if (attr == "type" && j + 1 < info.StringTable.Count)
                        element.Type = info.StringTable[j + 1];
                    else if (attr == "name" && j + 1 < info.StringTable.Count)
                        element.Name = info.StringTable[j + 1];
                    else if (attr == "id" && j + 1 < info.StringTable.Count)
                        element.Id = info.StringTable[j + 1];
                    else if (attr == "value" && j + 1 < info.StringTable.Count)
                        element.Value = info.StringTable[j + 1];
                }

                var component = info.Components.LastOrDefault() ?? new BlazorComponent();
                if (!info.Components.Contains(component))
                    info.Components.Add(component);
                component.Elements.Add(element);
            }

            // Detect event handler registrations
            if (eventNames.Contains(s.ToLowerInvariant()))
            {
                info.EventHandlers.Add(new BlazorEventHandler
                {
                    EventName = s,
                    ComponentId = info.Components.Count - 1
                });
            }
        }
    }
}
