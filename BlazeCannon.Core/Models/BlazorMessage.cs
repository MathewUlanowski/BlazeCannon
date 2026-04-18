namespace BlazeCannon.Core.Models;

public class BlazorMessage
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public MessageDirection Direction { get; set; }
    public BlazorMessageType MessageType { get; set; }
    public string? HubMethod { get; set; }
    public string RawPayload { get; set; } = string.Empty;
    public object?[]? DecodedArguments { get; set; }
    public int? SequenceId { get; set; }
    public string? InvocationId { get; set; }
}

public enum MessageDirection { ClientToServer, ServerToClient }

public enum BlazorMessageType
{
    Handshake,
    Invocation,
    StreamItem,
    Completion,
    StreamInvocation,
    CancelInvocation,
    Ping,
    Close,
    DispatchBrowserEvent,
    RenderBatch,
    OnRenderCompleted,
    OnLocationChanged,
    BeginInvokeDotNet,
    EndInvokeJS,
    ReceiveByteArray,
    AttachComponent,
    Unknown
}
