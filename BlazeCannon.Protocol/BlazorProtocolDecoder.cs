using System.Buffers;
using System.Text;
using System.Text.Json;
using BlazeCannon.Core.Interfaces;
using BlazeCannon.Core.Models;
using MessagePack;
using Microsoft.Extensions.Logging;

namespace BlazeCannon.Protocol;

public class BlazorProtocolDecoder : IProtocolDecoder
{
    private const char RecordSeparator = '\x1e';
    private readonly ILogger<BlazorProtocolDecoder> _logger;

    private static readonly Dictionary<string, BlazorMessageType> ClientMethodMap = new()
    {
        ["DispatchBrowserEvent"] = BlazorMessageType.DispatchBrowserEvent,
        ["OnRenderCompleted"] = BlazorMessageType.OnRenderCompleted,
        ["OnLocationChanged"] = BlazorMessageType.OnLocationChanged,
        ["BeginInvokeDotNetFromJS"] = BlazorMessageType.BeginInvokeDotNet,
        ["EndInvokeJSFromDotNet"] = BlazorMessageType.EndInvokeJS,
        ["ReceiveByteArray"] = BlazorMessageType.ReceiveByteArray,
    };

    private static readonly Dictionary<string, BlazorMessageType> ServerMethodMap = new()
    {
        ["JS.BeginInvokeJS"] = BlazorMessageType.EndInvokeJS,
        ["JS.EndInvokeDotNet"] = BlazorMessageType.BeginInvokeDotNet,
        ["JS.RenderBatch"] = BlazorMessageType.RenderBatch,
        ["JS.Error"] = BlazorMessageType.Unknown,
        ["JS.AttachComponent"] = BlazorMessageType.AttachComponent,
    };

    public BlazorProtocolDecoder(ILogger<BlazorProtocolDecoder> logger)
    {
        _logger = logger;
    }

    public IEnumerable<BlazorMessage> DecodeMessages(string rawData, MessageDirection direction)
    {
        var messages = rawData.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var msg in messages)
        {
            BlazorMessage? decoded = null;
            try
            {
                decoded = DecodeSingleMessage(msg.Trim(), direction);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decode message: {Raw}", msg.Length > 200 ? msg[..200] : msg);
            }
            if (decoded != null) yield return decoded;
        }
    }

    public BlazorMessage DecodeSingleMessage(string rawMessage, MessageDirection direction)
    {
        var trimmed = rawMessage.TrimEnd(RecordSeparator);
        var message = new BlazorMessage
        {
            RawPayload = trimmed,
            Direction = direction,
            Timestamp = DateTime.UtcNow,
        };

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            // Handshake message (no "type" field, has "protocol")
            if (root.TryGetProperty("protocol", out _))
            {
                message.MessageType = BlazorMessageType.Handshake;
                return message;
            }

            // Empty handshake response {}
            if (!root.TryGetProperty("type", out var typeElement))
            {
                message.MessageType = BlazorMessageType.Handshake;
                return message;
            }

            int type = typeElement.GetInt32();
            message.MessageType = type switch
            {
                1 => BlazorMessageType.Invocation,
                2 => BlazorMessageType.StreamItem,
                3 => BlazorMessageType.Completion,
                4 => BlazorMessageType.StreamInvocation,
                5 => BlazorMessageType.CancelInvocation,
                6 => BlazorMessageType.Ping,
                7 => BlazorMessageType.Close,
                _ => BlazorMessageType.Unknown
            };

            if (root.TryGetProperty("invocationId", out var invId))
                message.InvocationId = invId.GetString();

            // For invocation messages, extract the target method
            if (type == 1 && root.TryGetProperty("target", out var target))
            {
                message.HubMethod = target.GetString();
                var methodMap = direction == MessageDirection.ClientToServer ? ClientMethodMap : ServerMethodMap;
                if (message.HubMethod != null && methodMap.TryGetValue(message.HubMethod, out var blazorType))
                    message.MessageType = blazorType;

                if (root.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Array)
                {
                    var argList = new List<object?>();
                    foreach (var arg in args.EnumerateArray())
                    {
                        argList.Add(arg.ValueKind switch
                        {
                            JsonValueKind.String => arg.GetString(),
                            JsonValueKind.Number => arg.GetInt64(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.Null => null,
                            _ => arg.GetRawText()
                        });
                    }
                    message.DecodedArguments = argList.ToArray();
                }
            }
        }
        catch (JsonException)
        {
            // Might be MessagePack data passed as string — try binary decode
            try
            {
                var bytes = Encoding.Latin1.GetBytes(trimmed);
                var packMessages = DecodeMessagePackMessages(bytes, direction).ToList();
                if (packMessages.Count > 0)
                    return packMessages[0];
            }
            catch { }

            message.MessageType = BlazorMessageType.Unknown;
            _logger.LogDebug("Non-JSON message received: {Msg}", trimmed.Length > 100 ? trimmed[..100] : trimmed);
        }

        return message;
    }

    /// <summary>
    /// Decodes SignalR MessagePack (blazorpack) protocol frames.
    /// Wire format: [VarInt length][MessagePack array] repeated.
    /// Invocation array: [type, headers, invocationId, target, arguments, streamIds]
    /// </summary>
    public IEnumerable<BlazorMessage> DecodeMessagePackMessages(byte[] data, MessageDirection direction)
    {
        int offset = 0;
        while (offset < data.Length)
        {
            BlazorMessage? msg = null;
            try
            {
                // Read VarInt length prefix
                int msgLen = ReadVarInt(data, ref offset);
                if (msgLen <= 0 || offset + msgLen > data.Length)
                    break;

                var msgBytes = data.AsMemory(offset, msgLen);
                offset += msgLen;

                msg = DecodeSingleMessagePack(msgBytes, direction);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to decode MessagePack frame at offset {Offset}", offset);
                break;
            }

            if (msg != null)
                yield return msg;
        }
    }

    private BlazorMessage DecodeSingleMessagePack(ReadOnlyMemory<byte> data, MessageDirection direction)
    {
        var reader = new MessagePackReader(data);
        var message = new BlazorMessage
        {
            Direction = direction,
            Timestamp = DateTime.UtcNow,
        };

        int arrayLen = reader.ReadArrayHeader();
        if (arrayLen == 0)
        {
            message.MessageType = BlazorMessageType.Unknown;
            message.RawPayload = "(empty MessagePack array)";
            return message;
        }

        // First element is always the message type
        int type = reader.ReadInt32();
        message.MessageType = type switch
        {
            1 => BlazorMessageType.Invocation,
            2 => BlazorMessageType.StreamItem,
            3 => BlazorMessageType.Completion,
            4 => BlazorMessageType.StreamInvocation,
            5 => BlazorMessageType.CancelInvocation,
            6 => BlazorMessageType.Ping,
            7 => BlazorMessageType.Close,
            _ => BlazorMessageType.Unknown
        };

        if (type == 6) // Ping
        {
            message.RawPayload = "[Ping]";
            return message;
        }

        if (arrayLen < 4)
        {
            message.RawPayload = $"[MessagePack type={type}, fields={arrayLen}]";
            return message;
        }

        // Element 2: Headers (map) — skip
        reader.Skip();

        // Element 3: InvocationId (string or nil)
        if (reader.TryReadNil())
        {
            message.InvocationId = null;
        }
        else
        {
            message.InvocationId = reader.ReadString();
        }

        if (type == 1 && arrayLen >= 5) // Invocation
        {
            // Element 4: Target method name
            message.HubMethod = reader.ReadString();

            // Map to Blazor-specific message types
            var methodMap = direction == MessageDirection.ClientToServer ? ClientMethodMap : ServerMethodMap;
            if (message.HubMethod != null && methodMap.TryGetValue(message.HubMethod, out var blazorType))
                message.MessageType = blazorType;

            // Element 5: Arguments array
            if (arrayLen >= 5)
            {
                try
                {
                    int argCount = reader.ReadArrayHeader();
                    var args = new List<object?>();
                    for (int i = 0; i < argCount; i++)
                    {
                        args.Add(ReadMessagePackValue(ref reader));
                    }
                    message.DecodedArguments = args.ToArray();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to read arguments for {Method}", message.HubMethod);
                }
            }

            // Build a readable raw payload summary
            var sb = new StringBuilder();
            sb.Append($"[{message.HubMethod}]");
            if (message.DecodedArguments != null)
            {
                sb.Append(" args: ");
                foreach (var arg in message.DecodedArguments)
                {
                    var argStr = arg?.ToString() ?? "null";
                    sb.Append(argStr.Length > 100 ? argStr[..100] + "..." : argStr);
                    sb.Append(", ");
                }
            }
            message.RawPayload = sb.ToString().TrimEnd(',', ' ');
        }
        else if (type == 3 && arrayLen >= 4) // Completion
        {
            message.RawPayload = $"[Completion invocationId={message.InvocationId}]";
        }
        else
        {
            message.RawPayload = $"[MessagePack type={type}, method={message.HubMethod}]";
        }

        return message;
    }

    private static object? ReadMessagePackValue(ref MessagePackReader reader)
    {
        if (reader.End)
            return null;
        var nextType = reader.NextMessagePackType;

        switch (nextType)
        {
            case MessagePackType.Nil:
                reader.ReadNil();
                return null;
            case MessagePackType.Boolean:
                return reader.ReadBoolean();
            case MessagePackType.Integer:
                // Try to read as various int sizes
                try { return reader.ReadInt64(); }
                catch { return reader.ReadUInt64(); }
            case MessagePackType.Float:
                return reader.ReadDouble();
            case MessagePackType.String:
                return reader.ReadString();
            case MessagePackType.Binary:
                var binSeq = reader.ReadBytes();
                if (binSeq == null) return "(null bytes)";
                int binLen = (int)binSeq.Value.Length;
                var byteArr = new byte[binLen];
                binSeq.Value.CopyTo(byteArr.AsSpan());
                return binLen > 200
                    ? $"(binary {binLen} bytes)"
                    : Convert.ToBase64String(byteArr);
            case MessagePackType.Array:
                int arrLen = reader.ReadArrayHeader();
                var list = new List<object?>();
                for (int i = 0; i < arrLen; i++)
                    list.Add(ReadMessagePackValue(ref reader));
                return $"[{string.Join(", ", list.Select(x => x?.ToString() ?? "null"))}]";
            case MessagePackType.Map:
                int mapLen = reader.ReadMapHeader();
                var dict = new Dictionary<string, string>();
                for (int i = 0; i < mapLen; i++)
                {
                    var key = ReadMessagePackValue(ref reader)?.ToString() ?? "?";
                    var val = ReadMessagePackValue(ref reader)?.ToString() ?? "null";
                    dict[key] = val;
                }
                return $"{{{string.Join(", ", dict.Select(kv => $"{kv.Key}:{kv.Value}"))}}}";
            case MessagePackType.Extension:
                var ext = reader.ReadExtensionFormat();
                return $"(ext type={ext.TypeCode}, {ext.Data.Length} bytes)";
            default:
                reader.Skip();
                return "(skipped)";
        }
    }

    private static int ReadVarInt(byte[] data, ref int offset)
    {
        int result = 0;
        int shift = 0;
        while (offset < data.Length)
        {
            byte b = data[offset++];
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;
            shift += 7;
            if (shift >= 35)
                throw new FormatException("VarInt too long");
        }
        throw new FormatException("Unexpected end of data reading VarInt");
    }
}
