namespace BlazeCannon.Core.Interfaces;
using BlazeCannon.Core.Models;

public interface IProtocolDecoder
{
    IEnumerable<BlazorMessage> DecodeMessages(string rawData, MessageDirection direction);
    BlazorMessage DecodeSingleMessage(string rawMessage, MessageDirection direction);
    IEnumerable<BlazorMessage> DecodeMessagePackMessages(byte[] data, MessageDirection direction);
}
