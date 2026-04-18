using System.Text.Json;

namespace BlazeCannon.Protocol;

public static class BlazorEventFactory
{
    private static readonly BlazorProtocolEncoder Encoder = new();

    public static string CreateChangeEvent(ulong eventHandlerId, string fieldValue)
    {
        var eventArgs = JsonSerializer.Serialize(new
        {
            type = "change",
            value = fieldValue
        });
        return Encoder.EncodeDispatchBrowserEvent(eventHandlerId, "change", eventArgs);
    }

    public static string CreateInputEvent(ulong eventHandlerId, string fieldValue)
    {
        var eventArgs = JsonSerializer.Serialize(new
        {
            type = "input",
            value = fieldValue
        });
        return Encoder.EncodeDispatchBrowserEvent(eventHandlerId, "input", eventArgs);
    }

    public static string CreateClickEvent(ulong eventHandlerId)
    {
        var eventArgs = JsonSerializer.Serialize(new
        {
            type = "click",
            detail = 1,
            screenX = 0,
            screenY = 0,
            clientX = 0,
            clientY = 0,
            offsetX = 0,
            offsetY = 0,
            pageX = 0,
            pageY = 0,
            button = 0,
            buttons = 0,
            ctrlKey = false,
            shiftKey = false,
            altKey = false,
            metaKey = false
        });
        return Encoder.EncodeDispatchBrowserEvent(eventHandlerId, "click", eventArgs);
    }

    public static string CreateSubmitEvent(ulong eventHandlerId)
    {
        var eventArgs = JsonSerializer.Serialize(new { type = "submit" });
        return Encoder.EncodeDispatchBrowserEvent(eventHandlerId, "submit", eventArgs);
    }

    public static string CreateFocusEvent(ulong eventHandlerId)
    {
        var eventArgs = JsonSerializer.Serialize(new { type = "focus" });
        return Encoder.EncodeDispatchBrowserEvent(eventHandlerId, "focusin", eventArgs);
    }

    public static string CreateBlurEvent(ulong eventHandlerId)
    {
        var eventArgs = JsonSerializer.Serialize(new { type = "blur" });
        return Encoder.EncodeDispatchBrowserEvent(eventHandlerId, "focusout", eventArgs);
    }

    public static string CreateKeydownEvent(ulong eventHandlerId, string key, string code = "")
    {
        var eventArgs = JsonSerializer.Serialize(new
        {
            type = "keydown",
            key,
            code = string.IsNullOrEmpty(code) ? $"Key{key.ToUpper()}" : code,
            location = 0,
            repeat = false,
            ctrlKey = false,
            shiftKey = false,
            altKey = false,
            metaKey = false
        });
        return Encoder.EncodeDispatchBrowserEvent(eventHandlerId, "keydown", eventArgs);
    }

    /// <summary>
    /// Simulates a realistic field input sequence: focus -> input -> change -> blur
    /// </summary>
    public static IEnumerable<string> CreateFieldInputSequence(ulong eventHandlerId, string value)
    {
        yield return CreateFocusEvent(eventHandlerId);
        yield return CreateInputEvent(eventHandlerId, value);
        yield return CreateChangeEvent(eventHandlerId, value);
        yield return CreateBlurEvent(eventHandlerId);
    }
}
