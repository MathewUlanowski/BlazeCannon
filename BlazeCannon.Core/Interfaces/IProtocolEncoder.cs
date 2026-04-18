namespace BlazeCannon.Core.Interfaces;

public interface IProtocolEncoder
{
    string EncodeDispatchBrowserEvent(ulong eventHandlerId, string eventType, string eventArgsJson);
    string EncodeOnLocationChanged(string uri, string? state, bool intercepted);
    string EncodeOnRenderCompleted(long batchId, string? errorMessageOrNull);
    string EncodeBeginInvokeDotNet(string callId, string assemblyName, string methodIdentifier, long dotNetObjectId, string argsJson);
    string EncodeHandshake(string protocol = "json", int version = 1);
}
