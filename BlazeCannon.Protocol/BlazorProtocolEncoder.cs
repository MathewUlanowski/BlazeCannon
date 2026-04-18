using System.Text.Json;
using BlazeCannon.Core.Interfaces;

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
}
