using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Butthurt.VRChat.Services;

/// <summary>A single OSC message: an address like "/avatar/change" and its arguments.</summary>
public record OscMessage(string Address, IReadOnlyList<object> Arguments)
{
    public object? First => Arguments.Count > 0 ? Arguments[0] : null;
}

/// <summary>
/// Just enough of the OSC 1.0 wire format for VRChat: strings, floats, ints and booleans,
/// plus reading the bundles VRChat sometimes sends.
/// </summary>
public static class Osc
{
    public static byte[] Encode(string address, params object[] arguments)
    {
        using var stream = new MemoryStream();
        WriteString(stream, address);

        var tags = new StringBuilder(",");
        foreach (var argument in arguments)
        {
            tags.Append(argument switch
            {
                string => 's',
                float or double => 'f',
                int => 'i',
                bool b => b ? 'T' : 'F',
                _ => throw new ArgumentException($"OSC can't send {argument.GetType().Name}")
            });
        }

        WriteString(stream, tags.ToString());

        Span<byte> buffer = stackalloc byte[4];
        foreach (var argument in arguments)
        {
            switch (argument)
            {
                case string text:
                    WriteString(stream, text);
                    break;
                case float or double:
                    BinaryPrimitives.WriteSingleBigEndian(buffer, Convert.ToSingle(argument));
                    stream.Write(buffer);
                    break;
                case int number:
                    BinaryPrimitives.WriteInt32BigEndian(buffer, number);
                    stream.Write(buffer);
                    break;
            }
        }

        return stream.ToArray();
    }

    /// <summary>Every message in a packet (one, or several when it's a bundle). Garbage is ignored.</summary>
    public static IEnumerable<OscMessage> Decode(byte[] packet)
    {
        var messages = new List<OscMessage>();
        try
        {
            Decode(packet, 0, packet.Length, messages);
        }
        catch
        {
            // Malformed packet, whatever made it through so far is still useful.
        }

        return messages;
    }

    private static void Decode(byte[] data, int start, int length, List<OscMessage> messages)
    {
        var position = start;
        var end = start + length;

        if (ReadString(data, ref position, end) is not { } address) return;

        if (address == "#bundle")
        {
            position += 8; // time tag
            while (position + 4 <= end)
            {
                var size = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(position, 4));
                position += 4;
                if (size <= 0 || position + size > end) return;
                Decode(data, position, size, messages);
                position += size;
            }

            return;
        }

        var tags = ReadString(data, ref position, end) ?? ",";
        var arguments = new List<object>();
        foreach (var tag in tags.AsSpan(1))
        {
            switch (tag)
            {
                case 's':
                    arguments.Add(ReadString(data, ref position, end) ?? "");
                    break;
                case 'f':
                    arguments.Add(BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(position, 4)));
                    position += 4;
                    break;
                case 'i':
                    arguments.Add(BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(position, 4)));
                    position += 4;
                    break;
                case 'T':
                    arguments.Add(true);
                    break;
                case 'F':
                    arguments.Add(false);
                    break;
                default:
                    // Unknown type, we can't know how long it is, so stop here.
                    messages.Add(new OscMessage(address, arguments));
                    return;
            }
        }

        messages.Add(new OscMessage(address, arguments));
    }

    private static void WriteString(Stream stream, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes);

        // Null terminated and padded to a multiple of 4 bytes.
        var padding = 4 - bytes.Length % 4;
        for (var i = 0; i < padding; i++) stream.WriteByte(0);
    }

    private static string? ReadString(byte[] data, ref int position, int end)
    {
        var zero = Array.IndexOf(data, (byte)0, position, end - position);
        if (zero < 0) return null;

        var text = Encoding.UTF8.GetString(data, position, zero - position);
        position = (zero / 4 + 1) * 4;
        return text;
    }
}
