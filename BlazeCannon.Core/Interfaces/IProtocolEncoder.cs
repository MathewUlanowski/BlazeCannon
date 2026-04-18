namespace BlazeCannon.Core.Interfaces;

public interface IProtocolEncoder
{
    string EncodeDispatchBrowserEvent(ulong eventHandlerId, string eventType, string eventArgsJson);
    string EncodeOnLocationChanged(string uri, string? state, bool intercepted);
    string EncodeOnRenderCompleted(long batchId, string? errorMessageOrNull);
    string EncodeBeginInvokeDotNet(string callId, string assemblyName, string methodIdentifier, long dotNetObjectId, string argsJson);
    string EncodeHandshake(string protocol = "json", int version = 1);

    /// <summary>
    /// Encode a SignalR Invocation (type=1) as a JSON-text-protocol frame terminated with 0x1E.
    /// Arguments may be arbitrary JSON values (primitives, arrays, objects).
    /// </summary>
    string EncodeInvocationText(string target, string? invocationId, IReadOnlyList<object?> arguments);

    /// <summary>
    /// Encode a SignalR Invocation (type=1) as a MessagePack ("blazorpack") binary frame:
    /// [VarInt length][MessagePack array of (type, headers, invocationId, target, arguments, streamIds)].
    /// </summary>
    byte[] EncodeInvocationMessagePack(string target, string? invocationId, IReadOnlyList<object?> arguments);
}
