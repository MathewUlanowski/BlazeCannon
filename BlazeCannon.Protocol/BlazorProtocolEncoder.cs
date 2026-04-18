using System.Buffers;
using System.Text.Json;
using BlazeCannon.Core.Interfaces;
using MessagePack;

namespace BlazeCannon.Protocol;

public class BlazorProtocolEncoder : IProtocolEncoder
{
    private const char RecordSeparator = '\x1e';

    public string EncodeDispatchBrowserEvent(ulong eventHandlerId, string eventType, string eventArgsJson)
    {
        var message = new
        {
            type = 1,
            target = "DispatchBrowserEvent",
            arguments = new object[]
            {
                eventHandlerId,
                eventType,
                eventArgsJson
            }
        };
        return JsonSerializer.Serialize(message) + RecordSeparator;
    }

    public string EncodeOnLocationChanged(string uri, string? state, bool intercepted)
    {
        var message = new
        {
            type = 1,
            target = "OnLocationChanged",
            arguments = new object?[] { uri, state, intercepted }
        };
        return JsonSerializer.Serialize(message) + RecordSeparator;
    }

    public string EncodeOnRenderCompleted(long batchId, string? errorMessageOrNull)
    {
        var message = new
        {
            type = 1,
            target = "OnRenderCompleted",
            arguments = new object?[] { batchId, errorMessageOrNull }
        };
        return JsonSerializer.Serialize(message) + RecordSeparator;
    }

    public string EncodeBeginInvokeDotNet(string callId, string assemblyName, string methodIdentifier, long dotNetObjectId, string argsJson)
    {
        var message = new
        {
            type = 1,
            target = "BeginInvokeDotNetFromJS",
            arguments = new object[] { callId, assemblyName, methodIdentifier, dotNetObjectId, argsJson }
        };
        return JsonSerializer.Serialize(message) + RecordSeparator;
    }

    public string EncodeHandshake(string protocol = "json", int version = 1)
    {
        var message = new { protocol, version };
        return JsonSerializer.Serialize(message) + RecordSeparator;
    }

    /// <summary>
    /// Build the JSON-text SignalR Invocation frame: {"type":1,...}\x1E.
    /// Arguments are serialized as-is; if they come from a JsonElement they will
    /// preserve their original JSON shape.
    /// </summary>
    public string EncodeInvocationText(string target, string? invocationId, IReadOnlyList<object?> arguments)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (arguments is null) throw new ArgumentNullException(nameof(arguments));

        // Build a manual JSON object so we can omit invocationId when absent and
        // handle JsonElement arguments verbatim.
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteNumber("type", 1);
            if (!string.IsNullOrEmpty(invocationId))
                writer.WriteString("invocationId", invocationId);
            writer.WriteString("target", target);
            writer.WritePropertyName("arguments");
            writer.WriteStartArray();
            foreach (var arg in arguments)
                WriteJsonValue(writer, arg);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        var json = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        return json + RecordSeparator;
    }

    /// <summary>
    /// Build the blazorpack (SignalR MessagePack) Invocation frame:
    /// [VarInt length][MessagePack array of (type, headers, invocationId, target, arguments, streamIds)].
    /// </summary>
    public byte[] EncodeInvocationMessagePack(string target, string? invocationId, IReadOnlyList<object?> arguments)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (arguments is null) throw new ArgumentNullException(nameof(arguments));

        // Serialize the inner MessagePack array first so we can prefix its length.
        var inner = new ArrayBufferWriter<byte>(initialCapacity: 256);
        var writer = new MessagePackWriter(inner);

        // 6-element array: [type, headers, invocationId, target, arguments, streamIds]
        writer.WriteArrayHeader(6);

        // 1. type = 1 (Invocation)
        writer.Write(1);

        // 2. headers — empty map
        writer.WriteMapHeader(0);

        // 3. invocationId — string or nil
        if (string.IsNullOrEmpty(invocationId))
            writer.WriteNil();
        else
            writer.Write(invocationId);

        // 4. target
        writer.Write(target);

        // 5. arguments — array
        writer.WriteArrayHeader(arguments.Count);
        foreach (var arg in arguments)
            WriteMessagePackValue(ref writer, arg);

        // 6. streamIds — empty array (no streaming support in this scope)
        writer.WriteArrayHeader(0);

        writer.Flush();

        var innerBytes = inner.WrittenSpan.ToArray();

        // Prefix with VarInt length
        Span<byte> varIntBuf = stackalloc byte[5];
        int varIntLen = WriteVarInt(varIntBuf, innerBytes.Length);

        var framed = new byte[varIntLen + innerBytes.Length];
        varIntBuf.Slice(0, varIntLen).CopyTo(framed);
        Buffer.BlockCopy(innerBytes, 0, framed, varIntLen, innerBytes.Length);
        return framed;
    }

    // ---------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------

    private static int WriteVarInt(Span<byte> dest, int value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        int i = 0;
        uint v = (uint)value;
        while (v >= 0x80)
        {
            dest[i++] = (byte)(v | 0x80);
            v >>= 7;
        }
        dest[i++] = (byte)v;
        return i;
    }

    /// <summary>
    /// Reverses <see cref="BlazorProtocolDecoder.ReadMessagePackValue"/>.
    /// Accepts JsonElement, IDictionary&lt;string,object?&gt; (map), IList&lt;object?&gt; (array),
    /// and primitive types.
    /// </summary>
    private static void WriteMessagePackValue(ref MessagePackWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNil();
                return;
            case JsonElement je:
                WriteJsonElementAsMsgPack(ref writer, je);
                return;
            case bool b:
                writer.Write(b);
                return;
            case string s:
                writer.Write(s);
                return;
            case byte[] bytes:
                writer.Write(bytes);
                return;
            case sbyte sb: writer.Write(sb); return;
            case short sh: writer.Write(sh); return;
            case int i: writer.Write(i); return;
            case long l: writer.Write(l); return;
            case byte by: writer.Write(by); return;
            case ushort us: writer.Write(us); return;
            case uint ui: writer.Write(ui); return;
            case ulong ul: writer.Write(ul); return;
            case float f: writer.Write(f); return;
            case double d: writer.Write(d); return;
            case decimal dec: writer.Write((double)dec); return;
            case IDictionary<string, object?> map:
                writer.WriteMapHeader(map.Count);
                foreach (var kv in map)
                {
                    writer.Write(kv.Key);
                    WriteMessagePackValue(ref writer, kv.Value);
                }
                return;
            case IList<object?> list:
                writer.WriteArrayHeader(list.Count);
                foreach (var item in list)
                    WriteMessagePackValue(ref writer, item);
                return;
        }

        throw new InvalidOperationException(
            $"Unsupported argument type for MessagePack encoding: {value.GetType().FullName}");
    }

    private static void WriteJsonElementAsMsgPack(ref MessagePackWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNil();
                return;
            case JsonValueKind.True:
                writer.Write(true);
                return;
            case JsonValueKind.False:
                writer.Write(false);
                return;
            case JsonValueKind.String:
                writer.Write(element.GetString());
                return;
            case JsonValueKind.Number:
                if (element.TryGetInt64(out long lv))
                    writer.Write(lv);
                else if (element.TryGetUInt64(out ulong uv))
                    writer.Write(uv);
                else
                    writer.Write(element.GetDouble());
                return;
            case JsonValueKind.Array:
                writer.WriteArrayHeader(element.GetArrayLength());
                foreach (var item in element.EnumerateArray())
                    WriteJsonElementAsMsgPack(ref writer, item);
                return;
            case JsonValueKind.Object:
                var propCount = 0;
                foreach (var _ in element.EnumerateObject()) propCount++;
                writer.WriteMapHeader(propCount);
                foreach (var prop in element.EnumerateObject())
                {
                    writer.Write(prop.Name);
                    WriteJsonElementAsMsgPack(ref writer, prop.Value);
                }
                return;
            default:
                writer.WriteNil();
                return;
        }
    }

    private static void WriteJsonValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                return;
            case JsonElement je:
                je.WriteTo(writer);
                return;
            case bool b:
                writer.WriteBooleanValue(b);
                return;
            case string s:
                writer.WriteStringValue(s);
                return;
            case sbyte sb: writer.WriteNumberValue(sb); return;
            case short sh: writer.WriteNumberValue(sh); return;
            case int i: writer.WriteNumberValue(i); return;
            case long l: writer.WriteNumberValue(l); return;
            case byte by: writer.WriteNumberValue(by); return;
            case ushort us: writer.WriteNumberValue(us); return;
            case uint ui: writer.WriteNumberValue(ui); return;
            case ulong ul: writer.WriteNumberValue(ul); return;
            case float f: writer.WriteNumberValue(f); return;
            case double d: writer.WriteNumberValue(d); return;
            case decimal dec: writer.WriteNumberValue(dec); return;
            case IDictionary<string, object?> map:
                writer.WriteStartObject();
                foreach (var kv in map)
                {
                    writer.WritePropertyName(kv.Key);
                    WriteJsonValue(writer, kv.Value);
                }
                writer.WriteEndObject();
                return;
            case IList<object?> list:
                writer.WriteStartArray();
                foreach (var item in list)
                    WriteJsonValue(writer, item);
                writer.WriteEndArray();
                return;
            default:
                JsonSerializer.Serialize(writer, value);
                return;
        }
    }
}
